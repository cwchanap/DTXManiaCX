using System;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using Xunit;

namespace DTXMania.Test.Input;

[Trait("Category", "Unit")]
public class ModularInputManagerInjectionTests : IDisposable
{
    private readonly ConfigManager _configManager;
    private readonly ModularInputManager _manager;

    public ModularInputManagerInjectionTests()
    {
        _configManager = new ConfigManager();
        _manager = new ModularInputManager(_configManager);
    }

    public void Dispose()
    {
        _manager.Dispose();
    }

    [Fact]
    public void InjectButton_WithKeyCodeButtonId_ShouldReturnTrue()
    {
        // "Key.Up" maps via TryGetKeyCode — no lane binding required
        var result = _manager.InjectButton("Key.Up", isPressed: true);
        Assert.True(result);
    }

    [Fact]
    public void InjectButton_WithUnknownButtonId_ShouldReturnFalse()
    {
        var result = _manager.InjectButton("Unknown.Button", isPressed: true);
        Assert.False(result);
    }

    [Fact]
    public void DrainInjectedPressEvents_AfterInjectAndUpdate_ShouldReturnPressedKey()
    {
        _manager.InjectButton("Key.Up", isPressed: true);
        _manager.Update(0.016);

        var events = _manager.DrainInjectedPressEvents();

        Assert.Single(events);
    }

    [Fact]
    public void DrainInjectedPressEvents_CalledTwice_ShouldReturnEmptyOnSecondCall()
    {
        _manager.InjectButton("Key.Down", isPressed: true);
        _manager.Update(0.016);
        _manager.DrainInjectedPressEvents(); // first drain

        var secondDrain = _manager.DrainInjectedPressEvents();

        Assert.Empty(secondDrain);
    }

    [Fact]
    public void ClearInjectedState_BeforeUpdate_ShouldPreventPressEventsFromAppearing()
    {
        _manager.InjectButton("Key.Up", isPressed: true);
        // Do NOT call Update — the button is still in the queue

        _manager.ClearInjectedState();
        _manager.Update(0.016);

        var events = _manager.DrainInjectedPressEvents();
        Assert.Empty(events);
    }

    [Fact]
    public void ClearInjectedState_AfterUpdate_ShouldClearPressEvents()
    {
        // Inject a press and process it so it populates _injectedKeyStates
        _manager.InjectButton("Key.Up", isPressed: true);
        _manager.Update(0.016);

        _manager.ClearInjectedState();

        // After clearing, drain should be empty (press events cleared)
        var events = _manager.DrainInjectedPressEvents();
        Assert.Empty(events);
    }

    [Fact]
    public void InjectButton_WithEmptyOrWhitespaceButtonId_ShouldReturnFalse()
    {
        Assert.False(_manager.InjectButton("", isPressed: true));
        Assert.False(_manager.InjectButton("   ", isPressed: true));
    }
}
