using System.Collections.Generic;
using DTXMania.Game.Lib.Input;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Test.Input;

[Trait("Category", "Unit")]
public class InputManagerStateTests
{
    [Fact]
    public void IsCommandDown_WhenMappedKeyIsHeld_ReturnsTrue()
    {
        var manager = new InputManager();
        SetKeyboardStates(manager, new KeyboardState(Keys.Up), new KeyboardState());

        Assert.True(manager.IsCommandDown(InputCommandType.MoveUp));
        Assert.False(manager.IsCommandDown(InputCommandType.Back));
    }

    [Fact]
    public void IsKeyPressed_WhenKeyTransitionsDown_ReturnsTrue()
    {
        var manager = new InputManager();
        SetKeyboardStates(manager, new KeyboardState(Keys.Enter), new KeyboardState());

        Assert.True(manager.IsKeyPressed((int)Keys.Enter));
    }

    [Fact]
    public void IsKeyTriggered_WhenKeyTransitionsDown_ReturnsTrue()
    {
        var manager = new InputManager();
        SetKeyboardStates(manager, new KeyboardState(Keys.Left), new KeyboardState());

        Assert.True(manager.IsKeyTriggered((int)Keys.Left));
    }

    [Fact]
    public void IsKeyReleased_WhenKeyTransitionsUp_ReturnsTrue()
    {
        var manager = new InputManager();
        SetKeyboardStates(manager, new KeyboardState(), new KeyboardState(Keys.Right));

        Assert.True(manager.IsKeyReleased((int)Keys.Right));
    }

    [Fact]
    public void IsKeyDown_WhenKeyIsHeld_ReturnsTrue()
    {
        var manager = new InputManager();
        SetKeyboardStates(manager, new KeyboardState(Keys.Down), new KeyboardState(Keys.Down));

        Assert.True(manager.IsKeyDown((int)Keys.Down));
    }

    [Fact]
    public void IsBackActionTriggered_WhenEscapeTransitionsDown_ReturnsTrue()
    {
        var manager = new InputManager();
        SetKeyboardStates(manager, new KeyboardState(Keys.Escape), new KeyboardState());

        Assert.True(manager.IsBackActionTriggered());
    }

    [Fact]
    public void UpdateKeyRepeatStates_WhenKeyJustPressed_QueuesInitialCommandAndSeedsRepeatState()
    {
        var manager = new InputManager();
        SetKeyboardStates(manager, new KeyboardState(Keys.Up), new KeyboardState());
        SetCurrentTime(manager, 0.5);

        InvokeUpdateKeyRepeatStates(manager);

        var command = Assert.Single(manager.GetInputCommands());
        Assert.Equal(InputCommandType.MoveUp, command.Type);
        Assert.False(command.IsRepeat);
        Assert.Equal(0.5, command.Timestamp);

        var state = GetRepeatState(manager, Keys.Up);
        Assert.True(state.IsPressed);
        Assert.Equal(0.5, state.InitialPressTime);
        Assert.Equal(0.5, state.LastRepeatTime);
        Assert.Equal(0.2, state.CurrentRepeatInterval);
        Assert.False(state.HasStartedRepeating);
    }

    [Fact]
    public void UpdateKeyRepeatStates_WhenHeldPastRepeatDelay_QueuesRepeatCommandAndAccelerates()
    {
        var manager = new InputManager();
        SetKeyboardStates(manager, new KeyboardState(Keys.Up), new KeyboardState(Keys.Up));
        SetCurrentTime(manager, 0.31);

        GetRepeatStates(manager)[Keys.Up] = new KeyRepeatState
        {
            IsPressed = true,
            InitialPressTime = 0.0,
            LastRepeatTime = 0.0,
            CurrentRepeatInterval = 0.2,
            HasStartedRepeating = false
        };

        InvokeUpdateKeyRepeatStates(manager);

        var command = Assert.Single(manager.GetInputCommands());
        Assert.Equal(InputCommandType.MoveUp, command.Type);
        Assert.True(command.IsRepeat);
        Assert.Equal(0.31, command.Timestamp);

        var state = GetRepeatState(manager, Keys.Up);
        Assert.True(state.HasStartedRepeating);
        Assert.Equal(0.31, state.LastRepeatTime);
        Assert.InRange(state.CurrentRepeatInterval, 0.05, 0.2);
    }

    [Fact]
    public void UpdateKeyRepeatStates_WhenKeyIsReleased_ResetsRepeatState()
    {
        var manager = new InputManager();
        SetKeyboardStates(manager, new KeyboardState(), new KeyboardState(Keys.Up));
        SetCurrentTime(manager, 0.42);

        GetRepeatStates(manager)[Keys.Up] = new KeyRepeatState
        {
            IsPressed = true,
            InitialPressTime = 0.1,
            LastRepeatTime = 0.3,
            CurrentRepeatInterval = 0.12,
            HasStartedRepeating = true
        };

        InvokeUpdateKeyRepeatStates(manager);

        Assert.Empty(manager.GetInputCommands());

        var state = GetRepeatState(manager, Keys.Up);
        Assert.False(state.IsPressed);
        Assert.Equal(0, state.InitialPressTime);
        Assert.Equal(0, state.LastRepeatTime);
        Assert.Equal(0, state.CurrentRepeatInterval);
        Assert.False(state.HasStartedRepeating);
    }

    private static void SetKeyboardStates(InputManager manager, KeyboardState current, KeyboardState previous)
    {
        ReflectionHelpers.SetPrivateField(manager, "_currentKeyboardState", current);
        ReflectionHelpers.SetPrivateField(manager, "_previousKeyboardState", previous);
    }

    private static void SetCurrentTime(InputManager manager, double currentTime)
    {
        ReflectionHelpers.SetPrivateField(manager, "_currentTime", currentTime);
    }

    private static void InvokeUpdateKeyRepeatStates(InputManager manager)
    {
        ReflectionHelpers.InvokePrivateMethod(manager, "UpdateKeyRepeatStates");
    }

    private static Dictionary<Keys, KeyRepeatState> GetRepeatStates(InputManager manager)
    {
        return ReflectionHelpers.GetPrivateField<Dictionary<Keys, KeyRepeatState>>(manager, "_keyRepeatStates")!;
    }

    private static KeyRepeatState GetRepeatState(InputManager manager, Keys key)
    {
        return GetRepeatStates(manager)[key];
    }
}
