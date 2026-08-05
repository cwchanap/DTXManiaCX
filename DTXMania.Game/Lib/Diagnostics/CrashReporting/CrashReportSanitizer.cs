#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

internal sealed class CrashReportSanitizer
{
    internal const string RedactedValue = "[REDACTED]";
    internal const string ExceptionMessageOmitted = "[EXCEPTION MESSAGE OMITTED]";

    private const int MaximumStackTraceLength = 16 * 1024;
    private const int MaximumExceptionChainLength = 32 * 1024;
    private const int MaximumMetadataLength = 256;
    private const int MaximumInnerExceptions = 8;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex UriUserInfoRegex = new(
        """(?<prefix>\b(?:https?|ftp)://)[^/\s@?#]+@""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);
    private static readonly Regex UriQueryRegex = new(
        """(?<uri>\b(?:https?|ftp)://[^\s\]\)\}]+?)(?:\?[^\s\]\)}]*)""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);
    private static readonly Regex ApiKeyValueRegex = new(
        """(?<prefix>\b(?:api[_-]?key|access[_-]?token|token|authorization|password|secret)\b\s*(?:=|:)\s*)(?<value>[^\s,;]+(?:\s+[^\s,;]+)?)""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);
    private static readonly Regex SourceFrameRegex = new(
        """(?m)(?<prefix>\s+in\s+)(?:[A-Za-z]:[\\/][^\r\n:]+|\\\\[^\r\n:]+|/[^\r\n:]+)(?::line\s+(?<line>\d+))""",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex WindowsAbsolutePathRegex = new(
        """(?<![A-Za-z0-9_])[A-Za-z]:[\\/][^\r\n:;,\]\)}<>|?*]+""",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex WindowsUncPathRegex = new(
        """(?<!\\)\\\\[^\r\n:;,\]\)}]+""",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex UnixAbsolutePathRegex = new(
        """(?<![A-Za-z0-9_:/])/[^\r\n:;,\]\)}]+""",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex HomeSegmentRegex = new(
        """(?<![A-Za-z0-9_])(?:Users|home)[\\/][^\\/\s:;,\]\)}]+""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);
    private static readonly Regex RegisteredPathTailRegex = new(
        """\[PATH\](?:[\\/][^\s:;,\]\)}]+)*""",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex TypeNameRegex = new(
        """\A[A-Za-z_][A-Za-z0-9_.+\x60]*\z""",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex StableLabelRegex = new(
        """\A[A-Za-z0-9_.+\-]{1,128}\z""",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    private readonly IReadOnlyList<string> _sensitivePathPrefixes;
    private readonly bool _initializationFailed;

    internal CrashReportSanitizer(IReadOnlyList<string>? sensitivePaths)
    {
        try
        {
            _sensitivePathPrefixes = NormalizeSensitivePaths(sensitivePaths);
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or NotSupportedException
                                          or PathTooLongException
                                          or SecurityException)
        {
            _sensitivePathPrefixes = Array.Empty<string>();
            _initializationFailed = true;
        }
    }

    internal string SanitizeExceptionMessage(string? message)
    {
#if DEBUG
        if (string.Equals(
                message,
                DebugCrashInjection.ControlledCrashMessage,
                StringComparison.Ordinal))
        {
            return DebugCrashInjection.ControlledCrashMessage;
        }
#endif

        return ExceptionMessageOmitted;
    }

    internal string SanitizeStackTrace(string? stackTrace)
    {
        if (stackTrace is null)
        {
            return string.Empty;
        }

        if (_initializationFailed)
        {
            return RedactedValue;
        }

        try
        {
            var sanitized = UriUserInfoRegex.Replace(
                stackTrace,
                "$" + "{prefix}" + RedactedValue + "@");
            sanitized = UriQueryRegex.Replace(sanitized, "$" + "{uri}");
            sanitized = ApiKeyValueRegex.Replace(sanitized, "$" + "{prefix}" + RedactedValue);
            sanitized = SourceFrameRegex.Replace(
                sanitized,
                static match => match.Groups["prefix"].Value
                    + "[SOURCE]"
                    + (match.Groups["line"].Success ? ":line " + match.Groups["line"].Value : string.Empty));

            foreach (var prefix in _sensitivePathPrefixes)
            {
                sanitized = sanitized.Replace(prefix, "[PATH]", StringComparison.OrdinalIgnoreCase);
            }

            sanitized = RegisteredPathTailRegex.Replace(sanitized, "[PATH]");
            sanitized = WindowsAbsolutePathRegex.Replace(sanitized, "[PATH]");
            sanitized = WindowsUncPathRegex.Replace(sanitized, "[PATH]");
            sanitized = UnixAbsolutePathRegex.Replace(sanitized, "[PATH]");
            sanitized = HomeSegmentRegex.Replace(sanitized, "[USER]");
            return Limit(sanitized, MaximumStackTraceLength);
        }
        catch (RegexMatchTimeoutException)
        {
            return RedactedValue;
        }
        catch (ArgumentException)
        {
            return RedactedValue;
        }
    }

    internal string SanitizeExceptionChain(Exception exception)
    {
        return SanitizeExceptionChain(exception, out _);
    }

    internal string SanitizeExceptionChain(Exception exception, out bool truncated)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var builder = new StringBuilder();
        var current = exception;
        var depth = 0;
        truncated = false;

        while (current is not null && depth < MaximumInnerExceptions)
        {
            if (depth > 0)
            {
                builder.AppendLine("InnerException:");
            }

            builder.Append("ExceptionType: ")
                .AppendLine(SanitizeExceptionType(current.GetType()));
            builder.Append("Message: ")
                .AppendLine(SanitizeExceptionMessage(current.Message));

            var stackTrace = SanitizeStackTrace(current.StackTrace);
            if (!string.IsNullOrEmpty(stackTrace))
            {
                builder.AppendLine("StackTrace:");
                builder.AppendLine(stackTrace);
            }

            current = current.InnerException;
            depth++;
        }

        if (current is not null)
        {
            builder.AppendLine("InnerException: [TRUNCATED]");
            truncated = true;
        }

        var result = Limit(builder.ToString(), MaximumExceptionChainLength);
        if (result.Length < builder.Length)
        {
            truncated = true;
        }

        return result;
    }

    internal string SanitizeExceptionType(Type exceptionType)
    {
        ArgumentNullException.ThrowIfNull(exceptionType);

        return SanitizeTypeName(exceptionType.FullName ?? exceptionType.Name);
    }

    internal string SanitizeTypeName(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return "Unknown";
        }

        try
        {
            return typeName.Length <= MaximumMetadataLength && TypeNameRegex.IsMatch(typeName)
                ? typeName
                : RedactedValue;
        }
        catch (RegexMatchTimeoutException)
        {
            return RedactedValue;
        }
    }

    internal string SanitizeMetadata(string? value, string fallback = "Unknown")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (value.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            return fallback;
        }

        var sanitized = SanitizeStackTrace(value);
        return sanitized == RedactedValue ? RedactedValue : Limit(sanitized, MaximumMetadataLength);
    }

    internal string SanitizeStableLabel(string? value, string fallback = "Unknown")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        try
        {
            return StableLabelRegex.IsMatch(value) ? value : fallback;
        }
        catch (RegexMatchTimeoutException)
        {
            return RedactedValue;
        }
    }

    private static IReadOnlyList<string> NormalizeSensitivePaths(IReadOnlyList<string>? sensitivePaths)
    {
        if (sensitivePaths is null || sensitivePaths.Count == 0)
        {
            return Array.Empty<string>();
        }

        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in sensitivePaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var normalizedPath = Path.GetFullPath(path);
            var trimmedPath = normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            prefixes.Add(trimmedPath.Length == 0 ? normalizedPath : trimmedPath);
        }

        return prefixes
            .OrderByDescending(static path => path.Length)
            .ToArray();
    }

    private static string Limit(string value, int maximumLength)
    {
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }
}
