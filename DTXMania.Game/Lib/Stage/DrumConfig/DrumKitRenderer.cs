#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;

namespace DTXMania.Game.Lib.Stage.DrumConfig
{
    /// <summary>
    /// Draws the drum-kit pieces and their selection/focus/hover highlights. Each piece is black
    /// line-art (loaded from the skin, one image per <see cref="DrumZoneShape"/>) with a transparent
    /// interior; the renderer fills a light "head" behind it so the black detail reads on the dark
    /// stage, and lights that head yellow when the piece is highlighted. Coordinates are scaled from
    /// <see cref="DrumKitLayout"/> design space to the viewport. If the skin lacks the art, each
    /// piece falls back to a plain filled disc in the same body colour.
    /// </summary>
    public sealed class DrumKitRenderer : IDisposable
    {
        // Near-white head so the black line-art is visible on the black stage.
        private static readonly Color BodyColor = new(232, 235, 240);
        // Yellow selection accent, matching the focus ring used elsewhere on the stage.
        private static readonly Color HighlightColor = new(255, 216, 77);

        private readonly Texture2D _circle;
        private readonly ITexture? _cymbal;
        private readonly ITexture? _drum;
        private readonly ITexture? _kick;
        private readonly ITexture? _pedal;
        private bool _disposed;

        public DrumKitRenderer(GraphicsDevice graphicsDevice, IResourceManager? resourceManager = null)
        {
            _circle = CreateCircleTexture(graphicsDevice, 128);
            _cymbal = TryLoad(resourceManager, TexturePath.DrumPadCymbal);
            _drum = TryLoad(resourceManager, TexturePath.DrumPadDrum);
            _kick = TryLoad(resourceManager, TexturePath.DrumPadKick);
            _pedal = TryLoad(resourceManager, TexturePath.DrumPadPedal);
        }

        private static ITexture? TryLoad(IResourceManager? resourceManager, string path)
        {
            if (resourceManager == null)
                return null;
            try
            {
                return resourceManager.LoadTexture(path);
            }
            catch
            {
                // Missing/invalid art is non-fatal: the zone falls back to a plain disc.
                return null;
            }
        }

        /// <summary>Body colour drawn behind a piece: yellow when highlighted, else the near-white head.</summary>
        private static Color BodyColorFor(bool highlighted) => highlighted ? HighlightColor : BodyColor;

        private ITexture? ShapeTexture(DrumZoneShape shape) => shape switch
        {
            DrumZoneShape.Cymbal => _cymbal,
            DrumZoneShape.Drum => _drum,
            DrumZoneShape.Kick => _kick,
            DrumZoneShape.Pedal => _pedal,
            _ => null
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
                var bodyColor = BodyColorFor(highlight);

                var tex = ShapeTexture(zone.Shape);
                if (tex?.Texture != null)
                {
                    // Pedals are tall art in wide/short zones, so fit them without distortion;
                    // the rounder pieces fill the zone (a stretched cymbal reads as perspective).
                    var imgRect = zone.Shape == DrumZoneShape.Pedal
                        ? FitCentered(dest, tex.Width, tex.Height)
                        : dest;

                    // Light/yellow head behind the transparent interior, then the black detail.
                    spriteBatch.Draw(_circle, imgRect, bodyColor);
                    tex.Draw(spriteBatch, imgRect, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0f);
                }
                else
                {
                    // Skin lacks the art: plain disc in the body colour.
                    spriteBatch.Draw(_circle, dest, bodyColor);
                }
            }
        }

        /// <summary>Largest rectangle of the source's aspect ratio that fits inside <paramref name="bounds"/>, centered.</summary>
        private static Rectangle FitCentered(Rectangle bounds, int srcWidth, int srcHeight)
        {
            if (srcWidth <= 0 || srcHeight <= 0)
                return bounds;
            float scale = Math.Min(bounds.Width / (float)srcWidth, bounds.Height / (float)srcHeight);
            int w = Math.Max(1, (int)(srcWidth * scale));
            int h = Math.Max(1, (int)(srcHeight * scale));
            return new Rectangle(bounds.Center.X - (w / 2), bounds.Center.Y - (h / 2), w, h);
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
            _cymbal?.RemoveReference();
            _drum?.RemoveReference();
            _kick?.RemoveReference();
            _pedal?.RemoveReference();
            _disposed = true;
        }
    }
}
