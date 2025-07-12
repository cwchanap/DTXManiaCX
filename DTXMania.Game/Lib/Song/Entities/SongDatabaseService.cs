using DTX.Song;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
        private bool _isInitialized = false;

        public SongDatabaseService(string databasePath = "songs.db")
        {
            _databasePath = databasePath;

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


        /// Initialize the database and ensure it exists
        public async Task InitializeDatabaseAsync()
        {
            // Use lock to prevent multiple simultaneous initializations
            lock (_initializationLock)
            {
                if (_isInitialized)
                {
                    System.Diagnostics.Debug.WriteLine("SongDatabaseService: Database already initialized, skipping.");
                    return;
                }
            }

            try
            {
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

            // Close any existing connections
            using (var context = new SongDbContext(_options))
            {
                await context.Database.CloseConnectionAsync();
            }

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
                    .FirstOrDefaultAsync(c => c.FilePath == chart.FilePath);

                if (existingChart != null)
                {
                    // Song already exists, return the existing song ID
                    System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Song already exists with path: {chart.FilePath}");
                    return existingChart.SongId;
                }

                // Check if a song with the same title and artist already exists
                var existingSong = await context.Songs
                    .FirstOrDefaultAsync(s => s.Title == song.Title && s.Artist == song.Artist);

                if (existingSong != null)
                {
                    // Song exists, add chart to existing song
                    System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Adding chart to existing song: {song.Title} by {song.Artist}");
                    
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
                System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Creating new song: {song.Title} by {song.Artist}");

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

        /// Get a song by ID with all its charts
        public async Task<(SongEntity song, SongChart[] charts)?> GetSongWithChartsAsync(int songId)
        {
            using var context = CreateContext();

            var song = await context.Songs
                .Include(s => s.Charts)
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
                .FirstOrDefaultAsync(s => s.ChartId == chartId && s.Instrument == instrument);

            if (score != null)
            {
                // Update if this is a new best score
                if (newScore > score.BestScore)
                {
                    score.BestScore = newScore;
                    score.BestAchievementRate = achievementRate;
                    score.FullCombo = fullCombo;
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
        /// Get top scores for a specific instrument
        public async Task<List<SongScoreEntity>> GetTopScoresAsync(EInstrumentPart instrument, int limit = 10)
        {
            using var context = CreateContext();

            return await context.SongScores
                .Where(s => s.Instrument == instrument && s.BestScore > 0)
                .Include(s => s.Chart)
                .ThenInclude(c => c.Song)
                .OrderByDescending(s => s.BestScore)
                .Take(limit)
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

        private async Task<int> GetNextDisplayOrderAsync(SongDbContext context, int? parentId)
        {
            var maxOrder = await context.SongHierarchy
                .Where(h => h.ParentId == parentId)
                .MaxAsync(h => (int?)h.DisplayOrder) ?? 0;

            return maxOrder + 1;
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
            try
            {
                // Ensure we can write to the database
                await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = DELETE");
                
                // Set SQLite pragmas for UTF-8 and case-insensitive comparisons
                await context.Database.ExecuteSqlRawAsync("PRAGMA case_sensitive_like = OFF");
                
                System.Diagnostics.Debug.WriteLine("SongDatabaseService: UTF-8 encoding configured for database");

                // Create/update version table to mark Unicode configuration
                await EnsureDatabaseVersionTableAsync(context);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Warning - Could not configure UTF-8 encoding: {ex.Message}");
                // Continue anyway - most modern SQLite installations default to UTF-8
            }
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
        /// Handle invalid database file by removing it and forcing fresh creation
        /// </summary>
        private async Task HandleInvalidDatabaseFileAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Invalid database file detected at: {_databasePath}");

                // Simply delete the corrupted file - no backup needed since it's corrupted anyway
                if (File.Exists(_databasePath))
                {
                    File.Delete(_databasePath);
                    System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Corrupted database file deleted. Fresh database will be created.");
                }

                // Small delay to ensure file system has processed the deletion
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Error removing invalid database file: {ex.Message}");
                // Try to continue anyway - maybe the file was already deleted
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
