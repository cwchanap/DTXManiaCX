#nullable enable
using System;
using System.Linq;

namespace DTXMania.Game.Lib.Stage
{
    internal enum StartupSongLoadPath
    {
        Unknown,
        Cache,
        Enumeration
    }

    internal enum StartupSongLoadOutcome
    {
        Success,
        Cancellation,
        Failure
    }

    internal sealed record StartupSongLoadSummary(
        StartupSongLoadPath path,
        StartupSongLoadOutcome outcome,
        TimeSpan total,
        TimeSpan databaseInitialization,
        TimeSpan discoveryAndParsing,
        TimeSpan persistence,
        TimeSpan cleanup,
        TimeSpan hierarchy,
        int discoveredCharts,
        int parsedCharts,
        int logicalGroups,
        int added,
        int updated,
        int preserved,
        int skipped,
        int conflicts,
        int stale,
        string? error)
    {
        public string Format()
        {
            static long Ms(TimeSpan value) => (long)Math.Round(value.TotalMilliseconds);
            static string Token(string value) =>
                string.Concat(value.Select(character =>
                    char.IsLetterOrDigit(character) || character is '.' or '-'
                        ? character
                        : '_'));

            return FormattableString.Invariant(
                $"HPA192_STARTUP path={path.ToString().ToLowerInvariant()} outcome={outcome.ToString().ToLowerInvariant()} total_ms={Ms(total)} db_init_ms={Ms(databaseInitialization)} discovery_parse_ms={Ms(discoveryAndParsing)} persistence_ms={Ms(persistence)} cleanup_ms={Ms(cleanup)} hierarchy_ms={Ms(hierarchy)} discovered={discoveredCharts} parsed={parsedCharts} groups={logicalGroups} added={added} updated={updated} preserved={preserved} skipped={skipped} conflicts={conflicts} stale={stale} error={(string.IsNullOrWhiteSpace(error) ? "none" : Token(error))}");
        }

        public static StartupSongLoadSummary Failed(
            StartupSongLoadPath path,
            TimeSpan total,
            string error) =>
            new(path, StartupSongLoadOutcome.Failure, total,
                TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
                TimeSpan.Zero, 0, 0, 0, 0, 0, 0, 0, 0, 0, error);
    }
}
