using DTXMania.Game.Lib.Input;
using Xunit;

namespace DTXMania.Test.Input
{
    public class OpenSearchInputCommandTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void OpenSearch_WhenGettingEnum_ShouldExist()
        {
            var values = System.Enum.GetValues<InputCommandType>();
            Assert.Contains(InputCommandType.OpenSearch, values);
        }
    }
}
