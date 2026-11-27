using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Test.Helpers;
using DTXMania.Test.TestData;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class GameplayRulesTests
{
    [Fact]
    public void FrameState_ShouldUseManagersAndTimeOutNotesAfterEmptyHits()
    {
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("hud.dtx") { DurationMs = 10000 };
        for (int i = 0; i < 6; i++)
            chart.Notes.Add(new Note { LaneIndex = i, TimeMs = 1000 * (i + 1) });
        var rules = CreatePlayingRules(null, chart, input);
        var judgement = rules.JudgementManager!;
        void AssertStatusCounts()
        {
            foreach (var type in Enum.GetValues<JudgementType>())
                Assert.Equal(judgement.GetJudgementCount(type), judgement.GetStatusCount(type));
        }
        AssertStatusCounts();
        Assert.True(judgement.ResolveAutoHit(chart.Notes[0].Id));
        AssertStatusCounts();
        foreach (var (lane, time) in new[] { (1, 2040), (2, 3080), (3, 4120), (4, 5160) })
        {
            judgement.EnqueueLaneHit(lane);
            judgement.Update(time, 0);
            AssertStatusCounts();
        }
        var beforeTimeout = rules.BuildFrameState(2500);
        judgement.Update(0, 6201);
        AssertStatusCounts();
        var frame = rules.BuildFrameState(2500);
        var skill = rules.SkillManager!;

        Assert.Equal(rules.ScoreManager!.CurrentScore, frame.Score);
        Assert.Equal(rules.ComboManager!.CurrentCombo, frame.Combo);
        Assert.Equal(rules.ComboManager.MaxCombo, frame.MaxCombo);
        Assert.Equal(rules.GaugeManager!.CurrentLife / 100f, frame.Life);
        Assert.Equal(rules.GaugeManager.CurrentLife, frame.Gauge);
        Assert.False(frame.HasFailed);
        Assert.Equal(6, frame.TotalNotes);
        Assert.Equal(skill.CurrentSkill, frame.Achievement);
        Assert.Equal(skill.IsMax, frame.IsMaxSkill);
        Assert.Equal(1, frame.PerfectCount);
        Assert.Equal(1, frame.GreatCount);
        Assert.Equal(1, frame.GoodCount);
        Assert.Equal(1, frame.PoorCount);
        Assert.Equal(2, frame.MissCount);
        Assert.Equal(skill.MissCount, frame.MissCount);
        Assert.Equal(judgement.GetJudgementCount(JudgementType.Perfect), frame.PerfectCount);
        Assert.Equal(judgement.GetJudgementCount(JudgementType.Great), frame.GreatCount);
        Assert.Equal(judgement.GetJudgementCount(JudgementType.Good), frame.GoodCount);
        Assert.Equal(judgement.GetJudgementCount(JudgementType.Poor), frame.PoorCount);
        Assert.Equal(2, judgement.GetJudgementCount(JudgementType.Miss));
        Assert.Equal(1, frame.StatusPerfectCount);
        Assert.Equal(1, frame.StatusGreatCount);
        Assert.Equal(1, frame.StatusGoodCount);
        Assert.Equal(1, frame.StatusPoorCount);
        Assert.Equal(2, frame.StatusMissCount);
        Assert.Equal(0, beforeTimeout.StatusMissCount);
        var summary = rules.BuildSummary(CompletionReason.SongComplete, new PlaybackModifiers(100, 0), 78, 33);
        Assert.Equal(2, summary.MissCount);
        // The +160 ms hit on lane 4 is outside the Poor window: it judges nothing and the note times out.
        Assert.Equal(NoteStatus.Missed, judgement.GetNoteRuntimeData(chart.Notes[4].Id)!.Status);
        Assert.Equal(NoteStatus.Missed, judgement.GetNoteRuntimeData(chart.Notes[5].Id)!.Status);
        Assert.Equal(0, beforeTimeout.MissCount);
        Assert.Equal(0.25f, frame.Progress);
        Assert.Equal(2500, frame.ElapsedMs);
        Assert.Equal(10000, frame.TotalMs);
        Assert.False(frame.IsDanger);
        Assert.Equal(0, frame.FastCount);
        Assert.Equal(3, frame.SlowCount);
        var total = SkillPanelDisplay.GetProcessedJudgementCount(
            frame.PerfectCount, frame.GreatCount, frame.GoodCount, frame.PoorCount, frame.MissCount);
        Assert.Equal(6, total);
        Assert.Equal("   2", SkillPanelDisplay.FormatJudgementCount(frame.MissCount));
        Assert.Equal(" 33%", SkillPanelDisplay.FormatJudgementPercent(frame.MissCount, total));
        rules.ClearJudgement();
        AssertStatusCounts();
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();
    }

    [Fact]
    public void FrameStateWithoutManagers_ShouldKeepHudAndApiGaugeDefaultsDistinct()
    {
        var frame = new GameplayRules(null).BuildFrameState(0);

        Assert.Equal(0.5f, frame.Life);
        Assert.Equal(0f, frame.Gauge);
        Assert.False(frame.HasFailed);
        Assert.Equal(0, frame.TotalNotes);
        Assert.Equal(0, frame.StatusPerfectCount);
        Assert.Equal(0, frame.StatusGreatCount);
        Assert.Equal(0, frame.StatusGoodCount);
        Assert.Equal(0, frame.StatusPoorCount);
        Assert.Equal(0, frame.StatusMissCount);
    }

    [Fact]
    public void FrameStateMaxSkillDangerAndEmptyDuration_ShouldFollowManagers()
    {
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("max-hud.dtx");
        chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
        var rules = CreatePlayingRules(null, chart, input);
        var before = rules.BuildFrameState(500);
        Assert.False(before.IsMaxSkill);
        Assert.Equal(0f, before.Progress);
        Assert.Equal(0, before.TotalMs);
        Assert.True(rules.JudgementManager!.ResolveAutoHit(chart.Notes[0].Id));
        rules.GaugeManager!.Reset(15f);
        var after = rules.BuildFrameState(500);
        Assert.Equal(100.0, after.Achievement);
        Assert.True(after.IsMaxSkill);
        Assert.Equal(0.15f, after.Life);
        Assert.True(after.IsDanger);
        Assert.False(before.IsMaxSkill);
        Assert.False(before.IsDanger);
        Assert.Equal("100.00", SkillPanelDisplay.FormatSkillText(after.Achievement));
        rules.ClearJudgement();
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();
    }

    [Fact]
    public void FrameStateFastSlow_ShouldUseRoundedSuccessfulManualHitsAndReset()
    {
        var config = new ConfigData();
        config.AutoPlayLanes.Add(1);
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("timings.dtx") { DurationMs = 12000 };
        double[] deltas = { -160, -120, -40, -0.5, -0.4, 0, 0.4, 0.5, 80, 120, 160 };
        for (int i = 0; i < deltas.Length; i++)
            chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 * (i + 1) });
        chart.Notes.Add(new Note { LaneIndex = 1, TimeMs = 12000 });
        var rules = CreatePlayingRules(config, chart, input);
        var judgement = rules.JudgementManager!;
        for (int i = 0; i < deltas.Length; i++)
        {
            judgement.EnqueueLaneHit(0);
            judgement.Update(chart.Notes[i].TimeMs + deltas[i], 0);
        }
        Assert.True(judgement.ResolveAutoHit(chart.Notes[deltas.Length].Id));
        var frame = rules.BuildFrameState(20000);
        Assert.Equal(3, frame.FastCount);
        Assert.Equal(3, frame.SlowCount);
        Assert.Equal(2, frame.PoorCount);
        Assert.Equal(0, frame.MissCount);
        Assert.Equal(NoteStatus.Pending, judgement.GetNoteRuntimeData(chart.Notes[0].Id)!.Status);
        Assert.Equal(NoteStatus.Pending, judgement.GetNoteRuntimeData(chart.Notes[deltas.Length - 1].Id)!.Status);
        Assert.Equal(1f, frame.Progress);
        Assert.Equal(20000, frame.ElapsedMs);
        rules.ClearJudgement();
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();

        rules.InitializeJudgement(input);
        rules.InitializeScoreAndCombo();
        rules.InitializeGaugeAndSkill();
        frame = rules.BuildFrameState(-100);
        Assert.Equal(0, frame.FastCount);
        Assert.Equal(0, frame.SlowCount);
        Assert.Equal(0, frame.Progress);
        rules.ClearJudgement();
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();
    }

    [Fact]
    public void FrameStateDuringSynchronousFailure_ShouldSeePreSkillCountsAndNoFastSlowAfterCleanup()
    {
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("failure-hud.dtx") { DurationMs = 2000 };
        chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
        var rules = CreatePlayingRules(new ConfigData { Risky = 1 }, chart, input);
        var judgement = rules.JudgementManager!;
        PerformanceFrameState duringFailure = default;
        int resultPoorCount = 0;
        rules.GaugeManager!.Failed += (_, _) =>
        {
            duringFailure = rules.BuildFrameState(1120);
            resultPoorCount = judgement.GetJudgementCount(JudgementType.Poor);
            Assert.Equal(resultPoorCount, judgement.GetStatusCount(JudgementType.Poor));
            rules.ClearJudgement();
            rules.ClearScoreAndCombo();
            rules.ClearGaugeAndSkill();
        };
        judgement.EnqueueLaneHit(0);
        judgement.Update(1120, 0);

        Assert.False(duringFailure.IsDanger); // Risky can fail with a healthy gauge.
        Assert.Equal(0, duringFailure.PoorCount);
        Assert.Equal(1, duringFailure.StatusPoorCount);
        Assert.True(duringFailure.HasFailed);
        Assert.Equal(0, duringFailure.SlowCount);
        Assert.Equal(1, resultPoorCount);
        Assert.Equal(0, rules.BuildFrameState(1120).SlowCount);
    }

    [Fact]
    public void FrameStateFailureRebinding_ShouldNotSendOldJudgementToNewManagers()
    {
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("old-run.dtx");
        chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
        var rules = CreatePlayingRules(new ConfigData { Risky = 1 }, chart, input);
        var oldJudgement = rules.JudgementManager!;
        rules.GaugeManager!.Failed += (_, _) =>
        {
            rules.ClearJudgement();
            rules.ClearScoreAndCombo();
            rules.ClearGaugeAndSkill();
            var nextChart = new ParsedChart("new-run.dtx");
            nextChart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
            rules.BindChart(nextChart);
            rules.InitializeJudgement(input);
            rules.InitializeScoreAndCombo();
            rules.InitializeGaugeAndSkill();
        };
        oldJudgement.EnqueueLaneHit(0);
        oldJudgement.Update(1120, 0);

        var frame = rules.BuildFrameState(0);
        Assert.Equal(0, frame.PoorCount);
        Assert.Equal(0, frame.SlowCount);
        Assert.Equal(0, frame.Score);
        rules.ClearJudgement();
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AutoPlayRebindingDuringScoreOrCombo_ShouldNotAffectNewManagersOrFeedback(bool duringScore)
    {
        var config = new ConfigData();
        config.AutoPlayLanes.Add(0);
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("old-auto.dtx");
        chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
        var rules = CreatePlayingRules(config, chart, input);
        void Rebind()
        {
            rules.ClearJudgement();
            rules.ClearScoreAndCombo();
            rules.ClearGaugeAndSkill();
            var next = new ParsedChart("next-auto.dtx");
            next.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
            rules.BindChart(next);
            rules.InitializeJudgement(input);
            rules.InitializeScoreAndCombo();
            rules.InitializeGaugeAndSkill();
            rules.JudgementManager!.IsActive = true;
        }
        if (duringScore)
            rules.ScoreManager!.ScoreChanged += (_, _) => Rebind();
        else
            rules.ComboManager!.ComboChanged += (_, _) => Rebind();
        int feedback = 0;

        rules.ProcessAutoPlay(1000, _ => feedback++);

        Assert.Equal(0, feedback);
        Assert.Equal(0, rules.ScoreManager!.CurrentScore);
        Assert.Equal(0, rules.ComboManager!.CurrentCombo);
        Assert.Equal(GaugeManager.StartingLife, rules.GaugeManager!.CurrentLife);
        Assert.Equal(0, rules.SkillManager!.PerfectCount);
        rules.ProcessAutoPlay(1000, _ => feedback++);
        Assert.Equal(1, feedback);
        Assert.Equal(1000000, rules.ScoreManager.CurrentScore);
        Assert.Equal(1, rules.ComboManager.CurrentCombo);
        rules.ClearJudgement();
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();
    }

    [Fact]
    public void Completion_ShouldUseRealTimeFromEndCrossingAndResetOnSeekOrSongStart()
    {
        var rules = new GameplayRules(null);
        Assert.Null(rules.CheckCompletion(5000, null, 1));
        Assert.Null(rules.CheckCompletion(999, 1000, 9));
        Assert.Null(rules.CheckCompletion(5000, 1000, 10));
        Assert.Null(rules.CheckCompletion(5000, 1000, 12.999));
        Assert.Null(rules.CheckCompletion(999, 1000, 13));
        Assert.Null(rules.CheckCompletion(1000, 1000, 20));
        Assert.Null(rules.CheckCompletion(5000, 1000, 22.999));
        rules.ResetSongEndBuffer();
        Assert.Null(rules.CheckCompletion(5000, 1000, 23));
        Assert.Equal(CompletionReason.SongComplete, rules.CheckCompletion(5000, 1000, 26));
        Assert.True(rules.TryComplete());
        Assert.False(rules.TryComplete());
        Assert.Null(rules.CheckCompletion(5000, 1000, 30));
        rules.ResetCompletion();
        Assert.Null(rules.CheckCompletion(5000, 1000, 30));
        Assert.Equal(CompletionReason.SongComplete, rules.CheckCompletion(5000, 1000, 33));
    }

    [Fact]
    public void CompletionSongEnd_ShouldTakePriorityOverAlreadyFailedGauge()
    {
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("completion-priority.dtx") { DurationMs = 1000 };
        var rules = CreatePlayingRules(null, chart, input);
        Assert.Null(rules.CheckCompletion(1000, chart.DurationMs, 10));
        rules.GaugeManager!.Reset(2);
        rules.GaugeManager.ProcessJudgement(new JudgementEvent(0, 0, 0, JudgementType.Miss));
        Assert.True(rules.GaugeManager.HasFailed);
        Assert.Null(rules.CheckCompletion(5000, null, 13));
        Assert.Equal(CompletionReason.SongComplete, rules.CheckCompletion(5000, chart.DurationMs, 13));
        Assert.Equal(CompletionReason.PlayerFailed, rules.CheckCompletion(999, chart.DurationMs, 13));
        rules.ClearJudgement();
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();
    }

    [Fact]
    public void Summary_ShouldUseNoteStatusesAndFrozenPolicyWithAllJudgementsAndChartLevel()
    {
        var config = new ConfigData();
        config.AutoPlayLanes.Add(9);
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("summary.dtx");
        for (int i = 0; i < 6; i++)
            chart.Notes.Add(new Note { LaneIndex = i, TimeMs = 1000 * (i + 1) });
        var rules = CreatePlayingRules(config, chart, input);
        config.AutoPlayLanes.Clear();
        var judgement = rules.JudgementManager!;
        judgement.ResolveAutoHit(chart.Notes[0].Id);
        foreach (var (lane, time) in new[] { (1, 2040), (2, 3080), (3, 4120), (4, 5160) })
        {
            judgement.EnqueueLaneHit(lane);
            judgement.Update(time, 0);
        }
        judgement.Update(0, 6201);
        Assert.True(rules.TryComplete());
        var summary = rules.BuildSummary(CompletionReason.SongComplete, new PlaybackModifiers(75, -4), 78, 33);

        Assert.NotEqual(Guid.Empty, summary.RunId);
        Assert.True(summary.UsedAutoPlay);
        Assert.False(summary.IsSavable);
        Assert.Equal(75, summary.PlaySpeedPercent);
        Assert.Equal(-4, summary.PitchSemitones);
        Assert.True(summary.ClearFlag);
        Assert.Equal(CompletionReason.SongComplete, summary.CompletionReason);
        Assert.Equal(rules.ScoreManager!.CurrentScore, summary.Score);
        Assert.Equal(3, summary.MaxCombo);
        Assert.Equal(1, summary.PerfectCount);
        Assert.Equal(1, summary.GreatCount);
        Assert.Equal(1, summary.GoodCount);
        Assert.Equal(1, summary.PoorCount);
        Assert.Equal(2, summary.MissCount);
        Assert.Equal(2, rules.SkillManager!.MissCount);
        Assert.Equal(6, summary.TotalNotes);
        Assert.Equal(rules.GaugeManager!.CurrentLife, summary.FinalLife);
        Assert.Equal(rules.SkillManager.CurrentSkill, summary.PlayingSkill);
        Assert.Equal(SongScore.CalculateGameSkill(summary.PlayingSkill, 78, 33), summary.GameSkill);
        Assert.Equal(78, summary.ChartLevel);
        Assert.Equal(33, summary.ChartLevelDec);
        rules.ClearJudgement();
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();
    }

    [Theory]
    [InlineData(CompletionReason.SongComplete, true, true)]
    [InlineData(CompletionReason.PlayerFailed, false, true)]
    [InlineData(CompletionReason.PlayerQuit, false, false)]
    public void SummaryWithoutManagers_ShouldUseDefaultsAndReason(CompletionReason reason, bool clear, bool savable)
    {
        var rules = new GameplayRules(null);
        Assert.True(rules.TryComplete());
        var summary = rules.BuildSummary(reason, new PlaybackModifiers(100, 0), 0, 0);
        Assert.NotEqual(Guid.Empty, summary.RunId);
        Assert.Equal(reason, summary.CompletionReason);
        Assert.Equal(clear, summary.ClearFlag);
        Assert.Equal(savable, summary.IsSavable);
        Assert.False(summary.UsedAutoPlay);
        Assert.Equal(0, summary.Score);
        Assert.Equal(0, summary.MaxCombo);
        Assert.Equal(0, summary.TotalJudgements);
        Assert.Equal(0, summary.TotalNotes);
        Assert.Equal(0, summary.FinalLife);
        Assert.Equal(0, summary.PlayingSkill);
        Assert.Equal(0, summary.GameSkill);
    }

    [Theory]
    [InlineData(1120, 0, 1, 0)]
    [InlineData(0, 1201, 0, 1)]
    public void SummaryGaugeFailure_ShouldSeeFailingNoteStatusBeforeSkillFanout(
        int hitTime, int missTime, int poorCount, int missCount)
    {
        var config = new ConfigData { Risky = 1 };
        config.AutoPlayLanes.Add(0);
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("synchronous-failure.dtx");
        chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 100 });
        chart.Notes.Add(new Note { LaneIndex = 1, TimeMs = 1000 });
        var rules = CreatePlayingRules(config, chart, input);
        var judgement = rules.JudgementManager!;
        Assert.True(judgement.ResolveAutoHit(chart.Notes[0].Id));
        var preEventSkill = rules.SkillManager!.CurrentSkill;
        PerformanceSummary? summary = null;
        rules.GaugeManager!.Failed += (_, _) =>
        {
            Assert.True(rules.TryComplete());
            summary = rules.BuildSummary(CompletionReason.PlayerFailed, new PlaybackModifiers(100, 0), 78, 33);
            Assert.Equal(0, rules.SkillManager!.PoorCount);
            Assert.Equal(0, rules.SkillManager.MissCount);
        };

        if (hitTime != 0)
            judgement.EnqueueLaneHit(1);
        judgement.Update(hitTime, missTime);

        Assert.NotNull(summary);
        Assert.Equal(CompletionReason.PlayerFailed, summary.CompletionReason);
        Assert.False(summary.ClearFlag);
        Assert.Equal(500000, summary.Score);
        Assert.Equal(1, summary.MaxCombo);
        Assert.Equal(1, summary.PerfectCount);
        Assert.Equal(poorCount, summary.PoorCount);
        Assert.Equal(missCount, summary.MissCount);
        Assert.Equal(poorCount == 1 ? 50.5f : 49f, summary.FinalLife);
        Assert.Equal(preEventSkill, summary.PlayingSkill);
        Assert.Equal(SongScore.CalculateGameSkill(preEventSkill, 78, 33), summary.GameSkill);
        Assert.Equal(poorCount, rules.SkillManager.PoorCount);
        Assert.Equal(poorCount == 0 ? 1 : 0, rules.SkillManager.MissCount);
        Assert.False(rules.TryComplete());
        rules.ClearJudgement();
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();
    }

    [Fact]
    public void AutoPlayStallsTiesAndManualLanes_ShouldResolveOnlyDuePendingAutomatedNotes()
    {
        var config = new ConfigData();
        config.AutoPlayLanes.Add(1);
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("autoplay.dtx");
        var manual = new Note { LaneIndex = 0, TimeMs = 900 };
        var first = new Note { LaneIndex = 1, TimeMs = 1000 };
        var manualTie = new Note { LaneIndex = 0, TimeMs = 1000 };
        var simultaneous = new Note { LaneIndex = 1, TimeMs = 1000 };
        var later = new Note { LaneIndex = 1, TimeMs = 1100 };
        chart.Notes.AddRange(new[] { manual, first, manualTie, simultaneous, later });
        var rules = CreatePlayingRules(config, chart, input);
        config.AutoPlayLanes.Clear();
        config.AutoPlayLanes.Add(0);
        var order = new List<string>();
        rules.JudgementManager!.JudgementMade += (_, e) =>
            order.Add($"judgement:{e.NoteRef}:{e.Type}:{e.DeltaMs}");

        rules.ProcessAutoPlay(999, note => order.Add($"feedback:{note.Id}"));
        Assert.Empty(order);
        Assert.Equal(NoteStatus.Pending, rules.JudgementManager.GetNoteRuntimeData(first.Id)!.Status);
        rules.ProcessAutoPlay(1051, note => order.Add($"feedback:{note.Id}"));
        Assert.Equal(4, order.Count);
        Assert.Equal(NoteStatus.Pending, rules.JudgementManager.GetNoteRuntimeData(later.Id)!.Status);
        rules.ProcessAutoPlay(1600, note => order.Add($"feedback:{note.Id}"));
        input.TriggerLaneHit(0);
        rules.UpdateJudgement(1000);
        rules.ProcessAutoPlay(1600, note => order.Add($"feedback:{note.Id}"));

        Assert.Equal(new[]
        {
            $"judgement:{first.Id}:Perfect:0", $"feedback:{first.Id}",
            $"judgement:{simultaneous.Id}:Perfect:0", $"feedback:{simultaneous.Id}",
            $"judgement:{later.Id}:Perfect:0", $"feedback:{later.Id}",
            $"judgement:{manualTie.Id}:Perfect:0"
        }, order);
        Assert.Equal(NoteStatus.Pending, rules.JudgementManager.GetNoteRuntimeData(manual.Id)!.Status);
        Assert.Equal(NoteStatus.Hit, rules.JudgementManager.GetNoteRuntimeData(manualTie.Id)!.Status);
        rules.ClearJudgement();
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();
    }

    [Fact]
    public void AutoPlayAlreadyHitMissedAndInactiveNotes_ShouldKeepExistingFeedbackBehavior()
    {
        var config = new ConfigData();
        config.AutoPlayLanes.Add(1);
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("processed.dtx");
        var hit = new Note { LaneIndex = 1, TimeMs = 100 };
        var missed = new Note { LaneIndex = 1, TimeMs = 200 };
        var inactive = new Note { LaneIndex = 1, TimeMs = 1000 };
        chart.Notes.AddRange(new[] { hit, missed, inactive });
        var rules = CreatePlayingRules(config, chart, input);
        Assert.True(rules.JudgementManager!.ResolveAutoHit(hit.Id));
        rules.UpdateJudgement(500);
        Assert.Equal(NoteStatus.Missed, rules.JudgementManager.GetNoteRuntimeData(missed.Id)!.Status);
        rules.JudgementManager.IsActive = false;
        var feedback = new List<Note>();

        rules.ProcessAutoPlay(1000, feedback.Add);
        rules.JudgementManager.IsActive = true;
        rules.ProcessAutoPlay(1000, feedback.Add);

        Assert.Equal(new[] { inactive }, feedback);
        Assert.Equal(NoteStatus.Pending, rules.JudgementManager.GetNoteRuntimeData(inactive.Id)!.Status);
        rules.ClearJudgement();
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();
    }

    [Fact]
    public void AutoPlayWithoutAutomatedLanesOrManagers_ShouldLeaveNotesPending()
    {
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("manual.dtx");
        var note = new Note { LaneIndex = 0, TimeMs = 100 };
        chart.Notes.Add(note);
        var rules = new GameplayRules(null);
        var feedback = new List<Note>();
        rules.ProcessAutoPlay(100, feedback.Add);
        rules.BindChart(chart);
        rules.ProcessAutoPlay(100, feedback.Add);
        rules.InitializeJudgement(input);
        rules.JudgementManager!.IsActive = true;
        rules.ProcessAutoPlay(100, feedback.Add);
        Assert.Empty(feedback);
        Assert.Equal(NoteStatus.Pending, rules.JudgementManager.GetNoteRuntimeData(note.Id)!.Status);
        rules.ClearJudgement();
    }

    [Fact]
    public void AutoPlayReentrantCleanup_ShouldSkipFeedbackAndNotResolveFurtherNotes()
    {
        var config = new ConfigData();
        config.AutoPlayLanes.Add(1);
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("cleanup.dtx");
        var first = new Note { LaneIndex = 1, TimeMs = 100 };
        var second = new Note { LaneIndex = 1, TimeMs = 200 };
        chart.Notes.AddRange(new[] { first, second });
        var rules = CreatePlayingRules(config, chart, input);
        var order = new List<string>();
        rules.JudgementManager!.JudgementMade += (_, _) =>
        {
            order.Add("judgement");
            rules.ClearJudgement();
            rules.ClearScoreAndCombo();
            rules.ClearGaugeAndSkill();
            rules.ClearChart();
        };

        rules.ProcessAutoPlay(1000, note => order.Add($"feedback:{note.Id}"));

        Assert.Equal(new[] { "judgement" }, order);
        Assert.Null(rules.JudgementManager);
        Assert.Null(rules.ChartManager);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoPlayThrowingObserverOrFeedback_ShouldLeaveCursorToSkipResolvedNoteOnRetry(bool throwInFeedback)
    {
        var config = new ConfigData();
        config.AutoPlayLanes.Add(1);
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("callback-failure.dtx");
        var first = new Note { LaneIndex = 1, TimeMs = 100 };
        var second = new Note { LaneIndex = 1, TimeMs = 200 };
        chart.Notes.AddRange(new[] { first, second });
        var rules = CreatePlayingRules(config, chart, input);
        EventHandler<JudgementEvent> observer = (_, _) => throw new InvalidOperationException("observer");
        if (!throwInFeedback)
            rules.JudgementManager!.JudgementMade += observer;

        Assert.Throws<InvalidOperationException>(() => rules.ProcessAutoPlay(1000, _ =>
        {
            if (throwInFeedback)
                throw new InvalidOperationException("feedback");
        }));
        Assert.Equal(NoteStatus.Hit, rules.JudgementManager!.GetNoteRuntimeData(first.Id)!.Status);
        Assert.Equal(NoteStatus.Pending, rules.JudgementManager.GetNoteRuntimeData(second.Id)!.Status);
        rules.JudgementManager.JudgementMade -= observer;
        var feedback = new List<Note>();
        rules.ProcessAutoPlay(1000, feedback.Add);
        Assert.Equal(new[] { second }, feedback);
        Assert.Equal(NoteStatus.Hit, rules.JudgementManager.GetNoteRuntimeData(second.Id)!.Status);
        rules.ClearJudgement();
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();
    }

    [Fact]
    public void Judgement_ShouldUseCompensatedTimeForPendingHitsAndMissScanning()
    {
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("latency.dtx");
        var hit = new Note { LaneIndex = 0, TimeMs = 1000 };
        var missed = new Note { LaneIndex = 1, TimeMs = 1000 };
        chart.Notes.AddRange(new[] { hit, missed });
        var rules = CreatePlayingRules(null, chart, input);
        input.TriggerLaneHit(0);
        rules.UpdateJudgement(1000);
        Assert.Equal(JudgementType.Perfect, rules.JudgementManager!.GetNoteRuntimeData(hit.Id)!.JudgementEvent!.Type);
        Assert.Equal(NoteStatus.Pending, rules.JudgementManager.GetNoteRuntimeData(missed.Id)!.Status);
        rules.UpdateJudgement(1200);
        Assert.Equal(NoteStatus.Pending, rules.JudgementManager.GetNoteRuntimeData(missed.Id)!.Status);
        rules.UpdateJudgement(1201);
        Assert.Equal(NoteStatus.Missed, rules.JudgementManager.GetNoteRuntimeData(missed.Id)!.Status);
        rules.ClearJudgement();
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();
    }

    [Theory]
    [InlineData(50, 250, 1000, 875)]
    [InlineData(100, 250, 1000, 750)]
    [InlineData(150, 250, 1000, 625)]
    [InlineData(150, 250, 100, 0)]
    [InlineData(100, 0, 1000, 1000)]
    [InlineData(100, -1, -10, -10)]
    [InlineData(0, 250, 1000, 750)]
    public void PlayerJudgementTime_ShouldScaleLiveLatencyByPlaybackProfile(
        int speed, int latency, double raw, double expected)
    {
        Assert.Equal(expected, GameplayRules.GetPlayerJudgementTimeMs(raw, latency, new PlaybackModifiers(speed, 0)));
    }

    [Fact]
    public void ChipForHit_ShouldPlayJudgedNoteOrLaneNearestNoteForEmptyHit()
    {
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("chips.dtx");
        var early = new Note { LaneIndex = 3, TimeMs = 500 };
        var otherLane = new Note { LaneIndex = 4, TimeMs = 1000 };
        var hit = new Note { LaneIndex = 3, TimeMs = 1000 };
        var late = new Note { LaneIndex = 3, TimeMs = 1400 };
        chart.Notes.AddRange(new[] { early, otherLane, hit, late });
        var rules = CreatePlayingRules(null, chart, input);
        var resolved = new List<PlayerHitResolvedEventArgs>();
        rules.JudgementManager!.PlayerHitResolved += (_, e) => resolved.Add(e);

        input.TriggerLaneHit(3);
        rules.UpdateJudgement(1010);
        // Empty: the 1000 note is judged and the 1400 note is outside the Poor window.
        input.TriggerLaneHit(3);
        rules.UpdateJudgement(1020);
        input.TriggerLaneHit(5);
        rules.UpdateJudgement(1030);

        Assert.Equal(3, resolved.Count);
        Assert.Same(hit, resolved[0].JudgedNote);
        Assert.Same(hit, rules.FindChipNoteForHit(resolved[0]));
        Assert.Null(resolved[1].JudgedNote);
        Assert.Equal(1020, resolved[1].JudgementTimeMs);
        Assert.Same(hit, rules.FindChipNoteForHit(resolved[1]));
        Assert.Equal(5, resolved[2].Lane);
        Assert.Null(rules.FindChipNoteForHit(resolved[2]));
        Assert.Same(late, rules.FindChipNoteForHit(new PlayerHitResolvedEventArgs(3, null, 1250)));
        Assert.Same(otherLane, rules.FindChipNoteForHit(new PlayerHitResolvedEventArgs(4, null, 0)));

        rules.ClearJudgement();
        rules.ClearChart();
        Assert.Null(rules.FindChipNoteForHit(new PlayerHitResolvedEventArgs(3, null, 1000)));
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();
    }

    [Fact]
    public void SharedCymbalChord_ShouldGiveEachQueuedHitItsOwnNoteAndSound()
    {
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("cymbal-chord.dtx");
        var crash = new Note { LaneIndex = 8, TimeMs = 1000 };
        var ride = new Note { LaneIndex = 9, TimeMs = 1000 };
        chart.Notes.AddRange(new[] { crash, ride });
        var rules = CreatePlayingRules(new ConfigData { ShareCymbalLanes = true }, chart, input);
        var chips = new List<Note?>();
        rules.JudgementManager!.PlayerHitResolved += (_, e) => chips.Add(rules.FindChipNoteForHit(e));

        // Both hits wait in the queue until the frame's judgement update.
        input.TriggerLaneHit(8);
        input.TriggerLaneHit(9);
        rules.UpdateJudgement(1000);

        Assert.Equal(2, chips.Count);
        Assert.Contains(crash, chips);
        Assert.Contains(ride, chips);
        rules.ClearJudgement();
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();
    }

    [Theory]
    [InlineData(false, false, 52f)]
    [InlineData(true, false, 52f)]
    [InlineData(false, true, 50f)]
    [InlineData(true, true, 52f)]
    public void JudgementFanout_ShouldUseFrozenGaugePolicyForManualAndAutomatedLanes(
        bool autoAddGauge, bool automated, float expectedGauge)
    {
        var config = new ConfigData { AutoAddGauge = autoAddGauge };
        config.AutoPlayLanes.Add(1);
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("fanout.dtx");
        chart.Notes.Add(new Note { LaneIndex = automated ? 1 : 0, TimeMs = 1000 });
        var rules = CreatePlayingRules(config, chart, input);
        config.AutoAddGauge = !autoAddGauge;
        config.AutoPlayLanes.Clear();
        var order = new List<string>();
        rules.ScoreManager!.ScoreChanged += (_, _) => order.Add("score");
        rules.ComboManager!.ComboChanged += (_, _) => order.Add("combo");
        rules.GaugeManager!.GaugeChanged += (_, _) => order.Add("gauge");
        rules.SkillManager!.SkillChanged += (_, _) => order.Add("skill");
        rules.JudgementManager!.JudgementMade += (_, _) => order.Add("observer");

        if (automated)
            Assert.True(rules.JudgementManager.ResolveAutoHit(chart.Notes[0].Id));
        else
        {
            input.TriggerLaneHit(0);
            rules.JudgementManager.Update(1000);
        }

        Assert.Equal(1000000, rules.ScoreManager.CurrentScore);
        Assert.Equal(1, rules.ComboManager.CurrentCombo);
        Assert.Equal(expectedGauge, rules.GaugeManager.CurrentLife);
        Assert.Equal(1, rules.SkillManager.PerfectCount);
        Assert.Equal(SongScore.CalculatePlayingSkill(1, 1, 0, 1), rules.SkillManager.CurrentSkill);
        Assert.Equal(expectedGauge == 50f
            ? new[] { "score", "combo", "skill", "observer" }
            : new[] { "score", "combo", "gauge", "skill", "observer" }, order);
        rules.ClearJudgement();
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();
    }

    [Fact]
    public void JudgementFanoutAutomatedLaneGreat_ShouldSkipGaugeButUpdateOtherManagers()
    {
        var config = new ConfigData { AutoAddGauge = false };
        config.AutoPlayLanes.Add(1);
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("automated-great.dtx");
        chart.Notes.Add(new Note { LaneIndex = 1, TimeMs = 1000 });
        var rules = CreatePlayingRules(config, chart, input);

        rules.JudgementManager!.EnqueueLaneHit(1);
        rules.JudgementManager.Update(1040);

        Assert.Equal(900000, rules.ScoreManager!.CurrentScore);
        Assert.Equal(1, rules.ComboManager!.CurrentCombo);
        Assert.Equal(GaugeManager.StartingLife, rules.GaugeManager!.CurrentLife);
        Assert.Equal(1, rules.SkillManager!.GreatCount);
        rules.ClearJudgement();
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();
    }

    [Theory]
    [InlineData(1160)]
    [InlineData(840)]
    public void HitOutsidePoorWindow_ShouldNotJudgeScoreOrDamageAndLeaveNoteForLaterHit(int hitTime)
    {
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("empty-hit.dtx");
        chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
        var rules = CreatePlayingRules(new ConfigData { Risky = 1 }, chart, input);
        var judgements = new List<JudgementEvent>();
        rules.JudgementManager!.JudgementMade += (_, e) => judgements.Add(e);

        input.TriggerLaneHit(0);
        rules.JudgementManager.Update(hitTime);

        Assert.Empty(judgements);
        Assert.False(rules.GaugeManager!.HasFailed);
        Assert.Equal(0, rules.ScoreManager!.CurrentScore);
        Assert.Equal(NoteStatus.Pending, rules.JudgementManager.GetNoteRuntimeData(chart.Notes[0].Id)!.Status);

        if (hitTime < 1000)
        {
            input.TriggerLaneHit(0);
            rules.JudgementManager.Update(1000);
            Assert.Equal(JudgementType.Perfect, Assert.Single(judgements).Type);
        }

        rules.ClearJudgement();
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();
    }

    [Theory]
    [InlineData(true, 9, JudgementType.Perfect)]
    [InlineData(false, -1, null)]
    public void ShareCymbalLanes_ShouldLetCymbalHitJudgeRideNoteAndChipOnlyWhenShared(
        bool share, int expectedLane, JudgementType? expectedType)
    {
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("cymbal-group.dtx");
        var ride = new Note { LaneIndex = 9, TimeMs = 1000 };
        chart.Notes.Add(ride);
        var rules = CreatePlayingRules(new ConfigData { ShareCymbalLanes = share }, chart, input);
        var judgements = new List<JudgementEvent>();
        var resolved = new List<PlayerHitResolvedEventArgs>();
        rules.JudgementManager!.JudgementMade += (_, e) => judgements.Add(e);
        rules.JudgementManager.PlayerHitResolved += (_, e) => resolved.Add(e);

        Assert.Equal(share, rules.ShareCymbalLanes);
        input.TriggerLaneHit(8);
        rules.JudgementManager.Update(1000);
        Assert.Equal(share ? ride : null, Assert.Single(resolved).JudgedNote);

        if (expectedType is { } type)
        {
            var judgement = Assert.Single(judgements);
            Assert.Equal(type, judgement.Type);
            Assert.Equal(expectedLane, judgement.Lane);
        }
        else
        {
            Assert.Empty(judgements);
        }
        rules.ClearJudgement();
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();
    }

    [Fact]
    public void ShareCymbalLanes_ShouldPreferNearestNoteAndNeverTakeAutoplayOrOtherLanes()
    {
        var config = new ConfigData { ShareCymbalLanes = true };
        config.AutoPlayLanes.Add(9);
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("cymbal-group-autoplay.dtx");
        var autoRide = new Note { LaneIndex = 9, TimeMs = 1000 };
        var leftCymbal = new Note { LaneIndex = 0, TimeMs = 1000 };
        var crash = new Note { LaneIndex = 8, TimeMs = 1100 };
        chart.Notes.AddRange(new[] { autoRide, leftCymbal, crash });
        var rules = CreatePlayingRules(config, chart, input);

        input.TriggerLaneHit(8);
        rules.JudgementManager!.Update(1000);

        Assert.Equal(NoteStatus.Hit, rules.JudgementManager.GetNoteRuntimeData(crash.Id)!.Status);
        Assert.Equal(NoteStatus.Pending, rules.JudgementManager.GetNoteRuntimeData(autoRide.Id)!.Status);
        Assert.Equal(NoteStatus.Pending, rules.JudgementManager.GetNoteRuntimeData(leftCymbal.Id)!.Status);
        rules.ClearJudgement();
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();
    }

    [Fact]
    public void ClearJudgement_ShouldUnsubscribeRulesAndKeepOtherObserversAcrossRuns()
    {
        using var input = new MockInputManagerCompat();
        var chart = new ParsedChart("lifetime.dtx");
        chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
        var rules = CreatePlayingRules(null, chart, input);
        var oldManager = rules.JudgementManager!;
        EventHandler<JudgementEvent> observer = (_, _) => { };
        oldManager.JudgementMade += observer;
        var callbacks = ReflectionHelpers.GetPrivateField<EventHandler<JudgementEvent>>(oldManager, "JudgementMade");
        Assert.Contains(callbacks!.GetInvocationList(), callback => callback.Target == rules);

        rules.ClearJudgement();
        Assert.Equal(observer, ReflectionHelpers.GetPrivateField<EventHandler<JudgementEvent>>(oldManager, "JudgementMade"));
        rules.InitializeJudgement(input);
        rules.JudgementManager!.IsActive = true;
        input.TriggerLaneHit(0);
        rules.JudgementManager.Update(1000);
        Assert.Equal(1000000, rules.ScoreManager!.CurrentScore);
        Assert.Equal(1, rules.SkillManager!.PerfectCount);
        rules.ClearJudgement();
        rules.ClearScoreAndCombo();
        rules.ClearGaugeAndSkill();
    }

    private static GameplayRules CreatePlayingRules(ConfigData? config, ParsedChart chart, MockInputManagerCompat input)
    {
        var rules = new GameplayRules(config);
        rules.BindChart(chart);
        rules.InitializeJudgement(input);
        rules.InitializeScoreAndCombo();
        rules.InitializeGaugeAndSkill();
        rules.JudgementManager!.IsActive = true;
        return rules;
    }

    [Fact]
    public void InitializeGaugeAndSkill_ShouldUseFrozenPolicyBoundChartAndRulesCombo()
    {
        var config = new ConfigData { DamageLevel = GaugeDamageLevel.High, Risky = 2, NoFail = false };
        var rules = new GameplayRules(config);
        config.DamageLevel = GaugeDamageLevel.Low;
        config.Risky = 0;
        config.NoFail = true;
        var chart = new ParsedChart("gauge-skill.dtx");
        chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
        chart.Notes.Add(new Note { LaneIndex = 1, TimeMs = 2000 });
        rules.BindChart(chart);
        rules.InitializeScoreAndCombo();

        Assert.Null(rules.GaugeManager);
        Assert.Null(rules.SkillManager);
        rules.InitializeGaugeAndSkill();
        var gauge = Assert.IsType<GaugeManager>(rules.GaugeManager);
        var skill = Assert.IsType<SkillManager>(rules.SkillManager);
        Assert.Equal(GaugeManager.StartingLife, gauge.CurrentLife);
        Assert.Equal(2, skill.TotalNotes);

        var hit = new JudgementEvent(0, 0, 0, JudgementType.Perfect);
        rules.ComboManager!.ProcessJudgement(hit);
        skill.ProcessJudgement(hit);
        Assert.Equal(SongScore.CalculatePlayingSkill(2, 1, 0, 1), skill.CurrentSkill);
        var miss = new JudgementEvent(1, 0, 0, JudgementType.Miss);
        gauge.ProcessJudgement(miss);
        Assert.Equal(45.5f, gauge.CurrentLife);
        Assert.False(gauge.HasFailed);
        gauge.ProcessJudgement(miss);
        Assert.True(gauge.HasFailed);
        rules.ClearGaugeAndSkill();
        rules.ClearScoreAndCombo();
    }

    [Fact]
    public void ClearGaugeAndSkill_ShouldDisposeAndAllowFreshManagersForNextChart()
    {
        var rules = new GameplayRules(new ConfigData { NoFail = true, Risky = 1 });
        var first = new ParsedChart("first-gauge-skill.dtx");
        first.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
        rules.BindChart(first);
        rules.InitializeScoreAndCombo();
        rules.InitializeGaugeAndSkill();
        var oldGauge = Assert.IsType<GaugeManager>(rules.GaugeManager);
        var oldSkill = Assert.IsType<SkillManager>(rules.SkillManager);
        var miss = new JudgementEvent(0, 0, 0, JudgementType.Miss);
        oldGauge.ProcessJudgement(miss);
        Assert.False(oldGauge.HasFailed);

        rules.ClearGaugeAndSkill();
        Assert.Null(rules.GaugeManager);
        Assert.Null(rules.SkillManager);
        oldGauge.ProcessJudgement(miss);
        oldSkill.ProcessJudgement(miss);
        Assert.Equal(47f, oldGauge.CurrentLife);
        Assert.Equal(0, oldSkill.MissCount);
        rules.ClearGaugeAndSkill();
        rules.ClearScoreAndCombo();

        var second = new ParsedChart("second-gauge-skill.dtx");
        second.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
        second.Notes.Add(new Note { LaneIndex = 1, TimeMs = 2000 });
        rules.BindChart(second);
        rules.InitializeScoreAndCombo();
        rules.InitializeGaugeAndSkill();
        var newGauge = Assert.IsType<GaugeManager>(rules.GaugeManager);
        var newSkill = Assert.IsType<SkillManager>(rules.SkillManager);
        Assert.NotSame(oldGauge, newGauge);
        Assert.NotSame(oldSkill, newSkill);
        Assert.Equal(GaugeManager.StartingLife, newGauge.CurrentLife);
        Assert.Equal(2, newSkill.TotalNotes);
        rules.ClearGaugeAndSkill();
        rules.ClearScoreAndCombo();
    }

    [Fact]
    public void InitializeScoreAndCombo_ShouldUseBoundChartAndStartEmpty()
    {
        var rules = new GameplayRules(null);
        var chart = new ParsedChart("score.dtx");
        chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
        chart.Notes.Add(new Note { LaneIndex = 1, TimeMs = 2000 });
        rules.BindChart(chart);

        Assert.Null(rules.ScoreManager);
        Assert.Null(rules.ComboManager);
        rules.InitializeScoreAndCombo();
        var score = Assert.IsType<ScoreManager>(rules.ScoreManager);
        var combo = Assert.IsType<ComboManager>(rules.ComboManager);
        Assert.Equal(500000, score.BaseScore);
        Assert.Equal(0, score.CurrentScore);
        Assert.Equal(0, combo.CurrentCombo);
        Assert.Equal(0, combo.MaxCombo);

        var hit = new JudgementEvent(0, 0, 0, JudgementType.Perfect);
        score.ProcessJudgement(hit);
        combo.ProcessJudgement(hit);
        Assert.Equal(500000, score.CurrentScore);
        Assert.Equal(1, combo.MaxCombo);
        rules.ClearScoreAndCombo();
    }

    [Fact]
    public void ClearScoreAndCombo_ShouldDisposeAndAllowFreshManagersForNextChart()
    {
        var rules = new GameplayRules(null);
        var first = new ParsedChart("first-score.dtx");
        first.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
        rules.BindChart(first);
        rules.InitializeScoreAndCombo();
        var oldScore = Assert.IsType<ScoreManager>(rules.ScoreManager);
        var oldCombo = Assert.IsType<ComboManager>(rules.ComboManager);
        var hit = new JudgementEvent(0, 0, 0, JudgementType.Perfect);

        rules.ClearScoreAndCombo();
        Assert.Null(rules.ScoreManager);
        Assert.Null(rules.ComboManager);
        oldScore.ProcessJudgement(hit);
        oldCombo.ProcessJudgement(hit);
        Assert.Equal(0, oldScore.CurrentScore);
        Assert.Equal(0, oldCombo.CurrentCombo);
        rules.ClearScoreAndCombo();

        var second = new ParsedChart("second-score.dtx");
        second.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
        second.Notes.Add(new Note { LaneIndex = 1, TimeMs = 2000 });
        rules.BindChart(second);
        rules.InitializeScoreAndCombo();
        var newScore = Assert.IsType<ScoreManager>(rules.ScoreManager);
        var newCombo = Assert.IsType<ComboManager>(rules.ComboManager);
        Assert.NotSame(oldScore, newScore);
        Assert.NotSame(oldCombo, newCombo);
        newScore.ProcessJudgement(hit);
        newCombo.ProcessJudgement(hit);
        Assert.Equal(500000, newScore.CurrentScore);
        Assert.Equal(1, newCombo.CurrentCombo);
        rules.ClearScoreAndCombo();
    }

    [Fact]
    public void InitializeJudgement_ShouldUseBoundChartAndFrozenLanesAndStartInactive()
    {
        var config = new ConfigData();
        config.AutoPlayLanes.Add(1);
        var rules = new GameplayRules(config);
        config.AutoPlayLanes.Clear();
        var chart = new ParsedChart("judgement.dtx");
        chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
        chart.Notes.Add(new Note { LaneIndex = 1, TimeMs = 1000 });
        rules.BindChart(chart);
        using var input = new MockInputManagerCompat();

        rules.InitializeJudgement(input);
        var manager = Assert.IsType<JudgementManager>(rules.JudgementManager);
        Assert.False(manager.IsActive);
        input.TriggerLaneHit(0);
        manager.IsActive = true;
        input.TriggerLaneHit(1);
        manager.Update(1000);
        Assert.Equal(NoteStatus.Pending, manager.GetNoteRuntimeData(chart.Notes[0].Id)!.Status);
        Assert.Equal(NoteStatus.Pending, manager.GetNoteRuntimeData(chart.Notes[1].Id)!.Status);

        input.TriggerLaneHit(0);
        manager.Update(1000);
        Assert.Equal(NoteStatus.Hit, manager.GetNoteRuntimeData(chart.Notes[0].Id)!.Status);
        Assert.Equal(NoteStatus.Pending, manager.GetNoteRuntimeData(chart.Notes[1].Id)!.Status);
        Assert.True(manager.ResolveAutoHit(chart.Notes[1].Id));
        rules.ClearJudgement();
    }

    [Fact]
    public void ClearJudgement_ShouldDisposeAndAllowFreshManagerForNextChart()
    {
        var rules = new GameplayRules(null);
        var first = new ParsedChart("first.dtx");
        first.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
        rules.BindChart(first);
        using var input = new MockInputManagerCompat();
        rules.InitializeJudgement(input);
        var oldManager = Assert.IsType<JudgementManager>(rules.JudgementManager);
        oldManager.IsActive = true;

        rules.ClearJudgement();
        Assert.Null(rules.JudgementManager);
        Assert.False(oldManager.IsActive);
        Assert.Null(oldManager.GetNoteRuntimeData(first.Notes[0].Id));
        Assert.False(oldManager.ResolveAutoHit(first.Notes[0].Id));
        rules.ClearJudgement();

        var second = new ParsedChart("second.dtx");
        second.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
        rules.BindChart(second);
        rules.InitializeJudgement(input);
        var newManager = Assert.IsType<JudgementManager>(rules.JudgementManager);
        Assert.NotSame(oldManager, newManager);
        newManager.IsActive = true;
        input.TriggerLaneHit(0);
        newManager.Update(1000);
        Assert.Equal(NoteStatus.Hit, newManager.GetNoteRuntimeData(second.Notes[0].Id)!.Status);
        rules.ClearJudgement();
    }

    [Fact]
    public void BindChart_ShouldOwnSortedChartAndClearItWithoutChangingFrozenPolicy()
    {
        var config = new ConfigData();
        config.AutoPlayLanes.Add(2);
        var rules = new GameplayRules(config);
        var chart = new ParsedChart("rules-chart.dtx") { Bpm = 135, DurationMs = 4000 };
        var later = new Note { LaneIndex = 2, TimeMs = 2000 };
        var earlier = new Note { LaneIndex = 1, TimeMs = 1000 };
        chart.Notes.Add(later);
        chart.Notes.Add(earlier);
        chart.MeasureLines.Add(new MeasureLine { Bar = 1, TimeMs = 2000 });

        Assert.Null(rules.ChartManager);
        rules.BindChart(chart);

        var manager = Assert.IsType<ChartManager>(rules.ChartManager);
        Assert.Equal(2, manager.TotalNotes);
        Assert.Equal(135, manager.Bpm);
        Assert.Equal(4000, manager.DurationMs);
        Assert.Equal(new[] { earlier, later }, manager.AllNotes);
        Assert.Single(manager.AllMeasureLines);
        Assert.Equal(0, earlier.Id);
        Assert.Equal(1, later.Id);

        rules.ClearChart();
        Assert.Null(rules.ChartManager);
        Assert.True(rules.AutoPlayLanes.SetEquals(new[] { 2 }));
    }

    [Fact]
    public void BindChartWhenConstructionFails_ShouldKeepPreviouslyBoundChart()
    {
        var rules = new GameplayRules(null);
        rules.BindChart(new ParsedChart("first.dtx"));
        var original = rules.ChartManager;

        Assert.Throws<ArgumentNullException>(() => rules.BindChart(null!));
        Assert.Same(original, rules.ChartManager);
    }

    [Fact]
    public void Constructor_ShouldFreezeLanesAndGaugePolicyBeforeChartLoading()
    {
        var config = new ConfigData
        {
            AutoAddGauge = false,
            DamageLevel = GaugeDamageLevel.High,
            Risky = 3,
            NoFail = true
        };
        config.AutoPlayLanes.UnionWith(new[] { 1, 8 });

        var rules = new GameplayRules(config);
        config.AutoPlayLanes.Clear();
        config.AutoPlayLanes.Add(4);
        config.AutoAddGauge = true;
        config.DamageLevel = GaugeDamageLevel.Low;
        config.Risky = 0;
        config.NoFail = false;

        Assert.Equal(new[] { 1, 8 }, rules.AutoPlayLanes.OrderBy(lane => lane));
        Assert.False(rules.AutoPlayLanes.Contains(4));
        Assert.False(rules.AutoAddGauge);
        Assert.Equal(GaugeDamageLevel.High, rules.DamageLevel);
        Assert.Equal(3, rules.Risky);
        Assert.False(rules.FailureEnabled);
    }

    [Fact]
    public void ConstructorWithNoConfig_ShouldUseManualGameplayDefaults()
    {
        var rules = new GameplayRules(null);

        Assert.Empty(rules.AutoPlayLanes);
        Assert.True(rules.AutoAddGauge);
        Assert.Equal(GaugeDamageLevel.Normal, rules.DamageLevel);
        Assert.Equal(RiskyRange.Default, rules.Risky);
        Assert.True(rules.FailureEnabled);
    }

    [Theory]
    [InlineData(-1, RiskyRange.Min)]
    [InlineData(0, RiskyRange.Min)]
    [InlineData(3, 3)]
    [InlineData(11, RiskyRange.Max)]
    public void Constructor_ShouldClampRiskyToSupportedRange(int configured, int expected)
    {
        var rules = new GameplayRules(new ConfigData { Risky = configured });

        Assert.Equal(expected, rules.Risky);
    }

    [Fact]
    public void AutoPlayLanes_ShouldNotBeMutatedThroughCollectionInterfaces()
    {
        var config = new ConfigData();
        config.AutoPlayLanes.Add(2);
        var rules = new GameplayRules(config);

        Assert.False(rules.AutoPlayLanes is HashSet<int>);
        var collection = Assert.IsAssignableFrom<ICollection<int>>(rules.AutoPlayLanes);
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Add(7));
        Assert.True(rules.AutoPlayLanes.SetEquals(new[] { 2 }));
    }
}
