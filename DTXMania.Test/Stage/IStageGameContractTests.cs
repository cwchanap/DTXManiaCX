using DTXMania.Game;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI.Components;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
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
        public void GetTextInputSource_ShouldReturnNull_WhenWindowIsUnavailable()
        {
            // ReflectionHelpers.CreateGame builds an uninitialized BaseGame (Window is null),
            // which models the headless/test environment the search modal must tolerate.
            var game = ReflectionHelpers.CreateGame();
            Assert.Null(game.GetTextInputSource());
        }
    }
}
