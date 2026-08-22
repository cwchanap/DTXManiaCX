using Microsoft.Data.Sqlite;

namespace DTXMania.VideoRecorder.Tests;

/// <summary>
/// Test-only writer that plants v1 (or deliberately malformed) source
/// config databases for recorder tests. This is NOT a production writer:
/// production always builds config.db through the game's ConfigManager,
/// and the recorder itself only ever opens the source database read-only.
/// </summary>
internal static class TestSourceConfigDatabase
{
    public const string DatabaseFileName = "config.db";

    internal static string Create(
        string sourceRoot,
        IReadOnlyDictionary<string, string> rows,
        int userVersion = 1,
        bool createConfigEntriesTable = true)
    {
        Directory.CreateDirectory(sourceRoot);
        var databasePath = Path.Combine(sourceRoot, DatabaseFileName);
        // The builder escapes path-hostile characters (';', '"') and Pooling=false
        // guarantees disposing the connection releases the config.db handle, so
        // callers can delete the source root right after Create.
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false,
            }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();

        if (createConfigEntriesTable)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                "CREATE TABLE ConfigEntries (" +
                "Key TEXT PRIMARY KEY NOT NULL, " +
                "Value TEXT NOT NULL)");
        }

        ExecuteNonQuery(connection, transaction, $"PRAGMA user_version = {userVersion};");

        if (createConfigEntriesTable)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO ConfigEntries (Key, Value) VALUES ($key, $value)";
            var keyParameter = insert.Parameters.AddWithValue("$key", string.Empty);
            var valueParameter = insert.Parameters.AddWithValue("$value", string.Empty);
            foreach (var pair in rows)
            {
                keyParameter.Value = pair.Key;
                valueParameter.Value = pair.Value;
                insert.ExecuteNonQuery();
            }
        }

        transaction.Commit();
        return databasePath;
    }

    /// <summary>
    /// Canonical valid v1 row set: absolute SongRoot.N/SystemSkinRoot under
    /// <paramref name="pathRoot"/>, default skin token, recorder-owned keys
    /// in their non-recording state, plus representative user rows.
    /// </summary>
    internal static Dictionary<string, string> BuildValidRows(
        string pathRoot,
        params string[] overrides)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SkinPath"] = "Default",
            ["SongRoot.0"] = Path.Combine(pathRoot, "Songs"),
            ["SystemSkinRoot"] = Path.Combine(pathRoot, "System"),
            ["LastUsedSkin"] = "Default",
            ["ScreenWidth"] = "1920",
            ["ScreenHeight"] = "1080",
            ["FullScreen"] = "True",
            ["ScrollSpeed"] = "50",
            ["PlaySpeedPercent"] = "100",
            ["PitchSemitones"] = "0",
            ["MasterVolume"] = "100",
            ["BGMVolume"] = "100",
            ["SEVolume"] = "100",
            ["EnableGameApi"] = "False",
            ["GameApiPort"] = "8080",
            ["GameApiKey"] = "source-key",
            // No AutoPlay row: a source database without AutoPlay.{lane} rows means
            // manual play, and the recorder owns the full-lane rows it patches in.
            ["NoFail"] = "False"
        };

        foreach (var item in overrides)
        {
            var parts = item.Split('=', 2);
            values[parts[0]] = parts[1];
        }

        return values;
    }

    private static void ExecuteNonQuery(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }
}
