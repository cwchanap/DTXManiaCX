using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Game.Lib.Song
{
    public sealed record SongImportCandidate(
        global::DTXMania.Game.Lib.Song.Entities.Song ParsedSong,
        SongChart ParsedChart,
        string NormalizedChartPath,
        string GroupKey,
        int GroupOrder);

    internal sealed record SongBulkImportRequest(
        IReadOnlyList<string> ActiveRoots,
        IReadOnlySet<string> DiscoveredChartPaths,
        IReadOnlyList<SongImportCandidate> Candidates);

    public sealed record PendingSongNode(
        string GroupKey,
        SongListNode Placeholder,
        IReadOnlyList<string> OrderedChartPaths);

    public sealed record SongEnumerationError(
        string Path,
        string Message,
        bool IsRootFailure);

    internal enum SongBulkImportMilestone
    {
        PreloadStarted,
        MatchingCompleted,
        MutationsStaged,
        CleanupCompleted,
        SaveStarted,
        Committed
    }

    internal sealed record SongBulkImportProgress(
        SongBulkImportMilestone Milestone,
        int Processed,
        int Total);

    public sealed class SongEnumerationBatch
    {
        public required IReadOnlyList<string> ActiveRoots { get; init; }
        public required HashSet<string> DiscoveredChartPaths { get; init; }
        public required List<SongImportCandidate> Candidates { get; init; }
        public required List<SongListNode> RootNodes { get; init; }
        public required List<PendingSongNode> PendingSongs { get; init; }
        public required List<SongEnumerationError> Errors { get; init; }
        public required TimeSpan DiscoveryAndParsingDuration { get; init; }
        public bool IsComplete { get; init; }
    }

    /// <summary>
    /// Result of a bulk song import operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>PersistenceDuration</c> measures the entire import operation from
    /// the start of preload (chart identity query) through SaveChanges and
    /// transaction commit — it spans pre-save processing (matching, mutation
    /// staging, stale-removal) as well as SaveChanges/transaction persistence.
    /// </para>
    /// <para>
    /// <c>CleanupDuration</c> measures stale-removal staging only —
    /// identifying and removing stale charts and empty songs from the change
    /// tracker before SaveChanges. It does not include SaveChanges or
    /// transaction commit.
    /// </para>
    /// </remarks>
    public sealed record SongBulkImportResult(
        IReadOnlyDictionary<string, SongChart> ChartsByPath,
        int Added,
        int Updated,
        int Preserved,
        int Skipped,
        int Conflicts,
        int StaleCharts,
        int StaleSongs,
        TimeSpan PersistenceDuration,
        TimeSpan CleanupDuration)
    {
        private static readonly IReadOnlyDictionary<string, SongChart> EmptyCharts =
            new ReadOnlyDictionary<string, SongChart>(
                new Dictionary<string, SongChart>(StringComparer.Ordinal));

        public static SongBulkImportResult Empty { get; } = new(
            EmptyCharts,
            Added: 0,
            Updated: 0,
            Preserved: 0,
            Skipped: 0,
            Conflicts: 0,
            StaleCharts: 0,
            StaleSongs: 0,
            PersistenceDuration: TimeSpan.Zero,
            CleanupDuration: TimeSpan.Zero);
    }

    public enum SongEnumerationOutcome
    {
        ImportedAndPublished,
        NoActiveRoots,
    }

    public sealed record SongEnumerationResult(
        SongEnumerationOutcome Outcome,
        SongEnumerationBatch Batch,
        SongBulkImportResult Import,
        TimeSpan HierarchyDuration)
    {
        public SongEnumerationResult(
            SongEnumerationBatch batch,
            SongBulkImportResult import,
            TimeSpan hierarchyDuration)
            : this(
                SongEnumerationOutcome.ImportedAndPublished,
                batch,
                import,
                hierarchyDuration)
        {
        }
    }
}
