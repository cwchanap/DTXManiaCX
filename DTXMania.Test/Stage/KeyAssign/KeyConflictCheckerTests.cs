using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.KeyAssign;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;

namespace DTXMania.Test.Stage.KeyAssign;

public class KeyConflictCheckerTests
{
    // ─── CheckSystemConflict ───────────────────────────────────────────────────

    [Fact]
    public void CheckSystemConflict_KeyNotBound_ShouldReturnNull()
    {
        var systemBindings = new Dictionary<Keys, InputCommandType>
        {
            [Keys.Up] = InputCommandType.MoveUp
        };
        var result = KeyConflictChecker.CheckSystemConflict(systemBindings, Keys.Down);
        Assert.Null(result);
    }

    [Fact]
    public void CheckSystemConflict_KeyIsBound_ShouldReturnMessage()
    {
        var systemBindings = new Dictionary<Keys, InputCommandType>
        {
            [Keys.Up] = InputCommandType.MoveUp
        };
        var result = KeyConflictChecker.CheckSystemConflict(systemBindings, Keys.Up);
        Assert.NotNull(result);
        Assert.Contains("MoveUp", result);
    }

    // ─── CheckDrumConflict ────────────────────────────────────────────────────

    [Fact]
    public void CheckDrumConflict_KeyNotBound_ShouldReturnNull()
    {
        var drumBindings = new Dictionary<string, int>
        {
            ["Key.S"] = 4   // Snare
        };
        var result = KeyConflictChecker.CheckDrumConflict(drumBindings, Keys.A);
        Assert.Null(result);
    }

    [Fact]
    public void CheckDrumConflict_KeyIsBound_ShouldReturnMessage()
    {
        var drumBindings = new Dictionary<string, int>
        {
            ["Key.S"] = 4   // Snare
        };
        var result = KeyConflictChecker.CheckDrumConflict(drumBindings, Keys.S);
        Assert.NotNull(result);
        Assert.Contains("Snare", result);
    }

    // ─── CheckSystemAssignConflict ────────────────────────────────────────────

    [Fact]
    public void CheckSystemAssignConflict_NoDrumNorSystemConflict_ShouldReturnNull()
    {
        var drumBindings = new Dictionary<string, int> { ["Key.S"] = 4 };
        var systemBindings = new Dictionary<Keys, InputCommandType> { [Keys.Up] = InputCommandType.MoveUp };

        var result = KeyConflictChecker.CheckSystemAssignConflict(
            drumBindings, systemBindings, Keys.A, InputCommandType.Activate);

        Assert.Null(result);
    }

    [Fact]
    public void CheckSystemAssignConflict_KeyIsDrumKey_ShouldReturnMessage()
    {
        var drumBindings = new Dictionary<string, int> { ["Key.S"] = 4 };
        var systemBindings = new Dictionary<Keys, InputCommandType>();

        var result = KeyConflictChecker.CheckSystemAssignConflict(
            drumBindings, systemBindings, Keys.S, InputCommandType.Activate);

        Assert.NotNull(result);
        Assert.Contains("drum", result);
    }

    [Fact]
    public void CheckSystemAssignConflict_KeyBoundToDifferentSystemAction_ShouldReturnMessage()
    {
        var drumBindings = new Dictionary<string, int>();
        var systemBindings = new Dictionary<Keys, InputCommandType>
        {
            [Keys.Up] = InputCommandType.MoveUp
        };

        var result = KeyConflictChecker.CheckSystemAssignConflict(
            drumBindings, systemBindings, Keys.Up, InputCommandType.Activate);

        Assert.NotNull(result);
        Assert.Contains("MoveUp", result);
    }

    [Fact]
    public void CheckSystemAssignConflict_KeyBoundToSameSystemAction_ShouldReturnNull()
    {
        // Re-binding the same key to the same action is not a conflict
        var drumBindings = new Dictionary<string, int>();
        var systemBindings = new Dictionary<Keys, InputCommandType>
        {
            [Keys.Up] = InputCommandType.MoveUp
        };

        var result = KeyConflictChecker.CheckSystemAssignConflict(
            drumBindings, systemBindings, Keys.Up, InputCommandType.MoveUp);

        Assert.Null(result);
    }
}
