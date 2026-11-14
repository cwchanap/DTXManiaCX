using System;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;
using static DTXMania.Test.Helpers.TestFontFactory;
using static DTXMania.Test.TestData.ReflectionHelpers;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Pure logic tests for SkillPanelDisplay formatting helpers — runs on all platforms.
    /// </summary>
    [Collection("ManagedFont")]
    [Trait("Category", "Unit")]
    public class SkillPanelDisplayLogicTests
    {
        // Level encoding (DTXManiaNX-faithful):
        //   level <  100 → displayed = level/10 + levelDec/100  (e.g. 80+50 → 8.50)
        //   level >= 100 → displayed = level/100                (e.g. 850 → 8.50)
        [Theory]
        [InlineData(78,  0, "7.80")]
        [InlineData(80, 50, "8.50")]
        [InlineData(78, 33, "8.13")]
        [InlineData(50,  0, "5.00")]
        [InlineData(850, 0, "8.50")]
        [InlineData( 0,  0, "--")]
        [InlineData(-1,  0, "--")]
        public void FormatLevelText_WithVariousInputs_ShouldReturnExpected(int level, int levelDec, string expected)
        {
            Assert.Equal(expected, SkillPanelDisplay.FormatLevelText(level, levelDec));
        }

        [Theory]
        [InlineData(  0.0,  "  0.00")]
        [InlineData( 87.42, " 87.42")]
        [InlineData(100.0, "100.00")]
        [InlineData( 50.5,  " 50.50")]
        public void FormatSkillText_WithVariousInputs_ShouldReturnExpected(double skill, string expected)
        {
            Assert.Equal(expected, SkillPanelDisplay.FormatSkillText(skill));
        }

        [Theory]
        [InlineData(0, "   0")]
        [InlineData(7, "   7")]
        [InlineData(123, " 123")]
        [InlineData(1234, "1234")]
        [InlineData(12000, "9999")]
        public void FormatJudgementCount_WithVariousInputs_ShouldReturnFourColumnText(int count, string expected)
        {
            Assert.Equal(expected, SkillPanelDisplay.FormatJudgementCount(count));
        }

        [Theory]
        [InlineData(0, 0, "  0%")]
        [InlineData(1, 4, " 25%")]
        [InlineData(2, 3, " 67%")]
        [InlineData(10, 10, "100%")]
        public void FormatJudgementPercent_WithVariousInputs_ShouldReturnThreeColumnPercent(int count, int total, string expected)
        {
            Assert.Equal(expected, SkillPanelDisplay.FormatJudgementPercent(count, total));
        }

        // Difficulty badge cell selection mirrors Script/difficult.dtxs scene 7 (7_Difficulty.png is a
        // 60x720 vertical strip of twelve 60x60 cells; all cells share X=0, the label picks the Y row).
        [Theory]
        [InlineData("DTX", 0)]
        [InlineData("DEBUT", 60)]
        [InlineData("NOVICE", 120)]
        [InlineData("REGULAR", 180)]
        [InlineData("EXPERT", 240)]
        [InlineData("MASTER", 300)]
        [InlineData("BASIC", 360)]
        [InlineData("ADVANCED", 420)]
        [InlineData("EXTREME", 480)]
        [InlineData("RAW", 540)]
        [InlineData("RWS", 600)]
        [InlineData("REAL", 660)]
        [InlineData("extreme", 480)]   // case-insensitive
        [InlineData("  MASTER  ", 300)] // trimmed
        public void GetDifficultyPanelSourceRect_KnownLabels_SelectExpectedCell(string label, int expectedY)
        {
            var rect = SkillPanelDisplay.GetDifficultyPanelSourceRect(label);
            Assert.Equal(new Rectangle(0, expectedY, 60, 60), rect);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Level 3")]
        [InlineData("SPECIAL")]
        public void GetDifficultyPanelSourceRect_UnknownLabel_FallsBackToFirstCell(string? label)
        {
            var rect = SkillPanelDisplay.GetDifficultyPanelSourceRect(label);
            Assert.Equal(new Rectangle(0, 0, 60, 60), rect);
        }

        [Theory]
        [InlineData("DTX")]
        [InlineData("BASIC")]
        [InlineData("ADVANCED")]
        [InlineData("EXTREME")]
        [InlineData("MASTER")]
        [InlineData("REAL")]
        [InlineData("extreme")]      // case-insensitive
        [InlineData("  BASIC  ")]    // trimmed
        public void IsKnownDifficultyTier_AuthenticTierName_ShouldReturnTrue(string? label)
        {
            Assert.True(SkillPanelDisplay.IsKnownDifficultyTier(label));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("DRUMS Lv.36")]   // synthetic song-select display label, not a tier name
        [InlineData("GUITAR Lv.50")]
        [InlineData("Level 3")]
        [InlineData("SPECIAL")]
        public void IsKnownDifficultyTier_SyntheticOrUnknownLabel_ShouldReturnFalse(string? label)
        {
            Assert.False(SkillPanelDisplay.IsKnownDifficultyTier(label));
        }

        [Fact]
        public void GetProcessedJudgementCount_ShouldSumJudgementCounts()
        {
            Assert.Equal(15, SkillPanelDisplay.GetProcessedJudgementCount(
                perfectCount: 5,
                greatCount: 4,
                goodCount: 3,
                poorCount: 2,
                missCount: 1));
        }

        // CanRenderWithLevelTexture is internal — test via GetLevelNumberSourceRectangle
        // which is the gating check. Digits and '.' should be renderable; '-' should not.
        [Theory]
        [InlineData('0', true)]
        [InlineData('5', true)]
        [InlineData('9', true)]
        [InlineData('.', true)]
        [InlineData('-', false)]
        [InlineData('a', false)]
        [InlineData(' ', false)]
        public void GetLevelNumberSourceRectangle_ShouldOnlySupportDigitsAndDot(char ch, bool shouldHaveSource)
        {
            // Access the static method via reflection since it's private
            var method = typeof(SkillPanelDisplay).GetMethod(
                "GetLevelNumberSourceRectangle",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(method);
            var result = method.Invoke(null, new object[] { ch });
            if (shouldHaveSource)
                Assert.NotNull(result);
            else
                Assert.Null(result);
        }

        [Fact]
        public void CanRenderWithLevelTexture_DashText_ShouldReturnFalse()
        {
            var method = typeof(SkillPanelDisplay).GetMethod(
                "CanRenderWithLevelTexture",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(method);

            // "--" (unknown difficulty) should not be renderable via level texture
            Assert.False((bool)method.Invoke(null, new object[] { "--" })!);
            // "8.50" (normal level) should be renderable
            Assert.True((bool)method.Invoke(null, new object[] { "8.50" })!);
        }

        #region Constructor Guard Tests

        [Fact]
        public void Constructor_NullResourceManager_ShouldThrowArgumentNullException()
        {
            var graphicsDevice = CreateUninitialized<GraphicsDevice>();

            var ex = Assert.Throws<ArgumentNullException>(
                () => new SkillPanelDisplay(null!, graphicsDevice, null));

            Assert.Equal("resourceManager", ex.ParamName);
        }

        [Fact]
        public void Constructor_NullGraphicsDevice_ShouldThrowArgumentNullException()
        {
            var resourceManager = new Mock<IResourceManager>().Object;

            var ex = Assert.Throws<ArgumentNullException>(
                () => new SkillPanelDisplay(resourceManager, null!, null));

            Assert.Equal("graphicsDevice", ex.ParamName);
        }

        [Fact]
        public void Constructor_TextureLoadFails_ShouldSetMaxBadgeTextureNull()
        {
            var resourceManager = new Mock<IResourceManager>();
            resourceManager
                .Setup(rm => rm.LoadTexture(It.IsAny<string>()))
                .Throws(new Exception("texture load failure"));

            var display = WithManagedFontUnavailable(() =>
                new SkillPanelDisplay(resourceManager.Object, CreateUninitialized<GraphicsDevice>(), null));

            Assert.Null(GetPrivateField<ITexture?>(display, "_maxBadgeTexture"));
            display.Dispose();
        }

        [Fact]
        public void Constructor_TextureLoadSucceeds_ShouldStoreMaxBadgeTexture()
        {
            var resourceManager = new Mock<IResourceManager>();
            var maxBadgeTexture = new Mock<ITexture>().Object;
            resourceManager
                .Setup(rm => rm.LoadTexture(TexturePath.SkillMax))
                .Returns(maxBadgeTexture);

            var display = WithManagedFontUnavailable(() =>
                new SkillPanelDisplay(resourceManager.Object, CreateUninitialized<GraphicsDevice>(), null));

            Assert.Same(maxBadgeTexture, GetPrivateField<ITexture?>(display, "_maxBadgeTexture"));
            display.Dispose();
        }

        #endregion

        #region Difficulty Label Resolution

        [Fact]
        public void Constructor_ExplicitDifficultyLabel_ShouldBeUsedForBadge()
        {
            var resourceManager = new Mock<IResourceManager>();

            var display = WithManagedFontUnavailable(() =>
                new SkillPanelDisplay(resourceManager.Object, CreateUninitialized<GraphicsDevice>(), null, "MASTER"));

            Assert.Equal("MASTER", GetPrivateField<string?>(display, "_difficultyLabel"));
            display.Dispose();
        }

        [Fact]
        public void Constructor_NoExplicitLabel_ShouldFallBackToChartLabel()
        {
            var resourceManager = new Mock<IResourceManager>();
            var chart = new SongChart { DifficultyLabel = "EXTREME" };

            var display = WithManagedFontUnavailable(() =>
                new SkillPanelDisplay(resourceManager.Object, CreateUninitialized<GraphicsDevice>(), chart, null));

            Assert.Equal("EXTREME", GetPrivateField<string?>(display, "_difficultyLabel"));
            display.Dispose();
        }

        [Fact]
        public void Constructor_WhitespaceLabel_ShouldFallBackToChartLabel()
        {
            var resourceManager = new Mock<IResourceManager>();
            var chart = new SongChart { DifficultyLabel = "BASIC" };

            var display = WithManagedFontUnavailable(() =>
                new SkillPanelDisplay(resourceManager.Object, CreateUninitialized<GraphicsDevice>(), chart, "   "));

            Assert.Equal("BASIC", GetPrivateField<string?>(display, "_difficultyLabel"));
            display.Dispose();
        }

        [Fact]
        public void Constructor_SyntheticSlotLabel_ShouldPreferRealChartLabel()
        {
            // Regression: a single-chart SET.def song keeps the synthetic "DRUMS Lv.36" slot
            // label produced by SongListNode.CreateSongNode. That string never matches a badge
            // cell, so the badge must fall back to the chart's persisted DifficultyLabel
            // (the authentic SET.def label) instead of rendering the default DTX cell.
            var resourceManager = new Mock<IResourceManager>();
            var chart = new SongChart { DifficultyLabel = "BASIC" };

            var display = WithManagedFontUnavailable(() =>
                new SkillPanelDisplay(resourceManager.Object, CreateUninitialized<GraphicsDevice>(), chart, "DRUMS Lv.36"));

            Assert.Equal("BASIC", GetPrivateField<string?>(display, "_difficultyLabel"));
            display.Dispose();
        }

        [Fact]
        public void Constructor_SyntheticSlotLabelAndEmptyChartLabel_ShouldKeepSlotLabel()
        {
            // When neither the slot label nor the chart label names a real tier, keep the slot
            // label so a non-empty display string is still rendered as the default DTX cell
            // rather than collapsing to null.
            var resourceManager = new Mock<IResourceManager>();
            var chart = new SongChart { DifficultyLabel = "" };

            var display = WithManagedFontUnavailable(() =>
                new SkillPanelDisplay(resourceManager.Object, CreateUninitialized<GraphicsDevice>(), chart, "DRUMS Lv.36"));

            Assert.Equal("DRUMS Lv.36", GetPrivateField<string?>(display, "_difficultyLabel"));
            display.Dispose();
        }

        #endregion

        #region Update Tests

        [Fact]
        public void Update_ShouldNotThrow()
        {
            var display = CreateUninitialized<SkillPanelDisplay>();

            var exception = Record.Exception(() => display.Update(1.0));
            Assert.Null(exception);
        }

        #endregion

        #region Draw Guard Tests

        [Fact]
        public void Draw_WhenDisposed_ShouldReturnImmediately()
        {
            var display = CreateUninitialized<SkillPanelDisplay>();
            SetPrivateField(display, "_disposed", true);
            SetPrivateField(display, "_font", CreateUninitialized<ManagedFont>());

            var exception = Record.Exception(() => display.Draw(null!));
            Assert.Null(exception);
        }

        [Fact]
        public void Draw_WhenSpriteBatchNull_ShouldReturnImmediately()
        {
            var display = CreateUninitialized<SkillPanelDisplay>();
            SetPrivateField(display, "_disposed", false);
            SetPrivateField(display, "_font", CreateUninitialized<ManagedFont>());

            var exception = Record.Exception(() => display.Draw(null!));
            Assert.Null(exception);
        }

        [Fact]
        public void Draw_WhenFontNull_ShouldReturnImmediately()
        {
            var display = CreateUninitialized<SkillPanelDisplay>();
            SetPrivateField(display, "_disposed", false);
            SetPrivateField(display, "_font", null);

            var exception = Record.Exception(() => display.Draw(null!));
            Assert.Null(exception);
        }

        #endregion

        #region Property Tests

        [Fact]
        public void Skill_Property_ShouldGetAndSet()
        {
            var display = CreateUninitialized<SkillPanelDisplay>();

            display.Skill = 87.5;
            Assert.Equal(87.5, display.Skill);

            display.Skill = 0.0;
            Assert.Equal(0.0, display.Skill);
        }

        [Fact]
        public void ShowMax_Property_ShouldGetAndSet()
        {
            var display = CreateUninitialized<SkillPanelDisplay>();

            display.ShowMax = true;
            Assert.True(display.ShowMax);

            display.ShowMax = false;
            Assert.False(display.ShowMax);
        }

        #endregion

        #region Dispose Tests

        [Fact]
        public void Dispose_ShouldReleaseFontAndTextures()
        {
            var display = CreateUninitialized<SkillPanelDisplay>();
            var mockTexture = new Mock<ITexture>();
            SetPrivateField(display, "_font", null);
            SetPrivateField(display, "_maxBadgeTexture", mockTexture.Object);
            SetPrivateField(display, "_disposed", false);

            display.Dispose();

            Assert.Null(GetPrivateField<ManagedFont?>(display, "_font"));
            Assert.Null(GetPrivateField<ITexture?>(display, "_maxBadgeTexture"));
            Assert.True(GetPrivateField<bool>(display, "_disposed"));
            mockTexture.Verify(t => t.RemoveReference(), Times.Once);
        }

        [Fact]
        public void Dispose_CalledTwice_ShouldNotThrow()
        {
            var display = CreateUninitialized<SkillPanelDisplay>();
            SetPrivateField(display, "_font", null);
            SetPrivateField(display, "_maxBadgeTexture", null);
            SetPrivateField(display, "_disposed", false);

            display.Dispose();
            var exception = Record.Exception(() => display.Dispose());
            Assert.Null(exception);
        }

        #endregion

    }
}
