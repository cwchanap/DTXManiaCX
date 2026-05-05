using DTXMania.Game.Lib.Input;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace DTXMania.Test.Input
{
    public class InputManagerScrollSpeedKeysTests
    {
        [Fact]
        public void DefaultMapping_PageUp_MapsToIncreaseScrollSpeed()
        {
            var im = new InputManager();
            var snapshot = im.GetKeyMappingSnapshot();
            Assert.True(snapshot.TryGetValue(Keys.PageUp, out var cmd));
            Assert.Equal(InputCommandType.IncreaseScrollSpeed, cmd);
        }

        [Fact]
        public void DefaultMapping_PageDown_MapsToDecreaseScrollSpeed()
        {
            var im = new InputManager();
            var snapshot = im.GetKeyMappingSnapshot();
            Assert.True(snapshot.TryGetValue(Keys.PageDown, out var cmd));
            Assert.Equal(InputCommandType.DecreaseScrollSpeed, cmd);
        }

        [Fact]
        public void Enum_HasIncreaseScrollSpeed()
        {
            Assert.Contains(InputCommandType.IncreaseScrollSpeed, System.Enum.GetValues<InputCommandType>());
        }

        [Fact]
        public void Enum_HasDecreaseScrollSpeed()
        {
            Assert.Contains(InputCommandType.DecreaseScrollSpeed, System.Enum.GetValues<InputCommandType>());
        }
    }
}
