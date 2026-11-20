using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Test.Helpers;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class GameplayRulesTests
{
    [Fact]
    public void InitializeGaugeAndSkill_UsesFrozenPolicyBoundChartAndRulesCombo()
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
    public void ClearGaugeAndSkill_DisposesAndAllowsFreshManagersForNextChart()
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
    public void InitializeScoreAndCombo_UsesBoundChartAndStartsEmpty()
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
    public void ClearScoreAndCombo_DisposesAndAllowsFreshManagersForNextChart()
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
    public void InitializeJudgement_UsesBoundChartAndFrozenLanesAndStartsInactive()
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
    public void ClearJudgement_DisposesAndAllowsFreshManagerForNextChart()
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
    public void BindChart_OwnsSortedChartAndClearsItWithoutChangingFrozenPolicy()
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
    public void BindChart_WhenConstructionFails_KeepsPreviouslyBoundChart()
    {
        var rules = new GameplayRules(null);
        rules.BindChart(new ParsedChart("first.dtx"));
        var original = rules.ChartManager;

        Assert.Throws<ArgumentNullException>(() => rules.BindChart(null!));
        Assert.Same(original, rules.ChartManager);
    }

    [Fact]
    public void Constructor_FreezesLanesAndGaugePolicyBeforeChartLoading()
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
    public void Constructor_WithNoConfig_UsesManualGameplayDefaults()
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
    public void Constructor_ClampsRiskyToSupportedRange(int configured, int expected)
    {
        var rules = new GameplayRules(new ConfigData { Risky = configured });

        Assert.Equal(expected, rules.Risky);
    }

    [Fact]
    public void AutoPlayLanes_CannotBeMutatedThroughCollectionInterfaces()
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
