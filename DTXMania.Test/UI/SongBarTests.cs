using Xunit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Reflection;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Resources;
using DTXMania.Test.Helpers;
using DTXMania.Game.Lib.Song.Entities;
using Moq;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Unit tests for SongBar component (Phase 4 enhancement)
    /// </summary>
    [Trait("Category", "UI")]
    public class SongBarTests : IDisposable
    {
        private readonly SongBar _songBar;
        private readonly SongListNode _testSongNode;

        public SongBarTests()
        {

            // Create test song and chart
            var testSong = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song",
                Artist = "Test Artist",
                Genre = "Test Genre"
            };
            
            var testChart = new SongChart
            {
                FilePath = "test.dtx",
                BPM = 120.0,
                DrumLevel = 85
            };
            
            // Create test song node
            _testSongNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Test Song",
                DatabaseSong = testSong,
                DatabaseChart = testChart,
                Scores = new SongScore[]
                {
                    new SongScore
                    {
                        Instrument = EInstrumentPart.DRUMS,
                        BestScore = 950000,
                        BestRank = 85,
                        FullCombo = true,
                        PlayCount = 5
                    }
                }
            };

            // Create song bar
            _songBar = new SongBar();
        }

        [Fact]
        public void Constructor_ShouldInitializeWithDefaultValues()
        {
            var songBar = new SongBar();

            Assert.NotNull(songBar);
            Assert.Equal(new Vector2(DTXMania.Game.Lib.UI.Layout.SongSelectionUILayout.SongBars.BarWidth, DTXMania.Game.Lib.UI.DTXManiaVisualTheme.Layout.SongBarHeight), songBar.Size);
            Assert.False(songBar.IsSelected);
            Assert.False(songBar.IsCenter);
            Assert.Equal(0, songBar.CurrentDifficulty);
        }

        [Fact]
        public void SongNode_WhenSet_ShouldUpdateAndInvalidateTextures()
        {
            // Act
            _songBar.SongNode = _testSongNode;

            // Assert
            Assert.Equal(_testSongNode, _songBar.SongNode);
        }

        [Fact]
        public void CurrentDifficulty_ShouldClampToValidRange()
        {
            // Test lower bound
            _songBar.CurrentDifficulty = -1;
            Assert.Equal(0, _songBar.CurrentDifficulty);

            // Test upper bound
            _songBar.CurrentDifficulty = 10;
            Assert.Equal(4, _songBar.CurrentDifficulty);

            // Test valid value
            _songBar.CurrentDifficulty = 2;
            Assert.Equal(2, _songBar.CurrentDifficulty);
        }

        [Fact]
        public void IsSelected_WhenChanged_ShouldUpdateVisualState()
        {
            // Arrange
            _songBar.SongNode = _testSongNode;

            // Act
            _songBar.IsSelected = true;

            // Assert
            Assert.True(_songBar.IsSelected);
        }

        [Fact]
        public void IsCenter_WhenChanged_ShouldUpdateVisualState()
        {
            // Arrange
            _songBar.SongNode = _testSongNode;

            // Act
            _songBar.IsCenter = true;

            // Assert
            Assert.True(_songBar.IsCenter);
        }

        [Fact]
        public void UpdateVisualState_WithSelectedAndCenter_ShouldSetCorrectColors()
        {
            // Arrange
            _songBar.SongNode = _testSongNode;

            // Act
            _songBar.IsSelected = true;
            _songBar.IsCenter = true;
            _songBar.UpdateVisualState();

            // Assert - Visual state updated (no direct way to test colors, but method should not throw)
            Assert.True(_songBar.IsSelected);
            Assert.True(_songBar.IsCenter);
        }



        [Theory]
        [InlineData(NodeType.Score)]
        [InlineData(NodeType.Box)]
        [InlineData(NodeType.BackBox)]
        [InlineData(NodeType.Random)]
        public void SongBar_ShouldHandleAllNodeTypes(NodeType nodeType)
        {
            // Arrange
            var testNode = new SongListNode
            {
                Type = nodeType,
                Title = $"Test {nodeType}"
            };

            // Act
            _songBar.SongNode = testNode;

            // Assert
            Assert.Equal(testNode, _songBar.SongNode);
            Assert.Equal(nodeType, _songBar.SongNode.Type);
        }

        [Fact]
        public void Draw_WhenScoreNodeHasTextures_ShouldDrawTitlePreviewAndClearLamp()
        {
            var titleTexture = CreateTextureMock();
            var previewTexture = CreateTextureMock();
            var clearLampTexture = CreateTextureMock();

            _songBar.SongNode = _testSongNode;
            _songBar.SetTextures(titleTexture.Object, previewTexture.Object, clearLampTexture.Object);
            _songBar.Activate();

            _songBar.Draw(null!, 0);

            clearLampTexture.Verify(x => x.Draw(It.IsAny<SpriteBatch>(), It.IsAny<Rectangle>(), It.IsAny<Rectangle?>(), It.IsAny<Color>(), It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(), It.IsAny<float>()), Times.Once);
            previewTexture.Verify(x => x.Draw(It.IsAny<SpriteBatch>(), It.IsAny<Rectangle>(), It.IsAny<Rectangle?>(), It.IsAny<Color>(), It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(), It.IsAny<float>()), Times.Once);
            titleTexture.Verify(x => x.Draw(It.IsAny<SpriteBatch>(), It.IsAny<Vector2>()), Times.Once);
        }

        [Fact]
        public void Draw_WhenNodeIsNotScore_ShouldSkipClearLamp()
        {
            var titleTexture = CreateTextureMock();
            var previewTexture = CreateTextureMock();
            var clearLampTexture = CreateTextureMock();
            var folderNode = new SongListNode { Type = NodeType.Box, Title = "Folder" };

            _songBar.SongNode = folderNode;
            _songBar.SetTextures(titleTexture.Object, previewTexture.Object, clearLampTexture.Object);
            _songBar.Activate();

            _songBar.Draw(null!, 0);

            clearLampTexture.Verify(x => x.Draw(It.IsAny<SpriteBatch>(), It.IsAny<Rectangle>(), It.IsAny<Rectangle?>(), It.IsAny<Color>(), It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(), It.IsAny<float>()), Times.Never);
            previewTexture.Verify(x => x.Draw(It.IsAny<SpriteBatch>(), It.IsAny<Rectangle>(), It.IsAny<Rectangle?>(), It.IsAny<Color>(), It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(), It.IsAny<float>()), Times.Once);
            titleTexture.Verify(x => x.Draw(It.IsAny<SpriteBatch>(), It.IsAny<Vector2>()), Times.Once);
        }

        [Theory]
        [InlineData(null, "Unknown")]
        [InlineData(NodeType.BackBox, ".. (Back)")]
        [InlineData(NodeType.Box, "[Folder]")]
        [InlineData(NodeType.Random, "*** RANDOM SELECT ***")]
        [InlineData(NodeType.Score, "Song")]
        public void GetDisplayText_ShouldMapNodeTypes(NodeType? nodeType, string expected)
        {
            _songBar.SongNode = nodeType.HasValue
                ? new SongListNode { Type = nodeType.Value, Title = nodeType == NodeType.Box ? "Folder" : "Song" }
                : null!;

            var text = InvokePrivate<string>(_songBar, "GetDisplayText");

            Assert.Equal(expected, text);
        }

        [Fact]
        public void Draw_WhenSongNodeIsNull_ShouldReturnWithoutThrowing()
        {
            _songBar.Activate();

            var ex = Record.Exception(() => _songBar.Draw(null!, 0));

            Assert.Null(ex);
        }

        [Fact]
        public void SongNode_WhenChanged_ShouldInvalidateAllCachedTextures()
        {
            _songBar.SetTextures(CreateTextureMock().Object, CreateTextureMock().Object, CreateTextureMock().Object);

            _songBar.SongNode = _testSongNode;

            Assert.Null(GetField<ITexture?>(_songBar, "_titleTexture"));
            Assert.Null(GetField<ITexture?>(_songBar, "_previewImageTexture"));
            Assert.Null(GetField<ITexture?>(_songBar, "_clearLampTexture"));
        }

        [Fact]
        public void CurrentDifficulty_WhenChanged_ShouldInvalidateOnlyClearLamp()
        {
            var titleTexture = CreateTextureMock().Object;
            var previewTexture = CreateTextureMock().Object;
            var clearLampTexture = CreateTextureMock().Object;
            _songBar.SetTextures(titleTexture, previewTexture, clearLampTexture);

            _songBar.CurrentDifficulty = 2;

            Assert.Same(titleTexture, GetField<ITexture?>(_songBar, "_titleTexture"));
            Assert.Same(previewTexture, GetField<ITexture?>(_songBar, "_previewImageTexture"));
            Assert.Null(GetField<ITexture?>(_songBar, "_clearLampTexture"));
        }

        [Fact]
        public void InitializeGraphicsGenerator_WithNullRenderTarget_ShouldDisableGenerator()
        {
            var ex = Record.Exception(() => _songBar.InitializeGraphicsGenerator(null!, null!));

            Assert.Null(ex);
            Assert.Null(GetField<object?>(_songBar, "_graphicsGenerator"));
        }

        [Fact]
        public void DrawNodeTypeIndicator_WhenWhitePixelIsMissing_ShouldReturnWithoutThrowing()
        {
            _songBar.SongNode = _testSongNode;

            var ex = Record.Exception(() => InvokePrivate<object?>(_songBar, "DrawNodeTypeIndicator", null!, new Rectangle(0, 0, 100, 32)));

            Assert.Null(ex);
        }

        private static Mock<ITexture> CreateTextureMock()
        {
            var texture = new Mock<ITexture>();
            texture.SetupGet(x => x.Height).Returns(32);
            texture.Setup(x => x.Draw(It.IsAny<SpriteBatch>(), It.IsAny<Vector2>()));
            texture.Setup(x => x.Draw(It.IsAny<SpriteBatch>(), It.IsAny<Rectangle>(), It.IsAny<Rectangle?>(), It.IsAny<Color>(), It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(), It.IsAny<float>()));
            return texture;
        }

        private static T InvokePrivate<T>(object target, string methodName, params object[] args)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (T)method!.Invoke(target, args)!;
        }

        private static T GetField<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (T)field!.GetValue(target)!;
        }



        public void Dispose()
        {
            _songBar?.Dispose();
        }
    }
}
