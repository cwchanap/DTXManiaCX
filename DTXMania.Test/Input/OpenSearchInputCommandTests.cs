using DTXMania.Game.Lib.Input;
using Xunit;

namespace DTXMania.Test.Input
{
    public class OpenSearchInputCommandTests
    {
        [Fact]
        public void OpenSearch_ExistsInEnum()
        {
            var values = System.Enum.GetValues<InputCommandType>();
            Assert.Contains(InputCommandType.OpenSearch, values);
        }
    }
}
