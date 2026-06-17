#nullable enable

using System.Collections.Generic;
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

        public DrumZone(int lane, string name, DrumZoneShape shape,
                        float centerX, float centerY, float radiusX, float radiusY)
        {
            Lane = lane;
            Name = name;
            Shape = shape;
            CenterX = centerX;
            CenterY = centerY;
            RadiusX = radiusX;
            RadiusY = radiusY;
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
        public const int DesignWidth = 1280;
        public const int DesignHeight = 720;

        public static IReadOnlyList<DrumZone> Zones { get; } = new[]
        {
            new DrumZone(5, KeyBindings.GetLaneName(5), DrumZoneShape.Cymbal, 166f, 158f, 70f, 22f),
            new DrumZone(0, KeyBindings.GetLaneName(0), DrumZoneShape.Cymbal, 435f,  86f, 75f, 22f),
            new DrumZone(9, KeyBindings.GetLaneName(9), DrumZoneShape.Cymbal, 1024f, 115f, 78f, 22f),
            new DrumZone(7, KeyBindings.GetLaneName(7), DrumZoneShape.Drum,   525f, 295f, 42f, 42f),
            new DrumZone(8, KeyBindings.GetLaneName(8), DrumZoneShape.Drum,   755f, 295f, 46f, 46f),
            new DrumZone(4, KeyBindings.GetLaneName(4), DrumZoneShape.Drum,   346f, 432f, 50f, 50f),
            new DrumZone(1, KeyBindings.GetLaneName(1), DrumZoneShape.Drum,  1037f, 418f, 56f, 56f),
            new DrumZone(6, KeyBindings.GetLaneName(6), DrumZoneShape.Kick,   627f, 526f, 80f, 80f),
            new DrumZone(2, KeyBindings.GetLaneName(2), DrumZoneShape.HiHatPedal, 179f, 619f, 48f, 18f),
            new DrumZone(3, KeyBindings.GetLaneName(3), DrumZoneShape.Pedal,  422f, 641f, 48f, 18f),
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

        /// <summary>Number of drum zones (lanes 0-9). Mirrors <see cref="Zones"/>'s count.</summary>
        public const int ZoneCount = 10;

        /// <summary>Focus index of the Reset-to-defaults action (immediately after the last zone).</summary>
        public const int ResetActionIndex = ZoneCount;

        /// <summary>Total keyboard-focusable elements: the zones plus the Reset action.</summary>
        public const int FocusableCount = ZoneCount + 1;

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
