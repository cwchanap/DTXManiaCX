using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DTXMania.Game;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Test.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class LanesPlayfieldStageIntegrationTests
{
    [Fact]
    public void LoadingInput_ShouldReachRealPlayfieldPadsExceptAutomatedLaneAndUnsubscribe()
    {
        var config = new ConfigData();
        config.AutoPlayLanes.Add(1);
        using var input = new MockInputManagerCompat();
        var resources = CreateResources();
        var visuals = new LanesPlayfieldConcreteTests.RecordingVisuals(resources.Object);
        using var stage = new HeadlessLanesStage(CreateGame(config, input, resources.Object), config, resources.Object, visuals);
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Loading" }, 0, new ParsedChart("loading.dtx")));

        input.TriggerLaneHit(1);
        input.TriggerLaneHit(3);

        Assert.Equal(new[] { (3, false) }, visuals.Pads.Presses);
        Assert.Equal(1, PerformanceStageDeterministicTests.CountPadFeedbackSubscribers(stage, input));
        stage.Deactivate();
        input.TriggerLaneHit(4);
        Assert.Equal(0, PerformanceStageDeterministicTests.CountPadFeedbackSubscribers(stage, input));
        Assert.Equal(new[] { (3, false) }, visuals.Pads.Presses);
    }

    [Fact]
    public void ChartPublicationAndLiveSpeed_ShouldReachConcreteLanesNoteRenderer()
    {
        var config = new ConfigData { ScrollSpeed = 100 };
        using var input = new MockInputManagerCompat();
        var resources = CreateResources();
        var visuals = new LanesPlayfieldConcreteTests.RecordingVisuals(resources.Object);
        var game = CreateGame(config, input, resources.Object);
        using var stage = new HeadlessLanesStage(game, config, resources.Object, visuals);
        var chart = new ParsedChart("published-speed.dtx") { Bpm = 150, DurationMs = 10000 };
        chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 1000 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Speed" }, 0, chart));
        config.ScrollSpeed = 200;
        UpdateUntil(stage, () => visuals.Notes.Bpms.Count != 0);
        Assert.Equal(new[] { 150.0 }, visuals.Notes.Bpms);
        Assert.Equal(new[] { 200 }, visuals.Notes.Speeds);

        Mock.Get(game.ConfigManager).Raise(
            manager => manager.ScrollSpeedChanged += null, new ScrollSpeedChangedEventArgs(200, 125));

        Assert.Equal(new[] { 200, 125 }, visuals.Notes.Speeds);
        stage.Deactivate();
        Mock.Get(game.ConfigManager).Raise(
            manager => manager.ScrollSpeedChanged += null, new ScrollSpeedChangedEventArgs(125, 150));
        Assert.Equal(new[] { 200, 125 }, visuals.Notes.Speeds);
    }

    [Fact]
    public void Judgements_ShouldReachRealPlayfieldAfterRulesWithHitMissAndFlush()
    {
        var config = new ConfigData { EnableLaneFlush = true, NoFail = true };
        using var input = new MockInputManagerCompat();
        var resources = CreateResources();
        var visuals = new LanesPlayfieldConcreteTests.RecordingVisuals(resources.Object);
        using var stage = new HeadlessLanesStage(CreateGame(config, input, resources.Object), config, resources.Object, visuals);
        var chart = new ParsedChart("judgements.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { Id = 0, LaneIndex = 2, TimeMs = 1000 });
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 4, TimeMs = 2000 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Hit and miss" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);

        var judgement = stage.Rules!.JudgementManager!;
        judgement.IsActive = true;
        judgement.EnqueueLaneHit(2, "manual");
        judgement.Update(1040);
        judgement.Update(2201);

        Assert.Equal(new[] { (2, true) }, visuals.Pads.Presses);
        Assert.Equal(new[] { 2 }, visuals.Notes.Flashes);
        Assert.Equal(new[] { 1, 0 }, visuals.Combo.Values);
        Assert.Equal(2, visuals.FontPopups.ActivePopupCount);
#if DEBUG
        Assert.Equal(1, visuals.Effects.SpawnCallCountForTesting);
#endif
    }

    [Fact]
    public void JudgementAndAutoplay_ShouldOrderRulesConcreteFeedbackAndChipPlayback()
    {
        var config = new ConfigData { EnableLaneFlush = true, ShowHitTimingFeedback = true };
        config.AutoPlayLanes.Add(0);
        using var input = new MockInputManagerCompat();
        var resources = CreateResources();
        var timingTexture = new Mock<ITexture>();
        var spriteTexture = new Mock<ITexture>();
        var visuals = new LanesPlayfieldConcreteTests.RecordingVisuals(
            resources.Object, timingTexture.Object, spriteTexture.Object);
        using var stage = new HeadlessLanesStage(CreateGame(config, input, resources.Object), config, resources.Object, visuals);
        var chart = new ParsedChart("real-visual-hit.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { LaneIndex = 2, TimeMs = 1000 });
        for (int i = 0; i < 9; i++)
            chart.Notes.Add(new Note { LaneIndex = i == 0 ? 3 : 0, TimeMs = 3000 + i * 100 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Hit" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);

        var rules = stage.Rules!;
        var score = rules.ScoreManager!;
        var combo = rules.ComboManager!;
        var gauge = rules.GaugeManager!;
        var skill = rules.SkillManager!;
        var judgement = rules.JudgementManager!;
        var order = new List<string>();
        score.ScoreChanged += (_, _) => order.Add("score");
        combo.ComboChanged += (_, _) => order.Add("combo");
        gauge.GaugeChanged += (_, _) => order.Add("gauge");
        skill.SkillChanged += (_, _) =>
        {
            if (order.Count >= 4) return;
            order.Add("skill");
            Assert.Equal(0, rules.BuildFrameState(1040).SlowCount);
            Assert.Equal(0, visuals.SpritePopups!.ActivePopupCount);
        };
        visuals.Score.OnSet = _ =>
        {
            if (order.Count != 4) return;
            order.Add("visual");
            Assert.Equal(1, rules.BuildFrameState(1040).SlowCount);
            Assert.Equal(0, visuals.SpritePopups!.ActivePopupCount);
        };
        judgement.IsActive = true;
        judgement.EnqueueLaneHit(2, "manual");
        judgement.Update(1040);

        Assert.Equal(new[] { "score", "combo", "gauge", "skill", "visual" }, order);
        Assert.Equal(score.CalculateScoreForJudgement(JudgementType.Great), score.CurrentScore);
        Assert.Equal(1, combo.CurrentCombo);
        Assert.Equal(51.5f, gauge.CurrentLife);
        Assert.Equal(new[] { (2, true) }, visuals.Pads.Presses);
        Assert.Equal(new[] { 2 }, visuals.Notes.Flashes);
        Assert.Equal(JudgementType.Great, Assert.Single(visuals.SpritePopups!.ActivePopupsForTesting).JudgementType);
        Assert.NotNull(visuals.Timing.ActiveStatesForTesting[2]);
#if DEBUG
        Assert.Equal(1, visuals.Effects.SpawnCallCountForTesting);
        Assert.Equal(2, visuals.Effects.LastSpawnLaneForTesting);
        Assert.Equal(JudgementType.Great, visuals.Effects.LastSpawnJudgementTypeForTesting);
#endif
        Assert.Equal(1, rules.BuildFrameState(1040).GreatCount);
        Assert.Equal(1, rules.BuildFrameState(1040).SlowCount);
        Assert.Equal(1, rules.BuildFrameState(1040).MaxCombo);

        judgement.EnqueueLaneHit(3, "manual");
        judgement.Update(3160, 1040);
        Assert.Equal(1, rules.BuildFrameState(3160).MissCount);
        Assert.Equal(1, skill.MissCount);
        Assert.Equal(0, judgement.GetJudgementCount(JudgementType.Miss));
        Assert.Equal(NoteStatus.Hit, judgement.GetNoteRuntimeData(chart.Notes[1].Id)!.Status);
        Assert.Equal(JudgementType.Miss, visuals.SpritePopups.ActivePopupsForTesting.Last().JudgementType);
        Assert.Equal(new[] { 2 }, visuals.Notes.Flashes);
        Assert.Equal(new[] { (2, true) }, visuals.Pads.Presses);

        chart.Notes[2].Value = "07";
        var chip = new Mock<ISound>();
        bool feedbackWhenPlayed = false;
        chip.Setup(s => s.Play()).Callback(() => feedbackWhenPlayed = visuals.Pads.Presses.Contains((0, true)));
        var prepared = PerformanceStageDeterministicTests.CreatePreparedSetWithChips(
            new Dictionary<string, ISound> { ["07"] = chip.Object });
        var session = stage.AudioSession!;
        session.Dispose();
        session.Bind(chart, prepared);
        stage.Update(1.1);
        stage.Update(3.1);
        Assert.Contains((0, true), visuals.Pads.Presses);
        Assert.True(feedbackWhenPlayed);
        chip.Verify(s => s.Play(), Times.Once);
        Assert.Equal(NoteStatus.Hit, judgement.GetNoteRuntimeData(chart.Notes[2].Id)!.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Miss_ShouldDrawSpriteWithoutHitFeedbackOrScore(bool manualMiss)
    {
        var config = new ConfigData { EnableLaneFlush = true, NoFail = true };
        using var input = new MockInputManagerCompat();
        var resources = CreateResources();
        var visuals = new LanesPlayfieldConcreteTests.RecordingVisuals(
            resources.Object, new Mock<ITexture>().Object, new Mock<ITexture>().Object);
        using var stage = new HeadlessLanesStage(CreateGame(config, input, resources.Object), config, resources.Object, visuals);
        var chart = new ParsedChart("miss.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { LaneIndex = 4, TimeMs = 1000 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Miss" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var judgement = stage.Rules!.JudgementManager!;
        judgement.IsActive = true;
        if (manualMiss) judgement.EnqueueLaneHit(4, "manual");
        judgement.Update(manualMiss ? 1160 : 1201);

        Assert.Equal(0, stage.Rules.ScoreManager!.CurrentScore);
        Assert.Equal(0, stage.Rules.ComboManager!.CurrentCombo);
        Assert.Equal(47f, stage.Rules.GaugeManager!.CurrentLife);
        Assert.Equal(JudgementType.Miss, Assert.Single(visuals.SpritePopups!.ActivePopupsForTesting).JudgementType);
        Assert.Equal(0, visuals.FontPopups.ActivePopupCount);
        Assert.Empty(visuals.Pads.Presses);
        Assert.Empty(visuals.Notes.Flashes);
        Assert.Equal(0, visuals.Timing.ActiveLaneCountForTesting);
#if DEBUG
        Assert.Equal(0, visuals.Effects.SpawnCallCountForTesting);
        Assert.Null(visuals.Effects.LastSpawnLaneForTesting);
        Assert.Null(visuals.Effects.LastSpawnJudgementTypeForTesting);
#endif
    }

    [Fact]
    public void StageUpdate_WhenSongPlays_ShouldAdvanceRealLanesTimingFeedback()
    {
        var config = new ConfigData { ShowHitTimingFeedback = true };
        using var input = new MockInputManagerCompat();
        var resources = CreateResources();
        var visuals = new LanesPlayfieldConcreteTests.RecordingVisuals(
            resources.Object, new Mock<ITexture>().Object, new Mock<ITexture>().Object);
        using var stage = new HeadlessLanesStage(CreateGame(config, input, resources.Object), config, resources.Object, visuals);
        var chart = new ParsedChart("timing-update.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { LaneIndex = 2, TimeMs = 1000 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Timing" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Update(1.1);
        var manager = stage.Rules!.JudgementManager!;
        manager.IsActive = true;
        manager.EnqueueLaneHit(2, "manual");
        manager.Update(1040);
        Assert.Equal(0, visuals.Timing.ActiveStatesForTesting[2]!.ElapsedSeconds);

        stage.Update(0.016);

        var state = visuals.Timing.ActiveStatesForTesting[2];
        Assert.NotNull(state);
        Assert.True(state!.ElapsedSeconds > 0);
        Assert.True(state.Alpha < 1f);
    }

    [Fact]
    public void Deactivate_ShouldDisposeConcreteLanesBorrowersBeforeStagePixel()
    {
        var config = new ConfigData();
        using var input = new MockInputManagerCompat();
        var resources = CreateResources();
        var visuals = new LanesPlayfieldConcreteTests.RecordingVisuals(resources.Object);
        var pixel = new Mock<ITexture>();
        pixel.Setup(p => p.RemoveReference()).Callback(() =>
            Assert.Equal(new[] { "line", "score", "combo", "panel", "meter", "notes", "pads" }, visuals.Disposals));
        using var stage = new HeadlessLanesStage(CreateGame(config, input, resources.Object), config, resources.Object, visuals)
        {
            Pixel = pixel.Object
        };
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Lifetime" }, 0, new ParsedChart("lifetime.dtx")));

        stage.Deactivate();

        pixel.Verify(p => p.RemoveReference(), Times.Once);
    }

    [Fact]
    public void Deactivate_WhenLanesChildAndPixelReleaseThrow_ShouldFinishTeardownAndPreserveFirstFailure()
    {
        var config = new ConfigData();
        using var input = new MockInputManagerCompat();
        var resources = CreateResources();
        var visuals = new LanesPlayfieldConcreteTests.RecordingVisuals(resources.Object) { ThrowOnScoreDispose = true };
        var pixel = new Mock<ITexture>();
        pixel.Setup(p => p.RemoveReference()).Callback(() =>
        {
            Assert.Equal(new[] { "line", "score", "combo", "panel", "meter", "notes", "pads" }, visuals.Disposals);
            throw new InvalidOperationException("pixel release failed");
        });
        using var stage = new HeadlessLanesStage(CreateGame(config, input, resources.Object), config, resources.Object, visuals)
        {
            Pixel = pixel.Object
        };
        var chart = new ParsedChart("cleanup-error.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { LaneIndex = 2, TimeMs = 1000 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Cleanup failure" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var session = stage.AudioSession!;
        var rules = stage.Rules!;

        var failure = Assert.Throws<InvalidOperationException>(() => stage.Deactivate());

        Assert.Equal("score dispose failed", failure.Message);
        pixel.Verify(p => p.RemoveReference(), Times.Once);
        Assert.Null(stage.AudioSession);
        Assert.Null(rules.ChartManager);
        Assert.Null(session.Timer);
        input.TriggerLaneHit(4);
        Assert.Empty(visuals.Pads.Presses);
        stage.Deactivate();
        pixel.Verify(p => p.RemoveReference(), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TabWithMissingOrUnreadyHud_ShouldKeepCompleteLanesAndFeedback(bool createUnreadyHud)
    {
        var config = new ConfigData
        {
            NoteDisplayStyle = DrumsNoteDisplayStyle.Tab,
            ShowHitTimingFeedback = true,
            EnableLaneFlush = true
        };
        using var input = new MockInputManagerCompat();
        var resources = CreateResources();
        var font = new Mock<IFont>();
        font.Setup(f => f.SupportsCharacter(It.IsAny<char>())).Returns(true);
        font.SetupGet(f => f.LineSpacing).Returns(74f);
        resources.Setup(r => r.LoadFont(It.IsAny<string>(), It.IsAny<int>())).Returns(font.Object);
        var timingTexture = new Mock<ITexture>();
        var spriteTexture = new Mock<ITexture>();
        var visuals = new LanesPlayfieldConcreteTests.RecordingVisuals(resources.Object, timingTexture.Object, spriteTexture.Object);
        var hud = createUnreadyHud ? TabHudDisplay.CreateHeadlessForTesting() : null;
        using var stage = new HeadlessLanesStage(CreateGame(config, input, resources.Object), config, resources.Object, visuals)
        {
            TabHud = hud
        };
        var chart = new ParsedChart("tab-no-hud.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
        chart.FinalizeChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Fallback" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);

        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        input.TriggerLaneHit(3);
        stage.DrawPlayfieldBase(null!);
        stage.DrawPlayfieldHud(null!);

        Assert.Equal(nameof(DrumsNoteDisplayStyle.Lanes), telemetry.NoteDisplayStyle);
        Assert.Equal(new[] { (3, false) }, visuals.Pads.Presses);
        Assert.Equal(1, visuals.Pads.DrawCount);
        Assert.Equal(1, visuals.Score.DrawCount);
        Assert.Empty(visuals.Disposals);
        var judgement = stage.Rules!.JudgementManager!;
        judgement.IsActive = true;
        judgement.EnqueueLaneHit(0, "manual");
        judgement.Update(chart.Notes[0].TimeMs - 12);
        Assert.Equal(JudgementType.Perfect, Assert.Single(visuals.SpritePopups!.ActivePopupsForTesting).JudgementType);
        Assert.Equal(0, visuals.FontPopups.ActivePopupCount);
        Assert.Equal("-12", visuals.Timing.ActiveStatesForTesting[0]!.Projection.Text);
        Assert.Equal(new[] { 0 }, visuals.Notes.Flashes);
        Assert.Null(hud?.PopupForTesting);
    }

    [Fact]
    public void TabWithReadyHud_ShouldPublishAtomicallyAndRouteHitAndMissToRealHud()
    {
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab, ShowHitTimingFeedback = true, NoFail = true };
        using var input = new MockInputManagerCompat();
        var resources = CreateResources();
        var font = new Mock<IFont>();
        font.Setup(f => f.SupportsCharacter(It.IsAny<char>())).Returns(true);
        font.SetupGet(f => f.LineSpacing).Returns(74f);
        resources.Setup(r => r.LoadFont(It.IsAny<string>(), It.IsAny<int>())).Returns(font.Object);
        var visuals = new LanesPlayfieldConcreteTests.RecordingVisuals(resources.Object);
        var hud = TabHudDisplay.CreateHeadlessForTesting();
        hud.MarkResourcesReadyForTesting();
        using var stage = new HeadlessLanesStage(CreateGame(config, input, resources.Object), config, resources.Object, visuals)
        {
            TabHud = hud
        };
        var chart = new ParsedChart("tab-ready.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
        chart.AddNote(new Note(5, 1, 0, 0x13, "01"));
        chart.FinalizeChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Tab" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(nameof(DrumsNoteDisplayStyle.Tab), telemetry.NoteDisplayStyle);
        Assert.Empty(visuals.Disposals);

        var judgement = stage.Rules!.JudgementManager!;
        judgement.IsActive = true;
        judgement.EnqueueLaneHit(0, "manual");
        judgement.Update(chart.Notes[0].TimeMs - 12);
        Assert.NotNull(hud.PopupForTesting);
        Assert.Equal("PERFECT", hud.PopupForTesting!.Text);
        Assert.Equal("-12", hud.PopupTimingForTesting.Text);
        Assert.False(hud.PopupTimingForTesting.IsSlow);
        Assert.Equal(1f, hud.FlashTimersForTesting[0]);
        Assert.Equal(PerformanceUILayout.GetLaneColor(0), hud.FlashColorsForTesting[0]);
        Assert.Equal(0, visuals.FontPopups.ActivePopupCount);
        Assert.Equal(0, visuals.SpritePopups!.ActivePopupCount);
        Assert.Equal(0, visuals.Timing.ActiveLaneCountForTesting);

        judgement.Update(chart.Notes[1].TimeMs + 201);
        Assert.Equal("MISS", hud.PopupForTesting!.Text);
        Assert.Null(hud.PopupTimingForTesting.Text);
        Assert.Equal(1f, hud.FlashTimersForTesting[5]);
        Assert.Equal(hud.Palette.Danger, hud.FlashColorsForTesting[5]);
    }

    [Fact]
    public void DeactivateFromTab_ShouldClearStripBeforeDisposalAndReleaseHudBeforePixel()
    {
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab };
        using var input = new MockInputManagerCompat();
        var resources = CreateResources();
        var font = new Mock<IFont>();
        font.Setup(f => f.SupportsCharacter(It.IsAny<char>())).Returns(true);
        font.SetupGet(f => f.LineSpacing).Returns(74f);
        var hud = TabHudDisplay.CreateHeadlessForTesting();
        hud.MarkResourcesReadyForTesting();
        bool deactivating = false;
        int stripReleaseChecks = 0;
        font.Setup(f => f.RemoveReference()).Callback(() =>
        {
            if (!deactivating || !hud.IsReady) return;
            input.TriggerLaneHit(8);
            Assert.Equal(0, hud.FlashTimersForTesting[8]);
            stripReleaseChecks++;
        });
        resources.Setup(r => r.LoadFont("Bravura-Notation", It.IsAny<int>())).Returns(font.Object);
        resources.Setup(r => r.LoadFont("ShareTechMono", It.IsAny<int>())).Returns(font.Object);
        var visuals = new LanesPlayfieldConcreteTests.RecordingVisuals(resources.Object);
        var pixel = new Mock<ITexture>();
        pixel.Setup(p => p.RemoveReference()).Callback(() =>
        {
            Assert.False(hud.IsReady);
            Assert.Equal(new[] { "line", "score", "combo", "panel", "meter", "notes", "pads" }, visuals.Disposals);
        });
        using var stage = new HeadlessLanesStage(CreateGame(config, input, resources.Object), config, resources.Object, visuals)
        {
            TabHud = hud,
            Pixel = pixel.Object
        };
        var chart = new ParsedChart("tab-cleanup.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
        chart.FinalizeChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Tab" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);
        input.TriggerLaneHit(2);
        Assert.Equal(1f, hud.FlashTimersForTesting[2]);

        deactivating = true;
        stage.Deactivate();

        Assert.Equal(2, stripReleaseChecks);
        pixel.Verify(p => p.RemoveReference(), Times.Once);
        Assert.False(hud.IsReady);
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(nameof(DrumsNoteDisplayStyle.Lanes), telemetry.NoteDisplayStyle);
    }

    [Fact]
    public void UiPass_ShouldDrawRealLanesHudAndUiManager()
    {
        var config = new ConfigData();
        using var input = new MockInputManagerCompat();
        var resources = CreateResources();
        var panel = Texture(resources, TexturePath.SkillPanel, 180, 96);
        var gaugeBase = Texture(resources, TexturePath.GaugeFrame, 120, 24);
        var gaugeFill = Texture(resources, TexturePath.GaugeFill, 200, 8);
        var progressBase = Texture(resources, TexturePath.ProgressFrame, 60, 540);
        var visuals = new LanesPlayfieldConcreteTests.RecordingVisuals(resources.Object);
        using var stage = new HeadlessLanesStage(CreateGame(config, input, resources.Object), config, resources.Object, visuals);
        using var ui = new UIManager();
        var container = new RecordingContainer(() => Assert.Equal(1, visuals.Score.DrawCount));
        ui.AddRootContainer(container);
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "UI" }, 0, new ParsedChart("ui.dtx")));

        stage.DrawUiPass(ui);

        VerifyDraw(panel, 0.2f);
        VerifyDraw(gaugeBase, 0.19f);
        VerifyDraw(gaugeFill, 0.18f);
        VerifyDraw(progressBase, 0.2f);
        Assert.Equal(1, container.DrawCount);
    }

    [Fact]
    public void UiPass_ShouldDrawStageShutterAndDangerWithConcreteLanes()
    {
        var config = new ConfigData { NoFail = true };
        using var input = new MockInputManagerCompat();
        var resources = CreateResources();
        var shutter = Texture(resources, TexturePath.Shutter, 96, 512);
        var danger = Texture(resources, TexturePath.Danger, 1280, 720);
        var visuals = new LanesPlayfieldConcreteTests.RecordingVisuals(resources.Object);
        using var stage = new HeadlessLanesStage(CreateGame(config, input, resources.Object), config, resources.Object, visuals);
        var chart = new ParsedChart("stage-overlay.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { LaneIndex = 0, TimeMs = 9000 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Overlays" }, 0, chart));
        stage.LoadPerformanceUIAssets();

        stage.DrawUiPass(null);
        VerifyDraw(shutter, 0.2f);
        Assert.Equal(1, visuals.Score.DrawCount);

        UpdateUntil(stage, () => stage.AudioSession != null);
        stage.Rules!.GaugeManager!.Reset(10);
        stage.DrawUiPass(null);
        VerifyDraw(danger, 0.05f);
        Assert.Equal(2, visuals.Score.DrawCount);
    }

    [Fact]
    public void StageAssets_ShouldReleaseEveryLoadedOverlayOnDeactivate()
    {
        var config = new ConfigData();
        using var input = new MockInputManagerCompat();
        var resources = CreateResources();
        var background = Texture(resources, TexturePath.PerformanceBackgroundTexture, 1280, 720);
        var shutter = Texture(resources, TexturePath.Shutter, 96, 512);
        var danger = Texture(resources, TexturePath.Danger, 1280, 720);
        var visuals = new LanesPlayfieldConcreteTests.RecordingVisuals(resources.Object);
        using var stage = new HeadlessLanesStage(CreateGame(config, input, resources.Object), config, resources.Object, visuals);
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Assets" }, 0, new ParsedChart("assets.dtx")));

        stage.LoadPerformanceUIAssets();
        stage.Deactivate();

        background.Verify(t => t.RemoveReference(), Times.Exactly(2)); // BaseStage and performance assets each acquire it.
        foreach (var texture in new[] { shutter, danger })
            texture.Verify(t => t.RemoveReference(), Times.Once);
    }

    [Fact]
    public void StageAssets_WhenShutterReleaseThrows_ShouldReleaseDangerAndPixelOnce()
    {
        var config = new ConfigData();
        using var input = new MockInputManagerCompat();
        var resources = CreateResources();
        var shutter = Texture(resources, TexturePath.Shutter, 96, 512);
        var danger = Texture(resources, TexturePath.Danger, 1280, 720);
        var pixel = new Mock<ITexture>();
        var failure = new InvalidOperationException("shutter release failed");
        shutter.Setup(t => t.RemoveReference()).Throws(failure);
        var visuals = new LanesPlayfieldConcreteTests.RecordingVisuals(resources.Object);
        using var stage = new HeadlessLanesStage(CreateGame(config, input, resources.Object), config, resources.Object, visuals)
        {
            Pixel = pixel.Object
        };
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Assets" }, 0, new ParsedChart("assets.dtx")));
        stage.LoadPerformanceUIAssets();

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => stage.Deactivate()));
        danger.Verify(t => t.RemoveReference(), Times.Once);
        pixel.Verify(t => t.RemoveReference(), Times.Once);
        stage.Deactivate();
        shutter.Verify(t => t.RemoveReference(), Times.Once);
        danger.Verify(t => t.RemoveReference(), Times.Once);
        pixel.Verify(t => t.RemoveReference(), Times.Once);
    }

    [Fact]
    public void PassBodies_ShouldForwardLiveAndPausedSongClockToRealPlayfield()
    {
        var config = new ConfigData { PlaySpeedPercent = 150 };
        using var input = new MockInputManagerCompat();
        var resources = CreateResources();
        var visuals = new LanesPlayfieldConcreteTests.RecordingVisuals(resources.Object);
        using var stage = new HeadlessLanesStage(CreateGame(config, input, resources.Object), config, resources.Object, visuals);
        var chart = new ParsedChart("clock.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { Id = 0, LaneIndex = 0, TimeMs = 1500 });
        chart.MeasureLines.Add(new MeasureLine { TimeMs = 1500 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Clock" }, 0, chart));
        UpdateUntil(stage, () => stage.AudioSession != null);

        stage.DrawPlayfieldBase(null!);
        stage.DrawPlayfieldOverlay(null!);
        Assert.Empty(visuals.Notes.NoteDraws);
        Assert.Empty(visuals.Notes.OverlayDraws);
        stage.Update(1.0);
        stage.Update(1.0);
        stage.DrawPlayfieldBase(null!);
        stage.DrawPlayfieldOverlay(null!);
        Assert.Equal(1500, Assert.Single(visuals.Notes.NoteDraws).Time);
        Assert.Equal(1500, Assert.Single(visuals.Notes.MeasureDraws).Time);
        Assert.Equal(1500, Assert.Single(visuals.Notes.OverlayDraws).Time);

        stage.AudioSession!.Timer!.Pause(new GameTime(TimeSpan.FromSeconds(2), TimeSpan.Zero));
        stage.Update(3);
        stage.DrawPlayfieldBase(null!);
        stage.DrawPlayfieldOverlay(null!);
        Assert.Equal(1500, visuals.Notes.NoteDraws[1].Time);
        Assert.Equal(1500, visuals.Notes.MeasureDraws[1].Time);
        Assert.Equal(1500, visuals.Notes.OverlayDraws[1].Time);
        stage.DrawPlayfieldHud(null!);
        stage.DrawPlayfieldAdditive(null!);
        Assert.Equal(1, visuals.Score.DrawCount);
    }

    private static Mock<IResourceManager> CreateResources()
    {
        var pixel = new Mock<ITexture>();
        var resources = new Mock<IResourceManager>();
        resources.Setup(r => r.CreateTextureFromColor(Color.White)).Returns(pixel.Object);
        return resources;
    }

    private static Mock<ITexture> Texture(Mock<IResourceManager> resources, string path, int width, int height)
    {
        var texture = new Mock<ITexture>();
        texture.SetupGet(t => t.Width).Returns(width);
        texture.SetupGet(t => t.Height).Returns(height);
        var value = texture.Object;
        resources.Setup(r => r.TryLoadTexture(path, out value)).Returns(true);
        return texture;
    }

    private static void VerifyDraw(Mock<ITexture> texture, float depth) =>
        texture.Verify(t => t.Draw(It.IsAny<SpriteBatch>(), It.IsAny<Rectangle>(), It.IsAny<Rectangle?>(),
            It.IsAny<Color>(), It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(), depth), Times.Once);

    private sealed class RecordingContainer(Action onDraw) : UIContainer
    {
        public int DrawCount;
        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            onDraw();
            DrawCount++;
        }
    }

    private static IStageGame CreateGame(ConfigData config, MockInputManagerCompat input, IResourceManager resources)
    {
        var configManager = new Mock<IConfigManager>();
        configManager.SetupGet(x => x.Config).Returns(config);
        var game = new Mock<IStageGame>();
        game.SetupGet(x => x.ConfigManager).Returns(configManager.Object);
        game.SetupGet(x => x.InputManager).Returns(input);
        game.SetupGet(x => x.ResourceManager).Returns(resources);
        game.SetupGet(x => x.LoggerFactory).Returns(NullLoggerFactory.Instance);
        return game.Object;
    }

    private static void UpdateUntil(PerformanceStage stage, Func<bool> condition) =>
        Assert.True(SpinWait.SpinUntil(() => { stage.Update(0); return condition(); }, TimeSpan.FromSeconds(5)));

    private sealed class HeadlessLanesStage : PerformanceStage
    {
        private readonly ConfigData _config;
        private readonly IResourceManager _resources;
        private readonly ILanesVisualFactory _visuals;

        public HeadlessLanesStage(IStageGame game, ConfigData config, IResourceManager resources, ILanesVisualFactory visuals)
            : base(game) => (_config, _resources, _visuals) = (config, resources, visuals);

        public ITexture? Pixel { get; init; }
        public TabHudDisplay? TabHud { get; init; }
        protected override SpriteBatch? CreateSpriteBatch(GraphicsDevice graphicsDevice) => null;
        protected override ITexture? CreateFallbackWhiteTexture() => Pixel;
        internal override TabHudDisplay? CreateTabHud(SkinPalette palette, TabModeArt art) => TabHud;
        protected override IPerformancePlayfield CreateLanesPlayfield(GraphicsDevice? device, Texture2D? whitePixel) =>
            new LanesPlayfield(_resources, _config, _visuals);
    }
}
