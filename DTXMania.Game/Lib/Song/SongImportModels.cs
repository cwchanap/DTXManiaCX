using System;
using System.Collections.Generic;
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
        TimeSpan CleanupDuration);

    public sealed record SongEnumerationResult(
        SongEnumerationBatch Batch,
        SongBulkImportResult Import,
        TimeSpan HierarchyDuration);
}
