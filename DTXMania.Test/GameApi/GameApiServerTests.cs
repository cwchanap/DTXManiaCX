using DTXMania.Game.Lib;
using Moq;
using System;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace DTXMania.Test.GameApi
{
    /// <summary>
    /// Unit tests for GameApiServer: constructor, public properties, and the
    /// private static ValidateGameInput method (exercised via reflection so every
    /// validation branch is covered without starting the HTTP listener).
    /// </summary>
    [Trait("Category", "Unit")]
    public class GameApiServerTests
    {
        // Helper: invoke the private static ValidateGameInput method via reflection.
        private static (bool IsValid, string ErrorMessage) Validate(GameInput input)
        {
            var method = typeof(GameApiServer).GetMethod(
                "ValidateGameInput",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("ValidateGameInput method not found");

            var raw = method.Invoke(null, new object[] { input })!;
            // The return type is ValueTuple<bool, string>; accessed via Item1/Item2 at runtime.
            var type = raw.GetType();
            var isValid = (bool)type.GetField("Item1")!.GetValue(raw)!;
            var errorMessage = (string)type.GetField("Item2")!.GetValue(raw)!;
            return (isValid, errorMessage);
        }

        private static GameInput MouseInput(InputType type, JsonElement? data)
            => new GameInput { Type = type, Data = data };

        private static GameInput KeyInput(InputType type, JsonElement? data)
            => new GameInput { Type = type, Data = data };

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullGameApi_ShouldThrowArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => new GameApiServer(null!));
            Assert.Equal("gameApi", ex.ParamName);
        }

        [Fact]
        public void Constructor_WithValidGameApi_ShouldCreateInstance()
        {
            var gameApi = new Mock<IGameApi>().Object;
            using var server = new GameApiServer(gameApi);
            Assert.NotNull(server);
        }

        #endregion

        #region IsRunning / GetServerUrl Tests

        [Fact]
        public void IsRunning_BeforeStart_ShouldBeFalse()
        {
            var gameApi = new Mock<IGameApi>().Object;
            using var server = new GameApiServer(gameApi);
            Assert.False(server.IsRunning);
        }

        [Fact]
        public void GetServerUrl_DefaultPort_ShouldReturnLocalhostPort8080()
        {
            var gameApi = new Mock<IGameApi>().Object;
            using var server = new GameApiServer(gameApi);
            Assert.Equal("http://localhost:8080", server.GetServerUrl());
        }

        [Fact]
        public void GetServerUrl_CustomPort_ShouldIncludePort()
        {
            var gameApi = new Mock<IGameApi>().Object;
            using var server = new GameApiServer(gameApi, port: 12345);
            Assert.Equal("http://localhost:12345", server.GetServerUrl());
        }

        #endregion

        #region ValidateGameInput – Invalid Enum

        [Fact]
        public void ValidateGameInput_InvalidInputType_ShouldReturnFalse()
        {
            var input = new GameInput { Type = (InputType)999 };
            var (isValid, errorMessage) = Validate(input);
            Assert.False(isValid);
            Assert.Equal("Invalid input type", errorMessage);
        }

        #endregion

        #region ValidateGameInput – MouseClick

        [Fact]
        public void ValidateGameInput_MouseClick_NullData_ShouldReturnFalse()
        {
            var input = MouseInput(InputType.MouseClick, null);
            var (isValid, msg) = Validate(input);
            Assert.False(isValid);
            Assert.Equal("Mouse input requires position data", msg);
        }

        [Fact]
        public void ValidateGameInput_MouseClick_JsonNullData_ShouldReturnFalse()
        {
            var data = JsonSerializer.SerializeToElement<object?>(null);
            var input = MouseInput(InputType.MouseClick, data);
            var (isValid, msg) = Validate(input);
            Assert.False(isValid);
            Assert.Equal("Mouse input requires position data", msg);
        }

        [Fact]
        public void ValidateGameInput_MouseClick_StringData_ShouldReturnFalse()
        {
            var data = JsonSerializer.SerializeToElement("not an object");
            var input = MouseInput(InputType.MouseClick, data);
            var (isValid, msg) = Validate(input);
            Assert.False(isValid);
            Assert.Equal("Mouse input data must be an object", msg);
        }

        [Fact]
        public void ValidateGameInput_MouseClick_ValidObjectData_ShouldReturnTrue()
        {
            var data = JsonSerializer.SerializeToElement(new { x = 100, y = 200 });
            var input = MouseInput(InputType.MouseClick, data);
            var (isValid, msg) = Validate(input);
            Assert.True(isValid);
            Assert.Equal(string.Empty, msg);
        }

        #endregion

        #region ValidateGameInput – MouseMove

        [Fact]
        public void ValidateGameInput_MouseMove_NullData_ShouldReturnFalse()
        {
            var input = MouseInput(InputType.MouseMove, null);
            var (isValid, msg) = Validate(input);
            Assert.False(isValid);
            Assert.Equal("Mouse input requires position data", msg);
        }

        [Fact]
        public void ValidateGameInput_MouseMove_ValidObjectData_ShouldReturnTrue()
        {
            var data = JsonSerializer.SerializeToElement(new { x = 50, y = 75 });
            var input = MouseInput(InputType.MouseMove, data);
            var (isValid, msg) = Validate(input);
            Assert.True(isValid);
            Assert.Equal(string.Empty, msg);
        }

        [Fact]
        public void ValidateGameInput_MouseMove_NumberData_ShouldReturnFalse()
        {
            var data = JsonSerializer.SerializeToElement(42);
            var input = MouseInput(InputType.MouseMove, data);
            var (isValid, msg) = Validate(input);
            Assert.False(isValid);
            Assert.Equal("Mouse input data must be an object", msg);
        }

        #endregion

        #region ValidateGameInput – KeyPress

        [Fact]
        public void ValidateGameInput_KeyPress_NullData_ShouldReturnFalse()
        {
            var input = KeyInput(InputType.KeyPress, null);
            var (isValid, msg) = Validate(input);
            Assert.False(isValid);
            Assert.Equal("Key input requires key data", msg);
        }

        [Fact]
        public void ValidateGameInput_KeyPress_EmptyStringData_ShouldReturnFalse()
        {
            var data = JsonSerializer.SerializeToElement(string.Empty);
            var input = KeyInput(InputType.KeyPress, data);
            var (isValid, msg) = Validate(input);
            Assert.False(isValid);
            Assert.Equal("Invalid key data format", msg);
        }

        [Fact]
        public void ValidateGameInput_KeyPress_StringTooLong_ShouldReturnFalse()
        {
            var longKey = new string('A', 51); // 51 chars, exceeds limit of 50
            var data = JsonSerializer.SerializeToElement(longKey);
            var input = KeyInput(InputType.KeyPress, data);
            var (isValid, msg) = Validate(input);
            Assert.False(isValid);
            Assert.Equal("Invalid key data format", msg);
        }

        [Fact]
        public void ValidateGameInput_KeyPress_ValidStringData_ShouldReturnTrue()
        {
            var data = JsonSerializer.SerializeToElement("Enter");
            var input = KeyInput(InputType.KeyPress, data);
            var (isValid, msg) = Validate(input);
            Assert.True(isValid);
            Assert.Equal(string.Empty, msg);
        }

        [Fact]
        public void ValidateGameInput_KeyPress_ValidStringAtMaxLength_ShouldReturnTrue()
        {
            var maxKey = new string('A', 50); // exactly 50 chars
            var data = JsonSerializer.SerializeToElement(maxKey);
            var input = KeyInput(InputType.KeyPress, data);
            var (isValid, msg) = Validate(input);
            Assert.True(isValid);
            Assert.Equal(string.Empty, msg);
        }

        [Fact]
        public void ValidateGameInput_KeyPress_ValidNumberData_ShouldReturnTrue()
        {
            var data = JsonSerializer.SerializeToElement(65); // 'A' key code
            var input = KeyInput(InputType.KeyPress, data);
            var (isValid, msg) = Validate(input);
            Assert.True(isValid);
            Assert.Equal(string.Empty, msg);
        }

        [Theory]
        [InlineData(0, true, "")]
        [InlineData(255, true, "")]
        [InlineData(256, false, "Invalid key data format")]
        [InlineData(-1, false, "Invalid key data format")]
        public void ValidateGameInput_KeyPress_NumericKeyCodeBoundary_ShouldReturnExpected(
            int keyCode, bool expectedValid, string expectedMessage)
        {
            var data = JsonSerializer.SerializeToElement(keyCode);
            var input = KeyInput(InputType.KeyPress, data);
            var (isValid, msg) = Validate(input);
            Assert.Equal(expectedValid, isValid);
            Assert.Equal(expectedMessage, msg);
        }

        [Fact]
        public void ValidateGameInput_KeyPress_BooleanData_ShouldReturnFalse()
        {
            var data = JsonSerializer.SerializeToElement(true);
            var input = KeyInput(InputType.KeyPress, data);
            var (isValid, msg) = Validate(input);
            Assert.False(isValid);
            Assert.Equal("Invalid key data format", msg);
        }

        #endregion

        #region ValidateGameInput – KeyRelease

        [Fact]
        public void ValidateGameInput_KeyRelease_NullData_ShouldReturnFalse()
        {
            var input = KeyInput(InputType.KeyRelease, null);
            var (isValid, msg) = Validate(input);
            Assert.False(isValid);
            Assert.Equal("Key input requires key data", msg);
        }

        [Fact]
        public void ValidateGameInput_KeyRelease_ValidStringData_ShouldReturnTrue()
        {
            var data = JsonSerializer.SerializeToElement("Escape");
            var input = KeyInput(InputType.KeyRelease, data);
            var (isValid, msg) = Validate(input);
            Assert.True(isValid);
            Assert.Equal(string.Empty, msg);
        }

        [Fact]
        public void ValidateGameInput_KeyRelease_ValidNumberData_ShouldReturnTrue()
        {
            var data = JsonSerializer.SerializeToElement(27); // Escape key code
            var input = KeyInput(InputType.KeyRelease, data);
            var (isValid, msg) = Validate(input);
            Assert.True(isValid);
            Assert.Equal(string.Empty, msg);
        }

        [Fact]
        public void ValidateGameInput_KeyRelease_EmptyStringData_ShouldReturnFalse()
        {
            var data = JsonSerializer.SerializeToElement(string.Empty);
            var input = KeyInput(InputType.KeyRelease, data);
            var (isValid, msg) = Validate(input);
            Assert.False(isValid);
            Assert.Equal("Invalid key data format", msg);
        }

        #endregion
    }
}
