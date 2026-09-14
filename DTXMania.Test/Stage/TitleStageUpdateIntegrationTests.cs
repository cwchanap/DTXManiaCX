#nullable enable

using System;
using System.Collections.Generic;
using DTXMania.Game;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Graphics;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Input.Midi;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Update;
using DTXMania.Game.Lib.UI.Components;
using DTXMania.Test.TestData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage;

/// <summary>
/// Title-stage integration seams for the Windows auto-update (HPA-187 task 5):
/// CheckOnce fires from OnTransitionCompleted (not ctor/OnFirstUpdate), the closed banner leaves
/// the title menu fully usable, a downloading/launched install blocks Start/Config but not the
/// exit path, and the terminal InstallerCommitted state requests exit exactly once from the top
/// of OnUpdate — even while the crash panel already owns input (controller ruling 2).
/// InstallerLaunched alone must NEVER exit: a started bootstrapper may still be parked on an
/// unanswered UAC prompt, so only the observed exit-0 commit may terminate the game.
/// The F9 edge-trigger / frame-consumption contract itself lives in GameUpdateNotificationTests;
/// OnUpdate polls the real keyboard, so title-level tests here exercise the consumption wiring
/// through Activate/Back instead.
/// All tests drive OnUpdate/OnFirstUpdate directly via reflection so they stay headless
/// (no Keyboard.GetState, no GraphicsDevice) and thus Mac-safe.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TitleStageUpdateIntegrationTests
{
    private static readonly KeyboardState NoKeys = default;
    private static readonly KeyboardState F8Down = new(new[] { Keys.F8 });

    [Fact]
    public void CheckOnce_ShouldFireFromOnTransitionCompleted_NotFromConstructorOrOnFirstUpdate()
    {
        var service = new Mock<IGameUpdateService>(MockBehavior.Loose);
        service.Setup(x => x.CheckOnce()).Returns(Task.CompletedTask);
        var stage = CreateStage(new StubStageGame { GameUpdateService = service.Object });

        // Constructor must not check.
        service.Verify(x => x.CheckOnce(), Times.Never);

        // OnFirstUpdate (runs during FadeIn) must not check either.
        ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
        ReflectionHelpers.InvokePrivateMethod(stage, "OnFirstUpdate", 0.016);
        service.Verify(x => x.CheckOnce(), Times.Never);

        // The transition completed (title actually interactive) fires the one-shot check.
        stage.OnTransitionComplete();
        service.Verify(x => x.CheckOnce(), Times.Once);
    }

    [Fact]
    public void OnUpdate_WithClosedBannerAndActivatePressed_ShouldStillSelectGameStart()
    {
        var service = MockService(Snap(GameUpdateState.Available));
        var input = new StubInputManagerCompat();
        input.SetPressedCommand(InputCommandType.Activate);
        var stageManager = new Mock<IStageManager>();
        var stage = CreateStage(new StubStageGame { GameUpdateService = service.Object, InputManager = input });
        stage.StageManager = stageManager.Object;
        InjectUpdateNotification(stage, service.Object);
        SetPhaseNormal(stage);

        ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.016);

        // The closed banner consumed nothing, so the title still selects GAME START.
        stageManager.Verify(
            m => m.ChangeStage(StageType.SongSelect, It.IsAny<IStageTransition>()),
            Times.Once);
    }

    [Fact]
    public void OnUpdate_WhenDownloading_ShouldPreventStartAndConfigActivation()
    {
        var service = MockService(Snap(GameUpdateState.Downloading, percent: 43));
        var input = new StubInputManagerCompat();
        input.SetPressedCommand(InputCommandType.Activate);
        input.SetPressedCommand(InputCommandType.MoveDown);
        var stageManager = new Mock<IStageManager>();
        var stage = CreateStage(new StubStageGame { GameUpdateService = service.Object, InputManager = input });
        stage.StageManager = stageManager.Object;
        InjectUpdateNotification(stage, service.Object);
        SetPhaseNormal(stage);

        ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.016);

        // No Start, no Config, no exit: the in-flight operation consumes the frame.
        stageManager.Verify(
            m => m.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>()),
            Times.Never);
        Assert.Equal(0, GetGame(stage).ExitRequests);
    }

    [Fact]
    public void OnUpdate_WhenDownloading_ShouldStillAllowBackToExit()
    {
        var service = MockService(Snap(GameUpdateState.Downloading, percent: 43));
        var input = new StubInputManagerCompat();
        input.SetBackTriggered(true);
        var stage = CreateStage(new StubStageGame { GameUpdateService = service.Object, InputManager = input });
        InjectUpdateNotification(stage, service.Object);
        SetPhaseNormal(stage);

        ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.016);

        // The notification does not consume Back, so the title exit path stays reachable.
        Assert.Equal(1, GetGame(stage).ExitRequests);
    }

    [Fact]
    public void OnUpdate_WhenInstallerCommitted_ShouldRequestExitExactlyOnce()
    {
        var service = MockService(Snap(GameUpdateState.InstallerCommitted));
        var stage = CreateStage(new StubStageGame { GameUpdateService = service.Object });
        SetPhaseNormal(stage);

        ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.016);
        ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.016);

        Assert.Equal(1, GetGame(stage).ExitRequests);
    }

    [Fact]
    public void OnUpdate_WhenInstallerLaunched_ShouldNotRequestExit()
    {
        // Contract pin: InstallerLaunched means the elevation/install handoff is still
        // undecided — the bootstrapper may be parked on an unanswered UAC prompt that
        // can still be cancelled. The game must stay alive; only InstallerCommitted
        // (observed exit 0) or Inno's /CLOSEAPPLICATIONS may terminate it.
        var service = MockService(Snap(GameUpdateState.InstallerLaunched));
        var stage = CreateStage(new StubStageGame { GameUpdateService = service.Object });
        SetPhaseNormal(stage);

        ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.016);
        ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.016);

        Assert.Equal(0, GetGame(stage).ExitRequests);
    }

    [Fact]
    public void OnUpdate_WhenInstallerCommittedWhileCrashPanelOpen_ShouldStillRequestExit()
    {
        // Controller ruling 2 regression: the terminal-state observation lives at the very top of
        // OnUpdate — above the Normal-phase input gate and above the crash notification's
        // frame-consuming early-return — so the exit is requested even with the panel open.
        var service = MockService(Snap(GameUpdateState.InstallerCommitted));
        var stage = CreateStage(new StubStageGame { GameUpdateService = service.Object });
        SetPhaseNormal(stage);
        var crashNotification = new CrashReportNotification(CreateInboxWithOneReport());
        ReflectionHelpers.SetPrivateField(stage, "_crashReportNotification", crashNotification);
        crashNotification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        Assert.True(crashNotification.IsOpen);

        ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.016);

        Assert.Equal(1, GetGame(stage).ExitRequests);
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private static GameUpdateSnapshot Snap(GameUpdateState state, int? percent = null) =>
        new(state, "1.2.3", "https://example.com/installer.exe", "sha256:" + new string('a', 64), null, percent);

    private static Mock<IGameUpdateService> MockService(GameUpdateSnapshot snapshot)
    {
        var mock = new Mock<IGameUpdateService>(MockBehavior.Loose);
        mock.Setup(x => x.GetSnapshot()).Returns(snapshot);
        return mock;
    }

    private static TitleStage CreateStage(StubStageGame game) => new(game);

    private static StubStageGame GetGame(TitleStage stage) =>
        (StubStageGame)ReflectionHelpers.GetPrivateField<IStageGame>(stage, "_game")!;

    private static void SetPhaseNormal(TitleStage stage)
    {
        ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
        var titlePhaseType = ReflectionHelpers.GetPrivateField<object>(stage, "_titlePhase")!.GetType();
        ReflectionHelpers.SetPrivateField(
            stage, "_titlePhase", Enum.Parse(titlePhaseType, "Normal"));
    }

    private static void InjectUpdateNotification(TitleStage stage, IGameUpdateService service)
    {
        ReflectionHelpers.SetPrivateField(
            stage, "_updateNotification", new GameUpdateNotification(service));
    }

    private static ICrashReportInbox CreateInboxWithOneReport()
    {
        var summary = new CrashReportSummary(
            ReportId: "report-1",
            CapturedAtUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            BuildId: "build-1",
            OperatingSystem: "macOS",
            ProcessArchitecture: "arm64",
            StageOrMilestone: "Title",
            ExceptionType: "System.InvalidOperationException",
            FileName: "report-1.txt");
        var mock = new Mock<ICrashReportInbox>(MockBehavior.Loose);
        mock.Setup(x => x.GetReports())
            .Returns(new List<CrashReportInboxItem> { new(summary, IsAcknowledged: false) });
        return mock.Object;
    }

    /// <summary>
    /// Hand-rolled <see cref="IStageGame"/> stub: default interface members (CrashReportInbox,
    /// GameUpdateService when not overridden) keep their DIM bodies, which Moq proxies would not do.
    /// </summary>
    private sealed class StubStageGame : IStageGame
    {
        public IGameUpdateService GameUpdateService { get; set; } = DisabledGameUpdateService.Instance;
        public InputManagerCompat InputManager { get; set; } = null!;

        public int ExitRequests { get; private set; }

        public void RequestExit() => ExitRequests++;

        public GraphicsDevice GraphicsDevice => null!;
        public IStageManager StageManager => null!;
        public IConfigManager ConfigManager => null!;
        public IGraphicsManager GraphicsManager => null!;
        public IResourceManager ResourceManager => null!;
        public ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;
        public bool CanPerformStageTransition() => true;
        public void MarkStageTransition()
        {
        }

        public Point? MapMouseToVirtual(Point windowPoint) => null;
        public ITextInputSource? GetTextInputSource() => null;
    }

    /// <summary>
    /// Headless <see cref="InputManagerCompat"/> stub (same pattern as TitleStageLogicTests).
    /// </summary>
    private sealed class StubInputManagerCompat : InputManagerCompat
    {
        private readonly HashSet<InputCommandType> _pressedCommands = new();
        private bool _backTriggered;

        public StubInputManagerCompat() : base(new ConfigManager(), new TestMidiDeviceBackend())
        {
        }

        public override bool IsBackActionTriggered() => _backTriggered;

        public override bool IsCommandPressed(InputCommandType command) => _pressedCommands.Contains(command);

        public void SetBackTriggered(bool value) => _backTriggered = value;

        public void SetPressedCommand(InputCommandType command) => _pressedCommands.Add(command);
    }
}
