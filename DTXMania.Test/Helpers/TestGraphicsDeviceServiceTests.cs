using System;
using Xunit;

namespace DTXMania.Test.Helpers;

[Collection("GraphicsTests")]
public sealed class TestGraphicsDeviceServiceTests
{
    [Fact]
    public void RequiredDevice_WhenOptInDisabled_ShouldThrowAccurateMessage()
    {
        var previous = Environment.GetEnvironmentVariable(GraphicsTestGate.OptInVariable);
        try
        {
            Environment.SetEnvironmentVariable(GraphicsTestGate.OptInVariable, null);
            using var service = new TestGraphicsDeviceService();

            var ex = Assert.Throws<InvalidOperationException>(() => _ = service.RequiredDevice);
            Assert.Contains("is not set to 1", ex.Message);
            Assert.DoesNotContain("=1 was set", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(GraphicsTestGate.OptInVariable, previous);
        }
    }
}
