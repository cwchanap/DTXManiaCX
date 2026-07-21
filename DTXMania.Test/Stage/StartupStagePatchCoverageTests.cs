using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DTXMania.Game;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class StartupStagePatchCoverageTests
    {
        [Fact]
        public void DrawStatusLine_WithFullSizeDisplayFont_ShouldCenterAndDrawNormally()
        {
            var stage = CreateStageWithTheme(
                "Startup.StatusCenterX=640",
                "Startup.StatusY=452",
                "Startup.StatusMaxWidth=560",
                "Startup.StatusText=#AABBCC");
            var font = new Mock<IFont>();
            font.Setup(value => value.MeasureString("Ready")).Returns(new Vector2(200, 17));

            ReflectionHelpers.SetPrivateField(stage, "_boldFont", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_statusFallbackFont", null);
            ReflectionHelpers.SetPrivateField(stage, "_currentProgressMessage", "Ready");

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawStatusLine");

            font.Verify(value => value.DrawString(
                stage.SpriteBatchStub,
                "Ready",
                new Vector2(540, 452),
                new Color(0xAA, 0xBB, 0xCC)), Times.Once);
        }

        [Fact]
        public void DrawStatusLine_WithWideNonAsciiText_ShouldUseFallbackFontAndScaledDraw()
        {
            var stage = CreateStageWithTheme(
                "Startup.StatusCenterX=640",
                "Startup.StatusY=452",
                "Startup.StatusMaxWidth=560",
                "Startup.StatusText=#F1F5F9");
            var displayFont = new Mock<IFont>();
            var fallbackFont = new Mock<IFont>();
            const string message = "Scanning: 星空のオルゴール.dtx";
            fallbackFont.Setup(value => value.MeasureString(message)).Returns(new Vector2(800, 17));

            ReflectionHelpers.SetPrivateField(stage, "_boldFont", displayFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_statusFallbackFont", fallbackFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_currentProgressMessage", message);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawStatusLine");

            displayFont.Verify(value => value.DrawString(
                It.IsAny<SpriteBatch>(), It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>()), Times.Never);
            fallbackFont.Verify(value => value.DrawString(
                stage.SpriteBatchStub,
                message,
                new Vector2(360, 452),
                new Color(0xF1, 0xF5, 0xF9),
                0f,
                Vector2.Zero,
                It.Is<Vector2>(scale => Math.Abs(scale.X - 0.7f) < 0.001f && Math.Abs(scale.Y - 0.7f) < 0.001f),
                SpriteEffects.None,
                0f), Times.Once);
        }

        [Fact]
        public void DrawStatusLine_WithoutFonts_ShouldDrawCenteredFallbackRectangle()
        {
            var stage = CreateStageWithTheme(
                "Startup.StatusCenterX=640",
                "Startup.StatusY=452",
                "Startup.StatusText=#22D3EE");
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", null);
            ReflectionHelpers.SetPrivateField(stage, "_statusFallbackFont", null);
            ReflectionHelpers.SetPrivateField(stage, "_currentProgressMessage", "TEST");

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawStatusLine");

            Assert.Contains(stage.DrawCalls, call =>
                call.Destination == new Rectangle(624, 452, 32, 12) &&
                call.Color == new Color(0x22, 0xD3, 0xEE));
        }

        [Fact]
        public void DrawStatusLine_WithEmptyMessage_ShouldNotDraw()
        {
            var stage = CreateStageWithTheme("Startup.StatusCenterX=640");
            var font = new Mock<IFont>();
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_currentProgressMessage", string.Empty);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawStatusLine");

            font.VerifyNoOtherCalls();
            Assert.Empty(stage.DrawCalls);
        }

        [Fact]
        public void DrawCurrentProgress_WithoutWhitePixel_ShouldReturnWithoutDrawing()
        {
            var stage = CreateStageWithTheme("Startup.ProgressReadoutY=506");
            ReflectionHelpers.SetPrivateField(stage, "_whitePixel", null);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawCurrentProgress");

            Assert.Empty(stage.DrawCalls);
        }

        [Fact]
        public void DrawCurrentProgress_WithThemedLedger_ShouldDrawRailStepAndPercent()
        {
            var stage = CreateStageWithTheme(
                "Startup.ProgressBarWidth=560",
                "Startup.ProgressBarHeight=6",
                "Startup.ProgressBarY=490",
                "Startup.ProgressBarBack=#1E293B",
                "Startup.ProgressBarFill=#22D3EE",
                "Startup.ProgressReadoutY=506",
                "Startup.ProgressReadoutText=#94A3B8");
            var font = new Mock<IFont>();
            font.Setup(value => value.MeasureString("55%")).Returns(new Vector2(30, 14));

            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.LoadScoreFiles);
            ReflectionHelpers.SetPrivateField(stage, "_elapsedTime", 0.35d);
            ReflectionHelpers.SetPrivateField(stage, "_phaseStartTime", 0d);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawCurrentProgress");

            Assert.Contains(stage.DrawCalls, call =>
                call.Destination == new Rectangle(360, 490, 560, 6) &&
                call.Color == new Color(0x1E, 0x29, 0x3B));
            Assert.Contains(stage.DrawCalls, call =>
                call.Destination == new Rectangle(360, 490, 308, 6) &&
                call.Color == new Color(0x22, 0xD3, 0xEE));
            font.Verify(value => value.DrawString(
                stage.SpriteBatchStub,
                "STEP 06 / 10",
                new Vector2(360, 506),
                new Color(0x94, 0xA3, 0xB8)), Times.Once);
            font.Verify(value => value.DrawString(
                stage.SpriteBatchStub,
                "55%",
                new Vector2(890, 506),
                new Color(0x94, 0xA3, 0xB8)), Times.Once);
        }

        private static DrawingStartupStage CreateStageWithTheme(params string[] themeLines)
        {
            var stage = new DrawingStartupStage(ReflectionHelpers.CreateGame());
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.SetupGet(value => value.CurrentTheme).Returns(SkinTheme.Parse(themeLines));

            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", stage.SpriteBatchStub);
            ReflectionHelpers.SetPrivateField(stage, "_whitePixel", stage.WhitePixelStub);
            ReflectionHelpers.SetPrivateField(stage, "_progressMessages", new List<string>());

            return stage;
        }

        private sealed record DrawCall(Rectangle Destination, Color Color);

        private sealed class DrawingStartupStage : StartupStage
        {
            public DrawingStartupStage(BaseGame game) : base(game)
            {
            }

            public SpriteBatch SpriteBatchStub { get; } =
                (SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch));

            public Texture2D WhitePixelStub { get; } =
                (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));

            public List<DrawCall> DrawCalls { get; } = new();

            protected override Viewport GetViewportCore() => new(0, 0, 1280, 720);

            protected override void DrawSolidRectCore(
                SpriteBatch spriteBatch,
                Texture2D texture,
                Rectangle destination,
                Color color)
            {
                DrawCalls.Add(new DrawCall(destination, color));
            }
        }
    }
}
