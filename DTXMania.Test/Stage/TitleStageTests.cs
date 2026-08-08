using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Input.Midi;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Moq;
using System;
using System.Collections.Generic;
using Xunit;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Unit tests for TitleStage menu functionality
    /// Tests menu navigation, input handling, and sound integration
    /// </summary>
    public class TitleStageTests
    {


        [Fact]
        public void MenuNavigation_UpFromFirst_ShouldWrapToLast()
        {
            // Arrange
            var currentIndex = 0;
            var menuLength = 3;

            // Act - Simulate menu wrapping logic
            var newIndex = currentIndex > 0 ? currentIndex - 1 : menuLength - 1;

            // Assert
            Assert.Equal(2, newIndex); // Should wrap to last item
        }

        [Fact]
        public void MenuNavigation_DownFromLast_ShouldWrapToFirst()
        {
            // Arrange
            var currentIndex = 2;
            var menuLength = 3;

            // Act - Simulate menu wrapping logic
            var newIndex = currentIndex < menuLength - 1 ? currentIndex + 1 : 0;

            // Assert
            Assert.Equal(0, newIndex); // Should wrap to first item
        }





        [Fact]
        public void MouseHitTest_ShouldDetectMenuItemCollision()
        {
            // Arrange
            var menuX = 506;
            var menuY = 513;
            var menuItemWidth = 227;
            var menuItemHeight = 39;
            var mouseX = 600; // Within menu bounds
            var mouseY = 530; // Within first menu item

            // Act
            var menuItemRect = new Rectangle(menuX, menuY, menuItemWidth, menuItemHeight);
            var mousePoint = new Point(mouseX, mouseY);
            var isHit = menuItemRect.Contains(mousePoint);

            // Assert
            Assert.True(isHit);
        }

        [Fact]
        public void MouseHitTest_ShouldRejectOutsideMenuBounds()
        {
            // Arrange
            var menuX = 506;
            var menuY = 513;
            var menuItemWidth = 227;
            var menuItemHeight = 39;
            var mouseX = 400; // Outside menu bounds
            var mouseY = 530;

            // Act
            var menuItemRect = new Rectangle(menuX, menuY, menuItemWidth, menuItemHeight);
            var mousePoint = new Point(mouseX, mouseY);
            var isHit = menuItemRect.Contains(mousePoint);

            // Assert
            Assert.False(isHit);
        }

        [Fact]
        public void IsMenuSelectTriggered_WhenActivatePressed_ShouldReturnTrue()
        {
            var inputManager = new TestInputManager
            {
                ActivatePressed = true,
            };

            Assert.True(TitleStage.IsMenuSelectTriggered(inputManager));
        }

        [Fact]
        public void IsMenuSelectTriggered_WhenSpacePressedWithoutActivateMapping_ShouldReturnFalse()
        {
            var inputManager = new TestInputManager();
            inputManager.SetPressedKey(Keys.Space);

            Assert.False(TitleStage.IsMenuSelectTriggered(inputManager));
        }

        [Fact]
        public void IsMenuSelectTriggered_WhenNonActivateCommandAndSpacePressed_ShouldReturnFalse()
        {
            var inputManager = new TestInputManager();
            inputManager.SetPressedCommand(InputCommandType.MoveDown);
            inputManager.SetPressedKey(Keys.Space);

            Assert.False(TitleStage.IsMenuSelectTriggered(inputManager));
        }

        [Fact]
        public void IsMenuSelectTriggered_WhenInputManagerIsNull_ShouldReturnFalse()
        {
            Assert.False(TitleStage.IsMenuSelectTriggered(null));
        }

        private sealed class TestInputManager : IInputManager
        {
            private readonly HashSet<int> _pressedKeys = new();
            private readonly HashSet<InputCommandType> _pressedCommands = new();

            public bool ActivatePressed { get; set; }

            public bool HasPendingCommands => false;

            public void Dispose()
            {
            }

            public InputCommand? GetNextCommand() => null;

            public bool IsBackActionTriggered() => false;

            public bool IsCommandPressed(InputCommandType commandType)
                => (commandType == InputCommandType.Activate && ActivatePressed)
                    || _pressedCommands.Contains(commandType);

            public bool IsKeyDown(int keyCode) => false;

            public bool IsKeyPressed(int keyCode) => _pressedKeys.Contains(keyCode);

            public bool IsKeyReleased(int keyCode) => false;

            public bool IsKeyTriggered(int keyCode) => IsKeyPressed(keyCode);

            public void SetPressedKey(Keys key)
            {
                _pressedKeys.Add((int)key);
            }

            public void SetPressedCommand(InputCommandType commandType)
            {
                _pressedCommands.Add(commandType);
            }

            public void Update(double deltaTime)
            {
            }
        }

        // -----------------------------------------------------------------------------------------
        // Task 4 (HPA-530): thin crash-report notification leakage regression.
        //
        // These three tests prove ONLY that the notification's consumed input cannot leak into the
        // title's menu/exit path. Detailed notification state lives in CrashReportNotificationTests.
        // They follow the established TitleStage test pattern: a testable TitleStage subclass +
        // reflection-set fields, exercised without a graphics device.
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void CrashNotification_WhenConsuming_ShouldSkipGameStartConfigAndExit()
        {
            // Panel open + Activate (the entry into GAME START / CONFIG / EXIT). Because the
            // notification consumes, the title's HandleInput() must never run, so no stage
            // transition is requested.
            var game = ReflectionHelpers.CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateTitleStageWithOpenNotification(game, out var stageManager);
            var input = new StubInputManagerCompat();
            input.SetPressedCommand(InputCommandType.Activate);
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), input);
            ReflectionHelpers.SetPrivateField(stage, "_currentMenuIndex", 0); // GAME START

            stage.InvokeOnUpdate(0.016);

            stageManager.Verify(
                x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>()),
                Times.Never);
            // No MarkStageTransition -> exit/select path was not entered either.
            Assert.Equal(0.0, ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void CrashNotification_WhenPanelOpenAndBackPressed_ShouldNotCallRequestExit()
        {
            // Open-panel Back/Escape closes the panel and must NEVER reach the title's Back path,
            // which calls MarkStageTransition() + RequestExit() in the same frame. MarkStageTransition
            // advances _lastStageTransitionTime to _totalGameTime; if it stays unchanged the exit
            // path was unreachable. (RequestExit on the uninitialized test BaseGame is a no-op, so
            // _lastStageTransitionTime is the observable proof.)
            var game = ReflectionHelpers.CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateTitleStageWithOpenNotification(game, out _);
            var input = new StubInputManagerCompat();
            input.SetBackTriggered(true);
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), input);

            stage.InvokeOnUpdate(0.016);

            Assert.Equal(0.0, ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void CrashNotification_WhenClosedAndNotConsuming_ShouldStillReachTitleMenu()
        {
            // Empty inbox -> no banner, nothing to consume -> the existing title menu path still
            // runs. Activate on CONFIG requests the Config stage transition as before.
            var game = ReflectionHelpers.CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stageManager = new Mock<IStageManager>();
            var stage = new TestableTitleStage(game)
            {
                StageManager = stageManager.Object
            };
            PutStageInNormalPhase(stage);
            ReflectionHelpers.SetPrivateField(
                stage,
                "_crashReportNotification",
                new CrashReportNotification(EmptyCrashReportInbox.Instance));

            var input = new StubInputManagerCompat();
            input.SetPressedCommand(InputCommandType.Activate);
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), input);
            ReflectionHelpers.SetPrivateField(stage, "_currentMenuIndex", 1); // CONFIG

            stage.InvokeOnUpdate(0.016);

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.Config,
                    It.Is<IStageTransition>(t => t is CrossfadeTransition)),
                Times.Once);
        }

        private static TestableTitleStage CreateTitleStageWithOpenNotification(
            BaseGame game,
            out Mock<IStageManager> stageManager)
        {
            stageManager = new Mock<IStageManager>();
            var stage = new TestableTitleStage(game)
            {
                StageManager = stageManager.Object
            };
            PutStageInNormalPhase(stage);

            // Build a real notification around a fake inbox with one pending report, then pre-open
            // its panel with an F8 edge so the guard consumes on the next OnUpdate frame.
            var notification = new CrashReportNotification(new SpyCrashInbox(
                new CrashReportInboxItem(
                    new CrashReportSummary(
                        "crash-1",
                        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                        "build-1",
                        "macOS",
                        "arm64",
                        "Performance",
                        "System.InvalidOperationException",
                        "crash-1.txt"),
                    IsAcknowledged: false)));
            Assert.True(notification.HandleInput(
                new KeyboardState(Keys.F8), default, inputManager: null, virtualMouse: null, leftMouseClick: false));
            Assert.True(notification.IsOpen);

            ReflectionHelpers.SetPrivateField(stage, "_crashReportNotification", notification);
            return stage;
        }

        private static void PutStageInNormalPhase(TitleStage stage)
        {
            var titlePhaseType = ReflectionHelpers
                .GetPrivateField<object>(stage, "_titlePhase")!
                .GetType();
            ReflectionHelpers.SetPrivateField(
                stage,
                "_titlePhase",
                Enum.Parse(titlePhaseType, "Normal"));
            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
        }

        /// <summary>
        /// TitleStage subclass that exposes the protected <see cref="TitleStage.OnUpdate"/> for the
        /// leakage regression, mirroring the ControlledTitleStage seam already used by the title
        /// critical-path tests.
        /// </summary>
        private sealed class TestableTitleStage : TitleStage
        {
            public TestableTitleStage(IStageGame game) : base(game)
            {
            }

            public void InvokeOnUpdate(double deltaTime) => OnUpdate(deltaTime);
        }

        /// <summary>
        /// InputManagerCompat stub whose Back-action and command presses are controllable, so the
        /// notification guard and the title HandleInput path can be driven deterministically without
        /// hardware. Matches the StubInputManagerCompat already used by TitleStageLogicTests.
        /// </summary>
        private sealed class StubInputManagerCompat : InputManagerCompat
        {
            private readonly HashSet<InputCommandType> _pressedCommands = new();
            private bool _backTriggered;

            public StubInputManagerCompat() : base(new ConfigManager(), new TestMidiDeviceBackend())
            {
            }

            public override bool IsBackActionTriggered() => _backTriggered;

            public override bool IsCommandPressed(InputCommandType command) =>
                _pressedCommands.Contains(command);

            public void SetBackTriggered(bool value) => _backTriggered = value;

            public void SetPressedCommand(InputCommandType command) => _pressedCommands.Add(command);
        }

        /// <summary>
        /// Minimal fake inbox: returns one fixed pending item and reports success for every action
        /// without touching the filesystem or a process. The leakage test only needs the panel to
        /// open and stay open; action outcomes are irrelevant here.
        /// </summary>
        private sealed class SpyCrashInbox : ICrashReportInbox
        {
            private readonly CrashReportInboxItem[] _reports;

            public SpyCrashInbox(params CrashReportInboxItem[] reports)
            {
                _reports = reports ?? Array.Empty<CrashReportInboxItem>();
            }

            public IReadOnlyList<CrashReportInboxItem> GetReports() => _reports;
            public CrashReportActionResult OpenGitHubIssue(string reportId) => new(Succeeded: true);
            public CrashReportActionResult OpenReportFolder(string reportId) => new(Succeeded: true);
            public CrashReportActionResult Dismiss(string reportId) => new(Succeeded: true);
            public CrashReportActionResult Delete(string reportId) => new(Succeeded: true);
        }


    }
}
