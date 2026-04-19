using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Graphics;
using DTXMania.Game.Lib.JsonRpc;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
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
        public void GameContextAccessors_ShouldExposeAssignedManagers()
        {
            var configManager = CreateConfigManager(new ConfigData());
            var inputManager = new InputManagerCompat(new ConfigManager());
            var graphicsManager = new StubGraphicsManager(isDeviceAvailable: true, CreateFailingRenderTargetManager());
            var game = ReflectionHelpers.CreateGame();
            var context = (IGameContext)game;

            ReflectionHelpers.SetPrivateField(game, "<ConfigManager>k__BackingField", configManager);
            ReflectionHelpers.SetPrivateField(game, "<InputManager>k__BackingField", inputManager);
            ReflectionHelpers.SetPrivateField(game, "_graphicsManager", graphicsManager);

            Assert.Same(configManager, game.ConfigManager);
            Assert.Same(inputManager, context.InputManager);
            Assert.Same(graphicsManager, game.GraphicsManager);
            Assert.Same(graphicsManager, context.GraphicsManager);
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
        public void DisposeManagedResources_WhenGameApiStartupAlreadyCompleted_ShouldClearStartupTaskAndStopServer()
        {
            var game = CreateGameForLifecycle();
            var host = new Mock<IHost>();
            host.Setup(value => value.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var cancellation = new CancellationTokenSource();
            var server = CreateRunningJsonRpcServer(host, cancellation);

            ReflectionHelpers.SetPrivateField(game, "_graphicsManager", new StubGraphicsManager(isDeviceAvailable: true, CreateFailingRenderTargetManager()));
            ReflectionHelpers.SetPrivateField(game, "_jsonRpcServer", server);
            ReflectionHelpers.SetPrivateField(game, "_gameApiCancellation", cancellation);
            ReflectionHelpers.SetPrivateField(game, "_gameApiStartTask", Task.CompletedTask);

            ReflectionHelpers.InvokePrivateMethod(game, "DisposeManagedResources");

            host.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
            host.Verify(value => value.Dispose(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(game, "_gameApiStartTask"));
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(game, "_jsonRpcServer"));
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(game, "_gameApiCancellation"));
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
        public async Task StartGameApiServerAsync_WhenCancellationAlreadyRequested_ShouldSwallowOperationCanceledException()
        {
            var game = CreateGameForLifecycle(new ConfigData { GameApiPort = 12345 });
            var gameApi = new Mock<IGameApi>();
            gameApi.SetupGet(api => api.IsRunning).Returns(true);
            using var server = new JsonRpcServer(gameApi.Object, port: 12345);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            ReflectionHelpers.SetPrivateField(game, "_jsonRpcServer", server);
            ReflectionHelpers.SetPrivateField(game, "_gameApiCancellation", cancellation);

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(game, "StartGameApiServerAsync")!;
            await task;

            Assert.True(task.IsCompletedSuccessfully);
            Assert.False(server.IsRunning);
        }

        [Fact]
        public async Task StartGameApiServerAsync_WhenServerStartThrows_ShouldSwallowException()
        {
            var game = CreateGameForLifecycle(new ConfigData { GameApiPort = 12346 });
            var gameApi = new Mock<IGameApi>();
            gameApi.SetupGet(api => api.IsRunning).Returns(true);
            var server = new JsonRpcServer(gameApi.Object, port: 12346);
            server.Dispose();
            using var cancellation = new CancellationTokenSource();

            ReflectionHelpers.SetPrivateField(game, "_jsonRpcServer", server);
            ReflectionHelpers.SetPrivateField(game, "_gameApiCancellation", cancellation);

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(game, "StartGameApiServerAsync")!;
            await task;

            Assert.True(task.IsCompletedSuccessfully);
            Assert.False(server.IsRunning);
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

        [Fact]
        public void ApplySavedSystemKeyBindings_WhenConfigManagerIsConcrete_ShouldLoadPersistedBindings()
        {
            var configManager = new ConfigManager();
            configManager.Config.SystemKeyBindings["SystemKey.Activate"] = Keys.F1.ToString();
            var inputManager = new InputManagerCompat(configManager);
            var game = ReflectionHelpers.CreateGame();
            using var loggerFactory = LoggerFactory.Create(builder => { });

            ReflectionHelpers.SetPrivateField(game, "_loggerFactory", loggerFactory);
            ReflectionHelpers.SetPrivateField(game, "_logger", loggerFactory.CreateLogger<BaseGame>());
            ReflectionHelpers.SetPrivateField(game, "<ConfigManager>k__BackingField", configManager);
            ReflectionHelpers.SetPrivateField(game, "<InputManager>k__BackingField", inputManager);

            ReflectionHelpers.InvokePrivateMethod(game, "ApplySavedSystemKeyBindings");

            var snapshot = inputManager.GetKeyMappingSnapshot();
            Assert.Equal(InputCommandType.Activate, snapshot[Keys.F1]);
            Assert.DoesNotContain(Keys.Enter, snapshot.Keys);
        }

        [Fact]
        public void ApplySavedSystemKeyBindings_WhenConfigManagerIsInterfaceOnly_ShouldDoNothing()
        {
            var game = CreateGameForLifecycle();

            var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(game, "ApplySavedSystemKeyBindings"));

            Assert.Null(exception);
        }

        [Theory]
        [InlineData(false, "ignored", "API disabled")]
        [InlineData(true, null, "API key null")]
        [InlineData(true, "", "API key empty")]
        [InlineData(true, "   ", "API key whitespace")]
        public void TryInitializeGameApi_WhenConfigurationIsInvalid_ShouldLeaveServerStateUnset(
            bool enableGameApi, string? gameApiKey, string testCase)
        {
            var config = new ConfigData { EnableGameApi = enableGameApi, GameApiKey = gameApiKey!, GameApiPort = 12345 };
            var game = CreateGameForLifecycle(config);

            var initialized = Assert.IsType<bool>(ReflectionHelpers.InvokePrivateMethod(game, "TryInitializeGameApi", config));

            Assert.False(initialized);
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(game, "_gameApiImplementation"));
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(game, "_jsonRpcServer"));
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(game, "_gameApiCancellation"));
        }

        [Fact]
        public void LoadContent_WhenGameApiIsEnabledWithApiKey_ShouldInitializeLifecycleServicesAndQueueApiStartup()
        {
            var config = new ConfigData
            {
                EnableGameApi = true,
                GameApiKey = "secret-key",
                GameApiPort = 12345,
                UseBoxDefSkin = true,
                SkinPath = "Skins/Test"
            };
            var resourceManager = new Mock<IResourceManager>();
            var stageManager = new Mock<IStageManager>();
            var spriteBatch = ReflectionHelpers.CreateUninitialized<SpriteBatch>();
            var startupTask = Task.CompletedTask;
            var game = CreateGameForLoadContent(config, resourceManager.Object, stageManager.Object, spriteBatch);
            game.StartGameApiServerTask = startupTask;

            game.InvokeLoadContent();

            Assert.Same(spriteBatch, ReflectionHelpers.GetPrivateField<object>(game, "_spriteBatch"));
            Assert.Same(resourceManager.Object, game.ResourceManager);
            resourceManager.Verify(value => value.SetUseBoxDefSkin(true), Times.Once);
            resourceManager.Verify(value => value.SetSkinPath("Skins/Test"), Times.Once);
            Assert.Same(stageManager.Object, game.StageManager);
            stageManager.Verify(value => value.ChangeStage(StageType.Startup), Times.Once);
            Assert.Equal(1, game.CreateSpriteBatchCallCount);
            Assert.Equal(1, game.CreateResourceManagerCallCount);
            Assert.Equal(1, game.InitializeManagedFontFactoryCallCount);
            Assert.Equal(1, game.StartGameApiServerCallCount);
            Assert.Same(startupTask, ReflectionHelpers.GetPrivateField<Task>(game, "_gameApiStartTask"));
            Assert.IsType<GameApiImplementation>(ReflectionHelpers.GetPrivateField<object>(game, "_gameApiImplementation"));
            Assert.IsType<JsonRpcServer>(ReflectionHelpers.GetPrivateField<object>(game, "_jsonRpcServer"));

            var cancellation = ReflectionHelpers.GetPrivateField<CancellationTokenSource>(game, "_gameApiCancellation");
            Assert.NotNull(cancellation);
            Assert.False(cancellation!.IsCancellationRequested);

            cancellation.Dispose();
            (ReflectionHelpers.GetPrivateField<object>(game, "_jsonRpcServer") as IDisposable)?.Dispose();
        }

        [Fact]
        public void Update_WhenFullscreenToggleRequested_ShouldToggleFullscreenUpdateStageAndDrainQueuedActions()
        {
            var stageManager = new Mock<IStageManager>();
            var inputManager = new TrackingInputManager();
            var graphicsManager = new StubGraphicsManager(isDeviceAvailable: true, CreateFailingRenderTargetManager());
            var game = CreateGameForUpdate(inputManager, stageManager.Object, graphicsManager);
            var context = (IGameContext)game;
            var callOrder = new List<string>();

            game.ShouldToggleFullscreenResult = true;
            inputManager.OnUpdate = _ => callOrder.Add("input");
            graphicsManager.OnToggleFullscreen = () => callOrder.Add("toggle");
            stageManager.Setup(value => value.Update(It.IsAny<double>()))
                .Callback<double>(_ => callOrder.Add("stage"));
            context.QueueMainThreadAction(() => callOrder.Add("action"));

            ReflectionHelpers.InvokePrivateMethod(
                game,
                "Update",
                new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(16)));

            Assert.True(game.ShouldToggleFullscreenCalled);
            Assert.Equal(1, game.CompleteBaseUpdateCallCount);
            Assert.Equal(1, inputManager.UpdateCallCount);
            Assert.Equal(1, graphicsManager.ToggleFullscreenCallCount);
            stageManager.Verify(value => value.Update(0.016), Times.Once);
            Assert.Equal(new[] { "input", "toggle", "stage", "action" }, callOrder);
            Assert.Equal(0.016, ReflectionHelpers.GetPrivateField<double>(game, "_totalGameTime"));
            Assert.True(ReflectionHelpers.GetPrivateField<ConcurrentQueue<Action>>(game, "_mainThreadActions")!.IsEmpty);
        }

        [Fact]
        public void DrainMainThreadActions_WhenActionsAreQueued_ShouldExecuteThemInOrder()
        {
            var game = CreateGameForLifecycle();
            var context = (IGameContext)game;
            var executionOrder = new List<int>();

            context.QueueMainThreadAction(() => executionOrder.Add(1));
            context.QueueMainThreadAction(() => executionOrder.Add(2));
            context.QueueMainThreadAction(() => executionOrder.Add(3));

            ReflectionHelpers.InvokePrivateMethod(game, "DrainMainThreadActions");

            Assert.Equal(new[] { 1, 2, 3 }, executionOrder);
            Assert.True(ReflectionHelpers.GetPrivateField<ConcurrentQueue<Action>>(game, "_mainThreadActions")!.IsEmpty);
        }

        [Fact]
        public void DrainMainThreadActions_WhenActionThrows_ShouldContinueProcessingRemainingActions()
        {
            var game = CreateGameForLifecycle();
            var context = (IGameContext)game;
            var executionOrder = new List<int>();

            context.QueueMainThreadAction(() => executionOrder.Add(1));
            context.QueueMainThreadAction(() => throw new InvalidOperationException("boom"));
            context.QueueMainThreadAction(() => executionOrder.Add(3));

            ReflectionHelpers.InvokePrivateMethod(game, "DrainMainThreadActions");

            Assert.Equal(new[] { 1, 3 }, executionOrder);
            Assert.True(ReflectionHelpers.GetPrivateField<ConcurrentQueue<Action>>(game, "_mainThreadActions")!.IsEmpty);
        }

        [Fact]
        public void DrainMainThreadActions_WhenMoreThanSixtyFourActionsAreQueued_ShouldLeaveTheRemainderForNextFrame()
        {
            var game = CreateGameForLifecycle();
            var context = (IGameContext)game;
            var executed = 0;

            for (var i = 0; i < 70; i++)
            {
                context.QueueMainThreadAction(() => executed++);
            }

            ReflectionHelpers.InvokePrivateMethod(game, "DrainMainThreadActions");

            Assert.Equal(64, executed);
            Assert.Equal(6, ReflectionHelpers.GetPrivateField<ConcurrentQueue<Action>>(game, "_mainThreadActions")!.Count);
        }

        [Fact]
        public void OnGraphicsSettingsChanged_WhenRenderTargetRecreationSucceeds_ShouldReplaceRenderTarget()
        {
            var config = new ConfigData { ScreenWidth = 640, ScreenHeight = 480, FullScreen = false, VSyncWait = true };
            var fakeRenderTarget = ReflectionHelpers.CreateUninitialized<RenderTarget2D>();
            var renderTargetManager = new RecordingRenderTargetManager(fakeRenderTarget);
            var game = CreateGameForLifecycle(config);

            ReflectionHelpers.SetPrivateField(game, "_graphicsManager", new StubGraphicsManager(isDeviceAvailable: true, renderTargetManager));
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
            Assert.Same(fakeRenderTarget, ReflectionHelpers.GetPrivateField<object>(game, "_renderTarget"));
            Assert.Equal(("MainRenderTarget", 1920, 1080), renderTargetManager.LastRequest);
        }

        [Fact]
        public void OnGraphicsSettingsChanged_WhenPreviousRenderTargetExists_ShouldDisposeItBeforeReplacingTarget()
        {
            var config = new ConfigData { ScreenWidth = 640, ScreenHeight = 480, FullScreen = false, VSyncWait = true };
            var previousRenderTarget = ReflectionHelpers.CreateUninitialized<TrackableRenderTarget>();
            var replacementRenderTarget = ReflectionHelpers.CreateUninitialized<RenderTarget2D>();
            var renderTargetManager = new RecordingRenderTargetManager(replacementRenderTarget);
            var game = CreateGameForLifecycle(config);

            ReflectionHelpers.SetPrivateField(game, "_graphicsManager", new StubGraphicsManager(isDeviceAvailable: true, renderTargetManager));
            ReflectionHelpers.SetPrivateField(game, "_renderTarget", previousRenderTarget);

            ReflectionHelpers.InvokePrivateMethod(
                game,
                "OnGraphicsSettingsChanged",
                null!,
                new GraphicsSettingsChangedEventArgs(
                    new GraphicsSettings { Width = 640, Height = 480 },
                    new GraphicsSettings { Width = 1024, Height = 768 }));

            Assert.True(previousRenderTarget.DisposeCalled);
            Assert.Same(replacementRenderTarget, ReflectionHelpers.GetPrivateField<object>(game, "_renderTarget"));
        }

        [Fact]
        public void OnGraphicsDeviceReset_WhenRenderTargetRecreationSucceeds_ShouldReplaceRenderTarget()
        {
            var config = new ConfigData { ScreenWidth = 1280, ScreenHeight = 720 };
            var fakeRenderTarget = ReflectionHelpers.CreateUninitialized<RenderTarget2D>();
            var renderTargetManager = new RecordingRenderTargetManager(fakeRenderTarget);
            var game = CreateGameForLifecycle(config);

            ReflectionHelpers.SetPrivateField(game, "_graphicsManager", new StubGraphicsManager(isDeviceAvailable: true, renderTargetManager));
            ReflectionHelpers.SetPrivateField(game, "_renderTarget", null);

            ReflectionHelpers.InvokePrivateMethod(game, "OnGraphicsDeviceReset", null!, EventArgs.Empty);

            Assert.Same(fakeRenderTarget, ReflectionHelpers.GetPrivateField<object>(game, "_renderTarget"));
            Assert.Equal(("MainRenderTarget", 1280, 720), renderTargetManager.LastRequest);
        }

        [Fact]
        public void DisposeManagedResources_WhenServerStopsSuccessfully_ShouldDisposeCollaboratorsAndCancelPendingScreenshot()
        {
            var game = CreateGameForLifecycle();
            var stageManager = new Mock<IStageManager>();
            var resourceManager = new Mock<IResourceManager>();
            var graphicsManager = new StubGraphicsManager(isDeviceAvailable: true, CreateFailingRenderTargetManager());
            var pendingScreenshot = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var host = new Mock<IHost>();
            host.Setup(value => value.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var cancellation = new CancellationTokenSource();
            var server = CreateRunningJsonRpcServer(host, cancellation);

            ReflectionHelpers.SetPrivateField(game, "<StageManager>k__BackingField", stageManager.Object);
            ReflectionHelpers.SetPrivateField(game, "<ResourceManager>k__BackingField", resourceManager.Object);
            ReflectionHelpers.SetPrivateField(game, "_graphicsManager", graphicsManager);
            ReflectionHelpers.SetPrivateField(game, "_pendingScreenshot", pendingScreenshot);
            ReflectionHelpers.SetPrivateField(game, "_jsonRpcServer", server);
            ReflectionHelpers.SetPrivateField(game, "_gameApiCancellation", cancellation);

            ReflectionHelpers.InvokePrivateMethod(game, "DisposeManagedResources");

            stageManager.Verify(value => value.Dispose(), Times.Once);
            resourceManager.Verify(value => value.Dispose(), Times.Once);
            host.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
            host.Verify(value => value.Dispose(), Times.Once);
            Assert.True(graphicsManager.DisposeCalled);
            Assert.Equal(1, graphicsManager.SettingsChangedRemoveCount);
            Assert.Equal(1, graphicsManager.DeviceLostRemoveCount);
            Assert.Equal(1, graphicsManager.DeviceResetRemoveCount);
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(game, "<StageManager>k__BackingField"));
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(game, "_jsonRpcServer"));
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(game, "_gameApiCancellation"));
            Assert.True(pendingScreenshot.Task.IsCanceled);
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(game, "_pendingScreenshot"));
        }

        [Fact]
        public void DisposeManagedResources_WhenServerStopThrows_ShouldStillDisposeServerAndClearState()
        {
            var game = CreateGameForLifecycle();
            var host = new Mock<IHost>();
            host.Setup(value => value.StopAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("stop failed"));
            var cancellation = new CancellationTokenSource();
            var server = CreateRunningJsonRpcServer(host, cancellation);

            ReflectionHelpers.SetPrivateField(game, "_graphicsManager", new StubGraphicsManager(isDeviceAvailable: true, CreateFailingRenderTargetManager()));
            ReflectionHelpers.SetPrivateField(game, "_jsonRpcServer", server);
            ReflectionHelpers.SetPrivateField(game, "_gameApiCancellation", cancellation);

            var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(game, "DisposeManagedResources"));

            Assert.Null(exception);
            host.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
            host.Verify(value => value.Dispose(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(game, "_jsonRpcServer"));
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(game, "_gameApiCancellation"));
        }

        [Fact]
        public async Task DisposeManagedResources_WhenServerStartupIsStillRunning_ShouldWaitBeforeStoppingServer()
        {
            var game = CreateGameForLifecycle();
            var host = new Mock<IHost>();
            host.Setup(value => value.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var cancellation = new CancellationTokenSource();
            var server = CreateRunningJsonRpcServer(host, cancellation);
            var startTaskSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            ReflectionHelpers.SetPrivateField(game, "_graphicsManager", new StubGraphicsManager(isDeviceAvailable: true, CreateFailingRenderTargetManager()));
            ReflectionHelpers.SetPrivateField(game, "_jsonRpcServer", server);
            ReflectionHelpers.SetPrivateField(game, "_gameApiCancellation", cancellation);
            ReflectionHelpers.SetPrivateField(game, "_gameApiStartTask", startTaskSource.Task);

            var disposeTask = Task.Run(() => ReflectionHelpers.InvokePrivateMethod(game, "DisposeManagedResources"));

            // Give the dispose thread time to reach the blocking GetAwaiter().GetResult() call,
            // then use WhenAny with a timeout to verify it hasn't completed (still waiting on startTaskSource)
            var raceResult = await Task.WhenAny(disposeTask, Task.Delay(500));
            Assert.NotSame(disposeTask, raceResult);
            host.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Never);

            startTaskSource.SetResult();
            await disposeTask;

            host.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
            host.Verify(value => value.Dispose(), Times.Once);
        }

        [Fact]
        public void DisposeManagedResources_WhenGameApiStartupFaults_ShouldStillStopServerAndClearState()
        {
            var game = CreateGameForLifecycle();
            var host = new Mock<IHost>();
            host.Setup(value => value.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var cancellation = new CancellationTokenSource();
            var server = CreateRunningJsonRpcServer(host, cancellation);

            ReflectionHelpers.SetPrivateField(game, "_graphicsManager", new StubGraphicsManager(isDeviceAvailable: true, CreateFailingRenderTargetManager()));
            ReflectionHelpers.SetPrivateField(game, "_jsonRpcServer", server);
            ReflectionHelpers.SetPrivateField(game, "_gameApiCancellation", cancellation);
            ReflectionHelpers.SetPrivateField(game, "_gameApiStartTask", Task.FromException(new InvalidOperationException("startup failed")));

            var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(game, "DisposeManagedResources"));

            Assert.Null(exception);
            host.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
            host.Verify(value => value.Dispose(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(game, "_gameApiStartTask"));
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(game, "_jsonRpcServer"));
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(game, "_gameApiCancellation"));
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

        private static TestableBaseGame CreateGameForUpdate(
            TrackingInputManager inputManager,
            IStageManager stageManager,
            StubGraphicsManager graphicsManager)
        {
            var game = ReflectionHelpers.CreateUninitialized<TestableBaseGame>();

            ReflectionHelpers.SetPrivateField(game, "_mainThreadActions", new ConcurrentQueue<Action>());
            ReflectionHelpers.SetPrivateField(game, "_pendingScreenshot", null);
            ReflectionHelpers.SetPrivateField(game, "_totalGameTime", 0.0);
            ReflectionHelpers.SetPrivateField(game, "_lastStageTransitionTime", 0.0);
            ReflectionHelpers.SetPrivateField(game, "<ConfigManager>k__BackingField", CreateConfigManager(new ConfigData()));
            ReflectionHelpers.SetPrivateField(game, "<InputManager>k__BackingField", inputManager);
            ReflectionHelpers.SetPrivateField(game, "<StageManager>k__BackingField", stageManager);
            ReflectionHelpers.SetPrivateField(game, "_graphicsManager", graphicsManager);

            return game;
        }

        private static LoadContentTestableBaseGame CreateGameForLoadContent(
            ConfigData config,
            IResourceManager resourceManager,
            IStageManager stageManager,
            SpriteBatch spriteBatch)
        {
            var game = ReflectionHelpers.CreateUninitialized<LoadContentTestableBaseGame>();
            var loggerFactory = LoggerFactory.Create(builder => { });

            ReflectionHelpers.SetPrivateField(game, "_loggerFactory", loggerFactory);
            ReflectionHelpers.SetPrivateField(game, "_logger", loggerFactory.CreateLogger<BaseGame>());
            ReflectionHelpers.SetPrivateField(game, "<ConfigManager>k__BackingField", CreateConfigManager(config));
            game.ResourceManagerToReturn = resourceManager;
            game.StageManagerToReturn = stageManager;
            game.SpriteBatchToReturn = spriteBatch;

            return game;
        }

        private static JsonRpcServer CreateRunningJsonRpcServer(Mock<IHost> host, CancellationTokenSource cancellation)
        {
            var gameApi = new Mock<IGameApi>();
            gameApi.SetupGet(api => api.IsRunning).Returns(true);

            var server = new JsonRpcServer(gameApi.Object, port: 12345);
            ReflectionHelpers.SetPrivateField(server, "_host", host.Object);
            ReflectionHelpers.SetPrivateField(server, "_cancellationTokenSource", cancellation);
            ReflectionHelpers.SetPrivateField(server, "_isRunning", true);
            return server;
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

        private sealed class RecordingRenderTargetManager : RenderTargetManager
        {
            private readonly RenderTarget2D _renderTarget;

            public RecordingRenderTargetManager(RenderTarget2D renderTarget)
                : base(ReflectionHelpers.CreateUninitialized<GraphicsDevice>())
            {
                _renderTarget = renderTarget;
            }

            public (string Name, int Width, int Height)? LastRequest { get; private set; }

            protected override RenderTarget2D CreateRenderTarget(int width, int height, SurfaceFormat format, DepthFormat depthFormat, int multiSampleCount)
            {
                LastRequest = ("MainRenderTarget", width, height);
                return _renderTarget;
            }
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
            public int ToggleFullscreenCallCount { get; private set; }
            public int SettingsChangedRemoveCount { get; private set; }
            public int DeviceLostRemoveCount { get; private set; }
            public int DeviceResetRemoveCount { get; private set; }
            public Action? OnToggleFullscreen { get; set; }

            private event EventHandler<GraphicsSettingsChangedEventArgs>? _settingsChanged;
            private event EventHandler? _deviceLost;
            private event EventHandler? _deviceReset;

            public event EventHandler<GraphicsSettingsChangedEventArgs>? SettingsChanged
            {
                add => _settingsChanged += value;
                remove
                {
                    SettingsChangedRemoveCount++;
                    _settingsChanged -= value;
                }
            }

            public event EventHandler? DeviceLost
            {
                add => _deviceLost += value;
                remove
                {
                    DeviceLostRemoveCount++;
                    _deviceLost -= value;
                }
            }

            public event EventHandler? DeviceReset
            {
                add => _deviceReset += value;
                remove
                {
                    DeviceResetRemoveCount++;
                    _deviceReset -= value;
                }
            }

            public void Initialize()
            {
            }

            public bool ApplySettings(GraphicsSettings settings) => false;
            public bool ChangeResolution(int width, int height) => false;
            public bool ToggleFullscreen()
            {
                ToggleFullscreenCallCount++;
                OnToggleFullscreen?.Invoke();
                return true;
            }
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

        private sealed class TrackingInputManager : InputManagerCompat
        {
            public TrackingInputManager()
                : base(new ConfigManager())
            {
            }

            public int UpdateCallCount { get; private set; }

            public Action<double>? OnUpdate { get; set; }

            public override void Update(double deltaTime)
            {
                UpdateCallCount++;
                OnUpdate?.Invoke(deltaTime);
            }
        }

        private sealed class TrackableRenderTarget : RenderTarget2D
        {
            private TrackableRenderTarget()
                : base(null!, 1, 1)
            {
            }

            public bool DisposeCalled { get; private set; }

            protected override void Dispose(bool disposing)
            {
                DisposeCalled = true;
            }
        }

        private sealed class TestableBaseGame : BaseGame
        {
            public int CompleteBaseUpdateCallCount { get; private set; }

            public bool ShouldToggleFullscreenResult { get; set; }

            public bool ShouldToggleFullscreenCalled { get; private set; }

            internal override bool ShouldToggleFullscreen(KeyboardState keyboardState)
            {
                ShouldToggleFullscreenCalled = true;
                return ShouldToggleFullscreenResult;
            }

            internal override void CompleteBaseUpdate(GameTime gameTime)
            {
                CompleteBaseUpdateCallCount++;
            }
        }

        private sealed class LoadContentTestableBaseGame : BaseGame
        {
            public SpriteBatch SpriteBatchToReturn { get; set; } = null!;

            public IResourceManager ResourceManagerToReturn { get; set; } = null!;

            public IStageManager StageManagerToReturn { get; set; } = null!;

            public Task StartGameApiServerTask { get; set; } = Task.CompletedTask;

            public int CreateSpriteBatchCallCount { get; private set; }

            public int CreateResourceManagerCallCount { get; private set; }

            public int InitializeManagedFontFactoryCallCount { get; private set; }

            public int StartGameApiServerCallCount { get; private set; }

            public void InvokeLoadContent()
            {
                base.LoadContent();
            }

            internal override SpriteBatch CreateSpriteBatch()
            {
                CreateSpriteBatchCallCount++;
                return SpriteBatchToReturn;
            }

            internal override IResourceManager CreateResourceManager()
            {
                CreateResourceManagerCallCount++;
                return ResourceManagerToReturn;
            }

            internal override void InitializeManagedFontFactory()
            {
                InitializeManagedFontFactoryCallCount++;
            }

            internal override IStageManager CreateStageManager()
            {
                return StageManagerToReturn;
            }

            internal override Task StartGameApiServerAsync()
            {
                StartGameApiServerCallCount++;
                return StartGameApiServerTask;
            }
        }
    }
}
