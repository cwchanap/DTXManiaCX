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
    /// <summary>
    /// Theme-driven layout/typography for the startup (loading) screen. NX pins
    /// every value to the original corner-anchored console layout: white 14px
    /// serif log at (10,10), version line top-right, and a 400x20 gray/green bar
    /// 120px above the bottom edge with the phase name spelled out beside it.
    /// Skins move the readout to a centered composition by setting the "0 means
    /// NX" anchors (StatusCenterX / ProgressReadoutY).
    /// </summary>
    [Trait("Category", "Unit")]
    public class StartupStageThemeLayoutTests
    {
        [Fact]
        public void ResolveTextFont_WithEmptyTheme_ShouldKeepNxSerifDefaults()
        {
            Assert.Equal(string.Empty, StartupStage.ResolveTextFontFamily(SkinTheme.Empty));
            Assert.Equal(string.Empty, StartupStage.ResolveStatusFontFamily(SkinTheme.Empty));
            Assert.Equal(14, StartupStage.ResolveTextFontSize(SkinTheme.Empty));
            Assert.Equal(14, StartupStage.ResolveStatusFontSize(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveStatusFontFamily_WithOnlyTextFamily_ShouldFollowTextFamily()
        {
            // A skin that names one face uses it throughout.
            var theme = SkinTheme.Parse(new[] { "Startup.TextFontFamily=ShareTechMono" });

            Assert.Equal("ShareTechMono", StartupStage.ResolveStatusFontFamily(theme));
        }

        [Fact]
        public void ResolveStatusFontFamily_WithOwnFamily_ShouldPairTwoFaces()
        {
            // Mono telemetry for the log/ledger, display face for the status line.
            var theme = SkinTheme.Parse(new[]
            {
                "Startup.TextFontFamily=ShareTechMono",
                "Startup.StatusFontFamily=Orbitron"
            });

            Assert.Equal("ShareTechMono", StartupStage.ResolveTextFontFamily(theme));
            Assert.Equal("Orbitron", StartupStage.ResolveStatusFontFamily(theme));
        }

        [Fact]
        public void ResolveTextFont_WithThemedValues_ShouldUseThemedValues()
        {
            var theme = SkinTheme.Parse(new[]
            {
                "Startup.TextFontFamily=Orbitron",
                "Startup.TextFontSize=14",
                "Startup.StatusFontSize=18"
            });

            Assert.Equal("Orbitron", StartupStage.ResolveTextFontFamily(theme));
            Assert.Equal(14, StartupStage.ResolveTextFontSize(theme));
            Assert.Equal(18, StartupStage.ResolveStatusFontSize(theme));
        }

        [Fact]
        public void ResolveStatusFontSize_WithOnlyTextFontSize_ShouldFollowTextFontSize()
        {
            var theme = SkinTheme.Parse(new[] { "Startup.TextFontSize=20" });

            Assert.Equal(20, StartupStage.ResolveStatusFontSize(theme));
        }

        [Fact]
        public void ResolveLogLayout_WithEmptyTheme_ShouldKeepNxCorner()
        {
            Assert.Equal(10, StartupStage.ResolveLogX(SkinTheme.Empty));
            Assert.Equal(10, StartupStage.ResolveLogY(SkinTheme.Empty));
            Assert.Equal(18, StartupStage.ResolveLogLineHeight(SkinTheme.Empty));
            Assert.Equal(Color.White, StartupStage.ResolveLogColor(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveLogLayout_WithThemedValues_ShouldUseThemedValues()
        {
            var theme = SkinTheme.Parse(new[]
            {
                "Startup.LogX=40",
                "Startup.LogY=40",
                "Startup.LogLineHeight=20",
                "Startup.LogText=#64748B"
            });

            Assert.Equal(40, StartupStage.ResolveLogX(theme));
            Assert.Equal(40, StartupStage.ResolveLogY(theme));
            Assert.Equal(20, StartupStage.ResolveLogLineHeight(theme));
            Assert.Equal(new Color(0x64, 0x74, 0x8B), StartupStage.ResolveLogColor(theme));
        }

        [Fact]
        public void ResolveStatusAnchor_WithEmptyTheme_ShouldStayInlineWithLog()
        {
            // 0 = no centered status line; the current message trails the log.
            Assert.Equal(0, StartupStage.ResolveStatusCenterX(SkinTheme.Empty));
            Assert.Equal(0, StartupStage.ResolveStatusY(SkinTheme.Empty));
            Assert.Equal(Color.Yellow, StartupStage.ResolveStatusColor(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveStatusAnchor_WithThemedValues_ShouldUseThemedValues()
        {
            var theme = SkinTheme.Parse(new[]
            {
                "Startup.StatusCenterX=640",
                "Startup.StatusY=452",
                "Startup.StatusText=#F1F5F9"
            });

            Assert.Equal(640, StartupStage.ResolveStatusCenterX(theme));
            Assert.Equal(452, StartupStage.ResolveStatusY(theme));
            Assert.Equal(new Color(0xF1, 0xF5, 0xF9), StartupStage.ResolveStatusColor(theme));
        }

        [Fact]
        public void ResolveProgressBar_WithEmptyTheme_ShouldKeepNxBar()
        {
            Assert.Equal(400, StartupStage.ResolveProgressBarWidth(SkinTheme.Empty));
            Assert.Equal(20, StartupStage.ResolveProgressBarHeight(SkinTheme.Empty));
            Assert.Equal(0, StartupStage.ResolveProgressBarY(SkinTheme.Empty));
            Assert.Equal(Color.DarkGray, StartupStage.ResolveProgressBarBackColor(SkinTheme.Empty));
            Assert.Equal(Color.LightGreen, StartupStage.ResolveProgressBarFillColor(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveProgressBar_WithThemedValues_ShouldUseThemedValues()
        {
            var theme = SkinTheme.Parse(new[]
            {
                "Startup.ProgressBarWidth=560",
                "Startup.ProgressBarHeight=6",
                "Startup.ProgressBarY=490",
                "Startup.ProgressBarBack=#1E293B",
                "Startup.ProgressBarFill=#22D3EE"
            });

            Assert.Equal(560, StartupStage.ResolveProgressBarWidth(theme));
            Assert.Equal(6, StartupStage.ResolveProgressBarHeight(theme));
            Assert.Equal(490, StartupStage.ResolveProgressBarY(theme));
            Assert.Equal(new Color(0x1E, 0x29, 0x3B), StartupStage.ResolveProgressBarBackColor(theme));
            Assert.Equal(new Color(0x22, 0xD3, 0xEE), StartupStage.ResolveProgressBarFillColor(theme));
        }

        [Fact]
        public void ResolveProgressBarTop_WithEmptyTheme_ShouldSitAboveBottomEdge()
        {
            // NX: 120px above the bottom of the viewport.
            Assert.Equal(600, StartupStage.ResolveProgressBarTop(SkinTheme.Empty, 720));
        }

        [Fact]
        public void ResolveProgressBarTop_WithThemedY_ShouldUseAbsoluteY()
        {
            var theme = SkinTheme.Parse(new[] { "Startup.ProgressBarY=490" });

            Assert.Equal(490, StartupStage.ResolveProgressBarTop(theme, 720));
        }

        [Fact]
        public void ResolveProgressReadout_WithEmptyTheme_ShouldStayBesideTheBar()
        {
            Assert.Equal(0, StartupStage.ResolveProgressReadoutY(SkinTheme.Empty));
            Assert.Equal(Color.White, StartupStage.ResolveProgressReadoutColor(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveProgressReadout_WithThemedValues_ShouldUseThemedValues()
        {
            var theme = SkinTheme.Parse(new[]
            {
                "Startup.ProgressReadoutY=506",
                "Startup.ProgressReadoutText=#94A3B8"
            });

            Assert.Equal(506, StartupStage.ResolveProgressReadoutY(theme));
            Assert.Equal(new Color(0x94, 0xA3, 0xB8), StartupStage.ResolveProgressReadoutColor(theme));
        }

        [Fact]
        public void ResolveVersion_WithEmptyTheme_ShouldKeepNxTopRight()
        {
            Assert.Equal(2, StartupStage.ResolveVersionY(SkinTheme.Empty));
            Assert.Equal(Color.White, StartupStage.ResolveVersionColor(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveVersionRightEdge_WithEmptyTheme_ShouldUseEdgeMargin()
        {
            Assert.Equal(1270, StartupStage.ResolveVersionRightEdge(SkinTheme.Empty, 1280));
        }

        [Fact]
        public void ResolveVersionRightEdge_WithThemedValue_ShouldUseAbsoluteEdge()
        {
            var theme = SkinTheme.Parse(new[] { "Startup.VersionRightX=1240" });

            Assert.Equal(1240, StartupStage.ResolveVersionRightEdge(theme, 1280));
        }

        [Fact]
        public void ResolveVersion_WithThemedValues_ShouldUseThemedValues()
        {
            var theme = SkinTheme.Parse(new[]
            {
                "Startup.VersionY=690",
                "Startup.VersionText=#475569"
            });

            Assert.Equal(690, StartupStage.ResolveVersionY(theme));
            Assert.Equal(new Color(0x47, 0x55, 0x69), StartupStage.ResolveVersionColor(theme));
        }

        [Fact]
        public void ResolveStatusMaxWidth_WithEmptyTheme_ShouldBeUncapped()
        {
            Assert.Equal(0, StartupStage.ResolveStatusMaxWidth(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveStatusMaxWidth_WithThemedValue_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "Startup.StatusMaxWidth=560" });

            Assert.Equal(560, StartupStage.ResolveStatusMaxWidth(theme));
        }

        // 10px per character keeps the arithmetic obvious.
        private static float MeasureTenPerChar(string text) => text.Length * 10f;

        [Fact]
        public void ComputeStatusScale_WhenUncapped_ShouldStayFullSize()
        {
            Assert.Equal(1f, StartupStage.ComputeStatusScale(MeasureTenPerChar, new string('x', 200), 0));
        }

        [Fact]
        public void ComputeStatusScale_WhenTextFits_ShouldStayFullSize()
        {
            Assert.Equal(1f, StartupStage.ComputeStatusScale(MeasureTenPerChar, new string('x', 40), 560));
        }

        [Fact]
        public void ComputeStatusScale_WhenTextOverruns_ShouldShrinkToFit()
        {
            // 70 chars = 700px into a 560px cap.
            Assert.Equal(0.8f, StartupStage.ComputeStatusScale(MeasureTenPerChar, new string('x', 70), 560), 3);
        }

        [Fact]
        public void ComputeStatusScale_WhenTextIsFarTooLong_ShouldStopAtFloor()
        {
            // Enumeration streams long "[n processed] <filename>" lines; shrinking
            // without a floor would make them unreadable.
            var scale = StartupStage.ComputeStatusScale(MeasureTenPerChar, new string('x', 400), 560);

            Assert.Equal(StartupStage.StatusMinScale, scale);
        }

        [Fact]
        public void SelectStatusFont_WithAsciiText_ShouldUseDisplayFont()
        {
            var display = new Mock<IFont>().Object;
            var fallback = new Mock<IFont>().Object;

            Assert.Same(display,
                StartupStage.SelectStatusFont("Scanning for new/modified songs...", display, fallback));
        }

        [Fact]
        public void SelectStatusFont_WithNonAsciiText_ShouldUseFallbackFont()
        {
            // Song enumeration streams real filenames through the status line, and
            // the display faces are Latin-only.
            var display = new Mock<IFont>().Object;
            var fallback = new Mock<IFont>().Object;

            Assert.Same(fallback,
                StartupStage.SelectStatusFont("Scanning: 星空のオルゴール.dtx", display, fallback));
        }

        [Fact]
        public void SelectStatusFont_WithoutDisplayFont_ShouldUseFallbackFont()
        {
            var fallback = new Mock<IFont>().Object;

            Assert.Same(fallback, StartupStage.SelectStatusFont("anything", null, fallback));
        }

        [Theory]
        [InlineData(StartupPhase.SystemSounds, 0.0, "STEP 01 / 10")]
        [InlineData(StartupPhase.LoadScoreFiles, 0.571, "STEP 06 / 10")]
        [InlineData(StartupPhase.Complete, 1.0, "STEP 10 / 10")]
        public void FormatStepReadout_ShouldCountPhasesFromOne(StartupPhase phase, double progress, string expected)
        {
            Assert.Equal(expected, StartupStage.FormatStepReadout(phase, 10));
            Assert.InRange(progress, 0.0, 1.0);
        }

        [Theory]
        [InlineData(0.0, "0%")]
        [InlineData(0.571, "57%")]
        [InlineData(0.9999, "100%")]
        public void FormatPercentReadout_ShouldRenderWholePercent(double progress, string expected)
        {
            Assert.Equal(expected, StartupStage.FormatPercentReadout(progress));
        }

        [Fact]
        public void FormatPhaseReadout_ShouldKeepNxPhaseNameAndDecimal()
        {
            Assert.Equal("LoadScoreFiles (57.1%)",
                StartupStage.FormatPhaseReadout(StartupPhase.LoadScoreFiles, 0.5714));
        }

        [Fact]
        public void StartupActivation_WhenThemedBoldFaceFails_ShouldUseSerifFallbackAndIgnoreStatusFallbackFailure()
        {
            var game = ReflectionHelpers.CreateGame();
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.SetupGet(value => value.CurrentTheme).Returns(CreateStartupDisplayTheme());
            ReflectionHelpers.SetPrivateField(game, "<ResourceManager>k__BackingField", resourceManager.Object);

            var themedRegular = new Mock<IFont>();
            var fallbackRegular = new Mock<IFont>();
            var fallbackBold = new Mock<IFont>();
            var stage = new CoverageStartupStage(game)
            {
                ThemedRegularFont = themedRegular.Object,
                FallbackRegularFont = fallbackRegular.Object,
                FallbackBoldFont = fallbackBold.Object,
                ThrowOnThemedBold = true,
                ThrowOnStatusFallback = true
            };

            ReflectionHelpers.InvokePrivateMethod(stage, "OnActivate");

            themedRegular.Verify(value => value.RemoveReference(), Times.Once);
            Assert.Same(fallbackRegular.Object, ReflectionHelpers.GetPrivateField<IFont>(stage, "_font"));
            Assert.Same(fallbackBold.Object, ReflectionHelpers.GetPrivateField<IFont>(stage, "_boldFont"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_statusFallbackFont"));
            Assert.Contains(("Orbitron", 15, FontStyle.Regular), stage.FontRequests);
            Assert.Contains(("ShareTechMono", 17, FontStyle.Bold), stage.FontRequests);
            Assert.Contains(("NotoSerifJP", 15, FontStyle.Regular), stage.FontRequests);
            Assert.Contains(("NotoSerifJP", 17, FontStyle.Bold), stage.FontRequests);
        }

        [Fact]
        public void StartupActivation_WithThemedFaces_ShouldKeepDisplayFontsAndLoadCjkStatusFallback()
        {
            var game = ReflectionHelpers.CreateGame();
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.SetupGet(value => value.CurrentTheme).Returns(CreateStartupDisplayTheme());
            ReflectionHelpers.SetPrivateField(game, "<ResourceManager>k__BackingField", resourceManager.Object);

            var themedRegular = new Mock<IFont>();
            var themedBold = new Mock<IFont>();
            var statusFallback = new Mock<IFont>();
            var stage = new CoverageStartupStage(game)
            {
                ThemedRegularFont = themedRegular.Object,
                ThemedBoldFont = themedBold.Object,
                StatusFallbackFont = statusFallback.Object
            };

            ReflectionHelpers.InvokePrivateMethod(stage, "OnActivate");

            Assert.Same(themedRegular.Object, ReflectionHelpers.GetPrivateField<IFont>(stage, "_font"));
            Assert.Same(themedBold.Object, ReflectionHelpers.GetPrivateField<IFont>(stage, "_boldFont"));
            Assert.Same(statusFallback.Object,
                ReflectionHelpers.GetPrivateField<IFont>(stage, "_statusFallbackFont"));
        }

        [Fact]
        public void CreateStatusFallbackFontCore_WithNoDisplayFamily_ShouldReturnNullWithoutLoading()
        {
            var stage = new StartupStage(ReflectionHelpers.CreateGame());
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.SetupGet(value => value.CurrentTheme).Returns(SkinTheme.Empty);

            var result = ReflectionHelpers.InvokePrivateMethod<IFont>(
                stage, "CreateStatusFallbackFontCore", resourceManager.Object, 16);

            Assert.Null(result);
            resourceManager.Verify(value => value.LoadFont(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<FontStyle>()), Times.Never);
        }

        [Fact]
        public void CreateStatusFallbackFontCore_WithDisplayFamily_ShouldLoadBoldSerifFace()
        {
            var stage = new StartupStage(ReflectionHelpers.CreateGame());
            var expected = new Mock<IFont>();
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.SetupGet(value => value.CurrentTheme).Returns(CreateStartupDisplayTheme());
            resourceManager.Setup(value => value.LoadFont("NotoSerifJP", 17, FontStyle.Bold))
                .Returns(expected.Object);

            var result = ReflectionHelpers.InvokePrivateMethod<IFont>(
                stage, "CreateStatusFallbackFontCore", resourceManager.Object, 17);

            Assert.Same(expected.Object, result);
            resourceManager.Verify(value => value.LoadFont("NotoSerifJP", 17, FontStyle.Bold), Times.Once);
        }

        [Fact]
        public void ReleaseStartupFonts_WithAllFacesLoaded_ShouldReleaseAndClearEveryFace()
        {
            var stage = new StartupStage(ReflectionHelpers.CreateGame());
            var regular = new Mock<IFont>();
            var bold = new Mock<IFont>();
            var statusFallback = new Mock<IFont>();
            ReflectionHelpers.SetPrivateField(stage, "_font", regular.Object);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", bold.Object);
            ReflectionHelpers.SetPrivateField(stage, "_statusFallbackFont", statusFallback.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "ReleaseStartupFonts");

            regular.Verify(value => value.RemoveReference(), Times.Once);
            bold.Verify(value => value.RemoveReference(), Times.Once);
            statusFallback.Verify(value => value.RemoveReference(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_font"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_boldFont"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_statusFallbackFont"));
        }

        private static ISkinTheme CreateStartupDisplayTheme() => SkinTheme.Parse(new[]
        {
            "Startup.TextFontFamily=Orbitron",
            "Startup.StatusFontFamily=ShareTechMono",
            "Startup.TextFontSize=15",
            "Startup.StatusFontSize=17"
        });

        private sealed class CoverageStartupStage : StartupStage
        {
            public CoverageStartupStage(BaseGame game) : base(game)
            {
            }

            public GraphicsDevice GraphicsDeviceStub { get; } =
                (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice));

            public SpriteBatch SpriteBatchStub { get; } =
                (SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch));

            public Texture2D WhitePixelStub { get; } =
                (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));

            public IFont ThemedRegularFont { get; set; } = null!;

            public IFont ThemedBoldFont { get; set; } = null!;

            public IFont FallbackRegularFont { get; set; } = null!;

            public IFont FallbackBoldFont { get; set; } = null!;

            public IFont StatusFallbackFont { get; set; } = null!;

            public bool ThrowOnThemedBold { get; set; }

            public bool ThrowOnStatusFallback { get; set; }

            public List<(string Family, int Size, FontStyle Style)> FontRequests { get; } = new();

            protected override GraphicsDevice GetGraphicsDeviceCore() => GraphicsDeviceStub;

            protected override SpriteBatch CreateSpriteBatchCore(GraphicsDevice graphicsDevice) => SpriteBatchStub;

            protected override Texture2D CreateWhitePixelCore(GraphicsDevice graphicsDevice) => WhitePixelStub;

            protected override IFont CreateFontCore(
                IResourceManager resourceManager,
                string fontFamily,
                int size,
                FontStyle style)
            {
                FontRequests.Add((fontFamily, size, style));

                if (fontFamily == "NotoSerifJP")
                    return style == FontStyle.Bold ? FallbackBoldFont : FallbackRegularFont;

                if (style == FontStyle.Bold && ThrowOnThemedBold)
                    throw new InvalidOperationException("themed bold face unavailable");

                return style == FontStyle.Bold ? ThemedBoldFont : ThemedRegularFont;
            }

            protected override IFont CreateStatusFallbackFontCore(
                IResourceManager resourceManager,
                int size)
            {
                if (ThrowOnStatusFallback)
                    throw new InvalidOperationException("status fallback unavailable");

                return StatusFallbackFont;
            }
        }
}
}
