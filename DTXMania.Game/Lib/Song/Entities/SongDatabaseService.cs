using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        private readonly Func<SongDbContext>? _contextFactory;
        private readonly Func<int, bool>? _activeChartIdentityFilter;
        private readonly object _initializationLock = new object();
        private readonly SemaphoreSlim _initializationSemaphore = new SemaphoreSlim(1, 1);
        private bool _isInitialized = false;

        internal const int CurrentSongSchemaVersion = 1;

        internal const string EnumerationMetadataTableDdl = @"CREATE TABLE IF NOT EXISTS __EnumerationMetadata (
    Key TEXT PRIMARY KEY,
    Value TEXT NOT NULL
)";

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

        internal SongDatabaseService(
            DbContextOptions<SongDbContext> options,
            Func<SongDbContext> contextFactory)
            : this(options, contextFactory, initialized: true)
        {
        }

        internal SongDatabaseService(
            DbContextOptions<SongDbContext> options,
            Func<SongDbContext> contextFactory,
            bool initialized)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _contextFactory = contextFactory
                ?? throw new ArgumentNullException(nameof(contextFactory));
            _databasePath = string.Empty;
            _isInitialized = initialized;
        }

        internal SongDatabaseService(
            DbContextOptions<SongDbContext> options,
            Func<SongDbContext> contextFactory,
            Func<int, bool> activeChartIdentityFilter)
            : this(options, contextFactory)
        {
            _activeChartIdentityFilter = activeChartIdentityFilter
                ?? throw new ArgumentNullException(
                    nameof(activeChartIdentityFilter));
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

                if (InitializationDatabaseFileExists())
                {
                    var storedVersion = await ReadSchemaVersionOrMismatchAsync();

                    if (storedVersion != CurrentSongSchemaVersion)
                    {
                        System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Database schema version {storedVersion} does not match required version {CurrentSongSchemaVersion}, recreating database");
                        await PurgeDatabaseAsync();
                    }
                }

                using var context = CreateInitializationContext();

                // Ensure database is created first
                await EnsureCreatedForInitializationAsync(context);
                await EnsureEnumerationMetadataTableAsync(context);
                await StampSchemaVersionAsync(context);

                // Mark as initialized after successful creation
                lock (_initializationLock)
                {
                    _isInitialized = true;
                }

                System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Database initialized successfully at: {_databasePath}");
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

        internal virtual bool InitializationDatabaseFileExists() =>
            File.Exists(_databasePath);

        internal virtual SongDbContext CreateInitializationContext() =>
            new SongDbContext(_options);

        internal virtual Task EnsureCreatedForInitializationAsync(
            SongDbContext context) =>
            context.Database.EnsureCreatedAsync();

        private const int SqliteCorruptErrorCode = 11;
        private const int SqliteNotADatabaseErrorCode = 26;

        internal virtual async Task<long> ReadSchemaVersionOrMismatchAsync()
        {
            try
            {
                var connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = _databasePath,
                    Pooling = false,
                }.ToString();

                await using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version;";
                return (long)(await command.ExecuteScalarAsync())!;
            }
            catch (SqliteException ex) when (
                ex.SqliteErrorCode == SqliteCorruptErrorCode
                || ex.SqliteErrorCode == SqliteNotADatabaseErrorCode)
            {
                return -1;
            }
        }

        private static async Task StampSchemaVersionAsync(SongDbContext context)
        {
            var stampSql = $"PRAGMA user_version = {CurrentSongSchemaVersion}";
            await context.Database.ExecuteSqlRawAsync(stampSql);
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
            return _contextFactory?.Invoke() ?? new SongDbContext(_options);
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
        public virtual async Task PurgeDatabaseAsync()
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
                DeleteDatabaseFileSidecars(_databasePath);

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

        private static bool HasJournalSidecar(string databasePath) =>
            File.Exists(databasePath + "-wal")
            || File.Exists(databasePath + "-shm")
            || File.Exists(databasePath + "-journal");

        private static void DeleteDatabaseFileSidecars(string databasePath)
        {
            foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
            {
                var sidecar = databasePath + suffix;
                if (File.Exists(sidecar))
                {
                    File.Delete(sidecar);
                }
            }
        }


        /// Backup the database to a specified path
        public async Task BackupDatabaseAsync(string backupPath)
        {
            if (!File.Exists(_databasePath))
            {
                throw new FileNotFoundException($"Database file not found: {_databasePath}");
            }

            if (PathsReferToSameFile(_databasePath, backupPath))
            {
                throw new ArgumentException(
                    $"Backup destination must differ from the database file: {backupPath}",
                    nameof(backupPath));
            }

            if (File.Exists(backupPath) && HasJournalSidecar(backupPath))
            {
                throw new IOException(
                    $"Backup destination has outstanding SQLite journal state and cannot be replaced safely: {backupPath}");
            }

            var backupDirectory = Path.GetDirectoryName(Path.GetFullPath(backupPath))
                ?? Directory.GetCurrentDirectory();
            var tempBackupPath = Path.Combine(backupDirectory, Path.GetRandomFileName());
            (ulong, ulong)? tempFileIdentity = null;
            try
            {
                using (var reservation = new FileStream(
                    tempBackupPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    if (TryGetOpenFileIdentity(
                            reservation.SafeFileHandle,
                            out var createdIdentity))
                    {
                        tempFileIdentity = createdIdentity;
                    }
                }

                using var context = new SongDbContext(_options);
                await WriteSnapshotToTempFileAsync(context, tempBackupPath);

                await CopySnapshotToDestinationAsync(tempBackupPath, backupPath);
            }
            finally
            {
                if (tempFileIdentity.HasValue
                    && TryGetFileIdentity(tempBackupPath, out var currentIdentity)
                    && currentIdentity == tempFileIdentity.Value)
                {
                    File.Delete(tempBackupPath);
                }
            }
        }


        /// Restore database from a backup
        public async Task RestoreDatabaseAsync(string backupPath)
        {
            if (!File.Exists(backupPath))
            {
                throw new FileNotFoundException($"Backup file not found: {backupPath}");
            }

            if (PathsReferToSameFile(_databasePath, backupPath))
            {
                throw new ArgumentException(
                    $"Restore source must differ from the database file: {backupPath}",
                    nameof(backupPath));
            }

            await using var source = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = backupPath,
                    Pooling = false,
                }.ToString());
            await source.OpenAsync();

            await using var versionProbe = source.CreateCommand();
            versionProbe.CommandText = "PRAGMA user_version;";
            var backupVersion = (long)(await versionProbe.ExecuteScalarAsync())!;
            if (backupVersion != CurrentSongSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Backup schema version {backupVersion} does not match required version {CurrentSongSchemaVersion}: {backupPath}");
            }

            using var context = new SongDbContext(_options);
            var destination = (SqliteConnection)context.Database.GetDbConnection();
            await destination.OpenAsync();
            source.BackupDatabase(destination);
        }

        internal virtual Task WriteSnapshotToTempFileAsync(
            SongDbContext context,
            string tempBackupPath)
        {
            var backupSql =
                $"VACUUM INTO '{tempBackupPath.Replace("'", "''")}'";
            return context.Database.ExecuteSqlRawAsync(backupSql);
        }

        private static async Task CopySnapshotToDestinationAsync(
            string snapshotPath,
            string destinationPath)
        {
            await using var source = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = snapshotPath,
                    Pooling = false,
                    Mode = SqliteOpenMode.ReadOnly,
                }.ToString());
            await using var destination = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = destinationPath,
                    Pooling = false,
                }.ToString());
            await source.OpenAsync();
            await destination.OpenAsync();
            source.BackupDatabase(destination);
        }

        private bool PathsReferToSameFile(string firstPath, string secondPath)
        {
            var comparison = OperatingSystem.IsLinux()
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            var firstFullPath = Path.GetFullPath(firstPath);
            var secondFullPath = Path.GetFullPath(secondPath);
            if (string.Equals(firstFullPath, secondFullPath, comparison))
            {
                return true;
            }

            if (string.Equals(
                    ResolveLinkTargets(firstFullPath),
                    ResolveLinkTargets(secondFullPath),
                    comparison))
            {
                return true;
            }

            if (!File.Exists(firstFullPath) || !File.Exists(secondFullPath))
            {
                return false;
            }

            if (!TryGetFileIdentity(firstFullPath, out var firstIdentity))
            {
                throw new IOException(
                    $"Cannot determine file identity for '{firstFullPath}'");
            }
            if (!TryGetFileIdentity(secondFullPath, out var secondIdentity))
            {
                throw new IOException(
                    $"Cannot determine file identity for '{secondFullPath}'");
            }
            return firstIdentity == secondIdentity;
        }

        private static string ResolveLinkTargets(string fullPath)
        {
            var root = Path.GetPathRoot(fullPath) ?? string.Empty;
            var resolved = root;
            var segments = fullPath.Substring(root.Length).Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < segments.Length; i++)
            {
                var candidate = Path.Combine(resolved, segments[i]);
                FileSystemInfo? linkTarget;
                if (i + 1 < segments.Length)
                {
                    linkTarget = Directory.Exists(candidate)
                        ? Directory.ResolveLinkTarget(candidate, returnFinalTarget: true)
                        : null;
                }
                else
                {
                    linkTarget = File.Exists(candidate)
                        ? File.ResolveLinkTarget(candidate, returnFinalTarget: true)
                        : Directory.Exists(candidate)
                            ? Directory.ResolveLinkTarget(candidate, returnFinalTarget: true)
                            : null;
                }

                resolved = linkTarget == null
                    ? candidate
                    : ResolveLinkTargets(linkTarget.FullName);
            }

            return resolved;
        }

        internal virtual bool TryGetFileIdentity(
            string fullPath,
            out (ulong, ulong) identity)
        {
            identity = default;
            try
            {
                using var stream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                return TryGetOpenFileIdentity(stream.SafeFileHandle, out identity);
            }
            catch (Exception exception) when (exception
                is IOException
                or UnauthorizedAccessException
                or DllNotFoundException
                or EntryPointNotFoundException)
            {
                return false;
            }
        }

        internal virtual bool TryGetOpenFileIdentity(
            SafeFileHandle handle,
            out (ulong, ulong) identity)
        {
            identity = default;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    if (!GetFileInformationByHandle(handle, out var info))
                    {
                        return false;
                    }
                    identity = (
                        info.dwVolumeSerialNumber,
                        ((ulong)info.nFileIndexHigh << 32) | info.nFileIndexLow);
                    return true;
                }

                if (!BitConverter.IsLittleEndian)
                {
                    return false;
                }

                var statBuffer = new byte[StatBufferSize];
                if (fstat(handle.DangerousGetHandle(), statBuffer) != 0)
                {
                    return false;
                }
                identity = OperatingSystem.IsMacOS()
                    ? (BitConverter.ToUInt32(statBuffer, DarwinDeviceOffset),
                        BitConverter.ToUInt64(statBuffer, DarwinInodeOffset))
                    : (BitConverter.ToUInt64(statBuffer, PosixDeviceOffset),
                        BitConverter.ToUInt64(statBuffer, PosixInodeOffset));
                return true;
            }
            catch (Exception exception) when (exception
                is IOException
                or UnauthorizedAccessException
                or DllNotFoundException
                or EntryPointNotFoundException)
            {
                return false;
            }
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int fstat(IntPtr fd, [Out] byte[] buffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(
            SafeFileHandle hFile,
            out ByHandleFileInformation lpFileInformation);

        private const int StatBufferSize = 256;
        private const int PosixDeviceOffset = 0;
        private const int PosixInodeOffset = 8;
        private const int DarwinDeviceOffset = 0;
        private const int DarwinInodeOffset = 8;

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleFileInformation
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint dwVolumeSerialNumber;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint nNumberOfLinks;
            public uint nFileIndexHigh;
            public uint nFileIndexLow;
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

        internal async Task<SongBulkImportResult> ImportSongsAsync(
            SongBulkImportRequest request,
            IProgress<SongBulkImportProgress>? progress,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            await using var context = CreateContext();
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

                foreach (var identity in chartIdentities.Where(identity =>
                    !SongPathIdentity.TryNormalize(
                        identity.FilePath,
                        out _)))
                {
                    Debug.WriteLine(
                        "SongDatabaseService: retaining chart " +
                        $"{identity.Id} with malformed path " +
                        $"'{identity.FilePath}'");
                }

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
                    Debug.WriteLine(
                        "SongDatabaseService: ambiguous import group " +
                        $"'{group.Key}' matched song ids " +
                        $"[{string.Join(",", matchedSongIds)}]");
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
                catch (Exception progressException)
                {
                    Debug.WriteLine(
                        $"SongDatabaseService: Committed progress callback failed: {progressException.Message}");
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
                catch (Exception rollbackException)
                {
                    Debug.WriteLine(
                        $"SongDatabaseService: rollback failed: {rollbackException.Message}");
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
        /// Add a new song with charts and scores using EF Core entities
        /// Handles duplicate detection based on file path
        /// </summary>
        /// <remarks>
        /// Legacy/test-only helper with per-call persistence semantics. Startup song
        /// discovery must use <see cref="ImportSongsAsync"/> so the complete library is
        /// committed atomically.
        /// </remarks>
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
                // First, check whether this run was already saved (same-run
                // retry/idempotency). The winner may share this RunId if the
                // caller retried the same performance.
                var resolved = await ResolveReceiptAfterRaceAsync(
                    runId,
                    chartId,
                    instrument,
                    summary.PlaySpeedPercent,
                    cancellationToken).ConfigureAwait(false);
                if (resolved != null)
                    return resolved;

                // The race was against a different run (the winner has a different
                // RunId, so the receipt lookup above found nothing). Retry the
                // transaction: the winner's score now exists in the database, so
                // the retry will find and update it instead of inserting a
                // duplicate. The receipt check at the top of
                // SaveScoreTransactionAsync preserves same-run idempotency.
                // Limit retries to avoid unbounded loops under sustained
                // contention.
                const int maxRetries = 3;
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        return await SaveScoreTransactionAsync(
                            chartId,
                            instrument,
                            summary,
                            runId,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception retryEx) when (IsSqliteWriteRace(retryEx) && attempt < maxRetries)
                    {
                        await Task.Delay(
                            10 * (attempt + 1),
                            cancellationToken).ConfigureAwait(false);
                    }
                }
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
            bool wasNewlyCreated = false;
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
                wasNewlyCreated = true;
            }

            // Only force-overwrite the best fields when the score row is
            // genuinely empty — either created in this transaction, or a
            // placeholder row from AddScoreRecordAsync (PlayCount == 0 AND
            // BestScore == 0). NX import can persist a BestScore > 0 with
            // PlayCount == 0 (NxScoreData.HasDrumData allows that combination,
            // and NxScoreImporter writes best fields without requiring
            // PlayCount > 0). Using PlayCount == 0 alone as the "first play"
            // signal would treat that imported row as empty and overwrite the
            // imported best even when the new CX result is worse. Compare each
            // best field normally when the row already carries imported data.
            var isFirstPlay = wasNewlyCreated
                || (score.PlayCount == 0 && score.BestScore == 0);
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

            var normalizedRunRank = SongScoreEntity.RunRankBucket(summary.PlayingSkill);
            var normalizedBestRank = isFirstPlay
                ? 0
                : SongScoreEntity.NormalizeRankPercentage(score.BestRank);
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
            var rank = SongScoreEntity.RankString(normalizedRunRank);
            var status = summary.ClearFlag ? "Cleared" : "Failed";
            var line = string.Format(
                CultureInfo.InvariantCulture,
                "{0}.{1:yy/M/d} {2} ({3}: {4}) [{5}, {6}]",
                score.PlayCount,
                localDate,
                status,
                rank,
                SkillValueFormatter.Format(summary.PlayingSkill),
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
        /// <param name="activeChartIds">
        /// When non-null, recency (grouping, ordering, and pagination) considers only the
        /// scores of these charts. This pushes an active-root criterion into SQL so that
        /// ordering is globally correct across pages; null means every chart is considered.
        /// </param>
        public async Task<List<SongEntity>> GetRecentlyPlayedSongsAsync(
            int limit = 20,
            int skip = 0,
            IReadOnlyCollection<int>? activeChartIds = null)
        {
            if (limit <= 0) return new List<SongEntity>();

            using var context = CreateContext();

            // Push grouping + ordering + limit into SQL so we transfer at most `limit`
            // integer IDs back to the client instead of every played SongScore row.
            // The IDs are then used in a second query to load full Song+Chart+Score
            // graphs, and manually reordered client-side (IN does not preserve order).
            IQueryable<SongScore> scores = context.SongScores
                .Where(s => s.LastPlayedAt != null);
            if (activeChartIds != null)
                // EF Core 10 translates a parameterized collection as one SQL
                // parameter per element, which exceeds SQLite's variable limit
                // for large libraries. EF.Parameter forces the single JSON
                // parameter (json_each) translation for this unbounded set.
                scores = scores.Where(s => EF.Parameter(activeChartIds).Contains(s.ChartId));
            var orderedIds = await scores
                .Select(s => new { s.Chart.SongId, s.LastPlayedAt })
                .GroupBy(s => s.SongId)
                .Select(g => new { SongId = g.Key, LastPlayed = g.Max(s => s.LastPlayedAt) })
                .OrderByDescending(x => x.LastPlayed)
                .ThenBy(x => x.SongId)
                .Skip(skip)
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
        /// Returns the database IDs of charts whose <see cref="SongChart.FilePath"/>
        /// resides under one of <paramref name="roots"/> (root-containment uses
        /// <see cref="SongPathIdentity"/>, mirroring SongManager's active-chart
        /// rule). Used to push an active-root criterion into recency queries so
        /// that ordering and pagination are globally correct. Returns an empty
        /// list for an empty root set.
        /// </summary>
        public async Task<IReadOnlyList<int>> GetActiveChartIdsAsync(
            IReadOnlyList<string> roots)
        {
            if (roots == null || roots.Count == 0)
                return Array.Empty<int>();

            // Normalize defensively so blank or malformed roots do not abort the
            // query. Mirrors the active-root pre-normalization in
            // GetActiveChartCountAsync.
            var normalizedRoots = roots
                .Select(root =>
                {
                    SongPathIdentity.TryNormalize(root, out var normalized);
                    return normalized;
                })
                .Where(r => !string.IsNullOrEmpty(r))
                .ToArray();
            if (normalizedRoots.Length == 0)
                return Array.Empty<int>();
            using var context = CreateContext();
            var charts = await context.SongCharts
                .AsNoTracking()
                .Select(chart => new { chart.Id, chart.FilePath })
                .ToListAsync();
            var activeIds = new List<int>(charts.Count);
            foreach (var chart in charts)
            {
                if (SongPathIdentity.TryNormalize(chart.FilePath, out var normalized) &&
                    normalizedRoots.Any(root =>
                        SongPathIdentity.IsUnderRoot(normalized, root)))
                {
                    activeIds.Add(chart.Id);
                }
            }
            return activeIds;
        }

        /// <summary>
        /// Returns the <see cref="SongChart.Id"/> and <see cref="SongChart.FilePath"/>
        /// of every chart whose file path resides under one of <paramref name="roots"/>
        /// (root-containment uses <see cref="SongPathIdentity"/>, mirroring
        /// SongManager's active-chart rule). Used by the cache-freshness
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
            using var context = CreateContext();
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
        /// <see cref="SongPathIdentity"/>, mirroring SongManager's active-chart
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
            using var context = CreateContext();
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
        /// Ensures the <c>__EnumerationMetadata</c> key/value table exists. Created
        /// idempotently for both fresh and pre-existing databases via
        /// <c>CREATE TABLE IF NOT EXISTS</c> (the table is intentionally not modeled
        /// on <see cref="SongDbContext"/>, so <c>EnsureCreated</c> does not create
        /// it). The table stores the <see cref="LastSuccessfulEnumerationKey"/>
        /// timestamp used by the cache-freshness check instead of the database
        /// file's last-write time.
        /// </summary>
        private static async Task EnsureEnumerationMetadataTableAsync(SongDbContext context)
        {
            await context.Database.ExecuteSqlRawAsync(EnumerationMetadataTableDdl);
        }

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
                using var context = CreateContext();
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Warning - Could not read last enumeration timestamp: {ex.Message}");
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

                using var context = CreateContext();
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"SongDatabaseService: Warning - Could not read root enumeration timestamp: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Persists the UTC timestamp of the most recent successful enumeration.
        /// Called only after an <see cref="SongEnumerationOutcome.ImportedAndPublished"/>
        /// result so unrelated <c>SaveChangesAsync</c> calls (bookmark toggles, score
        /// saves) cannot advance the recorded enumeration time and mask later chart
        /// edits.
        /// </summary>
        public async Task SetLastSuccessfulEnumerationUtcAsync(DateTime utc)
        {
            try
            {
                using var context = CreateContext();
                await context.Database.ExecuteSqlRawAsync(
                    "INSERT OR REPLACE INTO __EnumerationMetadata (Key, Value) VALUES ({0}, {1})",
                    LastSuccessfulEnumerationKey,
                    utc.ToUniversalTime().ToString("o"));
            }
            catch (Exception ex)
            {
                // Swallow-only: a failed write leaves the previous timestamp in
                // place (or none), which only makes the next freshness check more
                // conservative. Never abort the post-enumeration path over metadata.
                System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Warning - Could not persist last enumeration timestamp: {ex.Message}");
            }
        }

        /// <summary>
        /// Persists the same enumeration watermark for each canonical root that
        /// participated in the successful scan. Roots omitted from the list keep
        /// their previous watermark, which is required for partial multi-root scans.
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
                using var context = CreateContext();
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
            catch (Exception ex)
            {
                // A failed root watermark leaves the previous value in place (or
                // none), so the next freshness check remains conservative.
                System.Diagnostics.Debug.WriteLine(
                    $"SongDatabaseService: Warning - Could not persist root enumeration timestamps: {ex.Message}");
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
                using var context = CreateContext();
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
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"SongDatabaseService: Warning - Could not persist rejected chart counts: {ex.Message}");
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
                using var context = CreateContext();
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
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"SongDatabaseService: Warning - Could not read rejected chart counts: {ex.Message}");
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
                using var context = CreateContext();
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"SongDatabaseService: Warning - Could not atomically persist enumeration watermark: {ex.Message}");
                return false;
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
                    // EF Core 10 translates a parameterized collection as one
                    // SQL parameter per element; EF.Parameter forces the single
                    // JSON parameter (json_each) translation so a large stale
                    // set cannot exceed SQLite's variable limit.
                    // Remove associated scores first
                    var staleScores = await context.SongScores
                        .Where(s => EF.Parameter(stalePaths).Contains(s.ChartId))
                        .ToListAsync();

                    if (staleScores.Count > 0)
                    {
                        context.SongScores.RemoveRange(staleScores);
                    }

                    // Remove the charts
                    var staleCharts = await context.SongCharts
                        .Where(c => EF.Parameter(stalePaths).Contains(c.Id))
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
