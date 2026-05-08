using DTXMania.Game.Lib.Input;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace DTXMania.Test.Input
{
    public class OpenSearchInputCommandTests
    {
        [Fact]
        public void DefaultMapping_BackspaceMapsToOpenSearch()
        {
            var mgr = new InputManager();
            var snapshot = mgr.GetKeyMappingSnapshot();

            Assert.True(snapshot.ContainsKey(Keys.Back));
            Assert.Equal(InputCommandType.OpenSearch, snapshot[Keys.Back]);
        }

        [Fact]
        public void OpenSearch_ExistsInEnum()
        {
            // Ensure the enum value is defined
            var values = System.Enum.GetValues<InputCommandType>();
            Assert.Contains(InputCommandType.OpenSearch, values);
        }
    }
}
