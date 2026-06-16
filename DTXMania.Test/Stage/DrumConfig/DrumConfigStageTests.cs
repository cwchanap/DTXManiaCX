using System.Reflection;
using DTXMania.Game.Lib.Stage;
using Xunit;

namespace DTXMania.Test.Stage.DrumConfig
{
    [Trait("Category", "Unit")]
    public class DrumConfigStageTests
    {
        [Fact]
        public void GetResetButtonRect_ReturnsCorrectPosition()
        {
            // Act - Use reflection to test the private static method
            var method = typeof(DrumConfigStage).GetMethod("GetResetButtonRect", 
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            
            var rect = (Microsoft.Xna.Framework.Rectangle)method!.Invoke(null, new object[] { 1280, 720 });

            // Assert
            Assert.Equal(1280 - 210, rect.X);
            Assert.Equal(12, rect.Y);
            Assert.Equal(190, rect.Width);
            Assert.Equal(30, rect.Height);
        }
    }
}
