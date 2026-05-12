using System;
using System.Reflection;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.UI;
using Microsoft.Xna.Framework;
using Moq;
using Xunit;
using static DTXMania.Test.TestData.ReflectionHelpers;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Test.UI
{
    [Trait("Category", "Unit")]
    public class SongStatusPanelMethodTests
    {
        [Fact]
        public void FormatDuration_UnderOneHour_ShouldReturnMSSFormat()
        {
            var panel = new SongStatusPanel();
            var result = InvokePrivateMethod<string>(panel, "FormatDuration", 90.0);
            Assert.Equal("1:30", result);
        }

        [Fact]
        public void FormatDuration_ExactlyOneHour_ShouldReturnHMMSSFormat()
        {
            var panel = new SongStatusPanel();
            var result = InvokePrivateMethod<string>(panel, "FormatDuration", 3600.0);
            Assert.Equal("1:00:00", result);
        }

        [Fact]
        public void FormatDuration_OverOneHour_ShouldReturnHMMSSFormat()
        {
            var panel = new SongStatusPanel();
            var result = InvokePrivateMethod<string>(panel, "FormatDuration", 5430.0);
            Assert.Equal("1:30:30", result);
        }

        [Fact]
        public void FormatDuration_Zero_ShouldReturnZeroMinutes()
        {
            var panel = new SongStatusPanel();
            var result = InvokePrivateMethod<string>(panel, "FormatDuration", 0.0);
            Assert.Equal("0:00", result);
        }

        [Fact]
        public void GetCurrentScore_NullSong_ShouldReturnNull()
        {
            var panel = new SongStatusPanel();
            var result = InvokePrivateMethod<SongScore>(panel, "GetCurrentScore", null!, 0);
            Assert.Null(result);
        }

        [Fact]
        public void GetCurrentScore_NullScoresArrayEntry_ShouldReturnNull()
        {
            var panel = new SongStatusPanel();
            var node = new SongListNode();
            node.Scores[0] = null;

            var result = InvokePrivateMethod<SongScore>(panel, "GetCurrentScore", node, 0);
            Assert.Null(result);
        }

        [Fact]
        public void GetCurrentScore_NegativeDifficulty_ShouldReturnNull()
        {
            var panel = new SongStatusPanel();
            var node = new SongListNode();
            node.Scores[0] = new SongScore();

            var result = InvokePrivateMethod<SongScore>(panel, "GetCurrentScore", node, -1);
            Assert.Null(result);
        }

        [Fact]
        public void GetCurrentScore_DifficultyOutOfRange_ShouldReturnNull()
        {
            var panel = new SongStatusPanel();
            var node = new SongListNode();
            node.Scores[0] = new SongScore();

            var result = InvokePrivateMethod<SongScore>(panel, "GetCurrentScore", node, 5);
            Assert.Null(result);
        }

        [Fact]
        public void GetCurrentScore_ValidDifficulty_ShouldReturnScore()
        {
            var panel = new SongStatusPanel();
            var node = new SongListNode();
            var expected = new SongScore { PlayCount = 42 };
            node.Scores[0] = expected;

            var result = InvokePrivateMethod<SongScore>(panel, "GetCurrentScore", node, 0);
            Assert.Same(expected, result);
            Assert.Equal(42, result!.PlayCount);
        }

        [Fact]
        public void GetInstrumentFromDifficulty_AlwaysReturnsDrums()
        {
            var panel = new SongStatusPanel();
            for (int i = 0; i < 5; i++)
            {
                var result = InvokePrivateMethod<string>(panel, "GetInstrumentFromDifficulty", i);
                Assert.Equal("DRUMS", result);
            }
        }

        [Fact]
        public void ReleaseManagedTexture_NullTexture_ShouldNotThrow()
        {
            var method = typeof(SongStatusPanel).GetMethod(
                "ReleaseManagedTexture",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            ITexture? texture = null;
            var args = new object[] { texture };
            var exception = Record.Exception(() => method!.Invoke(null, args));
            Assert.Null(exception);
        }

        [Fact]
        public void ReleaseManagedTexture_NonNullTexture_ShouldCallRemoveReference()
        {
            var method = typeof(SongStatusPanel).GetMethod(
                "ReleaseManagedTexture",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var mock = new Mock<ITexture>();
            ITexture? texture = mock.Object;
            var args = new object[] { texture };
            method!.Invoke(null, args);

            mock.Verify(t => t.RemoveReference(), Times.Once);
        }
    }
}
