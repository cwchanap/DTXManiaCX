using System;
using System.Collections.Concurrent;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Graphics;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI.Components;
using DTXMania.Test.TestData;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class IStageGameContractTests
    {
        private const string IStageGameFullName = "DTXMania.Game.Lib.Stage.IStageGame";

        [Fact]
        public void BaseGame_ShouldImplementIStageGame()
        {
            var iface = typeof(BaseGame).GetInterface(IStageGameFullName);
            Assert.NotNull(iface);
        }

        [Fact]
        public void IStageGame_ShouldDeclareMapMouseToVirtualAndGetTextInputSourceAndRequestExit()
        {
            var iface = typeof(BaseGame).GetInterface(IStageGameFullName)!;
            Assert.NotNull(iface.GetMethod("MapMouseToVirtual"));
            Assert.NotNull(iface.GetMethod("GetTextInputSource"));
            Assert.NotNull(iface.GetMethod("RequestExit"));
        }

        [Fact]
        public void GetTextInputSource_ShouldReturnNull_InHeadlessEnvironment()
        {
            // ReflectionHelpers.CreateGame builds an uninitialized BaseGame with no graphics
            // manager (and thus no OS window), modeling the headless/test environment the
            // search modal must tolerate.
            var game = ReflectionHelpers.CreateGame();
            Assert.Null(game.GetTextInputSource());
        }

        [Fact]
        public void GetTextInputSource_WithGraphicsManagerButNoWindow_ShouldReturnNull()
        {
            // When _graphicsManager is set (non-headless) but no OS window is available,
            // GetTextInputSource should reach the window lookup and return null rather than
            // throwing. The test subclass overrides the GetGameWindow seam to avoid touching
            // the MonoGame Game.Window getter on an uninitialized instance.
            var game = CreateTextInputSpyGame(window: null);
            ReflectionHelpers.SetPrivateField(game, "_graphicsManager", new Mock<IGraphicsManager>().Object);

            var result = game.GetTextInputSource();

            Assert.Null(result);
        }

        [Fact]
        public void GetTextInputSource_WithGraphicsManagerAndWindow_ShouldReturnTextInputSource()
        {
            // When both _graphicsManager and a GameWindow are available, GetTextInputSource
            // should return a live WindowTextInputSource wrapping the window. Moq generates a
            // concrete proxy for the abstract GameWindow so the TextInput event hook works.
            var window = new Mock<GameWindow>().Object;
            var game = CreateTextInputSpyGame(window: window);
            ReflectionHelpers.SetPrivateField(game, "_graphicsManager", new Mock<IGraphicsManager>().Object);

            var result = game.GetTextInputSource();

            Assert.NotNull(result);
            Assert.IsType<WindowTextInputSource>(result);
            result!.Dispose();
        }

        [Fact]
        public void RequestExit_ShouldNotThrow_OnUninitializedGame()
        {
            // RequestExit forwards to Game.Exit(). On an uninitialized BaseGame the MonoGame
            // platform layer is null, so Exit's null-platform guard prevents dispatch and the
            // call should be a no-op rather than throwing.
            var game = ReflectionHelpers.CreateGame();
            var exception = Record.Exception(() => game.RequestExit());
            Assert.Null(exception);
        }

        [Fact]
        public void IStageGame_MapMouseToVirtual_ShouldForwardToInternalImplementation()
        {
            // The explicit IStageGame.MapMouseToVirtual implementation must forward to the
            // internal MapMouseToVirtual method. In headless mode (no _graphicsManager) the
            // internal method returns the input point unchanged (1:1 identity mapping).
            var game = ReflectionHelpers.CreateGame();
            var stageGame = (IStageGame)game;

            var result = stageGame.MapMouseToVirtual(new Point(300, 200));

            Assert.Equal(new Point(300, 200), result);
        }

        [Fact]
        public void IStageGame_DefaultStartupReportMethods_ShouldBeNoOps_WhenImplementationDoesNotOverrideThem()
        {
            // The three startup-report methods are default interface methods (DIMs) with empty
            // bodies, acting as optional hooks. An implementation that does not override them
            // must still be able to call them through the interface without throwing.
            IStageGame stageGame = new MinimalStageGameStub();

            var exception = Record.Exception(() =>
            {
                stageGame.ReportStartupActivated();
                stageGame.ReportStartupFrameRendered();
                stageGame.ReportStartupSummaryAndTitleRequested();
            });

            Assert.Null(exception);
        }

        [Fact]
        public void IStageGame_CrashReportInbox_ShouldReturnEmptyFacade_WhenImplementationDoesNotOverrideIt()
        {
            // CrashReportInbox is a default interface member whose body returns the null-object
            // facade. An IStageGame stub that does NOT override it (like MinimalStageGameStub
            // below) must compile and observe the facade without modification — this is what
            // keeps the existing test stubs valid for the new property.
            IStageGame stageGame = new MinimalStageGameStub();

            Assert.Same(EmptyCrashReportInbox.Instance, stageGame.CrashReportInbox);
        }

        [Fact]
        public async Task IStageGame_DefaultCaptureScreenshotAsync_ShouldReturnNull_WhenImplementationDoesNotOverrideIt()
        {
            // CaptureScreenshotAsync is a default interface member whose body returns a
            // synchronously completed null task, so existing IStageGame implementations and
            // test stubs remain valid without modification. The concrete BaseGame overrides
            // it to forward to the shared capture queue (pinned in BaseGameTests).
            IStageGame stageGame = new MinimalStageGameStub();

            var result = await stageGame.CaptureScreenshotAsync();

            Assert.Null(result);
        }

        /// <summary>
        /// Minimal <see cref="IStageGame"/> implementation that leaves the three startup-report
        /// default interface methods unoverridden, so the DIM bodies can be exercised.
        /// </summary>
        private sealed class MinimalStageGameStub : IStageGame
        {
            public GraphicsDevice GraphicsDevice => null!;
            public IStageManager StageManager => null!;
            public IConfigManager ConfigManager => null!;
            public InputManagerCompat InputManager => null!;
            public IGraphicsManager GraphicsManager => null!;
            public IResourceManager ResourceManager => null!;
            public ILoggerFactory LoggerFactory => null!;
            public bool CanPerformStageTransition() => false;
            public void MarkStageTransition() { }
            public Point? MapMouseToVirtual(Point windowPoint) => windowPoint;
            public ITextInputSource? GetTextInputSource() => null;
            public void RequestExit() { }
        }

        /// <summary>
        /// Test-only <see cref="BaseGame"/> that overrides the <see cref="BaseGame.GetGameWindow"/>
        /// seam with a controllable <see cref="GameWindow"/>, so <see cref="BaseGame.GetTextInputSource"/>
        /// can be exercised without a live OS window.
        /// </summary>
        private sealed class TextInputSpyGame : BaseGame
        {
            private TextInputSpyGame(
                StartupTimingTrace startupTimingTrace,
                IGameCrashDiagnostics crashDiagnostics)
                : base(startupTimingTrace, crashDiagnostics)
            {
            }

            private readonly GameWindow? _window;

            protected override GameWindow? GetGameWindow() => _window;
        }

        private static TextInputSpyGame CreateTextInputSpyGame(GameWindow? window)
        {
            var game = ReflectionHelpers.CreateUninitialized<TextInputSpyGame>();
            ReflectionHelpers.SetPrivateField(game, "_mainThreadActions", new ConcurrentQueue<Action>());
            // CreateUninitialized bypasses the constructor, so set the private field directly.
            ReflectionHelpers.SetPrivateField(game, "_window", window);
            return game;
        }
    }
}
