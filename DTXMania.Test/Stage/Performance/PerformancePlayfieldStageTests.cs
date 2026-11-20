using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Test.Helpers;
using DTXMania.Test.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class PerformancePlayfieldStageTests
{
    [Fact]
    public async Task HeadlessStage_NonDefaultProfile_UsesIsolatedActualCacheFactory()
    {
        using var input = new MockInputManagerCompat();
        using var stage = new HeadlessStage(
            CreateGame(new ConfigData { PlaySpeedPercent = 150 }, input),
            new Mock<IPerformancePlayfield>().Object);
        var orphan = Path.Combine(stage.CacheRoot, "orphan.tmp-test");
        File.WriteAllText(orphan, "owned");
        try
        {
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Isolated" }, 0,
                new ParsedChart("isolated-playfield.dtx")));
            Assert.NotNull(await stage.PreparationTask!.WaitAsync(TimeSpan.FromSeconds(5)));
            var cache = await stage.CacheCreated.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(Path.GetFullPath(stage.CacheRoot), cache.CacheRoot);
            Assert.False(File.Exists(orphan));
        }
        finally
        {
            stage.Dispose();
            await stage.CacheCleanup!.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.False(Directory.Exists(stage.CacheRoot));
    }

    [Fact]
    public void Activation_ShouldRouteLoadingInputAndDisposeThePlayfield()
    {
        using var input = new MockInputManagerCompat();
        var field = new Mock<IPerformancePlayfield>();
        using var stage = new HeadlessStage(CreateGame(new ConfigData(), input), field.Object);

        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Loading" }, 0, new ParsedChart("loading.dtx")));
        input.TriggerLaneHit(3);

        field.Verify(x => x.OnLaneInput(3), Times.Once);
        stage.Deactivate();
        field.Verify(x => x.Dispose(), Times.Once);
        input.TriggerLaneHit(3);
        field.Verify(x => x.OnLaneInput(3), Times.Once);
    }

    [Fact]
    public void ChartPublication_ShouldSendBpmAndCurrentScrollSpeedToPlayfield()
    {
        using var input = new MockInputManagerCompat();
        var config = new ConfigData { ScrollSpeed = 100 };
        var field = new Mock<IPerformancePlayfield>();
        bool published = false;
        field.Setup(x => x.OnChartPublished(It.IsAny<double>(), It.IsAny<int>()))
            .Callback(() => published = true);
        using var stage = new HeadlessStage(CreateGame(config, input), field.Object);
        var chart = new ParsedChart("publication.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x11, "01"));
        chart.FinalizeChart();
        chart.Bpm = 150;

        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Publication" }, 0, chart));
        config.ScrollSpeed = 200;
        Assert.True(SpinWait.SpinUntil(() =>
        {
            stage.Update(0);
            return published;
        }, TimeSpan.FromSeconds(5)));

        field.Verify(x => x.OnChartPublished(150, 200), Times.Once);
        stage.Deactivate();
    }

    [Fact]
    public void ScrollSpeedChanges_ShouldReachPlayfieldDuringLoadingAndUnsubscribeOnDeactivate()
    {
        using var input = new MockInputManagerCompat();
        var config = new ConfigData();
        var configManager = new Mock<IConfigManager>();
        configManager.SetupGet(x => x.Config).Returns(config);
        var field = new Mock<IPerformancePlayfield>();
        using var stage = new HeadlessStage(CreateGame(config, input, configManager), field.Object);
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Scroll" }, 0, new ParsedChart("scroll.dtx")));

        configManager.Raise(x => x.ScrollSpeedChanged += null, new ScrollSpeedChangedEventArgs(100, 125));
        field.Verify(x => x.OnScrollSpeedChanged(125), Times.Once);
        stage.Deactivate();
        configManager.Raise(x => x.ScrollSpeedChanged += null, new ScrollSpeedChangedEventArgs(125, 150));
        field.Verify(x => x.OnScrollSpeedChanged(150), Times.Never);
    }

    [Fact]
    public void AutoHits_ShouldForwardEachJudgementFrameBeforeItsPadFeedback()
    {
        var config = new ConfigData();
        config.AutoPlayLanes.Add(0);
        using var input = new MockInputManagerCompat();
        var field = new Mock<IPerformancePlayfield>();
        var events = new List<string>();
        field.Setup(x => x.OnJudgement(It.IsAny<JudgementEvent>(), It.IsAny<PerformanceFrameState>(), It.IsAny<bool>()))
            .Callback<JudgementEvent, PerformanceFrameState, bool>((_, frame, _) => events.Add($"judgement:{frame.Combo}"));
        field.Setup(x => x.OnAutoHit(0)).Callback(() => events.Add("pad"));
        using var stage = new HeadlessStage(CreateGame(config, input), field.Object);
        var chart = new ParsedChart("autoplay.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { Id = 0, LaneIndex = 0, TimeMs = 100 });
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 0, TimeMs = 100 });

        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Auto" }, 0, chart));
        Assert.True(SpinWait.SpinUntil(() =>
        {
            stage.Update(0);
            return stage.AudioSession != null;
        }, TimeSpan.FromSeconds(5)));
        stage.Update(1.1);
        stage.Update(0.1);

        Assert.Equal(new[] { "judgement:1", "pad", "judgement:2", "pad" }, events);
        stage.Deactivate();
    }

    [Fact]
    public void LoadingInput_ShouldSuppressOnlyAutomatedLanes()
    {
        var config = new ConfigData();
        config.AutoPlayLanes.Add(0);
        using var input = new MockInputManagerCompat();
        var field = new Mock<IPerformancePlayfield>();
        using var stage = new HeadlessStage(CreateGame(config, input), field.Object);
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Mixed" }, 0, new ParsedChart("mixed.dtx")));

        input.TriggerLaneHit(0);
        input.TriggerLaneHit(1);

        field.Verify(x => x.OnLaneInput(0), Times.Never);
        field.Verify(x => x.OnLaneInput(1), Times.Once);
        stage.Deactivate();
    }

    [Fact]
    public void Deactivate_ShouldDisposeVisualBorrowerBeforeReleasingWhitePixel()
    {
        using var input = new MockInputManagerCompat();
        var order = new List<string>();
        var pixel = new Mock<ITexture>();
        var field = new Mock<IPerformancePlayfield>();
        field.Setup(x => x.Dispose()).Callback(() => order.Add("playfield"));
        pixel.Setup(x => x.RemoveReference()).Callback(() => order.Add("pixel"));
        using var stage = new HeadlessStage(CreateGame(new ConfigData(), input), field.Object)
        {
            Pixel = pixel.Object
        };
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Lifetime" }, 0, new ParsedChart("lifetime.dtx")));

        stage.Deactivate();

        Assert.Equal(new[] { "playfield", "pixel" }, order);
        field.Verify(x => x.Dispose(), Times.Once);
        pixel.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void Activation_WhenPlayfieldConstructionFails_ShouldReleaseWhitePixelOnce()
    {
        using var input = new MockInputManagerCompat();
        var pixel = new Mock<ITexture>();
        var failure = new InvalidOperationException("playfield construction failed");
        using var stage = new HeadlessStage(CreateGame(new ConfigData(), input), new Mock<IPerformancePlayfield>().Object)
        {
            Pixel = pixel.Object,
            FactoryFailure = failure
        };

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Failure" }, 0, new ParsedChart("failure.dtx")))));
        pixel.Verify(x => x.RemoveReference(), Times.Once);
        stage.Deactivate();
        pixel.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void Activation_WhenPixelReleaseAlsoFails_ShouldPreserveConstructionFailure()
    {
        using var input = new MockInputManagerCompat();
        var pixel = new Mock<ITexture>();
        pixel.Setup(x => x.RemoveReference()).Throws(new InvalidOperationException("pixel release failed"));
        var failure = new InvalidOperationException("playfield construction failed");
        using var stage = new HeadlessStage(CreateGame(new ConfigData(), input), new Mock<IPerformancePlayfield>().Object)
        {
            Pixel = pixel.Object,
            FactoryFailure = failure
        };

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Failure" }, 0, new ParsedChart("failure.dtx")))));
        stage.Deactivate();
        pixel.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void Judgement_ShouldShowTimingOnlyForManualHitsAndForwardEachFrame()
    {
        var config = new ConfigData { ShowHitTimingFeedback = true, NoFail = true };
        config.AutoPlayLanes.Add(0);
        using var input = new MockInputManagerCompat();
        var field = new Mock<IPerformancePlayfield>();
        var events = new List<(int Lane, JudgementType Type, bool Timing, int Combo)>();
        field.Setup(x => x.OnJudgement(It.IsAny<JudgementEvent>(), It.IsAny<PerformanceFrameState>(), It.IsAny<bool>()))
            .Callback<JudgementEvent, PerformanceFrameState, bool>(
                (judgement, frame, timing) => events.Add((judgement.Lane, judgement.Type, timing, frame.Combo)));
        using var stage = new HeadlessStage(CreateGame(config, input), field.Object);
        var chart = new ParsedChart("timing.dtx") { DurationMs = 10000 };
        chart.Notes.Add(new Note { Id = 0, LaneIndex = 0, TimeMs = 100 });
        chart.Notes.Add(new Note { Id = 1, LaneIndex = 1, TimeMs = 100 });
        chart.Notes.Add(new Note { Id = 2, LaneIndex = 2, TimeMs = 100 });
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Timing" }, 0, chart));
        Assert.True(SpinWait.SpinUntil(() =>
        {
            stage.Update(0);
            return stage.AudioSession != null;
        }, TimeSpan.FromSeconds(5)));
        stage.Update(1.1);
        stage.Update(0.1);
        input.ModularInputManager.KeyBindings.BindButton("Test.Manual", 1);
        Assert.True(input.ModularInputManager.InjectButton("Test.Manual", true));
        input.ModularInputManager.Update();
        stage.Update(0);
        stage.Update(0.3);

        Assert.Contains(events, e => e.Lane == 0 && e.Type == JudgementType.Perfect && !e.Timing && e.Combo == 1);
        Assert.Contains(events, e => e.Lane == 1 && e.Type == JudgementType.Perfect && e.Timing && e.Combo == 2);
        Assert.Contains(events, e => e.Lane == 2 && e.Type == JudgementType.Miss && !e.Timing && e.Combo == 0);
        stage.Deactivate();
    }

    [Fact]
    public void TabSelection_ShouldPublishOnUpdateThreadAndReleaseLanesBeforePixel()
    {
        using var input = new MockInputManagerCompat();
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab, ScrollSpeed = 100, ShowCombo = false };
        config.AutoPlayLanes.Add(2);
        var manager = new Mock<IConfigManager>();
        var lanes = new Mock<IPerformancePlayfield>();
        var tab = new Mock<IPerformancePlayfield>();
        tab.SetupGet(t => t.Mode).Returns(DrumsNoteDisplayStyle.Tab);
        var order = new List<string>();
        int refs = 1;
        var pixel = CountedPixel("pixel", order, () => refs, n => refs = n);
        lanes.Setup(x => x.Dispose()).Callback(() => order.Add("lanes"));
        tab.Setup(x => x.OnChartPublished(150, 200)).Callback(() => order.Add("prepare"));
        tab.Setup(x => x.Dispose()).Callback(() => order.Add("tab"));
        using var stage = new HeadlessStage(CreateGame(config, input, manager), lanes.Object)
        {
            Pixel = pixel.Object,
            TabFactory = () => tab.Object
        };
        var chart = new ParsedChart("tab-factory.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x1A, "01"));
        chart.FinalizeChart();
        chart.Bpm = 150;
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Tab" }, 0, chart));
        input.TriggerLaneHit(1);
        lanes.Verify(x => x.OnLaneInput(1), Times.Once);
        config.ScrollSpeed = 200;
        config.ShowCombo = true;
        config.AutoPlayLanes.Clear();
        int thread = Environment.CurrentManagedThreadId;
        Assert.True(SpinWait.SpinUntil(() =>
        {
            stage.Update(0);
            return stage.TabFactoryThread != 0;
        }, TimeSpan.FromSeconds(5)));

        Assert.Equal(thread, stage.TabFactoryThread);
        Assert.Equal(new[] { "pixel add:2", "prepare", "lanes", "pixel remove:1" }, order);
        Assert.Equal(1, pixel.Object.ReferenceCount);
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(nameof(DrumsNoteDisplayStyle.Tab), telemetry.NoteDisplayStyle);
        input.TriggerLaneHit(1);
        tab.Verify(x => x.OnLaneInput(1), Times.Once);
        manager.Raise(x => x.ScrollSpeedChanged += null, new ScrollSpeedChangedEventArgs(200, 225));
        tab.Verify(x => x.OnScrollSpeedChanged(225), Times.Once);
        stage.DrawPlayfieldBase(null!);
        stage.DrawPlayfieldOverlay(null!);
        stage.DrawPlayfieldHud(null!);
        stage.DrawPlayfieldAdditive(null!);
        tab.Verify(x => x.DrawBase(null!, It.IsAny<ChartManager?>(), It.IsAny<bool>(), It.IsAny<double>()), Times.Once);
        tab.Verify(x => x.DrawOverlay(null!, It.IsAny<ChartManager?>(), It.IsAny<bool>(), It.IsAny<double>()), Times.Once);
        tab.Verify(x => x.DrawHud(null!, It.IsAny<PerformanceFrameState>()), Times.Once);
        tab.Verify(x => x.DrawAdditive(null!), Times.Once);
        stage.Deactivate();
        Assert.Equal(new[] { "pixel add:2", "prepare", "lanes", "pixel remove:1", "tab", "pixel remove:0" }, order);
        Assert.Equal(0, pixel.Object.ReferenceCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TabSelection_WhenCandidateUnavailableOrThrows_ShouldKeepCompleteLanes(int failure)
    {
        using var input = new MockInputManagerCompat();
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab };
        var lanes = new Mock<IPerformancePlayfield>();
        lanes.SetupGet(x => x.Mode).Returns(DrumsNoteDisplayStyle.Lanes);
        var tab = new Mock<IPerformancePlayfield>();
        if (failure == 2)
            tab.Setup(x => x.OnChartPublished(It.IsAny<double>(), It.IsAny<int>())).Throws(new InvalidOperationException("prepare"));
        using var stage = new HeadlessStage(CreateGame(config, input), lanes.Object)
        {
            TabFactory = () => failure == 0 ? null : failure == 1
                ? throw new InvalidOperationException("create") : tab.Object
        };
        var chart = new ParsedChart("tab-fallback.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x1A, "01"));
        chart.FinalizeChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Fallback" }, 0, chart));
        Assert.True(SpinWait.SpinUntil(() =>
        {
            stage.Update(0);
            return stage.AudioSession != null;
        }, TimeSpan.FromSeconds(5)));

        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(nameof(DrumsNoteDisplayStyle.Lanes), telemetry.NoteDisplayStyle);
        Assert.True(telemetry.PerformanceReady);
        input.TriggerLaneHit(1);
        lanes.Verify(x => x.OnLaneInput(1), Times.Once);
        lanes.Verify(x => x.Dispose(), Times.Never);
        tab.Verify(x => x.Dispose(), Times.Exactly(failure == 2 ? 1 : 0));
        stage.Deactivate();
        lanes.Verify(x => x.Dispose(), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TabSelection_WhenCandidateSynchronouslyDeactivates_ShouldDisposeWithoutPublishingIt(bool duringPrepare)
    {
        using var input = new MockInputManagerCompat();
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab };
        var lanes = new Mock<IPerformancePlayfield>();
        var tab = new Mock<IPerformancePlayfield>();
        tab.SetupGet(t => t.Mode).Returns(DrumsNoteDisplayStyle.Tab);
        using var stage = new HeadlessStage(CreateGame(config, input), lanes.Object);
        if (duringPrepare)
            tab.Setup(t => t.OnChartPublished(It.IsAny<double>(), It.IsAny<int>())).Callback(stage.Deactivate);
        stage.TabFactory = () =>
        {
            if (!duringPrepare) stage.Deactivate();
            return tab.Object;
        };
        var chart = new ParsedChart("tab-reentrant.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x1A, "01"));
        chart.FinalizeChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Reentrant" }, 0, chart));

        Assert.True(SpinWait.SpinUntil(() =>
        {
            stage.Update(0);
            return !stage.IsActive;
        }, TimeSpan.FromSeconds(5)));

        tab.Verify(t => t.OnChartPublished(It.IsAny<double>(), It.IsAny<int>()),
            Times.Exactly(duringPrepare ? 1 : 0));
        tab.Verify(t => t.Dispose(), Times.Once);
        lanes.Verify(t => t.Dispose(), Times.Once);
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal(nameof(DrumsNoteDisplayStyle.Lanes), telemetry.NoteDisplayStyle);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ChartPublication_WhenLanesRetiresActivation_DoesNotPublishOrFailAnotherRun(
        bool reactivate, bool throwAfterRetirement)
    {
        using var input = new MockInputManagerCompat();
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab };
        var oldLanes = new Mock<IPerformancePlayfield>();
        var newLanes = new Mock<IPerformancePlayfield>();
        var events = new List<string>();
        int oldRefs = 1, newRefs = 1;
        var oldPixel = CountedPixel("old", events, () => oldRefs, n => oldRefs = n);
        var newPixel = CountedPixel("new", events, () => newRefs, n => newRefs = n);
        oldLanes.Setup(x => x.Dispose()).Callback(() => events.Add($"lanes dispose:{oldRefs}"));
        newLanes.SetupGet(x => x.Mode).Returns(DrumsNoteDisplayStyle.Lanes);
        var tab = new Mock<IPerformancePlayfield>();
        tab.SetupGet(x => x.Mode).Returns(DrumsNoteDisplayStyle.Tab);
        var newChart = new ParsedChart("new-lanes.dtx") { Bpm = 213 };
        newChart.AddNote(new Note(0, 0, 96, 0x1A, "01"));
        newChart.FinalizeChart();
        using var stage = new HeadlessStage(CreateGame(config, input), oldLanes.Object) { Pixel = oldPixel.Object };
        bool retired = false;
        stage.TabFactory = () => tab.Object;
        oldLanes.Setup(x => x.OnChartPublished(It.IsAny<double>(), It.IsAny<int>())).Callback(() =>
        {
            events.Add($"lanes prepare:{oldRefs}");
            stage.Deactivate();
            if (reactivate)
            {
                config.NoteDisplayStyle = DrumsNoteDisplayStyle.Lanes;
                stage.Pixel = newPixel.Object;
                stage.LanesFactory = () => newLanes.Object;
                stage.Activate(new SongPlayRequest(new SongListNode { Title = "New" }, 0, newChart));
            }
            events.Add($"lanes resumed:{oldRefs}");
            retired = true;
            if (throwAfterRetirement) throw new InvalidOperationException("old callback failed");
        });
        var oldChart = new ParsedChart("old-tab.dtx") { Bpm = 147 };
        oldChart.AddNote(new Note(0, 0, 96, 0x1A, "01"));
        oldChart.FinalizeChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Old" }, 0, oldChart));
        var oldTask = stage.PreparationTask!;

        Assert.True(SpinWait.SpinUntil(() => { stage.Update(0); return retired; }, TimeSpan.FromSeconds(5)));
        await oldTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(oldTask.IsCompletedSuccessfully);
        Assert.Equal(new[] { "old add:2", "lanes prepare:2", "lanes dispose:2",
            "old remove:1", "lanes resumed:1", "old remove:0" }, events);
        Assert.Equal(0, oldPixel.Object.ReferenceCount);
        Assert.Equal(0, stage.TabFactoryThread);
        tab.Verify(x => x.OnChartPublished(It.IsAny<double>(), It.IsAny<int>()), Times.Never);
        tab.Verify(x => x.Dispose(), Times.Never);
        oldLanes.Verify(x => x.Dispose(), Times.Once);

        if (reactivate)
        {
            var newTask = stage.PreparationTask!;
            Assert.NotSame(oldTask, newTask);
            Assert.True(SpinWait.SpinUntil(() => { stage.Update(0); return stage.AudioSession != null; }, TimeSpan.FromSeconds(5)));
            await newTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(newTask.IsCompletedSuccessfully);
            newLanes.Verify(x => x.OnChartPublished(213, It.IsAny<int>()), Times.Once);
            newLanes.Verify(x => x.Dispose(), Times.Never);
            var telemetry = new GameTelemetrySnapshot();
            stage.PopulateTelemetry(telemetry);
            Assert.Equal("New", telemetry.SelectedSongTitle);
            Assert.Equal(nameof(DrumsNoteDisplayStyle.Lanes), telemetry.NoteDisplayStyle);
            Assert.True(telemetry.PerformanceReady);
            stage.Deactivate();
            Assert.Equal(0, newPixel.Object.ReferenceCount);
        }
        else
        {
            Assert.False(stage.IsActive);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChartPublication_WhenRetired_DropsQueuedAudioAndPreservesFreshTerminal(bool reactivate)
    {
        var directory = Directory.CreateTempSubdirectory("dtx-publication-retirement-");
        using var input = new MockInputManagerCompat();
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab };
        var oldLanes = new Mock<IPerformancePlayfield>();
        var newLanes = new Mock<IPerformancePlayfield>();
        using var stage = new HeadlessStage(CreateGame(config, input), oldLanes.Object);
        try
        {
            var oldChart = new ParsedChart("old-audio.dtx") { Bpm = 117 };
            oldChart.AddNote(new Note(0, 0, 96, 0x1A, "01"));
            oldChart.FinalizeChart();
            oldChart.BackgroundAudioPath = AudioTestUtils.CreateTestWavFile(directory.FullName, "old.wav");
            var newChart = new ParsedChart("new-audio.dtx") { Bpm = 229 };
            newChart.AddNote(new Note(0, 0, 192, 0x1B, "01"));
            newChart.FinalizeChart();
            newChart.BackgroundAudioPath = AudioTestUtils.CreateTestWavFile(directory.FullName, "new.wav");
            oldLanes.Setup(x => x.OnChartPublished(It.IsAny<double>(), It.IsAny<int>())).Callback(() =>
            {
                stage.Deactivate();
                if (reactivate)
                {
                    config.NoteDisplayStyle = DrumsNoteDisplayStyle.Lanes;
                    stage.LanesFactory = () => newLanes.Object;
                    stage.Activate(new SongPlayRequest(new SongListNode { Title = "Fresh audio" }, 0, newChart));
                }
            });
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Stale audio" }, 0, oldChart));
            var oldTask = stage.PreparationTask!;
            var oldSet = Assert.IsType<PreparedGameplayAudioSet>(await oldTask.WaitAsync(TimeSpan.FromSeconds(5)));
            var oldSound = Assert.IsAssignableFrom<ISound>(oldSet.MainBackground);
            Assert.False(oldSound.IsDisposed);

            stage.BackgroundWork!.Drain();

            Assert.True(oldSound.IsDisposed);
            Assert.Equal(0, stage.TabFactoryThread);
            if (!reactivate)
            {
                Assert.False(stage.IsActive);
                Assert.Null(stage.AudioSession);
                return;
            }
            var newTask = stage.PreparationTask!;
            Assert.NotSame(oldTask, newTask);
            var newSet = Assert.IsType<PreparedGameplayAudioSet>(await newTask.WaitAsync(TimeSpan.FromSeconds(5)));
            var newSound = Assert.IsAssignableFrom<ISound>(newSet.MainBackground);
            Assert.False(newSound.IsDisposed);
            stage.Update(0);
            Assert.Same(newSet, stage.AudioSession!.PreparedSet);
            Assert.Same(newChart.Notes[0], Assert.Single(stage.Rules!.ChartManager!.AllNotes));
            newLanes.Verify(x => x.OnChartPublished(229, It.IsAny<int>()), Times.Once);
            var telemetry = new GameTelemetrySnapshot();
            stage.PopulateTelemetry(telemetry);
            Assert.Equal("Fresh audio", telemetry.SelectedSongTitle);
            Assert.Equal(nameof(DrumsNoteDisplayStyle.Lanes), telemetry.NoteDisplayStyle);
            Assert.True(telemetry.PerformanceReady);
            stage.Deactivate();
            Assert.True(newSound.IsDisposed);
        }
        finally
        {
            stage.Deactivate();
            await stage.Workers.WaitAsync(TimeSpan.FromSeconds(5));
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, true, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, true)]
    public async Task TabSelection_PixelLeaseSurvivesCandidateCallbacksAndStaleCleanup(
        bool duringPrepare, bool reactivate, bool callbackThrows, bool cleanupThrows)
    {
        using var input = new MockInputManagerCompat();
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab };
        var events = new List<string>();
        int oldRefs = 1, newRefs = 1;
        var oldPixel = CountedPixel("old", events, () => oldRefs, n => oldRefs = n);
        var newPixel = CountedPixel("new", events, () => newRefs, n => newRefs = n);
        var lanes = new Mock<IPerformancePlayfield>();
        var newLanes = new Mock<IPerformancePlayfield>();
        var candidate = new Mock<IPerformancePlayfield>();
        lanes.Setup(x => x.OnChartPublished(It.IsAny<double>(), It.IsAny<int>()))
            .Callback(() => events.Add($"lanes prepare:{oldRefs}"));
        lanes.Setup(x => x.Dispose()).Callback(() => events.Add($"lanes dispose:{oldRefs}"));
        candidate.Setup(x => x.Dispose()).Callback(() =>
        {
            events.Add($"candidate dispose:{oldRefs}");
            if (cleanupThrows) throw new InvalidOperationException("candidate cleanup");
        });
        using var stage = new HeadlessStage(CreateGame(config, input), lanes.Object) { Pixel = oldPixel.Object };
        void Retire()
        {
            stage.Deactivate();
            if (reactivate)
            {
                config.NoteDisplayStyle = DrumsNoteDisplayStyle.Lanes;
                stage.Pixel = newPixel.Object;
                stage.LanesFactory = () => newLanes.Object;
                stage.Activate(new SongPlayRequest(new SongListNode { Title = "New" }, 0,
                    new ParsedChart("pixel-new.dtx") { Bpm = 211 }));
            }
        }
        candidate.Setup(x => x.OnChartPublished(It.IsAny<double>(), It.IsAny<int>())).Callback(() =>
        {
            events.Add($"candidate prepare:{oldRefs}");
            if (!duringPrepare) return;
            Retire();
            events.Add($"prepare resumed:{oldRefs}");
            if (callbackThrows) throw new InvalidOperationException("prepare");
        });
        bool finished = false;
        stage.TabFactory = () =>
        {
            events.Add($"factory:{oldRefs}");
            if (!duringPrepare)
            {
                Retire();
                events.Add($"factory resumed:{oldRefs}");
                if (callbackThrows)
                {
                    events.Add($"factory partial cleanup:{oldRefs}");
                    finished = true;
                    throw new InvalidOperationException("factory");
                }
            }
            finished = true;
            return candidate.Object;
        };
        var oldChart = new ParsedChart("pixel-old.dtx");
        oldChart.AddNote(new Note(0, 0, 96, 0x1A, "01"));
        oldChart.FinalizeChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Old" }, 0, oldChart));
        var oldTask = stage.PreparationTask!;

        Assert.True(SpinWait.SpinUntil(() => { stage.Update(0); return finished; }, TimeSpan.FromSeconds(5)));
        await oldTask.WaitAsync(TimeSpan.FromSeconds(5));
        var expected = new List<string> { "old add:2", "lanes prepare:2", "factory:2" };
        if (duringPrepare) expected.Add("candidate prepare:2");
        expected.AddRange(new[] { "lanes dispose:2", "old remove:1",
            duringPrepare ? "prepare resumed:1" : "factory resumed:1" });
        if (!duringPrepare && callbackThrows) expected.Add("factory partial cleanup:1");
        else expected.Add("candidate dispose:1");
        expected.Add("old remove:0");
        Assert.Equal(expected, events);
        Assert.Equal(0, oldPixel.Object.ReferenceCount);
        candidate.Verify(x => x.OnChartPublished(It.IsAny<double>(), It.IsAny<int>()), Times.Exactly(duringPrepare ? 1 : 0));
        candidate.Verify(x => x.Dispose(), Times.Exactly(!duringPrepare && callbackThrows ? 0 : 1));
        Assert.Equal(reactivate, stage.IsActive);
        if (reactivate)
        {
            Assert.Equal(1, newPixel.Object.ReferenceCount);
            Assert.True(SpinWait.SpinUntil(() => { stage.Update(0); return stage.AudioSession != null; }, TimeSpan.FromSeconds(5)));
            await stage.PreparationTask!.WaitAsync(TimeSpan.FromSeconds(5));
            var telemetry = new GameTelemetrySnapshot();
            stage.PopulateTelemetry(telemetry);
            Assert.Equal("New", telemetry.SelectedSongTitle);
            Assert.Equal(nameof(DrumsNoteDisplayStyle.Lanes), telemetry.NoteDisplayStyle);
            Assert.True(telemetry.PerformanceReady);
            stage.Deactivate();
            Assert.Equal(0, newPixel.Object.ReferenceCount);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TabSelection_PixelLeaseSurvivesOutgoingLanesTeardown(bool reactivate, bool cleanupThrows)
    {
        using var input = new MockInputManagerCompat();
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab };
        var events = new List<string>();
        int oldRefs = 1, newRefs = 1;
        var oldPixel = CountedPixel("old", events, () => oldRefs, n => oldRefs = n);
        var newPixel = CountedPixel("new", events, () => newRefs, n => newRefs = n);
        var lanes = new Mock<IPerformancePlayfield>();
        var newLanes = new Mock<IPerformancePlayfield>();
        var candidate = new Mock<IPerformancePlayfield>();
        candidate.SetupGet(x => x.Mode).Returns(DrumsNoteDisplayStyle.Tab);
        candidate.Setup(x => x.OnChartPublished(It.IsAny<double>(), It.IsAny<int>()))
            .Callback(() => events.Add($"candidate prepare:{oldRefs}"));
        candidate.Setup(x => x.Dispose()).Callback(() => events.Add($"candidate dispose:{oldRefs}"));
        using var stage = new HeadlessStage(CreateGame(config, input), lanes.Object)
        {
            Pixel = oldPixel.Object,
            TabFactory = () => candidate.Object
        };
        bool retired = false;
        lanes.Setup(x => x.Dispose()).Callback(() =>
        {
            events.Add($"lanes begin:{oldRefs}");
            stage.Deactivate();
            if (reactivate)
            {
                config.NoteDisplayStyle = DrumsNoteDisplayStyle.Lanes;
                stage.Pixel = newPixel.Object;
                stage.LanesFactory = () => newLanes.Object;
                stage.Activate(new SongPlayRequest(new SongListNode { Title = "New" }, 0,
                    new ParsedChart("outgoing-new.dtx") { Bpm = 203 }));
            }
            events.Add($"lanes end:{oldRefs}");
            retired = true;
            if (cleanupThrows) throw new InvalidOperationException("outgoing lanes cleanup");
        });
        var chart = new ParsedChart("outgoing-old.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x1A, "01"));
        chart.FinalizeChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Old" }, 0, chart));
        var oldTask = stage.PreparationTask!;
        Assert.True(SpinWait.SpinUntil(() => { stage.Update(0); return retired; }, TimeSpan.FromSeconds(5)));
        await oldTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "old add:2", "candidate prepare:2", "lanes begin:2",
            "candidate dispose:2", "old remove:1", "lanes end:1", "old remove:0" }, events);
        Assert.Equal(0, oldPixel.Object.ReferenceCount);
        candidate.Verify(x => x.Dispose(), Times.Once);
        Assert.Equal(reactivate, stage.IsActive);
        if (reactivate)
        {
            Assert.Equal(1, newPixel.Object.ReferenceCount);
            Assert.True(SpinWait.SpinUntil(() => { stage.Update(0); return stage.AudioSession != null; }, TimeSpan.FromSeconds(5)));
            await stage.PreparationTask!.WaitAsync(TimeSpan.FromSeconds(5));
            var telemetry = new GameTelemetrySnapshot();
            stage.PopulateTelemetry(telemetry);
            Assert.Equal("New", telemetry.SelectedSongTitle);
            Assert.Equal(nameof(DrumsNoteDisplayStyle.Lanes), telemetry.NoteDisplayStyle);
            Assert.True(telemetry.PerformanceReady);
            stage.Deactivate();
            Assert.Equal(0, newPixel.Object.ReferenceCount);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task TabSelection_PixelLeaseBalancesNormalFallbackAndCandidateCleanup(int failure)
    {
        using var input = new MockInputManagerCompat();
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab };
        var events = new List<string>();
        int refs = 1;
        var pixel = CountedPixel("pixel", events, () => refs, n => refs = n);
        var lanes = new Mock<IPerformancePlayfield>();
        var candidate = new Mock<IPerformancePlayfield>();
        lanes.Setup(x => x.Dispose()).Callback(() => events.Add($"lanes dispose:{refs}"));
        candidate.Setup(x => x.OnChartPublished(It.IsAny<double>(), It.IsAny<int>())).Callback(() =>
        {
            events.Add($"candidate prepare:{refs}");
            if (failure == 2) throw new InvalidOperationException("prepare failed");
        });
        candidate.Setup(x => x.Dispose()).Callback(() => events.Add($"candidate dispose:{refs}"));
        using var stage = new HeadlessStage(CreateGame(config, input), lanes.Object)
        {
            Pixel = pixel.Object,
            TabFactory = () =>
            {
                events.Add($"factory:{refs}");
                if (failure == 1) throw new InvalidOperationException("factory failed");
                return failure == 0 ? null : candidate.Object;
            }
        };
        var chart = new ParsedChart("fallback-pixel.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x1A, "01"));
        chart.FinalizeChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Fallback" }, 0, chart));
        Assert.True(SpinWait.SpinUntil(() => { stage.Update(0); return stage.AudioSession != null; }, TimeSpan.FromSeconds(5)));
        await stage.PreparationTask!.WaitAsync(TimeSpan.FromSeconds(5));
        var expected = new List<string> { "pixel add:2", "factory:2" };
        if (failure == 2) expected.AddRange(new[] { "candidate prepare:2", "candidate dispose:2" });
        expected.Add("pixel remove:1");
        Assert.Equal(expected, events);
        Assert.Equal(1, pixel.Object.ReferenceCount);
        lanes.Verify(x => x.Dispose(), Times.Never);
        stage.Deactivate();
        expected.AddRange(new[] { "lanes dispose:1", "pixel remove:0" });
        Assert.Equal(expected, events);
        Assert.Equal(0, pixel.Object.ReferenceCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TabSelection_WhenCandidateCleanupReactivates_PixelOutlivesCleanup(bool cleanupThrows)
    {
        using var input = new MockInputManagerCompat();
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab };
        var events = new List<string>();
        int oldRefs = 1, newRefs = 1;
        var oldPixel = CountedPixel("old", events, () => oldRefs, n => oldRefs = n);
        var newPixel = CountedPixel("new", events, () => newRefs, n => newRefs = n);
        var lanes = new Mock<IPerformancePlayfield>();
        var newLanes = new Mock<IPerformancePlayfield>();
        lanes.Setup(x => x.Dispose()).Callback(() => events.Add($"lanes dispose:{oldRefs}"));
        var candidate = new Mock<IPerformancePlayfield>();
        candidate.Setup(x => x.OnChartPublished(It.IsAny<double>(), It.IsAny<int>()))
            .Throws(new InvalidOperationException("candidate prepare"));
        using var stage = new HeadlessStage(CreateGame(config, input), lanes.Object)
        {
            Pixel = oldPixel.Object,
            TabFactory = () => candidate.Object
        };
        bool retired = false;
        candidate.Setup(x => x.Dispose()).Callback(() =>
        {
            events.Add($"candidate cleanup begin:{oldRefs}");
            stage.Deactivate();
            config.NoteDisplayStyle = DrumsNoteDisplayStyle.Lanes;
            stage.Pixel = newPixel.Object;
            stage.LanesFactory = () => newLanes.Object;
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "New cleanup" }, 0,
                new ParsedChart("cleanup-new.dtx")));
            events.Add($"candidate cleanup end:{oldRefs}");
            retired = true;
            if (cleanupThrows) throw new InvalidOperationException("candidate cleanup");
        });
        var chart = new ParsedChart("cleanup-old.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x1A, "01"));
        chart.FinalizeChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Old cleanup" }, 0, chart));
        var oldTask = stage.PreparationTask!;
        Assert.True(SpinWait.SpinUntil(() => { stage.Update(0); return retired; }, TimeSpan.FromSeconds(5)));
        await oldTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "old add:2", "candidate cleanup begin:2", "lanes dispose:2",
            "old remove:1", "candidate cleanup end:1", "old remove:0" }, events);
        Assert.Equal(0, oldPixel.Object.ReferenceCount);
        candidate.Verify(x => x.Dispose(), Times.Once);
        Assert.True(SpinWait.SpinUntil(() => { stage.Update(0); return stage.AudioSession != null; }, TimeSpan.FromSeconds(5)));
        await stage.PreparationTask!.WaitAsync(TimeSpan.FromSeconds(5));
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal("New cleanup", telemetry.SelectedSongTitle);
        Assert.Equal(nameof(DrumsNoteDisplayStyle.Lanes), telemetry.NoteDisplayStyle);
        Assert.True(telemetry.PerformanceReady);
        stage.Deactivate();
        Assert.Equal(0, newPixel.Object.ReferenceCount);
    }

    [Fact]
    public async Task ChartPublication_WhenTelemetryCallbackReactivates_DoesNotOverwriteNewSnapshot()
    {
        using var input = new MockInputManagerCompat();
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab };
        var oldLanes = new Mock<IPerformancePlayfield>();
        var newLanes = new Mock<IPerformancePlayfield>();
        newLanes.SetupGet(x => x.Mode).Returns(DrumsNoteDisplayStyle.Lanes);
        var candidate = new Mock<IPerformancePlayfield>();
        using var stage = new HeadlessStage(CreateGame(config, input), oldLanes.Object)
        {
            TabFactory = () => candidate.Object
        };
        bool reactivated = false;
        candidate.SetupGet(x => x.Mode).Returns(() =>
        {
            if (!reactivated)
            {
                reactivated = true;
                stage.Deactivate();
                config.NoteDisplayStyle = DrumsNoteDisplayStyle.Lanes;
                stage.LanesFactory = () => newLanes.Object;
                stage.Activate(new SongPlayRequest(new SongListNode { Title = "New telemetry" }, 0,
                    new ParsedChart("new-telemetry.dtx") { Bpm = 223 }));
            }
            return DrumsNoteDisplayStyle.Tab;
        });
        var chart = new ParsedChart("old-telemetry.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x1A, "01"));
        chart.FinalizeChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Old telemetry" }, 0, chart));
        var oldTask = stage.PreparationTask!;
        await oldTask.WaitAsync(TimeSpan.FromSeconds(5));
        var oldScope = stage.BackgroundWork!;
        oldScope.Drain();
        Assert.True(reactivated);
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal("New telemetry", telemetry.SelectedSongTitle);
        Assert.Equal(nameof(DrumsNoteDisplayStyle.Lanes), telemetry.NoteDisplayStyle);
        Assert.True(SpinWait.SpinUntil(() => { stage.Update(0); return stage.AudioSession != null; }, TimeSpan.FromSeconds(5)));
        await stage.PreparationTask!.WaitAsync(TimeSpan.FromSeconds(5));
        stage.Deactivate();
    }

    [Fact]
    public async Task ChartPublication_WhenFailureTelemetryReactivates_DoesNotFailOrOverwriteNewRun()
    {
        using var input = new MockInputManagerCompat();
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab };
        var events = new List<string>();
        int oldRefs = 1, newRefs = 1;
        var oldPixel = CountedPixel("old", events, () => oldRefs, n => oldRefs = n);
        var newPixel = CountedPixel("new", events, () => newRefs, n => newRefs = n);
        var oldLanes = new Mock<IPerformancePlayfield>();
        var newLanes = new Mock<IPerformancePlayfield>();
        newLanes.SetupGet(x => x.Mode).Returns(DrumsNoteDisplayStyle.Lanes);
        oldLanes.Setup(x => x.Dispose()).Callback(() => events.Add($"lanes dispose:{oldRefs}"));
        using var stage = new HeadlessStage(CreateGame(config, input), oldLanes.Object) { Pixel = oldPixel.Object };
        bool failed = false, reactivated = false;
        oldLanes.Setup(x => x.OnChartPublished(It.IsAny<double>(), It.IsAny<int>())).Callback(() =>
        {
            failed = true;
            throw new InvalidOperationException("chart callback");
        });
        oldLanes.SetupGet(x => x.Mode).Returns(() =>
        {
            if (failed && !reactivated)
            {
                reactivated = true;
                events.Add($"failure telemetry:{oldRefs}");
                stage.Deactivate();
                config.NoteDisplayStyle = DrumsNoteDisplayStyle.Lanes;
                stage.Pixel = newPixel.Object;
                stage.LanesFactory = () => newLanes.Object;
                stage.Activate(new SongPlayRequest(new SongListNode { Title = "New after failure" }, 0,
                    new ParsedChart("failure-new.dtx")));
                events.Add($"telemetry resumed:{oldRefs}");
            }
            return DrumsNoteDisplayStyle.Lanes;
        });
        var chart = new ParsedChart("failure-old.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x1A, "01"));
        chart.FinalizeChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Old failure" }, 0, chart));
        await stage.PreparationTask!.WaitAsync(TimeSpan.FromSeconds(5));
        stage.BackgroundWork!.Drain();
        Assert.True(reactivated);
        Assert.Equal(new[] { "old add:2", "failure telemetry:2", "lanes dispose:2",
            "old remove:1", "telemetry resumed:1", "old remove:0" }, events);
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal("New after failure", telemetry.SelectedSongTitle);
        Assert.True(SpinWait.SpinUntil(() => { stage.Update(0); return stage.AudioSession != null; }, TimeSpan.FromSeconds(5)));
        await stage.PreparationTask!.WaitAsync(TimeSpan.FromSeconds(5));
        stage.PopulateTelemetry(telemetry);
        Assert.True(telemetry.PerformanceReady);
        Assert.Equal(nameof(DrumsNoteDisplayStyle.Lanes), telemetry.NoteDisplayStyle);
        stage.Deactivate();
        Assert.Equal(0, newPixel.Object.ReferenceCount);
    }

    [Fact]
    public async Task TabSelection_RealCandidateRetiredDuringResourceLoad_CleansOldBorrowersBeforePixel()
    {
        using var input = new MockInputManagerCompat();
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab };
        var events = new List<string>();
        int oldRefs = 1, newRefs = 1;
        var oldPixel = CountedPixel("old", events, () => oldRefs, n => oldRefs = n);
        var newPixel = CountedPixel("new", events, () => newRefs, n => newRefs = n);
        var resources = new Mock<IResourceManager>();
        var font = new Mock<IFont>();
        font.Setup(f => f.SupportsCharacter(It.IsAny<char>())).Returns(true);
        font.Setup(f => f.RemoveReference()).Callback(() => events.Add($"font dispose:{oldRefs}"));
        resources.Setup(x => x.LoadFont(It.IsAny<string>(), It.IsAny<int>())).Returns(font.Object);
        var oldLanes = new Mock<IPerformancePlayfield>();
        oldLanes.Setup(x => x.Dispose()).Callback(() => events.Add($"lanes dispose:{oldRefs}"));
        var newLanes = new Mock<IPerformancePlayfield>();
        using var stage = new HeadlessStage(CreateGame(config, input, resources: resources.Object), oldLanes.Object)
        {
            Pixel = oldPixel.Object,
            TabFactoryWithCreate = create => create()
        };
        bool retired = false;
        resources.SetupGet(x => x.CurrentTheme).Returns(() =>
        {
            events.Add($"theme:{oldRefs}");
            stage.Deactivate();
            config.NoteDisplayStyle = DrumsNoteDisplayStyle.Lanes;
            stage.Pixel = newPixel.Object;
            stage.LanesFactory = () => newLanes.Object;
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "New real" }, 0,
                new ParsedChart("real-new.dtx")));
            events.Add($"theme resumed:{oldRefs}");
            retired = true;
            return null!;
        });
        var chart = new ParsedChart("real-old.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x1A, "01"));
        chart.FinalizeChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Old real" }, 0, chart));
        var oldTask = stage.PreparationTask!;
        Assert.True(SpinWait.SpinUntil(() => { stage.Update(0); return retired; }, TimeSpan.FromSeconds(5)));
        await oldTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "old add:2", "theme:2", "lanes dispose:2", "old remove:1",
            "theme resumed:1", "font dispose:1", "font dispose:1", "old remove:0" }, events);
        Assert.Equal(0, oldPixel.Object.ReferenceCount);
        Assert.Equal(1, newPixel.Object.ReferenceCount);
        Assert.Equal(0, stage.HudFactoryCalls);
        Assert.True(SpinWait.SpinUntil(() => { stage.Update(0); return stage.AudioSession != null; }, TimeSpan.FromSeconds(5)));
        await stage.PreparationTask!.WaitAsync(TimeSpan.FromSeconds(5));
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal("New real", telemetry.SelectedSongTitle);
        Assert.Equal(nameof(DrumsNoteDisplayStyle.Lanes), telemetry.NoteDisplayStyle);
        Assert.True(telemetry.PerformanceReady);
        stage.Deactivate();
        Assert.Equal(0, newPixel.Object.ReferenceCount);
    }

    [Fact]
    public async Task TabSelection_WhenFactoryReactivates_StaleCreateDelegateCannotReadNewResources()
    {
        using var input = new MockInputManagerCompat();
        var config = new ConfigData { NoteDisplayStyle = DrumsNoteDisplayStyle.Tab };
        var resources = new Mock<IResourceManager>();
        var lanes = new Mock<IPerformancePlayfield>();
        var newLanes = new Mock<IPerformancePlayfield>();
        using var stage = new HeadlessStage(CreateGame(config, input, resources: resources.Object), lanes.Object);
        bool called = false;
        IPerformancePlayfield? result = null;
        stage.TabFactoryWithCreate = create =>
        {
            stage.Deactivate();
            config.NoteDisplayStyle = DrumsNoteDisplayStyle.Lanes;
            stage.LanesFactory = () => newLanes.Object;
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "New delegate" }, 0,
                new ParsedChart("delegate-new.dtx")));
            result = create();
            called = true;
            return result;
        };
        var chart = new ParsedChart("delegate-old.dtx");
        chart.AddNote(new Note(0, 0, 96, 0x1A, "01"));
        chart.FinalizeChart();
        stage.Activate(new SongPlayRequest(new SongListNode { Title = "Old delegate" }, 0, chart));
        var oldTask = stage.PreparationTask!;
        Assert.True(SpinWait.SpinUntil(() => { stage.Update(0); return called; }, TimeSpan.FromSeconds(5)));
        await oldTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(result);
        resources.VerifyGet(x => x.CurrentTheme, Times.Never);
        resources.Verify(x => x.LoadFont(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        Assert.True(SpinWait.SpinUntil(() => { stage.Update(0); return stage.AudioSession != null; }, TimeSpan.FromSeconds(5)));
        await stage.PreparationTask!.WaitAsync(TimeSpan.FromSeconds(5));
        var telemetry = new GameTelemetrySnapshot();
        stage.PopulateTelemetry(telemetry);
        Assert.Equal("New delegate", telemetry.SelectedSongTitle);
        Assert.True(telemetry.PerformanceReady);
        stage.Deactivate();
    }

    private static Mock<ITexture> CountedPixel(string name, List<string> events, Func<int> get, Action<int> set)
    {
        var pixel = new Mock<ITexture>();
        pixel.SetupGet(x => x.ReferenceCount).Returns(get);
        pixel.Setup(x => x.AddReference()).Callback(() =>
        {
            set(get() + 1);
            events.Add($"{name} add:{get()}");
        });
        pixel.Setup(x => x.RemoveReference()).Callback(() =>
        {
            set(get() - 1);
            events.Add($"{name} remove:{get()}");
        });
        return pixel;
    }

    private static IStageGame CreateGame(ConfigData config, MockInputManagerCompat input,
        Mock<IConfigManager>? configManager = null, IResourceManager? resources = null)
    {
        configManager ??= new Mock<IConfigManager>();
        configManager.SetupGet(x => x.Config).Returns(config);
        var game = new Mock<IStageGame>();
        game.SetupGet(x => x.ConfigManager).Returns(configManager.Object);
        game.SetupGet(x => x.ResourceManager).Returns(resources ?? new Mock<IResourceManager>().Object);
        game.SetupGet(x => x.InputManager).Returns(input);
        game.SetupGet(x => x.LoggerFactory).Returns(NullLoggerFactory.Instance);
        return game.Object;
    }

    private sealed class HeadlessStage : PerformanceStage
    {
        private readonly IsolatedStageAudioCache _cache = new();
        private readonly IPerformancePlayfield _field;

        public HeadlessStage(IStageGame game, IPerformancePlayfield field) : base(game) => _field = field;
        public ITexture? Pixel { get; set; }
        public Exception? FactoryFailure { get; set; }
        public Func<IPerformancePlayfield?>? TabFactory { get; set; }
        public Func<Func<IPerformancePlayfield?>, IPerformancePlayfield?>? TabFactoryWithCreate { get; set; }
        public Func<IPerformancePlayfield>? LanesFactory { get; set; }
        public Task<PreparedGameplayAudioSet?>? PreparationTask { get; private set; }
        public Task<PreparedGameplayAudioSet?[]> Workers => _cache.Workers;
        public string CacheRoot => _cache.Root;
        public Task<PlaybackAudioVariantCache> CacheCreated => _cache.Created;
        public Task? CacheCleanup => _cache.Cleanup;
        public int HudFactoryCalls { get; private set; }
        public int TabFactoryThread { get; private set; }
        protected override SpriteBatch? CreateSpriteBatch(GraphicsDevice graphicsDevice) => null;
        protected override ITexture? CreateFallbackWhiteTexture() => Pixel;
        protected override IPerformancePlayfield? CreateLanesPlayfield(GraphicsDevice? device, Texture2D? whitePixel) =>
            FactoryFailure != null ? throw FactoryFailure : LanesFactory?.Invoke() ?? _field;
        internal override Task<PreparedGameplayAudioSet?> StartGameplayInitialization() =>
            PreparationTask = _cache.Track(base.StartGameplayInitialization());
        protected override Func<PlaybackAudioVariantCache> CreateAudioVariantCacheFactory() => _cache.Factory();
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                _cache.Release();
        }
        protected override IPerformancePlayfield? CreateTabPlayfield(Func<IPerformancePlayfield?> create)
        {
            TabFactoryThread = Environment.CurrentManagedThreadId;
            return TabFactoryWithCreate != null ? TabFactoryWithCreate(create) : TabFactory?.Invoke();
        }
        internal override TabHudDisplay? CreateTabHud(GraphicsDevice? device, SongChart? chart,
            string? title, string? artist, Texture2D? whitePixel, DTXMania.Game.Lib.UI.SkinPalette palette, TabModeArt art)
        {
            HudFactoryCalls++;
            return null;
        }
        protected override IChartVideoPlayer CreateChartVideoPlayer() =>
            throw new InvalidOperationException("Headless activation must not construct a video player.");
    }
}
