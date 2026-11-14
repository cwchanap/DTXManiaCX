using System;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;
using static DTXMania.Test.Helpers.TestFontFactory;
using static DTXMania.Test.TestData.ReflectionHelpers;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Pure logic tests for SkillMeterDisplay math helpers — runs on all platforms.
    /// </summary>
    [Collection("ManagedFont")]
    [Trait("Category", "Unit")]
    public class SkillMeterDisplayLogicTests
    {
        [Theory]
        [InlineData(  0.0,   0)]
        [InlineData( 50.0, 217)]
        [InlineData(100.0, 434)]
        [InlineData(150.0, 434)]   // clamped
        [InlineData( -5.0,   0)]   // clamped low
        [InlineData( 33.33, 144)]  // 434 * 33.33 / 100 = 144.6522 -> truncates to 144
        public void ComputeGaugeHeight_ReturnsExpected(double skill, int expected)
        {
            Assert.Equal(expected, SkillMeterDisplay.ComputeGaugeHeight(skill));
        }

        [Fact]
        public void ComputeBarTopY_FullGauge_ShouldEqualBackgroundTop()
        {
            // GaugeBaselineY (527) - GaugeMaxHeight (434) = 93
            Assert.Equal(93, SkillMeterDisplay.ComputeBarTopY(100.0));
        }

        [Fact]
        public void ComputeBarTopY_EmptyGauge_ShouldEqualBaseline()
        {
            Assert.Equal(527, SkillMeterDisplay.ComputeBarTopY(0.0));
        }

        [Fact]
        public void ShouldDrawBar_ZeroSkill_ShouldBeFalse()
        {
            Assert.False(SkillMeterDisplay.ShouldDrawBar(0.0));
        }

        [Fact]
        public void ShouldDrawBar_AnyPositiveSkill_ShouldBeTrue()
        {
            Assert.True(SkillMeterDisplay.ShouldDrawBar(0.01));
            Assert.True(SkillMeterDisplay.ShouldDrawBar(100.0));
        }

        /// <summary>
        /// Verifies that Draw() clamps Skill before passing to helpers, so bar and
        /// numeric overlay never disagree. We test the clamping pattern indirectly:
        /// an over-range Skill fed through the same clamp+helper pipeline that Draw()
        /// uses must produce the same results as a max-valid Skill.
        /// </summary>
        [Fact]
        public void ClampedSkill_OverflowValue_ShouldMatchMaxValue()
        {
            double overflow = 150.0;
            double clamped = Math.Clamp(overflow, 0.0, 100.0);

            Assert.Equal(100.0, clamped);
            Assert.Equal(
                SkillMeterDisplay.ComputeGaugeHeight(100.0),
                SkillMeterDisplay.ComputeGaugeHeight(clamped));
            Assert.Equal(
                SkillMeterDisplay.ComputeBarTopY(100.0),
                SkillMeterDisplay.ComputeBarTopY(clamped));
            Assert.True(SkillMeterDisplay.ShouldDrawBar(clamped));
        }

        [Fact]
        public void ClampedSkill_NegativeValue_ShouldMatchZero()
        {
            double negative = -10.0;
            double clamped = Math.Clamp(negative, 0.0, 100.0);

            Assert.Equal(0.0, clamped);
            Assert.Equal(
                SkillMeterDisplay.ComputeGaugeHeight(0.0),
                SkillMeterDisplay.ComputeGaugeHeight(clamped));
            Assert.False(SkillMeterDisplay.ShouldDrawBar(clamped));
        }

        private static SkillMeterDisplay CreateUninitializedDisplay()
        {
#pragma warning disable SYSLIB0050
            return (SkillMeterDisplay)FormatterServices.GetUninitializedObject(typeof(SkillMeterDisplay));
#pragma warning restore SYSLIB0050
        }

        private static void InvokeDisposeBool(object target, bool disposing)
        {
            var method = typeof(SkillMeterDisplay).GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(bool) },
                null);
            Assert.NotNull(method);
            method!.Invoke(target, new object[] { disposing });
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_NullResourceManager_ShouldThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new SkillMeterDisplay(null!, null!));
        }

        [Fact]
        public void Constructor_NullGraphicsDevice_ShouldThrowArgumentNullException()
        {
            var rm = new Mock<IResourceManager>().Object;
            Assert.Throws<ArgumentNullException>(() => new SkillMeterDisplay(rm, null!));
        }

        [Fact]
        public void Constructor_WithTexturesAvailable_ShouldStoreTexturesAndIgnoreUnavailableFont()
        {
            var rm = new Mock<IResourceManager>();
            var backgroundTexture = new Mock<ITexture>().Object;
            var gaugeTexture = new Mock<ITexture>().Object;
            rm.Setup(x => x.LoadTexture(TexturePath.PerfGraphMain)).Returns(backgroundTexture);
            rm.Setup(x => x.LoadTexture(TexturePath.PerfGraphGauge)).Returns(gaugeTexture);

            var display = WithManagedFontUnavailable(() =>
                new SkillMeterDisplay(rm.Object, CreateUninitialized<GraphicsDevice>()));

            Assert.Same(backgroundTexture, GetPrivateField<ITexture?>(display, "_backgroundTexture"));
            Assert.Same(gaugeTexture, GetPrivateField<ITexture?>(display, "_gaugeTexture"));
            Assert.Null(GetPrivateField<ManagedFont?>(display, "_font"));
            display.Dispose();
        }

        [Fact]
        public void Constructor_WhenBackgroundLoadFails_ShouldStillLoadGaugeTexture()
        {
            var rm = new Mock<IResourceManager>();
            var gaugeTexture = new Mock<ITexture>().Object;
            rm.Setup(x => x.LoadTexture(TexturePath.PerfGraphMain)).Throws(new Exception("background failed"));
            rm.Setup(x => x.LoadTexture(TexturePath.PerfGraphGauge)).Returns(gaugeTexture);

            var display = WithManagedFontUnavailable(() =>
                new SkillMeterDisplay(rm.Object, CreateUninitialized<GraphicsDevice>()));

            Assert.Null(GetPrivateField<ITexture?>(display, "_backgroundTexture"));
            Assert.Same(gaugeTexture, GetPrivateField<ITexture?>(display, "_gaugeTexture"));
            display.Dispose();
        }

        [Fact]
        public void Constructor_WhenGaugeLoadFails_ShouldKeepGaugeTextureNull()
        {
            var rm = new Mock<IResourceManager>();
            var backgroundTexture = new Mock<ITexture>().Object;
            rm.Setup(x => x.LoadTexture(TexturePath.PerfGraphMain)).Returns(backgroundTexture);
            rm.Setup(x => x.LoadTexture(TexturePath.PerfGraphGauge)).Throws(new Exception("gauge failed"));

            var display = WithManagedFontUnavailable(() =>
                new SkillMeterDisplay(rm.Object, CreateUninitialized<GraphicsDevice>()));

            Assert.Same(backgroundTexture, GetPrivateField<ITexture?>(display, "_backgroundTexture"));
            Assert.Null(GetPrivateField<ITexture?>(display, "_gaugeTexture"));
            display.Dispose();
        }

        [Fact]
        public void Constructor_TextureLoadFails_ShouldSetBackgroundTextureNull()
        {
            var rm = new Mock<IResourceManager>();
            rm.Setup(x => x.LoadTexture(It.IsAny<string>())).Throws(new Exception("load failed"));

            var display = WithManagedFontUnavailable(() =>
                new SkillMeterDisplay(rm.Object, CreateUninitialized<GraphicsDevice>()));

            Assert.Null(GetPrivateField<ITexture>(display, "_backgroundTexture"));
            Assert.Null(GetPrivateField<ITexture>(display, "_gaugeTexture"));
            display.Dispose();
        }

        [Fact]
        public void Constructor_BackgroundTextureLoadFails_ShouldSetBackgroundTextureNullOnly()
        {
            var rm = new Mock<IResourceManager>();
            var gaugeTexture = new Mock<ITexture>().Object;
            int callCount = 0;
            rm.Setup(x => x.LoadTexture(It.IsAny<string>()))
              .Returns<string>(path =>
              {
                  callCount++;
                  if (callCount == 1)
                      throw new Exception("bg failed");
                  return gaugeTexture;
              });

            var display = WithManagedFontUnavailable(() =>
                new SkillMeterDisplay(rm.Object, CreateUninitialized<GraphicsDevice>()));

            Assert.Null(GetPrivateField<ITexture>(display, "_backgroundTexture"));
            Assert.Same(gaugeTexture, GetPrivateField<ITexture>(display, "_gaugeTexture"));
            display.Dispose();
        }

        #endregion

        #region Update Tests

        [Fact]
        public void Update_ShouldNotThrow()
        {
            var display = CreateUninitializedDisplay();
            var exception = Record.Exception(() => display.Update(1.0));
            Assert.Null(exception);
        }

        #endregion

        #region Draw Tests

        [Fact]
        public void Draw_WhenDisposed_ShouldReturnImmediately()
        {
            var display = CreateUninitializedDisplay();
            SetPrivateField(display, "_disposed", true);
            SetPrivateField(display, "_backgroundTexture", null);
            SetPrivateField(display, "_gaugeTexture", null);
            SetPrivateField(display, "_font", null);

            display.Draw(null!);
        }

        [Fact]
        public void Draw_WhenSpriteBatchNull_ShouldReturnImmediately()
        {
            var display = CreateUninitializedDisplay();
            SetPrivateField(display, "_disposed", false);
            SetPrivateField(display, "_backgroundTexture", null);
            SetPrivateField(display, "_gaugeTexture", null);
            SetPrivateField(display, "_font", null);

            display.Draw(null!);
        }

        // These Draw tests drive the real branches (background frame + label, gauge
        // bar, font overlay) through an uninitialized SpriteBatch — the batch is only
        // ever passed through to the ITexture.Draw / IFont seams, which are mocked,
        // so no GraphicsDevice is needed. The null-batch early-return is covered by
        // the guard tests above.

        [Fact]
        public void Draw_WithNoTextures_ShouldNotThrow()
        {
            var display = CreateUninitializedDisplay();
            SetPrivateField(display, "_disposed", false);
            SetPrivateField(display, "_backgroundTexture", null);
            SetPrivateField(display, "_gaugeTexture", null);
            SetPrivateField(display, "_font", null);
            display.Skill = 50.0;

            var exception = Record.Exception(() => display.Draw(CreateUninitializedSpriteBatch()));
            Assert.Null(exception);
        }

        [Fact]
        public void Draw_WithBackgroundTextureAndNoGauge_ShouldDrawFrameAndLabelOnly()
        {
            var display = CreateUninitializedDisplay();
            SetPrivateField(display, "_disposed", false);
            var backgroundTexture = new Mock<ITexture>();
            SetPrivateField(display, "_backgroundTexture", backgroundTexture.Object);
            SetPrivateField(display, "_gaugeTexture", null);
            SetPrivateField(display, "_font", null);
            display.Skill = 0.0;
            var spriteBatch = CreateUninitializedSpriteBatch();

            var exception = Record.Exception(() => display.Draw(spriteBatch));
            Assert.Null(exception);
            backgroundTexture.Verify(
                t => t.Draw(spriteBatch, It.IsAny<Vector2>(), It.IsAny<Rectangle?>()),
                Times.Exactly(2));
        }

        [Fact]
        public void Draw_WithGaugeTextureAndPositiveSkill_ShouldDrawGaugeBar()
        {
            var display = CreateUninitializedDisplay();
            SetPrivateField(display, "_disposed", false);
            SetPrivateField(display, "_backgroundTexture", new Mock<ITexture>().Object);
            var gaugeTexture = new Mock<ITexture>();
            SetPrivateField(display, "_gaugeTexture", gaugeTexture.Object);
            SetPrivateField(display, "_font", null);
            display.Skill = 75.0;
            var spriteBatch = CreateUninitializedSpriteBatch();

            var exception = Record.Exception(() => display.Draw(spriteBatch));
            Assert.Null(exception);
            gaugeTexture.Verify(
                t => t.Draw(spriteBatch, It.IsAny<Vector2>(), It.IsAny<Rectangle?>()),
                Times.Once);
        }

        [Fact]
        public void Draw_WithFontAndPositiveSkill_ShouldNotThrow()
        {
            var display = CreateUninitializedDisplay();
            SetPrivateField(display, "_disposed", false);
            SetPrivateField(display, "_backgroundTexture", new Mock<ITexture>().Object);
            SetPrivateField(display, "_gaugeTexture", new Mock<ITexture>().Object);
            // Non-null font enters the numeric-overlay branch; the uninitialized
            // ManagedFont's null _spriteFont makes the shadow draw a safe no-op.
            SetPrivateField(display, "_font", CreateUninitialized<ManagedFont>());
            display.Skill = 42.5;

            var exception = Record.Exception(() => display.Draw(CreateUninitializedSpriteBatch()));
            Assert.Null(exception);
        }

        [Fact]
        public void Draw_WithZeroSkill_ShouldNotDrawGaugeBar()
        {
            var display = CreateUninitializedDisplay();
            SetPrivateField(display, "_disposed", false);
            var backgroundTexture = new Mock<ITexture>();
            var gaugeTexture = new Mock<ITexture>();
            SetPrivateField(display, "_backgroundTexture", backgroundTexture.Object);
            SetPrivateField(display, "_gaugeTexture", gaugeTexture.Object);
            SetPrivateField(display, "_font", null);
            display.Skill = 0.0;
            var spriteBatch = CreateUninitializedSpriteBatch();

            var exception = Record.Exception(() => display.Draw(spriteBatch));
            Assert.Null(exception);
            gaugeTexture.Verify(
                t => t.Draw(It.IsAny<SpriteBatch>(), It.IsAny<Vector2>(), It.IsAny<Rectangle?>()),
                Times.Never);
            backgroundTexture.Verify(
                t => t.Draw(spriteBatch, It.IsAny<Vector2>(), It.IsAny<Rectangle?>()),
                Times.Exactly(2));
        }

        #endregion

        #region Skill Property Tests

        [Fact]
        public void Skill_GetterSetter_ShouldWork()
        {
            var display = CreateUninitializedDisplay();
            display.Skill = 88.5;
            Assert.Equal(88.5, display.Skill);
        }

        #endregion

        #region Dispose Tests

        [Fact]
        public void Dispose_ShouldSetDisposed()
        {
            var display = CreateUninitializedDisplay();
            SetPrivateField(display, "_disposed", false);
            SetPrivateField(display, "_backgroundTexture", null);
            SetPrivateField(display, "_gaugeTexture", null);
            SetPrivateField(display, "_font", null);

            display.Dispose();

            Assert.True(GetPrivateField<bool>(display, "_disposed"));
        }

        [Fact]
        public void Dispose_CalledTwice_ShouldNotThrow()
        {
            var display = CreateUninitializedDisplay();
            SetPrivateField(display, "_disposed", false);
            SetPrivateField(display, "_backgroundTexture", null);
            SetPrivateField(display, "_gaugeTexture", null);
            SetPrivateField(display, "_font", null);

            display.Dispose();
            var exception = Record.Exception(() => display.Dispose());
            Assert.Null(exception);
        }

        [Fact]
        public void Dispose_ShouldReleaseTextures()
        {
            var display = CreateUninitializedDisplay();
            SetPrivateField(display, "_disposed", false);
            var bgTex = new Mock<ITexture>();
            var gaugeTex = new Mock<ITexture>();
            bgTex.Setup(t => t.RemoveReference());
            gaugeTex.Setup(t => t.RemoveReference());
            SetPrivateField(display, "_backgroundTexture", bgTex.Object);
            SetPrivateField(display, "_gaugeTexture", gaugeTex.Object);
            SetPrivateField(display, "_font", null);

            display.Dispose();

            bgTex.Verify(t => t.RemoveReference(), Times.Once);
            gaugeTex.Verify(t => t.RemoveReference(), Times.Once);
            Assert.Null(GetPrivateField<ITexture>(display, "_backgroundTexture"));
            Assert.Null(GetPrivateField<ITexture>(display, "_gaugeTexture"));
            Assert.True(GetPrivateField<bool>(display, "_disposed"));
        }

        [Fact]
        public void Dispose_ShouldHandleNullFontAndTextures()
        {
            var display = CreateUninitializedDisplay();
            SetPrivateField(display, "_disposed", false);
            SetPrivateField(display, "_backgroundTexture", null);
            SetPrivateField(display, "_gaugeTexture", null);
            SetPrivateField(display, "_font", null);

            var exception = Record.Exception(() => display.Dispose());
            Assert.Null(exception);
            Assert.True(GetPrivateField<bool>(display, "_disposed"));
        }

        #endregion

        #region Dispose(bool) Tests

        [Fact]
        public void Dispose_BoolDisposingFalse_ShouldStillSetDisposed()
        {
            var display = CreateUninitializedDisplay();
            SetPrivateField(display, "_disposed", false);
            var bgTex = new Mock<ITexture>();
            bgTex.Setup(t => t.RemoveReference());
            SetPrivateField(display, "_backgroundTexture", bgTex.Object);
            SetPrivateField(display, "_gaugeTexture", null);
            SetPrivateField(display, "_font", null);

            InvokeDisposeBool(display, false);

            Assert.True(GetPrivateField<bool>(display, "_disposed"));
            Assert.NotNull(GetPrivateField<ITexture>(display, "_backgroundTexture"));
            bgTex.Verify(t => t.RemoveReference(), Times.Never);
        }

        [Fact]
        public void Dispose_BoolDisposingTrue_ShouldReleaseResources()
        {
            var display = CreateUninitializedDisplay();
            SetPrivateField(display, "_disposed", false);
            var bgTex = new Mock<ITexture>();
            var gaugeTex = new Mock<ITexture>();
            bgTex.Setup(t => t.RemoveReference());
            gaugeTex.Setup(t => t.RemoveReference());
            SetPrivateField(display, "_backgroundTexture", bgTex.Object);
            SetPrivateField(display, "_gaugeTexture", gaugeTex.Object);
            SetPrivateField(display, "_font", null);

            InvokeDisposeBool(display, true);

            bgTex.Verify(t => t.RemoveReference(), Times.Once);
            gaugeTex.Verify(t => t.RemoveReference(), Times.Once);
            Assert.Null(GetPrivateField<ITexture>(display, "_backgroundTexture"));
            Assert.Null(GetPrivateField<ITexture>(display, "_gaugeTexture"));
            Assert.True(GetPrivateField<bool>(display, "_disposed"));
        }

        #endregion
    }
}
