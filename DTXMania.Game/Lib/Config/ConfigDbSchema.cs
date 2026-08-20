using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace DTXMania.Game.Lib.Config
{
    /// <summary>
    /// Shared v1 config-database schema contract:
    /// <code>
    /// CREATE TABLE ConfigEntries (Key TEXT PRIMARY KEY NOT NULL, Value TEXT NOT NULL);
    /// PRAGMA user_version = 1;
    /// </code>
    ///
    /// Consumed by the game's <see cref="SqliteConfigStore"/> and, via linked
    /// source, by the recorder's read-only SourceConfigDatabase — so both
    /// sides validate the schema identically without coupling the recorder
    /// to the game project.
    /// </summary>
    internal static class ConfigDbSchema
    {
        internal const int SchemaVersion = 1;

        internal static long ReadUserVersion(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            return (long)command.ExecuteScalar()!;
        }

        internal static bool HasConfigEntriesTable(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ConfigEntries'";
            return (long)command.ExecuteScalar()! == 1L;
        }

        /// <summary>
        /// Validate the v1 schema on an open connection and read all rows.
        /// Fails loudly on an unsupported version or a missing
        /// <c>ConfigEntries</c> table; the messages are identical on both
        /// consumer sides so drift surfaces identically.
        /// </summary>
        internal static Dictionary<string, string> ReadEntries(
            SqliteConnection connection,
            string databasePath)
        {
            var version = ReadUserVersion(connection);
            if (version != SchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported config database schema version {version} at '{databasePath}'; expected {SchemaVersion}.");
            }

            if (!HasConfigEntriesTable(connection))
            {
                throw new InvalidOperationException(
                    $"Config database at '{databasePath}' is missing the ConfigEntries table.");
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
    }
}
