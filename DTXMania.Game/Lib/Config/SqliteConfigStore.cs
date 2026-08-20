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

        /// <summary>Full path of the config database file.</summary>
        public string DatabasePath => _databasePath;

        /// <summary>
        /// Load the complete key/value snapshot from an existing v1 database.
        /// Fails loudly when the database is unreadable, has an unsupported
        /// schema version, or is missing the <c>ConfigEntries</c> table.
        /// </summary>
        public IReadOnlyDictionary<string, string> Load()
        {
            // The builder escapes path-hostile characters (';', '"') that raw
            // string interpolation would misparse as connection delimiters.
            using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = _databasePath,
                    Mode = SqliteOpenMode.ReadOnly,
                }.ToString());
            connection.Open();

            return ConfigDbSchema.ReadEntries(connection, _databasePath);
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

            // See Load(): the builder keeps delimiter-bearing paths intact.
            using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = _databasePath }.ToString());
            connection.Open();

            using var transaction = connection.BeginTransaction();

            // Never downgrade a database written by a NEWER schema version:
            // a save that blindly wrote user_version would strand that data
            // behind a version check the older build then rejects.
            var existingVersion = ConfigDbSchema.ReadUserVersion(connection);
            if (existingVersion > ConfigDbSchema.SchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Refusing to save over config database at '{_databasePath}' with schema version " +
                    $"{existingVersion} newer than the supported {ConfigDbSchema.SchemaVersion}.");
            }

            ExecuteNonQuery(connection, transaction,
                "CREATE TABLE IF NOT EXISTS ConfigEntries (" +
                "Key TEXT PRIMARY KEY NOT NULL, " +
                "Value TEXT NOT NULL)");
            ExecuteNonQuery(connection, transaction, $"PRAGMA user_version = {ConfigDbSchema.SchemaVersion};");
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

        private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string commandText)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = commandText;
            command.ExecuteNonQuery();
        }
    }
}
