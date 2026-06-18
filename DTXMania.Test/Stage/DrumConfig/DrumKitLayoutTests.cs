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

        [Fact]
        public void Zone_AllPropertiesAreReadableForEveryZone()
        {
            // Reads every struct property (Lane/Name/Shape/Center/Radius/FlipHorizontal) so the
            // auto-property accessors stay covered, including the rarely-read FlipHorizontal.
            foreach (var zone in DrumKitLayout.Zones)
            {
                Assert.InRange(zone.Lane, 0, DrumKitLayout.ZoneCount - 1);
                Assert.Equal(KeyBindings.GetLaneName(zone.Lane), zone.Name);
                Assert.True(zone.RadiusX > 0 && zone.RadiusY > 0);
                _ = zone.Shape;
                _ = zone.CenterX;
                _ = zone.CenterY;
                _ = zone.FlipHorizontal;
            }
        }

        [Fact]
        public void Zone_LeftPedalIsFlippedAndRightPedalIsNot()
        {
            // The left pedal (lane 3) mirrors the right so its beater faces the other way.
            var leftPedal = DrumKitLayout.Zones.Single(z => z.Lane == 3);
            var rightPedal = DrumKitLayout.Zones.Single(z => z.Lane == 6);
            Assert.True(leftPedal.FlipHorizontal);
            Assert.False(rightPedal.FlipHorizontal);
        }
    }
}
