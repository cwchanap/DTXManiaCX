using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Unit")]
    public class SkillManagerCoverageTests
    {
        private static JudgementEvent JudgEvent(JudgementType type) =>
            new JudgementEvent(noteRef: 0, lane: 0, deltaMs: 0.0, type: type);

        [Fact]
        public void Dispose_WithActiveSubscribers_ShouldClearEventAndPreventFurtherUse()
        {
            var combo = new ComboManager();
            var sm = new SkillManager(100, combo);
            int eventCount = 0;
            sm.SkillChanged += (s, e) => eventCount++;

            combo.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            sm.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            Assert.Equal(1, eventCount);

            sm.Dispose();

            combo.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            sm.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            Assert.Equal(1, eventCount);
            Assert.Equal(1, sm.PerfectCount);
        }

        [Fact]
        public void Dispose_CalledTwice_ShouldNotThrow()
        {
            var combo = new ComboManager();
            var sm = new SkillManager(100, combo);

            sm.Dispose();
            sm.Dispose();

            combo.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            sm.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            Assert.Equal(0, sm.PerfectCount);
        }

        [Fact]
        public void Reset_AfterDispose_ShouldNotFireEvent()
        {
            var combo = new ComboManager();
            var sm = new SkillManager(100, combo);
            combo.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            sm.ProcessJudgement(JudgEvent(JudgementType.Perfect));

            sm.Dispose();

            SkillChangedEventArgs? captured = null;
            sm.SkillChanged += (s, e) => captured = e;
            sm.Reset();

            Assert.Equal(1, sm.PerfectCount);
            Assert.Null(captured);
        }

        [Fact]
        public void SkillChanged_AfterDisposeAndReSubscribe_ShouldNotFire()
        {
            var combo = new ComboManager();
            var sm = new SkillManager(100, combo);
            sm.Dispose();

            int eventCount = 0;
            sm.SkillChanged += (s, e) => eventCount++;

            combo.ProcessJudgement(JudgEvent(JudgementType.Perfect));
            sm.ProcessJudgement(JudgEvent(JudgementType.Perfect));

            Assert.Equal(0, eventCount);
        }
    }
}
