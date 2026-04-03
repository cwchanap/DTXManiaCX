using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace DTXMania.Test.GameApi
{
    /// <summary>
    /// Unit tests for Game API-related types.
    /// This file creates real GameApiImplementation instances with mocked dependencies (e.g. IGameContext) via Moq,
    /// exercising the implementation behavior while avoiding the need to spin up a full game runtime.
    /// Full integration tests would still require a graphics-capable BaseGame/MonoGame fixture.
    /// </summary>
    [Trait("Category", "Unit")]
    public class GameApiImplementationTests
    {
        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullGame_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() => new GameApiImplementation(null!));
            Assert.Equal("game", ex.ParamName);
        }

        #endregion

        #region GameApiImplementation Tests

        [Fact]
        public void IsRunning_ShouldReturnTrue()
        {
            var gameContext = new Mock<IGameContext>();
            var api = new GameApiImplementation(gameContext.Object);

            Assert.True(api.IsRunning);
        }

        [Fact]
        public async Task GetGameStateAsync_WithStageAndConfig_ShouldPopulateFields()
        {
            var stage = new Mock<IStage>();
            stage.SetupGet(s => s.Type).Returns(StageType.Title);
            stage.Setup(s => s.ToString()).Returns("Title");

            var stageManager = new Mock<IStageManager>();
            stageManager.SetupGet(sm => sm.CurrentStage).Returns(stage.Object);

            var configManager = new Mock<IConfigManager>();
            configManager.SetupGet(cm => cm.Config).Returns(new ConfigData { ScreenWidth = 1234, ScreenHeight = 567 });

            var gameContext = new Mock<IGameContext>();
            gameContext.SetupGet(g => g.StageManager).Returns(stageManager.Object);
            gameContext.SetupGet(g => g.ConfigManager).Returns(configManager.Object);

            var api = new GameApiImplementation(gameContext.Object);

            var result = await api.GetGameStateAsync();

            Assert.NotNull(result);
            Assert.Equal("Title", result.CurrentStage);
            Assert.Equal("DTXManiaCX", result.CustomData["game_name"]);
            Assert.Equal(1234, (int)result.CustomData["config_screen_width"]);
            Assert.Equal(567, (int)result.CustomData["config_screen_height"]);
        }

        [Fact]
        public async Task GetWindowInfoAsync_WithConfig_ShouldUseConfiguredSize()
        {
            var configManager = new Mock<IConfigManager>();
            configManager.SetupGet(cm => cm.Config).Returns(new ConfigData { ScreenWidth = 800, ScreenHeight = 600 });

            var gameContext = new Mock<IGameContext>();
            gameContext.SetupGet(g => g.ConfigManager).Returns(configManager.Object);

            var api = new GameApiImplementation(gameContext.Object);

            var result = await api.GetWindowInfoAsync();

            Assert.Equal(800, result.Width);
            Assert.Equal(600, result.Height);
            Assert.Equal("DTXManiaCX", result.Title);
            Assert.True(result.IsVisible);
        }

        [Fact]
        public async Task SendInputAsync_WhenModularInputManagerIsUnavailable_ShouldReturnFalse()
        {
            var inputManager = new Mock<IInputManagerCompat>();
            inputManager.SetupGet(i => i.ModularInputManager).Returns((ModularInputManager)null!);

            var gameContext = new Mock<IGameContext>();
            gameContext.SetupGet(g => g.InputManager).Returns(inputManager.Object);
            var api = new GameApiImplementation(gameContext.Object);

            var input = new GameInput { Type = InputType.KeyPress, Data = JsonSerializer.SerializeToElement("test") };
            var result = await api.SendInputAsync(input);

            Assert.False(result);
        }

        [Fact]
        public async Task SendInputAsync_WithNullInput_ShouldReturnFalse()
        {
            var gameContext = new Mock<IGameContext>();
            var api = new GameApiImplementation(gameContext.Object);

            var result = await api.SendInputAsync(null!);

            Assert.False(result);
        }

        [Fact]
        public async Task GetWindowInfoAsync_WhenContextThrows_ShouldReturnFallback()
        {
            var gameContext = new Mock<IGameContext>();
            gameContext.SetupGet(g => g.ConfigManager).Throws(new InvalidOperationException("crash"));

            var api = new GameApiImplementation(gameContext.Object);

            var result = await api.GetWindowInfoAsync();

            Assert.NotNull(result);
            Assert.Contains("Error", result.Title);
            Assert.False(result.IsVisible);
        }

        [Fact]
        public async Task GetGameStateAsync_WhenContextThrows_ShouldReturnSanitizedError_AndLog()
        {
            var logger = new Mock<ILogger<GameApiImplementation>>();

            var gameContext = new Mock<IGameContext>();
            gameContext.SetupGet(g => g.StageManager).Throws(new InvalidOperationException("secret"));

            var api = new GameApiImplementation(gameContext.Object, logger.Object);

            var result = await api.GetGameStateAsync();

            Assert.Equal("Error", result.CurrentStage);
            Assert.True(result.CustomData.TryGetValue("error", out var errorValue));
            Assert.Equal("Internal error (InvalidOperationException)", errorValue);

            logger.Verify(l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString() == "Error getting game state"),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        #endregion

        #region GameState Model Tests

        [Fact]
        public void GameState_CustomData_CanStoreValues()
        {
            // Arrange
            var state = new GameState();

            // Act
            state.CustomData["key1"] = "value1";
            state.CustomData["key2"] = 42;

            // Assert
            Assert.Equal("value1", state.CustomData["key1"]);
            Assert.Equal(42, state.CustomData["key2"]);
        }

        #endregion

        #region GameInput Model Tests

        #endregion

        #region GameWindowInfo Model Tests

        #endregion

        #region Thread Safety Tests (using mock)

        [Fact]
        public async Task MultipleConcurrentCalls_ShouldNotThrow()
        {
            var gameContext = new Mock<IGameContext>();
            var api = new GameApiImplementation(gameContext.Object);

            var tasks = new List<Task>();

            // Act
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(api.GetGameStateAsync());
                tasks.Add(api.GetWindowInfoAsync());
            }

            // Assert - should complete without throwing
            await Task.WhenAll(tasks);
        }

        #endregion

        #region TakeScreenshotAsync Tests

        [Fact]
        public async Task TakeScreenshotAsync_ShouldDelegateToGameContext()
        {
            var expectedBytes = new byte[] { 137, 80, 78, 71, 13, 10 }; // PNG header
            var gameContext = new Mock<IGameContext>();
            gameContext.Setup(g => g.CaptureScreenshotAsync()).ReturnsAsync(expectedBytes);

            var api = new GameApiImplementation(gameContext.Object);

            var result = await api.TakeScreenshotAsync();

            Assert.Equal(expectedBytes, result);
            gameContext.Verify(g => g.CaptureScreenshotAsync(), Times.Once);
        }

        [Fact]
        public async Task TakeScreenshotAsync_WhenContextReturnsNull_ShouldReturnNull()
        {
            var gameContext = new Mock<IGameContext>();
            gameContext.Setup(g => g.CaptureScreenshotAsync()).ReturnsAsync((byte[]?)null);

            var api = new GameApiImplementation(gameContext.Object);

            var result = await api.TakeScreenshotAsync();

            Assert.Null(result);
        }

        [Fact]
        public async Task TakeScreenshotAsync_ShouldReturnExactBytesFromContext()
        {
            var expectedBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            var gameContext = new Mock<IGameContext>();
            gameContext.Setup(g => g.CaptureScreenshotAsync()).ReturnsAsync(expectedBytes);

            var api = new GameApiImplementation(gameContext.Object);

            var result = await api.TakeScreenshotAsync();

            Assert.NotNull(result);
            Assert.Equal(expectedBytes.Length, result.Length);
            Assert.Equal(expectedBytes, result);
        }

        #endregion

        #region ChangeStageAsync Tests

        [Fact]
        public async Task ChangeStageAsync_WithValidStageName_ShouldQueueActionAndReturnTrue()
        {
            var capturedActions = new List<Action>();
            var stageManager = new Mock<IStageManager>();
            var gameContext = new Mock<IGameContext>();
            gameContext.Setup(g => g.QueueMainThreadAction(It.IsAny<Action>()))
                       .Callback<Action>(a => capturedActions.Add(a));
            gameContext.SetupGet(g => g.StageManager).Returns(stageManager.Object);

            var api = new GameApiImplementation(gameContext.Object);

            var result = await api.ChangeStageAsync("Title");

            Assert.True(result);
            Assert.Single(capturedActions);

            // Execute the queued action and verify the stage transition is triggered
            capturedActions[0]();
            stageManager.Verify(sm => sm.ChangeStage(StageType.Title, It.IsAny<IStageTransition>()), Times.Once);
        }

        [Fact]
        public async Task ChangeStageAsync_WithInvalidStageName_ShouldReturnFalse()
        {
            var gameContext = new Mock<IGameContext>();

            var api = new GameApiImplementation(gameContext.Object);

            var result = await api.ChangeStageAsync("NonExistentStage");

            Assert.False(result);
            gameContext.Verify(g => g.QueueMainThreadAction(It.IsAny<Action>()), Times.Never);
        }

        [Fact]
        public async Task ChangeStageAsync_WithEmptyStageName_ShouldReturnFalse()
        {
            var gameContext = new Mock<IGameContext>();

            var api = new GameApiImplementation(gameContext.Object);

            var result = await api.ChangeStageAsync(string.Empty);

            Assert.False(result);
            gameContext.Verify(g => g.QueueMainThreadAction(It.IsAny<Action>()), Times.Never);
        }

        [Fact]
        public async Task ChangeStageAsync_WithNullStageName_ShouldReturnFalse()
        {
            var gameContext = new Mock<IGameContext>();

            var api = new GameApiImplementation(gameContext.Object);

            var result = await api.ChangeStageAsync(null!);

            Assert.False(result);
            gameContext.Verify(g => g.QueueMainThreadAction(It.IsAny<Action>()), Times.Never);
        }

        [Theory]
        [InlineData("99")]
        [InlineData("100")]
        [InlineData("-1")]
        [InlineData("7")]
        public async Task ChangeStageAsync_WithNumericString_ShouldReturnFalse(string numericStageName)
        {
            // Enum.TryParse accepts numeric strings even when the value is not a defined enum member.
            // A numeric like "99" would parse to (StageType)99, which is undefined and would throw
            // inside GetOrCreateStage on the main thread. Verify we reject such inputs up front.
            var gameContext = new Mock<IGameContext>();

            var api = new GameApiImplementation(gameContext.Object);

            var result = await api.ChangeStageAsync(numericStageName);

            Assert.False(result);
            gameContext.Verify(g => g.QueueMainThreadAction(It.IsAny<Action>()), Times.Never);
        }

        [Theory]
        [InlineData("title")]
        [InlineData("TITLE")]
        [InlineData("Title")]
        [InlineData("SONGSELECT")]
        [InlineData("songselect")]
        [InlineData("SongSelect")]
        public async Task ChangeStageAsync_IsCaseInsensitive_ShouldReturnTrue(string stageName)
        {
            var capturedActions = new List<Action>();
            var gameContext = new Mock<IGameContext>();
            gameContext.Setup(g => g.QueueMainThreadAction(It.IsAny<Action>()))
                       .Callback<Action>(a => capturedActions.Add(a));
            gameContext.SetupGet(g => g.StageManager).Returns(new Mock<IStageManager>().Object);

            var api = new GameApiImplementation(gameContext.Object);

            var result = await api.ChangeStageAsync(stageName);

            Assert.True(result);
            Assert.Single(capturedActions);
        }

        [Theory]
        [InlineData("Startup", StageType.Startup)]
        [InlineData("Title", StageType.Title)]
        [InlineData("Config", StageType.Config)]
        [InlineData("SongSelect", StageType.SongSelect)]
        [InlineData("SongTransition", StageType.SongTransition)]
        [InlineData("Performance", StageType.Performance)]
        [InlineData("Result", StageType.Result)]
        public async Task ChangeStageAsync_WithEachValidStageType_ShouldQueueCorrectTransition(string stageName, StageType expectedStageType)
        {
            var capturedActions = new List<Action>();
            var stageManager = new Mock<IStageManager>();
            var gameContext = new Mock<IGameContext>();
            gameContext.Setup(g => g.QueueMainThreadAction(It.IsAny<Action>()))
                       .Callback<Action>(a => capturedActions.Add(a));
            gameContext.SetupGet(g => g.StageManager).Returns(stageManager.Object);

            var api = new GameApiImplementation(gameContext.Object);

            var result = await api.ChangeStageAsync(stageName);

            Assert.True(result);
            Assert.Single(capturedActions);

            capturedActions[0]();
            stageManager.Verify(sm => sm.ChangeStage(expectedStageType, It.IsAny<IStageTransition>()), Times.Once);
        }

        [Fact]
        public async Task ChangeStageAsync_WhenStageManagerIsNull_ShouldNotThrow()
        {
            var capturedActions = new List<Action>();
            var gameContext = new Mock<IGameContext>();
            gameContext.Setup(g => g.QueueMainThreadAction(It.IsAny<Action>()))
                       .Callback<Action>(a => capturedActions.Add(a));
            gameContext.SetupGet(g => g.StageManager).Returns((IStageManager?)null);

            var api = new GameApiImplementation(gameContext.Object);

            var result = await api.ChangeStageAsync("Title");

            Assert.True(result);
            // Executing the queued action should not throw even with null StageManager
            var ex = Record.Exception(() => capturedActions[0]());
            Assert.Null(ex);
        }

        [Fact]
        public async Task ChangeStageAsync_WhenStageManagerIsNull_ShouldLogError_WhenActionExecutes()
        {
            var capturedActions = new List<Action>();
            var logger = new Mock<ILogger<GameApiImplementation>>();
            var gameContext = new Mock<IGameContext>();
            gameContext.Setup(g => g.QueueMainThreadAction(It.IsAny<Action>()))
                       .Callback<Action>(a => capturedActions.Add(a));
            gameContext.SetupGet(g => g.StageManager).Returns((IStageManager?)null);

            var api = new GameApiImplementation(gameContext.Object, logger.Object);
            await api.ChangeStageAsync("Title");

            // Execute the queued action — this is when the null StageManager is detected
            capturedActions[0]();

            logger.Verify(l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("StageManager")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        #endregion

        #region Logging Tests

        [Fact]
        public async Task SendInputAsync_WhenInputManagerUnavailable_ShouldLogWarning()
        {
            var logger = new Mock<ILogger<GameApiImplementation>>();
            var gameContext = new Mock<IGameContext>();
            // InputManager returns null by default from Moq (not set up)

            var api = new GameApiImplementation(gameContext.Object, logger.Object);

            var input = new GameInput { Type = InputType.KeyPress, Data = System.Text.Json.JsonSerializer.SerializeToElement("Enter") };
            var result = await api.SendInputAsync(input);

            Assert.False(result);
            logger.Verify(l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("unavailable")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task GetWindowInfoAsync_WhenContextThrows_ShouldLogError()
        {
            var logger = new Mock<ILogger<GameApiImplementation>>();
            var gameContext = new Mock<IGameContext>();
            gameContext.SetupGet(g => g.ConfigManager).Throws(new InvalidOperationException("crash"));

            var api = new GameApiImplementation(gameContext.Object, logger.Object);

            await api.GetWindowInfoAsync();

            logger.Verify(l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        #endregion
    }
}
