using System.Linq;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;
using static DTXMania.Test.TestData.ReflectionHelpers;
using TabHud = DTXMania.Game.Lib.UI.Layout.PerformanceUILayout.TabHud;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// GPU-free state tests for the Tab HUD feedback slots (HPA-509 Task 2): the
    /// newest-wins judgement popup, its per-hit fast/slow line, and the per-lane
    /// key flash slots. The display is built with an uninitialized GraphicsDevice —
    /// font/primitive acquisition fails exactly as on a headless runner, the draw
    /// paths skip, and the plain-state slots remain fully exercisable.
    /// </summary>
    [Trait("Category", "Unit")]
    [Collection("ManagedFont")]
    public class TabHudFeedbackTests
    {
        private static TabHudDisplay CreateDisplay()
        {
            // The ctor touches ManagedFont's process-global factory; pin it to the
            // pristine state (same seam the sibling ManagedFont-collection tests
            // use) so the uninitialized-GraphicsDevice path is deterministic
            // regardless of which tests ran first.
            var state = ManagedFont.CaptureFontFactoryStateForTesting();
            try
            {
                ManagedFont.ResetFontFactoryForTesting();
                return new TabHudDisplay(
                    CreateUninitialized<GraphicsDevice>(),
                    chart: null,
                    title: "Test Song",
                    artist: "CI");
            }
            finally
            {
                ManagedFont.RestoreFontFactoryStateForTesting(
                    state.ContentManager, state.DefaultFont, state.LoadedFonts);
            }
        }

        #region Key flash slots

        [Fact]
        public void TriggerKeyFlash_ShouldSetOnlyThatLanesSlot()
        {
            using var display = CreateDisplay();

            display.TriggerKeyFlash(3, TabHud.KeyFlashHitColor);

            Assert.Equal(1f, display.FlashTimersForTesting[3]);
            Assert.Equal(TabHud.KeyFlashHitColor, display.FlashColorsForTesting[3]);
            Assert.All(display.FlashTimersForTesting.Where((_, lane) => lane != 3), t => Assert.Equal(0f, t));
        }

        [Fact]
        public void TriggerKeyFlash_RapidSameLaneEvents_ShouldOverwriteSingleSlot()
        {
            using var display = CreateDisplay();

            display.TriggerKeyFlash(5, TabHud.KeyFlashHitColor);
            display.TriggerKeyFlash(5, TabHud.KeyFlashMissColor);

            // Newest wins, same slot — never a queue or second flash.
            Assert.Equal(1, display.FlashTimersForTesting.Count(t => t > 0f));
            Assert.Equal(1f, display.FlashTimersForTesting[5]);
            Assert.Equal(TabHud.KeyFlashMissColor, display.FlashColorsForTesting[5]);
        }

        [Fact]
        public void TriggerKeyFlash_Chord_ShouldLightIndependentLaneSlots()
        {
            using var display = CreateDisplay();

            display.TriggerKeyFlash(2, TabHud.KeyFlashHitColor);
            display.TriggerKeyFlash(5, TabHud.KeyFlashHitColor);

            Assert.Equal(1f, display.FlashTimersForTesting[2]);
            Assert.Equal(1f, display.FlashTimersForTesting[5]);
            Assert.Equal(2, display.FlashTimersForTesting.Count(t => t > 0f));
        }

        [Fact]
        public void TriggerKeyFlash_MissColor_ShouldBeRed()
        {
            Assert.Equal(Color.Red, TabHud.KeyFlashMissColor);

            using var display = CreateDisplay();
            display.TriggerKeyFlash(1, TabHud.KeyFlashMissColor);

            Assert.Equal(Color.Red, display.FlashColorsForTesting[1]);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(10)]
        public void TriggerKeyFlash_InvalidLane_ShouldBeIgnored(int lane)
        {
            using var display = CreateDisplay();

            display.TriggerKeyFlash(lane, TabHud.KeyFlashHitColor);

            Assert.All(display.FlashTimersForTesting, t => Assert.Equal(0f, t));
        }

        [Fact]
        public void Update_ShouldDecayFlashTimersToZeroWithoutGoingNegative()
        {
            using var display = CreateDisplay();
            display.TriggerKeyFlash(0, TabHud.KeyFlashHitColor);
            display.TriggerKeyFlash(9, TabHud.KeyFlashHitColor);

            display.Update(0.05); // 0.05 * FlashDecayRate(8) = 0.4

            Assert.Equal(0.6f, display.FlashTimersForTesting[0], 3);

            display.Update(10.0); // far past full decay

            Assert.Equal(0f, display.FlashTimersForTesting[0]);
            Assert.Equal(0f, display.FlashTimersForTesting[9]);
        }

        [Fact]
        public void Update_WhenNoFlashActive_ShouldKeepAllSlotsEmpty()
        {
            using var display = CreateDisplay();

            display.Update(0.5);

            Assert.All(display.FlashTimersForTesting, t => Assert.Equal(0f, t));
        }

        #endregion

        #region Newest-wins judgement popup slot

        [Fact]
        public void NotifyJudgement_ShouldSpawnPopupCenteredOnHitMarker()
        {
            using var display = CreateDisplay();

            display.NotifyJudgement("Perfect", Color.Yellow, showTiming: false, deltaMs: 0);

            var popup = display.PopupForTesting;
            Assert.NotNull(popup);
            Assert.Equal("Perfect", popup!.Text);
            Assert.True(popup.IsActive);
            // Font-less fallback: the headless display has no _titleFont, so centering
            // falls back to the raw center x (halfWidth 0); measured half-width centering
            // only happens when fonts acquired.
            Assert.Equal(TabHud.HitFeedbackCenterX, popup.CurrentPosition.X);
            Assert.Equal(TabHud.PopupWordTop, popup.CurrentPosition.Y);
        }

        [Fact]
        public void NotifyJudgement_RapidEvents_ShouldReplacePopupNewestWins()
        {
            using var display = CreateDisplay();

            display.NotifyJudgement("Perfect", Color.Yellow, showTiming: false, deltaMs: 0);
            var first = display.PopupForTesting;
            display.NotifyJudgement("Great", Color.LightGreen, showTiming: false, deltaMs: 0);

            Assert.NotSame(first, display.PopupForTesting);
            Assert.Equal("Great", display.PopupForTesting!.Text);
        }

        [Fact]
        public void NotifyJudgement_Miss_ShouldReplacePopupWithMissWord()
        {
            using var display = CreateDisplay();

            display.NotifyJudgement("Perfect", Color.Yellow, showTiming: false, deltaMs: 0);
            display.NotifyJudgement("Miss", Color.Red, showTiming: false, deltaMs: 0);

            Assert.Equal("Miss", display.PopupForTesting!.Text);
        }

        [Fact]
        public void NotifyJudgement_WhenTimingShown_ShouldClassifyByRoundedDelta()
        {
            using var display = CreateDisplay();

            display.NotifyJudgement("Great", Color.LightGreen, showTiming: true, deltaMs: -12.4);
            Assert.Equal("-12", display.PopupTimingForTesting.Text);
            Assert.False(display.PopupTimingForTesting.IsSlow);

            display.NotifyJudgement("Great", Color.LightGreen, showTiming: true, deltaMs: 12.4);
            Assert.Equal("12", display.PopupTimingForTesting.Text);
            Assert.True(display.PopupTimingForTesting.IsSlow);

            // Rounded to 0 → neutral text, never labelled fast or slow (AutoPlay).
            display.NotifyJudgement("Great", Color.LightGreen, showTiming: true, deltaMs: 0.4);
            Assert.Equal("0", display.PopupTimingForTesting.Text);
            Assert.False(display.PopupTimingForTesting.IsSlow);
        }

        [Fact]
        public void NotifyJudgement_WhenTimingHidden_ShouldClearPriorTimingLine()
        {
            using var display = CreateDisplay();

            display.NotifyJudgement("Great", Color.LightGreen, showTiming: true, deltaMs: -5);
            display.NotifyJudgement("Miss", Color.Red, showTiming: false, deltaMs: 0);

            Assert.Null(display.PopupTimingForTesting.Text);
            Assert.False(display.PopupTimingForTesting.IsSlow);
        }

        [Fact]
        public void Update_WhenPopupExpires_ShouldClearSlotAndTimingLine()
        {
            using var display = CreateDisplay();
            display.NotifyJudgement("Great", Color.LightGreen, showTiming: true, deltaMs: -5);

            display.Update(0.7); // JudgementTextPopup fades out over 0.6s

            Assert.Null(display.PopupForTesting);
            Assert.Null(display.PopupTimingForTesting.Text);
        }

        [Fact]
        public void Update_WhilePopupActive_ShouldRisePopupAndKeepSlot()
        {
            using var display = CreateDisplay();
            display.NotifyJudgement("Great", Color.LightGreen, showTiming: false, deltaMs: 0);
            float initialY = display.PopupForTesting!.CurrentPosition.Y;

            display.Update(0.3);

            var popup = display.PopupForTesting;
            Assert.NotNull(popup);
            Assert.True(popup!.IsActive);
            Assert.True(popup.CurrentPosition.Y < initialY);
        }

        [Fact]
        public void NotifyJudgement_TabPopup_ShouldUseReducedRiseDistance()
        {
            using var display = CreateDisplay();
            display.NotifyJudgement("Perfect", Color.Yellow, showTiming: false, deltaMs: 0);
            var popup = display.PopupForTesting!;

            display.Update(1.0); // run the 0.6s fade to completion

            // The Tab popup rises only PopupRiseDistance — shorter than the shared
            // Lanes 30px — so the word and the fast/slow line riding above it can
            // never reach the combo digits (HPA-509 review).
            Assert.Equal(TabHud.PopupWordTop - TabHud.PopupRiseDistance, popup.CurrentPosition.Y);
        }

        #endregion

        #region Readiness

        [Fact]
        public void IsReady_WhenDrawResourcesFailToAcquire_ShouldBeFalse()
        {
            // The stage keys its Tab-HUD/Lanes decision on this flag — a display
            // whose fonts could not load must report unready so the Lanes HUD
            // draws as the fallback (HPA-509 review).
            using var display = CreateDisplay();

            Assert.False(display.IsReady);
        }

        [Fact]
        public void MarkResourcesReadyForTesting_ShouldFlipIsReady()
        {
            using var display = CreateDisplay();

            display.MarkResourcesReadyForTesting();

            Assert.True(display.IsReady);
        }

        [Fact]
        public void Dispose_ShouldClearIsReady()
        {
            var display = CreateDisplay();
            display.MarkResourcesReadyForTesting();

            display.Dispose();

            Assert.False(display.IsReady);
        }

        #endregion

        [Fact]
        public void Clamp01_ShouldClampToUnitRangeAndRejectNonFiniteValues()
        {
            Assert.Equal(0f, TabHudDisplay.Clamp01(-0.5f));
            Assert.Equal(0f, TabHudDisplay.Clamp01(0f));
            Assert.Equal(0.5f, TabHudDisplay.Clamp01(0.5f));
            Assert.Equal(1f, TabHudDisplay.Clamp01(1f));
            Assert.Equal(1f, TabHudDisplay.Clamp01(2f));
            Assert.Equal(0f, TabHudDisplay.Clamp01(float.NaN));
            Assert.Equal(0f, TabHudDisplay.Clamp01(float.PositiveInfinity));
            Assert.Equal(0f, TabHudDisplay.Clamp01(float.NegativeInfinity));
        }

        [Fact]
        public void ToSafeSeconds_ShouldRejectNonFiniteAndClampExtremes()
        {
            // FormatDuration throws on NaN/overflow; the HUD collapses degenerate
            // chart timings to bounded seconds before formatting (HPA-509 review).
            Assert.Equal(0.0, TabHudDisplay.ToSafeSeconds(double.NaN));
            Assert.Equal(0.0, TabHudDisplay.ToSafeSeconds(double.PositiveInfinity));
            Assert.Equal(0.0, TabHudDisplay.ToSafeSeconds(double.NegativeInfinity));
            Assert.Equal(0.0, TabHudDisplay.ToSafeSeconds(-5000));
            Assert.Equal(1.5, TabHudDisplay.ToSafeSeconds(1500));
            Assert.Equal(359999.0, TabHudDisplay.ToSafeSeconds(1e15));
        }
    }
}
