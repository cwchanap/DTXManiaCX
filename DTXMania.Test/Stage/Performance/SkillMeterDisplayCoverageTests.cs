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
using static DTXMania.Test.TestData.ReflectionHelpers;

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Unit")]
    public class SkillMeterDisplayCoverageTests
    {
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
        public void Constructor_TextureLoadFails_ShouldSetBackgroundTextureNull()
        {
            var rm = new Mock<IResourceManager>();
            rm.Setup(x => x.LoadTexture(It.IsAny<string>())).Throws(new Exception("load failed"));

            var display = CreateUninitializedDisplay();

            Assert.Null(GetPrivateField<ITexture>(display, "_backgroundTexture"));
            Assert.Null(GetPrivateField<ITexture>(display, "_gaugeTexture"));
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

            var display = CreateUninitializedDisplay();

            Assert.Null(GetPrivateField<ITexture>(display, "_backgroundTexture"));
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

        [Fact]
        public void Draw_WithNoTextures_ShouldNotThrow()
        {
            var display = CreateUninitializedDisplay();
            SetPrivateField(display, "_disposed", false);
            SetPrivateField(display, "_backgroundTexture", null);
            SetPrivateField(display, "_gaugeTexture", null);
            SetPrivateField(display, "_font", null);
            display.Skill = 50.0;

            var exception = Record.Exception(() => display.Draw(null!));
            Assert.Null(exception);
        }

        [Fact]
        public void Draw_WithBackgroundTextureAndNoGauge_ShouldNotThrow()
        {
            var display = CreateUninitializedDisplay();
            SetPrivateField(display, "_disposed", false);
            SetPrivateField(display, "_backgroundTexture", new Mock<ITexture>().Object);
            SetPrivateField(display, "_gaugeTexture", null);
            SetPrivateField(display, "_font", null);
            display.Skill = 0.0;

            var exception = Record.Exception(() => display.Draw(null!));
            Assert.Null(exception);
        }

        [Fact]
        public void Draw_WithGaugeTextureAndPositiveSkill_ShouldNotThrow()
        {
            var display = CreateUninitializedDisplay();
            SetPrivateField(display, "_disposed", false);
            SetPrivateField(display, "_backgroundTexture", new Mock<ITexture>().Object);
            SetPrivateField(display, "_gaugeTexture", new Mock<ITexture>().Object);
            SetPrivateField(display, "_font", null);
            display.Skill = 75.0;

            var exception = Record.Exception(() => display.Draw(null!));
            Assert.Null(exception);
        }

        [Fact]
        public void Draw_WithFontAndPositiveSkill_ShouldNotThrow()
        {
            var display = CreateUninitializedDisplay();
            SetPrivateField(display, "_disposed", false);
            SetPrivateField(display, "_backgroundTexture", new Mock<ITexture>().Object);
            SetPrivateField(display, "_gaugeTexture", new Mock<ITexture>().Object);
            SetPrivateField(display, "_font", null);
            display.Skill = 42.5;

            var exception = Record.Exception(() => display.Draw(null!));
            Assert.Null(exception);
        }

        [Fact]
        public void Draw_WithZeroSkill_ShouldNotDrawGaugeBar()
        {
            var display = CreateUninitializedDisplay();
            SetPrivateField(display, "_disposed", false);
            SetPrivateField(display, "_backgroundTexture", new Mock<ITexture>().Object);
            SetPrivateField(display, "_gaugeTexture", new Mock<ITexture>().Object);
            SetPrivateField(display, "_font", null);
            display.Skill = 0.0;

            var exception = Record.Exception(() => display.Draw(null!));
            Assert.Null(exception);
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
