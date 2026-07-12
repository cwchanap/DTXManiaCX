using System.Linq;
using DTXMania.Game.Lib.Resources;
using Xunit;

namespace DTXMania.Test.Resources
{
    [Trait("Category", "Unit")]
    public class SoundPathTests
    {
        [Fact]
        public void GetAllSoundPaths_ShouldReturnEightDistinctOggPathsUnderSounds()
        {
            var paths = SoundPath.GetAllSoundPaths();

            Assert.Equal(8, paths.Length);
            Assert.Equal(8, paths.Distinct().Count());
            Assert.All(paths, p => Assert.StartsWith("Sounds/", p));
            Assert.All(paths, p => Assert.EndsWith(".ogg", p));
        }

        [Fact]
        public void Constants_ShouldMatchLegacyOnDiskFileNames()
        {
            Assert.Equal("Sounds/Move.ogg", SoundPath.CursorMove);
            Assert.Equal("Sounds/Decide.ogg", SoundPath.Decide);
            Assert.Equal("Sounds/Game start.ogg", SoundPath.GameStart);
            Assert.Equal("Sounds/Now loading.ogg", SoundPath.NowLoading);
            Assert.Equal("Sounds/Stage Clear.ogg", SoundPath.StageClear);
            Assert.Equal("Sounds/Full Combo.ogg", SoundPath.FullCombo);
            Assert.Equal("Sounds/Excellent.ogg", SoundPath.Excellent);
            Assert.Equal("Sounds/New Record.ogg", SoundPath.NewRecord);
        }
    }
}
