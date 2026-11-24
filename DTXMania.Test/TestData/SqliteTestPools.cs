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
        /// spelling (SongDatabase).
        /// </summary>
        public static void ClearForFile(string databasePath)
        {
            ClearPool($"Data Source={databasePath}");
            ClearPool($"Data Source={databasePath};Mode=ReadOnly");
            ClearPool($"Data Source={databasePath};Cache=Shared;");
        }

        private static void ClearPool(string connectionString)
        {
            using var connection = new SqliteConnection(connectionString);
            SqliteConnection.ClearPool(connection);
        }
    }
}
