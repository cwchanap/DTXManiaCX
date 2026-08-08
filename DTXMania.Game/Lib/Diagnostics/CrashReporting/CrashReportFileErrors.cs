#nullable enable

using System;
using System.IO;
using System.Security;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

/// <summary>
/// Shared filesystem-exception classification for the crash-reporting subsystem. Both
/// <see cref="CrashReportStore"/> and <see cref="CrashReportSummaryReader"/> absorb the same
/// set of expected I/O/access exceptions; centralizing the predicate keeps the two call sites
/// in lockstep.
/// </summary>
internal static class CrashReportFileErrors
{
    /// <summary>
    /// True for the exception kinds the crash-report store/reader treat as expected filesystem
    /// failures (absorbed and mapped to stable codes) rather than propagating.
    /// </summary>
    internal static bool IsExpectedFileSystemException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or SecurityException;
    }
}
