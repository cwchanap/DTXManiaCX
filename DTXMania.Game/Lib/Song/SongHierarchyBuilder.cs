#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using SongScoreEntity = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// Builds the warm-start song hierarchy from persisted charts and finalizes the
    /// cold scan's pending nodes (HPA-801 Decision C/D). Also owns the one BOX and
    /// root-node factory shared by the cold scan and this warm rebuild, so both
    /// paths agree on skin path, genre, text colour, breadcrumb, root header,
    /// bundled-root placement and root-title disambiguation. Uses the same
    /// library-owned <see cref="SongDefinitionReader"/> instance as the cold
    /// scanner; holds no publication state.
    /// </summary>
    internal sealed class SongHierarchyBuilder
    {
        private readonly SongCatalogStore _catalog;
        private readonly SongDefinitionReader _definitions;
        private readonly SongRootPolicy _rootPolicy;
        private readonly string _bundledSongsRoot;
        private readonly ILogger _logger;

        public SongHierarchyBuilder(
            SongCatalogStore catalog,
            SongDefinitionReader definitions,
            SongRootPolicy rootPolicy,
            string bundledSongsRoot,
            ILogger? logger = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _rootPolicy = rootPolicy ?? throw new ArgumentNullException(nameof(rootPolicy));
            _bundledSongsRoot = bundledSongsRoot;
            _logger = logger ?? NullLogger.Instance;
        }

        /// <summary>
        /// Builds the database hierarchy without publishing it. Returns null when
        /// there is no database, no available root, or the build fails. The caller
        /// commits the result via <see cref="SongLibrary.PublishDatabaseHierarchy(SongDatabaseHierarchy)"/>,
        /// which lets activation-scoped stages defer publication to their update
        /// thread and drop it entirely if the activation retired first.
        /// </summary>
        internal async Task<SongDatabaseHierarchy?> PrepareHierarchyFromDatabaseOnceAsync(
            string[] searchPaths)
        {
            try
            {
                var newRootNodes = new List<SongListNode>();
                var allSongs = await _catalog.GetSongsAsync().ConfigureAwait(false);
                var configuredRoots = _rootPolicy.Validate(searchPaths)
                    .CanonicalRoots;
                var availableRoots = configuredRoots
                    .Where(root =>
                        _rootPolicy.Probe(root).Availability == SongRootAvailability.Available)
                    .ToArray();

                if (availableRoots.Length == 0)
                {
                    return null;
                }

                var activeChartCount = 0;
                var rootHeaders = await ResolveSongRootHeadersOrNullAsync(
                    availableRoots,
                    CancellationToken.None).ConfigureAwait(false);

                foreach (var normalizedRoot in availableRoots)
                {
                    // Get all charts that belong to this search path
                    var relevantCharts = allSongs
                        .SelectMany(song => song.Charts
                            .Where(chart =>
                                SongPathIdentity.TryNormalize(
                                    chart.FilePath,
                                    out var normalized) &&
                                SongPathIdentity.IsUnderNormalizedRoot(
                                    normalized,
                                    normalizedRoot))
                            .Select(chart => (song, chart)))
                        .ToList();

                    activeChartCount += relevantCharts.Count;

                    // Build the folder hierarchy structure from file paths
                    var pathNodes = await BuildHierarchyFromCharts(normalizedRoot, relevantCharts);
                    if (rootHeaders == null ||
                        !rootHeaders.TryGetValue(normalizedRoot, out var header))
                    {
                        newRootNodes.AddRange(pathNodes);
                        continue;
                    }

                    if (pathNodes.Count == 0)
                        continue;

                    var rootBox = CreateSongRootBoxNode(normalizedRoot, header);
                    foreach (var node in pathNodes)
                        AttachSubtree(rootBox, node);
                    newRootNodes.Add(rootBox);
                }

                return new SongDatabaseHierarchy(
                    newRootNodes,
                    availableRoots,
                    activeChartCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error building song list from database");
                return null;
            }
        }

        /// <summary>
        /// Builds hierarchical folder structure from chart file paths
        /// Recreates the original folder structure based on file paths
        /// </summary>
        internal async Task<List<SongListNode>> BuildHierarchyFromCharts(string searchPath, List<(SongEntity song, SongChart chart)> charts)
        {
            var rootNodes = new List<SongListNode>();
            var folderNodeCache = new Dictionary<string, SongListNode>(StringComparer.OrdinalIgnoreCase);

            // Persisted Song.Id is the grouping authority. Title/artist are display
            // metadata and can legitimately collide across different directories.
            // Groups follow the order a scan publishes them in, so a cached launch
            // lists songs exactly like the launch that enumerated them.
            var songGroups = charts
                .GroupBy(item => item.song.Id)
                .OrderBy(
                    group => group
                        .Select(item => GetScanOrderKey(searchPath, item.chart.FilePath))
                        .Min(SongPathIdentity.CanonicalComparer),
                    SongPathIdentity.CanonicalComparer)
                .ToList();

            foreach (var songGroup in songGroups)
            {
                var firstChart = songGroup.First().chart;
                var firstSong = songGroup.First().song;
                var allCharts = songGroup.Select(item => item.chart).ToArray();

                if (string.IsNullOrEmpty(firstChart.FilePath))
                    continue;

                // Check if the file still exists at the recorded path
                if (!File.Exists(firstChart.FilePath))
                {
                    continue;
                }

                // Get the directory path relative to the search path
                var fullDirectoryPath = Path.GetDirectoryName(firstChart.FilePath);
                if (string.IsNullOrEmpty(fullDirectoryPath))
                    continue;

                // A set.def song lives where its set.def is, even when the charts sit in
                // a child folder, matching where the scan published it.
                var songDirectoryPath = FindOwningSetDefDirectory(searchPath, fullDirectoryPath)
                    ?? fullDirectoryPath;

                // Create folder hierarchy for this song
                var parentNode = await EnsureFolderHierarchy(searchPath, songDirectoryPath, folderNodeCache, rootNodes);

                // Create the song node
                var songNode = CreateSongNodeFromDatabaseEntities(firstSong, allCharts);
                if (songNode != null)
                {
                    if (parentNode != null)
                    {
                        parentNode.AddChild(songNode);
                        songNode.Parent = parentNode;
                    }
                    else
                    {
                        // Song is directly in the search path root
                        songNode.BreadcrumbPath = songNode.Title;
                        rootNodes.Add(songNode);
                    }
                }
            }

            return rootNodes;
        }

        /// <summary>
        /// Sort key reproducing the cold scan's visit order: in every folder,
        /// subfolders (ordinal) come before the folder's own chart files (ordinal),
        /// so directory segments are tagged "0" and the file segment "1". The
        /// separator sorts below any path character, keeping a folder ahead of a
        /// longer sibling that shares its name as a prefix.
        /// </summary>
        private static string GetScanOrderKey(string root, string? chartPath)
        {
            if (string.IsNullOrEmpty(chartPath))
                return string.Empty;

            var segments = Path.GetRelativePath(root, chartPath)
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            return string.Join(
                '\0',
                segments.Select((segment, index) =>
                    (index < segments.Length - 1 ? "0" : "1") + segment));
        }

        /// <summary>
        /// Returns the folder above <paramref name="directory"/> (from
        /// <paramref name="root"/> down) whose set.def claims its charts, or null.
        /// The cold scan stops descending at the first folder holding a set.def and
        /// publishes everything it references there.
        /// </summary>
        private static string? FindOwningSetDefDirectory(string root, string directory)
        {
            var relative = Path.GetRelativePath(root, directory);
            if (relative == ".")
                return null;

            var current = root;
            foreach (var segment in relative.Split(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                if (File.Exists(Path.Combine(current, "set.def")))
                    return current;
                current = Path.Combine(current, segment);
            }

            return null;
        }

        /// <summary>
        /// Ensures folder hierarchy exists for a given path
        /// Returns the deepest folder node for the path
        /// </summary>
        private async Task<SongListNode?> EnsureFolderHierarchy(string searchPath, string fullPath, Dictionary<string, SongListNode> folderCache, List<SongListNode> rootNodes)
        {
            if (string.IsNullOrEmpty(fullPath) || fullPath.Equals(searchPath, StringComparison.OrdinalIgnoreCase))
                return null;

            // Check cache first
            if (folderCache.TryGetValue(fullPath, out var cachedNode))
                return cachedNode;

            var parentPath = Path.GetDirectoryName(fullPath);
            var folderName = Path.GetFileName(fullPath);

            if (string.IsNullOrEmpty(folderName))
                return null;

            // Recursively ensure parent folder exists (only if parentPath is not null/empty)
            SongListNode? parentNode = null;
            if (!string.IsNullOrEmpty(parentPath))
            {
                parentNode = await EnsureFolderHierarchy(searchPath, parentPath, folderCache, rootNodes);
            }

            // Check if this is a DTXFiles.* prefixed folder or has box.def
            var isDTXFilesFolder = folderName.StartsWith("DTXFiles.", StringComparison.OrdinalIgnoreCase);
            var boxDefPath = Path.Combine(fullPath, "box.def");
            var hasBoxDef = File.Exists(boxDefPath);

            // Create folder node if it's a BOX folder
            if (isDTXFilesFolder || hasBoxDef)
            {
                BoxDefinition? boxDef = null;
                if (hasBoxDef)
                {
                    try
                    {
                        boxDef = await _definitions.ParseBoxDefinitionAsync(boxDefPath, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error parsing box.def at {BoxDefPath}", boxDefPath);
                    }
                }

                // The shared BOX factory keeps this warm rebuild visually identical
                // to the cold scan (title, genre, skin, text colour, breadcrumb).
                var folderNode = CreateBoxNodeFromDirectory(
                    new DirectoryInfo(fullPath),
                    parentNode,
                    boxDef);

                // Add to parent or root
                if (parentNode != null)
                {
                    parentNode.AddChild(folderNode);
                }
                else
                {
                    // This is a top-level folder, add to root nodes
                    rootNodes.Add(folderNode);
                }

                folderCache[fullPath] = folderNode;
                return folderNode;
            }

            // If not a BOX folder, treat as part of the path but don't create a folder node
            return parentNode;
        }

        /// <summary>
        /// Fills the cold scan's temporary song placeholders with their persisted
        /// charts after the database import commits. Placeholder identity, parent,
        /// position, breadcrumb and slot layout survive untouched, so the published
        /// tree equals what a cached database reload would rebuild.
        /// </summary>
        internal void FinalizePendingNodes(
            SongEnumerationBatch batch,
            IReadOnlyDictionary<string, SongChart> chartsByPath)
        {
            foreach (var pending in batch.PendingSongs)
            {
                var placeholder = pending.Placeholder;
                var resolvedCharts = new List<SongChart>();
                // A set.def may list one file in several slots; songs.db keeps it once.
                foreach (var path in pending.OrderedChartPaths
                    .Distinct(SongPathIdentity.CanonicalComparer))
                {
                    if (chartsByPath.TryGetValue(path, out var chart))
                        resolvedCharts.Add(chart);
                }

                placeholder.Scores = new SongScoreEntity[placeholder.Scores.Length];
                if (resolvedCharts.Count == 0)
                {
                    RemovePlaceholder(batch, placeholder);
                    continue;
                }

                // Slots take the difficulty order GetCurrentDifficultyChart plays them in,
                // not discovery order, exactly as CreateSongNodeFromDatabaseEntities
                // rebuilds them on a cached launch: sort every chart, then fill the slots.
                var slot = 0;
                foreach (var chart in resolvedCharts
                    .GetDifficultySlotCharts()
                    .Take(placeholder.Scores.Length))
                {
                    var (instrument, difficultyLevel) =
                        GetPrimaryInstrumentAndDifficulty(chart);
                    var label = ResolveDifficultyLabel(
                        chart,
                        new Dictionary<string, string>(),
                        slot);
                    placeholder.DifficultyLabels[slot] = label;
                    placeholder.SetScore(slot, new SongScoreEntity
                    {
                        ChartId = chart.Id,
                        Instrument = instrument,
                        DifficultyLevel = difficultyLevel,
                        DifficultyLabel = label
                    });
                    slot++;
                }

                placeholder.DatabaseSong = resolvedCharts[0].Song;
                placeholder.DatabaseChart = resolvedCharts[0];
                placeholder.DatabaseSongId = resolvedCharts[0].SongId;
                HydrateScoreVariants(placeholder, resolvedCharts);
            }
        }

        private static void RemovePlaceholder(
            SongEnumerationBatch batch,
            SongListNode placeholder)
        {
            var node = placeholder;
            while (node.Parent != null)
            {
                var parent = node.Parent;
                parent.RemoveChild(node);
                // A BOX whose last chart was rejected would be published empty and
                // could not be entered, so drop it too and keep walking up.
                if (parent.Type != NodeType.Box || parent.Children.Count > 0)
                    return;

                node = parent;
            }

            batch.RootNodes.Remove(node);
        }

        /// <summary>
        /// The one BOX factory for the cold scan and the warm rebuild: applies a
        /// parsed box.def (title, genre, skin path, text colour) onto a BOX node,
        /// falling back to the folder name as the title.
        /// </summary>
        internal SongListNode CreateBoxNodeFromDirectory(DirectoryInfo directory, SongListNode? parent, BoxDefinition? boxDef)
        {
            var title = boxDef?.Title ?? directory.Name;
            var boxNode = SongListNode.CreateBoxNode(title, directory.FullName, parent);

            if (boxDef != null)
            {
                boxNode.Genre = boxDef.Genre ?? "";
                boxNode.SkinPath = boxDef.SkinPath ?? "";
                boxNode.TextColor = new Microsoft.Xna.Framework.Color(
                    boxDef.TextColor.R,
                    boxDef.TextColor.G,
                    boxDef.TextColor.B,
                    boxDef.TextColor.A);
            }

            return boxNode;
        }

        /// <summary>
        /// Resolves the root BOX headers when more than one configured root is active,
        /// otherwise null so a lone root merges into the top level. The bundled songs
        /// root never gets a header: its own BOX folders sit beside the user's library
        /// rather than pushing a single configured root one level deeper.
        /// </summary>
        internal async Task<IReadOnlyDictionary<string, (BoxDefinition? Definition, string Title)>?>
            ResolveSongRootHeadersOrNullAsync(
                IReadOnlyList<string> activeRoots,
                CancellationToken cancellationToken)
        {
            var headerRoots = activeRoots
                .Where(root => !_rootPolicy.AreSameRoot(root, _bundledSongsRoot))
                .ToArray();
            return headerRoots.Length > 1
                ? await ResolveSongRootHeadersAsync(headerRoots, cancellationToken).ConfigureAwait(false)
                : null;
        }

        /// <summary>
        /// Reads each active root's box.def once and settles the title its top-level BOX
        /// shows: the box.def #TITLE when set, otherwise the folder name. Titles that would
        /// collide fall back to the root's full path so every root stays distinguishable.
        /// </summary>
        private async Task<IReadOnlyDictionary<string, (BoxDefinition? Definition, string Title)>>
            ResolveSongRootHeadersAsync(
                IReadOnlyList<string> activeRoots,
                CancellationToken cancellationToken)
        {
            var definitions = new BoxDefinition?[activeRoots.Count];
            var titles = new string[activeRoots.Count];
            for (var index = 0; index < activeRoots.Count; index++)
            {
                var boxDefPath = Path.Combine(activeRoots[index], "box.def");
                definitions[index] = File.Exists(boxDefPath)
                    ? await _definitions.ParseBoxDefinitionAsync(boxDefPath, cancellationToken).ConfigureAwait(false)
                    : null;
                titles[index] = string.IsNullOrWhiteSpace(definitions[index]?.Title)
                    ? GetSongRootFolderName(activeRoots[index])
                    : definitions[index]!.Title!;
            }

            var displayTitles = DisambiguateSongRootTitles(activeRoots, titles);
            var headers = new Dictionary<string, (BoxDefinition?, string)>(StringComparer.Ordinal);
            for (var index = 0; index < activeRoots.Count; index++)
                headers[activeRoots[index]] = (definitions[index], displayTitles[index]);

            return headers;
        }

        /// <summary>
        /// Creates the top-level BOX that holds one configured song root when more than
        /// one root is active, so each folder browses as its own entry instead of every
        /// root's songs being merged into a single root page. A box.def in the root
        /// supplies colours like any other BOX; the title comes from
        /// <see cref="ResolveSongRootHeadersAsync"/>.
        /// </summary>
        internal SongListNode CreateSongRootBoxNode(
            string root,
            (BoxDefinition? Definition, string Title) header)
        {
            var boxNode = CreateBoxNodeFromDirectory(new DirectoryInfo(root), parent: null, header.Definition);
            boxNode.IsSongRoot = true;
            boxNode.Title = header.Title;
            boxNode.BreadcrumbPath = header.Title;
            return boxNode;
        }

        /// <summary>
        /// Returns <paramref name="titles"/> with every title that more than one root
        /// would show (case-insensitively) replaced by that root's full path. Repeats
        /// until the shown titles are unique, since a fallback path can itself match
        /// another root's box.def title; roots are distinct, so this always settles.
        /// </summary>
        internal static IReadOnlyList<string> DisambiguateSongRootTitles(
            IReadOnlyList<string> roots,
            IReadOnlyList<string> titles)
        {
            var result = titles.ToArray();
            bool changed;
            do
            {
                changed = false;
                var collisions = result
                    .GroupBy(title => title, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < result.Length; index++)
                {
                    if (!collisions.Contains(result[index]) ||
                        string.Equals(result[index], roots[index], StringComparison.Ordinal))
                        continue;

                    result[index] = roots[index];
                    changed = true;
                }
            }
            while (changed);

            return result;
        }

        private static string GetSongRootFolderName(string root)
        {
            var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
            return string.IsNullOrEmpty(folderName) ? root : folderName;
        }

        /// <summary>
        /// Re-parents an already-built subtree and refreshes every descendant breadcrumb,
        /// since <see cref="SongListNode.AddChild"/> only updates the direct child.
        /// </summary>
        private static void AttachSubtree(SongListNode parent, SongListNode node)
        {
            parent.AddChild(node);
            RefreshDescendantBreadcrumbs(node);
        }

        private static void RefreshDescendantBreadcrumbs(SongListNode node)
        {
            foreach (var child in node.Children)
            {
                child.BreadcrumbPath = $"{node.BreadcrumbPath} > {child.Title}";
                RefreshDescendantBreadcrumbs(child);
            }
        }

        /// <summary>
        /// Creates a SongListNode from database entities (song + charts)
        /// </summary>
        internal SongListNode? CreateSongNodeFromDatabaseEntities(SongEntity song, SongChart[] charts)
        {
            try
            {
                if (charts.Length == 0) return null;

                // Use the first chart for the primary file path
                var primaryChart = charts[0];
                var orderedCharts = charts.GetDifficultySlotCharts();

                // Create the song node using the first chart
                var songNode = SongListNode.CreateSongNode(
                    song,
                    primaryChart,
                    hydratePersistedScores: false);

                songNode.DatabaseSongId = song.Id;

                // The factory seeded slots from only the primary chart. Rebuild the
                // complete metadata layout from scratch, even for a single chart, so the
                // slots and labels match what enumeration publishes and unused
                // instrument slots from that chart cannot survive the ordered pass.
                songNode.Scores = new SongScore[5];
                songNode.PublishScoreVariants(
                    Array.Empty<KeyValuePair<ScoreVariantKey, SongScore>>());

                // Recover the authentic difficulty-tier labels (BASIC/ADVANCED/EXTREME/...) from the
                // SET.def when the persisted chart label is empty, so the performance-stage difficulty
                // badge selects the matching cell instead of always falling back to the DTX cell.
                // Legacy databases stored an empty SongChart.DifficultyLabel.
                // Only hit disk when at least one chart is missing its label — avoids re-reading
                // SET.def for every song on startup when labels are already persisted.
                var setDefLabels = charts.Any(c => string.IsNullOrWhiteSpace(c.DifficultyLabel))
                    ? _definitions.GetSetDefLabelsByFile(Path.GetDirectoryName(primaryChart.FilePath) ?? "")
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                int scoreIndex = 0;
                // Slots follow SongChartHelper.GetDifficultySlotCharts, the order
                // GetCurrentDifficultyChart maps a difficulty index into and the scan's
                // FinalizePendingNodes publishes, so DifficultyLabels[scoreIndex] belongs to the
                // chart that difficulty plays. Without this, an arbitrary database ordering could
                // place an EXTREME chart's label in slot 0 while difficulty 0 loads the BASIC chart,
                // making the performance-stage badge show the wrong difficulty cell.
                foreach (var chart in orderedCharts.Take(5)) // Limit to 5 difficulties
                {
                    if (scoreIndex >= songNode.Scores.Length) break;

                    // Determine the primary instrument and difficulty for this chart
                    var primaryInstrument = DTXMania.Game.Lib.Song.Entities.EInstrumentPart.DRUMS;
                    int difficultyLevel = 50;

                    if (chart.HasDrumChart && chart.DrumLevel > 0)
                    {
                        primaryInstrument = DTXMania.Game.Lib.Song.Entities.EInstrumentPart.DRUMS;
                        difficultyLevel = chart.DrumLevel;
                    }
                    else if (chart.HasGuitarChart && chart.GuitarLevel > 0)
                    {
                        primaryInstrument = DTXMania.Game.Lib.Song.Entities.EInstrumentPart.GUITAR;
                        difficultyLevel = chart.GuitarLevel;
                    }
                    else if (chart.HasBassChart && chart.BassLevel > 0)
                    {
                        primaryInstrument = DTXMania.Game.Lib.Song.Entities.EInstrumentPart.BASS;
                        difficultyLevel = chart.BassLevel;
                    }

                    string difficultyLabelText = ResolveDifficultyLabel(chart, setDefLabels, scoreIndex);

                    songNode.SetScore(scoreIndex, new DTXMania.Game.Lib.Song.Entities.SongScore
                    {
                        ChartId = chart.Id,
                        Instrument = primaryInstrument,
                        DifficultyLevel = difficultyLevel,
                        DifficultyLabel = difficultyLabelText
                    });

                    songNode.DifficultyLabels[scoreIndex] = difficultyLabelText;
                    scoreIndex++;
                }

                // Metadata slots are now final. Publish every eagerly loaded persisted
                // speed as one complete immutable snapshot; only 1.00x replaces Scores.
                HydrateScoreVariants(songNode, charts);

                return songNode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error creating song node from database entities");
                return null;
            }
        }

        private static void HydrateScoreVariants(
            SongListNode songNode,
            IEnumerable<SongChart> charts)
        {
            var chartArray = charts.ToArray();
            var variants = new Dictionary<ScoreVariantKey, SongScore>();

            for (int difficultyIndex = 0;
                difficultyIndex < songNode.Scores.Length;
                difficultyIndex++)
            {
                var metadata = songNode.Scores[difficultyIndex];
                if (metadata == null)
                    continue;

                metadata.PlaySpeedPercent = PlaySpeedRange.Default;
                variants[new ScoreVariantKey(
                    difficultyIndex,
                    PlaySpeedRange.Default)] = metadata.Clone();

                var persistedScores = chartArray
                    .Where(chart => metadata.ChartId != 0
                        ? chart.Id == metadata.ChartId
                        : chart.GetDifficultyLevel(
                            metadata.Instrument.ToString()) == metadata.DifficultyLevel)
                    .SelectMany(chart =>
                        chart.Scores ?? Enumerable.Empty<SongScore>())
                    .Where(score => score.Instrument == metadata.Instrument);

                foreach (var persisted in persistedScores)
                {
                    var snapshot = CreateScoreSnapshot(persisted);
                    snapshot.ChartId = metadata.ChartId;
                    snapshot.Instrument = metadata.Instrument;
                    snapshot.DifficultyLevel = metadata.DifficultyLevel;
                    snapshot.DifficultyLabel = metadata.DifficultyLabel;
                    variants[new ScoreVariantKey(
                        difficultyIndex,
                        persisted.PlaySpeedPercent)] = snapshot;
                }
            }

            songNode.PublishScoreVariants(variants);
        }

        /// <summary>
        /// Resolves the difficulty-tier label shown by the performance-stage difficulty badge for a
        /// chart. Prefers the persisted <see cref="SongChart.DifficultyLabel"/>, then the SET.def
        /// #LnLABEL recovered by matching the chart's file name (e.g. "bas.dtx" -> "BASIC"), and
        /// finally a synthetic "Level N" placeholder when no authentic label is available. N is
        /// the SET.def #Ln slot when the chart came from one, as the scan labels it, else the
        /// one-based slot index.
        /// </summary>
        internal static string ResolveDifficultyLabel(SongChart chart, IReadOnlyDictionary<string, string> setDefLabels, int scoreIndex)
        {
            if (!string.IsNullOrWhiteSpace(chart.DifficultyLabel))
                return chart.DifficultyLabel;

            if (setDefLabels != null && !string.IsNullOrEmpty(chart.FilePath))
            {
                var fileName = Path.GetFileName(chart.FilePath);
                if (setDefLabels.TryGetValue(fileName, out var label) && !string.IsNullOrWhiteSpace(label))
                    return label;
            }

            return $"Level {(chart.DifficultyLevel > 0 ? chart.DifficultyLevel : scoreIndex + 1)}";
        }

        internal static (
            EInstrumentPart instrument,
            int difficultyLevel) GetPrimaryInstrumentAndDifficulty(
                SongChart chart)
        {
            if (chart.HasDrumChart && chart.DrumLevel > 0)
                return (EInstrumentPart.DRUMS, chart.DrumLevel);
            if (chart.HasGuitarChart && chart.GuitarLevel > 0)
                return (EInstrumentPart.GUITAR, chart.GuitarLevel);
            if (chart.HasBassChart && chart.BassLevel > 0)
                return (EInstrumentPart.BASS, chart.BassLevel);
            return (EInstrumentPart.DRUMS, 50);
        }

        /// <summary>
        /// Creates a detached score snapshot with only its own pitch-bearing history
        /// rows and rebuilds the legacy display lines from that scoped collection.
        /// </summary>
        internal static SongScore CreateScoreSnapshot(SongScore source)
        {
            var snapshot = source.Clone();
            snapshot.PerformanceHistory = snapshot.PerformanceHistory
                .Where(h => h.SongScoreId == source.Id)
                .OrderBy(h => h.DisplayOrder)
                .ToList();
            snapshot.PlayHistoryLines = snapshot.PerformanceHistory
                .Select(history => history.HistoryLine)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(GameConstants.PlayHistory.MaxRecentPlays)
                .ToList();
            return snapshot;
        }
    }
}
