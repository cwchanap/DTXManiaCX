using System;
using DTXMania.Game.Lib.Input;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace DTXMania.Test.Input;

[Trait("Category", "Unit")]
public class InjectedKeyboardStateTests
{
    private static readonly int[] None = Array.Empty<int>();

    [Fact]
    public void Merge_WithNoInjectedKeys_ShouldReturnHardwareStatesUnchanged()
    {
        var hardwareCurrent = new KeyboardState(new[] { Keys.Up }, capsLock: true, numLock: true);
        var previous = new KeyboardState(Keys.Down);

        var (current, adjustedPrevious) = InjectedKeyboardState.Merge(hardwareCurrent, previous, None, None);

        Assert.Equal(hardwareCurrent, current);
        Assert.Equal(previous, adjustedPrevious);
    }

    [Fact]
    public void Merge_WithNullInputManager_ShouldReturnHardwareStatesUnchanged()
    {
        var hardwareCurrent = new KeyboardState(Keys.Enter);
        var previous = new KeyboardState();

        var (current, adjustedPrevious) = InjectedKeyboardState.Merge(hardwareCurrent, previous, input: null);

        Assert.Equal(hardwareCurrent, current);
        Assert.Equal(previous, adjustedPrevious);
    }

    [Fact]
    public void Merge_WithInjectedPress_ShouldReadAsRisingEdgeAlongsideHardwareKeys()
    {
        var hardwareCurrent = new KeyboardState(new[] { Keys.LeftShift }, capsLock: true, numLock: false);

        var (current, previous) = InjectedKeyboardState.Merge(
            hardwareCurrent, new KeyboardState(), new[] { (int)Keys.Down }, new[] { (int)Keys.Down });

        Assert.True(current.IsKeyDown(Keys.Down));
        Assert.True(current.IsKeyDown(Keys.LeftShift));
        Assert.True(current.CapsLock);
        Assert.False(previous.IsKeyDown(Keys.Down));
    }

    [Fact]
    public void Merge_WhenInjectedPressAndReleaseDrainInSameFrame_ShouldStillReadAsRisingEdge()
    {
        // The release already cleared the held state, so only the pressed-this-frame set has it.
        var (current, previous) = InjectedKeyboardState.Merge(
            new KeyboardState(), new KeyboardState(), None, new[] { (int)Keys.Enter });

        Assert.True(current.IsKeyDown(Keys.Enter));
        Assert.False(previous.IsKeyDown(Keys.Enter));
    }

    [Fact]
    public void Merge_WhenInjectedKeyStaysHeld_ShouldNotReadAsRisingEdgeAgain()
    {
        var (frame1, _) = InjectedKeyboardState.Merge(
            new KeyboardState(), new KeyboardState(), new[] { (int)Keys.Down }, new[] { (int)Keys.Down });

        var (frame2, frame2Previous) = InjectedKeyboardState.Merge(
            new KeyboardState(), frame1, new[] { (int)Keys.Down }, None);

        Assert.True(frame2.IsKeyDown(Keys.Down));
        Assert.True(frame2Previous.IsKeyDown(Keys.Down));
    }

    [Fact]
    public void Merge_WhenInjectedTapFollowsTapFromLastFrame_ShouldReadAsNewRisingEdge()
    {
        // Two sub-frame taps on consecutive frames: last frame's merged state still shows Down.
        var lastFrame = new KeyboardState(Keys.Down, Keys.Enter);

        var (current, previous) = InjectedKeyboardState.Merge(
            new KeyboardState(), lastFrame, None, new[] { (int)Keys.Down });

        Assert.True(current.IsKeyDown(Keys.Down));
        Assert.False(previous.IsKeyDown(Keys.Down));
        Assert.True(previous.IsKeyDown(Keys.Enter));
    }

    [Fact]
    public void Merge_WhenHardwareAlreadyHoldsInjectedKey_ShouldNotForceRisingEdge()
    {
        var held = new KeyboardState(Keys.Down);

        var (current, previous) = InjectedKeyboardState.Merge(
            held, held, new[] { (int)Keys.Down }, new[] { (int)Keys.Down });

        Assert.True(current.IsKeyDown(Keys.Down));
        Assert.True(previous.IsKeyDown(Keys.Down));
    }
}
