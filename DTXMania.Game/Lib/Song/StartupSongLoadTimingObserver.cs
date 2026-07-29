#nullable enable

namespace DTXMania.Game.Lib.Song;

internal enum StartupDatabaseTimingSpan
{
    ServiceSetup,
    CorruptionProbe,
    InvalidRecovery,
    EnsureCreated,
    EncodingPragmas,
    VersionWork,
    SchemaEnsures
}

internal enum StartupOperationOutcome
{
    Success,
    Failure,
    Cancellation
}

internal interface IStartupSongLoadTimingObserver
{
    void BeginDatabaseSpan(StartupDatabaseTimingSpan span);
    void EndDatabaseSpan(StartupDatabaseTimingSpan span);
    void RecordUnexpectedTableExistsPath();
    void RecordEnumerationTerminal(
        SongEnumerationResult? result,
        StartupOperationOutcome outcome);
}

internal static class StartupSongLoadTimingObserverExtensions
{
    internal static void TryBeginDatabaseSpan(
        this IStartupSongLoadTimingObserver? observer,
        StartupDatabaseTimingSpan span)
    {
        try
        {
            observer?.BeginDatabaseSpan(span);
        }
        catch
        {
        }
    }

    internal static void TryEndDatabaseSpan(
        this IStartupSongLoadTimingObserver? observer,
        StartupDatabaseTimingSpan span)
    {
        try
        {
            observer?.EndDatabaseSpan(span);
        }
        catch
        {
        }
    }

    internal static void TryRecordUnexpectedTableExistsPath(
        this IStartupSongLoadTimingObserver? observer)
    {
        try
        {
            observer?.RecordUnexpectedTableExistsPath();
        }
        catch
        {
        }
    }

    internal static void TryRecordEnumerationTerminal(
        this IStartupSongLoadTimingObserver? observer,
        SongEnumerationResult? result,
        StartupOperationOutcome outcome)
    {
        try
        {
            observer?.RecordEnumerationTerminal(result, outcome);
        }
        catch
        {
        }
    }
}
