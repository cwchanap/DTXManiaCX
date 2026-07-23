using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using DTXMania.Game.Lib.Config;
using Xunit;

namespace DTXMania.Test.Config
{
    public sealed class PlaySpeedAndPitchConfigTests : IDisposable
    {
        private readonly string _tempPath = Path.Combine(
            Path.GetTempPath(),
            "dtxmania-playback-modifiers-" + Guid.NewGuid().ToString("N") + ".ini");

        public void Dispose()
        {
            if (File.Exists(_tempPath))
                File.Delete(_tempPath);
        }

        [Fact]
        public void ConfigData_DefaultsToNormalSpeedAndUnshiftedPitch()
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
        public void PlaySpeedRange_SnapAndClampUsesFivePercentSteps(int input, int expected)
        {
            Assert.Equal(expected, PlaySpeedRange.SnapAndClamp(input));
        }

        [Theory]
        [InlineData(-13, -12)]
        [InlineData(-12, -12)]
        [InlineData(0, 0)]
        [InlineData(12, 12)]
        [InlineData(13, 12)]
        public void PitchRange_SnapAndClampUsesSemitoneSteps(int input, int expected)
        {
            Assert.Equal(expected, PitchRange.SnapAndClamp(input));
        }

        [Fact]
        public void RangeFormat_IsInvariantAndCanonical()
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
        public void SaveAndLoad_RoundTripsCanonicalValues()
        {
            var manager = new ConfigManager();
            manager.LoadConfig(_tempPath);

            manager.SetPlaySpeedPercent(128);
            manager.SetPitchSemitones(99);
            manager.FlushPendingSave();

            var saved = File.ReadAllText(_tempPath);
            Assert.Contains("PlaySpeedPercent=130", saved);
            Assert.Contains("PitchSemitones=12", saved);

            var roundTrip = new ConfigManager();
            roundTrip.LoadConfig(_tempPath);
            Assert.Equal(130, roundTrip.Config.PlaySpeedPercent);
            Assert.Equal(12, roundTrip.Config.PitchSemitones);
        }

        [Fact]
        public void LoadConfig_MalformedValuesKeepDefaults()
        {
            File.WriteAllText(_tempPath,
                "PlaySpeedPercent=not-a-number\n" +
                "PitchSemitones=also-not-a-number\n");

            var manager = new ConfigManager();
            manager.LoadConfig(_tempPath);

            Assert.Equal(PlaySpeedRange.Default, manager.Config.PlaySpeedPercent);
            Assert.Equal(PitchRange.Default, manager.Config.PitchSemitones);
        }

        [Fact]
        public void LoadConfig_SnapsAndClampsHandEditedValues()
        {
            File.WriteAllText(_tempPath,
                "PlaySpeedPercent=127\n" +
                "PitchSemitones=-99\n");

            var manager = new ConfigManager();
            manager.LoadConfig(_tempPath);

            Assert.Equal(125, manager.Config.PlaySpeedPercent);
            Assert.Equal(-12, manager.Config.PitchSemitones);
        }

        [Fact]
        public void UnchangedSetters_DoNotScheduleDeferredWrite()
        {
            var manager = new ConfigManager();
            manager.LoadConfig(_tempPath);

            manager.SetPlaySpeedPercent(PlaySpeedRange.Default);
            manager.SetPitchSemitones(PitchRange.Default);

            var pendingField = typeof(ConfigManager).GetField(
                "_pendingSavePath",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(pendingField);
            Assert.Null(pendingField!.GetValue(manager));
        }
    }
}