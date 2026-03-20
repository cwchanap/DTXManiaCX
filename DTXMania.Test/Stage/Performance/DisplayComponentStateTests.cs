using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Tests for display component state logic (ComboDisplay, GaugeDisplay, ScoreDisplay)
    /// that can be exercised without a real GraphicsDevice.
    /// These tests use FormatterServices to bypass the graphics-requiring constructors
    /// and test the pure state/logic portions of these components.
    /// </summary>
    [Trait("Category", "Performance")]
    public class DisplayComponentStateTests
    {
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

            // Start at 0 combo
            display.Combo = 0;
            var targetScaleBefore = GetPrivateField<float>(display, "_targetScale");

            // Increase combo
            display.Combo = 1;
            var targetScaleAfter = GetPrivateField<float>(display, "_targetScale");

            // Should have set target scale to combo hit scale (> 1.0)
            Assert.True(targetScaleAfter > targetScaleBefore || targetScaleAfter > 1.0f);
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

        #endregion

        #region Helper Methods

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

        #endregion
    }
}
