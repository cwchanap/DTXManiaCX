using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Config;
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

        [Theory]
        [InlineData(InputType.MouseClick)]
        [InlineData(InputType.MouseMove)]
        [InlineData(InputType.KeyPress)]
        [InlineData(InputType.KeyRelease)]
        public async Task SendInputAsync_ShouldReturnFalse_ForSupportedInputTypes(InputType inputType)
        {
            var gameContext = new Mock<IGameContext>();
            var api = new GameApiImplementation(gameContext.Object);

            var input = new GameInput { Type = inputType, Data = JsonSerializer.SerializeToElement("test") };
            var result = await api.SendInputAsync(input);

            Assert.False(result);
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
        public void GameState_DefaultValues_AreCorrect()
        {
            // Act
            var state = new GameState();

            // Assert
            Assert.Equal(0, state.PlayerPositionX);
            Assert.Equal(0, state.PlayerPositionY);
            Assert.Equal(0, state.Score);
            Assert.Equal(0, state.Level);
            Assert.Equal(string.Empty, state.CurrentStage);
            Assert.NotNull(state.CustomData);
        }

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

        [Fact]
        public void GameInput_CanBeCreatedWithAllTypes()
        {
            // Assert all input types are valid
            Assert.True(Enum.IsDefined(typeof(InputType), InputType.MouseClick));
            Assert.True(Enum.IsDefined(typeof(InputType), InputType.MouseMove));
            Assert.True(Enum.IsDefined(typeof(InputType), InputType.KeyPress));
            Assert.True(Enum.IsDefined(typeof(InputType), InputType.KeyRelease));
        }

        [Fact]
        public void GameInput_DataProperty_CanHoldVariousTypes()
        {
            // Arrange & Act
            var mouseInput = new GameInput { Type = InputType.MouseClick, Data = JsonSerializer.SerializeToElement(new { x = 100, y = 200 }) };
            var keyInput = new GameInput { Type = InputType.KeyPress, Data = JsonSerializer.SerializeToElement("Enter") };
            var numericInput = new GameInput { Type = InputType.KeyPress, Data = JsonSerializer.SerializeToElement(65) };

            // Assert
            Assert.NotNull(mouseInput.Data);
            Assert.NotNull(keyInput.Data);
            Assert.NotNull(numericInput.Data);
        }

        #endregion

        #region GameWindowInfo Model Tests

        [Fact]
        public void GameWindowInfo_DefaultValues_AreCorrect()
        {
            // Act
            var info = new GameWindowInfo();

            // Assert
            Assert.Equal(0, info.Width);
            Assert.Equal(0, info.Height);
            Assert.Equal(0, info.X);
            Assert.Equal(0, info.Y);
            Assert.Equal(string.Empty, info.Title);
            Assert.False(info.IsVisible);
        }

        [Fact]
        public void GameWindowInfo_CanSetAllProperties()
        {
            // Act
            var info = new GameWindowInfo
            {
                Width = 1920,
                Height = 1080,
                X = 100,
                Y = 50,
                Title = "Test Window",
                IsVisible = true
            };

            // Assert
            Assert.Equal(1920, info.Width);
            Assert.Equal(1080, info.Height);
            Assert.Equal(100, info.X);
            Assert.Equal(50, info.Y);
            Assert.Equal("Test Window", info.Title);
            Assert.True(info.IsVisible);
        }

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

        #endregion
    }
}
