using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Stage.Performance;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class GameplayRulesTests
{
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
