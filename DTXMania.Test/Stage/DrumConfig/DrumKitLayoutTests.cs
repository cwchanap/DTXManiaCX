using System.Linq;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.DrumConfig;
using Xunit;

namespace DTXMania.Test.Stage.DrumConfig
{
    [Trait("Category", "Unit")]
    public class DrumKitLayoutTests
    {
        [Fact]
        public void Zones_CoverAllTenLanesExactlyOnce()
        {
            var lanes = DrumKitLayout.Zones.Select(z => z.Lane).OrderBy(l => l).ToArray();
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, lanes);
        }

        [Fact]
        public void Zone_NameMatchesKeyBindingsLaneName()
        {
            var snare = DrumKitLayout.Zones.Single(z => z.Lane == 4);
            Assert.Equal(KeyBindings.GetLaneName(4), snare.Name);
        }

        [Fact]
        public void HitTest_AtSnareCenter_ReturnsLane4()
        {
            var snare = DrumKitLayout.Zones.Single(z => z.Lane == 4);
            Assert.Equal(4, DrumKitLayout.HitTest(snare.CenterX, snare.CenterY));
        }

        [Fact]
        public void HitTest_FarOutsideAnyZone_ReturnsMinusOne()
        {
            Assert.Equal(-1, DrumKitLayout.HitTest(2f, 2f));
        }

        [Fact]
        public void Zone_Contains_EllipseContainmentInDesignSpace()
        {
            var snare = DrumKitLayout.Zones.Single(z => z.Lane == 4);
            
            // Center should be contained
            Assert.True(snare.Contains(snare.CenterX, snare.CenterY));
            
            // Point just outside should not be contained
            Assert.False(snare.Contains(snare.CenterX + snare.RadiusX + 1, snare.CenterY));
            
            // Point far outside should not be contained
            Assert.False(snare.Contains(0f, 0f));
        }

        [Fact]
        public void AdvanceFocus_ForwardFromLastZone_LandsOnResetAction()
        {
            // The Reset action sits right after the last zone in the focus sequence.
            Assert.Equal(DrumKitLayout.ResetActionIndex,
                DrumKitLayout.AdvanceFocus(DrumKitLayout.ZoneCount - 1, +1));
        }

        [Fact]
        public void AdvanceFocus_ForwardFromResetAction_WrapsToFirstZone()
        {
            Assert.Equal(0, DrumKitLayout.AdvanceFocus(DrumKitLayout.ResetActionIndex, +1));
        }

        [Fact]
        public void AdvanceFocus_BackwardFromFirstZone_WrapsToResetAction()
        {
            // Left/Shift navigation from the first zone wraps back to the Reset action.
            Assert.Equal(DrumKitLayout.ResetActionIndex, DrumKitLayout.AdvanceFocus(0, -1));
        }

        [Fact]
        public void IsResetAction_OnlyTrueForResetActionIndex()
        {
            Assert.True(DrumKitLayout.IsResetAction(DrumKitLayout.ResetActionIndex));
            Assert.False(DrumKitLayout.IsResetAction(0));
            Assert.False(DrumKitLayout.IsResetAction(DrumKitLayout.ZoneCount - 1));
        }
    }
}
