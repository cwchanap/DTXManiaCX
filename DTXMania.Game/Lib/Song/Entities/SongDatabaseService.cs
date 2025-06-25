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
    public class SongDatabaseService
    {
        private readonly string _databasePath;
        private readonly DbContextOptions<SongDbContext> _options;

        public SongDatabaseService(string databasePath = "songs.db")
        {
            _databasePath = databasePath;

            var optionsBuilder = new DbContextOptionsBuilder<SongDbContext>();
            optionsBuilder.UseSqlite($"Data Source={_databasePath}");
            _options = optionsBuilder.Options;
        }


        /// Initialize the database and ensure it exists
        public async Task InitializeDatabaseAsync()
        {
            using var context = new SongDbContext(_options);

            // Apply any pending migrations
            var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
            if (pendingMigrations.Any())
            {
                await context.Database.MigrateAsync();
            }
        }


        /// Create a new DbContext instance
        public SongDbContext CreateContext()
        {
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


        /// Reset the database (delete and recreate)
        public async Task ResetDatabaseAsync()
        {
            using var context = new SongDbContext(_options);

            // Delete the database
            await context.Database.EnsureDeletedAsync();

            // Recreate it
            await context.Database.EnsureCreatedAsync();
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
        /// Add a new song with charts and scores
        public async Task<int> AddSongAsync(SongMetadata metadata)
        {
            using var context = CreateContext();

            // Create the song entity (metadata only)
            var song = new SongEntity
            {
                Title = metadata.Title,
                Artist = metadata.Artist,
                Genre = metadata.Genre,
                Comment = metadata.Comment,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.Songs.Add(song);
            await context.SaveChangesAsync();

            // Create the chart entity (DTX file specific)
            var chart = new SongChart
            {
                SongId = song.Id,
                FilePath = metadata.FilePath,
                FileHash = CalculateFileHash(metadata.FilePath),
                FileSize = metadata.FileSize,
                LastModified = metadata.LastModified,
                DifficultyLevel = 0, // Default difficulty level
                DifficultyLabel = "Standard",
                Bpm = metadata.BPM ?? 120.0,
                Duration = metadata.Duration ?? 0.0,
                BGMAdjust = 0,
                DrumLevel = metadata.DrumLevel ?? 0,
                GuitarLevel = metadata.GuitarLevel ?? 0,
                BassLevel = metadata.BassLevel ?? 0,
                HasDrumChart = metadata.DrumLevel.HasValue,
                HasGuitarChart = metadata.GuitarLevel.HasValue,
                HasBassChart = metadata.BassLevel.HasValue,
                DrumNoteCount = metadata.DrumNoteCount ?? 0,
                GuitarNoteCount = metadata.GuitarNoteCount ?? 0,
                BassNoteCount = metadata.BassNoteCount ?? 0,
                PreviewFile = metadata.PreviewFile ?? "",
                PreviewImage = metadata.PreviewImage ?? "",
                BackgroundFile = metadata.BackgroundImage ?? ""
            };

            context.SongCharts.Add(chart);
            await context.SaveChangesAsync();

            // Create initial score records for each available instrument
            if (metadata.DrumLevel.HasValue)
            {
                await AddScoreRecordAsync(context, chart.Id, EInstrumentPart.DRUMS);
            }
            if (metadata.GuitarLevel.HasValue)
            {
                await AddScoreRecordAsync(context, chart.Id, EInstrumentPart.GUITAR);
            }
            if (metadata.BassLevel.HasValue)
            {
                await AddScoreRecordAsync(context, chart.Id, EInstrumentPart.BASS);
            }

            return song.Id;
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


        /// Create a folder in the hierarchy
        public async Task<int> CreateFolderAsync(string title, string genre = "", int? parentId = null)
        {
            using var context = CreateContext();

            var folder = new SongHierarchy
            {
                NodeType = ENodeType.Box,
                Title = title,
                Genre = genre,
                ParentId = parentId,
                DisplayOrder = await GetNextDisplayOrderAsync(context, parentId),
                TextColorArgb = unchecked((int)0xFFFFFFFF), // White
                BreadcrumbPath = await BuildBreadcrumbPathAsync(context, title, parentId),
                IncludeInRandom = true
            };

            context.SongHierarchy.Add(folder);
            await context.SaveChangesAsync();

            return folder.Id;
        }


        /// Add a song to the hierarchy
        public async Task AddSongToHierarchyAsync(int songId, int? parentId = null)
        {
            using var context = CreateContext();

            var song = await context.Songs.FindAsync(songId);
            if (song == null) return;

            var hierarchyNode = new SongHierarchy
            {
                SongId = songId,
                NodeType = ENodeType.Song,
                Title = song.Title,
                Genre = song.Genre,
                ParentId = parentId,
                DisplayOrder = await GetNextDisplayOrderAsync(context, parentId),
                TextColorArgb = unchecked((int)0xFFFFFFFF), // White
                BreadcrumbPath = await BuildBreadcrumbPathAsync(context, song.Title, parentId),
                IncludeInRandom = true
            };

            context.SongHierarchy.Add(hierarchyNode);
            await context.SaveChangesAsync();
        }

        // Private helper methods        
        /// Add a chart for a song
        private async Task AddChartAsync(SongDbContext context, int songId, EInstrumentPart instrument, int level, int noteCount, string filePath = "")
        {
            var chart = new SongChart
            {
                SongId = songId,
                FilePath = filePath,
                DifficultyLevel = 0, // Default difficulty level
                DifficultyLabel = "Standard",
                DrumLevel = instrument == EInstrumentPart.DRUMS ? level : 0,
                GuitarLevel = instrument == EInstrumentPart.GUITAR ? level : 0,
                BassLevel = instrument == EInstrumentPart.BASS ? level : 0,
                HasDrumChart = instrument == EInstrumentPart.DRUMS,
                HasGuitarChart = instrument == EInstrumentPart.GUITAR,
                HasBassChart = instrument == EInstrumentPart.BASS,
                DrumNoteCount = instrument == EInstrumentPart.DRUMS ? noteCount : 0,
                GuitarNoteCount = instrument == EInstrumentPart.GUITAR ? noteCount : 0,
                BassNoteCount = instrument == EInstrumentPart.BASS ? noteCount : 0
            };

            context.SongCharts.Add(chart);
            await context.SaveChangesAsync();

            // Initialize score record for this chart
            await AddScoreRecordAsync(context, chart.Id, instrument);
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

        private async Task<string> BuildBreadcrumbPathAsync(SongDbContext context, string currentTitle, int? parentId)
        {
            if (!parentId.HasValue)
                return currentTitle;

            var parent = await context.SongHierarchy.FindAsync(parentId.Value);
            if (parent == null)
                return currentTitle;

            return $"{parent.BreadcrumbPath} > {currentTitle}";
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
