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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using SongScoreEntity = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Game.Lib.Song.Entities
{
    /// Comprehensive service for managing the song database initialization and operations
    /// Provides both low-level database management and high-level CRUD operations
    public class SongDatabaseService : IDisposable
    {
        private readonly string _databasePath;
        private readonly DbContextOptions<SongDbContext> _options;
        private readonly object _initializationLock = new object();
        private readonly SemaphoreSlim _initializationSemaphore = new SemaphoreSlim(1, 1);
        private bool _isInitialized = false;

        /// <summary>
        /// Gets the path to the database file
        /// </summary>
        public string DatabasePath => _databasePath;

        public SongDatabaseService(string databasePath = "songs.db")
        {
            _databasePath = databasePath;

            var databaseDirectory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(databaseDirectory))
            {
                Directory.CreateDirectory(databaseDirectory);
            }

            var optionsBuilder = new DbContextOptionsBuilder<SongDbContext>();
            // Configure SQLite with UTF-8 support for Japanese text
            // Include explicit UTF-8 encoding in connection string
            var connectionString = $"Data Source={_databasePath};Cache=Shared;";
            optionsBuilder.UseSqlite(connectionString, options =>
            {
                options.CommandTimeout(30);
            });
            _options = optionsBuilder.Options;
        }

        /// <summary>
        /// Test-friendly constructor that accepts pre-built DbContext options.
        /// Lets unit tests inject an in-memory SQLite connection without going through
        /// the file-path-based config path. The caller is responsible for ensuring the
        /// schema is created (e.g., via SongDbContext.Database.EnsureCreated()).
        /// </summary>
        internal SongDatabaseService(DbContextOptions<SongDbContext> options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _databasePath = string.Empty;
            // Tests provide a pre-created schema; mark the service as initialized so
            // CreateContext() does not throw the "must initialize first" guard.
            _isInitialized = true;
        }


        /// Initialize the database and ensure it exists
        public async Task InitializeDatabaseAsync()
        {
            await _initializationSemaphore.WaitAsync();
            try
            {
                lock (_initializationLock)
                {
                    if (_isInitialized)
                    {
                        System.Diagnostics.Debug.WriteLine("SongDatabaseService: Database already initialized, skipping.");
                        return;
                    }
                }

                // Check if file exists but is not a valid SQLite database
                if (File.Exists(_databasePath) && !await IsValidSqliteDatabaseAsync())
                {
                    await HandleInvalidDatabaseFileAsync();
                }

                // Check if database exists but lacks proper Unicode configuration for Japanese text
                if (File.Exists(_databasePath) && !await HasProperUnicodeConfigurationAsync())
                {
                    System.Diagnostics.Debug.WriteLine("SongDatabaseService: Database lacks proper Unicode configuration, recreating for Japanese text support");
                    await HandleInvalidDatabaseFileAsync();
                }

                using var context = new SongDbContext(_options);

                // Ensure database is created first
                await context.Database.EnsureCreatedAsync();

                // Configure UTF-8 encoding AFTER tables are created
                await ConfigureUtf8EncodingAsync(context);

                // Mark as initialized after successful creation
                lock (_initializationLock)
                {
                    _isInitialized = true;
                }

                System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Database initialized successfully at: {_databasePath}");
            }
            catch (Exception ex) when (ex.Message.Contains("file is not a database") || ex.Message.Contains("not a database"))
            {
                // Handle SQLite-specific "file is not a database" errors
                System.Diagnostics.Debug.WriteLine($"Database corruption error during initialization: {ex.Message}");
                System.Diagnostics.Debug.WriteLine("Attempting to recover from corrupted database...");

                await HandleInvalidDatabaseFileAsync();

                // Retry initialization after fixing the invalid file
                using var context = new SongDbContext(_options);
                
                await context.Database.EnsureCreatedAsync();
                
                // Configure UTF-8 encoding AFTER creating tables
                await ConfigureUtf8EncodingAsync(context);

                lock (_initializationLock)
                {
                    _isInitialized = true;
                }
                System.Diagnostics.Debug.WriteLine("Database recovery successful - fresh database created.");
            }
            catch (Exception ex) when (ex.Message.Contains("table") && ex.Message.Contains("already exists"))
            {
                // Handle "table already exists" errors - this means the database is already initialized
                System.Diagnostics.Debug.WriteLine($"Database tables already exist: {ex.Message}");
                lock (_initializationLock)
                {
                    _isInitialized = true;
                }
                System.Diagnostics.Debug.WriteLine("Database initialization skipped - tables already exist.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unexpected error during database initialization: {ex.Message}");
                throw;
            }
            finally
            {
                _initializationSemaphore.Release();
            }
        }


        /// Create a new DbContext instance
        public SongDbContext CreateContext()
        {
            // Ensure database is initialized before creating context
            lock (_initializationLock)
            {
                if (!_isInitialized)
                {
                    throw new InvalidOperationException("Database must be initialized before creating contexts. Call InitializeDatabaseAsync() first.");
                }
            }
            return new SongDbContext(_options);
        }


        /// Check if the database exists and is accessible
        public async Task<bool> DatabaseExistsAsync()
        {
            using var context = new SongDbContext(_options);
            return await context.Database.CanConnectAsync();
        }


        /// Get database file size in bytes
        public long GetDatabaseSize()
        {
            if (File.Exists(_databasePath))
            {
                return new FileInfo(_databasePath).Length;
            }
            return 0;
        }

        /// <summary>
        /// Purge the database file completely (for fresh rebuild)
        /// </summary>
        public async Task PurgeDatabaseAsync()
        {
            try
            {
                // Close any existing connections first
                using (var context = new SongDbContext(_options))
                {
                    await context.Database.CloseConnectionAsync();
                }

                // Clear all pooled SQLite connections so the file is no longer locked
                SqliteConnection.ClearAllPools();

                // Delete the database file completely
                if (File.Exists(_databasePath))
                {
                    File.Delete(_databasePath);
                    System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Database file purged: {_databasePath}");
                }

                // Reset initialization flag
                lock (_initializationLock)
                {
                    _isInitialized = false;
                }

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Error purging database: {ex.Message}");
                throw;
            }
        }


        /// Backup the database to a specified path
        public async Task BackupDatabaseAsync(string backupPath)
        {
            if (File.Exists(_databasePath))
            {
                await Task.Run(() => File.Copy(_databasePath, backupPath, overwrite: true));
            }
            else
            {
                throw new FileNotFoundException($"Database file not found: {_databasePath}");
            }
        }


        /// Restore database from a backup
        public async Task RestoreDatabaseAsync(string backupPath)
        {
            if (!File.Exists(backupPath))
            {
                throw new FileNotFoundException($"Backup file not found: {backupPath}");
            }

            // Close any existing connections and clear pooled connections
            using (var context = new SongDbContext(_options))
            {
                await context.Database.CloseConnectionAsync();
            }

            SqliteConnection.ClearAllPools();

            // Copy backup over current database
            await Task.Run(() => File.Copy(backupPath, _databasePath, overwrite: true));
        }
        /// Get database statistics
        public async Task<DatabaseStats> GetDatabaseStatsAsync()
        {
            using var context = new SongDbContext(_options);

            var stats = new DatabaseStats
            {
                SongCount = await context.Songs.CountAsync(),
                DifficultyCount = await context.SongCharts.CountAsync(),
                ScoreCount = await context.SongScores.CountAsync(),
                HierarchyNodeCount = await context.SongHierarchy.CountAsync(),
                PerformanceHistoryCount = await context.PerformanceHistory.CountAsync(),
                DatabaseSizeBytes = GetDatabaseSize()
            };

            return stats;
        }

        // Song Management Operations        
        // Legacy AddSongAsync(SongMetadata) method removed - use AddSongAsync(Song, SongChart) instead

        /// <summary>
        /// Add a new song with charts and scores using EF Core entities
        /// Handles duplicate detection based on file path
        /// </summary>
        public async Task<int> AddSongAsync(SongEntity song, SongChart chart)
        {
            // Ensure initialization is complete
            lock (_initializationLock)
            {
                if (!_isInitialized)
                {
                    throw new InvalidOperationException("Database must be initialized before adding songs. Call InitializeDatabaseAsync() first.");
                }
            }

            try
            {
                using var context = CreateContext();

                // Check if a chart with this file path already exists
                var existingChart = await context.SongCharts
                    .Include(c => c.Song)
                    .FirstOrDefaultAsync(c => c.FilePath == chart.FilePath);

                if (existingChart != null)
                {
                    // Song already exists. Hydrate the caller's parsed entity with the
                    // persisted id and bookmark so in-memory nodes built from it reflect
                    // real DB state after a rescan. Without the bookmark copy, a bookmarked
                    // song re-parsed during enumeration keeps its default IsBookmarked ==
                    // false, hiding the star marker and inverting the B-key toggle (it sets
                    // the bookmark instead of clearing it). The new-song branch below gets
                    // the id for free via EF change-tracking; this branch mirrors that.
                    song.Id = existingChart.SongId;
                    if (existingChart.Song != null)
                        song.IsBookmarked = existingChart.Song.IsBookmarked;
                    return existingChart.SongId;
                }

                // Check if a song with the same title and artist already exists
                var existingSong = await context.Songs
                    .FirstOrDefaultAsync(s => s.Title == song.Title && s.Artist == song.Artist);

                if (existingSong != null)
                {
                    // Song exists, add chart to existing song. Mirror the persisted id and
                    // bookmark onto the caller's parsed entity for the same reason as the
                    // duplicate-file-path branch above.
                    song.Id = existingSong.Id;
                    song.IsBookmarked = existingSong.IsBookmarked;

                    // Calculate file hash if not already set
                    if (string.IsNullOrEmpty(chart.FileHash) && !string.IsNullOrEmpty(chart.FilePath))
                    {
                        chart.FileHash = CalculateFileHash(chart.FilePath);
                    }

                    // Link chart to existing song
                    chart.SongId = existingSong.Id;
                    chart.Song = existingSong;

                    context.SongCharts.Add(chart);
                    await context.SaveChangesAsync();

                    // Create initial score records for each available instrument
                    if (chart.HasDrumChart && chart.DrumLevel > 0)
                    {
                        await AddScoreRecordAsync(context, chart.Id, EInstrumentPart.DRUMS);
                    }
                    if (chart.HasGuitarChart && chart.GuitarLevel > 0)
                    {
                        await AddScoreRecordAsync(context, chart.Id, EInstrumentPart.GUITAR);
                    }
                    if (chart.HasBassChart && chart.BassLevel > 0)
                    {
                        await AddScoreRecordAsync(context, chart.Id, EInstrumentPart.BASS);
                    }

                    return existingSong.Id;
                }

                // No existing song found, create a new one

                // Calculate file hash if not already set
                if (string.IsNullOrEmpty(chart.FileHash) && !string.IsNullOrEmpty(chart.FilePath))
                {
                    chart.FileHash = CalculateFileHash(chart.FilePath);
                }

                // Add the song entity
                context.Songs.Add(song);
                await context.SaveChangesAsync();

                // Link chart to song and add to context
                chart.SongId = song.Id;
                chart.Song = song;

                context.SongCharts.Add(chart);
                await context.SaveChangesAsync();

                // Create initial score records for each available instrument
                if (chart.HasDrumChart && chart.DrumLevel > 0)
                {
                    await AddScoreRecordAsync(context, chart.Id, EInstrumentPart.DRUMS);
                }
                if (chart.HasGuitarChart && chart.GuitarLevel > 0)
                {
                    await AddScoreRecordAsync(context, chart.Id, EInstrumentPart.GUITAR);
                }
                if (chart.HasBassChart && chart.BassLevel > 0)
                {
                    await AddScoreRecordAsync(context, chart.Id, EInstrumentPart.BASS);
                }

                return song.Id;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Error in AddSongAsync: {ex.Message}");
                throw;
            }
        }

        /// Search songs by title or artist
        public async Task<List<SongEntity>> SearchSongsAsync(string searchTerm)
        {
            using var context = CreateContext();

            return await context.Songs
                .Where(s => s.Title.Contains(searchTerm) || s.Artist.Contains(searchTerm))
                .Include(s => s.Charts)
                .OrderBy(s => s.Title)
                .ToListAsync();
        }

        /// <summary>
        /// Get all songs from the database
        /// </summary>
        public async Task<List<SongEntity>> GetSongsAsync()
        {
            using var context = CreateContext();

            return await context.Songs
                .Include(s => s.Charts)
                    .ThenInclude(c => c.Scores)
                        .ThenInclude(sc => sc.PerformanceHistory)
                .OrderBy(s => s.Title)
                .ToListAsync();
        }

        /// Get songs by genre
        public async Task<List<SongEntity>> GetSongsByGenreAsync(string genre)
        {
            using var context = CreateContext();

            return await context.Songs
                .Where(s => s.Genre == genre)
                .Include(s => s.Charts)
                .OrderBy(s => s.Title)
                .ToListAsync();
        }

        /// Get a song by ID with all its charts (including persisted scores)
        public async Task<(SongEntity song, SongChart[] charts)?> GetSongWithChartsAsync(int songId)
        {
            using var context = CreateContext();

            var song = await context.Songs
                .Include(s => s.Charts)
                    .ThenInclude(c => c.Scores)
                        .ThenInclude(sc => sc.PerformanceHistory)
                .FirstOrDefaultAsync(s => s.Id == songId);

            if (song == null)
                return null;

            return (song, song.Charts.ToArray());
        }

        /// Update a song score
        public async Task UpdateScoreAsync(int chartId, EInstrumentPart instrument, int newScore, double achievementRate, bool fullCombo)
        {
            using var context = CreateContext();

            var score = await context.SongScores
                .FirstOrDefaultAsync(
                    s => s.ChartId == chartId
                        && s.Instrument == instrument
                        && s.PlaySpeedPercent == PlaySpeedRange.Default);

            if (score != null)
            {
                // Update if this is a new best score
                if (newScore > score.BestScore)
                {
                    score.BestScore = newScore;
                    score.BestAchievementRate = achievementRate;
                    score.FullCombo = score.FullCombo || fullCombo;
                }

                score.LastScore = newScore;
                score.LastPlayedAt = DateTime.UtcNow;
                score.PlayCount++;

                if (newScore > 0) // Assuming any score > 0 means cleared
                {
                    score.ClearCount++;
                }

                await context.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Persists a complete PerformanceSummary into the SongScore for the given chart+instrument.
        /// Updates best fields only when the new score exceeds the existing best. Always updates the
        /// "last play" fields and increments PlayCount. The pre-computed summary.GameSkill is assigned
        /// directly (not re-derived via score.CalculateSkill()), preserving level-decimal precision.
        /// </summary>
        public async Task<ScoreSaveResult> UpdateScoreAsync(
            int chartId,
            EInstrumentPart instrument,
            PerformanceSummary summary,
            CancellationToken cancellationToken = default)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            if (summary.PlaySpeedPercent < PlaySpeedRange.Min
                || summary.PlaySpeedPercent > PlaySpeedRange.Max
                || summary.PlaySpeedPercent % PlaySpeedRange.Step != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(summary),
                    summary.PlaySpeedPercent,
                    "PerformanceSummary.PlaySpeedPercent must be a canonical gameplay speed.");
            }

            // Pre-feature callers did not carry a run id. Preserve those direct service
            // calls as non-idempotent legacy writes while all modern gameplay summaries
            // use their frozen, non-empty RunId.
            var runId = summary.RunId == Guid.Empty
                ? Guid.NewGuid()
                : summary.RunId;

            try
            {
                return await SaveScoreTransactionAsync(
                    chartId,
                    instrument,
                    summary,
                    runId,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsSqliteWriteRace(ex))
            {
                var resolved = await ResolveReceiptAfterRaceAsync(
                    runId,
                    chartId,
                    instrument,
                    summary.PlaySpeedPercent,
                    cancellationToken).ConfigureAwait(false);
                if (resolved != null)
                    return resolved;
                throw;
            }
        }

        private async Task<ScoreSaveResult> SaveScoreTransactionAsync(
            int chartId,
            EInstrumentPart instrument,
            PerformanceSummary summary,
            Guid runId,
            CancellationToken cancellationToken)
        {
            using var context = CreateContext();
            await using var transaction = await context.Database.BeginTransactionAsync(
                cancellationToken);

            // Receipt identity is deliberately checked before chart or score lookup.
            // This keeps retries valid after stale cleanup removes the related score
            // and nulls the optional SongScoreId foreign key.
            var existingReceipt = await context.ScoreSaveReceipts
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    receipt => receipt.RunId == runId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existingReceipt != null)
            {
                return ResolveExistingReceipt(
                    existingReceipt,
                    chartId,
                    instrument,
                    summary.PlaySpeedPercent);
            }

            var score = await context.SongScores
                .Include(s => s.Chart)
                .FirstOrDefaultAsync(
                    s => s.ChartId == chartId
                        && s.Instrument == instrument
                        && s.PlaySpeedPercent == summary.PlaySpeedPercent,
                    cancellationToken)
                .ConfigureAwait(false);
            if (score == null)
            {
                var chart = await context.SongCharts.FirstOrDefaultAsync(
                    chart => chart.Id == chartId,
                    cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new KeyNotFoundException($"Chart with Id {chartId} was not found.");
                score = new SongScoreEntity
                {
                    ChartId = chartId,
                    Chart = chart,
                    Instrument = instrument,
                    PlaySpeedPercent = summary.PlaySpeedPercent
                };
                context.SongScores.Add(score);
            }

            var isFirstPlay = score.PlayCount == 0;
            if (isFirstPlay || summary.Score > score.BestScore)
            {
                score.BestScore = summary.Score;
                score.BestPerfect = summary.PerfectCount;
                score.BestGreat = summary.GreatCount;
                score.BestGood = summary.GoodCount;
                score.BestPoor = summary.PoorCount;
                score.BestMiss = summary.MissCount;
                score.TotalNotes = summary.TotalNotes;
            }

            var normalizedRunRank = SongScoreEntity.NormalizeRankPercentage(
                (int)Math.Floor(summary.PlayingSkill));
            var normalizedBestRank = isFirstPlay
                ? 0
                : SongScoreEntity.NormalizeStoredBestRank(score.BestRank);
            if (isFirstPlay || normalizedRunRank > normalizedBestRank)
                score.BestRank = normalizedRunRank;
            else
                score.BestRank = normalizedBestRank;

            score.MaxCombo = Math.Max(score.MaxCombo, summary.MaxCombo);
            score.FullCombo = score.FullCombo ||
                (summary.ClearFlag &&
                 summary.MissCount == 0 &&
                 summary.PoorCount == 0);

            if (summary.GameSkill > score.HighSkill)
            {
                score.HighSkill = summary.GameSkill;
                // NX stores the achievement rate (達成率 / playing skill, 0-100) on a new skill record
                // (CStageResult: HighSkill[m] = dbPerformanceSkill when bNewRecordSkill). The song-select
                // panel reads this as the per-cell percentage, so it must be persisted alongside HighSkill.
                score.BestAchievementRate = summary.PlayingSkill;
            }
            score.SongSkill = summary.GameSkill;

            score.LastScore = summary.Score;
            score.LastSkillPoint = summary.GameSkill;
            var nowUtc = DateTime.UtcNow;
            score.LastPlayedAt = nowUtc;
            score.PlayCount++;
            if (summary.ClearFlag) score.ClearCount++;

            await context.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);

            var localDate = nowUtc.ToLocalTime();
            var rank = SongScoreEntity.RankString((int)Math.Floor(summary.PlayingSkill));
            var status = summary.ClearFlag ? "Cleared" : "Failed";
            var line = string.Format(
                CultureInfo.InvariantCulture,
                "{0}.{1:yy/M/d} {2} ({3}: {4:F2}) [{5}, {6}]",
                score.PlayCount,
                localDate,
                status,
                rank,
                summary.PlayingSkill,
                PlaySpeedRange.Format(summary.PlaySpeedPercent),
                PitchRange.Format(summary.PitchSemitones));

            await PerformanceHistoryMerger.MergeAsync(
                context,
                score.Chart.SongId,
                score.Id,
                new[]
                {
                    new PerformanceHistoryCandidate(
                        line,
                        nowUtc,
                        summary.PitchSemitones)
                },
                cancellationToken).ConfigureAwait(false);

            context.ScoreSaveReceipts.Add(new ScoreSaveReceipt
            {
                RunId = runId,
                ChartId = chartId,
                Instrument = instrument,
                PlaySpeedPercent = summary.PlaySpeedPercent,
                SongScoreId = score.Id,
                SavedAtUtc = nowUtc
            });

            await context.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken)
                .ConfigureAwait(false);
            return ScoreSaveResult.Saved(score.Id);
        }

        private static ScoreSaveResult ResolveExistingReceipt(
            ScoreSaveReceipt receipt,
            int chartId,
            EInstrumentPart instrument,
            int playSpeedPercent)
        {
            if (receipt.ChartId == chartId
                && receipt.Instrument == instrument
                && receipt.PlaySpeedPercent == playSpeedPercent)
            {
                return ScoreSaveResult.AlreadySaved(receipt.SongScoreId);
            }

            return ScoreSaveResult.Failed(
                $"RunId collision: {receipt.RunId} is already stored for " +
                $"chart {receipt.ChartId}, instrument {receipt.Instrument}, " +
                $"speed {receipt.PlaySpeedPercent}, not chart {chartId}, " +
                $"instrument {instrument}, speed {playSpeedPercent}.");
        }

        private async Task<ScoreSaveResult> ResolveReceiptAfterRaceAsync(
            Guid runId,
            int chartId,
            EInstrumentPart instrument,
            int playSpeedPercent,
            CancellationToken cancellationToken)
        {
            const int maxAttempts = 20;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var context = CreateContext();
                    var receipt = await context.ScoreSaveReceipts
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            candidate => candidate.RunId == runId,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (receipt != null)
                    {
                        return ResolveExistingReceipt(
                            receipt,
                            chartId,
                            instrument,
                            playSpeedPercent);
                    }
                }
                catch (SqliteException ex) when (
                    ex.SqliteErrorCode == 5
                    || ex.SqliteErrorCode == 6)
                {
                    // The winning transaction still owns the database lock.
                }

                await Task.Delay(
                    TimeSpan.FromMilliseconds(10 + (attempt * 5)),
                    cancellationToken).ConfigureAwait(false);
            }

            return null;
        }

        private static bool IsSqliteWriteRace(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is SqliteException sqlite
                    && (sqlite.SqliteErrorCode == 5
                        || sqlite.SqliteErrorCode == 6
                        || sqlite.SqliteExtendedErrorCode == 1555
                        || sqlite.SqliteExtendedErrorCode == 2067))
                {
                    return true;
                }
            }

            return false;
        }

        /// Get top scores for a specific instrument
        public async Task<List<SongScoreEntity>> GetTopScoresAsync(EInstrumentPart instrument, int limit = 10)
        {
            return await GetTopScoresForSpeedAsync(
                instrument,
                PlaySpeedRange.Default,
                limit).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets top scores for one explicit gameplay-speed bucket.
        /// </summary>
        public async Task<List<SongScoreEntity>> GetTopScoresForSpeedAsync(
            EInstrumentPart instrument,
            int playSpeedPercent,
            int limit = 10)
        {
            using var context = CreateContext();

            return await context.SongScores
                .Where(s => s.Instrument == instrument
                    && s.PlaySpeedPercent == playSpeedPercent
                    && s.BestScore > 0)
                .Include(s => s.Chart)
                .ThenInclude(c => c.Song)
                .OrderByDescending(s => s.BestScore)
                .Take(limit)
                .ToListAsync();
        }

        /// <summary>
        /// Loads a single <see cref="SongScore"/> with its <see cref="SongScore.PerformanceHistory"/>
        /// collection eagerly loaded. Used by <see cref="SongManager"/> to refresh the
        /// in-memory score cache after a score update without rebuilding the entire
        /// song-list tree. This overload is retained only for legacy callers and reads
        /// the default 100% speed variant.
        /// </summary>
        public async Task<SongScoreEntity> GetScoreWithHistoryAsync(int chartId, EInstrumentPart instrument)
        {
            return await GetScoreWithHistoryAsync(
                chartId,
                instrument,
                playSpeedPercent: 100,
                CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Loads one exact chart/instrument/speed score variant with its own history.
        /// Returns null when that specific speed has not been played; another speed's
        /// score is never substituted.
        /// </summary>
        public async Task<SongScoreEntity> GetScoreWithHistoryAsync(
            int chartId,
            EInstrumentPart instrument,
            int playSpeedPercent,
            CancellationToken cancellationToken = default)
        {
            using var context = CreateContext();

            return await context.SongScores
                .Include(s => s.Chart)
                .Include(s => s.PerformanceHistory)
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    s => s.ChartId == chartId
                        && s.Instrument == instrument
                        && s.PlaySpeedPercent == playSpeedPercent,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Returns the most-recently-played songs, one row per song. A song with multiple
        /// difficulty charts is collapsed to a single entry using the maximum LastPlayedAt
        /// across its charts. Songs that have never been played (no score with a LastPlayedAt)
        /// are excluded. Results are ordered newest-first and limited to <paramref name="limit"/>.
        /// Recency intentionally aggregates across speeds at the song level. Each returned
        /// Song retains every eagerly loaded score variant and each variant's pitch-bearing
        /// history, so materialization does not collapse chart/instrument/speed identity.
        /// </summary>
        public async Task<List<SongEntity>> GetRecentlyPlayedSongsAsync(int limit = 20)
        {
            if (limit <= 0) return new List<SongEntity>();

            using var context = CreateContext();

            // Push grouping + ordering + limit into SQL so we transfer at most `limit`
            // integer IDs back to the client instead of every played SongScore row.
            // The IDs are then used in a second query to load full Song+Chart+Score
            // graphs, and manually reordered client-side (IN does not preserve order).
            var orderedIds = await context.SongScores
                .Where(s => s.LastPlayedAt != null)
                .Select(s => new { s.Chart.SongId, s.LastPlayedAt })
                .GroupBy(s => s.SongId)
                .Select(g => new { SongId = g.Key, LastPlayed = g.Max(s => s.LastPlayedAt) })
                .OrderByDescending(x => x.LastPlayed)
                .Take(limit)
                .Select(x => x.SongId)
                .ToListAsync();

            if (orderedIds.Count == 0) return new List<SongEntity>();

            var songs = await context.Songs
                .Where(s => orderedIds.Contains(s.Id))
                .Include(s => s.Charts)
                    .ThenInclude(c => c.Scores)
                        .ThenInclude(sc => sc.PerformanceHistory)
                .ToListAsync();

            // Re-order the loaded songs to match the recency ordering (the IN query does
            // not preserve order).
            var byId = songs.ToDictionary(s => s.Id);
            var result = new List<SongEntity>(orderedIds.Count);
            foreach (var id in orderedIds)
            {
                if (byId.TryGetValue(id, out var song))
                    result.Add(song);
            }
            return result;
        }

        /// <summary>
        /// Sets or clears the bookmark flag on a song. No-op if the song id is not found.
        /// </summary>
        public async Task SetBookmarkAsync(int songId, bool bookmarked)
        {
            using var context = CreateContext();
            var song = await context.Songs.FirstOrDefaultAsync(s => s.Id == songId);
            if (song == null) return;
            song.IsBookmarked = bookmarked;
            song.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        /// <summary>
        /// Returns all bookmarked songs ordered alphabetically by Title (Id as a stable
        /// tiebreak). Each Song has its Charts and the charts' Scores eagerly loaded so
        /// callers can build fully-populated SongListNodes, mirroring
        /// <see cref="GetRecentlyPlayedSongsAsync"/>.
        /// </summary>
        public async Task<List<SongEntity>> GetBookmarkedSongsAsync()
        {
            using var context = CreateContext();
            return await context.Songs
                .Where(s => s.IsBookmarked)
                .Include(s => s.Charts)
                    .ThenInclude(c => c.Scores)
                        .ThenInclude(sc => sc.PerformanceHistory)
                .OrderBy(s => s.Title)
                .ThenBy(s => s.Id)
                .ToListAsync();
        }

        /// Add a score record for a chart
        private async Task AddScoreRecordAsync(SongDbContext context, int chartId, EInstrumentPart instrument)
        {
            var score = new SongScoreEntity
            {
                ChartId = chartId,
                Instrument = instrument,
                BestScore = 0,
                BestRank = 0,
                BestSkillPoint = 0.0,
                BestAchievementRate = 0.0,
                FullCombo = false,
                Excellent = false,
                PlayCount = 0,
                ClearCount = 0,
                MaxCombo = 0,
                ProgressBar = "",
                LastScore = 0,
                LastSkillPoint = 0.0
            };

            context.SongScores.Add(score);
            await context.SaveChangesAsync();
        }

        private string CalculateFileHash(string filePath)
        {
            if (!File.Exists(filePath)) return string.Empty;

            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    var hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        /// <summary>
        /// Dispose of resources
        /// </summary>
        public void Dispose()
        {
            // Database connections are managed by DbContext instances and disposed automatically
            // No explicit cleanup needed as we use 'using' statements for context management
        }

        /// <summary>
        /// Check if the database file is a valid SQLite database
        /// </summary>
        private async Task<bool> IsValidSqliteDatabaseAsync()
        {
            if (!File.Exists(_databasePath))
                return false;

            try
            {
                using var context = new SongDbContext(_options);
                return await context.Database.CanConnectAsync();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if the database has proper Unicode/UTF-8 configuration
        /// Returns false if the database needs to be recreated for proper Japanese text support
        /// </summary>
        private async Task<bool> HasProperUnicodeConfigurationAsync()
        {
            try
            {
                using var context = new SongDbContext(_options);
                
                // Check if database can connect first
                if (!await context.Database.CanConnectAsync())
                    return false;

                // Check if our Unicode collation changes have been applied
                // We can do this by checking if a metadata table exists with version info
                var versionTableCount = await context.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__DatabaseVersion'"
                ).ToListAsync();
                
                var hasVersionTable = versionTableCount.FirstOrDefault() > 0;

                if (!hasVersionTable)
                {
                    System.Diagnostics.Debug.WriteLine("SongDatabaseService: Database lacks version table, needs Unicode reconfiguration");
                    return false;
                }

                // Check version number for Unicode support
                var versionResult = await context.Database.SqlQueryRaw<int>(
                    "SELECT COALESCE(Version, 0) FROM __DatabaseVersion WHERE Feature='UnicodeCollation' LIMIT 1"
                ).ToListAsync();
                
                var version = versionResult.FirstOrDefault();

                const int REQUIRED_UNICODE_VERSION = 2;
                if (version < REQUIRED_UNICODE_VERSION)
                {
                    System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Database Unicode version {version} < required {REQUIRED_UNICODE_VERSION}, needs reconfiguration");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Error checking Unicode configuration: {ex.Message}");
                return false; // Assume needs reconfiguration on error
            }
        }

        /// <summary>
        /// Configure UTF-8 encoding for proper Japanese text support
        /// </summary>
        private async Task ConfigureUtf8EncodingAsync(SongDbContext context)
        {
            // UTF-8 pragma configuration is best-effort: most modern SQLite installations
            // default to UTF-8, so a pragma failure here should not abort initialization.
            try
            {
                // Ensure we can write to the database
                await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = DELETE");

                // Set SQLite pragmas for UTF-8 and case-insensitive comparisons
                await context.Database.ExecuteSqlRawAsync("PRAGMA case_sensitive_like = OFF");

                System.Diagnostics.Debug.WriteLine("SongDatabaseService: UTF-8 encoding configured for database");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Warning - Could not configure UTF-8 encoding: {ex.Message}");
                // Continue anyway - most modern SQLite installations default to UTF-8
            }

            // The bookmark-column and NX-import-column migrations below must NOT be swallowed
            // by the UTF-8 best-effort catch above: a failed migration would otherwise leave
            // init reporting success while queries fail later with confusing errors. Note that
            // EnsureDatabaseVersionTableAsync has its own swallow-all catch and therefore does
            // NOT fail fast — only EnsureBookmarkColumnAsync and EnsureNxImportColumnsAsync
            // propagate real errors. They run unguarded so a genuine schema error fails
            // initialization fast.
            // Create/update version table to mark Unicode configuration as configured.
            await EnsureDatabaseVersionTableAsync(context);

            // Additive schema upgrade for existing databases: EnsureCreated never alters an
            // existing schema, so add new columns here, idempotently.
            await EnsureBookmarkColumnAsync(context);

            // Additive schema upgrade: NX-import snapshot columns.
            await EnsureNxImportColumnsAsync(context);

            // Additive schema upgrade: score-scoped play history.
            await EnsurePerformanceHistoryScoreScopeAsync(context);

            // Additive schema upgrade: playback-speed-scoped scores, pitch history,
            // and durable save receipts.
            await EnsurePlaybackSpeedScoreScopeAsync(context);
        }

        /// <summary>
        /// Ensure the database version table exists and mark Unicode collation as configured
        /// </summary>
        private async Task EnsureDatabaseVersionTableAsync(SongDbContext context)
        {
            try
            {
                // Create version table if it doesn't exist
                await context.Database.ExecuteSqlRawAsync(@"
                    CREATE TABLE IF NOT EXISTS __DatabaseVersion (
                        Feature TEXT PRIMARY KEY,
                        Version INTEGER NOT NULL,
                        AppliedAt TEXT NOT NULL
                    )");

                // Insert or update Unicode support version
                await context.Database.ExecuteSqlRawAsync(@"
                    INSERT OR REPLACE INTO __DatabaseVersion (Feature, Version, AppliedAt)
                    VALUES ('UnicodeCollation', 2, datetime('now'))");

                System.Diagnostics.Debug.WriteLine("SongDatabaseService: Database version table updated with Unicode support");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Warning - Could not create version table: {ex.Message}");
            }
        }

        /// <summary>
        /// Ensures the Songs.IsBookmarked column exists. Fresh databases already have it via
        /// EnsureCreated; pre-existing databases get it added here exactly once. Idempotent and
        /// defensive: a duplicate-column error (concurrent caller) is treated as success, but a
        /// genuine migration failure propagates so initialization fails fast rather than leaving
        /// the bookmark schema broken for later queries.
        /// </summary>
        private async Task EnsureBookmarkColumnAsync(SongDbContext context)
        {
            var columnCount = await context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) FROM pragma_table_info('Songs') WHERE name='IsBookmarked'"
            ).ToListAsync();

            if (columnCount.FirstOrDefault() == 0)
            {
                try
                {
                    await context.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE Songs ADD COLUMN IsBookmarked INTEGER NOT NULL DEFAULT 0");
                    System.Diagnostics.Debug.WriteLine("SongDatabaseService: Added Songs.IsBookmarked column");
                }
                // The COUNT guard above already confirmed the column is absent, so the only way
                // to reach this ALTER and still get a "duplicate column" error is a concurrent
                // initializer racing us between the check and the ALTER. Microsoft.Data.Sqlite
                // surfaces SQLite's stable, English-only error messages (SQLite is not localized),
                // so matching on the message here is reliable and more precise than the generic
                // error code (SQLITE_ERROR == 1). Narrowing to SqliteException keeps unrelated
                // faults reaching the rethrow below.
                catch (SqliteException ex) when (ex.Message.Contains("duplicate column"))
                {
                    // Another caller added it concurrently; nothing to do.
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "SongDatabaseService: Failed to add IsBookmarked column during schema migration.", ex);
                }
            }

            // Always ensure the index exists, even on columns added by older versions that
            // predate the index, so bookmark queries stay fast on partially migrated DBs.
            // CREATE INDEX IF NOT EXISTS is idempotent.
            await context.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS IX_Songs_IsBookmarked ON Songs(IsBookmarked)");
        }

        /// <summary>
        /// Ensures SongScores.NxImportedPlayCount / NxImportedClearCount exist. Fresh
        /// databases already have them via EnsureCreated; pre-existing databases get them
        /// added here exactly once. Idempotent; a concurrent duplicate-column error is
        /// treated as success, a genuine failure propagates.
        /// </summary>
        /// <remarks>
        /// Each column is added by a hardcoded call to <see cref="EnsureNxImportColumnAsync"/>
        /// below. The column name is a SQL identifier embedded into the DDL string;
        /// SQLite (and standard SQL) does not allow parameterizing identifiers in DDL,
        /// so the names are passed as hardcoded literals. Do not extend this method to
        /// iterate over user-supplied names.
        /// </remarks>
        private async Task EnsureNxImportColumnsAsync(SongDbContext context)
        {
            await EnsureNxImportColumnAsync(context, "NxImportedPlayCount");
            await EnsureNxImportColumnAsync(context, "NxImportedClearCount");
        }

        private static async Task EnsureNxImportColumnAsync(SongDbContext context, string column)
        {
            var columnCount = await context.Database.SqlQueryRaw<int>(
                $"SELECT COUNT(*) FROM pragma_table_info('SongScores') WHERE name='{column}'"
            ).ToListAsync();

            if (columnCount.FirstOrDefault() != 0)
                return;

            try
            {
                await context.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE SongScores ADD COLUMN {column} INTEGER NOT NULL DEFAULT 0");
                System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Added SongScores.{column} column");
            }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column"))
            {
                // Concurrent initializer added it; nothing to do.
                System.Diagnostics.Debug.WriteLine($"SongDatabaseService: {column} column already exists (concurrent race)");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"SongDatabaseService: Failed to add {column} column during schema migration.", ex);
            }
        }

        private static async Task EnsurePerformanceHistoryScoreScopeAsync(SongDbContext context)
        {
            var columnCount = await context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) FROM pragma_table_info('PerformanceHistory') WHERE name='SongScoreId'"
            ).ToListAsync();

            if (columnCount.FirstOrDefault() == 0)
            {
                try
                {
                    await context.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE PerformanceHistory ADD COLUMN SongScoreId INTEGER NULL");
                    System.Diagnostics.Debug.WriteLine("SongDatabaseService: Added PerformanceHistory.SongScoreId column");
                }
                catch (SqliteException ex) when (ex.Message.Contains("duplicate column"))
                {
                    System.Diagnostics.Debug.WriteLine("SongDatabaseService: PerformanceHistory.SongScoreId already exists");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "SongDatabaseService: Failed to add PerformanceHistory.SongScoreId during schema migration.", ex);
                }
            }

            var scoreSetNullFkCount = await context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) FROM pragma_foreign_key_list('PerformanceHistory') " +
                "WHERE [table]='SongScores' AND [from]='SongScoreId' AND [on_delete]='SET NULL'"
            ).ToListAsync();

            if (scoreSetNullFkCount.FirstOrDefault() == 0)
            {
                await RebuildPerformanceHistoryTableWithScoreScopeAsync(context);
            }

            await context.Database.ExecuteSqlRawAsync(
                "DROP INDEX IF EXISTS IX_PerformanceHistory_SongId_DisplayOrder");
            await context.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS IX_PerformanceHistory_SongId ON PerformanceHistory(SongId)");
            await context.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_PerformanceHistory_SongScoreId_DisplayOrder " +
                "ON PerformanceHistory(SongScoreId, DisplayOrder) WHERE SongScoreId IS NOT NULL");
        }

        private static async Task RebuildPerformanceHistoryTableWithScoreScopeAsync(SongDbContext context)
        {
            var pitchColumnCount = await context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) FROM pragma_table_info('PerformanceHistory') WHERE name='PitchSemitones'"
            ).ToListAsync();
            var pitchSelectExpression = pitchColumnCount.FirstOrDefault() == 0
                ? "0"
                : "PitchSemitones";

            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
            try
            {
                await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS PerformanceHistory_new");
                await context.Database.ExecuteSqlRawAsync(@"
                    CREATE TABLE PerformanceHistory_new (
                        Id INTEGER NOT NULL CONSTRAINT PK_PerformanceHistory PRIMARY KEY AUTOINCREMENT,
                        SongId INTEGER NOT NULL,
                        SongScoreId INTEGER NULL,
                        PerformedAt TEXT NOT NULL,
                        HistoryLine TEXT NOT NULL,
                        DisplayOrder INTEGER NOT NULL,
                        PitchSemitones INTEGER NOT NULL DEFAULT 0,
                        CONSTRAINT FK_PerformanceHistory_Songs_SongId
                            FOREIGN KEY (SongId) REFERENCES Songs (Id) ON DELETE CASCADE,
                        CONSTRAINT FK_PerformanceHistory_SongScores_SongScoreId
                            FOREIGN KEY (SongScoreId) REFERENCES SongScores (Id) ON DELETE SET NULL
                    )");
                await context.Database.ExecuteSqlRawAsync($@"
                    INSERT INTO PerformanceHistory_new (
                        Id,
                        SongId,
                        SongScoreId,
                        PerformedAt,
                        HistoryLine,
                        DisplayOrder,
                        PitchSemitones)
                    SELECT
                        Id,
                        SongId,
                        CASE
                            WHEN SongScoreId IS NOT NULL
                                AND EXISTS (SELECT 1 FROM SongScores WHERE SongScores.Id = PerformanceHistory.SongScoreId)
                            THEN SongScoreId
                            ELSE NULL
                        END,
                        PerformedAt,
                        HistoryLine,
                        DisplayOrder,
                        {pitchSelectExpression}
                    FROM PerformanceHistory");
                await context.Database.ExecuteSqlRawAsync("DROP TABLE PerformanceHistory");
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE PerformanceHistory_new RENAME TO PerformanceHistory");
            }
            finally
            {
                await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON");
            }
        }

        private static async Task EnsurePlaybackSpeedScoreScopeAsync(SongDbContext context)
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            try
            {
                await EnsureMigrationColumnAsync(
                    context,
                    table: "SongScores",
                    column: "PlaySpeedPercent",
                    definition: "INTEGER NOT NULL DEFAULT 100");
                await EnsureMigrationColumnAsync(
                    context,
                    table: "PerformanceHistory",
                    column: "PitchSemitones",
                    definition: "INTEGER NOT NULL DEFAULT 0");

                // The legacy index prevents a second speed variant for the same
                // chart/instrument. Replace it without rebuilding SongScores so every
                // score id, and therefore every PerformanceHistory foreign key, remains
                // stable.
                await context.Database.ExecuteSqlRawAsync(
                    "DROP INDEX IF EXISTS IX_SongScores_ChartId_Instrument");

                var correctScoreIndexCount = await context.Database.SqlQueryRaw<int>(@"
                    SELECT COUNT(*)
                    FROM pragma_index_list('SongScores') AS index_list
                    WHERE index_list.name='IX_SongScores_ChartId_Instrument_PlaySpeedPercent'
                      AND index_list.[unique]=1
                      AND (SELECT COUNT(*) FROM pragma_index_info(index_list.name))=3
                      AND (SELECT name FROM pragma_index_info(index_list.name) WHERE seqno=0)='ChartId'
                      AND (SELECT name FROM pragma_index_info(index_list.name) WHERE seqno=1)='Instrument'
                      AND (SELECT name FROM pragma_index_info(index_list.name) WHERE seqno=2)='PlaySpeedPercent'"
                ).ToListAsync();

                if (correctScoreIndexCount.FirstOrDefault() == 0)
                {
                    await context.Database.ExecuteSqlRawAsync(
                        "DROP INDEX IF EXISTS IX_SongScores_ChartId_Instrument_PlaySpeedPercent");
                    try
                    {
                        await context.Database.ExecuteSqlRawAsync(
                            "CREATE UNIQUE INDEX IX_SongScores_ChartId_Instrument_PlaySpeedPercent " +
                            "ON SongScores(ChartId, Instrument, PlaySpeedPercent)");
                    }
                    catch (SqliteException ex)
                    {
                        throw new InvalidOperationException(
                            "SongDatabaseService: Cannot create the speed-scoped SongScores index. " +
                            "Existing duplicate rows share ChartId, Instrument, and PlaySpeedPercent; " +
                            "no score data was deleted.",
                            ex);
                    }
                }

                await EnsureScoreSaveReceiptsAsync(context);
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <remarks>
        /// All identifiers passed here are hardcoded migration constants. SQLite does
        /// not support parameterized identifiers, so this helper must never receive
        /// user-controlled table, column, or definition text.
        /// </remarks>
        private static async Task EnsureMigrationColumnAsync(
            SongDbContext context,
            string table,
            string column,
            string definition)
        {
            var columnCount = await context.Database.SqlQueryRaw<int>(
                $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'"
            ).ToListAsync();
            if (columnCount.FirstOrDefault() != 0)
                return;

            try
            {
                await context.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE {table} ADD COLUMN {column} {definition}");
            }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column"))
            {
                // Concurrent initialization completed the same additive step.
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"SongDatabaseService: Failed to add {table}.{column} during schema migration.",
                    ex);
            }
        }

        private static async Task EnsureScoreSaveReceiptsAsync(SongDbContext context)
        {
            var tableCount = await context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ScoreSaveReceipts'"
            ).ToListAsync();
            if (tableCount.FirstOrDefault() == 0)
            {
                await CreateScoreSaveReceiptsTableAsync(
                    context,
                    "ScoreSaveReceipts");
            }
            else
            {
                var runIdColumnCount = await context.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('ScoreSaveReceipts') WHERE name='RunId'"
                ).ToListAsync();
                if (runIdColumnCount.FirstOrDefault() == 0)
                {
                    throw new InvalidOperationException(
                        "SongDatabaseService: Existing ScoreSaveReceipts table has no RunId identity column; " +
                        "migration stopped without deleting receipt data.");
                }

                await EnsureMigrationColumnAsync(
                    context,
                    table: "ScoreSaveReceipts",
                    column: "ChartId",
                    definition: "INTEGER NOT NULL DEFAULT 0");
                await EnsureMigrationColumnAsync(
                    context,
                    table: "ScoreSaveReceipts",
                    column: "Instrument",
                    definition: "INTEGER NOT NULL DEFAULT 0");
                await EnsureMigrationColumnAsync(
                    context,
                    table: "ScoreSaveReceipts",
                    column: "PlaySpeedPercent",
                    definition: "INTEGER NOT NULL DEFAULT 100");
                await EnsureMigrationColumnAsync(
                    context,
                    table: "ScoreSaveReceipts",
                    column: "SongScoreId",
                    definition: "INTEGER NULL");
                await EnsureMigrationColumnAsync(
                    context,
                    table: "ScoreSaveReceipts",
                    column: "SavedAtUtc",
                    definition: "TEXT NOT NULL DEFAULT ''");

                var correctSetNullFkCount = await context.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) FROM pragma_foreign_key_list('ScoreSaveReceipts') " +
                    "WHERE [table]='SongScores' AND [from]='SongScoreId' AND [on_delete]='SET NULL'"
                ).ToListAsync();
                var correctColumnShapeCount = await context.Database.SqlQueryRaw<int>(@"
                    SELECT COUNT(*)
                    FROM pragma_table_info('ScoreSaveReceipts')
                    WHERE (name='RunId' AND type='TEXT' AND [notnull]=1 AND pk=1)
                       OR (name='ChartId' AND type='INTEGER' AND [notnull]=1)
                       OR (name='Instrument' AND type='INTEGER' AND [notnull]=1)
                       OR (name='PlaySpeedPercent' AND type='INTEGER' AND [notnull]=1
                           AND CAST(dflt_value AS INTEGER)=100)
                       OR (name='SongScoreId' AND type='INTEGER' AND [notnull]=0)
                       OR (name='SavedAtUtc' AND type='TEXT' AND [notnull]=1)"
                ).ToListAsync();

                if (correctColumnShapeCount.FirstOrDefault() != 6
                    || correctSetNullFkCount.FirstOrDefault() == 0)
                {
                    await RebuildScoreSaveReceiptsTableAsync(context);
                }
            }

            var correctReceiptIndexCount = await context.Database.SqlQueryRaw<int>(@"
                SELECT COUNT(*)
                FROM pragma_index_list('ScoreSaveReceipts') AS index_list
                WHERE index_list.name='IX_ScoreSaveReceipts_SongScoreId'
                  AND index_list.[unique]=0
                  AND (SELECT COUNT(*) FROM pragma_index_info(index_list.name))=1
                  AND (SELECT name FROM pragma_index_info(index_list.name) WHERE seqno=0)='SongScoreId'"
            ).ToListAsync();
            if (correctReceiptIndexCount.FirstOrDefault() == 0)
            {
                await context.Database.ExecuteSqlRawAsync(
                    "DROP INDEX IF EXISTS IX_ScoreSaveReceipts_SongScoreId");
                await context.Database.ExecuteSqlRawAsync(
                    "CREATE INDEX IX_ScoreSaveReceipts_SongScoreId ON ScoreSaveReceipts(SongScoreId)");
            }
        }

        private static Task CreateScoreSaveReceiptsTableAsync(
            SongDbContext context,
            string tableName)
        {
            return context.Database.ExecuteSqlRawAsync($@"
                CREATE TABLE {tableName} (
                    RunId TEXT NOT NULL CONSTRAINT PK_ScoreSaveReceipts PRIMARY KEY,
                    ChartId INTEGER NOT NULL,
                    Instrument INTEGER NOT NULL,
                    PlaySpeedPercent INTEGER NOT NULL DEFAULT 100,
                    SongScoreId INTEGER NULL,
                    SavedAtUtc TEXT NOT NULL,
                    CONSTRAINT FK_ScoreSaveReceipts_SongScores_SongScoreId
                        FOREIGN KEY (SongScoreId) REFERENCES SongScores (Id) ON DELETE SET NULL
                )");
        }

        private static async Task RebuildScoreSaveReceiptsTableAsync(SongDbContext context)
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync(
                    "DROP TABLE IF EXISTS ScoreSaveReceipts_new");
                await CreateScoreSaveReceiptsTableAsync(
                    context,
                    "ScoreSaveReceipts_new");
                await context.Database.ExecuteSqlRawAsync(@"
                    INSERT INTO ScoreSaveReceipts_new (
                        RunId,
                        ChartId,
                        Instrument,
                        PlaySpeedPercent,
                        SongScoreId,
                        SavedAtUtc)
                    SELECT
                        RunId,
                        ChartId,
                        Instrument,
                        PlaySpeedPercent,
                        CASE
                            WHEN SongScoreId IS NOT NULL
                                AND EXISTS (
                                    SELECT 1
                                    FROM SongScores
                                    WHERE SongScores.Id=ScoreSaveReceipts.SongScoreId)
                            THEN SongScoreId
                            ELSE NULL
                        END,
                        SavedAtUtc
                    FROM ScoreSaveReceipts");
                await context.Database.ExecuteSqlRawAsync(
                    "DROP TABLE ScoreSaveReceipts");
                await context.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE ScoreSaveReceipts_new RENAME TO ScoreSaveReceipts");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "SongDatabaseService: Failed to converge the ScoreSaveReceipts schema; " +
                    "the migration was rolled back without deleting receipt data.",
                    ex);
            }
        }

        /// <summary>
        /// Handle invalid database file by removing it and forcing fresh creation
        /// </summary>
        private async Task HandleInvalidDatabaseFileAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Invalid database file detected at: {_databasePath}");

                // Clear pooled SQLite connections so retries do not keep using the broken file handle.
                SqliteConnection.ClearAllPools();

                // Simply delete the corrupted file - no backup needed since it's corrupted anyway
                if (File.Exists(_databasePath))
                {
                    File.Delete(_databasePath);
                    System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Corrupted database file deleted. Fresh database will be created.");
                }

                SqliteConnection.ClearAllPools();

                // Small delay to ensure file system has processed the deletion
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Error removing invalid database file: {ex.Message}");
                // Try to continue anyway - maybe the file was already deleted
            }
        }

        /// <summary>
        /// Removes stale chart entries where the files no longer exist
        /// This helps clean up duplicate entries caused by file moves
        /// </summary>
        public async Task CleanupStaleChartsAsync()
        {
            try
            {
                using var context = CreateContext();
                
                var allCharts = await context.SongCharts.ToListAsync();
                var stalePaths = new List<int>();
                
                foreach (var chart in allCharts)
                {
                    if (!string.IsNullOrEmpty(chart.FilePath) && !File.Exists(chart.FilePath))
                    {
                        stalePaths.Add(chart.Id);
                    }
                }
                
                if (stalePaths.Count > 0)
                {
                    // Remove associated scores first
                    var staleScores = await context.SongScores
                        .Where(s => stalePaths.Contains(s.ChartId))
                        .ToListAsync();
                    
                    if (staleScores.Count > 0)
                    {
                        context.SongScores.RemoveRange(staleScores);
                    }
                    
                    // Remove the charts
                    var staleCharts = await context.SongCharts
                        .Where(c => stalePaths.Contains(c.Id))
                        .ToListAsync();
                    
                    if (staleCharts.Count > 0)
                    {
                        context.SongCharts.RemoveRange(staleCharts);
                    }
                    
                    await context.SaveChangesAsync();
                    
                    // Clean up songs that no longer have any charts
                    await CleanupOrphanedSongsAsync(context);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Error during stale chart cleanup: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Removes songs that no longer have any associated charts
        /// </summary>
        private async Task CleanupOrphanedSongsAsync(SongDbContext context)
        {
            try
            {
                var orphanedSongs = await context.Songs
                    .Where(s => !context.SongCharts.Any(c => c.SongId == s.Id))
                    .ToListAsync();
                
                if (orphanedSongs.Count > 0)
                {
                    context.Songs.RemoveRange(orphanedSongs);
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Error cleaning up orphaned songs: {ex.Message}");
            }
        }

    }


    /// Database statistics information
    public class DatabaseStats
    {
        public int SongCount { get; set; }
        public int DifficultyCount { get; set; }
        public int ScoreCount { get; set; }
        public int HierarchyNodeCount { get; set; }
        public int PerformanceHistoryCount { get; set; }
        public long DatabaseSizeBytes { get; set; }

        public string FormattedSize => DatabaseSizeBytes switch
        {
            < 1024 => $"{DatabaseSizeBytes} B",
            < 1024 * 1024 => $"{DatabaseSizeBytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{DatabaseSizeBytes / (1024.0 * 1024):F1} MB",
            _ => $"{DatabaseSizeBytes / (1024.0 * 1024 * 1024):F1} GB"
        };
    }
}
