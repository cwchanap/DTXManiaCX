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
    /// Owns the game's song database: path/options, initialization lifecycle,
    /// schema versioning and short-lived <see cref="SongDbContext"/> creation
    /// (HPA-801). Bulk import/chart inventory/watermark reads live on
    /// <see cref="SongCatalogStore"/>; score transactions live on
    /// <see cref="SongScoreStore"/>. Both stores share one instance of this class.
    /// </summary>
    public class SongDatabase : IDisposable
    {
        private readonly string _databasePath;
        private readonly DbContextOptions<SongDbContext> _options;
        private readonly Func<SongDbContext>? _contextFactory;
        private readonly object _initializationLock = new object();
        private readonly SemaphoreSlim _initializationSemaphore = new SemaphoreSlim(1, 1);
        private bool _isInitialized = false;

        /// <summary>
        /// Gets the path to the database file
        /// </summary>
        public string DatabasePath => _databasePath;

        /// <summary>
        /// Whether <see cref="InitializeDatabaseAsync"/> completed. Exposed to the
        /// game-owned library so its query paths can preserve the pre-HPA-801
        /// contract: queries before Startup's initialization phase read as empty
        /// instead of throwing (HPA-801).
        /// </summary>
        internal bool IsInitialized
        {
            get
            {
                lock (_initializationLock)
                {
                    return _isInitialized;
                }
            }
        }

        internal const int CurrentSongSchemaVersion = 1;

        internal const string EnumerationMetadataTableDdl = @"CREATE TABLE IF NOT EXISTS __EnumerationMetadata (
    Key TEXT PRIMARY KEY,
    Value TEXT NOT NULL
)";

        public SongDatabase(string databasePath = "songs.db")
        {
            _databasePath = databasePath;

            // No filesystem side effects here (HPA-801): ordinary BaseGame
            // construction runs sandboxed, so the database directory is created
            // by InitializeDatabaseAsync instead.

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
        /// Lets unit tests inject an in-memory/temporary SQLite database (and
        /// optionally counting/throwing contexts) without going through the
        /// file-path-based config path — the single DB test seam (HPA-801,
        /// Decision E). The caller is responsible for ensuring the schema is
        /// created (e.g., via SongDbContext.Database.EnsureCreated()).
        /// </summary>
        /// <param name="options">Pre-built SQLite options for every context.</param>
        /// <param name="contextFactory">
        /// Optional context factory (counting/throwing test seams); defaults to
        /// plain <c>new SongDbContext(options)</c>.
        /// </param>
        /// <param name="initialized">
        /// Marks the database as already initialized so <see cref="CreateContext"/>
        /// skips the "must initialize first" guard (pre-created test schemas).
        /// </param>
        internal SongDatabase(
            DbContextOptions<SongDbContext> options,
            Func<SongDbContext>? contextFactory = null,
            bool initialized = true)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _contextFactory = contextFactory;
            _databasePath = string.Empty;
            _isInitialized = initialized;
        }

        /// Initialize the database and ensure it exists
        public async Task InitializeDatabaseAsync()
        {
            await _initializationSemaphore.WaitAsync();
            try
            {
                // Create the database directory here rather than in the path
                // constructor, so constructing the game-owned database never
                // writes into host AppData (HPA-801).
                var databaseDirectory = Path.GetDirectoryName(_databasePath);
                if (!string.IsNullOrWhiteSpace(databaseDirectory))
                {
                    Directory.CreateDirectory(databaseDirectory);
                }

                lock (_initializationLock)
                {
                    if (_isInitialized)
                    {
                        return;
                    }
                }

                bool needsRecreation = true;

                if (InitializationDatabaseFileExists())
                {
                    var storedVersion = await ReadSchemaVersionOrMismatchAsync();

                    if (storedVersion != CurrentSongSchemaVersion)
                    {
                        await PurgeDatabaseAsync();
                    }
                    else
                    {
                        needsRecreation = false;
                    }
                }

                if (needsRecreation)
                {
                    using var context = CreateInitializationContext();

                    // Ensure database is created first
                    await EnsureCreatedForInitializationAsync(context);
                    await EnsureEnumerationMetadataTableAsync(context);
                    await StampSchemaVersionAsync(context);
                }

                // Mark as initialized after successful creation
                lock (_initializationLock)
                {
                    _isInitialized = true;
                }

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
                var version = (long)(await command.ExecuteScalarAsync())!;
                if (version != CurrentSongSchemaVersion)
                {
                    return version;
                }

                command.CommandText = "PRAGMA quick_check(1);";
                var checkResult = (string?)(await command.ExecuteScalarAsync());
                if (!string.Equals(checkResult, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    return -1;
                }

                return version;
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

        /// <summary>
        /// Ensures the <c>__EnumerationMetadata</c> key/value table exists. Created
        /// idempotently for both fresh and pre-existing databases via
        /// <c>CREATE TABLE IF NOT EXISTS</c> (the table is intentionally not modeled
        /// on <see cref="SongDbContext"/>, so <c>EnsureCreated</c> does not create
        /// it). The table stores the <see cref="SongCatalogStore"/> scan-watermark
        /// timestamp used by the cache-freshness check instead of the database
        /// file's last-write time.
        /// </summary>
        private static async Task EnsureEnumerationMetadataTableAsync(SongDbContext context)
        {
            await context.Database.ExecuteSqlRawAsync(EnumerationMetadataTableDdl);
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


        /// <summary>
        /// Checks whether the database file is reachable (an open-able SQLite
        /// data source). Deliberately bypasses the initialized guard so the
        /// startup freshness decision can treat an uninitialized or failed
        /// database as "needs enumeration" instead of throwing.
        /// </summary>
        public async Task<bool> CanConnectAsync()
        {
            using var context = new SongDbContext(_options);
            return await context.Database.CanConnectAsync();
        }

        /// <summary>
        /// Purge the database file completely (for fresh rebuild)
        /// </summary>
        public virtual async Task PurgeDatabaseAsync()
        {
            // Close any existing connections first
            using (var context = new SongDbContext(_options))
            {
                await context.Database.CloseConnectionAsync();
            }

            // Clear this database's pooled connections so the file is no
            // longer locked. Scoped ClearPool calls rather than
            // ClearAllPools(): the latter closes idle pooled connections
            // belonging to every other database in the process — under a
            // parallel test run that can drop a shared or in-memory database
            // out from under an unrelated test.
            ClearPooledConnections();

            // Delete the database file completely
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
            DeleteDatabaseFileSidecars(_databasePath);

            // Reset initialization flag
            lock (_initializationLock)
            {
                _isInitialized = false;
            }
        }

        /// <summary>
        /// Releases idle pooled connections that can hold this database file
        /// open: the resolved options connection string (covers both
        /// constructors, including a caller-supplied connection) plus the two
        /// file-path spellings other contexts sharing the file may have used.
        /// </summary>
        private void ClearPooledConnections()
        {
            var connectionStrings = new HashSet<string>(StringComparer.Ordinal);
            using (var probe = new SongDbContext(_options))
            {
                if (probe.Database.GetDbConnection() is SqliteConnection pooled &&
                    !string.IsNullOrEmpty(pooled.ConnectionString))
                {
                    connectionStrings.Add(pooled.ConnectionString);
                }
            }
            if (!string.IsNullOrEmpty(_databasePath))
            {
                connectionStrings.Add($"Data Source={_databasePath}");
                connectionStrings.Add($"Data Source={_databasePath};Cache=Shared;");
            }

            foreach (var connectionString in connectionStrings)
            {
                using var key = new SqliteConnection(connectionString);
                SqliteConnection.ClearPool(key);
            }
        }

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

        /// <summary>
        /// Dispose of resources
        /// </summary>
        public void Dispose()
        {
            // Database connections are managed by DbContext instances and disposed automatically
            // No explicit cleanup needed as we use 'using' statements for context management
        }

    }
}
