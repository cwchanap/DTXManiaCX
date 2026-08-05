#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

/// <summary>
/// Trims crash report text to a sane size and replaces the account name in filesystem
/// paths so a report can be pasted into a bug report as-is.
///
/// Deliberately minimal: reports are written to the local machine and are never uploaded
/// automatically, so exception messages and stack traces are preserved verbatim. They are
/// the whole point of the report.
/// </summary>
internal sealed class CrashReportSanitizer
{
    internal const string RedactedValue = "[REDACTED]";

    private const int MaximumStackTraceLength = 16 * 1024;
    private const int MaximumExceptionChainLength = 32 * 1024;
    private const int MaximumMetadataLength = 256;
    private const int MaximumInnerExceptions = 8;

    private static readonly Regex HomeSegmentRegex = new(
        """(?<![A-Za-z0-9_])(?:Users|home)[\\/][^\\/\s:;,\]\)}]+""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    private readonly IReadOnlyList<string> _sensitivePathPrefixes;

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
        }
    }

    /// <summary>
    /// Replaces known roots with [PATH] and the home-directory account name with [USER].
    /// </summary>
    internal string Scrub(string? value, int maximumLength = MaximumStackTraceLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        try
        {
            var scrubbed = value;
            foreach (var prefix in _sensitivePathPrefixes)
            {
                scrubbed = scrubbed.Replace(prefix, "[PATH]", StringComparison.OrdinalIgnoreCase);
            }

            scrubbed = HomeSegmentRegex.Replace(scrubbed, "[USER]");
            return Limit(scrubbed, maximumLength);
        }
        catch (RegexMatchTimeoutException)
        {
            return RedactedValue;
        }
    }

    /// <summary>
    /// Scrubs a single-line metadata value, rejecting embedded newlines so they cannot
    /// break the line-oriented report format.
    /// </summary>
    internal string SanitizeMetadata(string? value, string fallback = "Unknown")
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            return fallback;
        }

        return Scrub(value, MaximumMetadataLength);
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
                .AppendLine(current.GetType().FullName ?? current.GetType().Name);
            builder.Append("Message: ")
                .AppendLine(Scrub(current.Message, MaximumMetadataLength));

            var stackTrace = Scrub(current.StackTrace);
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
            var trimmedPath = normalizedPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            prefixes.Add(trimmedPath.Length == 0 ? normalizedPath : trimmedPath);
        }

        // Longest first so a nested root is replaced before its parent.
        return prefixes
            .OrderByDescending(static path => path.Length)
            .ToArray();
    }

    private static string Limit(string value, int maximumLength)
    {
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }
}
