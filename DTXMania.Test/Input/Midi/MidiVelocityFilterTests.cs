using DTXMania.Game.Lib.Input.Midi;
using Xunit;

namespace DTXMania.Test.Input.Midi;

[Trait("Category", "Input")]
public sealed class MidiVelocityFilterTests
{
    [Fact]
    public void ShouldAcceptPress_DefaultThreshold_AllowsVelocityOne()
    {
        var filter = new MidiVelocityFilter(_ => 0);

        Assert.True(filter.ShouldAcceptPress(36, 1));
    }

    [Fact]
    public void ShouldAcceptPress_ZeroVelocity_IsRejectedAsPress()
    {
        var filter = new MidiVelocityFilter(_ => 0);

        Assert.False(filter.ShouldAcceptPress(36, 0));
    }

    [Fact]
    public void ShouldAcceptPress_VelocityEqualThreshold_IsRejected()
    {
        var filter = new MidiVelocityFilter(_ => 20);

        Assert.False(filter.ShouldAcceptPress(36, 20));
    }

    [Fact]
    public void ShouldAcceptPress_VelocityAboveThreshold_IsAccepted()
    {
        var filter = new MidiVelocityFilter(_ => 20);

        Assert.True(filter.ShouldAcceptPress(36, 21));
    }

    [Fact]
    public void ShouldAcceptPress_ThresholdProviderOutOfRange_IsClamped()
    {
        var high = new MidiVelocityFilter(_ => 300);
        var low = new MidiVelocityFilter(_ => -10);

        Assert.False(high.ShouldAcceptPress(36, 127));
        Assert.True(low.ShouldAcceptPress(36, 1));
    }

    [Fact]
    public void ShouldAcceptPress_InvalidNoteNumber_IsRejected()
    {
        var filter = new MidiVelocityFilter(_ => 0);

        Assert.False(filter.ShouldAcceptPress(-1, 100));
        Assert.False(filter.ShouldAcceptPress(128, 100));
    }
}
