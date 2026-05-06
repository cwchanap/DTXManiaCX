using DTXMania.Game.Lib.Input;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace DTXMania.Test.Input
{
    public class InputManagerScrollSpeedKeysTests
    {
        [Theory]
        [InlineData(Keys.PageUp, InputCommandType.IncreaseScrollSpeed)]
        [InlineData(Keys.PageDown, InputCommandType.DecreaseScrollSpeed)]
        [Trait("Category", "Unit")]
        public void DefaultMapping_Key_WhenQueried_ShouldMapToExpectedScrollCommand(Keys key, InputCommandType expected)
        {
            var im = new InputManager();
            var snapshot = im.GetKeyMappingSnapshot();
            Assert.True(snapshot.TryGetValue(key, out var cmd));
            Assert.Equal(expected, cmd);
        }
    }
}
