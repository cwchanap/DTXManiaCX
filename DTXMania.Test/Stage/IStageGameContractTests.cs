using DTXMania.Game;
using DTXMania.Test.TestData;
using Xunit;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class IStageGameContractTests
    {
        private const string IStageGameFullName = "DTXMania.Game.Lib.Stage.IStageGame";

        [Fact]
        public void BaseGame_ShouldImplementIStageGame()
        {
            var iface = typeof(BaseGame).GetInterface(IStageGameFullName);
            Assert.NotNull(iface);
        }

        [Fact]
        public void IStageGame_ShouldDeclareMapMouseToVirtualAndGetTextInputSourceAndRequestExit()
        {
            var iface = typeof(BaseGame).GetInterface(IStageGameFullName)!;
            Assert.NotNull(iface.GetMethod("MapMouseToVirtual"));
            Assert.NotNull(iface.GetMethod("GetTextInputSource"));
            Assert.NotNull(iface.GetMethod("RequestExit"));
        }

        [Fact]
        public void GetTextInputSource_ShouldReturnNull_InHeadlessEnvironment()
        {
            // ReflectionHelpers.CreateGame builds an uninitialized BaseGame with no graphics
            // manager (and thus no OS window), modeling the headless/test environment the
            // search modal must tolerate.
            var game = ReflectionHelpers.CreateGame();
            Assert.Null(game.GetTextInputSource());
        }
    }
}
