using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DTXMania.Game;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Exercises the runtime branches introduced by the CX Neon theme work that
    /// are not reached by the pure resolver tests.
    /// </summary>
    [Trait("Category", "Unit")]
    public class CxNeonCoverageGapTests
    {
        [Fact]
        public void DrawBpmNumberBitmap_WithScaledThemeAndDoubleResolutionSheet_ShouldScaleSourceAndDestination()
        {
            var panel = new SongStatusPanel();
            var texture = new Mock<ITexture>();
            texture.SetupGet(value => value.Height).Returns(40);

            var resourceManager = new Mock<IResourceManager>();
            resourceManager.SetupGet(value => value.CurrentTheme).Returns(
                SkinTheme.Parse(new[] { "SongSelect.StatusNumberScale=1.5" }));

            ReflectionHelpers.SetPrivateField(panel, "_bpmNumberTexture", texture.Object);
            ReflectionHelpers.SetPrivateField(panel, "_resourceManager", resourceManager.Object);

            ReflectionHelpers.InvokePrivateMethod(
                panel,
                "DrawBpmNumberBitmap",
                (SpriteBatch)null!,
                10f,
                20f,
                "1:A2");

            VerifyBitmapDraw(texture, new Rectangle(10, 20, 18, 30), new Rectangle(24, 0, 24, 40));
            VerifyBitmapDraw(texture, new Rectangle(28, 20, 9, 30), new Rectangle(246, 0, 12, 40));
            // The unsupported 'A' advances by one digit cell but does not draw.
            VerifyBitmapDraw(texture, new Rectangle(55, 20, 18, 30), new Rectangle(48, 0, 24, 40));

            texture.Verify(value => value.Draw(
                    It.IsAny<SpriteBatch>(),
                    It.IsAny<Rectangle>(),
                    It.IsAny<Rectangle?>(),
                    It.IsAny<Color>(),
                    It.IsAny<float>(),
                    It.IsAny<Vector2>(),
                    It.IsAny<SpriteEffects>(),
                    It.IsAny<float>()),
                Times.Exactly(3));
        }

        [Fact]
        public void LoadDisplayFonts_WhenTitleFaceFails_ShouldStillLoadArtistFaceAndReleasePreviousFonts()
        {
            var stage = new SongTransitionStage(ReflectionHelpers.CreateGame());
            var oldTitle = new Mock<IFont>();
            var oldArtist = new Mock<IFont>();
            var loadedArtist = new Mock<IFont>();
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.SetupGet(value => value.CurrentTheme).Returns(SkinTheme.Parse(new[]
            {
                "Transition.TitleFontFamily=BrokenTitle",
                "Transition.TitleFontSize=31",
                "Transition.ArtistFontFamily=ArtistFace",
                "Transition.ArtistFontSize=19"
            }));
            resourceManager.Setup(value => value.LoadFont("BrokenTitle", 31))
                .Throws(new InvalidOperationException("title unavailable"));
            resourceManager.Setup(value => value.LoadFont("ArtistFace", 19))
                .Returns(loadedArtist.Object);

            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            ReflectionHelpers.SetPrivateField(stage, "_titleDisplayFont", oldTitle.Object);
            ReflectionHelpers.SetPrivateField(stage, "_artistDisplayFont", oldArtist.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "LoadDisplayFonts");

            oldTitle.Verify(value => value.RemoveReference(), Times.Once);
            oldArtist.Verify(value => value.RemoveReference(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_titleDisplayFont"));
            Assert.Same(loadedArtist.Object,
                ReflectionHelpers.GetPrivateField<IFont>(stage, "_artistDisplayFont"));
        }

        [Fact]
        public void LoadDisplayFonts_WhenArtistFaceFails_ShouldKeepLoadedTitleFace()
        {
            var stage = new SongTransitionStage(ReflectionHelpers.CreateGame());
            var loadedTitle = new Mock<IFont>();
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.SetupGet(value => value.CurrentTheme).Returns(SkinTheme.Parse(new[]
            {
                "Transition.TitleFontFamily=TitleFace",
                "Transition.TitleFontSize=29",
                "Transition.ArtistFontFamily=BrokenArtist",
                "Transition.ArtistFontSize=18"
            }));
            resourceManager.Setup(value => value.LoadFont("TitleFace", 29))
                .Returns(loadedTitle.Object);
            resourceManager.Setup(value => value.LoadFont("BrokenArtist", 18))
                .Throws(new InvalidOperationException("artist unavailable"));

            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "LoadDisplayFonts");

            Assert.Same(loadedTitle.Object,
                ReflectionHelpers.GetPrivateField<IFont>(stage, "_titleDisplayFont"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_artistDisplayFont"));
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

        private static void VerifyBitmapDraw(
            Mock<ITexture> texture,
            Rectangle destination,
            Rectangle source)
        {
            texture.Verify(value => value.Draw(
                    It.IsAny<SpriteBatch>(),
                    destination,
                    It.Is<Rectangle?>(candidate => candidate.HasValue && candidate.Value == source),
                    Color.White,
                    0f,
                    Vector2.Zero,
                    SpriteEffects.None,
                    0f),
                Times.Once);
        }

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
