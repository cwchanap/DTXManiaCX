using DTXMania.Game.Lib.Config;
using Microsoft.Data.Sqlite;

namespace DTXMania.VideoRecorder.Sandbox;

/// <summary>
/// Read-only access to the authoritative v1 source configuration database
/// (<c>&lt;source-app-data&gt;/config.db</c>) written by the game's
/// ConfigManager. Schema validation and row reading live in the shared
/// <see cref="ConfigDbSchema"/> (linked source from the game project), so
/// both sides validate identically.
/// The recorder never writes or copies the database (no config.db, -wal,
/// or -shm writes anywhere in recorder code) — rows are only loaded into
/// memory to serialize the sandbox bootstrap Config.ini.
/// </summary>
internal static class SourceConfigDatabase
{
    internal const string DatabaseFileName = "config.db";

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
        // The builder escapes path-hostile characters (';', '"') that raw
        // string interpolation would misparse as connection delimiters.
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
        connection.Open();

        return ConfigDbSchema.ReadEntries(connection, databasePath);
    }
}
