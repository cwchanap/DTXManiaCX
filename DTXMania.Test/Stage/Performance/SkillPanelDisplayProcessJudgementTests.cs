using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Xna.Framework;
using Moq;
using Xunit;
using static DTXMania.Test.TestData.ReflectionHelpers;

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Unit")]
    public class SkillPanelDisplayProcessJudgementTests
    {
        static SkillPanelDisplay CreateDisplay() => CreateUninitialized<SkillPanelDisplay>();

        static void SetField(object target, string name, object value) =>
            SetPrivateField(target, name, value);

        static T GetField<T>(object target, string name) =>
            GetPrivateField<T>(target, name)!;

        static T? InvokeStatic<T>(string methodName, params object[] args)
        {
            var method = typeof(SkillPanelDisplay).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(method);
            return (T?)method!.Invoke(null, args);
        }

        #region ProcessJudgement Tests

        [Fact]
        public void ProcessJudgement_NullEvent_ShouldNotCrash()
        {
            var display = CreateDisplay();

            var ex = Record.Exception(() => display.ProcessJudgement(null!, 0));

            Assert.Null(ex);
        }

        [Fact]
        public void ProcessJudgement_Perfect_ShouldIncrementPerfectCount()
        {
            var display = CreateDisplay();

            display.ProcessJudgement(new JudgementEvent(0, 0, 0, JudgementType.Perfect), 1);

            Assert.Equal(1, display.PerfectCount);
            Assert.Equal(0, display.GreatCount);
            Assert.Equal(0, display.GoodCount);
            Assert.Equal(0, display.PoorCount);
            Assert.Equal(0, display.MissCount);
        }

        [Fact]
        public void ProcessJudgement_Great_ShouldIncrementGreatCount()
        {
            var display = CreateDisplay();

            display.ProcessJudgement(new JudgementEvent(0, 0, 0, JudgementType.Great), 1);

            Assert.Equal(0, display.PerfectCount);
            Assert.Equal(1, display.GreatCount);
        }

        [Fact]
        public void ProcessJudgement_Good_ShouldIncrementGoodCount()
        {
            var display = CreateDisplay();

            display.ProcessJudgement(new JudgementEvent(0, 0, 0, JudgementType.Good), 1);

            Assert.Equal(1, display.GoodCount);
        }

        [Fact]
        public void ProcessJudgement_Poor_ShouldIncrementPoorCount()
        {
            var display = CreateDisplay();

            display.ProcessJudgement(new JudgementEvent(0, 0, 0, JudgementType.Poor), 1);

            Assert.Equal(1, display.PoorCount);
        }

        [Fact]
        public void ProcessJudgement_Miss_ShouldIncrementMissCount()
        {
            var display = CreateDisplay();

            display.ProcessJudgement(new JudgementEvent(0, 0, 0, JudgementType.Miss), 0);

            Assert.Equal(1, display.MissCount);
        }

        [Fact]
        public void ProcessJudgement_MultipleEvents_ShouldAccumulateCounts()
        {
            var display = CreateDisplay();

            display.ProcessJudgement(new JudgementEvent(0, 0, 0, JudgementType.Perfect), 1);
            display.ProcessJudgement(new JudgementEvent(1, 0, 0, JudgementType.Perfect), 2);
            display.ProcessJudgement(new JudgementEvent(2, 0, 0, JudgementType.Great), 3);
            display.ProcessJudgement(new JudgementEvent(3, 0, 0, JudgementType.Good), 4);
            display.ProcessJudgement(new JudgementEvent(4, 0, 0, JudgementType.Poor), 5);
            display.ProcessJudgement(new JudgementEvent(5, 0, 0, JudgementType.Miss), 6);

            Assert.Equal(2, display.PerfectCount);
            Assert.Equal(1, display.GreatCount);
            Assert.Equal(1, display.GoodCount);
            Assert.Equal(1, display.PoorCount);
            Assert.Equal(1, display.MissCount);
        }

        [Fact]
        public void ProcessJudgement_ShouldUpdateMaxCombo()
        {
            var display = CreateDisplay();

            display.ProcessJudgement(new JudgementEvent(0, 0, 0, JudgementType.Perfect), 5);
            Assert.Equal(5, display.MaxCombo);

            display.ProcessJudgement(new JudgementEvent(1, 0, 0, JudgementType.Perfect), 3);
            Assert.Equal(5, display.MaxCombo);

            display.ProcessJudgement(new JudgementEvent(2, 0, 0, JudgementType.Perfect), 10);
            Assert.Equal(10, display.MaxCombo);
        }

        [Fact]
        public void ProcessedJudgementCount_ShouldReflectAccumulatedCounts()
        {
            var display = CreateDisplay();

            display.ProcessJudgement(new JudgementEvent(0, 0, 0, JudgementType.Perfect), 1);
            display.ProcessJudgement(new JudgementEvent(1, 0, 0, JudgementType.Great), 2);
            display.ProcessJudgement(new JudgementEvent(2, 0, 0, JudgementType.Good), 3);
            display.ProcessJudgement(new JudgementEvent(3, 0, 0, JudgementType.Poor), 4);
            display.ProcessJudgement(new JudgementEvent(4, 0, 0, JudgementType.Miss), 5);

            Assert.Equal(5, display.ProcessedJudgementCount);
        }

        #endregion

        #region Fast/Slow Count Tests (HPA-509)

        [Fact]
        public void ProcessJudgement_NegativeDelta_ShouldIncrementFastCountOnly()
        {
            var display = CreateDisplay();

            display.ProcessJudgement(new JudgementEvent(0, 0, -12.4, JudgementType.Perfect), 0);

            Assert.Equal(1, display.FastCount);
            Assert.Equal(0, display.SlowCount);
        }

        [Fact]
        public void ProcessJudgement_PositiveDelta_ShouldIncrementSlowCountOnly()
        {
            var display = CreateDisplay();

            display.ProcessJudgement(new JudgementEvent(0, 0, 23.6, JudgementType.Great), 0);

            Assert.Equal(0, display.FastCount);
            Assert.Equal(1, display.SlowCount);
        }

        [Theory]
        [InlineData(0.0)]  // exact zero
        [InlineData(0.4)]  // rounds to 0 → neutral (sub-millisecond)
        [InlineData(-0.4)] // rounds to 0 → neutral (sub-millisecond)
        public void ProcessJudgement_RoundedZeroDelta_ShouldIncrementNeither(double delta)
        {
            var display = CreateDisplay();

            display.ProcessJudgement(new JudgementEvent(0, 0, delta, JudgementType.Good), 0);

            Assert.Equal(0, display.FastCount);
            Assert.Equal(0, display.SlowCount);
            Assert.Equal(1, display.GoodCount);
        }

        [Fact]
        public void ProcessJudgement_Miss_ShouldNeverCountFastOrSlow()
        {
            var display = CreateDisplay();

            display.ProcessJudgement(new JudgementEvent(0, 0, -50, JudgementType.Miss), 0);
            display.ProcessJudgement(new JudgementEvent(1, 0, 50, JudgementType.Miss), 0);

            Assert.Equal(2, display.MissCount);
            Assert.Equal(0, display.FastCount);
            Assert.Equal(0, display.SlowCount);
        }

        [Fact]
        public void ProcessJudgement_PoorHit_ShouldCountTowardFastOrSlow()
        {
            // Poor is a registered hit, so its delta classifies like any other hit.
            var display = CreateDisplay();

            display.ProcessJudgement(new JudgementEvent(0, 0, 15, JudgementType.Poor), 0);
            display.ProcessJudgement(new JudgementEvent(1, 0, -15, JudgementType.Poor), 0);

            Assert.Equal(2, display.PoorCount);
            Assert.Equal(1, display.FastCount);
            Assert.Equal(1, display.SlowCount);
        }

        [Fact]
        public void ProcessJudgement_AutoPlayRoundedZeroDeltas_ShouldIncrementNeither()
        {
            // AutoPlay contributes rounded-zero offsets; the always-visible totals
            // must therefore stay untouched by an AutoPlay run.
            var display = CreateDisplay();

            display.ProcessJudgement(new JudgementEvent(0, 0, 0, JudgementType.Perfect), 0);
            display.ProcessJudgement(new JudgementEvent(1, 0, 0.2, JudgementType.Perfect), 0);
            display.ProcessJudgement(new JudgementEvent(2, 0, -0.2, JudgementType.Perfect), 0);

            Assert.Equal(3, display.PerfectCount);
            Assert.Equal(0, display.FastCount);
            Assert.Equal(0, display.SlowCount);
        }

        [Fact]
        public void ProcessJudgement_FastSlow_ShouldAccumulateAcrossEvents()
        {
            var display = CreateDisplay();

            display.ProcessJudgement(new JudgementEvent(0, 0, -12, JudgementType.Perfect), 0);
            display.ProcessJudgement(new JudgementEvent(1, 0, -8, JudgementType.Great), 0);
            display.ProcessJudgement(new JudgementEvent(2, 0, 31, JudgementType.Good), 0);
            display.ProcessJudgement(new JudgementEvent(3, 0, 0, JudgementType.Miss), 0);

            Assert.Equal(2, display.FastCount);
            Assert.Equal(1, display.SlowCount);
        }

        #endregion

        #region GetSmallRateSourceRectangle Tests

        [Theory]
        [InlineData('0')]
        [InlineData('1')]
        [InlineData('2')]
        [InlineData('3')]
        [InlineData('4')]
        [InlineData('5')]
        [InlineData('6')]
        [InlineData('7')]
        [InlineData('8')]
        [InlineData('9')]
        public void GetSmallRateSourceRectangle_Digit_ShouldReturnRect(char ch)
        {
            var result = InvokeStatic<Rectangle?>("GetSmallRateSourceRectangle", ch);

            Assert.NotNull(result);
            var rect = result!.Value;
            Assert.Equal((ch - '0') * 20, rect.X);
            Assert.Equal(0, rect.Y);
            Assert.Equal(20, rect.Width);
            Assert.Equal(26, rect.Height);
        }

        [Fact]
        public void GetSmallRateSourceRectangle_Percent_ShouldReturnRect()
        {
            var result = InvokeStatic<Rectangle?>("GetSmallRateSourceRectangle", '%');

            Assert.NotNull(result);
            var rect = result!.Value;
            Assert.Equal(200, rect.X);
            Assert.Equal(0, rect.Y);
            Assert.Equal(20, rect.Width);
            Assert.Equal(26, rect.Height);
        }

        [Fact]
        public void GetSmallRateSourceRectangle_Dot_ShouldReturnRect()
        {
            var result = InvokeStatic<Rectangle?>("GetSmallRateSourceRectangle", '.');

            Assert.NotNull(result);
            var rect = result!.Value;
            Assert.Equal(210, rect.X);
            Assert.Equal(0, rect.Y);
            Assert.Equal(10, rect.Width);
            Assert.Equal(26, rect.Height);
        }

        [Theory]
        [InlineData('a')]
        [InlineData(' ')]
        [InlineData('-')]
        public void GetSmallRateSourceRectangle_InvalidChar_ShouldReturnNull(char ch)
        {
            var result = InvokeStatic<Rectangle?>("GetSmallRateSourceRectangle", ch);

            Assert.Null(result);
        }

        #endregion

        #region GetLargeRateSourceRectangle Tests

        [Theory]
        [InlineData('0')]
        [InlineData('5')]
        [InlineData('9')]
        public void GetLargeRateSourceRectangle_Digit_ShouldReturnRect(char ch)
        {
            var result = InvokeStatic<Rectangle?>("GetLargeRateSourceRectangle", ch);

            Assert.NotNull(result);
            var rect = result!.Value;
            Assert.Equal((ch - '0') * 28, rect.X);
            Assert.Equal(0, rect.Y);
            Assert.Equal(28, rect.Width);
            Assert.Equal(42, rect.Height);
        }

        [Fact]
        public void GetLargeRateSourceRectangle_Dot_ShouldReturnRect()
        {
            var result = InvokeStatic<Rectangle?>("GetLargeRateSourceRectangle", '.');

            Assert.NotNull(result);
            var rect = result!.Value;
            Assert.Equal(280, rect.X);
            Assert.Equal(0, rect.Y);
            Assert.Equal(10, rect.Width);
            Assert.Equal(42, rect.Height);
        }

        [Theory]
        [InlineData('a')]
        [InlineData('-')]
        public void GetLargeRateSourceRectangle_InvalidChar_ShouldReturnNull(char ch)
        {
            var result = InvokeStatic<Rectangle?>("GetLargeRateSourceRectangle", ch);

            Assert.Null(result);
        }

        #endregion

        #region GetLevelNumberSourceRectangle Tests

        [Theory]
        [InlineData('0')]
        [InlineData('5')]
        [InlineData('9')]
        public void GetLevelNumberSourceRectangle_Digit_ShouldReturnRect(char ch)
        {
            var result = InvokeStatic<Rectangle?>("GetLevelNumberSourceRectangle", ch);

            Assert.NotNull(result);
            var rect = result!.Value;
            Assert.Equal((ch - '0') * 16, rect.X);
            Assert.Equal(0, rect.Y);
            Assert.Equal(16, rect.Width);
            Assert.Equal(32, rect.Height);
        }

        [Fact]
        public void GetLevelNumberSourceRectangle_Dot_ShouldReturnRect()
        {
            var result = InvokeStatic<Rectangle?>("GetLevelNumberSourceRectangle", '.');

            Assert.NotNull(result);
            var rect = result!.Value;
            Assert.Equal(160, rect.X);
            Assert.Equal(0, rect.Y);
            Assert.Equal(5, rect.Width);
            Assert.Equal(32, rect.Height);
        }

        [Theory]
        [InlineData('-')]
        [InlineData('a')]
        public void GetLevelNumberSourceRectangle_InvalidChar_ShouldReturnNull(char ch)
        {
            var result = InvokeStatic<Rectangle?>("GetLevelNumberSourceRectangle", ch);

            Assert.Null(result);
        }

        #endregion

        #region TryLoadTexture Tests

        [Fact]
        public void TryLoadTexture_WhenLoadSucceeds_ShouldReturnTexture()
        {
            var mockTexture = new Mock<ITexture>();
            var mockResourceManager = new Mock<IResourceManager>();
            mockResourceManager
                .Setup(rm => rm.LoadTexture("some/path"))
                .Returns(mockTexture.Object);

            var result = InvokeStatic<ITexture?>("TryLoadTexture", mockResourceManager.Object, "some/path", "test");

            Assert.NotNull(result);
            Assert.Same(mockTexture.Object, result);
        }

        [Fact]
        public void TryLoadTexture_WhenLoadThrows_ShouldReturnNull()
        {
            var mockResourceManager = new Mock<IResourceManager>();
            mockResourceManager
                .Setup(rm => rm.LoadTexture(It.IsAny<string>()))
                .Throws(new Exception("load failed"));

            var result = InvokeStatic<ITexture?>("TryLoadTexture", mockResourceManager.Object, "bad/path", "test");

            Assert.Null(result);
        }

        #endregion

        #region Dispose with all texture fields

        [Fact]
        public void Dispose_WithAllTextures_ShouldRemoveReferencesOnAll()
        {
            var display = CreateDisplay();
            var mockMaxBadge = new Mock<ITexture>();
            var mockSmallRate = new Mock<ITexture>();
            var mockLargeRate = new Mock<ITexture>();
            var mockLevelNum = new Mock<ITexture>();
            var mockRatePercent = new Mock<ITexture>();

            SetField(display, "_font", null);
            SetField(display, "_disposed", false);
            SetField(display, "_maxBadgeTexture", mockMaxBadge.Object);
            SetField(display, "_smallRateNumbersTexture", mockSmallRate.Object);
            SetField(display, "_largeRateNumbersTexture", mockLargeRate.Object);
            SetField(display, "_levelNumbersTexture", mockLevelNum.Object);
            SetField(display, "_ratePercentTexture", mockRatePercent.Object);

            display.Dispose();

            mockMaxBadge.Verify(t => t.RemoveReference(), Times.Once);
            mockSmallRate.Verify(t => t.RemoveReference(), Times.Once);
            mockLargeRate.Verify(t => t.RemoveReference(), Times.Once);
            mockLevelNum.Verify(t => t.RemoveReference(), Times.Once);
            mockRatePercent.Verify(t => t.RemoveReference(), Times.Once);

            Assert.Null(GetField<ITexture?>(display, "_maxBadgeTexture"));
            Assert.Null(GetField<ITexture?>(display, "_smallRateNumbersTexture"));
            Assert.Null(GetField<ITexture?>(display, "_largeRateNumbersTexture"));
            Assert.Null(GetField<ITexture?>(display, "_levelNumbersTexture"));
            Assert.Null(GetField<ITexture?>(display, "_ratePercentTexture"));
            Assert.True(GetField<bool>(display, "_disposed"));
        }

        #endregion
    }
}
