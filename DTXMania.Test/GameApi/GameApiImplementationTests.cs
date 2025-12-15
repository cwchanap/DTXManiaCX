using DTXMania.Game.Lib;
using Moq;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace DTXMania.Test.GameApi
{
    /// <summary>
    /// Unit tests for GameApiImplementation.
    /// Uses a mock IGameApi to test the interface contract without MonoGame dependencies.
    /// Integration tests with actual BaseGame would require graphics context.
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

        #region IGameApi Interface Tests (using mock)

        [Fact]
        public void IGameApi_IsRunning_CanBeQueried()
        {
            // Arrange
            var mockApi = new Mock<IGameApi>();
            mockApi.Setup(api => api.IsRunning).Returns(true);

            // Act & Assert
            Assert.True(mockApi.Object.IsRunning);
        }

        [Fact]
        public async Task IGameApi_GetGameStateAsync_ReturnsGameState()
        {
            // Arrange
            var mockApi = new Mock<IGameApi>();
            var expectedState = new GameState
            {
                PlayerPositionX = 100,
                PlayerPositionY = 200,
                Score = 1000,
                Level = 5,
                CurrentStage = "TestStage",
                Timestamp = DateTime.UtcNow
            };
            mockApi.Setup(api => api.GetGameStateAsync()).ReturnsAsync(expectedState);

            // Act
            var result = await mockApi.Object.GetGameStateAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(100, result.PlayerPositionX);
            Assert.Equal(200, result.PlayerPositionY);
            Assert.Equal(1000, result.Score);
            Assert.Equal(5, result.Level);
            Assert.Equal("TestStage", result.CurrentStage);
        }

        [Fact]
        public async Task IGameApi_GetWindowInfoAsync_ReturnsWindowInfo()
        {
            // Arrange
            var mockApi = new Mock<IGameApi>();
            var expectedInfo = new GameWindowInfo
            {
                Width = 1920,
                Height = 1080,
                X = 0,
                Y = 0,
                Title = "DTXManiaCX",
                IsVisible = true
            };
            mockApi.Setup(api => api.GetWindowInfoAsync()).ReturnsAsync(expectedInfo);

            // Act
            var result = await mockApi.Object.GetWindowInfoAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(1920, result.Width);
            Assert.Equal(1080, result.Height);
            Assert.Equal("DTXManiaCX", result.Title);
            Assert.True(result.IsVisible);
        }

        [Theory]
        [InlineData(InputType.MouseClick)]
        [InlineData(InputType.MouseMove)]
        [InlineData(InputType.KeyPress)]
        [InlineData(InputType.KeyRelease)]
        public async Task IGameApi_SendInputAsync_AcceptsAllInputTypes(InputType inputType)
        {
            // Arrange
            var mockApi = new Mock<IGameApi>();
            mockApi.Setup(api => api.SendInputAsync(It.IsAny<GameInput>())).ReturnsAsync(false);
            var input = new GameInput { Type = inputType, Data = JsonSerializer.SerializeToElement("test") };

            // Act
            var result = await mockApi.Object.SendInputAsync(input);

            // Assert
            mockApi.Verify(api => api.SendInputAsync(It.Is<GameInput>(i => i.Type == inputType)), Times.Once);
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
        public async Task IGameApi_MultipleConcurrentCalls_ShouldNotThrow()
        {
            // Arrange
            var mockApi = new Mock<IGameApi>();
            mockApi.Setup(api => api.GetGameStateAsync()).ReturnsAsync(new GameState());
            mockApi.Setup(api => api.GetWindowInfoAsync()).ReturnsAsync(new GameWindowInfo());

            var tasks = new List<Task>();

            // Act
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(mockApi.Object.GetGameStateAsync());
                tasks.Add(mockApi.Object.GetWindowInfoAsync());
            }

            // Assert - should complete without throwing
            await Task.WhenAll(tasks);
        }

        #endregion
    }
}
