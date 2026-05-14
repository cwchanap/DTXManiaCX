using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Tests for display component state logic (ComboDisplay, GaugeDisplay, ScoreDisplay)
    /// that can be exercised without a real GraphicsDevice.
    /// These tests use FormatterServices to bypass the graphics-requiring constructors
    /// and test the pure state/logic portions of these components.
    /// </summary>
    [Collection("ManagedFont")]
    [Trait("Category", "Performance")]
    public class DisplayComponentStateTests
    {
        #region Constructor Guard Tests

        [Fact]
        public void ComboDisplay_Constructor_WithNullResourceManager_ShouldThrowArgumentNullException()
        {
            var graphicsDevice = CreateGraphicsDeviceStub();

            var ex = Assert.Throws<ArgumentNullException>(() => new ComboDisplay(null!, graphicsDevice));

            Assert.Equal("resourceManager", ex.ParamName);
        }

        [Fact]
        public void ComboDisplay_Constructor_WithNullGraphicsDevice_ShouldThrowArgumentNullException()
        {
            var resourceManager = new Mock<IResourceManager>();

            var ex = Assert.Throws<ArgumentNullException>(() => new ComboDisplay(resourceManager.Object, null!));

            Assert.Equal("graphicsDevice", ex.ParamName);
        }

        [Fact]
        public void ComboDisplay_Constructor_WhenFontFactoryUnavailable_ShouldInitializeHiddenDefaultState()
        {
            using var display = WithManagedFontFactoryUnavailable(() =>
                new ComboDisplay(new Mock<IResourceManager>().Object, CreateGraphicsDeviceStub()));

            Assert.Equal(0, display.Combo);
            Assert.Equal("0", GetPrivateField<string>(display, "_comboText"));
            Assert.False(GetPrivateField<bool>(display, "_visible"));
            Assert.Null(GetPrivateField<ManagedFont?>(display, "_comboFont"));
            Assert.Null(GetPrivateField<ManagedFont?>(display, "_labelFont"));
        }

        [Fact]
        public void GaugeDisplay_Constructor_WithNullResourceManager_ShouldThrowArgumentNullException()
        {
            var graphicsDevice = CreateGraphicsDeviceStub();

            var ex = Assert.Throws<ArgumentNullException>(() => new GaugeDisplay(null!, graphicsDevice));

            Assert.Equal("resourceManager", ex.ParamName);
        }

        [Fact]
        public void GaugeDisplay_Constructor_WithNullGraphicsDevice_ShouldThrowArgumentNullException()
        {
            var resourceManager = new Mock<IResourceManager>();

            var ex = Assert.Throws<ArgumentNullException>(() => new GaugeDisplay(resourceManager.Object, null!));

            Assert.Equal("graphicsDevice", ex.ParamName);
        }

        [Fact]
        public void ScoreDisplay_Constructor_WithNullResourceManager_ShouldThrowArgumentNullException()
        {
            var graphicsDevice = CreateGraphicsDeviceStub();

            var ex = Assert.Throws<ArgumentNullException>(() => new ScoreDisplay(null!, graphicsDevice));

            Assert.Equal("resourceManager", ex.ParamName);
        }

        [Fact]
        public void ScoreDisplay_Constructor_WithNullGraphicsDevice_ShouldThrowArgumentNullException()
        {
            var resourceManager = new Mock<IResourceManager>();

            var ex = Assert.Throws<ArgumentNullException>(() => new ScoreDisplay(resourceManager.Object, null!));

            Assert.Equal("graphicsDevice", ex.ParamName);
        }

        [Fact]
        public void ScoreDisplay_Constructor_WhenFontFactoryUnavailable_ShouldWrapLoadFailure()
        {
            var resourceManager = new Mock<IResourceManager>();

            var ex = WithManagedFontFactoryUnavailable(() =>
                Assert.Throws<InvalidOperationException>(() => new ScoreDisplay(resourceManager.Object, CreateGraphicsDeviceStub())));

            Assert.Contains("font could not be loaded", ex.Message);
            Assert.NotNull(ex.InnerException);
        }

        #endregion

        #region ComboDisplay State Tests

        [Fact]
        public void ComboDisplay_Combo_WhenSetToPositiveValue_ShouldRetainValue()
        {
#pragma warning disable SYSLIB0050
            var display = (ComboDisplay)FormatterServices.GetUninitializedObject(typeof(ComboDisplay));
#pragma warning restore SYSLIB0050

            display.Combo = 5;
            Assert.Equal(5, display.Combo);
        }

        [Fact]
        public void ComboDisplay_Combo_WhenSetToNegative_ShouldClampToZero()
        {
#pragma warning disable SYSLIB0050
            var display = (ComboDisplay)FormatterServices.GetUninitializedObject(typeof(ComboDisplay));
#pragma warning restore SYSLIB0050

            display.Combo = -10;
            Assert.Equal(0, display.Combo);
        }

        [Fact]
        public void ComboDisplay_Combo_WhenSetToZero_ShouldBeZero()
        {
#pragma warning disable SYSLIB0050
            var display = (ComboDisplay)FormatterServices.GetUninitializedObject(typeof(ComboDisplay));
#pragma warning restore SYSLIB0050

            display.Combo = 0;
            Assert.Equal(0, display.Combo);
        }

        [Fact]
        public void ComboDisplay_Combo_WhenIncreased_ShouldUpdateComboText()
        {
#pragma warning disable SYSLIB0050
            var display = (ComboDisplay)FormatterServices.GetUninitializedObject(typeof(ComboDisplay));
#pragma warning restore SYSLIB0050

            display.Combo = 42;

            var comboText = GetPrivateField<string>(display, "_comboText");
            Assert.Equal("42", comboText);
        }

        [Fact]
        public void ComboDisplay_Combo_WhenSetToZero_ShouldBeInvisible()
        {
#pragma warning disable SYSLIB0050
            var display = (ComboDisplay)FormatterServices.GetUninitializedObject(typeof(ComboDisplay));
#pragma warning restore SYSLIB0050

            display.Combo = 0;

            var visible = GetPrivateField<bool>(display, "_visible");
            Assert.False(visible);
        }

        [Fact]
        public void ComboDisplay_Combo_WhenSetToPositive_ShouldBeVisible()
        {
#pragma warning disable SYSLIB0050
            var display = (ComboDisplay)FormatterServices.GetUninitializedObject(typeof(ComboDisplay));
#pragma warning restore SYSLIB0050

            display.Combo = 1;

            var visible = GetPrivateField<bool>(display, "_visible");
            Assert.True(visible);
        }

        [Fact]
        public void ComboDisplay_Combo_WhenIncreasing_ShouldTriggerScaleAnimation()
        {
#pragma warning disable SYSLIB0050
            var display = (ComboDisplay)FormatterServices.GetUninitializedObject(typeof(ComboDisplay));
#pragma warning restore SYSLIB0050

            // Set _targetScale to its field-initializer value (1.0f) so state is deterministic
            SetPrivateField(display, "_targetScale", 1.0f);

            // Start at 0 combo
            display.Combo = 0;
            var targetScaleBefore = GetPrivateField<float>(display, "_targetScale");

            // Increase combo
            display.Combo = 1;
            var targetScaleAfter = GetPrivateField<float>(display, "_targetScale");

            // Should have set target scale to combo hit scale (1.5f), which is > the resting value
            Assert.True(targetScaleAfter > targetScaleBefore);
        }

        [Fact]
        public void ComboDisplay_TextColor_ShouldBeSettable()
        {
#pragma warning disable SYSLIB0050
            var display = (ComboDisplay)FormatterServices.GetUninitializedObject(typeof(ComboDisplay));
#pragma warning restore SYSLIB0050

            display.TextColor = Color.Red;
            Assert.Equal(Color.Red, display.TextColor);
        }

        [Fact]
        public void ComboDisplay_ShadowColor_ShouldBeSettable()
        {
#pragma warning disable SYSLIB0050
            var display = (ComboDisplay)FormatterServices.GetUninitializedObject(typeof(ComboDisplay));
#pragma warning restore SYSLIB0050

            display.ShadowColor = Color.Blue;
            Assert.Equal(Color.Blue, display.ShadowColor);
        }

        [Fact]
        public void ComboDisplay_ShadowOffset_ShouldBeSettable()
        {
#pragma warning disable SYSLIB0050
            var display = (ComboDisplay)FormatterServices.GetUninitializedObject(typeof(ComboDisplay));
#pragma warning restore SYSLIB0050

            display.ShadowOffset = new Vector2(3, 3);
            Assert.Equal(new Vector2(3, 3), display.ShadowOffset);
        }

        [Fact]
        public void ComboDisplay_LargeCombo_ShouldStoreCorrectText()
        {
#pragma warning disable SYSLIB0050
            var display = (ComboDisplay)FormatterServices.GetUninitializedObject(typeof(ComboDisplay));
#pragma warning restore SYSLIB0050

            display.Combo = 999;

            var comboText = GetPrivateField<string>(display, "_comboText");
            Assert.Equal("999", comboText);
        }

        [Fact]
        public void ComboDisplay_Update_WhenDisposed_ShouldLeaveAnimationStateUnchanged()
        {
            var display = CreateUninitialized<ComboDisplay>();
            SetPrivateField(display, "_disposed", true);
            SetPrivateField(display, "_scale", 1.25f);
            SetPrivateField(display, "_targetScale", 1.5f);
            SetPrivateField(display, "_scaleVelocity", 0.75f);

            display.Update(0.1);

            Assert.Equal(1.25f, GetPrivateField<float>(display, "_scale"));
            Assert.Equal(1.5f, GetPrivateField<float>(display, "_targetScale"));
            Assert.Equal(0.75f, GetPrivateField<float>(display, "_scaleVelocity"));
        }

        [Fact]
        public void ComboDisplay_Update_WhenActive_ShouldAdvanceAnimationTowardRestingScale()
        {
            var display = CreateUninitialized<ComboDisplay>();
            SetPrivateField(display, "_scale", 1.0f);
            SetPrivateField(display, "_targetScale", 1.5f);
            SetPrivateField(display, "_scaleVelocity", 0.0f);

            display.Update(0.1);

            Assert.True(GetPrivateField<float>(display, "_scale") > 1.0f);
            Assert.True(GetPrivateField<float>(display, "_scaleVelocity") > 0.0f);
            Assert.True(GetPrivateField<float>(display, "_targetScale") < 1.5f);
        }

        [Fact]
        public void ComboDisplay_Draw_WhenSpriteBatchIsNull_ShouldReturnWithoutThrowing()
        {
            var display = CreateUninitialized<ComboDisplay>();
            SetPrivateField(display, "_visible", true);

            var exception = Record.Exception(() => display.Draw(null!));

            Assert.Null(exception);
        }

        [Fact]
        public void ComboDisplay_Dispose_ShouldDisposeFontsAndClearReferences()
        {
            var display = CreateUninitialized<ComboDisplay>();
            var comboFont = CreateTrackingManagedFont();
            var labelFont = CreateTrackingManagedFont();
            SetPrivateField(display, "_comboFont", comboFont);
            SetPrivateField(display, "_labelFont", labelFont);

            display.Dispose();

            Assert.Equal(1, comboFont.DisposeCount);
            Assert.Equal(1, labelFont.DisposeCount);
            Assert.Null(GetPrivateField<ManagedFont?>(display, "_comboFont"));
            Assert.Null(GetPrivateField<ManagedFont?>(display, "_labelFont"));
            Assert.True(GetPrivateField<bool>(display, "_disposed"));
        }

        #endregion

        #region GaugeDisplay State Tests

        [Fact]
        public void GaugeDisplay_Value_WhenSetInRange_ShouldRetainValue()
        {
#pragma warning disable SYSLIB0050
            var display = (GaugeDisplay)FormatterServices.GetUninitializedObject(typeof(GaugeDisplay));
#pragma warning restore SYSLIB0050

            display.Value = 0.75f;
            Assert.Equal(0.75f, display.Value, 5);
        }

        [Fact]
        public void GaugeDisplay_Value_WhenSetToNegative_ShouldClampToZero()
        {
#pragma warning disable SYSLIB0050
            var display = (GaugeDisplay)FormatterServices.GetUninitializedObject(typeof(GaugeDisplay));
#pragma warning restore SYSLIB0050

            display.Value = -0.5f;
            Assert.Equal(0.0f, display.Value, 5);
        }

        [Fact]
        public void GaugeDisplay_Value_WhenSetAboveOne_ShouldClampToOne()
        {
#pragma warning disable SYSLIB0050
            var display = (GaugeDisplay)FormatterServices.GetUninitializedObject(typeof(GaugeDisplay));
#pragma warning restore SYSLIB0050

            display.Value = 1.5f;
            Assert.Equal(1.0f, display.Value, 5);
        }

        public static IEnumerable<object[]> GaugeDisplayColorData()
        {
            yield return new object[] { 0.9f, Color.Green };    // clearly above 0.8
            yield return new object[] { 0.8f, Color.Green };    // boundary: exactly 0.8
            yield return new object[] { 0.65f, Color.Yellow };  // between 0.5 and 0.8
            yield return new object[] { 0.5f, Color.Yellow };   // boundary: exactly 0.5
            yield return new object[] { 0.35f, Color.Orange };  // between 0.2 and 0.5
            yield return new object[] { 0.2f, Color.Orange };   // boundary: exactly 0.2
            yield return new object[] { 0.1f, Color.Red };      // below 0.2
        }

        [Theory]
        [MemberData(nameof(GaugeDisplayColorData))]
        public void GaugeDisplay_SetValue_SetsExpectedColor(float value, Color expectedColor)
        {
#pragma warning disable SYSLIB0050
            var display = (GaugeDisplay)FormatterServices.GetUninitializedObject(typeof(GaugeDisplay));
#pragma warning restore SYSLIB0050

            display.SetValue(value);

            Assert.Equal(expectedColor, display.FillColor);
        }

        [Theory]
        [MemberData(nameof(GaugeDisplayColorData))]
        public void GaugeDisplay_Value_WhenAssignedDirectly_ShouldAlsoRefreshFillColor(float value, Color expectedColor)
        {
            var display = CreateUninitialized<GaugeDisplay>();

            display.Value = value;

            Assert.Equal(expectedColor, display.FillColor);
        }

        [Fact]
        public void GaugeDisplay_FrameColor_ShouldBeSettable()
        {
#pragma warning disable SYSLIB0050
            var display = (GaugeDisplay)FormatterServices.GetUninitializedObject(typeof(GaugeDisplay));
#pragma warning restore SYSLIB0050

            display.FrameColor = Color.Gold;
            Assert.Equal(Color.Gold, display.FrameColor);
        }

        [Fact]
        public void GaugeDisplay_BackgroundColor_ShouldBeSettable()
        {
#pragma warning disable SYSLIB0050
            var display = (GaugeDisplay)FormatterServices.GetUninitializedObject(typeof(GaugeDisplay));
#pragma warning restore SYSLIB0050

            display.BackgroundColor = Color.Black;
            Assert.Equal(Color.Black, display.BackgroundColor);
        }

        [Fact]
        public void GaugeDisplay_Update_ShouldNotThrow()
        {
            var display = CreateUninitialized<GaugeDisplay>();

            var exception = Record.Exception(() => display.Update(0.016));

            Assert.Null(exception);
        }

        [Fact]
        public void GaugeDisplay_Draw_WhenSpriteBatchIsNull_ShouldReturnWithoutThrowing()
        {
            var display = CreateUninitialized<GaugeDisplay>();

            var exception = Record.Exception(() => display.Draw(null!));

            Assert.Null(exception);
        }

        [Fact]
        public void GaugeDisplay_Dispose_ShouldDisposeWhiteTextureAndClearReference()
        {
            var display = CreateUninitialized<GaugeDisplay>();
            var whiteTexture = CreateTrackingTexture2D();
            SetPrivateField(display, "_whiteTexture", whiteTexture);

            display.Dispose();

            Assert.True(whiteTexture.WasDisposed);
            Assert.Null(GetPrivateField<Texture2D?>(display, "_whiteTexture"));
            Assert.True(GetPrivateField<bool>(display, "_disposed"));
        }

        [Fact]
        public void GaugeDisplay_Dispose_WhenAlreadyDisposed_ShouldNotDisposeTextureTwice()
        {
            var display = CreateUninitialized<GaugeDisplay>();
            var whiteTexture = CreateTrackingTexture2D();
            SetPrivateField(display, "_whiteTexture", whiteTexture);

            display.Dispose();
            display.Dispose();

            Assert.Equal(1, whiteTexture.DisposeCount);
        }

        #endregion

        #region ScoreDisplay State Tests

        [Fact]
        public void ScoreDisplay_Score_WhenSetToValidValue_ShouldRetainValue()
        {
#pragma warning disable SYSLIB0050
            var display = (ScoreDisplay)FormatterServices.GetUninitializedObject(typeof(ScoreDisplay));
#pragma warning restore SYSLIB0050

            display.Score = 500000;
            Assert.Equal(500000, display.Score);
        }

        [Fact]
        public void ScoreDisplay_Score_WhenSetToNegative_ShouldClampToZero()
        {
#pragma warning disable SYSLIB0050
            var display = (ScoreDisplay)FormatterServices.GetUninitializedObject(typeof(ScoreDisplay));
#pragma warning restore SYSLIB0050

            display.Score = -100;
            Assert.Equal(0, display.Score);
        }

        [Fact]
        public void ScoreDisplay_Score_WhenSetAboveMax_ShouldClampToMax()
        {
#pragma warning disable SYSLIB0050
            var display = (ScoreDisplay)FormatterServices.GetUninitializedObject(typeof(ScoreDisplay));
#pragma warning restore SYSLIB0050

            display.Score = 10_000_000; // above MaxScore of 9,999,999
            Assert.Equal(9_999_999, display.Score);
        }

        [Fact]
        public void ScoreDisplay_Score_ShouldFormatWithLeadingZeros()
        {
#pragma warning disable SYSLIB0050
            var display = (ScoreDisplay)FormatterServices.GetUninitializedObject(typeof(ScoreDisplay));
#pragma warning restore SYSLIB0050

            display.Score = 1234;

            var scoreText = GetPrivateField<string>(display, "_scoreText");
            Assert.Equal("0001234", scoreText);
        }

        [Fact]
        public void ScoreDisplay_Score_WhenMaxValue_ShouldFormatCorrectly()
        {
#pragma warning disable SYSLIB0050
            var display = (ScoreDisplay)FormatterServices.GetUninitializedObject(typeof(ScoreDisplay));
#pragma warning restore SYSLIB0050

            display.Score = 9_999_999;

            var scoreText = GetPrivateField<string>(display, "_scoreText");
            Assert.Equal("9999999", scoreText);
        }

        [Fact]
        public void ScoreDisplay_Score_WhenZero_ShouldFormatAsAllZeros()
        {
#pragma warning disable SYSLIB0050
            var display = (ScoreDisplay)FormatterServices.GetUninitializedObject(typeof(ScoreDisplay));
#pragma warning restore SYSLIB0050

            display.Score = 0;

            var scoreText = GetPrivateField<string>(display, "_scoreText");
            Assert.Equal("0000000", scoreText);
        }

        [Fact]
        public void ScoreDisplay_TextColor_ShouldBeSettable()
        {
#pragma warning disable SYSLIB0050
            var display = (ScoreDisplay)FormatterServices.GetUninitializedObject(typeof(ScoreDisplay));
#pragma warning restore SYSLIB0050

            display.TextColor = Color.Cyan;
            Assert.Equal(Color.Cyan, display.TextColor);
        }

        [Fact]
        public void ScoreDisplay_ShadowOffset_ShouldBeSettable()
        {
#pragma warning disable SYSLIB0050
            var display = (ScoreDisplay)FormatterServices.GetUninitializedObject(typeof(ScoreDisplay));
#pragma warning restore SYSLIB0050

            display.ShadowOffset = new Vector2(1, 1);
            Assert.Equal(new Vector2(1, 1), display.ShadowOffset);
        }

        [Fact]
        public void ScoreDisplay_ShadowColor_ShouldBeSettable()
        {
            var display = CreateUninitialized<ScoreDisplay>();

            display.ShadowColor = Color.DarkSlateBlue;

            Assert.Equal(Color.DarkSlateBlue, display.ShadowColor);
        }

        [Fact]
        public void ScoreDisplay_Update_ShouldNotThrow()
        {
            var display = CreateUninitialized<ScoreDisplay>();

            var exception = Record.Exception(() => display.Update(0.016));

            Assert.Null(exception);
        }

        [Fact]
        public void ScoreDisplay_Draw_WhenSpriteBatchIsNull_ShouldReturnWithoutThrowing()
        {
            var display = CreateUninitialized<ScoreDisplay>();

            var exception = Record.Exception(() => display.Draw(null!));

            Assert.Null(exception);
        }

        [Fact]
        public void ScoreDisplay_Dispose_ShouldDisposeFontAndClearReference()
        {
            var display = CreateUninitialized<ScoreDisplay>();
            var scoreFont = CreateTrackingManagedFont();
            SetPrivateField(display, "_scoreFont", scoreFont);

            display.Dispose();

            Assert.Equal(1, scoreFont.DisposeCount);
            Assert.Null(GetPrivateField<ManagedFont?>(display, "_scoreFont"));
            Assert.True(GetPrivateField<bool>(display, "_disposed"));
        }

        [Fact]
        public void ScoreDisplay_Dispose_WhenAlreadyDisposed_ShouldNotDisposeFontTwice()
        {
            var display = CreateUninitialized<ScoreDisplay>();
            var scoreFont = CreateTrackingManagedFont();
            SetPrivateField(display, "_scoreFont", scoreFont);

            display.Dispose();
            display.Dispose();

            Assert.Equal(1, scoreFont.DisposeCount);
        }

        #endregion

        #region Helper Methods

        private static T CreateUninitialized<T>() where T : class
        {
#pragma warning disable SYSLIB0050
            return (T)FormatterServices.GetUninitializedObject(typeof(T));
#pragma warning restore SYSLIB0050
        }

        private static GraphicsDevice CreateGraphicsDeviceStub()
        {
            return CreateUninitialized<GraphicsDevice>();
        }

        private static TrackingManagedFont CreateTrackingManagedFont()
        {
            return CreateUninitialized<TrackingManagedFont>();
        }

        private static TrackingTexture2D CreateTrackingTexture2D()
        {
            return CreateUninitialized<TrackingTexture2D>();
        }

        private static T? GetPrivateField<T>(object target, string fieldName)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                    return (T?)field.GetValue(target);
                type = type.BaseType;
            }
            throw new InvalidOperationException($"Field '{fieldName}' not found on {target.GetType().Name}");
        }

        private static T WithManagedFontFactoryUnavailable<T>(Func<T> action)
        {
            var managedFontType = typeof(ManagedFont);
            var factoryLock = managedFontType
                .GetField("_fontFactoryLock", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null)!;
            var contentManagerField = managedFontType.GetField("_contentManager", BindingFlags.Static | BindingFlags.NonPublic)!;
            var originalContentManager = contentManagerField.GetValue(null);

            var loadedFontsField = managedFontType.GetField("_loadedFonts", BindingFlags.Static | BindingFlags.NonPublic)!;
            var loadedFonts = (System.Collections.IDictionary)loadedFontsField.GetValue(null)!;
            var originalEntries = new System.Collections.DictionaryEntry[loadedFonts.Count];
            loadedFonts.CopyTo(originalEntries, 0);

            lock (factoryLock)
            {
                try
                {
                    contentManagerField.SetValue(null, null);
                    loadedFonts.Clear();
                    return action();
                }
                finally
                {
                    loadedFonts.Clear();
                    foreach (System.Collections.DictionaryEntry entry in originalEntries)
                    {
                        loadedFonts[entry.Key] = entry.Value;
                    }
                    contentManagerField.SetValue(null, originalContentManager);
                }
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
                type = type.BaseType;
            }
            throw new InvalidOperationException($"Field '{fieldName}' not found on {target.GetType().Name}");
        }

        private sealed class TrackingManagedFont : ManagedFont
        {
            private TrackingManagedFont() : base((SpriteFont)null!, "tracking", 1)
            {
            }

            public int DisposeCount { get; private set; }

            protected override void Dispose(bool disposing)
            {
                DisposeCount++;
            }
        }

        private sealed class TrackingTexture2D : Texture2D
        {
            private TrackingTexture2D() : base(null!, 1, 1)
            {
            }

            public bool WasDisposed { get; private set; }
            public int DisposeCount { get; private set; }

            protected override void Dispose(bool disposing)
            {
                WasDisposed = true;
                DisposeCount++;
            }
        }

        #endregion
    }
}
