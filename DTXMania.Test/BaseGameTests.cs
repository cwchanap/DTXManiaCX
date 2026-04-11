using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Graphics;
using DTXMania.Test.TestData;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;

namespace DTXMania.Test
{
    [Trait("Category", "Unit")]
    public class BaseGameTests
    {
        [Theory]
        [InlineData(0.1, 0.0, false)]
        [InlineData(GameConstants.StageTransition.DebounceDelaySeconds, 0.0, true)]
        [InlineData(2.0, 1.0, true)]
        public void CanPerformStageTransition_ShouldRespectDebounceThreshold(
            double totalGameTime,
            double lastStageTransitionTime,
            bool expected)
        {
            var game = ReflectionHelpers.CreateGame(totalGameTime, lastStageTransitionTime);

            Assert.Equal(expected, game.CanPerformStageTransition());
        }

        [Fact]
        public void MarkStageTransition_ShouldCaptureCurrentGameTime()
        {
            var game = ReflectionHelpers.CreateGame(totalGameTime: 3.25, lastStageTransitionTime: 0.0);

            game.MarkStageTransition();

            Assert.Equal(3.25, ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void QueueMainThreadAction_ShouldEnqueueAction()
        {
            var game = ReflectionHelpers.CreateGame();
            var context = (IGameContext)game;
            var queue = ReflectionHelpers.GetPrivateField<ConcurrentQueue<Action>>(game, "_mainThreadActions");
            var executed = false;

            context.QueueMainThreadAction(() => executed = true);

            Assert.True(queue.TryDequeue(out var action));
            action();
            Assert.True(executed);
        }

        [Fact]
        public void QueueMainThreadAction_WithNullAction_ShouldThrowArgumentNullException()
        {
            var game = ReflectionHelpers.CreateGame();
            var context = (IGameContext)game;

            Assert.Throws<ArgumentNullException>(() => context.QueueMainThreadAction(null!));
        }

        [Fact]
        public void CaptureScreenshotAsync_WhenNoPendingRequest_ShouldStorePendingTask()
        {
            var game = ReflectionHelpers.CreateGame();
            var context = (IGameContext)game;

            var task = context.CaptureScreenshotAsync();

            Assert.False(task.IsCompleted);
            var pendingScreenshot = ReflectionHelpers.GetPrivateField<TaskCompletionSource<byte[]?>>(game, "_pendingScreenshot");
            Assert.NotNull(pendingScreenshot);
            Assert.Same(pendingScreenshot!.Task, task);
        }

        [Fact]
        public async Task CaptureScreenshotAsync_WhenRequestAlreadyPending_ShouldReturnCompletedNullTask()
        {
            var game = ReflectionHelpers.CreateGame();
            var context = (IGameContext)game;

            _ = context.CaptureScreenshotAsync();
            var secondRequest = context.CaptureScreenshotAsync();

            Assert.True(secondRequest.IsCompletedSuccessfully);
            Assert.Null(await secondRequest);
        }

        [Fact]
        public void CaptureRenderTargetAsPng_WhenRenderTargetIsNull_ShouldReturnNull()
        {
            var method = typeof(BaseGame).GetMethod(
                "CaptureRenderTargetAsPng",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            var result = (byte[]?)method!.Invoke(null, new object?[] { null });

            Assert.Null(result);
        }

        [Fact]
        public async Task StartGameApiServerAsync_WhenServerOrCancellationAreMissing_ShouldCompleteWithoutThrowing()
        {
            var game = CreateGameForLifecycle();

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(game, "StartGameApiServerAsync")!;
            await task;

            Assert.True(task.IsCompletedSuccessfully);
        }

        [Fact]
        public void Draw_WhenGraphicsDeviceIsUnavailable_ShouldCompletePendingScreenshotWithNull()
        {
            var game = CreateGameForLifecycle();
            var pendingScreenshot = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReflectionHelpers.SetPrivateField(game, "_graphicsManager", new StubGraphicsManager(isDeviceAvailable: false, CreateFailingRenderTargetManager()));
            ReflectionHelpers.SetPrivateField(game, "_pendingScreenshot", pendingScreenshot);

            ReflectionHelpers.InvokePrivateMethod(game, "Draw", new GameTime());

            Assert.True(pendingScreenshot.Task.IsCompletedSuccessfully);
            Assert.Null(pendingScreenshot.Task.Result);
            Assert.Null(ReflectionHelpers.GetPrivateField<TaskCompletionSource<byte[]?>>(game, "_pendingScreenshot"));
        }

        [Fact]
        public void OnGraphicsSettingsChanged_WhenRenderTargetRecreationFails_ShouldUpdateConfigAndClearRenderTarget()
        {
            var config = new ConfigData { ScreenWidth = 640, ScreenHeight = 480, FullScreen = false, VSyncWait = true };
            var game = CreateGameForLifecycle(config);
            ReflectionHelpers.SetPrivateField(game, "_graphicsManager", new StubGraphicsManager(isDeviceAvailable: true, CreateFailingRenderTargetManager()));
            ReflectionHelpers.SetPrivateField(game, "_renderTarget", null);

            var newSettings = new GraphicsSettings
            {
                Width = 1920,
                Height = 1080,
                IsFullscreen = true,
                VSync = false
            };

            ReflectionHelpers.InvokePrivateMethod(
                game,
                "OnGraphicsSettingsChanged",
                null!,
                new GraphicsSettingsChangedEventArgs(new GraphicsSettings { Width = 640, Height = 480 }, newSettings));

            Assert.Equal(1920, config.ScreenWidth);
            Assert.Equal(1080, config.ScreenHeight);
            Assert.True(config.FullScreen);
            Assert.False(config.VSyncWait);
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(game, "_renderTarget"));
        }

        [Fact]
        public void OnGraphicsDeviceReset_WhenRenderTargetRecreationFails_ShouldClearRenderTarget()
        {
            var config = new ConfigData { ScreenWidth = 1280, ScreenHeight = 720 };
            var game = CreateGameForLifecycle(config);
            ReflectionHelpers.SetPrivateField(game, "_graphicsManager", new StubGraphicsManager(isDeviceAvailable: true, CreateFailingRenderTargetManager()));
            ReflectionHelpers.SetPrivateField(game, "_renderTarget", null);

            var ex = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(game, "OnGraphicsDeviceReset", null!, EventArgs.Empty));

            Assert.Null(ex);
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(game, "_renderTarget"));
        }

        [Fact]
        public void OnGraphicsDeviceLost_ShouldReturnWithoutThrowing()
        {
            var game = CreateGameForLifecycle();

            var ex = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(game, "OnGraphicsDeviceLost", null!, EventArgs.Empty));

            Assert.Null(ex);
        }

        private static BaseGame CreateGameForLifecycle(ConfigData? config = null)
        {
            var game = ReflectionHelpers.CreateGame();
            var loggerFactory = LoggerFactory.Create(builder => { });

            ReflectionHelpers.SetPrivateField(game, "_loggerFactory", loggerFactory);
            ReflectionHelpers.SetPrivateField(game, "_logger", loggerFactory.CreateLogger<BaseGame>());
            ReflectionHelpers.SetPrivateField(game, "<ConfigManager>k__BackingField", CreateConfigManager(config ?? new ConfigData()));

            return game;
        }

        private static IConfigManager CreateConfigManager(ConfigData config)
        {
            var configManager = new Mock<IConfigManager>();
            configManager.SetupGet(manager => manager.Config).Returns(config);
            return configManager.Object;
        }

        private static RenderTargetManager CreateFailingRenderTargetManager()
        {
#pragma warning disable SYSLIB0050
            var graphicsDevice = (GraphicsDevice)FormatterServices.GetUninitializedObject(typeof(GraphicsDevice));
#pragma warning restore SYSLIB0050
            return new RenderTargetManager(graphicsDevice);
        }

        private sealed class StubGraphicsManager : IGraphicsManager
        {
            public StubGraphicsManager(bool isDeviceAvailable, RenderTargetManager renderTargetManager)
            {
                IsDeviceAvailable = isDeviceAvailable;
                RenderTargetManager = renderTargetManager;
            }

            public GraphicsDevice GraphicsDevice => null!;
            public GraphicsSettings Settings => new();
            public bool IsDeviceAvailable { get; }
            public RenderTargetManager RenderTargetManager { get; }
            public bool DisposeCalled { get; private set; }

            public event EventHandler<GraphicsSettingsChangedEventArgs>? SettingsChanged;
            public event EventHandler? DeviceLost;
            public event EventHandler? DeviceReset;

            public void Initialize()
            {
            }

            public bool ApplySettings(GraphicsSettings settings) => false;
            public bool ChangeResolution(int width, int height) => false;
            public bool ToggleFullscreen() => false;
            public bool SetFullscreen(bool fullscreen) => false;
            public bool SetVSync(bool vsync) => false;
            public DisplayMode[] GetAvailableDisplayModes() => Array.Empty<DisplayMode>();
            public bool IsResolutionSupported(int width, int height) => false;
            public bool ResetDevice() => false;

            public void Dispose()
            {
                DisposeCalled = true;
            }
        }
    }
}
