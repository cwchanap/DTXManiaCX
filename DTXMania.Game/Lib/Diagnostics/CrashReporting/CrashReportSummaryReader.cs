#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

internal sealed class CrashReportSummaryReader
{
    internal const int MaximumHeaderLines = 32;
    internal const int MaximumHeaderCharacters = 16 * 1024;
    internal const int MaximumFieldValueLength = 256;
    internal const string UnknownValue = "Unknown";

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    internal CrashReportSummary Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fileName = Path.GetFileName(path);
        var logicalId = GetLogicalReportId(fileName);
        var fields = ReadHeaderFields(path);

        var capturedAtUtc = TryParseCapturedAt(fields, out var headerTimestamp)
            ? headerTimestamp
            : TryParseTimestampFromId(logicalId, out var filenameTimestamp)
                ? filenameTimestamp
                : DateTimeOffset.FromUnixTimeSeconds(0);

        return new CrashReportSummary(
            logicalId,
            capturedAtUtc,
            NormalizeField(fields, nameof(CrashReportSummary.BuildId)),
            NormalizeField(fields, nameof(CrashReportSummary.OperatingSystem)),
            NormalizeField(fields, nameof(CrashReportSummary.ProcessArchitecture)),
            NormalizeField(fields, nameof(CrashReportSummary.StageOrMilestone)),
            NormalizeField(fields, nameof(CrashReportSummary.ExceptionType)),
            fileName);
    }

    internal static string GetLogicalReportId(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (name.EndsWith(".ack.txt", StringComparison.OrdinalIgnoreCase))
        {
            return name[..^".ack.txt".Length];
        }

        if (name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return name[..^".txt".Length];
        }

        return Path.GetFileNameWithoutExtension(name);
    }

    private static Dictionary<string, string> ReadHeaderFields(string path)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: 1024);
            using var reader = new StreamReader(
                stream,
                Utf8WithoutBom,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 1024,
                leaveOpen: false);

            var lineBuilder = new StringBuilder();
            var lineCount = 0;
            var totalChars = 0;
            var headerValidated = false;

            while (totalChars < MaximumHeaderCharacters)
            {
                var next = reader.Read();
                if (next < 0)
                {
                    break;
                }

                totalChars++;

                if (next == '\n')
                {
                    var line = lineBuilder.ToString();
                    lineBuilder.Clear();
                    if (line.Length > 0 && line[line.Length - 1] == '\r')
                    {
                        line = line[..^1];
                    }

                    lineCount++;
                    if (lineCount > MaximumHeaderLines)
                    {
                        break;
                    }

                    if (!headerValidated)
                    {
                        if (line != CrashReportTextWriter.Header)
                        {
                            return new Dictionary<string, string>(StringComparer.Ordinal);
                        }

                        headerValidated = true;
                        continue;
                    }

                    if (line == CrashReportTextWriter.ExceptionSection)
                    {
                        break;
                    }

                    if (line.Length == 0)
                    {
                        continue;
                    }

                    ParseField(line, fields);
                }
                else
                {
                    lineBuilder.Append((char)next);
                }
            }
        }
        catch (Exception exception) when (CrashReportFileErrors.IsExpectedFileSystemException(exception))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return fields;
    }

    private static void ParseField(string line, Dictionary<string, string> fields)
    {
        var colon = line.IndexOf(':');
        if (colon <= 0)
        {
            return;
        }

        var key = line[..colon];
        var value = line[(colon + 1)..];
        if (value.Length > 0 && value[0] == ' ')
        {
            value = value[1..];
        }

        fields[key] = value;
    }

    private static string NormalizeField(Dictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out var value) || string.IsNullOrEmpty(value))
        {
            return UnknownValue;
        }

        var length = Math.Min(value.Length, MaximumFieldValueLength);
        var chars = new char[length];
        for (var index = 0; index < length; index++)
        {
            var character = value[index];
            chars[index] = character < 0x20 || character == 0x7F ? ' ' : character;
        }

        var normalized = new string(chars).Trim();
        return normalized.Length == 0 ? UnknownValue : normalized;
    }

    private static bool TryParseCapturedAt(Dictionary<string, string> fields, out DateTimeOffset value)
    {
        if (fields.TryGetValue(nameof(CrashReportSummary.CapturedAtUtc), out var text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
        {
            value = value.ToUniversalTime();
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryParseTimestampFromId(string reportId, out DateTimeOffset value)
    {
        value = default;
        // The retained-name policy is matched case-insensitively, so the corrupt-header
        // fallback must accept the same casing it accepts: normalize the id before looking
        // for the lowercase "crash-" prefix and lowercase "z" UTC designator.
        var normalized = reportId.ToLowerInvariant();
        const string prefix = "crash-";
        if (!normalized.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = normalized[prefix.Length..];
        var zIndex = rest.IndexOf('z');
        if (zIndex != 15 || rest.Length <= zIndex)
        {
            return false;
        }

        var stamp = rest[..zIndex];
        if (stamp.Length != 15 || stamp[8] != '-')
        {
            return false;
        }

        if (!DateTimeOffset.TryParseExact(
                stamp[..8] + stamp[9..],
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out value))
        {
            return false;
        }

        value = value.ToUniversalTime();
        return true;
    }

}
