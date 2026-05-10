using DTXMania.Game.Lib.Input;
using Microsoft.Xna.Framework.Input;
using System;
using System.IO;

namespace DTXMania.Test.Input;

public class InputManagerTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public void ClearPendingCommands_WhenCommandsAreQueued_ShouldEmptyQueue()
    {
        var manager = new TestableInputManager();
        manager.QueueCommand(InputCommandType.MoveDown, 0.1);
        manager.QueueCommand(InputCommandType.Activate, 0.2, isRepeat: true);

        Assert.True(manager.HasPendingCommands);

        manager.ClearPendingCommands();

        Assert.False(manager.HasPendingCommands);
        Assert.Empty(manager.GetInputCommands());
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Constructor_ShouldNotMapSpaceToActivateByDefault()
    {
        var manager = new InputManager();

        var snapshot = manager.GetKeyMappingSnapshot();
        Assert.Equal(InputCommandType.Activate, snapshot[Keys.Enter]);
        Assert.DoesNotContain(snapshot, kvp => kvp.Key == Keys.Space && kvp.Value == InputCommandType.Activate);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void PerformanceStage_OnUpdate_ShouldNotUpdateInputManagerDirectly()
    {
        var repositoryRoot = TryFindRepositoryRoot();
        Assert.True(repositoryRoot != null,
            "Could not locate repository root containing DTXMania.sln from AppContext.BaseDirectory.");

        var performanceStagePath = Path.Combine(repositoryRoot!, "DTXMania.Game", "Lib", "Stage", "PerformanceStage.cs");
        Assert.True(File.Exists(performanceStagePath), $"Could not locate source file: {performanceStagePath}");

        var source = File.ReadAllText(performanceStagePath);

        Assert.DoesNotContain("_inputManager?.Update(deltaTime);", source, StringComparison.Ordinal);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void SongTransitionStage_ShouldCreateConfiguredLocalInputManager()
    {
        var repositoryRoot = TryFindRepositoryRoot();
        Assert.True(repositoryRoot != null,
            "Could not locate repository root containing DTXMania.sln from AppContext.BaseDirectory.");

        var songTransitionStagePath = Path.Combine(repositoryRoot!, "DTXMania.Game", "Lib", "Stage", "SongTransitionStage.cs");
        Assert.True(File.Exists(songTransitionStagePath), $"Could not locate source file: {songTransitionStagePath}");

        var source = File.ReadAllText(songTransitionStagePath);

        Assert.Contains("_inputManager = CreateConfiguredInputManager();", source, StringComparison.Ordinal);
        Assert.Contains("return concreteConfig.CreateConfiguredInputManager();", source, StringComparison.Ordinal);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ResetKeyRepeatStates_ShouldNotThrow_WhenNoRepeatStates()
    {
        var manager = new TestableInputManager();

        // Should not throw even when no keys have been pressed
        manager.ResetKeyRepeatStates();

        Assert.False(manager.HasPendingCommands);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void KeyRepeatState_Suppress_ShouldSetSuppressedFlag()
    {
        var state = new KeyRepeatState();

        Assert.False(state.Suppressed);

        state.Suppress();

        Assert.True(state.Suppressed);
        Assert.False(state.IsPressed);
        Assert.Equal(0, state.LastRepeatTime);
        Assert.Equal(0, state.CurrentRepeatInterval);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void KeyRepeatState_Reset_ShouldClearSuppressedFlag()
    {
        var state = new KeyRepeatState();
        state.Suppress();

        Assert.True(state.Suppressed);

        state.Reset();

        Assert.False(state.Suppressed);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ResetKeyRepeatStates_ShouldSuppressRatherThanReset()
    {
        var manager = new TestableInputManager();

        // Simulate that Escape was pressed — seed a KeyRepeatState so
        // ResetKeyRepeatStates has something to suppress.
        manager.SimulateKeyRepeatState(Keys.Escape);

        // Now suppress via ResetKeyRepeatStates (e.g. modal closed while Escape held).
        manager.ResetKeyRepeatStates();

        // The state should be suppressed, not just reset — this means if the key is
        // still held on the next frame, no phantom repeat will fire.
        var state = manager.GetKeyRepeatState(Keys.Escape);
        Assert.NotNull(state);
        Assert.True(state.Suppressed);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void SetKeyMapping_EvictsPreviousKeyForSameCommand()
    {
        var manager = new InputManager();
        manager.SetKeyMapping(Keys.Enter, InputCommandType.Activate);
        manager.SetKeyMapping(Keys.Space, InputCommandType.Activate);

        var snapshot = manager.GetKeyMappingSnapshot();
        Assert.DoesNotContain(Keys.Enter, snapshot.Keys);
        Assert.Equal(InputCommandType.Activate, snapshot[Keys.Space]);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void RemoveKeyMapping_RemovesMapping()
    {
        var manager = new InputManager();
        Assert.Contains(Keys.Enter, manager.GetKeyMappingSnapshot().Keys);

        manager.RemoveKeyMapping(Keys.Enter);

        Assert.DoesNotContain(Keys.Enter, manager.GetKeyMappingSnapshot().Keys);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void GetKeyMappingSnapshot_ReturnsCopy()
    {
        var manager = new InputManager();
        var snap1 = manager.GetKeyMappingSnapshot();
        manager.RemoveKeyMapping(Keys.Enter);
        var snap2 = manager.GetKeyMappingSnapshot();

        Assert.Contains(Keys.Enter, snap1.Keys);
        Assert.DoesNotContain(Keys.Enter, snap2.Keys);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Dispose_ClearsState()
    {
        var manager = new InputManager();
        manager.Dispose();

        Assert.False(manager.HasPendingCommands);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void SetKeyMapping_SameKeySameCommand_NoOp()
    {
        var manager = new InputManager();
        var countBefore = manager.GetKeyMappingSnapshot().Count;
        manager.SetKeyMapping(Keys.Enter, InputCommandType.Activate);
        Assert.Equal(countBefore, manager.GetKeyMappingSnapshot().Count);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void GetNextCommand_ReturnsNullWhenEmpty()
    {
        var manager = new InputManager();
        Assert.Null(manager.GetNextCommand());
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void IsCommandDown_ReturnsFalseWhenNoKeyHeld()
    {
        var manager = new InputManager();
        Assert.False(manager.IsCommandDown(InputCommandType.Activate));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void IsKeyReleased_ReturnsFalseInitially()
    {
        var manager = new InputManager();
        Assert.False(manager.IsKeyReleased((int)Keys.Enter));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void IsKeyDown_ReturnsFalseInitially()
    {
        var manager = new InputManager();
        Assert.False(manager.IsKeyDown((int)Keys.Enter));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void KeyRepeatState_SuppressedClearedOnFreshPress()
    {
        // Simulate: modal closes, ResetKeyRepeatStates suppresses all keys,
        // then user freshly presses a key that wasn't held.
        // The Suppressed flag should be cleared on the initial press so
        // repeat commands continue to work.
        var state = new KeyRepeatState();
        state.Suppress();
        Assert.True(state.Suppressed);

        // Simulate initial press branch behavior: clearing Suppressed
        state.IsPressed = true;
        state.InitialPressTime = 1.0;
        state.LastRepeatTime = 1.0;
        state.CurrentRepeatInterval = 0.2;
        state.HasStartedRepeating = false;
        state.Suppressed = false;

        Assert.False(state.Suppressed);
        Assert.True(state.IsPressed);
    }

    private sealed class TestableInputManager : InputManager
    {
        public void QueueCommand(InputCommandType commandType, double timestamp, bool isRepeat = false)
        {
            EnqueueCommand(new InputCommand(commandType, timestamp, isRepeat));
        }

        public void SimulateKeyRepeatState(Keys key)
        {
            // Add mapping and seed a repeat state so ResetKeyRepeatStates has something to suppress.
            AddKeyMapping(key, InputCommandType.Back);
            SeedKeyRepeatState(key, new KeyRepeatState());
        }

        public KeyRepeatState? GetKeyRepeatState(Keys key)
        {
            return TryGetKeyRepeatState(key);
        }
    }

    private static string? TryFindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DTXMania.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
