using Microsoft.Data.Sqlite;

namespace DTXMania.VideoRecorder.Sandbox;

/// <summary>
/// Read-only access to the authoritative v1 source configuration database
/// (<c>&lt;source-app-data&gt;/config.db</c>) written by the game's
/// ConfigManager. Mirrors the schema contract of
/// <c>DTXMania.Game/Lib/Config/SqliteConfigStore.cs</c>; the messages are
/// intentionally identical so drift surfaces identically on both sides.
/// The recorder never writes or copies the database (no config.db, -wal,
/// or -shm writes anywhere in recorder code) — rows are only loaded into
/// memory to serialize the sandbox bootstrap Config.ini.
/// </summary>
internal static class SourceConfigDatabase
{
    internal const string DatabaseFileName = "config.db";

    private const int SchemaVersion = 1;

    internal static string GetDatabasePath(string sourceRoot) =>
        Path.Combine(sourceRoot, DatabaseFileName);

    /// <summary>
    /// Loads the complete key/value snapshot from an existing v1 database,
    /// opened READ-ONLY. Fails loudly when the database is unreadable, has
    /// an unsupported schema version, or is missing the
    /// <c>ConfigEntries</c> table.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> Load(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
        connection.Open();

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
}
