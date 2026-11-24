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
        /// file: the bare path spelling (config stores and test helpers), the
        /// read-only spelling (SqliteConfigStore.Load), and the shared-cache
        /// spelling (SongDatabase). Candidates go through
        /// <see cref="SqliteConnectionStringBuilder"/> so the key matches the
        /// normalized connection string pools group by — raw interpolation
        /// misses files under paths containing ';' or '"'.
        /// </summary>
        public static void ClearForFile(string databasePath)
        {
            ClearPool(new SqliteConnectionStringBuilder { DataSource = databasePath });
            ClearPool(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly });
            ClearPool(new SqliteConnectionStringBuilder { DataSource = databasePath, Cache = SqliteCacheMode.Shared });
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

        private static void ClearPool(SqliteConnectionStringBuilder builder)
        {
            using var connection = new SqliteConnection(builder.ToString());
            SqliteConnection.ClearPool(connection);
        }
    }
}
