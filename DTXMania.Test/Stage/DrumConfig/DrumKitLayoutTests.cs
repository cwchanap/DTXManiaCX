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
        public void ZoneCount_ShouldEqualNumberOfZonesAndKeepFocusCountsInSync()
        {
            // The focus math derives from the zone list, so these must never drift out of sync.
            Assert.Equal(DrumKitLayout.Zones.Count, DrumKitLayout.ZoneCount);
            Assert.Equal(DrumKitLayout.ZoneCount, DrumKitLayout.ResetActionIndex);
            Assert.Equal(DrumKitLayout.ZoneCount + 1, DrumKitLayout.FocusableCount);
        }

        [Fact]
        public void Zones_CoverAllTenLanesExactlyOnce()
        {
            var lanes = DrumKitLayout.Zones.Select(z => z.Lane).OrderBy(l => l).ToArray();
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, lanes);
        }

        [Fact]
        public void GetLaneForZoneIndex_ShouldReturnEachAuthoredZoneLane()
        {
            var expectedLanes = DrumKitLayout.Zones.Select(zone => zone.Lane).ToArray();

            for (int zoneIndex = 0; zoneIndex < expectedLanes.Length; zoneIndex++)
                Assert.Equal(expectedLanes[zoneIndex], DrumKitLayout.GetLaneForZoneIndex(zoneIndex));

            Assert.Equal(1, DrumKitLayout.GetLaneForZoneIndex(0)); // first focused zone is HH/1
            Assert.Equal(9, DrumKitLayout.GetLaneForZoneIndex(2));
        }

        [Fact]
        public void FindZoneIndexByLane_ShouldRoundTripEveryAuthoredZone()
        {
            for (int zoneIndex = 0; zoneIndex < DrumKitLayout.ZoneCount; zoneIndex++)
            {
                var lane = DrumKitLayout.GetLaneForZoneIndex(zoneIndex);
                Assert.Equal(zoneIndex, DrumKitLayout.FindZoneIndexByLane(lane));
            }

            Assert.Equal(5, DrumKitLayout.FindZoneIndexByLane(3)); // snare sits at focus index 5
            Assert.Equal(0, DrumKitLayout.FindZoneIndexByLane(1)); // HH stands first in focus order
        }

        [Fact]
        public void GetLaneForZoneIndex_ResetAndInvalidIndices_ShouldReturnMinusOne()
        {
            Assert.Equal(-1, DrumKitLayout.GetLaneForZoneIndex(DrumKitLayout.ResetActionIndex));
            Assert.Equal(-1, DrumKitLayout.GetLaneForZoneIndex(-1));
            Assert.Equal(-1, DrumKitLayout.GetLaneForZoneIndex(DrumKitLayout.ZoneCount));
        }

        [Fact]
        public void FindZoneIndexByLane_InvalidLane_ShouldReturnMinusOne()
        {
            Assert.Equal(-1, DrumKitLayout.FindZoneIndexByLane(-1));
            Assert.Equal(-1, DrumKitLayout.FindZoneIndexByLane(10));
        }

        [Fact]
        public void Zone_NameMatchesKeyBindingsLaneName()
        {
            var snare = DrumKitLayout.Zones.Single(z => z.Lane == 3);
            Assert.Equal(KeyBindings.GetLaneName(3), snare.Name);
        }

        [Fact]
        public void FirstFocusedZone_IsHiHatLane1()
        {
            Assert.Equal(1, DrumKitLayout.Zones[0].Lane);
        }

        [Fact]
        public void Zone_ShapesMatchTheirInstruments()
        {
            Assert.Equal(DrumZoneShape.Cymbal, DrumKitLayout.Zones.Single(z => z.Lane == 1).Shape);          // HH stand
            Assert.Equal(DrumZoneShape.Cymbal, DrumKitLayout.Zones.Single(z => z.Lane == 0).Shape);          // Left crash
            Assert.Equal(DrumZoneShape.Cymbal, DrumKitLayout.Zones.Single(z => z.Lane == 9).Shape);          // Ride
            Assert.Equal(DrumZoneShape.Drum, DrumKitLayout.Zones.Single(z => z.Lane == 4).Shape);            // High tom
            Assert.Equal(DrumZoneShape.Drum, DrumKitLayout.Zones.Single(z => z.Lane == 6).Shape);            // Low tom
            Assert.Equal(DrumZoneShape.Drum, DrumKitLayout.Zones.Single(z => z.Lane == 3).Shape);            // Snare
            Assert.Equal(DrumZoneShape.Drum, DrumKitLayout.Zones.Single(z => z.Lane == 7).Shape);            // Floor tom
            Assert.Equal(DrumZoneShape.Pedal, DrumKitLayout.Zones.Single(z => z.Lane == 5).Shape);           // Right bass pedal
            Assert.Equal(DrumZoneShape.HiHatPedal, DrumKitLayout.Zones.Single(z => z.Lane == 2).Shape);      // Hi-hat foot pedal
            Assert.Equal(DrumZoneShape.Cymbal, DrumKitLayout.Zones.Single(z => z.Lane == 8).Shape);          // Right crash (replaces the old mirrored left pedal)
        }

        [Fact]
        public void HitTest_AtEveryZoneCenter_ReturnsThatZonesLane()
        {
            foreach (var zone in DrumKitLayout.Zones)
                Assert.Equal(zone.Lane, DrumKitLayout.HitTest(zone.CenterX, zone.CenterY));
        }

        [Fact]
        public void HitTest_AtSnareCenter_ReturnsLane3()
        {
            var snare = DrumKitLayout.Zones.Single(z => z.Lane == 3);
            Assert.Equal(3, DrumKitLayout.HitTest(snare.CenterX, snare.CenterY));
        }

        [Fact]
        public void HitTest_FarOutsideAnyZone_ReturnsMinusOne()
        {
            Assert.Equal(-1, DrumKitLayout.HitTest(2f, 2f));
        }

        [Fact]
        public void Zone_Contains_EllipseContainmentInDesignSpace()
        {
            var snare = DrumKitLayout.Zones.Single(z => z.Lane == 3);
            
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
            // Reads every struct property (Lane/Name/Shape/Center/Radius) so the
            // auto-property accessors stay covered.
            foreach (var zone in DrumKitLayout.Zones)
            {
                Assert.InRange(zone.Lane, 0, DrumKitLayout.ZoneCount - 1);
                Assert.Equal(KeyBindings.GetLaneName(zone.Lane), zone.Name);
                Assert.True(zone.RadiusX > 0 && zone.RadiusY > 0);
                _ = zone.Shape;
                _ = zone.CenterX;
                _ = zone.CenterY;
            }
        }

        [Fact]
        public void Zones_BoundingBoxesDoNotOverlap()
        {
            // Axis-aligned bounding-box separation is a conservative no-overlap guarantee:
            // if the boxes are apart, the hit ellipses (and drawn sprites) cannot collide.
            for (int i = 0; i < DrumKitLayout.Zones.Count; i++)
            {
                for (int j = i + 1; j < DrumKitLayout.Zones.Count; j++)
                {
                    var a = DrumKitLayout.Zones[i];
                    var b = DrumKitLayout.Zones[j];
                    var separated = System.Math.Abs(a.CenterX - b.CenterX) >= a.RadiusX + b.RadiusX ||
                                    System.Math.Abs(a.CenterY - b.CenterY) >= a.RadiusY + b.RadiusY;
                    Assert.True(separated, $"Zones '{a.Name}' and '{b.Name}' overlap");
                }
            }
        }
    }
}
