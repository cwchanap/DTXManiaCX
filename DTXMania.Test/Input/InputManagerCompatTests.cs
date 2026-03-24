using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage;

namespace DTXMania.Test.Input;

[Trait("Category", "Unit")]
public sealed class InputManagerCompatTests : IDisposable
{
    private readonly InputManagerCompat _manager;

    public InputManagerCompatTests()
    {
        _manager = new InputManagerCompat(new ConfigManager());
    }

    public void Dispose()
    {
        _manager.Dispose();
    }

    [Fact]
    public void IsCommandPressed_WhenMoveUpInjected_ShouldReturnTrueAndQueueCommand()
    {
        _manager.ModularInputManager.InjectButton("Key.Up", isPressed: true);

        _manager.Update(0.016);

        Assert.True(_manager.IsCommandPressed(InputCommandType.MoveUp));

        var command = _manager.GetNextCommand();
        Assert.True(command.HasValue);
        Assert.Equal(InputCommandType.MoveUp, command.Value.Type);
        Assert.False(command.Value.IsRepeat);
    }

    [Fact]
    public void IsBackActionTriggered_WhenEscapeInjected_ShouldReturnTrue()
    {
        _manager.ModularInputManager.InjectButton("Key.Escape", isPressed: true);

        _manager.Update(0.016);

        Assert.True(_manager.IsBackActionTriggered());
        Assert.True(_manager.IsCommandPressed(InputCommandType.Back));
    }

    [Fact]
    public void IsMenuSelectTriggered_WhenActivateInjected_ShouldReturnTrue()
    {
        _manager.ModularInputManager.InjectButton("Key.Enter", isPressed: true);

        _manager.Update(0.016);

        Assert.True(TitleStage.IsMenuSelectTriggered(_manager));
        Assert.True(_manager.IsCommandPressed(InputCommandType.Activate));
    }

    [Fact]
    public void IsCommandPressed_OnNextFrameWithoutNewInjection_ShouldResetInjectedCommandState()
    {
        _manager.ModularInputManager.InjectButton("Key.Enter", isPressed: true);
        _manager.Update(0.016);
        Assert.True(_manager.IsCommandPressed(InputCommandType.Activate));

        _manager.Update(0.016);

        Assert.False(_manager.IsCommandPressed(InputCommandType.Activate));
        Assert.False(TitleStage.IsMenuSelectTriggered(_manager));
    }
}
