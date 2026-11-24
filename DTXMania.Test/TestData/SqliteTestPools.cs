using System.IO;
using Microsoft.Data.Sqlite;

namespace DTXMania.Test.TestData
{
    /// <summary>
    /// Releases pooled SQLite connections for specific database files. Unlike
    /// <see cref="SqliteConnection.ClearAllPools"/> — which closes idle pooled
    /// connections for every database in the process — these clear only the
    /// pool groups that can hold the named file, so a parallel test's pooled
    /// connection (including a pooled in-memory database) is never dropped
    /// mid-test.
    /// </summary>
    internal static class SqliteTestPools
    {
        /// <summary>
        /// Clears every pool group the codebase can form for one database
        /// file. Pool groups are keyed by the VERBATIM connection string
        /// passed to <see cref="SqliteConnection"/> (no normalization), so
        /// candidates must spell it exactly as the code under test does:
        /// builder output for the config stores (which also quotes paths
        /// containing ';' or '"'), and the raw-interpolated spellings used
        /// by test helpers and SongDatabase's trailing-semicolon
        /// 'Cache=Shared;' string.
        /// </summary>
        public static void ClearForFile(string databasePath)
        {
            ClearPool(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
            ClearPool(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
            ClearPool(new SqliteConnectionStringBuilder { DataSource = databasePath, Cache = SqliteCacheMode.Shared }.ToString());
            ClearPool($"Data Source={databasePath}");
            ClearPool($"Data Source={databasePath};Mode=ReadOnly");
            ClearPool($"Data Source={databasePath};Cache=Shared;");
        }

        /// <summary>
        /// Clears pool groups for every <c>*.db</c> file under a directory
        /// (recursive). Use for test sandboxes that can create databases at
        /// arbitrary names or in subdirectories.
        /// </summary>
        public static void ClearForDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(directoryPath, "*.db", SearchOption.AllDirectories))
            {
                ClearForFile(file);
            }
        }

        private static void ClearPool(string connectionString)
        {
            try
            {
                using var connection = new SqliteConnection(connectionString);
                SqliteConnection.ClearPool(connection);
            }
            catch (ArgumentException)
            {
                // A candidate spelling that cannot parse cannot key a pool group.
            }
        }
    }
}
