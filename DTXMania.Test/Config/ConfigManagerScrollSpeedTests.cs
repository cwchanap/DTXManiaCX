using System;
using System.IO;
using DTXMania.Game.Lib.Config;
using Xunit;

namespace DTXMania.Test.Config
{
    public class ConfigManagerScrollSpeedTests : IDisposable
    {
        private readonly string _tempPath;

        public ConfigManagerScrollSpeedTests()
        {
            _tempPath = Path.Combine(Path.GetTempPath(),
                "dtxmania-scrollspeed-" + Guid.NewGuid().ToString("N") + ".ini");
        }

        public void Dispose()
        {
            if (File.Exists(_tempPath))
                File.Delete(_tempPath);
        }

        [Theory]
        [InlineData(117, 100)]
        [InlineData(130, 150)]
        [InlineData(425, 400)]
        [InlineData(30, 50)]
        [Trait("Category", "ConfigManager")]
        public void SetScrollSpeed_SnapsToNearestStep(int input, int expected)
        {
            var cm = new ConfigManager();
            cm.SetScrollSpeed(_tempPath, input);
            Assert.Equal(expected, cm.Config.ScrollSpeed);
        }

        [Theory]
        [InlineData(0, 50)]
        [InlineData(-100, 50)]
        [InlineData(9999, 400)]
        [Trait("Category", "ConfigManager")]
        public void SetScrollSpeed_ClampsToRange(int input, int expected)
        {
            var cm = new ConfigManager();
            cm.SetScrollSpeed(_tempPath, input);
            Assert.Equal(expected, cm.Config.ScrollSpeed);
        }

        [Fact]
        [Trait("Category", "ConfigManager")]
        public void SetScrollSpeed_RaisesChangedEventWithOldAndNew()
        {
            var cm = new ConfigManager();
            cm.Config.ScrollSpeed = 100;

            ScrollSpeedChangedEventArgs? captured = null;
            cm.ScrollSpeedChanged += (_, e) => captured = e;

            cm.SetScrollSpeed(_tempPath, 200);

            Assert.NotNull(captured);
            Assert.Equal(100, captured!.OldPercent);
            Assert.Equal(200, captured.NewPercent);
        }

        [Fact]
        [Trait("Category", "ConfigManager")]
        public void SetScrollSpeed_NoOpWhenUnchanged_DoesNotRaiseEvent()
        {
            var cm = new ConfigManager();
            cm.Config.ScrollSpeed = 150;
            var raised = false;
            cm.ScrollSpeedChanged += (_, _) => raised = true;

            cm.SetScrollSpeed(_tempPath, 150);

            Assert.False(raised);
            Assert.False(File.Exists(_tempPath));
        }

        [Fact]
        [Trait("Category", "ConfigManager")]
        public void SetScrollSpeed_PersistsToConfigIni()
        {
            var cm = new ConfigManager();
            cm.SetScrollSpeed(_tempPath, 250);

            var roundTrip = new ConfigManager();
            roundTrip.LoadConfig(_tempPath);
            Assert.Equal(250, roundTrip.Config.ScrollSpeed);
        }

        [Fact]
        [Trait("Category", "ConfigManager")]
        public void LoadConfig_SnapsHandEditedScrollSpeedToNearestStep()
        {
            File.WriteAllText(_tempPath, "ScrollSpeed=133\n");

            var cm = new ConfigManager();
            cm.LoadConfig(_tempPath);

            Assert.Equal(150, cm.Config.ScrollSpeed);
        }

        [Fact]
        [Trait("Category", "ConfigManager")]
        public void AdjustScrollSpeed_StepsUp()
        {
            var cm = new ConfigManager();
            cm.Config.ScrollSpeed = 100;
            cm.AdjustScrollSpeed(_tempPath, +1);
            Assert.Equal(150, cm.Config.ScrollSpeed);
        }

        [Fact]
        [Trait("Category", "ConfigManager")]
        public void AdjustScrollSpeed_StepsDown()
        {
            var cm = new ConfigManager();
            cm.Config.ScrollSpeed = 200;
            cm.AdjustScrollSpeed(_tempPath, -1);
            Assert.Equal(150, cm.Config.ScrollSpeed);
        }

        [Fact]
        [Trait("Category", "ConfigManager")]
        public void AdjustScrollSpeed_FloorsAtMin()
        {
            var cm = new ConfigManager();
            cm.Config.ScrollSpeed = 50;
            cm.AdjustScrollSpeed(_tempPath, -1);
            Assert.Equal(50, cm.Config.ScrollSpeed);
        }

        [Fact]
        [Trait("Category", "ConfigManager")]
        public void AdjustScrollSpeed_CeilingsAtMax()
        {
            var cm = new ConfigManager();
            cm.Config.ScrollSpeed = 400;
            cm.AdjustScrollSpeed(_tempPath, +1);
            Assert.Equal(400, cm.Config.ScrollSpeed);
        }

        [Fact]
        [Trait("Category", "ConfigManager")]
        public void AdjustScrollSpeed_RaisesChangedEventWithOldAndNew()
        {
            var cm = new ConfigManager();
            cm.Config.ScrollSpeed = 100;

            ScrollSpeedChangedEventArgs? captured = null;
            cm.ScrollSpeedChanged += (_, e) => captured = e;

            cm.AdjustScrollSpeed(_tempPath, +1);

            Assert.NotNull(captured);
            Assert.Equal(100, captured!.OldPercent);
            Assert.Equal(150, captured.NewPercent);
        }
    }
}
