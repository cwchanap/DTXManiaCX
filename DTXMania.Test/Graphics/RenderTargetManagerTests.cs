using DTX.Graphics;

namespace DTXMania.Test.Graphics;

public class RenderTargetManagerTests
{
    [Fact]
    public void RenderTargetManager_Constructor_WithNullGraphicsDevice_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RenderTargetManager(null!));
    }

    // Note: Most RenderTargetManager tests require a real GraphicsDevice instance
    // which is difficult to create in unit tests without a full MonoGame context.
    // The constructor null check test above covers the main validation logic.
    // Integration tests would be more appropriate for testing the full functionality.
}
