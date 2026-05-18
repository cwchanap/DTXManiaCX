using System;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Tests for SkillManager. Verifies DTXManiaNX-faithful Playing Skill computation
    /// based on live judgement events and combo state.
    /// </summary>
    [Trait("Category", "Unit")]
    public class SkillManagerTests
    {
        private static JudgementEvent JudgEvent(JudgementType type) =>
            new JudgementEvent(noteRef: 0, lane: 0, deltaMs: 0.0, type: type);

        #region Constructor

        [Fact]
        public void Constructor_ValidTotalNotes_ShouldInitializeZero()
        {
            var combo = new ComboManager();
            var sm = new SkillManager(100, combo);
            Assert.Equal(0.0, sm.CurrentSkill);
            Assert.False(sm.IsMax);
            Assert.Equal(0, sm.PerfectCount);
        }

        [Fact]
        public void Constructor_ZeroTotalNotes_ShouldThrow()
        {
            Assert.Throws<ArgumentException>(() => new SkillManager(0, new ComboManager()));
        }

        [Fact]
        public void Constructor_NegativeTotalNotes_ShouldThrow()
        {
            Assert.Throws<ArgumentException>(() => new SkillManager(-5, new ComboManager()));
        }

        [Fact]
        public void Constructor_NullComboManager_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() => new SkillManager(100, null!));
        }

        #endregion

        #region ProcessJudgement counts

        [Fact]
        public void ProcessJudgement_PerfectIncrementsPerfectCount()
        {
            var sm = new SkillManager(10, new ComboManager());
            sm.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            Assert.Equal(1, sm.PerfectCount);
            Assert.Equal(0, sm.GreatCount);
        }

        [Fact]
        public void ProcessJudgement_AllFiveTypesIncrementCorrectly()
        {
            var sm = new SkillManager(10, new ComboManager());
            sm.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            sm.ProcessJudgement(JudgEvent(JudgementType.Great));
            sm.ProcessJudgement(JudgEvent(JudgementType.Good));
            sm.ProcessJudgement(JudgEvent(JudgementType.Poor));
            sm.ProcessJudgement(JudgEvent(JudgementType.Miss));
            Assert.Equal(1, sm.PerfectCount);
            Assert.Equal(1, sm.GreatCount);
            Assert.Equal(1, sm.GoodCount);
            Assert.Equal(1, sm.PoorCount);
            Assert.Equal(1, sm.MissCount);
        }

        [Fact]
        public void ProcessJudgement_NullEvent_ShouldBeIgnored()
        {
            var sm = new SkillManager(10, new ComboManager());
            sm.ProcessJudgement(null!);
            Assert.Equal(0, sm.PerfectCount);
        }

        #endregion

        #region CurrentSkill formula

        /// <summary>
        /// All Perfect + full combo → 100. Combo is fed via the real ComboManager.
        /// </summary>
        [Fact]
        public void CurrentSkill_AllPerfectFullCombo_ShouldReach100()
        {
            var combo = new ComboManager();
            var sm = new SkillManager(100, combo);
            for (int i = 0; i < 100; i++)
            {
                combo.ProcessJudgement(JudgEvent(JudgementType.Perfect));
                sm.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            }
            Assert.Equal(100.0, sm.CurrentSkill, 6);
            Assert.True(sm.IsMax);
        }

        [Fact]
        public void CurrentSkill_AllGreatFullCombo_ShouldReach50()
        {
            var combo = new ComboManager();
            var sm = new SkillManager(100, combo);
            for (int i = 0; i < 100; i++)
            {
                combo.ProcessJudgement(JudgEvent(JudgementType.Great));
                sm.ProcessJudgement(JudgEvent(JudgementType.Great));
            }
            Assert.Equal(50.0, sm.CurrentSkill, 6);
            Assert.False(sm.IsMax);
        }

        [Fact]
        public void CurrentSkill_HalfPerfectHalfMiss_WithPartialCombo_ShouldReturn49_85()
        {
            // Sequence: 1 Perfect, 1 Miss (combo break), 49 Perfect (combo 1→49, max=49), 49 Miss.
            // Final tally: PerfectCount=50, MissCount=50, MaxCombo=49.
            // Skill = 50*0.85 + 0 + 49*0.15 = 42.5 + 7.35 = 49.85.
            var combo = new ComboManager();
            var sm = new SkillManager(100, combo);
            combo.ProcessJudgement(JudgEvent(JudgementType.Perfect)); // combo=1, max=1
            sm.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            combo.ProcessJudgement(JudgEvent(JudgementType.Miss));    // combo reset
            sm.ProcessJudgement(JudgEvent(JudgementType.Miss));
            for (int i = 0; i < 49; i++)
            {
                combo.ProcessJudgement(JudgEvent(JudgementType.Perfect));
                sm.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            }
            for (int i = 0; i < 49; i++)
            {
                combo.ProcessJudgement(JudgEvent(JudgementType.Miss));
                sm.ProcessJudgement(JudgEvent(JudgementType.Miss));
            }
            Assert.Equal(49.85, sm.CurrentSkill, 4);
        }

        [Fact]
        public void CurrentSkill_NoJudgements_ShouldBeZero()
        {
            var sm = new SkillManager(100, new ComboManager());
            Assert.Equal(0.0, sm.CurrentSkill);
        }

        #endregion

        #region SkillChanged event

        [Fact]
        public void ProcessJudgement_ShouldRaiseSkillChanged()
        {
            var combo = new ComboManager();
            var sm = new SkillManager(100, combo);
            SkillChangedEventArgs? captured = null;
            sm.SkillChanged += (s, e) => captured = e;

            combo.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            sm.ProcessJudgement(JudgEvent(JudgementType.Perfect));

            Assert.NotNull(captured);
            Assert.Equal(0.0, captured!.PreviousSkill);
            Assert.True(captured.CurrentSkill > 0.0);
            Assert.Equal(JudgementType.Perfect, captured.JudgementType);
        }

        #endregion

        #region Reset

        [Fact]
        public void Reset_ShouldClearCountsAndFireEvent()
        {
            var combo = new ComboManager();
            var sm = new SkillManager(100, combo);
            combo.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            sm.ProcessJudgement(JudgEvent(JudgementType.Perfect));

            SkillChangedEventArgs? captured = null;
            sm.SkillChanged += (s, e) => captured = e;
            sm.Reset();

            Assert.Equal(0, sm.PerfectCount);
            Assert.Equal(0.0, sm.CurrentSkill);
            Assert.NotNull(captured);
            Assert.Equal(0.0, captured!.CurrentSkill);
        }

        #endregion
    }
}
