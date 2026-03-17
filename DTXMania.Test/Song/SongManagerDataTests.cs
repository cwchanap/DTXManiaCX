using System.Drawing;
using DTXMania.Game.Lib.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Tests for data classes defined in SongManager.cs:
    /// EnumerationProgress, EnumerationProgressEventArgs, BoxDefinition
    /// </summary>
    public class EnumerationProgressTests
    {
        [Fact]
        public void EnumerationProgress_DefaultValues_ShouldBeCorrect()
        {
            var progress = new EnumerationProgress();
            Assert.Equal("", progress.CurrentFile);
            Assert.Equal("", progress.CurrentDirectory);
            Assert.Equal(0, progress.ProcessedCount);
            Assert.Equal(0, progress.DiscoveredSongs);
        }

        [Fact]
        public void EnumerationProgress_SetProperties_ShouldRetainValues()
        {
            var progress = new EnumerationProgress
            {
                CurrentFile = "/music/song.dtx",
                CurrentDirectory = "/music",
                ProcessedCount = 50,
                DiscoveredSongs = 42
            };

            Assert.Equal("/music/song.dtx", progress.CurrentFile);
            Assert.Equal("/music", progress.CurrentDirectory);
            Assert.Equal(50, progress.ProcessedCount);
            Assert.Equal(42, progress.DiscoveredSongs);
        }
    }

    public class EnumerationProgressEventArgsTests
    {
        [Fact]
        public void EnumerationProgressEventArgs_Constructor_ShouldSetProgress()
        {
            var progress = new EnumerationProgress
            {
                CurrentFile = "test.dtx",
                ProcessedCount = 10
            };

            var args = new EnumerationProgressEventArgs(progress);

            Assert.Equal(progress, args.Progress);
            Assert.Equal("test.dtx", args.Progress.CurrentFile);
            Assert.Equal(10, args.Progress.ProcessedCount);
        }
    }

    public class BoxDefinitionTests
    {
        [Fact]
        public void BoxDefinition_DefaultValues_ShouldBeCorrect()
        {
            var box = new BoxDefinition();
            Assert.Equal("", box.Title);
            Assert.Equal("", box.Genre);
            Assert.Equal("", box.SkinPath);
            Assert.Equal(Color.Black, box.BackgroundColor);
            Assert.Equal(Color.White, box.TextColor);
        }

        [Fact]
        public void BoxDefinition_SetProperties_ShouldRetainValues()
        {
            var box = new BoxDefinition
            {
                Title = "My Box",
                Genre = "Rock",
                SkinPath = "/skins/rock",
                BackgroundColor = Color.DarkBlue,
                TextColor = Color.Yellow
            };

            Assert.Equal("My Box", box.Title);
            Assert.Equal("Rock", box.Genre);
            Assert.Equal("/skins/rock", box.SkinPath);
            Assert.Equal(Color.DarkBlue, box.BackgroundColor);
            Assert.Equal(Color.Yellow, box.TextColor);
        }
    }
}
