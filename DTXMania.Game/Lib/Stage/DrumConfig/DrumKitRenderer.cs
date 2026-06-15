#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;

namespace DTXMania.Game.Lib.Stage.DrumConfig
{
    /// <summary>
    /// Draws the drum-kit zones (ellipses), their current binding text, and selection/focus/hover
    /// highlights. Coordinates are scaled from DrumKitLayout design space to the viewport.
    /// </summary>
    public sealed class DrumKitRenderer : IDisposable
    {
        private readonly Texture2D _circle;
        private bool _disposed;

        public DrumKitRenderer(GraphicsDevice graphicsDevice)
        {
            _circle = CreateCircleTexture(graphicsDevice, 128);
        }

        private static Color ZoneColor(DrumZoneShape shape) => shape switch
        {
            DrumZoneShape.Cymbal => new Color(214, 177, 60),
            DrumZoneShape.Drum => new Color(60, 110, 170),
            DrumZoneShape.Kick => new Color(40, 44, 56),
            DrumZoneShape.Pedal => new Color(74, 79, 90),
            _ => Color.Gray
        };

        public void Draw(SpriteBatch spriteBatch, IFont? font, Texture2D whitePixel,
                         KeyBindings bindings, int viewportWidth, int viewportHeight,
                         int selectedLane, int focusedLane, int hoveredLane)
        {
            float sx = viewportWidth / (float)DrumKitLayout.DesignWidth;
            float sy = viewportHeight / (float)DrumKitLayout.DesignHeight;

            foreach (var zone in DrumKitLayout.Zones)
            {
                var dest = new Rectangle(
                    (int)((zone.CenterX - zone.RadiusX) * sx),
                    (int)((zone.CenterY - zone.RadiusY) * sy),
                    (int)(zone.RadiusX * 2 * sx),
                    (int)(zone.RadiusY * 2 * sy));

                bool highlight = zone.Lane == selectedLane || zone.Lane == focusedLane || zone.Lane == hoveredLane;
                if (highlight)
                {
                    var ring = dest;
                    ring.Inflate(5, 5);
                    spriteBatch.Draw(_circle, ring, new Color(255, 216, 77));
                }

                spriteBatch.Draw(_circle, dest, ZoneColor(zone.Shape));

                if (font != null)
                {
                    var labelPos = new Vector2(dest.Center.X - 28, dest.Center.Y - 8);
                    font.DrawString(spriteBatch, KeyBindings.GetLaneName(zone.Lane), labelPos, Color.White);
                    var chipPos = new Vector2(dest.Center.X - 28, dest.Bottom + 2);
                    font.DrawString(spriteBatch, bindings.GetLaneDescription(zone.Lane), chipPos,
                        new Color(200, 220, 235));
                }
            }
        }

        private static Texture2D CreateCircleTexture(GraphicsDevice device, int size)
        {
            var data = new Color[size * size];
            float r = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - r + 0.5f;
                    float dy = y - r + 0.5f;
                    float dist = (float)Math.Sqrt((dx * dx) + (dy * dy));
                    data[(y * size) + x] = dist <= r ? Color.White : Color.Transparent;
                }
            }
            var tex = new Texture2D(device, size, size);
            tex.SetData(data);
            return tex;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _circle.Dispose();
            _disposed = true;
        }
    }
}
