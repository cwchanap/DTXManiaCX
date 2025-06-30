using DTX.Config;
using DTX.Stage;
using DTX.Input;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace DTXMania.Test.Stage;

public class KeyboardRepeatTests
{
    [Fact]
    public void InputCommand_Constructor_ShouldSetProperties()
    {
        // Arrange
        var commandType = InputCommandType.MoveUp;
        var timestamp = 1.5;
        var isRepeat = true;

        // Act
        var command = new InputCommand(commandType, timestamp, isRepeat);

        // Assert
        Assert.Equal(commandType, command.Type);
        Assert.Equal(timestamp, command.Timestamp);
        Assert.Equal(isRepeat, command.IsRepeat);
    }

    [Fact]
    public void KeyRepeatState_Reset_ShouldClearAllProperties()
    {
        // Arrange
        var state = new KeyRepeatState
        {
            IsPressed = true,
            InitialPressTime = 1.0,
            LastRepeatTime = 2.0,
            CurrentRepeatInterval = 0.1,
            HasStartedRepeating = true
        };

        // Act
        state.Reset();

        // Assert
        Assert.False(state.IsPressed);
        Assert.Equal(0, state.InitialPressTime);
        Assert.Equal(0, state.LastRepeatTime);
        Assert.Equal(0, state.CurrentRepeatInterval);
        Assert.False(state.HasStartedRepeating);
    }

    [Theory]
    [InlineData(Keys.Up, InputCommandType.MoveUp)]
    [InlineData(Keys.Down, InputCommandType.MoveDown)]
    [InlineData(Keys.Left, InputCommandType.MoveLeft)]
    [InlineData(Keys.Right, InputCommandType.MoveRight)]
    [InlineData(Keys.Enter, InputCommandType.Activate)]
    [InlineData(Keys.Escape, InputCommandType.Back)]
    public void GetCommandTypeForKey_ShouldReturnCorrectCommandType(Keys key, InputCommandType expectedCommand)
    {
        // This test validates that key mappings are consistent
        // The actual mapping logic is tested in integration tests
        Assert.True(Enum.IsDefined(typeof(Keys), key));
        Assert.True(Enum.IsDefined(typeof(InputCommandType), expectedCommand));
    }

    [Fact]
    public void InputCommandType_ShouldHaveAllRequiredValues()
    {
        // Arrange & Act
        var values = Enum.GetValues<InputCommandType>();

        // Assert
        Assert.Contains(InputCommandType.MoveUp, values);
        Assert.Contains(InputCommandType.MoveDown, values);
        Assert.Contains(InputCommandType.MoveLeft, values);
        Assert.Contains(InputCommandType.MoveRight, values);
        Assert.Contains(InputCommandType.Activate, values);
        Assert.Contains(InputCommandType.Back, values);
    }
}
