using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using SongScoreEntity = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Game.Lib.Song.Entities
{
    /// <summary>
    /// Catalog-side persistence over one shared <see cref="SongDatabase"/>:
    /// atomic bulk import/cleanup, chart inventory queries, per-root scan
    /// watermarks and rejected-tab counts (HPA-801). Score transactions and
    /// receipts live on <see cref="SongScoreStore"/>.
    /// </summary>
    public class SongCatalogStore
    {
        private readonly SongDatabase _database;

        private readonly Func<int, bool>? _activeChartIdentityFilter;

        public SongCatalogStore(
            SongDatabase database,
            Func<int, bool>? activeChartIdentityFilter = null)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _activeChartIdentityFilter = activeChartIdentityFilter;
        }

        /// <summary>
        /// Narrow song-presence read (single EXISTS query). Startup's freshness
        /// decision only needs "does the database contain any song"; the old
        /// five-COUNT aggregate statistics query was removed for it.
        /// </summary>
        public async Task<bool> HasAnySongsAsync()
        {
            using var context = _database.CreateContext();
            return await context.Songs
                .AsNoTracking()
                .AnyAsync();
        }


        internal async Task<SongBulkImportResult> ImportSongsAsync(
            SongBulkImportRequest request,
            IProgress<SongBulkImportProgress>? progress,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            await using var context = _database.CreateContext();
            context.Database.SetCommandTimeout(TimeSpan.FromSeconds(120));
            await using var transaction =
                await context.Database.BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);

            try
            {
                var chartsByPath = new Dictionary<string, SongChart>(
                    SongPathIdentity.CanonicalComparer);
                var added = 0;
                var updated = 0;
                var preserved = 0;
                var skipped = 0;
                var conflicts = 0;
                progress?.Report(new(
                    SongBulkImportMilestone.PreloadStarted,
                    0,
                    request.Candidates.Count));

                var chartIdentities = await context.SongCharts
                    .AsNoTracking()
                    .Select(chart => new
                    {
                        chart.Id,
                        chart.SongId,
                        chart.FilePath
                    })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                // Pre-normalize active roots once to avoid redundant Normalize
                // calls per chart per root in the containment check below.
                var normalizedActiveRoots = request.ActiveRoots
                    .Select(root =>
                    {
                        SongPathIdentity.TryNormalize(root, out var normalized);
                        return normalized;
                    })
                    .Where(r => !string.IsNullOrEmpty(r))
                    .ToArray();

                var activeChartIds = chartIdentities
                    .Where(chart =>
                        SongPathIdentity.TryNormalize(
                            chart.FilePath,
                            out var normalized) &&
                        normalizedActiveRoots.Any(root =>
                            SongPathIdentity.IsUnderNormalizedRoot(normalized, root)) &&
                        (_activeChartIdentityFilter == null ||
                            _activeChartIdentityFilter(chart.Id)))
                    .Select(chart => chart.Id)
                    .OrderBy(id => id)
                    .ToArray();

                var persistedCharts = new List<SongChart>();
                foreach (var activeChartIdChunk in activeChartIds.Chunk(900))
                {
                    var chunk = await context.SongCharts
                        .Where(chart =>
                            activeChartIdChunk.Contains(chart.Id))
                        .Include(chart => chart.Song)
                        .Include(chart => chart.Scores)
                            .ThenInclude(score => score.PerformanceHistory)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    persistedCharts.AddRange(chunk);
                }
                persistedCharts = persistedCharts
                    .OrderBy(chart => chart.Id)
                    .ToList();

                var persistedSongs = persistedCharts
                    .Select(chart => chart.Song)
                    .DistinctBy(song => song.Id)
                    .ToList();
                var persistedByCanonicalPath = persistedCharts
                    .Select(chart => new
                    {
                        Chart = chart,
                        Normalized = SongPathIdentity.Normalize(chart.FilePath)
                    })
                    .GroupBy(
                        item => item.Normalized,
                        SongPathIdentity.CanonicalComparer)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .Select(item => item.Chart)
                            .OrderBy(chart => chart.Id)
                            .ToList(),
                        SongPathIdentity.CanonicalComparer);

                var persistedChartByDiscoveredPath =
                    new Dictionary<string, SongChart>(
                        SongPathIdentity.CanonicalComparer);
                var discoveredPersistedChartIds = new HashSet<int>();
                var pathConflictProtectedChartIds = new HashSet<int>();
                var migratedChartIds = new HashSet<int>();
                var skippedDiscoveredPaths = new HashSet<string>(
                    SongPathIdentity.CanonicalComparer);
                var discoveredPaths = request.DiscoveredChartPaths
                    .OrderBy(
                        path => path,
                        SongPathIdentity.CanonicalComparer)
                    .ToArray();

                void MarkPathConflict(
                    IReadOnlyCollection<string> paths,
                    IEnumerable<int> protectedChartIds)
                {
                    conflicts++;
                    foreach (var chartId in protectedChartIds)
                        pathConflictProtectedChartIds.Add(chartId);
                    foreach (var path in paths)
                    {
                        if (skippedDiscoveredPaths.Add(path))
                            skipped++;
                    }
                }

                // Precompute normalized-path -> ordered chart-id lookup once so
                // each migration-collision check is O(1) instead of re-scanning
                // and re-normalizing every persisted chart identity per call.
                var chartIdsByNormalizedPath = chartIdentities
                    .Select(identity =>
                    {
                        var valid = SongPathIdentity.TryNormalize(
                            identity.FilePath, out var normalizedPath);
                        return new
                        {
                            identity.Id,
                            NormalizedPath = normalizedPath,
                            Valid = valid
                        };
                    })
                    .Where(item => item.Valid)
                    .GroupBy(
                        item => item.NormalizedPath,
                        SongPathIdentity.CanonicalComparer)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .Select(item => item.Id)
                            .OrderBy(id => id)
                            .ToArray(),
                        SongPathIdentity.CanonicalComparer);

                bool HasBinaryTargetCollision(
                    SongChart chart,
                    string targetPath,
                    out int[] collidingIds)
                {
                    collidingIds = chartIdsByNormalizedPath.TryGetValue(
                            targetPath, out var ids)
                        ? ids.Where(id => id != chart.Id).ToArray()
                        : Array.Empty<int>();
                    return collidingIds.Length > 0;
                }

                void ResolveUniquePath(
                    string discoveredPath,
                    SongChart chart)
                {
                    if (!string.Equals(
                        chart.FilePath,
                        discoveredPath,
                        StringComparison.Ordinal))
                    {
                        if (HasBinaryTargetCollision(
                            chart,
                            discoveredPath,
                            out var collidingIds))
                        {
                            MarkPathConflict(
                                new[] { discoveredPath },
                                collidingIds.Append(chart.Id));
                            return;
                        }

                        chart.FilePath = discoveredPath;
                        migratedChartIds.Add(chart.Id);
                    }

                    persistedChartByDiscoveredPath.Add(
                        discoveredPath,
                        chart);
                    discoveredPersistedChartIds.Add(chart.Id);
                }

                var unmatchedDiscoveredPaths = new List<string>();
                foreach (var discoveredPath in discoveredPaths)
                {
                    if (!persistedByCanonicalPath.TryGetValue(
                        discoveredPath,
                        out var exactCharts))
                    {
                        unmatchedDiscoveredPaths.Add(discoveredPath);
                        continue;
                    }

                    if (exactCharts.Count == 1)
                    {
                        ResolveUniquePath(discoveredPath, exactCharts[0]);
                    }
                    else
                    {
                        MarkPathConflict(
                            new[] { discoveredPath },
                            exactCharts.Select(chart => chart.Id));
                    }
                }

                foreach (var aliasPathGroup in unmatchedDiscoveredPaths.GroupBy(
                    path => path,
                    SongPathIdentity.LegacyAliasComparer))
                {
                    var aliasPaths = aliasPathGroup
                        .OrderBy(
                            path => path,
                            SongPathIdentity.CanonicalComparer)
                        .ToArray();
                    if (aliasPaths.Length > 1)
                    {
                        var protectedIds = persistedByCanonicalPath
                            .Where(pair =>
                                aliasPaths.Any(path =>
                                    SongPathIdentity.LegacyAliasComparer.Equals(
                                        pair.Key,
                                        path)) &&
                                pair.Value.Any(chart =>
                                    !discoveredPersistedChartIds.Contains(
                                        chart.Id)))
                            .SelectMany(pair => pair.Value)
                            .Where(chart =>
                                !discoveredPersistedChartIds.Contains(
                                    chart.Id))
                            .Select(chart => chart.Id)
                            .Distinct()
                            .ToArray();
                        MarkPathConflict(aliasPaths, protectedIds);
                        continue;
                    }

                    var discoveredPath = aliasPaths[0];
                    var aliasCharts = persistedByCanonicalPath
                        .Where(pair =>
                            SongPathIdentity.LegacyAliasComparer.Equals(
                                pair.Key,
                                discoveredPath))
                        .SelectMany(pair => pair.Value)
                        .Where(chart =>
                            !discoveredPersistedChartIds.Contains(chart.Id))
                        .OrderBy(chart => chart.Id)
                        .ToArray();
                    var hasConflictProtectedAlias = aliasCharts.Any(chart =>
                        pathConflictProtectedChartIds.Contains(chart.Id));
                    if (aliasCharts.Length == 1 &&
                        !hasConflictProtectedAlias)
                    {
                        ResolveUniquePath(
                            discoveredPath,
                            aliasCharts[0]);
                    }
                    else if (aliasCharts.Length > 0)
                    {
                        MarkPathConflict(
                            aliasPaths,
                            aliasCharts.Select(chart => chart.Id));
                    }
                }

                var uniqueCandidates = new List<SongImportCandidate>();
                var candidatePaths = new HashSet<string>(
                    SongPathIdentity.CanonicalComparer);
                foreach (var candidate in request.Candidates
                    .OrderBy(candidate => candidate.GroupKey, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.GroupOrder)
                    .ThenBy(
                        candidate => candidate.NormalizedChartPath,
                        SongPathIdentity.CanonicalComparer))
                {
                    if (!candidatePaths.Add(candidate.NormalizedChartPath))
                    {
                        skipped++;
                        continue;
                    }

                    uniqueCandidates.Add(candidate);
                }

                foreach (var discoveredPath in discoveredPaths)
                {
                    if (!candidatePaths.Contains(discoveredPath) &&
                        skippedDiscoveredPaths.Add(discoveredPath))
                    {
                        skipped++;
                    }
                }

                progress?.Report(new(
                    SongBulkImportMilestone.MatchingCompleted,
                    request.Candidates.Count,
                    request.Candidates.Count));

                foreach (var group in uniqueCandidates
                    .Where(candidate =>
                        request.DiscoveredChartPaths.Contains(
                            candidate.NormalizedChartPath) &&
                        !skippedDiscoveredPaths.Contains(
                            candidate.NormalizedChartPath))
                    .GroupBy(
                        candidate => candidate.GroupKey,
                        StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var primary = group
                        .OrderBy(candidate => candidate.GroupOrder)
                        .ThenBy(
                            candidate => candidate.NormalizedChartPath,
                            SongPathIdentity.CanonicalComparer)
                        .First();
                    var groupCandidates = group.ToArray();
                    var matchedCharts = groupCandidates
                        .Select(candidate =>
                            persistedChartByDiscoveredPath.TryGetValue(
                                candidate.NormalizedChartPath,
                                out var chart)
                                ? chart
                                : null)
                        .Where(chart => chart != null)
                        .Select(chart => chart!)
                        .DistinctBy(chart => chart.Id)
                        .ToArray();
                    var matchedSongIds = matchedCharts
                        .Select(chart => chart.SongId)
                        .Distinct()
                        .OrderBy(songId => songId)
                        .ToArray();

                    if (matchedSongIds.Length == 0)
                    {
                        var song = CreateSongFromParsed(primary.ParsedSong);
                        context.Songs.Add(song);
                        foreach (var candidate in groupCandidates)
                        {
                            var chart = CreateChartFromParsed(
                                candidate.ParsedChart,
                                song);
                            chart.FilePath = candidate.NormalizedChartPath;
                            AddMissingInitialScores(chart);
                            context.SongCharts.Add(chart);
                            chartsByPath.Add(
                                candidate.NormalizedChartPath,
                                chart);
                            added++;
                        }

                        continue;
                    }

                    if (matchedSongIds.Length == 1)
                    {
                        var song = matchedCharts[0].Song;
                        var songChanged = UpdateSongFromParsed(
                            song,
                            primary.ParsedSong);
                        foreach (var candidate in groupCandidates)
                        {
                            if (persistedChartByDiscoveredPath.TryGetValue(
                                candidate.NormalizedChartPath,
                                out var chart))
                            {
                                var chartChanged = UpdateChartFromParsed(
                                    chart,
                                    candidate.ParsedChart,
                                    candidate.NormalizedChartPath);
                                var scoreAdded = AddMissingInitialScores(chart);
                                if (songChanged ||
                                    chartChanged ||
                                    scoreAdded ||
                                    migratedChartIds.Contains(chart.Id))
                                {
                                    updated++;
                                }
                                else
                                {
                                    preserved++;
                                }
                                chartsByPath.Add(
                                    candidate.NormalizedChartPath,
                                    chart);
                            }
                            else
                            {
                                var createdChart = CreateChartFromParsed(
                                    candidate.ParsedChart,
                                    song);
                                createdChart.FilePath =
                                    candidate.NormalizedChartPath;
                                AddMissingInitialScores(createdChart);
                                context.SongCharts.Add(createdChart);
                                chartsByPath.Add(
                                    candidate.NormalizedChartPath,
                                    createdChart);
                                added++;
                            }
                        }

                        continue;
                    }

                    conflicts++;
                    var songChangedById = matchedCharts
                        .Select(chart => chart.Song)
                        .DistinctBy(song => song.Id)
                        .ToDictionary(
                            song => song.Id,
                            song => UpdateSongFromParsed(
                                song,
                                primary.ParsedSong));
                    SongEntity newSiblingSong = null;
                    foreach (var candidate in groupCandidates)
                    {
                        if (persistedChartByDiscoveredPath.TryGetValue(
                            candidate.NormalizedChartPath,
                            out var chart))
                        {
                            var chartChanged = UpdateChartFromParsed(
                                chart,
                                candidate.ParsedChart,
                                candidate.NormalizedChartPath);
                            var scoreAdded = AddMissingInitialScores(chart);
                            if (songChangedById[chart.SongId] ||
                                chartChanged ||
                                scoreAdded ||
                                migratedChartIds.Contains(chart.Id))
                            {
                                updated++;
                            }
                            else
                            {
                                preserved++;
                            }
                            chartsByPath.Add(
                                candidate.NormalizedChartPath,
                                chart);
                        }
                        else
                        {
                            if (newSiblingSong == null)
                            {
                                newSiblingSong =
                                    CreateSongFromParsed(primary.ParsedSong);
                                context.Songs.Add(newSiblingSong);
                            }
                            var createdChart = CreateChartFromParsed(
                                candidate.ParsedChart,
                                newSiblingSong);
                            createdChart.FilePath =
                                candidate.NormalizedChartPath;
                            AddMissingInitialScores(createdChart);
                            context.SongCharts.Add(createdChart);
                            chartsByPath.Add(
                                candidate.NormalizedChartPath,
                                createdChart);
                            added++;
                        }
                    }
                }

                skipped += uniqueCandidates.Count(candidate =>
                    !request.DiscoveredChartPaths.Contains(
                        candidate.NormalizedChartPath));
                progress?.Report(new(
                    SongBulkImportMilestone.MutationsStaged,
                    request.Candidates.Count,
                    request.Candidates.Count));

                var staleCharts = persistedCharts
                    .Where(chart =>
                        !discoveredPersistedChartIds.Contains(chart.Id) &&
                        !pathConflictProtectedChartIds.Contains(chart.Id))
                    .ToList();
                context.SongCharts.RemoveRange(staleCharts);

                var staleChartIds = staleCharts
                    .Select(chart => chart.Id)
                    .ToHashSet();
                var candidateEmptySongIds = staleCharts
                    .Select(chart => chart.SongId)
                    .Distinct()
                    .Where(songId => !chartIdentities.Any(identity =>
                        identity.SongId == songId &&
                        !staleChartIds.Contains(identity.Id)))
                    .ToHashSet();
                var emptySongs = persistedSongs
                    .Where(song =>
                        candidateEmptySongIds.Contains(song.Id))
                    .ToList();
                context.Songs.RemoveRange(emptySongs);

                progress?.Report(new(
                    SongBulkImportMilestone.CleanupCompleted,
                    request.Candidates.Count,
                    request.Candidates.Count));
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new(
                    SongBulkImportMilestone.SaveStarted,
                    request.Candidates.Count,
                    request.Candidates.Count));
                await context.SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await transaction.CommitAsync(cancellationToken)
                    .ConfigureAwait(false);
                // The transaction has committed; nothing the progress callback
                // can do should be able to turn this into a reported failure or
                // trigger a meaningless post-commit rollback. Report outside the
                // catch scope so a throwing subscriber is swallowed and logged.
                try
                {
                    progress?.Report(new(
                        SongBulkImportMilestone.Committed,
                        request.Candidates.Count,
                        request.Candidates.Count));
                }
                catch (Exception)
                {
                }
                return new SongBulkImportResult(
                    chartsByPath,
                    added,
                    updated,
                    preserved,
                    skipped,
                    conflicts,
                    staleCharts.Count,
                    emptySongs.Count);
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                }

                throw;
            }
        }

        private static SongEntity CreateSongFromParsed(SongEntity parsed) =>
            new()
            {
                Title = parsed.Title,
                Artist = parsed.Artist,
                Genre = parsed.Genre,
                Comment = parsed.Comment
            };

        private static bool UpdateSongFromParsed(
            SongEntity song,
            SongEntity parsed)
        {
            var changed =
                !string.Equals(
                    song.Title,
                    parsed.Title,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    song.Artist,
                    parsed.Artist,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    song.Genre,
                    parsed.Genre,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    song.Comment,
                    parsed.Comment,
                    StringComparison.Ordinal);
            if (!changed)
                return false;

            song.Title = parsed.Title;
            song.Artist = parsed.Artist;
            song.Genre = parsed.Genre;
            song.Comment = parsed.Comment;
            song.UpdatedAt = DateTime.UtcNow;
            return true;
        }

        private static SongChart CreateChartFromParsed(
            SongChart parsed,
            SongEntity song) =>
            new()
            {
                Song = song,
                FilePath = parsed.FilePath,
                FileSize = parsed.FileSize,
                LastModified = parsed.LastModified,
                FileFormat = parsed.FileFormat,
                DifficultyLevel = parsed.DifficultyLevel,
                DifficultyLabel = parsed.DifficultyLabel,
                Bpm = parsed.Bpm,
                Duration = parsed.Duration,
                BGMAdjust = parsed.BGMAdjust,
                DrumLevel = parsed.DrumLevel,
                DrumLevelDec = parsed.DrumLevelDec,
                GuitarLevel = parsed.GuitarLevel,
                GuitarLevelDec = parsed.GuitarLevelDec,
                BassLevel = parsed.BassLevel,
                BassLevelDec = parsed.BassLevelDec,
                HasDrumChart = parsed.HasDrumChart,
                HasGuitarChart = parsed.HasGuitarChart,
                HasBassChart = parsed.HasBassChart,
                IsClassicDrums = parsed.IsClassicDrums,
                IsClassicGuitar = parsed.IsClassicGuitar,
                IsClassicBass = parsed.IsClassicBass,
                DrumNoteCount = parsed.DrumNoteCount,
                GuitarNoteCount = parsed.GuitarNoteCount,
                BassNoteCount = parsed.BassNoteCount,
                PreviewFile = parsed.PreviewFile,
                PreviewImage = parsed.PreviewImage,
                BackgroundFile = parsed.BackgroundFile,
                StageFile = parsed.StageFile
            };

        private static bool UpdateChartFromParsed(
            SongChart chart,
            SongChart parsed,
            string normalizedPath)
        {
            var changed =
                !string.Equals(
                    chart.FilePath,
                    normalizedPath,
                    StringComparison.Ordinal) ||
                chart.FileSize != parsed.FileSize ||
                chart.LastModified != parsed.LastModified ||
                !string.Equals(
                    chart.FileFormat,
                    parsed.FileFormat,
                    StringComparison.Ordinal) ||
                chart.DifficultyLevel != parsed.DifficultyLevel ||
                !string.Equals(
                    chart.DifficultyLabel,
                    parsed.DifficultyLabel,
                    StringComparison.Ordinal) ||
                chart.Bpm != parsed.Bpm ||
                chart.Duration != parsed.Duration ||
                chart.BGMAdjust != parsed.BGMAdjust ||
                chart.DrumLevel != parsed.DrumLevel ||
                chart.DrumLevelDec != parsed.DrumLevelDec ||
                chart.GuitarLevel != parsed.GuitarLevel ||
                chart.GuitarLevelDec != parsed.GuitarLevelDec ||
                chart.BassLevel != parsed.BassLevel ||
                chart.BassLevelDec != parsed.BassLevelDec ||
                chart.HasDrumChart != parsed.HasDrumChart ||
                chart.HasGuitarChart != parsed.HasGuitarChart ||
                chart.HasBassChart != parsed.HasBassChart ||
                chart.IsClassicDrums != parsed.IsClassicDrums ||
                chart.IsClassicGuitar != parsed.IsClassicGuitar ||
                chart.IsClassicBass != parsed.IsClassicBass ||
                chart.DrumNoteCount != parsed.DrumNoteCount ||
                chart.GuitarNoteCount != parsed.GuitarNoteCount ||
                chart.BassNoteCount != parsed.BassNoteCount ||
                !string.Equals(
                    chart.PreviewFile,
                    parsed.PreviewFile,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    chart.PreviewImage,
                    parsed.PreviewImage,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    chart.BackgroundFile,
                    parsed.BackgroundFile,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    chart.StageFile,
                    parsed.StageFile,
                    StringComparison.Ordinal);

            chart.FilePath = normalizedPath;
            chart.FileSize = parsed.FileSize;
            chart.LastModified = parsed.LastModified;
            chart.FileFormat = parsed.FileFormat;
            chart.DifficultyLevel = parsed.DifficultyLevel;
            chart.DifficultyLabel = parsed.DifficultyLabel;
            chart.Bpm = parsed.Bpm;
            chart.Duration = parsed.Duration;
            chart.BGMAdjust = parsed.BGMAdjust;
            chart.DrumLevel = parsed.DrumLevel;
            chart.DrumLevelDec = parsed.DrumLevelDec;
            chart.GuitarLevel = parsed.GuitarLevel;
            chart.GuitarLevelDec = parsed.GuitarLevelDec;
            chart.BassLevel = parsed.BassLevel;
            chart.BassLevelDec = parsed.BassLevelDec;
            chart.HasDrumChart = parsed.HasDrumChart;
            chart.HasGuitarChart = parsed.HasGuitarChart;
            chart.HasBassChart = parsed.HasBassChart;
            chart.IsClassicDrums = parsed.IsClassicDrums;
            chart.IsClassicGuitar = parsed.IsClassicGuitar;
            chart.IsClassicBass = parsed.IsClassicBass;
            chart.DrumNoteCount = parsed.DrumNoteCount;
            chart.GuitarNoteCount = parsed.GuitarNoteCount;
            chart.BassNoteCount = parsed.BassNoteCount;
            chart.PreviewFile = parsed.PreviewFile;
            chart.PreviewImage = parsed.PreviewImage;
            chart.BackgroundFile = parsed.BackgroundFile;
            chart.StageFile = parsed.StageFile;
            return changed;
        }

        private static bool AddMissingInitialScores(SongChart chart)
        {
            var added = AddMissingInitialScore(
                chart,
                EInstrumentPart.DRUMS,
                chart.HasDrumChart && chart.DrumLevel > 0);
            added |= AddMissingInitialScore(
                chart,
                EInstrumentPart.GUITAR,
                chart.HasGuitarChart && chart.GuitarLevel > 0);
            added |= AddMissingInitialScore(
                chart,
                EInstrumentPart.BASS,
                chart.HasBassChart && chart.BassLevel > 0);
            return added;
        }

        private static bool AddMissingInitialScore(
            SongChart chart,
            EInstrumentPart instrument,
            bool isAvailable)
        {
            if (!isAvailable || chart.Scores.Any(
                score => score.Instrument == instrument
                    && score.PlaySpeedPercent == 100))
            {
                return false;
            }

            chart.Scores.Add(new SongScoreEntity
            {
                Chart = chart,
                Instrument = instrument,
                PlaySpeedPercent = 100
            });
            return true;
        }

        /// <summary>
        /// Get all songs from the database
        /// </summary>
        public async Task<List<SongEntity>> GetSongsAsync()
        {
            using var context = _database.CreateContext();

            return await context.Songs
                .Include(s => s.Charts)
                    .ThenInclude(c => c.Scores)
                        .ThenInclude(sc => sc.PerformanceHistory)
                .OrderBy(s => s.Title)
                .ToListAsync();
        }

        /// <summary>
        /// Returns the <see cref="SongChart.Id"/> and <see cref="SongChart.FilePath"/>
        /// of every chart whose file path resides under one of <paramref name="roots"/>
        /// (root-containment uses <see cref="SongPathIdentity"/>, mirroring
        /// SongLibrary's active-chart rule). Used by the cache-freshness
        /// file-existence check so charts belonging to removed (retained) roots are
        /// not flagged as missing on every startup. Returns an empty list for an
        /// empty root set.
        /// </summary>
        public async Task<IReadOnlyList<(int Id, string FilePath)>> GetActiveChartsAsync(
            IReadOnlyList<string> roots)
        {
            if (roots == null || roots.Count == 0)
                return Array.Empty<(int, string)>();

            // Normalize defensively so blank or malformed roots (which
            // CountDTXFilesAsync also skips) do not abort the lookup. Mirrors
            // the active-root pre-normalization in GetActiveChartCountAsync.
            var normalizedRoots = roots
                .Select(root =>
                {
                    SongPathIdentity.TryNormalize(root, out var normalized);
                    return normalized;
                })
                .Where(r => !string.IsNullOrEmpty(r))
                .ToArray();
            if (normalizedRoots.Length == 0)
                return Array.Empty<(int, string)>();
            using var context = _database.CreateContext();
            var charts = await context.SongCharts
                .AsNoTracking()
                .Select(chart => new { chart.Id, chart.FilePath })
                .ToListAsync();
            var active = new List<(int Id, string FilePath)>(charts.Count);
            foreach (var chart in charts)
            {
                if (!string.IsNullOrEmpty(chart.FilePath) &&
                    SongPathIdentity.TryNormalize(chart.FilePath, out var normalized) &&
                    normalizedRoots.Any(root =>
                        SongPathIdentity.IsUnderRoot(normalized, root)))
                {
                    active.Add((chart.Id, chart.FilePath));
                }
            }
            return active;
        }

        /// <summary>
        /// Counts <see cref="SongChart"/> rows whose <see cref="SongChart.FilePath"/>
        /// resides under one of <paramref name="roots"/> (root-containment uses
        /// <see cref="SongPathIdentity"/>, mirroring SongLibrary's active-chart
        /// rule). This is the active-root-scoped chart count compared against the
        /// filesystem chart-file count by the cache-freshness check.
        /// <para>
        /// IMPORTANT: this must count <em>charts</em>, not <see cref="SongScoreEntity"/>
        /// rows. A single chart has one-to-many <see cref="SongScoreEntity"/> rows
        /// (uniqueness = ChartId + Instrument + PlaySpeedPercent): import seeds
        /// DRUMS/GUITAR/BASS rows, and each distinct play speed adds another. Once a
        /// player records results at multiple speeds or instruments, the score-row
        /// count permanently exceeds the chart-file count and forces a full rescan
        /// on every startup. Counting charts keeps the database side of the
        /// cache-freshness comparison aligned with the filesystem contract (one
        /// file = one chart).
        /// </para>
        /// Returns 0 for an empty root set or when no active charts exist.
        /// </summary>
        public async Task<int> GetActiveChartCountAsync(IReadOnlyList<string> roots)
        {
            if (roots == null || roots.Count == 0)
                return 0;

            // Normalize defensively so blank or malformed roots (which
            // CountDTXFilesAsync also skips) do not abort the count. Mirrors the
            // active-root pre-normalization in ImportSongsAsync.
            var normalizedRoots = roots
                .Select(root =>
                {
                    SongPathIdentity.TryNormalize(root, out var normalized);
                    return normalized;
                })
                .Where(r => !string.IsNullOrEmpty(r))
                .ToArray();
            if (normalizedRoots.Length == 0)
                return 0;
            using var context = _database.CreateContext();
            var charts = await context.SongCharts
                .AsNoTracking()
                .Select(chart => new { chart.Id, chart.FilePath })
                .ToListAsync();
            var activeChartIds = new HashSet<int>();
            foreach (var chart in charts)
            {
                if (SongPathIdentity.TryNormalize(chart.FilePath, out var normalized) &&
                    normalizedRoots.Any(root =>
                        SongPathIdentity.IsUnderRoot(normalized, root)))
                {
                    activeChartIds.Add(chart.Id);
                }
            }

            // Count active charts, NOT score rows: one chart file maps to many
            // SongScore rows across instrument and play-speed variants, so the
            // score count would never match the filesystem file count.
            return activeChartIds.Count;
        }

        /// <summary>
        /// Key under which the last successful enumeration timestamp is stored in
        /// <see cref="__EnumerationMetadata"/>. The value is an ISO 8601 UTC string.
        /// </summary>
        private const string LastSuccessfulEnumerationKey = "LastSuccessfulEnumerationUtc";

        /// <summary>
        /// Key prefix for per-root enumeration watermarks stored in
        /// <see cref="__EnumerationMetadata"/>. Exposed as internal so tests
        /// can build the same wildcard delete filter without hardcoding the
        /// literal. The full key is built by <see cref="BuildRootWatermarkKey"/>.
        /// </summary>
        internal const string LastSuccessfulEnumerationRootKeyPrefix =
            "LastSuccessfulEnumerationUtc:Root:";

        /// <summary>
        /// Builds the per-root watermark key for a normalized root. Centralized
        /// so the read/write/atomic-write sites cannot drift from each other.
        /// </summary>
        private static string BuildRootWatermarkKey(string normalizedRoot) =>
            LastSuccessfulEnumerationRootKeyPrefix +
                SongPathIdentity.GetStableRootKey(normalizedRoot);

        /// <summary>
        /// Returns the UTC timestamp of the last successful enumeration, or null
        /// when no enumeration has been recorded (fresh database, missing table, or
        /// unparseable value). Never throws: any storage error is treated as
        /// "no enumeration recorded" so the caller falls back to a full scan.
        /// </summary>
        public async Task<DateTime?> GetLastSuccessfulEnumerationUtcAsync()
        {
            try
            {
                using var context = _database.CreateContext();
                var value = await context.Database.SqlQueryRaw<string>(
                    "SELECT Value FROM __EnumerationMetadata WHERE Key = {0} LIMIT 1",
                    LastSuccessfulEnumerationKey).ToListAsync();
                var raw = value.FirstOrDefault();
                if (string.IsNullOrEmpty(raw) ||
                    !DateTime.TryParse(raw, null,
                        System.Globalization.DateTimeStyles.RoundtripKind,
                        out var parsed))
                {
                    return null;
                }
                return parsed.ToUniversalTime();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the UTC timestamp recorded for a canonical song root, or null
        /// when that root has not been included in a successful enumeration.
        /// </summary>
        public async Task<DateTime?> GetLastSuccessfulEnumerationUtcAsync(
            string canonicalRoot)
        {
            try
            {
                if (!SongPathIdentity.TryNormalize(canonicalRoot, out var normalizedRoot))
                    return null;

                using var context = _database.CreateContext();
                var value = await context.Database.SqlQueryRaw<string>(
                    "SELECT Value FROM __EnumerationMetadata WHERE Key = {0} LIMIT 1",
                    BuildRootWatermarkKey(normalizedRoot))
                    .ToListAsync();
                var raw = value.FirstOrDefault();
                if (string.IsNullOrEmpty(raw) ||
                    !DateTime.TryParse(
                        raw,
                        null,
                        DateTimeStyles.RoundtripKind,
                        out var parsed))
                {
                    return null;
                }

                return parsed.ToUniversalTime();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Writes a legacy global enumeration watermark into the
        /// __EnumerationMetadata table. Production writes go through
        /// <see cref="SetEnumerationWatermarkAtomicallyAsync"/>; this method is
        /// retained as a test seeding helper for watermark/freshness states
        /// (HPA-801). Never aborts the caller over metadata failures.
        /// </summary>
        public async Task SetLastSuccessfulEnumerationUtcAsync(DateTime utc)
        {
            try
            {
                using var context = _database.CreateContext();
                await context.Database.ExecuteSqlRawAsync(
                    "INSERT OR REPLACE INTO __EnumerationMetadata (Key, Value) VALUES ({0}, {1})",
                    LastSuccessfulEnumerationKey,
                    utc.ToUniversalTime().ToString("o"));
            }
            catch (Exception)
            {
                // Swallow-only: a failed write leaves the previous timestamp in
                // place (or none), which only makes the next freshness check more
                // conservative. Never abort the post-enumeration path over metadata.
            }
        }

        /// <summary>
        /// Writes the same enumeration watermark for each given canonical root.
        /// Production writes go through
        /// <see cref="SetEnumerationWatermarkAtomicallyAsync"/>; this overload is
        /// retained as a test seeding helper for per-root watermark states
        /// (HPA-801). Roots omitted from the list keep their previous watermark.
        /// <para>
        /// Each root key is built with <see cref="SongPathIdentity.GetStableRootKey"/>
        /// so that a casing-only change in configuration cannot make a root's
        /// watermark appear absent on case-insensitive platforms (Windows, macOS).
        /// </para>
        /// </summary>
        public async Task SetLastSuccessfulEnumerationUtcAsync(
            IReadOnlyList<string> canonicalRoots,
            DateTime utc)
        {
            if (canonicalRoots == null || canonicalRoots.Count == 0)
                return;

            try
            {
                using var context = _database.CreateContext();
                var value = utc.ToUniversalTime().ToString("o");
                foreach (var canonicalRoot in canonicalRoots)
                {
                    if (!SongPathIdentity.TryNormalize(canonicalRoot, out var normalizedRoot))
                        continue;

                    await context.Database.ExecuteSqlRawAsync(
                        "INSERT OR REPLACE INTO __EnumerationMetadata (Key, Value) VALUES ({0}, {1})",
                        BuildRootWatermarkKey(normalizedRoot),
                        value);
                }
            }
            catch (Exception)
            {
                // A failed root watermark leaves the previous value in place (or
                // none), so the next freshness check remains conservative.
            }
        }

        /// <summary>
        /// Key prefix for the per-root count of tab files the last scan
        /// rejected for having no playable drum part (HPA-693). The freshness
        /// check adds it to the database chart count, because the chart
        /// inventory counts those files but they get no chart row.
        /// </summary>
        private const string RejectedChartCountRootKeyPrefix = "RejectedChartCount:Root:";

        private static string BuildRejectedChartCountKey(string normalizedRoot) =>
            RejectedChartCountRootKeyPrefix +
                SongPathIdentity.GetStableRootKey(normalizedRoot);

        /// <summary>
        /// Stores how many tabs the scan rejected under each root. A failed
        /// write is conservative: the next freshness check sees a count
        /// mismatch and rescans.
        /// </summary>
        public async Task SetRejectedChartCountsAsync(
            IReadOnlyDictionary<string, int> countsByRoot)
        {
            try
            {
                using var context = _database.CreateContext();
                foreach (var (root, count) in countsByRoot)
                {
                    if (!SongPathIdentity.TryNormalize(root, out var normalizedRoot))
                        continue;

                    await context.Database.ExecuteSqlRawAsync(
                        "INSERT OR REPLACE INTO __EnumerationMetadata (Key, Value) VALUES ({0}, {1})",
                        BuildRejectedChartCountKey(normalizedRoot),
                        count.ToString(CultureInfo.InvariantCulture));
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Total rejected tab count stored for <paramref name="canonicalRoots"/>;
        /// a root with no stored count adds 0.
        /// </summary>
        public async Task<int> GetRejectedChartCountAsync(
            IReadOnlyList<string> canonicalRoots)
        {
            try
            {
                using var context = _database.CreateContext();
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var root in canonicalRoots)
                {
                    if (SongPathIdentity.TryNormalize(root, out var normalizedRoot))
                        keys.Add(BuildRejectedChartCountKey(normalizedRoot));
                }

                var total = 0;
                foreach (var key in keys)
                {
                    var values = await context.Database.SqlQueryRaw<string>(
                        "SELECT Value FROM __EnumerationMetadata WHERE Key = {0} LIMIT 1",
                        key).ToListAsync();
                    if (int.TryParse(
                            values.FirstOrDefault(),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var count))
                    {
                        total += count;
                    }
                }

                return total;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// Atomically persists the global enumeration watermark and every
        /// active-root watermark in a single database transaction. Either all
        /// values are written or none are — a crash or failure mid-write cannot
        /// leave the global timestamp committed while per-root rows are absent.
        /// <para>
        /// The read path treats a missing per-root watermark as "needs
        /// enumeration" (a conservative full rescan). Without atomicity, a
        /// partial write (global committed, per-root missing) would force that
        /// conservative rescan on every startup until per-root rows catch up.
        /// This method eliminates that window by committing both the global key
        /// and every per-root key together.
        /// </para>
        /// <para>
        /// Returns <c>true</c> on success, <c>false</c> on failure. A failed
        /// write leaves the previous watermark in place (or none), which only
        /// makes the next freshness check more conservative — it never creates
        /// a partial state.
        /// </para>
        /// </summary>
        public async Task<bool> SetEnumerationWatermarkAtomicallyAsync(
            DateTime utc,
            IReadOnlyList<string> canonicalRoots)
        {
            try
            {
                using var context = _database.CreateContext();
                await using var transaction =
                    await context.Database.BeginTransactionAsync().ConfigureAwait(false);

                var value = utc.ToUniversalTime().ToString("o");

                await context.Database.ExecuteSqlRawAsync(
                    "INSERT OR REPLACE INTO __EnumerationMetadata (Key, Value) VALUES ({0}, {1})",
                    LastSuccessfulEnumerationKey,
                    value).ConfigureAwait(false);

                if (canonicalRoots != null && canonicalRoots.Count > 0)
                {
                    foreach (var canonicalRoot in canonicalRoots)
                    {
                        if (!SongPathIdentity.TryNormalize(canonicalRoot, out var normalizedRoot))
                            continue;

                        await context.Database.ExecuteSqlRawAsync(
                            "INSERT OR REPLACE INTO __EnumerationMetadata (Key, Value) VALUES ({0}, {1})",
                            BuildRootWatermarkKey(normalizedRoot),
                            value).ConfigureAwait(false);
                    }
                }

                await transaction.CommitAsync().ConfigureAwait(false);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

    }
}
