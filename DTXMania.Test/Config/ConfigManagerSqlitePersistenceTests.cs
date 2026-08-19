using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using Microsoft.Data.Sqlite;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace DTXMania.Test.Config
{
    /// <summary>
    /// Pins the HPA-190 DB-authoritative migration contract of
    /// <see cref="ConfigManager"/>: DB read when present; legacy Config.ini
    /// imported only when the DB is absent (bytes untouched); DB wins on
    /// conflict; invalid DB fails loudly with no INI fallback; DTXPath stays
    /// an in-memory legacy mirror excluded from DB rows; deferred saves and
    /// immediate song-root saves target the SQLite store.
    /// </summary>
    [Collection("AppPaths")]
    [Trait("Category", "Unit")]
    public sealed class ConfigManagerSqlitePersistenceTests : IDisposable
    {
        private readonly string _root;
        private readonly string? _previousAppDataRoot;

        public ConfigManagerSqlitePersistenceTests()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "dtx-config-sqlite-pins-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);

            // Sandbox the app-data root: LoadConfig normalization resolves and
            // creates default app-data directories (System skin root, managed
            // songs root); without this those would land in the real user
            // app-data directory.
            _previousAppDataRoot = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", _root);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", _previousAppDataRoot);
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // Best effort; temp dirs are cleaned by the OS.
            }
        }

        private string DbPath => Path.Combine(_root, "config.db");
        private string IniPath => Path.Combine(_root, "Config.ini");

        private ConfigManager CreateManager(string? baseDir = null) =>
            new(DbPath, IniPath, baseDir: baseDir);

        private IReadOnlyDictionary<string, string> ReadRows() =>
            new SqliteConfigStore(DbPath).Load();

        // ── 2.1 pins ──────────────────────────────────────────────────────

        [Fact]
        public void LoadConfig_WithNoDbAndNoIni_ShouldLoadDefaultsAndCreateDb()
        {
            var manager = CreateManager();

            manager.LoadConfig();

            Assert.True(File.Exists(DbPath), "First launch must create the config database.");
            Assert.Equal(1280, manager.Config.ScreenWidth);
            Assert.Equal(720, manager.Config.ScreenHeight);
            Assert.Equal(ScrollSpeedRange.Default, manager.Config.ScrollSpeed);
            Assert.Equal(ConfigManager.DefaultSkinPathToken, manager.Config.SkinPath);

            var rows = ReadRows();
            Assert.Equal("1280", rows["ScreenWidth"]);
            Assert.Equal("720", rows["ScreenHeight"]);
            Assert.Equal(ConfigManager.DefaultSkinPathToken, rows["SkinPath"]);
        }

        [Fact]
        public void LoadConfig_WithIniOnly_ShouldImportCreateDbAndLeaveIniBytesUnchanged()
        {
            File.WriteAllText(IniPath,
                "[System]\nSkinPath=/skins/Custom\nDTXPath=/songs/legacy\n" +
                "[Display]\nScreenWidth=1920\nFullScreen=true\n");
            var iniBytesBefore = File.ReadAllBytes(IniPath);

            var manager = CreateManager();
            manager.LoadConfig();

            Assert.True(File.Exists(DbPath), "Import must create the config database.");
            Assert.Equal(1920, manager.Config.ScreenWidth);
            Assert.True(manager.Config.FullScreen);

            var rows = ReadRows();
            Assert.Equal("/skins/Custom", rows["SkinPath"]);
            Assert.Equal("/songs/legacy", rows["SongRoot.0"]);

            Assert.Equal(iniBytesBefore, File.ReadAllBytes(IniPath));
        }

        [Fact]
        public void LoadConfig_WithDbAndConflictingIni_ShouldPreferDb()
        {
            new SqliteConfigStore(DbPath).Save(
                new Dictionary<string, string> { ["ScrollSpeed"] = "250" });
            File.WriteAllText(IniPath, "[Game]\nScrollSpeed=120\n");
            var iniBytesBefore = File.ReadAllBytes(IniPath);

            var manager = CreateManager();
            manager.LoadConfig();

            Assert.Equal(250, manager.Config.ScrollSpeed);
            Assert.Equal(iniBytesBefore, File.ReadAllBytes(IniPath));
        }

        [Fact]
        public void LoadConfig_WithInvalidDbVersion_ShouldFailWithoutIniFallback()
        {
            WriteDatabaseWithUserVersion(DbPath, 2);
            File.WriteAllText(IniPath, "[Display]\nScreenWidth=1920\n");

            var manager = CreateManager();

            Assert.ThrowsAny<Exception>(() => manager.LoadConfig());
        }

        [Fact]
        public void LoadConfig_WithLegacyDtxPathOnly_ShouldPersistIndexedSongRootWithoutDtxPathRow()
        {
            var legacyRoot = Path.Combine(_root, "legacy-songs");
            File.WriteAllText(IniPath, $"[System]\nDTXPath={legacyRoot}\n");

            var manager = CreateManager();
            manager.LoadConfig();

            Assert.Equal([legacyRoot], manager.Config.SongRoots);
            Assert.Equal(legacyRoot, manager.Config.DTXPath);

            var rows = ReadRows();
            Assert.Equal(legacyRoot, rows["SongRoot.0"]);
            Assert.False(rows.ContainsKey("DTXPath"),
                "DTXPath is a legacy input/in-memory mirror only and must not be persisted as a DB row.");
        }

        [Fact]
        public void LoadConfig_WhenNormalizationCorrectsSongRoots_ShouldPersistCorrectionToDb()
        {
            // A NUL character is an illegal path character; normalization must
            // discard the entry, fall back to the managed default, and persist
            // the corrected root so the malformed row does not survive a reload.
            new SqliteConfigStore(DbPath).Save(
                new Dictionary<string, string> { ["SongRoot.0"] = "bad\0root" });

            var manager = CreateManager();
            manager.LoadConfig();

            var rows = ReadRows();
            Assert.True(rows.TryGetValue("SongRoot.0", out var correctedRoot));
            Assert.Equal(
                Path.GetFullPath(Path.Combine(_root, "DTXFiles")),
                Path.GetFullPath(correctedRoot!));
        }

        [Fact]
        public void LoadConfig_WhenGameApiEnabledWithoutKey_ShouldGenerateAndPersistKeyToDb()
        {
            File.WriteAllText(IniPath, "[Api]\nEnableGameApi=true\n");

            var manager = CreateManager();
            manager.LoadConfig();

            Assert.True(manager.Config.EnableGameApi);
            Assert.False(string.IsNullOrWhiteSpace(manager.Config.GameApiKey));
            Assert.Equal(32, manager.Config.GameApiKey.Length);

            var rows = ReadRows();
            Assert.Equal(manager.Config.GameApiKey, rows["GameApiKey"]);
        }

        [Fact]
        public void FlushPendingSave_WithDeferredEdit_ShouldUpdateDb()
        {
            var manager = CreateManager();
            manager.LoadConfig();

            manager.SetNoFail(true);
            // The deferred write must not land before the flush.
            Assert.Equal("False", ReadRows()["NoFail"]);

            manager.FlushPendingSave();

            Assert.Equal("True", ReadRows()["NoFail"]);
        }

        [Fact]
        public void FlushPendingSave_WhenSaveFails_ShouldRemainPendingAndRetryLater()
        {
            var manager = CreateManager();
            manager.LoadConfig();
            manager.SetNoFail(true);

            // Break the filesystem at the app-data root: replace the directory
            // with a file so the store's directory creation throws on save.
            Directory.Delete(_root, recursive: true);
            File.WriteAllText(_root, "blocker");

            // The failed flush must not throw and must keep the edit pending.
            manager.FlushPendingSave();
            Assert.True(manager.Config.NoFail);

            // Repair the filesystem and retry: the pending edit now lands.
            // ClearAllPools first: the pooled handle from the initial load
            // points at the deleted inode and SQLite rejects writes to it
            // ("attempt to write a readonly database").
            SqliteConnection.ClearAllPools();
            File.Delete(_root);
            Directory.CreateDirectory(_root);
            manager.FlushPendingSave();

            Assert.Equal("True", ReadRows()["NoFail"]);
        }

        [Fact]
        public void SetSongRoots_WhenImmediateSaveFails_ShouldRestorePriorValues()
        {
            var oldRoot = Path.Combine(_root, "old-root");
            var newRoot = Path.Combine(_root, "new-root");
            Directory.CreateDirectory(oldRoot);
            Directory.CreateDirectory(newRoot);

            var manager = CreateManager();
            manager.LoadConfig();
            manager.Config.SongRoots.Clear();
            manager.Config.SongRoots.Add(oldRoot);
            manager.Config.DTXPath = oldRoot;

            Directory.Delete(_root, recursive: true);
            File.WriteAllText(_root, "blocker");
            try
            {
                var result = manager.SetSongRoots([newRoot]);

                Assert.Equal(SongRootUpdateStatus.PersistenceFailed, result.Status);
                Assert.Equal([oldRoot], manager.Config.SongRoots);
                Assert.Equal(oldRoot, manager.Config.DTXPath);
            }
            finally
            {
                File.Delete(_root);
                Directory.CreateDirectory(_root);
            }
        }

        [Fact]
        public void FlushPendingSave_AfterSnapshotReplacement_ShouldDropStaleDynamicRows()
        {
            new SqliteConfigStore(DbPath).Save(new Dictionary<string, string>
            {
                ["SongRoot.0"] = Path.Combine(_root, "songs"),
                ["Key.X"] = "2",
                ["SystemKey.IncreaseScrollSpeed"] = "F1",
                ["MidiVelocity.36"] = "20",
            });

            var manager = CreateManager();
            manager.LoadConfig();
            Assert.Equal(2, manager.Config.KeyBindings["Key.X"]);

            // Reset bindings/MIDI state through the public setters so the next
            // snapshot no longer contains the stale dynamic rows.
            manager.SetKeyBindings(new KeyBindings());
            manager.SetSystemKeyBindings(new Dictionary<Keys, InputCommandType>());
            manager.SetMidiVelocityThreshold(36, 0);
            manager.FlushPendingSave();

            var rows = ReadRows();
            Assert.False(rows.ContainsKey("Key.X"));
            Assert.False(rows.ContainsKey("MidiVelocity.36"));
            Assert.NotEqual("F1", rows["SystemKey.IncreaseScrollSpeed"]);
        }

        [Fact]
        public void SetScrollSpeed_DeferredUntilFlush_ShouldPersistViaDbRoundTrip()
        {
            var manager = CreateManager();
            manager.LoadConfig();

            manager.SetScrollSpeed(250);
            Assert.Equal(250, manager.Config.ScrollSpeed);
            manager.FlushPendingSave();

            var reloaded = CreateManager();
            reloaded.LoadConfig();
            Assert.Equal(250, reloaded.Config.ScrollSpeed);
        }

        private static void WriteDatabaseWithUserVersion(string dbPath, long version)
        {
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {version};";
            command.ExecuteNonQuery();
        }
    }
}
