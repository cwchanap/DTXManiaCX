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

    private sealed class TestableInputManager : InputManager
    {
        public void QueueCommand(InputCommandType commandType, double timestamp, bool isRepeat = false)
        {
            EnqueueCommand(new InputCommand(commandType, timestamp, isRepeat));
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
