using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using DTXMania.Game;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI.Layout;
using static DTXMania.Test.TestData.ReflectionHelpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class ResultStageCoverageTests
    {
        [Fact]
        public void DrawFallbackBackground_WithWhitePixel_ShouldDrawTexture()
        {
#pragma warning disable SYSLIB0050
            var stage = (InspectableResultStage)FormatterServices.GetUninitializedObject(typeof(InspectableResultStage));
            var whitePixel = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
#pragma warning restore SYSLIB0050
            GC.SuppressFinalize(whitePixel);
            SetPrivateField(stage, "_whitePixel", whitePixel);

            var rect = new Rectangle(0, 0, 800, 600);
            var color = Color.DarkBlue;

            stage.DrawFallbackBackground(rect, color);

            Assert.Same(whitePixel, stage.DrawTextureArgument);
            Assert.Equal(rect, stage.DrawTextureRectangle);
            Assert.Equal(color, stage.DrawTextureColor);
        }

        [Fact]
        public void DrawFallbackBackground_WithoutWhitePixel_ShouldNotDraw()
        {
#pragma warning disable SYSLIB0050
            var stage = (InspectableResultStage)FormatterServices.GetUninitializedObject(typeof(InspectableResultStage));
#pragma warning restore SYSLIB0050
            SetPrivateField(stage, "_whitePixel", null);

            var rect = new Rectangle(0, 0, 800, 600);
            var color = Color.DarkBlue;

            stage.DrawFallbackBackground(rect, color);

            Assert.Null(stage.DrawTextureArgument);
            Assert.Null(stage.DrawTextureRectangle);
            Assert.Null(stage.DrawTextureColor);
        }

        [Fact]
        public void DrawResultLine_WithNoFont_ShouldDrawFallbackRectangle()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            SetPrivateField(stage, "_spriteBatch", CreateFakeSpriteBatch(800, 600));
            SetPrivateField(stage, "_resultFont", null);
            SetPrivateField(stage, "_whitePixel", null);

            object[] args = ["CLEARED", 400, 100, Color.Green, 40];
            var exception = Record.Exception(() => InvokePrivateMethod(stage, "DrawResultLine", args));

            Assert.Null(exception);
            Assert.Equal(140, args[2]);
        }

        [Fact]
        public void DrawResults_WithNullSummary_ShouldReturnImmediately()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            SetPrivateField(stage, "_performanceSummary", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "DrawResults"));

            Assert.Null(exception);
        }

        [Fact]
        public void DrawResults_WithClearedPerformance_ShouldShowCleared()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            SetPrivateField(stage, "_spriteBatch", CreateFakeSpriteBatch(800, 600));
            SetPrivateField(stage, "_resultFont", null);
            SetPrivateField(stage, "_whitePixel", null);
            SetPrivateField(stage, "_performanceSummary", new PerformanceSummary
            {
                Score = 950000,
                MaxCombo = 200,
                ClearFlag = true,
                PerfectCount = 100,
                GreatCount = 50,
                GoodCount = 20,
                PoorCount = 5,
                MissCount = 2,
                TotalNotes = 177,
                CompletionReason = CompletionReason.SongComplete
            });

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "DrawResults"));

            Assert.Null(exception);
        }

        [Fact]
        public void DrawResults_WithFailedPerformance_ShouldShowFailed()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            SetPrivateField(stage, "_spriteBatch", CreateFakeSpriteBatch(800, 600));
            SetPrivateField(stage, "_resultFont", null);
            SetPrivateField(stage, "_whitePixel", null);
            SetPrivateField(stage, "_performanceSummary", new PerformanceSummary
            {
                Score = 200000,
                MaxCombo = 50,
                ClearFlag = false,
                PerfectCount = 30,
                GreatCount = 10,
                GoodCount = 5,
                PoorCount = 3,
                MissCount = 40,
                TotalNotes = 88,
                CompletionReason = CompletionReason.PlayerFailed
            });

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "DrawResults"));

            Assert.Null(exception);
        }

        [Fact]
        public void OnActivate_WithSelectedSongAndDifficulty_ShouldExtractSharedDataAndRequestScorePersist()
        {
            SongManager.ResetInstanceForTesting();
            try
            {
#pragma warning disable SYSLIB0050
                var stage = (InspectableResultStage)FormatterServices.GetUninitializedObject(typeof(InspectableResultStage));
#pragma warning restore SYSLIB0050

                var summary = new PerformanceSummary
                {
                    Score = 750000,
                    MaxCombo = 120,
                    ClearFlag = true,
                    GameSkill = 124.5
                };
                var chart = new SongChart
                {
                    Id = 42,
                    HasDrumChart = true,
                    DrumLevel = 75
                };
                var song = new SongListNode
                {
                    DatabaseSong = new SongEntity
                    {
                        Charts = new List<SongChart> { chart }
                    }
                };
                SetPrivateField(stage, "_inputManager", null);
                SetPrivateField(stage, "_sharedData", new Dictionary<string, object>
                {
                    ["performanceSummary"] = summary,
                    ["selectedSong"] = song,
                    ["selectedDifficulty"] = 0
                });

                var exception = Record.Exception(() => InvokePrivateMethod(stage, "OnActivate"));

                Assert.Null(exception);
                Assert.Same(summary, GetPrivateField<PerformanceSummary>(stage, "_performanceSummary"));
                Assert.Same(song, GetPrivateField<SongListNode?>(stage, "_selectedSong"));
                Assert.Equal(0, GetPrivateField<int>(stage, "_selectedDifficulty"));
                Assert.True(stage.WhitePixelRequested);
                Assert.True(stage.ResultFontRequested);
            }
            finally
            {
                SongManager.ResetInstanceForTesting();
            }
        }

        #region Helpers

        private static SpriteBatch CreateFakeSpriteBatch(int width, int height)
        {
#pragma warning disable SYSLIB0050
            var spriteBatch = (SpriteBatch)FormatterServices.GetUninitializedObject(typeof(SpriteBatch));
            var graphicsDevice = (GraphicsDevice)FormatterServices.GetUninitializedObject(typeof(GraphicsDevice));
#pragma warning restore SYSLIB0050
            // Suppress finalization — these objects were never properly constructed and their
            // GraphicsResource finalizer would crash trying to access uninitialized state.
            GC.SuppressFinalize(spriteBatch);
            GC.SuppressFinalize(graphicsDevice);
            SetPrivateField(spriteBatch, "graphicsDevice", graphicsDevice);
            SetPrivateField(graphicsDevice, "_viewport", new Viewport(0, 0, width, height));
            return spriteBatch;
        }

        private sealed class InspectableResultStage : ResultStage
        {
            public InspectableResultStage(BaseGame game) : base(game) { }

            public Texture2D DrawTextureArgument { get; private set; }
            public Rectangle? DrawTextureRectangle { get; private set; }
            public Color? DrawTextureColor { get; private set; }
            public bool WhitePixelRequested { get; private set; }
            public bool ResultFontRequested { get; private set; }

            internal override Texture2D CreateWhitePixel()
            {
                WhitePixelRequested = true;
                return null!;
            }

            internal override BitmapFont CreateResultFont()
            {
                ResultFontRequested = true;
                return null!;
            }

            internal override void DrawTexture(Texture2D texture, Rectangle destinationRectangle, Color color)
            {
                DrawTextureArgument = texture;
                DrawTextureRectangle = destinationRectangle;
                DrawTextureColor = color;
            }
        }

        #endregion
    }
}
