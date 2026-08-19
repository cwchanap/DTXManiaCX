using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace DTXMania.Game.Lib.Config
{
    /// <summary>
    /// Concrete v1 key/value configuration store backed by a SQLite database
    /// (by default <c>&lt;app-data&gt;/config.db</c>).
    ///
    /// Schema v1:
    /// <code>
    /// CREATE TABLE ConfigEntries (Key TEXT PRIMARY KEY NOT NULL, Value TEXT NOT NULL);
    /// PRAGMA user_version = 1;
    /// </code>
    ///
    /// A save replaces the complete logical snapshot inside a single
    /// transaction, which also removes stale dynamic rows (key bindings,
    /// song roots, MIDI thresholds).
    /// </summary>
    internal sealed class SqliteConfigStore
    {
        private const int SchemaVersion = 1;

        private readonly string _databasePath;

        /// <param name="databasePath">Full path of the config database file.</param>
        public SqliteConfigStore(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                throw new ArgumentException("Database path must be provided.", nameof(databasePath));
            }

            _databasePath = databasePath;
        }

        /// <summary>Whether the config database file exists on disk.</summary>
        public bool Exists => File.Exists(_databasePath);

        /// <summary>
        /// Load the complete key/value snapshot from an existing v1 database.
        /// Fails loudly when the database is unreadable, has an unsupported
        /// schema version, or is missing the <c>ConfigEntries</c> table.
        /// </summary>
        public IReadOnlyDictionary<string, string> Load()
        {
            using var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
            connection.Open();

            var version = ReadUserVersion(connection);
            if (version != SchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported config database schema version {version} at '{_databasePath}'; expected {SchemaVersion}.");
            }

            if (!HasConfigEntriesTable(connection))
            {
                throw new InvalidOperationException(
                    $"Config database at '{_databasePath}' is missing the ConfigEntries table.");
            }

            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var select = connection.CreateCommand())
            {
                select.CommandText = "SELECT Key, Value FROM ConfigEntries";
                using var reader = select.ExecuteReader();
                while (reader.Read())
                {
                    entries[reader.GetString(0)] = reader.GetString(1);
                }
            }

            return entries;
        }

        /// <summary>
        /// Replace the stored snapshot with <paramref name="entries"/> in one
        /// transaction (table/version setup + delete + inserts), so rows that
        /// are no longer present disappear.
        /// </summary>
        public void Save(IReadOnlyDictionary<string, string> entries)
        {
            var parentDirectory = Path.GetDirectoryName(Path.GetFullPath(_databasePath));
            if (!string.IsNullOrEmpty(parentDirectory))
            {
                Directory.CreateDirectory(parentDirectory);
            }

            using var connection = new SqliteConnection($"Data Source={_databasePath}");
            connection.Open();

            using var transaction = connection.BeginTransaction();

            ExecuteNonQuery(connection, transaction,
                "CREATE TABLE IF NOT EXISTS ConfigEntries (" +
                "Key TEXT PRIMARY KEY NOT NULL, " +
                "Value TEXT NOT NULL)");
            ExecuteNonQuery(connection, transaction, $"PRAGMA user_version = {SchemaVersion};");
            ExecuteNonQuery(connection, transaction, "DELETE FROM ConfigEntries");

            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO ConfigEntries (Key, Value) VALUES ($key, $value)";
                var keyParameter = insert.Parameters.AddWithValue("$key", string.Empty);
                var valueParameter = insert.Parameters.AddWithValue("$value", string.Empty);

                foreach (var pair in entries)
                {
                    keyParameter.Value = pair.Key;
                    valueParameter.Value = pair.Value;
                    insert.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        }

        private static long ReadUserVersion(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            return (long)command.ExecuteScalar()!;
        }

        private static bool HasConfigEntriesTable(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ConfigEntries'";
            return (long)command.ExecuteScalar()! == 1L;
        }

        private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string commandText)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = commandText;
            command.ExecuteNonQuery();
        }
    }
}
