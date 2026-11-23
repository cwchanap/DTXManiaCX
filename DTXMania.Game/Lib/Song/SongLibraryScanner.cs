#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Song.Import;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using SongScoreEntity = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// Stateless filesystem traversal, chart inventory and cache-freshness
    /// checking for the song library (HPA-801 Decision B/D). Builds one
    /// <see cref="SongEnumerationBatch"/> per scan and answers
    /// <see cref="NeedsEnumerationAsync"/>; it owns no locks, no cancellation
    /// sources, no publication and no score state — those stay with
    /// <see cref="SongLibrary"/>, the sole scan-transaction owner. All mutable
    /// file I/O (chart parsing, file and directory enumeration) arrives through
    /// constructor delegates, and SET.def/box.def reading uses the same
    /// library-owned <see cref="SongDefinitionReader"/> instance as the
    /// hierarchy builder.
    /// </summary>
    internal sealed class SongLibraryScanner
    {
        private readonly SongDatabase _database;
        private readonly SongCatalogStore _catalog;
        private readonly SongHierarchyBuilder _hierarchy;
        private readonly SongRootPolicy _rootPolicy;
        private readonly SongDefinitionReader _definitions;
        private readonly ILogger _logger;

        // Constructor seams (HPA-801 Decision E): immutable per instance so
        // tests inject collaborators exactly once, at construction.
        private readonly Func<string, Task<(SongEntity song, SongChart chart)>>
            _parseSongEntitiesAsync;

        private readonly Func<string, IEnumerable<string>> _enumerateFiles;

        private readonly Func<string, IEnumerable<string>> _enumerateDirectories;

        /// <summary>
        /// Creates the scanner. <paramref name="definitions"/> is the library's
        /// one shared definition reader (HPA-801 Decision C), so the cold scan
        /// and the warm hierarchy builder follow the same ReadAllLines seam and
        /// encoding-fallback rules.
        /// </summary>
        public SongLibraryScanner(
            SongDatabase database,
            SongCatalogStore catalog,
            SongHierarchyBuilder hierarchy,
            SongRootPolicy rootPolicy,
            SongDefinitionReader definitions,
            ILogger? logger = null,
            Func<string, Task<(SongEntity song, SongChart chart)>>? parseSongEntitiesAsync = null,
            Func<string, IEnumerable<string>>? enumerateFiles = null,
            Func<string, IEnumerable<string>>? enumerateDirectories = null)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
            _rootPolicy = rootPolicy ?? throw new ArgumentNullException(nameof(rootPolicy));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _logger = logger ?? NullLogger<SongLibraryScanner>.Instance;
            _parseSongEntitiesAsync = parseSongEntitiesAsync
                ?? DTXChartParser.ParseSongEntitiesAsync;
            _enumerateFiles = enumerateFiles ?? Directory.EnumerateFiles;
            _enumerateDirectories = enumerateDirectories ?? Directory.EnumerateDirectories;
        }

        #region Batch Building

        internal async Task<SongEnumerationBatch> BuildEnumerationBatchAsync(
            string[] searchPaths,
            IProgress<EnumerationProgress>? progress,
            CancellationToken cancellationToken)
        {
            var builder = CreateBatchBuilder(searchPaths);
            var rootHeaders = await _hierarchy.ResolveSongRootHeadersOrNullAsync(
                builder.ActiveRoots,
                cancellationToken).ConfigureAwait(false);

            foreach (var root in builder.ActiveRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rootBox = rootHeaders != null &&
                    rootHeaders.TryGetValue(root, out var header)
                    ? _hierarchy.CreateSongRootBoxNode(root, header)
                    : null;
                await EnumerateDirectoryIntoBatchAsync(
                    root,
                    rootBox,
                    builder,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                if (rootBox != null && rootBox.Children.Count > 0)
                    AddNodeToBatch(builder, parent: null, rootBox);
            }

            return builder.Complete();
        }

        private sealed class SongEnumerationBatchBuilder
        {
            public SongEnumerationBatchBuilder(
                IReadOnlyList<string> activeRoots,
                List<SongEnumerationError>? errors = null)
            {
                ActiveRoots = activeRoots;
                if (errors != null)
                    Errors.AddRange(errors);
            }

            public IReadOnlyList<string> ActiveRoots { get; }
            public HashSet<string> DiscoveredChartPaths { get; } =
                new(SongPathIdentity.CanonicalComparer);
            public List<SongImportCandidate> Candidates { get; } = new();
            public List<SongListNode> RootNodes { get; } = new();
            public List<PendingSongNode> PendingSongs { get; } = new();
            public List<SongEnumerationError> Errors { get; } = new();
            public HashSet<string> RejectedChartPaths { get; } =
                new(SongPathIdentity.CanonicalComparer);

            public SongEnumerationBatch Complete()
            {
                return new SongEnumerationBatch
                {
                    ActiveRoots = ActiveRoots,
                    DiscoveredChartPaths = DiscoveredChartPaths,
                    Candidates = Candidates,
                    RootNodes = RootNodes,
                    PendingSongs = PendingSongs,
                    Errors = Errors,
                    RejectedChartPaths = RejectedChartPaths,
                    IsComplete = true
                };
            }
        }

        private sealed class TemporaryPendingGroup
        {
            public TemporaryPendingGroup(
                string groupKey,
                SongListNode placeholder)
            {
                GroupKey = groupKey;
                Placeholder = placeholder;
            }

            public string GroupKey { get; }
            public SongListNode Placeholder { get; }
            public List<string> OrderedChartPaths { get; } = new();
        }

        private SongEnumerationBatchBuilder CreateBatchBuilder(
            IEnumerable<string> searchPaths)
        {
            ArgumentNullException.ThrowIfNull(searchPaths);
            var configuredRoots = searchPaths.ToArray();
            var validation = _rootPolicy.Validate(configuredRoots);
            var errors = new List<SongEnumerationError>();
            foreach (var diagnostic in validation.Diagnostics)
            {
                if (diagnostic.IsWarning)
                    continue;

                var error = new SongEnumerationError(
                    diagnostic.Path,
                    diagnostic.Message,
                    IsRootFailure: true);
                errors.Add(error);
            }

            var activeRoots = new List<string>();
            foreach (var normalizedRoot in validation.CanonicalRoots)
            {
                var probe = _rootPolicy.Probe(normalizedRoot);
                if (probe.Availability == SongRootAvailability.Available)
                {
                    activeRoots.Add(normalizedRoot);
                    continue;
                }

                var message = probe.Availability == SongRootAvailability.Missing
                    ? $"Configured song root does not exist: {normalizedRoot}"
                    : $"Cannot read configured song root ({probe.Reason}): {normalizedRoot}";
                var error = new SongEnumerationError(
                    normalizedRoot,
                    message,
                    IsRootFailure: true);
                errors.Add(error);
            }

            return new SongEnumerationBatchBuilder(activeRoots, errors);
        }

        private async Task EnumerateDirectoryIntoBatchAsync(
            string directoryPath,
            SongListNode? parent,
            SongEnumerationBatchBuilder builder,
            IProgress<EnumerationProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var setDefPath = Path.Combine(directoryPath, "set.def");
            if (File.Exists(setDefPath))
            {
                await ParseSetDefinitionIntoBatchAsync(
                    setDefPath,
                    parent,
                    builder,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var subdirectories = _enumerateDirectories(directoryPath)
                .Select(SongPathIdentity.Normalize)
                .OrderBy(path => path, SongPathIdentity.CanonicalComparer)
                .ToArray();
            foreach (var subdirectoryPath in subdirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = new DirectoryInfo(subdirectoryPath);
                var boxDefPath = Path.Combine(subdirectoryPath, "box.def");
                var isBox = directory.Name.StartsWith(
                    "DTXFiles.",
                    StringComparison.OrdinalIgnoreCase) ||
                    File.Exists(boxDefPath);
                if (!isBox)
                {
                    await EnumerateDirectoryIntoBatchAsync(
                        subdirectoryPath,
                        parent,
                        builder,
                        progress,
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                BoxDefinition? boxDefinition = null;
                if (File.Exists(boxDefPath))
                {
                    try
                    {
                        boxDefinition = await _definitions.ParseBoxDefinitionAsync(
                            boxDefPath,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        builder.Errors.Add(new SongEnumerationError(
                            SongPathIdentity.Normalize(boxDefPath),
                            exception.Message,
                            IsRootFailure: false));
                    }
                }

                var boxNode = _hierarchy.CreateBoxNodeFromDirectory(
                    directory,
                    parent,
                    boxDefinition);
                await EnumerateDirectoryIntoBatchAsync(
                    subdirectoryPath,
                    boxNode,
                    builder,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                if (boxNode.Children.Count > 0)
                    AddNodeToBatch(builder, parent, boxNode);
            }

            var groups = new Dictionary<string, TemporaryPendingGroup>(
                StringComparer.Ordinal);
            var files = _enumerateFiles(directoryPath)
                .Where(DTXChartParser.IsSupportedFile)
                .Select(SongPathIdentity.Normalize)
                .OrderBy(path => path, SongPathIdentity.CanonicalComparer)
                .ToArray();
            foreach (var normalizedPath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                builder.DiscoveredChartPaths.Add(normalizedPath);
                var fileName = Path.GetFileName(normalizedPath);
                try
                {
                    var (song, chart) = await _parseSongEntitiesAsync(
                        normalizedPath).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    chart.FilePath = normalizedPath;
                    var groupKey = SongPathIdentity.ForOrdinaryChart(
                        normalizedPath,
                        song.Title,
                        song.Artist);
                    if (!groups.TryGetValue(groupKey, out var group))
                    {
                        var placeholder = CreateTemporarySongNode(
                            song,
                            chart,
                            parent);
                        group = new TemporaryPendingGroup(
                            groupKey,
                            placeholder);
                        groups.Add(groupKey, group);
                        AddNodeToBatch(builder, parent, placeholder);
                    }

                    var groupOrder = group.OrderedChartPaths.Count;
                    group.OrderedChartPaths.Add(normalizedPath);
                    AddTemporaryScore(
                        group.Placeholder,
                        chart,
                        groupOrder,
                        authoredLabel: chart.DifficultyLabel);
                    builder.Candidates.Add(new SongImportCandidate(
                        song,
                        chart,
                        normalizedPath,
                        groupKey,
                        groupOrder));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (UnplayableTabException exception)
                {
                    RejectUnplayableTab(builder, normalizedPath, exception);
                }
                catch (Exception exception)
                {
                    builder.Errors.Add(new SongEnumerationError(
                        normalizedPath,
                        exception.Message,
                        IsRootFailure: false));
                }

                progress?.Report(new EnumerationProgress
                {
                    CurrentFile = fileName,
                    CurrentDirectory = directoryPath,
                    ProcessedCount = builder.DiscoveredChartPaths.Count,
                    DiscoveredSongs = builder.Candidates.Count
                });
            }

            foreach (var group in groups.Values)
            {
                builder.PendingSongs.Add(new PendingSongNode(
                    group.GroupKey,
                    group.Placeholder,
                    group.OrderedChartPaths));
            }
        }

        private async Task ParseSetDefinitionIntoBatchAsync(
            string setDefPath,
            SongListNode? parent,
            SongEnumerationBatchBuilder builder,
            IProgress<EnumerationProgress>? progress,
            CancellationToken cancellationToken)
        {
            string[] lines;
            try
            {
                lines = await _definitions.ReadAllLines(
                    setDefPath,
                    Encoding.UTF8,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                ProtectSupportedChartPathsInSubtree(
                    Path.GetDirectoryName(setDefPath) ?? "",
                    builder,
                    cancellationToken);
                builder.Errors.Add(new SongEnumerationError(
                    SongPathIdentity.Normalize(setDefPath),
                    exception.Message,
                    IsRootFailure: false));
                return;
            }

            string setTitle;
            Dictionary<int, (string label, string file)> difficulties;
            try
            {
                (setTitle, difficulties) = _definitions.ParseSetDefContent(
                    lines,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                ProtectSupportedChartPathsInSubtree(
                    Path.GetDirectoryName(setDefPath) ?? "",
                    builder,
                    cancellationToken);
                builder.Errors.Add(new SongEnumerationError(
                    SongPathIdentity.Normalize(setDefPath),
                    exception.Message,
                    IsRootFailure: false));
                return;
            }

            var directory = Path.GetDirectoryName(setDefPath) ?? "";
            var normalizedSetDefDirectory = SongPathIdentity.Normalize(directory);
            var groupKey = SongPathIdentity.ForSetDefinition(setDefPath);
            SongListNode? placeholder = null;
            var orderedPaths = new List<string>();
            var orderedDifficulties = difficulties
                .OrderBy(difficulty => difficulty.Key)
                .ToArray();
            foreach (var difficulty in orderedDifficulties)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var level = difficulty.Key;
                var (label, fileName) = difficulty.Value;
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                // Reject absolute set.def chart references outright.
                // Path.Combine silently overrides the set.def directory when
                // the second argument is rooted, which would let an absolute
                // path point anywhere on disk.
                if (Path.IsPathRooted(fileName))
                {
                    continue;
                }

                var path = SongPathIdentity.Normalize(
                    Path.Combine(directory, fileName));
                if (!DTXChartParser.IsSupportedFile(path) ||
                    !File.Exists(path))
                {
                    continue;
                }

                // Reject set.def chart references that escape the directory
                // containing the set.def. A reference like
                // ../Sibling/chart.dtx (still inside the active root) creates a
                // duplicate placeholder: one under the set.def's directory, one
                // under the chart's own directory. Both resolve to the single
                // persisted chart in the freshly published hierarchy, but on a
                // cached restart the chart is placed only by its physical
                // path, so the duplicate/misplaced node disappears — an
                // inconsistency between fresh publish and cached
                // reconstruction. Valid child paths (e.g. charts/basic.dtx)
                // are permitted.
                if (!SongPathIdentity.IsUnderNormalizedRoot(
                        path,
                        normalizedSetDefDirectory))
                {
                    continue;
                }

                builder.DiscoveredChartPaths.Add(path);
                var groupOrder = orderedPaths.Count;
                orderedPaths.Add(path);
                try
                {
                    var (song, chart) = await _parseSongEntitiesAsync(path)
                        .ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    chart.FilePath = path;
                    chart.DifficultyLevel = level;
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        chart.DifficultyLabel = label.Length > 50
                            ? label[..50]
                            : label;
                    }

                    if (!string.IsNullOrWhiteSpace(setTitle))
                        song.Title = setTitle;
                    else if (string.IsNullOrWhiteSpace(song.Title))
                        song.Title = new DirectoryInfo(directory).Name;

                    placeholder ??= CreateTemporarySongNode(
                        song,
                        chart,
                        parent);
                    AddTemporaryScore(
                        placeholder,
                        chart,
                        groupOrder,
                        authoredLabel: string.IsNullOrWhiteSpace(label)
                            ? $"Level {level}"
                            : label);
                    builder.Candidates.Add(new SongImportCandidate(
                        song,
                        chart,
                        path,
                        groupKey,
                        groupOrder));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (UnplayableTabException exception)
                {
                    RejectUnplayableTab(builder, path, exception);
                }
                catch (Exception exception)
                {
                    builder.Errors.Add(new SongEnumerationError(
                        path,
                        exception.Message,
                        IsRootFailure: false));
                }

                progress?.Report(new EnumerationProgress
                {
                    CurrentFile = Path.GetFileName(path),
                    CurrentDirectory = directory,
                    ProcessedCount = builder.DiscoveredChartPaths.Count,
                    DiscoveredSongs = builder.Candidates.Count
                });
            }

            if (placeholder == null)
                return;

            AddNodeToBatch(builder, parent, placeholder);
            builder.PendingSongs.Add(new PendingSongNode(
                groupKey,
                placeholder,
                orderedPaths));
        }

        private void ProtectSupportedChartPathsInSubtree(
            string directoryPath,
            SongEnumerationBatchBuilder builder,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var path in _enumerateFiles(directoryPath)
                .Where(DTXChartParser.IsSupportedFile)
                .Select(SongPathIdentity.Normalize)
                .OrderBy(path => path, SongPathIdentity.CanonicalComparer))
            {
                cancellationToken.ThrowIfCancellationRequested();
                builder.DiscoveredChartPaths.Add(path);
            }

            foreach (var subdirectory in _enumerateDirectories(directoryPath)
                .Select(SongPathIdentity.Normalize)
                .OrderBy(path => path, SongPathIdentity.CanonicalComparer))
            {
                ProtectSupportedChartPathsInSubtree(
                    subdirectory,
                    builder,
                    cancellationToken);
            }
        }

        private static SongListNode CreateTemporarySongNode(
            SongEntity song,
            SongChart chart,
            SongListNode? parent)
        {
            var node = SongListNode.CreateSongNode(
                song,
                chart,
                hydratePersistedScores: false);
            node.Scores = new SongScoreEntity[5];
            node.PublishScoreVariants(
                Array.Empty<KeyValuePair<ScoreVariantKey, SongScoreEntity>>());
            node.Parent = parent;
            node.DatabaseSongId = null;
            return node;
        }

        private static void AddTemporaryScore(
            SongListNode node,
            SongChart chart,
            int scoreIndex,
            string? authoredLabel)
        {
            if (scoreIndex < 0 || scoreIndex >= node.Scores.Length)
                return;

            var (instrument, difficultyLevel) =
                SongHierarchyBuilder.GetPrimaryInstrumentAndDifficulty(chart);
            var label = string.IsNullOrWhiteSpace(authoredLabel)
                ? $"Level {scoreIndex + 1}"
                : authoredLabel;
            node.DifficultyLabels[scoreIndex] = label;
            node.SetScore(scoreIndex, new SongScoreEntity
            {
                Instrument = instrument,
                DifficultyLevel = difficultyLevel,
                DifficultyLabel = label
            });
        }

        /// <summary>
        /// A tab with no playable drum part is a stable result, not a
        /// recoverable error (HPA-693): it does not hold back its root's
        /// watermark, and dropping it from the discovered paths removes any
        /// row left from when the tab had drums. The freshness check adds the
        /// persisted per-root rejected count to the database chart count,
        /// because the inventory still counts the file.
        /// </summary>
        private static void RejectUnplayableTab(
            SongEnumerationBatchBuilder builder,
            string path,
            UnplayableTabException exception)
        {
            builder.DiscoveredChartPaths.Remove(path);
            builder.RejectedChartPaths.Add(path);
        }

        private static void AddNodeToBatch(
            SongEnumerationBatchBuilder builder,
            SongListNode? parent,
            SongListNode node)
        {
            if (parent != null)
            {
                if (!parent.Children.Contains(node))
                    parent.AddChild(node);
                return;
            }

            if (!builder.RootNodes.Contains(node))
            {
                node.Parent = null;
                node.BreadcrumbPath = node.Title;
                builder.RootNodes.Add(node);
            }
        }

        #endregion

        #region Cache Freshness

        /// <summary>
        /// Checks if enumeration is needed based on database accessibility,
        /// song presence and filesystem changes since the last scan.
        /// </summary>
        public async Task<bool> NeedsEnumerationAsync(string[] searchPaths, bool forceEnumeration = false)
        {
            try
            {
                if (forceEnumeration)
                {
                    return true;
                }

                // Check if database service exists and is accessible
                if (!await _database.CanConnectAsync())
                {
                    return true;
                }

                // Cache freshness must evaluate the same canonical root set as database
                // hierarchy rebuilds and filesystem enumeration. Otherwise duplicate or
                // overlapping configured roots can double-count charts here while the
                // published library only contains one root identity.
                var canonicalRoots = _rootPolicy.Validate(searchPaths)
                    .CanonicalRoots
                    .ToArray();

                // An accessible but empty catalog cannot back a warm start even
                // when roots carry watermarks (every song pruned): enumerate
                // instead of publishing an empty hierarchy. Persisted
                // rejected-tab counts are the exception: a catalog the last
                // scan itself emptied by rejecting every tab is a stable result
                // the freshness check below can reuse, so startup does not
                // re-enumerate unchanged files just to rediscover them.
                if (!await _catalog.HasAnySongsAsync()
                    && await _catalog.GetRejectedChartCountAsync(canonicalRoots)
                        .ConfigureAwait(false) == 0)
                {
                    return true;
                }

                // Check for filesystem changes by comparing directory modification times
                var changeDetected = await DetectFilesystemChangesAsync(canonicalRoots);
                if (changeDetected)
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking enumeration need");
                return true; // Default to enumeration if we can't determine
            }
        }

        /// <summary>
        /// Checks if files in the database still exist at their recorded paths
        /// This detects when files have been moved or deleted.
        /// <para>
        /// Only charts whose <see cref="SongChart.FilePath"/> resides under one of
        /// <paramref name="activeRoots"/> are checked. Charts belonging to removed
        /// (retained) roots are intentionally skipped: their files are expected to be
        /// absent from the active root set, and flagging them as missing would force a
        /// full rescan on every startup. This mirrors the active-root scoping already
        /// applied to the filesystem and database chart counts in
        /// <see cref="DetectFilesystemChangesAsync"/>.
        /// </summary>
        private async Task<bool> CheckDatabaseFilesStillExist(
            IReadOnlyList<string> activeRoots,
            IReadOnlyList<ChartInventoryEntry> inventoryCharts)
        {
            try
            {
                var charts = await _catalog.GetActiveChartsAsync(activeRoots);

                // Paths already assigned to a catalog row belong to that row:
                // an identical untouched copy of a moved chart must not count
                // as a move candidate or break the unique-match tie-break,
                // which would leave the moved chart unrepaired and lose its
                // scores to the rescan.
                var ownedPaths = new HashSet<string>(SongPathIdentity.CanonicalComparer);
                foreach (var ownedChart in charts)
                {
                    if (SongPathIdentity.TryNormalize(ownedChart.FilePath, out var ownedPath))
                    {
                        ownedPaths.Add(ownedPath);
                    }
                }

                int missingFiles = 0;
                int updatedFiles = 0;
                int totalFiles = 0;
                var repairs = new List<(SongChart Chart, string NewPath)>();

                foreach (var chart in charts)
                {
                    totalFiles++;
                    if (!string.IsNullOrEmpty(chart.FilePath) && !File.Exists(chart.FilePath))
                    {
                        // File is missing from recorded path - try to find it in new location
                        string? newPath = FindMovedFile(chart, inventoryCharts, ownedPaths);

                        if (!string.IsNullOrEmpty(newPath))
                        {
                            repairs.Add((chart, newPath));
                        }
                        else
                        {
                            missingFiles++;
                        }
                    }
                }

                // Two missing rows can verify against the same file (identical
                // copies of a chart under two deleted directories, one
                // reappearing elsewhere). Persisting in row order would let the
                // first save win while the second dies on the unique FilePath
                // index — and the rescan would then delete the loser with its
                // scores. A contested file has no provable owner, so every
                // claim on it is left missing for the rescan to resolve.
                var claimsByPath = new Dictionary<string, int>(
                    SongPathIdentity.CanonicalComparer);
                foreach (var (_, newPath) in repairs)
                {
                    if (SongPathIdentity.TryNormalize(newPath, out var normalizedClaim))
                    {
                        claimsByPath[normalizedClaim] =
                            claimsByPath.GetValueOrDefault(normalizedClaim) + 1;
                    }
                }

                foreach (var (chart, newPath) in repairs)
                {
                    var contested =
                        SongPathIdentity.TryNormalize(newPath, out var normalizedClaim) &&
                        claimsByPath[normalizedClaim] > 1;

                    // Found the file in a new location - update database. Only a
                    // repair that persists counts: a failed save stays missing
                    // so the rescan still runs.
                    if (!contested && await UpdateChartFilePathAsync(chart, newPath))
                    {
                        updatedFiles++;
                    }
                    else
                    {
                        missingFiles++;
                    }
                }


                // Return true if we had missing files OR updated file paths (both require re-enumeration)
                return missingFiles > 0 || updatedFiles > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking database file existence");
                // If we can't check, assume no changes to avoid unnecessary re-enumeration
                return false;
            }
        }

        /// <summary>
        /// Attempts to find a moved file inside the chart inventory the
        /// freshness check already walked — exactly the files a rescan would
        /// import. Returns the new path only when exactly one candidate
        /// verifies against the persisted chart fingerprint; null otherwise,
        /// leaving the missing row for the normal rescan to recreate rather
        /// than redirecting it to an unrelated same-named chart.
        /// <para>
        /// Restricting candidates to <paramref name="inventoryCharts"/> matters
        /// beyond precision: a same-named file the importer would ignore
        /// (unreferenced in a set.def directory, inside an unreadable subtree)
        /// can neither be the repair target nor break the unique-match
        /// tie-break, because the rescan would never attach the row to it.
        /// </para>
        /// </summary>
        private string? FindMovedFile(
            SongChart missingChart,
            IReadOnlyList<ChartInventoryEntry> inventoryCharts,
            ISet<string> ownedPaths)
        {
            var originalPath = missingChart.FilePath;
            try
            {
                var fileName = Path.GetFileName(originalPath);
                if (string.IsNullOrEmpty(fileName))
                    return null;

                var verified = new List<string>();

                foreach (var entry in inventoryCharts)
                {
                    // Filename prefilter: it admits alias spellings (case folds
                    // where the filesystem does) so the verifier below decides
                    // identity, then skips the FileInfo probe for every
                    // non-matching entry.
                    if (!SongPathIdentity.LegacyAliasComparer.Equals(
                            fileName,
                            Path.GetFileName(entry.Path)))
                    {
                        continue;
                    }

                    // A path owned by another chart row is that row's file:
                    // it can neither be the repair target nor break the
                    // unique-match tie-break.
                    if (OwnedByAnotherChart(entry.Path, ownedPaths))
                    {
                        continue;
                    }

                    if (IsVerifiedMoveCandidate(missingChart, entry.Path))
                    {
                        verified.Add(entry.Path);
                    }
                }

                // Every verified candidate matches the full persisted
                // fingerprint (name, size and mtime): a single match is the
                // move target, zero or several leave the row missing for the
                // rescan — identical copies have no provable owner, and
                // guessing would graft the missing chart's scores onto an
                // unrelated file.
                return verified.Count == 1 ? verified[0] : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error finding moved file {OriginalPath}", originalPath);
                return null;
            }
        }

        /// <summary>
        /// Whether <paramref name="candidatePath"/> resolves to a file already
        /// owned by a chart row. A set.def's authored spelling can alias an
        /// owned file where the volume folds case — 'dupe.dtx' resolving to
        /// the recorded 'DUPE.dtx' — and the ordinal set misses it, letting a
        /// missing row claim another chart's file. Resolving the entry's
        /// on-disk name catches the alias without folding the stored paths,
        /// so spellings that differ only by case stay distinct on volumes
        /// that treat them so.
        /// </summary>
        private static bool OwnedByAnotherChart(
            string candidatePath,
            ISet<string> ownedPaths)
        {
            if (ownedPaths.Contains(candidatePath))
            {
                return true;
            }

            var directory = Path.GetDirectoryName(candidatePath);
            var spelling = Path.GetFileName(candidatePath);
            if (string.IsNullOrEmpty(directory) ||
                string.IsNullOrEmpty(spelling) ||
                !Directory.Exists(directory))
            {
                return false;
            }

            var resolvedName = ResolveDirectoryEntryName(
                directory,
                spelling,
                Directory
                    .EnumerateFiles(directory)
                    .Select(Path.GetFileName)
                    .ToList());
            return resolvedName != null &&
                !string.Equals(resolvedName, spelling, StringComparison.Ordinal) &&
                ownedPaths.Contains(Path.Combine(directory, resolvedName));
        }

        /// <summary>
        /// Checks whether <paramref name="candidatePath"/> is the same file as
        /// the missing <paramref name="chart"/> using the persisted fingerprint.
        /// A filename match alone never suffices: this check only runs when the
        /// recorded path no longer exists, so there is no original file left to
        /// compare against — the persisted <see cref="SongChart.FileSize"/> and
        /// <see cref="SongChart.LastModified"/> are the fingerprint. A move
        /// preserves mtime, so a same-named same-sized file with a different
        /// mtime is a different chart and stays unverified. Charts without a
        /// persisted fingerprint (legacy or seeded rows) are treated as
        /// unverifiable and left for the rescan.
        /// The candidate's name must also equal the recorded name literally —
        /// without it a differently named same-size file could inherit the
        /// missing chart's scores. A case-only difference names the same file
        /// only where the filesystem folds case — proven per file by
        /// <see cref="FileNameMatchesRecordedFile"/>, not assumed from the OS:
        /// a case-sensitive volume can host 'song.dtx' and 'SONG.DTX' as
        /// distinct files.
        /// </summary>
        private bool IsVerifiedMoveCandidate(SongChart chart, string candidatePath)
        {
            try
            {
                var candidateInfo = new FileInfo(candidatePath);
                return chart.FileSize > 0 &&
                    FileNameMatchesRecordedFile(
                        Path.GetFileName(chart.FilePath),
                        candidateInfo) &&
                    candidateInfo.Length == chart.FileSize &&
                    IsFingerprintTimestamp(candidateInfo, chart.LastModified);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// The candidate's filename must equal the recorded one, or be a
        /// spelling that resolves to the same directory entry. On a
        /// case-sensitive volume 'SONG.DTX' is a different file than the
        /// recorded 'song.dtx': the recorded spelling either resolves to a
        /// distinct sibling or does not resolve, so the candidate is rejected.
        /// Only a volume that folds the spellings resolves both to one entry —
        /// and there the directory entry carrying the recorded spelling is
        /// the candidate itself, not a separate file.
        /// </summary>
        private bool FileNameMatchesRecordedFile(
            string? recordedName,
            FileInfo candidateInfo)
        {
            if (string.IsNullOrEmpty(recordedName) ||
                string.IsNullOrEmpty(candidateInfo.Name))
            {
                return false;
            }
            if (string.Equals(
                    recordedName,
                    candidateInfo.Name,
                    StringComparison.Ordinal))
            {
                return true;
            }

            var directory = candidateInfo.Directory?.FullName;
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }

            var entryNames = Directory
                .EnumerateFiles(directory)
                .Select(Path.GetFileName)
                .ToList();
            var candidateEntry = ResolveDirectoryEntryName(
                directory, candidateInfo.Name, entryNames);
            var recordedEntry = ResolveDirectoryEntryName(
                directory, recordedName, entryNames);
            return candidateEntry != null &&
                string.Equals(
                    candidateEntry, recordedEntry, StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns the on-disk entry name <paramref name="spelling"/> resolves
        /// to inside <paramref name="directory"/>, or null when it resolves to
        /// nothing or cannot be tied to a single entry. An exact directory
        /// entry wins on any volume. A folded match counts only when the
        /// filesystem itself resolves the spelling — the
        /// <see cref="File.Exists(string)"/> gate — which proves the volume
        /// folds case: a case-sensitive volume never resolves 'dupe.dtx' to a
        /// sibling 'DUPE.dtx'. The folded compare also normalizes, because
        /// APFS stores NFD while authored paths (set.def) tend to carry NFC.
        /// </summary>
        private static string? ResolveDirectoryEntryName(
            string directory,
            string spelling,
            IReadOnlyList<string?> directoryEntryNames)
        {
            if (!File.Exists(Path.Combine(directory, spelling)))
            {
                return null;
            }

            string? normalizedSpelling = null;
            try
            {
                normalizedSpelling = spelling.Normalize();
            }
            catch (ArgumentException)
            {
            }

            string? folded = null;
            var foldedCount = 0;
            foreach (var entryName in directoryEntryNames)
            {
                if (string.Equals(entryName, spelling, StringComparison.Ordinal))
                {
                    return entryName;
                }
                if (entryName != null &&
                    (string.Equals(
                        entryName,
                        spelling,
                        StringComparison.OrdinalIgnoreCase) ||
                     FoldedNameEquals(entryName, normalizedSpelling)))
                {
                    folded = entryName;
                    foldedCount++;
                }
            }

            return foldedCount == 1 ? folded : null;
        }

        private static bool FoldedNameEquals(string entryName, string? normalizedSpelling)
        {
            if (normalizedSpelling == null)
            {
                return false;
            }
            try
            {
                return string.Equals(
                    entryName.Normalize(),
                    normalizedSpelling,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static readonly IReadOnlyList<TimeZoneInfo> _fingerprintZones =
            TimeZoneInfo.GetSystemTimeZones();

        /// <summary>
        /// The persisted <see cref="SongChart.LastModified"/> is the file's
        /// local-time reading under the timezone in effect when it was
        /// indexed, stored without an offset the schema could reinterpret.
        /// Comparing it to the current local reading strands a move whenever
        /// the timezone (or its rules) changed between launches — the same
        /// instant reads differently — so the fingerprint accepts any reading
        /// a real zone could have produced of the same instant: an exact
        /// local match, or a stored value whose difference from the
        /// candidate's UTC mtime equals some installed zone's offset at that
        /// instant (e.g. a chart indexed at UTC+13:45 and read at UTC−8).
        /// A difference no real zone explains is a different file's mtime.
        /// </summary>
        private bool IsFingerprintTimestamp(
            FileInfo candidateInfo,
            DateTime storedReading)
        {
            if (candidateInfo.LastWriteTime == storedReading)
            {
                return true;
            }

            var candidateUtc = candidateInfo.LastWriteTimeUtc;
            var offset = storedReading - candidateUtc;
            return _fingerprintZones.Any(zone =>
                zone.GetUtcOffset(candidateUtc) == offset);
        }

        /// <summary>
        /// Updates the file path for a chart in the database. The row is
        /// re-read and re-verified before saving: it must still point at the
        /// (still missing) original path, and the staged destination must
        /// still satisfy the fingerprint — a file restored or replaced while
        /// the repair was staged must not inherit the row's scores. This
        /// narrows, but cannot eliminate, the window between the existence
        /// check and the write; a contested or stale claim is left missing
        /// for the rescan to resolve.
        /// </summary>
        internal async Task<bool> UpdateChartFilePathAsync(SongChart chart, string newPath)
        {
            try
            {
                using var context = _database.CreateContext();
                var row = await context.SongCharts.FindAsync(chart.Id);
                if (row == null ||
                    !string.Equals(
                        row.FilePath,
                        chart.FilePath,
                        StringComparison.Ordinal) ||
                    File.Exists(row.FilePath) ||
                    !IsVerifiedMoveCandidate(row, newPath))
                {
                    return false;
                }

                row.FilePath = newPath;
                await context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error updating chart file path for ID {ChartId}", chart.Id);
                return false;
            }
        }

        /// <summary>
        /// Detects filesystem changes in song directories by walking the
        /// available roots once via the shared chart-inventory scanner.
        /// </summary>
        private async Task<bool> DetectFilesystemChangesAsync(string[] searchPaths)
        {
            try
            {
                // Get the last enumeration timestamp from database
                var lastEnumerationTime = await GetLastEnumerationTimestampAsync();
                if (lastEnumerationTime == null)
                {
                    return true; // First time enumeration
                }


                // Probe canonical roots once and carry only currently available
                // roots through every downstream check. A missing or inaccessible
                // configured root is a diagnostic condition, NOT a filesystem
                // change: a full scan cannot make the root available, and
                // treating its absence as a change whenever another root has
                // charts forces a perpetual rescan on every startup. When the
                // root returns (external drive reattached, network share
                // mounted), its retained watermark is used to detect edits even
                // when the chart count still matches the retained database rows.
                var availableRoots = new List<string>(searchPaths.Length);
                foreach (var searchPath in searchPaths)
                {
                    if (string.IsNullOrEmpty(searchPath))
                        continue;
                    if (_rootPolicy.Probe(searchPath).Availability == SongRootAvailability.Available)
                        availableRoots.Add(searchPath);
                }

                // Walk the available roots once, mirroring full enumeration's
                // directory and set.def discovery rules. The inventory yields
                // exactly the chart files enumeration would import (set.def-
                // referenced charts only in a set.def directory; case-insensitive
                // extension matching) plus the set.def and directory mtimes used
                // by the change check below.
                var inventory = await ScanChartInventoryAsync(
                    availableRoots.ToArray(), CancellationToken.None).ConfigureAwait(false);

                // File count mismatch is a reliable indicator of added/removed
                // charts. Both counts are scoped to the same available root set:
                // the inventory is restricted to these roots, so the database
                // count must be too. The global chart count would include
                // retained rows belonging to a removed or unavailable root,
                // which can never match the scoped filesystem count and would
                // force a full rescan on every startup. The database side counts
                // charts (one row per chart file), not score rows, so it matches
                // the one-file-per-chart filesystem contract even when a chart
                // has many score variants.
                // Tabs the last scan rejected (HPA-693) are in the inventory
                // but have no database row, so their persisted count is added.
                var currentFileCount = inventory.Charts.Count;
                var databaseChartCount = await GetActiveDatabaseChartCountAsync(availableRoots)
                    + await _catalog.GetRejectedChartCountAsync(availableRoots).ConfigureAwait(false);


                // Check if files in database still exist at their recorded paths
                // (detects moves) BEFORE the count comparison: a verified move
                // must repair the persisted path while the row is still matched
                // to it. When a move coincides with an add/remove, the early
                // count return would skip this repair, and the rescan — which
                // matches charts by path only — would import the moved file as
                // a new chart and delete the old row along with its scores.
                // Scoped to the same available roots as the file/DB counts so
                // retained charts belonging to a removed or unavailable root
                // are not flagged as missing. Move candidates come from the
                // inventory above so a file the import would ignore cannot
                // claim — or veto — a repair.
                var filesMovedOrDeleted = await CheckDatabaseFilesStillExist(
                    availableRoots,
                    inventory.Charts);
                if (filesMovedOrDeleted)
                {
                    return true;
                }

                if (currentFileCount != databaseChartCount)
                {
                    return true;
                }

                // Check each available root against its own last successful
                // watermark. A root omitted from a partial scan has no new
                // watermark, so its return is conservatively treated as a change.
                var rootWatermarks = await GetRootEnumerationWatermarksAsync(
                    availableRoots).ConfigureAwait(false);
                if (InventoryHasChangesSince(
                        inventory,
                        availableRoots,
                        rootWatermarks))
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error detecting filesystem changes");
                return true; // Default to enumeration on error
            }
        }

        private async Task<int> GetActiveDatabaseChartCountAsync(
            IReadOnlyList<string> activeRoots)
        {
            try
            {
                return await _catalog.GetActiveChartCountAsync(activeRoots);
            }
            catch
            {
                return 0;
            }
        }

        private async Task<IReadOnlyDictionary<string, DateTime>>
            GetRootEnumerationWatermarksAsync(
                IReadOnlyList<string> availableRoots)
        {
            var watermarks = new Dictionary<string, DateTime>(
                SongPathIdentity.CanonicalComparer);

            foreach (var root in availableRoots)
            {
                var watermark = await _catalog
                    .GetLastSuccessfulEnumerationUtcAsync(root)
                    .ConfigureAwait(false);
                if (watermark.HasValue)
                    watermarks[root] = watermark.Value;
            }

            // A missing per-root watermark must remain missing. Assigning the
            // legacy global timestamp to a root that was never successfully
            // enumerated (or that returned after a partial scan where another
            // root advanced the global value) would hide stale content: the
            // root's retained rows and file mtimes would compare clean against
            // a watermark it never earned, so NeedsEnumerationAsync would
            // return false and never retry the failed parse. The conservative
            // behavior — treat a missing watermark as "needs enumeration" —
            // costs one full scan to initialize per-root metadata on a legacy
            // database, then never triggers again.
            return watermarks;
        }

        /// <summary>
        /// Walks the active song roots once, mirroring full enumeration's
        /// directory and set.def discovery rules, and returns the chart inventory
        /// that would be imported plus the set.def and directory mtimes used by
        /// the change check. Replaces the previous per-extension recursive globs
        /// (six walks for counting plus seven for timestamp checking per root)
        /// with a single walk per root.
        /// <para>
        /// Traversal matches <see cref="EnumerateDirectoryIntoBatchAsync"/>: a
        /// directory containing <c>set.def</c> contributes only the set.def's
        /// referenced charts (and is not recursed); a directory without one
        /// contributes its loose supported charts and recurses into all
        /// subdirectories. Chart paths are deduplicated by normalized path so
        /// overlapping roots or a set.def referencing one file from two
        /// difficulties cannot double-count.
        /// </para>
        /// </summary>
        private Task<ChartInventory> ScanChartInventoryAsync(
            string[] searchPaths, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var inventory = new ChartInventory();
                var seenCharts = new HashSet<string>(SongPathIdentity.CanonicalComparer);
                foreach (var root in searchPaths
                    .Where(path => !string.IsNullOrEmpty(path) && Directory.Exists(path)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ScanChartInventoryDirectory(
                        root, inventory, seenCharts, cancellationToken);
                }
                return inventory;
            }, cancellationToken);
        }

        private void ScanChartInventoryDirectory(
            string directoryPath,
            ChartInventory inventory,
            HashSet<string> seenCharts,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Wrap this directory's access (DirectoryInfo construction and
            // file/directory enumeration) so an inaccessible or unreadable
            // directory skips itself while its siblings continue scanning.
            // OperationCanceledException is NOT caught here, so cancellation
            // from ThrowIfCancellationRequested (above and in the loops below)
            // propagates unchanged.
            try
            {
                var dirInfo = new DirectoryInfo(directoryPath);
                inventory.Directories.Add(new ChartInventoryEntry(
                    SongPathIdentity.Normalize(directoryPath),
                    dirInfo.LastWriteTimeUtc,
                    dirInfo.CreationTimeUtc));

                var setDefPath = Path.Combine(directoryPath, "set.def");
                if (File.Exists(setDefPath))
                {
                    // set.def directory: only referenced charts are imported; loose
                    // charts and subdirectories are NOT scanned (enumeration returns
                    // early after ParseSetDefinitionIntoBatchAsync).
                    var setDefInfo = new FileInfo(setDefPath);
                    inventory.SetDefinitions.Add(new ChartInventoryEntry(
                        SongPathIdentity.Normalize(setDefPath),
                        setDefInfo.LastWriteTimeUtc,
                        setDefInfo.CreationTimeUtc));
                    foreach (var chartPath in EnumerateSetDefReferencedCharts(
                        setDefPath, cancellationToken))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!seenCharts.Add(chartPath))
                            continue;
                        var chartInfo = new FileInfo(chartPath);
                        inventory.Charts.Add(new ChartInventoryEntry(
                            chartPath,
                            chartInfo.LastWriteTimeUtc,
                            chartInfo.CreationTimeUtc));
                    }
                    return;
                }

                // No set.def: collect loose supported charts (case-insensitive via
                // IsSupportedFile, matching full enumeration) and recurse into all
                // subdirectories. Boxes are organizational only and do not filter
                // charts, so they are recursed like any other directory.
                foreach (var file in _enumerateFiles(directoryPath)
                    .Where(DTXChartParser.IsSupportedFile)
                    .Select(SongPathIdentity.Normalize)
                    .OrderBy(path => path, SongPathIdentity.CanonicalComparer))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!File.Exists(file))
                        continue;
                    if (!seenCharts.Add(file))
                        continue;
                    var fileInfo = new FileInfo(file);
                    inventory.Charts.Add(new ChartInventoryEntry(
                        file,
                        fileInfo.LastWriteTimeUtc,
                        fileInfo.CreationTimeUtc));
                }

                foreach (var subdirectory in _enumerateDirectories(directoryPath)
                    .Select(SongPathIdentity.Normalize)
                    .OrderBy(path => path, SongPathIdentity.CanonicalComparer))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ScanChartInventoryDirectory(
                        subdirectory, inventory, seenCharts, cancellationToken);
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }

        /// <summary>
        /// Resolves the chart file paths a <c>set.def</c> references (and that
        /// exist on disk, are supported, and remain within the directory
        /// containing the set.def), mirroring
        /// <see cref="ParseSetDefinitionIntoBatchAsync"/>. Reuses the shared
        /// <see cref="SongDefinitionReader.ReadSetDefLines"/> encoding chain and
        /// <see cref="SongDefinitionReader.ParseSetDefContent"/> so the inventory never drifts from
        /// what full enumeration imports. References that escape the set.def's
        /// directory via <c>../</c> traversal or absolute paths are rejected so
        /// the inventory count matches the database freshness count and no
        /// duplicate placeholders are created.
        /// </summary>
        private IEnumerable<string> EnumerateSetDefReferencedCharts(
            string setDefPath,
            CancellationToken cancellationToken)
        {
            var directory = Path.GetDirectoryName(setDefPath) ?? "";
            var normalizedSetDefDirectory = SongPathIdentity.Normalize(directory);
            var lines = _definitions.ReadSetDefLines(setDefPath);
            if (lines == null)
                yield break;

            var (_, difficulties) = _definitions.ParseSetDefContent(lines, cancellationToken);
            var emitted = new HashSet<string>(SongPathIdentity.CanonicalComparer);
            foreach (var (_, fileName) in difficulties.Values)
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;
                if (Path.IsPathRooted(fileName))
                    continue;
                var path = SongPathIdentity.Normalize(Path.Combine(directory, fileName));
                if (DTXChartParser.IsSupportedFile(path) &&
                    File.Exists(path) &&
                    SongPathIdentity.IsUnderNormalizedRoot(
                        path, normalizedSetDefDirectory) &&
                    emitted.Add(path))
                {
                    yield return path;
                }
            }
        }

        /// <summary>
        /// Returns true if any directory, set.def, or chart in
        /// <paramref name="inventory"/> was created or modified after the
        /// watermark for its containing root. Mirrors the per-entry checks
        /// previously spread across multiple recursive checks, but evaluated over
        /// a single inventory walk instead of one recursive walk per extension.
        /// </summary>
        private static bool InventoryHasChangesSince(
            ChartInventory inventory,
            IReadOnlyList<string> activeRoots,
            IReadOnlyDictionary<string, DateTime> rootWatermarks)
        {
            foreach (var root in activeRoots)
            {
                if (!rootWatermarks.ContainsKey(root))
                {
                    return true;
                }
            }

            foreach (var dir in inventory.Directories)
            {
                if (TryGetRootWatermark(
                        dir.Path,
                        activeRoots,
                        rootWatermarks,
                        out var watermark) &&
                    dir.LastWriteTimeUtc > watermark)
                {
                    return true;
                }
            }

            foreach (var setDef in inventory.SetDefinitions)
            {
                if (TryGetRootWatermark(
                        setDef.Path,
                        activeRoots,
                        rootWatermarks,
                        out var watermark) &&
                    (setDef.CreationTimeUtc > watermark ||
                     setDef.LastWriteTimeUtc > watermark))
                {
                    return true;
                }
            }

            foreach (var chart in inventory.Charts)
            {
                if (TryGetRootWatermark(
                        chart.Path,
                        activeRoots,
                        rootWatermarks,
                        out var watermark) &&
                    (chart.CreationTimeUtc > watermark ||
                     chart.LastWriteTimeUtc > watermark))
                {
                    var reason = chart.CreationTimeUtc > watermark ? "new" : "modified";
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetRootWatermark(
            string path,
            IReadOnlyList<string> activeRoots,
            IReadOnlyDictionary<string, DateTime> rootWatermarks,
            out DateTime watermark)
        {
            watermark = default;
            var containingRoot = FindContainingRoot(path, activeRoots);
            return containingRoot != null &&
                rootWatermarks.TryGetValue(containingRoot, out watermark);
        }

        /// <summary>
        /// Finds the most specific (longest) normalized active root that
        /// contains the supplied normalized path, or <c>null</c> if the path is
        /// not beneath any root. Used to map a recoverable enumeration error
        /// (file parse failure, set.def read failure) to the root whose
        /// watermark must NOT advance so the next freshness check retries it,
        /// and to attribute rejected tabs and inventory entries to their root.
        /// Shared with <see cref="SongLibrary"/>'s post-scan metadata writes.
        /// </summary>
        internal static string? FindContainingRoot(
            string normalizedPath,
            IReadOnlyList<string> normalizedRoots)
        {
            string? containingRoot = null;
            foreach (var root in normalizedRoots)
            {
                if (!SongPathIdentity.IsUnderNormalizedRoot(normalizedPath, root) ||
                    (containingRoot != null && root.Length <= containingRoot.Length))
                {
                    continue;
                }
                containingRoot = root;
            }
            return containingRoot;
        }

        /// <summary>
        /// Gets the timestamp of the last successful enumeration from the database.
        /// <para>
        /// The timestamp is an explicitly persisted
        /// <c>LastSuccessfulEnumerationUtc</c> metadata value, NOT the database
        /// file's last-write time. The file mtime advances on every
        /// <c>SaveChangesAsync</c> (bookmark toggle, score save), so deriving the
        /// enumeration time from it would let an unrelated write mask later chart
        /// edits when the file count is unchanged. A null result means no
        /// enumeration has been recorded yet and forces a first-time scan.
        /// </para>
        /// <para>
        /// Returns UTC directly. The inventory walker uses
        /// <c>LastWriteTimeUtc</c>/<c>CreationTimeUtc</c>, so the comparison is
        /// UTC-vs-UTC — no local-time conversion that would break under DST or
        /// timezone shifts.
        /// </para>
        /// </summary>
        private async Task<DateTime?> GetLastEnumerationTimestampAsync()
        {
            try
            {
                var timestamp = await _catalog.GetLastSuccessfulEnumerationUtcAsync();
                if (timestamp == null)
                {
                    return null;
                }

                return timestamp;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting last enumeration timestamp");
                return null;
            }
        }

        #endregion
    }
}
