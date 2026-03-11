using DTXMania.Game.Lib.Input;

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

    private sealed class TestableInputManager : InputManager
    {
        public void QueueCommand(InputCommandType commandType, double timestamp, bool isRepeat = false)
        {
            EnqueueCommand(new InputCommand(commandType, timestamp, isRepeat));
        }
    }
}
