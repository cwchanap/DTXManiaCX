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
        var repositoryRoot = FindRepositoryRoot();
        var performanceStagePath = Path.Combine(repositoryRoot, "DTXMania.Game", "Lib", "Stage", "PerformanceStage.cs");

        var source = File.ReadAllText(performanceStagePath);

        Assert.DoesNotContain("_inputManager?.Update(deltaTime);", source, StringComparison.Ordinal);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void SongTransitionStage_ShouldCreateConfiguredLocalInputManager()
    {
        var repositoryRoot = FindRepositoryRoot();
        var songTransitionStagePath = Path.Combine(repositoryRoot, "DTXMania.Game", "Lib", "Stage", "SongTransitionStage.cs");

        var source = File.ReadAllText(songTransitionStagePath);

        Assert.Contains("_inputManager = CreateConfiguredInputManager();", source, StringComparison.Ordinal);
        Assert.Contains("return concreteConfig.CreateConfiguredInputManager();", source, StringComparison.Ordinal);
    }

    private sealed class TestableInputManager : InputManager
    {
        public void QueueCommand(InputCommandType commandType, double timestamp, bool isRepeat = false)
        {
            EnqueueCommand(new InputCommand(commandType, timestamp, isRepeat));
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "DTXMania.Game"))
                && Directory.Exists(Path.Combine(current.FullName, "DTXMania.Test")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
