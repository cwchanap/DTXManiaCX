using DTXMania.E2E.Fixtures;
using DTXMania.Game.Lib.Config;

namespace DTXMania.E2E.Support;

/// <summary>
/// Post-run persistence reader for a finished E2E run (HPA-190). Loads the
/// fixture's authoritative SQLite config database through the production
/// <see cref="ConfigManager"/> explicit-path constructor — no process-global
/// <c>DTXMANIA_APPDATA_ROOT</c> mutation, no INI fallback.
/// </summary>
public static class E2EConfigPersistence
{
    public static ConfigManager LoadPersistedConfig(E2EFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        // The database must exist BEFORE loading: ConfigManager imports the
        // legacy bootstrap INI when the database is absent, which would let a
        // persistence test (notably the reset test) pass green without the
        // run ever having persisted anything.
        Assert.True(
            File.Exists(fixture.ConfigDatabasePath),
            $"Config database was not created at '{fixture.ConfigDatabasePath}'; " +
            "the run never persisted an authoritative config database.");

        var manager = new ConfigManager(fixture.ConfigDatabasePath, fixture.LegacyConfigPath);
        manager.LoadConfig();
        return manager;
    }
}
