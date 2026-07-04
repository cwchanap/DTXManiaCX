#nullable enable

using System.Collections.Generic;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;

namespace DTXMania.Game.Lib.Stage.DrumConfig
{
    /// <summary>Visual shape used when drawing a drum zone.</summary>
    public enum DrumZoneShape { Cymbal, Drum, Pedal, Kick, HiHatPedal }

    /// <summary>
    /// One clickable drum-kit zone, mapped 1:1 to a drum lane (0-9).
    /// Geometry is defined in a fixed design space (<see cref="DrumKitLayout.DesignWidth"/> x
    /// <see cref="DrumKitLayout.DesignHeight"/>); callers scale screen/mouse coords into it.
    /// </summary>
    public readonly struct DrumZone
    {
        public int Lane { get; }
        public string Name { get; }
        public DrumZoneShape Shape { get; }
        public float CenterX { get; }
        public float CenterY { get; }
        public float RadiusX { get; }
        public float RadiusY { get; }
        /// <summary>Draw the piece mirrored left-to-right (e.g. the left bass pedal faces the other way).</summary>
        public bool FlipHorizontal { get; }

        public DrumZone(int lane, string name, DrumZoneShape shape,
                        float centerX, float centerY, float radiusX, float radiusY,
                        bool flipHorizontal = false)
        {
            Lane = lane;
            Name = name;
            Shape = shape;
            CenterX = centerX;
            CenterY = centerY;
            RadiusX = radiusX;
            RadiusY = radiusY;
            FlipHorizontal = flipHorizontal;
        }

        /// <summary>Ellipse containment test in design space.</summary>
        public bool Contains(float x, float y)
        {
            var dx = (x - CenterX) / RadiusX;
            var dy = (y - CenterY) / RadiusY;
            return (dx * dx) + (dy * dy) <= 1f;
        }
    }

    /// <summary>
    /// Static drum-kit zone layout. Lane names come from <see cref="KeyBindings.GetLaneName"/>
    /// so the visual labels stay in sync with the binding model.
    /// </summary>
    public static class DrumKitLayout
    {
        public const int DesignWidth = GameConstants.Display.VirtualWidth;
        public const int DesignHeight = GameConstants.Display.VirtualHeight;

        public static IReadOnlyList<DrumZone> Zones { get; } = new[]
        {
            // Positions roughly follow the mounts on the connected rack skeleton (drumkit_skeleton.png).
            new DrumZone(5, KeyBindings.GetLaneName(5), DrumZoneShape.Cymbal,     160f, 178f, 70f, 30f),  // Hi-Hat (left stand)
            new DrumZone(0, KeyBindings.GetLaneName(0), DrumZoneShape.Cymbal,     330f,  92f, 72f, 28f),  // Splash/Crash (left arm)
            new DrumZone(9, KeyBindings.GetLaneName(9), DrumZoneShape.Cymbal,    1130f, 110f, 75f, 28f),  // Ride (right arm)
            new DrumZone(7, KeyBindings.GetLaneName(7), DrumZoneShape.Drum,       545f, 300f, 46f, 46f),  // High Tom
            new DrumZone(8, KeyBindings.GetLaneName(8), DrumZoneShape.Drum,       735f, 300f, 50f, 50f),  // Low Tom
            new DrumZone(4, KeyBindings.GetLaneName(4), DrumZoneShape.Drum,       380f, 430f, 54f, 54f),  // Snare
            new DrumZone(1, KeyBindings.GetLaneName(1), DrumZoneShape.Drum,      1015f, 430f, 60f, 60f),  // Floor Tom (right)
            // Lane 6 ("Bass Drum") is the clickable right bass pedal; the bass drum itself is drawn as decoration.
            new DrumZone(6, KeyBindings.GetLaneName(6), DrumZoneShape.Pedal,      712f, 600f, 52f, 36f),  // Bass pedal (right)
            new DrumZone(2, KeyBindings.GetLaneName(2), DrumZoneShape.HiHatPedal, 175f, 598f, 52f, 42f),  // Hi-Hat Foot (left)
            // Left pedal mirrors the right one so its beater faces the other way.
            new DrumZone(3, KeyBindings.GetLaneName(3), DrumZoneShape.Pedal,      560f, 600f, 52f, 36f, flipHorizontal: true), // Left Pedal
        };

        /// <summary>Returns the lane of the first zone containing the design-space point, or -1.</summary>
        public static int HitTest(float x, float y)
        {
            foreach (var zone in Zones)
            {
                if (zone.Contains(x, y))
                    return zone.Lane;
            }
            return -1;
        }

        // ---- Keyboard focus order (zones 0..ZoneCount-1, then the Reset action) ----
        // The design specifies a "stable focus sequence over the 10 zones plus the Reset action"
        // that is unit-testable and Mac-safe. Kept here, as pure data/math, so the stage wires
        // input to focus without owning the geometry or order.

        /// <summary>Number of drum zones. Derived from <see cref="Zones"/> so the focus math can
        /// never desync from the actual zone list if a zone is added or removed.</summary>
        public static int ZoneCount => Zones.Count;

        /// <summary>Focus index of the Reset-to-defaults action (immediately after the last zone).</summary>
        public static int ResetActionIndex => ZoneCount;

        /// <summary>Total keyboard-focusable elements: the zones plus the Reset action.</summary>
        public static int FocusableCount => ZoneCount + 1;

        /// <summary>True when <paramref name="focusIndex"/> points at the Reset-to-defaults action.</summary>
        public static bool IsResetAction(int focusIndex) => focusIndex == ResetActionIndex;

        /// <summary>
        /// Advances the keyboard focus by <paramref name="delta"/> (+1 forward / -1 back) with
        /// wraparound across every focusable element (zones 0..ZoneCount-1 then Reset). Pure math
        /// so the focus order is unit-testable without a GraphicsDevice.
        /// </summary>
        public static int AdvanceFocus(int currentIndex, int delta)
            => ((currentIndex % FocusableCount) + (delta % FocusableCount) + FocusableCount) % FocusableCount;
    }
}
