using System;
using System.Globalization;
using System.IO;
using DTXMania.Game.Lib.Config;
using DTXMania.Test.TestData;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DTXMania.Test.Config
{
    [Trait("Category", "Unit")]
    [Collection("AppPaths")]
    public sealed class PlaySpeedAndPitchConfigTests : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "dtxmania-playback-modifiers-" + Guid.NewGuid().ToString("N"));
        private readonly string? _previousAppDataRoot;

        public PlaySpeedAndPitchConfigTests()
        {
            Directory.CreateDirectory(_root);
            // Sandbox the app-data root: LoadConfig normalization creates
            // default app-data directories.
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

        [Fact]
        public void ConfigData_ShouldDefaultToNormalSpeedAndUnshiftedPitch()
        {
            var config = new ConfigData();

            Assert.Equal(50, PlaySpeedRange.Min);
            Assert.Equal(150, PlaySpeedRange.Max);
            Assert.Equal(5, PlaySpeedRange.Step);
            Assert.Equal(100, PlaySpeedRange.Default);
            Assert.Equal(-12, PitchRange.Min);
            Assert.Equal(12, PitchRange.Max);
            Assert.Equal(1, PitchRange.Step);
            Assert.Equal(0, PitchRange.Default);
            Assert.Equal(PlaySpeedRange.Default, config.PlaySpeedPercent);
            Assert.Equal(PitchRange.Default, config.PitchSemitones);
        }

        [Theory]
        [InlineData(49, 50)]
        [InlineData(52, 50)]
        [InlineData(53, 55)]
        [InlineData(147, 145)]
        [InlineData(148, 150)]
        [InlineData(151, 150)]
        public void PlaySpeedRange_SnapAndClamp_ShouldUseFivePercentSteps(int input, int expected)
        {
            Assert.Equal(expected, PlaySpeedRange.SnapAndClamp(input));
        }

        [Theory]
        [InlineData(-13, -12)]
        [InlineData(-12, -12)]
        [InlineData(0, 0)]
        [InlineData(12, 12)]
        [InlineData(13, 12)]
        public void PitchRange_SnapAndClamp_ShouldUseSemitoneSteps(int input, int expected)
        {
            Assert.Equal(expected, PitchRange.SnapAndClamp(input));
        }

        [Fact]
        public void RangeFormat_ShouldBeInvariantAndCanonical()
        {
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");

                Assert.Equal("0.50x", PlaySpeedRange.Format(50));
                Assert.Equal("1.00x", PlaySpeedRange.Format(100));
                Assert.Equal("1.50x", PlaySpeedRange.Format(150));
                Assert.Equal("-12 st", PitchRange.Format(-12));
                Assert.Equal("0 st", PitchRange.Format(0));
                Assert.Equal("+12 st", PitchRange.Format(12));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }

        [Fact]
        public void SaveAndLoad_ShouldRoundTripCanonicalValues()
        {
            var manager = CreateManager();
            manager.LoadConfig();

            manager.SetPlaySpeedPercent(128);
            manager.SetPitchSemitones(99);
            manager.FlushPendingSave();

            var rows = new SqliteConfigStore(DbPath).Load();
            Assert.Equal("130", rows["PlaySpeedPercent"]);
            Assert.Equal("12", rows["PitchSemitones"]);

            var roundTrip = CreateManager();
            roundTrip.LoadConfig();
            Assert.Equal(130, roundTrip.Config.PlaySpeedPercent);
            Assert.Equal(12, roundTrip.Config.PitchSemitones);
        }

        [Fact]
        public void LoadConfig_WithMalformedValues_ShouldKeepDefaults()
        {
            File.WriteAllText(IniPath,
                "PlaySpeedPercent=not-a-number\n" +
                "PitchSemitones=also-not-a-number\n");

            var manager = CreateManager();
            manager.LoadConfig();

            Assert.Equal(PlaySpeedRange.Default, manager.Config.PlaySpeedPercent);
            Assert.Equal(PitchRange.Default, manager.Config.PitchSemitones);
        }

        [Fact]
        public void LoadConfig_WithHandEditedValues_ShouldSnapAndClamp()
        {
            File.WriteAllText(IniPath,
                "PlaySpeedPercent=127\n" +
                "PitchSemitones=-99\n");

            var manager = CreateManager();
            manager.LoadConfig();

            Assert.Equal(125, manager.Config.PlaySpeedPercent);
            Assert.Equal(-12, manager.Config.PitchSemitones);
        }

        [Fact]
        public void UnchangedSetters_ShouldNotScheduleDeferredWrite()
        {
            var manager = CreateManager();
            manager.LoadConfig();

            manager.SetPlaySpeedPercent(PlaySpeedRange.Default);
            manager.SetPitchSemitones(PitchRange.Default);

            Assert.False(ReflectionHelpers.GetPrivateField<bool>(manager, "_hasPendingSave"));
        }
    }
}