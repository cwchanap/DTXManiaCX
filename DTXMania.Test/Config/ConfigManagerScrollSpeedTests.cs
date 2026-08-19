using System;
using System.Collections.Generic;
using System.IO;
using DTXMania.Game.Lib.Config;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DTXMania.Test.Config
{
    /// <summary>
    /// Scroll-speed setter/snap/event/persistence behavior against the SQLite
    /// config store. Each test owns a unique temp root with its own
    /// config.db + legacy Config.ini pair (the internal ConfigManager test
    /// seam), so no test touches the real app-data directory. The app-data
    /// root env var is sandboxed because LoadConfig's normalization resolves
    /// and creates default app-data directories.
    /// </summary>
    [Collection("AppPaths")]
    public class ConfigManagerScrollSpeedTests : IDisposable
    {
        private readonly string _root;
        private readonly string? _previousAppDataRoot;

        public ConfigManagerScrollSpeedTests()
        {
            _root = Path.Combine(Path.GetTempPath(),
                "dtxmania-scrollspeed-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _previousAppDataRoot = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", _root);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", _previousAppDataRoot);
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private string DbPath => Path.Combine(_root, "config.db");
        private string IniPath => Path.Combine(_root, "Config.ini");

        private ConfigManager CreateManager() => new(DbPath, IniPath);

        [Theory]
        [InlineData(117, 100)]
        [InlineData(130, 150)]
        [InlineData(425, 400)]
        [InlineData(30, 50)]
        [Trait("Category", "ConfigManager")]
        public void SetScrollSpeed_SnapsToNearestStep(int input, int expected)
        {
            var cm = CreateManager();
            cm.SetScrollSpeed(input);
            Assert.Equal(expected, cm.Config.ScrollSpeed);
        }

        [Theory]
        [InlineData(0, 50)]
        [InlineData(-100, 50)]
        [InlineData(9999, 400)]
        [Trait("Category", "ConfigManager")]
        public void SetScrollSpeed_ClampsToRange(int input, int expected)
        {
            var cm = CreateManager();
            cm.SetScrollSpeed(input);
            Assert.Equal(expected, cm.Config.ScrollSpeed);
        }

        [Fact]
        [Trait("Category", "ConfigManager")]
        public void SetScrollSpeed_RaisesChangedEventWithOldAndNew()
        {
            var cm = CreateManager();
            cm.Config.ScrollSpeed = 100;

            ScrollSpeedChangedEventArgs? captured = null;
            cm.ScrollSpeedChanged += (_, e) => captured = e;

            cm.SetScrollSpeed(200);

            Assert.NotNull(captured);
            Assert.Equal(100, captured!.OldPercent);
            Assert.Equal(200, captured.NewPercent);
        }

        [Fact]
        [Trait("Category", "ConfigManager")]
        public void SetScrollSpeed_NoOpWhenUnchanged_DoesNotRaiseEvent()
        {
            var cm = CreateManager();
            cm.LoadConfig();
            cm.Config.ScrollSpeed = 150;
            var raised = false;
            cm.ScrollSpeedChanged += (_, _) => raised = true;

            cm.SetScrollSpeed(150);

            Assert.False(raised);
        }

        [Fact]
        [Trait("Category", "ConfigManager")]
        public void SetScrollSpeed_PersistsToConfigDb()
        {
            var cm = CreateManager();
            cm.LoadConfig();
            cm.SetScrollSpeed(250);
            cm.FlushPendingSave();

            var roundTrip = CreateManager();
            roundTrip.LoadConfig();
            Assert.Equal(250, roundTrip.Config.ScrollSpeed);
        }

        [Fact]
        [Trait("Category", "ConfigManager")]
        public void LoadConfig_SnapsHandEditedScrollSpeedToNearestStep()
        {
            // A hand-edited legacy INI (or DB row) holding a non-step value is
            // snapped on load; the import persists the snapped snapshot.
            File.WriteAllText(IniPath, "ScrollSpeed=133\n");

            var cm = CreateManager();
            cm.LoadConfig();

            Assert.Equal(150, cm.Config.ScrollSpeed);
        }

        [Fact]
        [Trait("Category", "ConfigManager")]
        public void AdjustScrollSpeed_StepsUp()
        {
            var cm = CreateManager();
            cm.Config.ScrollSpeed = 100;
            cm.AdjustScrollSpeed(+1);
            Assert.Equal(150, cm.Config.ScrollSpeed);
        }

        [Fact]
        [Trait("Category", "ConfigManager")]
        public void AdjustScrollSpeed_StepsDown()
        {
            var cm = CreateManager();
            cm.Config.ScrollSpeed = 200;
            cm.AdjustScrollSpeed(-1);
            Assert.Equal(150, cm.Config.ScrollSpeed);
        }

        [Fact]
        [Trait("Category", "ConfigManager")]
        public void AdjustScrollSpeed_FloorsAtMin()
        {
            var cm = CreateManager();
            cm.Config.ScrollSpeed = 50;
            cm.AdjustScrollSpeed(-1);
            Assert.Equal(50, cm.Config.ScrollSpeed);
        }

        [Fact]
        [Trait("Category", "ConfigManager")]
        public void AdjustScrollSpeed_CeilingsAtMax()
        {
            var cm = CreateManager();
            cm.Config.ScrollSpeed = 400;
            cm.AdjustScrollSpeed(+1);
            Assert.Equal(400, cm.Config.ScrollSpeed);
        }

        [Fact]
        [Trait("Category", "ConfigManager")]
        public void AdjustScrollSpeed_RaisesChangedEventWithOldAndNew()
        {
            var cm = CreateManager();
            cm.Config.ScrollSpeed = 100;

            ScrollSpeedChangedEventArgs? captured = null;
            cm.ScrollSpeedChanged += (_, e) => captured = e;

            cm.AdjustScrollSpeed(+1);

            Assert.NotNull(captured);
            Assert.Equal(100, captured!.OldPercent);
            Assert.Equal(150, captured.NewPercent);
        }

        [Fact]
        [Trait("Category", "ConfigManager")]
        public void FlushPendingSave_FailureStillRetainsInMemoryAndFiredEvent()
        {
            var cm = CreateManager();
            cm.LoadConfig();
            cm.Config.ScrollSpeed = 100;

            ScrollSpeedChangedEventArgs? captured = null;
            cm.ScrollSpeedChanged += (_, e) => captured = e;

            // SetScrollSpeed defers the write; event fires immediately
            cm.SetScrollSpeed(200);

            // In-memory value should be updated
            Assert.Equal(200, cm.Config.ScrollSpeed);

            // Event should have fired
            Assert.NotNull(captured);
            Assert.Equal(100, captured!.OldPercent);
            Assert.Equal(200, captured.NewPercent);

            // Break the store's directory so the flush's save fails.
            Directory.Delete(_root, recursive: true);
            File.WriteAllText(_root, "blocker");
            try
            {
                // Flush attempts the write — should NOT throw; failure is caught internally
                cm.FlushPendingSave();

                // In-memory value should still be updated despite save failure
                Assert.Equal(200, cm.Config.ScrollSpeed);
            }
            finally
            {
                File.Delete(_root);
                Directory.CreateDirectory(_root);
            }
        }

        [Fact]
        [Trait("Category", "ConfigManager")]
        public void FlushPendingSave_FailurePreservesPendingStateForRetry()
        {
            // After a failed flush, the pending marker must remain set so the
            // next flush retries against the same database.
            var cm = CreateManager();
            cm.LoadConfig();
            cm.Config.ScrollSpeed = 100;
            cm.SetScrollSpeed(200);

            // Break the store's directory: the save's directory creation throws.
            Directory.Delete(_root, recursive: true);
            File.WriteAllText(_root, "blocker");
            try
            {
                // First flush fails
                cm.FlushPendingSave();

                // In-memory value is still updated
                Assert.Equal(200, cm.Config.ScrollSpeed);

                // Remove blocker so the next write can succeed. ClearAllPools
                // first: the pooled handle from the initial load points at the
                // deleted inode and SQLite rejects writes to it ("attempt to
                // write a readonly database").
                SqliteConnection.ClearAllPools();
                File.Delete(_root);
                Directory.CreateDirectory(_root);

                // Second flush should succeed — pending state was preserved
                cm.FlushPendingSave();
            }
            finally
            {
                if (File.Exists(_root))
                    File.Delete(_root);
                if (!Directory.Exists(_root))
                    Directory.CreateDirectory(_root);
            }

            // Verify the value was persisted
            var roundTrip = CreateManager();
            roundTrip.LoadConfig();
            Assert.Equal(200, roundTrip.Config.ScrollSpeed);
        }
    }
}
