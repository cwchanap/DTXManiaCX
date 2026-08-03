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

    /// <summary>
    /// One filesystem entry (chart file, set.def, or directory) collected by the
    /// shared chart-inventory scanner, with the timestamps the cache-freshness
    /// change check compares against the last successful enumeration time.
    /// </summary>
    internal sealed record ChartInventoryEntry(
        string Path,
        DateTime LastWriteTime,
        DateTime CreationTime);

    /// <summary>
    /// The result of a single filesystem walk over the active song roots that
    /// mirrors full enumeration's directory and set.def discovery rules. Used by
    /// both the chart-file count and the mtime change check so they agree with
    /// what full enumeration would import (set.def-referenced charts only, no
    /// unreferenced/backup charts; case-insensitive extension matching) and so
    /// the active roots are walked once instead of once per extension per pass.
    /// <para>
    /// <see cref="Charts"/> holds exactly the chart files full enumeration would
    /// import (deduplicated by normalized path so overlapping roots or a set.def
    /// referencing the same file from two difficulties cannot double-count).
    /// <see cref="SetDefinitions"/> holds every <c>set.def</c> discovered (its
    /// modification signals a rescan even when referenced charts are unchanged).
    /// <see cref="Directories"/> holds every directory visited for the directory
    /// mtime change signal.
    /// </para>
    /// </summary>
    internal sealed class ChartInventory
    {
        public List<ChartInventoryEntry> Charts { get; } = new();
        public List<ChartInventoryEntry> SetDefinitions { get; } = new();
        public List<ChartInventoryEntry> Directories { get; } = new();
    }

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

    /// <summary>
    /// Identifies the live-library phase that failed after the database import
    /// had already committed. Callers must not present this as a rollback.
    /// </summary>
    public enum SongEnumerationPostCommitPhase
    {
        Finalization,
        Publication,
    }

    /// <summary>
    /// Signals an HPA-192 failure after the import transaction committed but
    /// before its in-memory library publication completed. The captured batch
    /// lets callers retain accurate root-failure diagnostics while reporting
    /// the operation as partial success requiring recovery/restart.
    /// </summary>
    public sealed class SongEnumerationPostCommitException : Exception
    {
        internal SongEnumerationPostCommitException(
            SongEnumerationPostCommitPhase phase,
            SongEnumerationBatch batch,
            SongBulkImportResult import,
            Exception innerException)
            : base($"Song enumeration {phase.ToString().ToLowerInvariant()} failed after database commit.",
                innerException)
        {
            Phase = phase;
            Batch = batch ?? throw new ArgumentNullException(nameof(batch));
            Import = import ?? throw new ArgumentNullException(nameof(import));
        }

        public SongEnumerationPostCommitPhase Phase { get; }
        public SongEnumerationBatch Batch { get; }
        public SongBulkImportResult Import { get; }
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
