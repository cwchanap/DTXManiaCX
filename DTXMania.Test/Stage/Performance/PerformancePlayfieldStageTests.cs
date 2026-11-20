using System;
using System.Collections.Generic;
using System.Threading;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Test.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

[Trait("Category", "Unit")]
public class PerformancePlayfieldStageTests
{
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

    private static IStageGame CreateGame(ConfigData config, MockInputManagerCompat input,
        Mock<IConfigManager>? configManager = null)
    {
        configManager ??= new Mock<IConfigManager>();
        configManager.SetupGet(x => x.Config).Returns(config);
        var game = new Mock<IStageGame>();
        game.SetupGet(x => x.ConfigManager).Returns(configManager.Object);
        game.SetupGet(x => x.ResourceManager).Returns(new Mock<IResourceManager>().Object);
        game.SetupGet(x => x.InputManager).Returns(input);
        game.SetupGet(x => x.LoggerFactory).Returns(NullLoggerFactory.Instance);
        return game.Object;
    }

    private sealed class HeadlessStage : PerformanceStage
    {
        private readonly IPerformancePlayfield _field;

        public HeadlessStage(IStageGame game, IPerformancePlayfield field) : base(game) => _field = field;
        public ITexture? Pixel { get; set; }
        public Exception? FactoryFailure { get; set; }
        protected override SpriteBatch? CreateSpriteBatch(GraphicsDevice graphicsDevice) => null;
        protected override ITexture? CreateFallbackWhiteTexture() => Pixel;
        protected override IPerformancePlayfield? CreateLanesPlayfield(GraphicsDevice? device, Texture2D? whitePixel) =>
            FactoryFailure != null ? throw FactoryFailure : _field;
        protected override IChartVideoPlayer CreateChartVideoPlayer() =>
            throw new InvalidOperationException("Headless activation must not construct a video player.");
    }
}
