using DTX.Config;
using DTX.Stage;
using DTX.Input;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace DTXMania.Test.Stage;

public class KeyboardRepeatTests
{


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


}
