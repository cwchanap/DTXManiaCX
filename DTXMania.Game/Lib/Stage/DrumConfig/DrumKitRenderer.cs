#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;

namespace DTXMania.Game.Lib.Stage.DrumConfig
{
    /// <summary>
    /// Draws the drum-kit pieces and their selection/focus/hover highlights. Each piece is a
    /// photorealistic 3D render (loaded from the skin, one image per <see cref="DrumZoneShape"/>)
    /// on a transparent background. A highlighted piece gets a yellow glow behind it. Pieces are
    /// fit, without distortion, into a square box centered on each zone (sized to the zone), with
    /// coordinates scaled from <see cref="DrumKitLayout"/> design space to the viewport. If the skin
    /// lacks the art, each piece falls back to a plain filled disc.
    /// </summary>
    public sealed class DrumKitRenderer : IDisposable
    {
        // Fallback-disc colour when the 3D art is missing.
        private static readonly Color BodyColor = new(232, 235, 240);
        // Yellow selection accent, matching the focus ring used elsewhere on the stage.
        private static readonly Color HighlightColor = new(255, 216, 77);

        private readonly Texture2D _circle;
        private readonly ITexture? _cymbal;
        private readonly ITexture? _drum;
        private readonly ITexture? _kick;
        private readonly ITexture? _pedal;
        private readonly ITexture? _hihat;
        private bool _disposed;

        public DrumKitRenderer(GraphicsDevice graphicsDevice, IResourceManager? resourceManager = null)
        {
            _circle = CreateCircleTexture(graphicsDevice, 128);
            _cymbal = TryLoad(resourceManager, TexturePath.DrumPadCymbal);
            _drum = TryLoad(resourceManager, TexturePath.DrumPadDrum);
            _kick = TryLoad(resourceManager, TexturePath.DrumPadKick);
            _pedal = TryLoad(resourceManager, TexturePath.DrumPadPedal);
            _hihat = TryLoad(resourceManager, TexturePath.DrumPadHiHat);
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

        /// <summary>Fallback-disc colour: yellow when highlighted, else the neutral body colour.</summary>
        private static Color BodyColorFor(bool highlighted) => highlighted ? HighlightColor : BodyColor;

        private ITexture? ShapeTexture(DrumZoneShape shape) => shape switch
        {
            DrumZoneShape.Cymbal => _cymbal,
            DrumZoneShape.Drum => _drum,
            DrumZoneShape.Kick => _kick,
            DrumZoneShape.Pedal => _pedal,
            DrumZoneShape.HiHatPedal => _hihat,
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
                // Square draw box centered on the zone (sized to its larger radius) so the 3D
                // pieces sit at a sensible size and aspect regardless of the zone's hit-ellipse.
                float half = Math.Max(zone.RadiusX, zone.RadiusY);
                var box = new Rectangle(
                    (int)((zone.CenterX - half) * sx),
                    (int)((zone.CenterY - half) * sy),
                    (int)(half * 2 * sx),
                    (int)(half * 2 * sy));

                bool highlight = zone.Lane == selectedLane || zone.Lane == focusedLane || zone.Lane == hoveredLane;

                var tex = ShapeTexture(zone.Shape);
                if (tex?.Texture != null)
                {
                    // Fit the piece into the box without distortion.
                    var imgRect = FitCentered(box, tex.Width, tex.Height);

                    if (highlight)
                    {
                        // Soft yellow glow behind the selected/focused/hovered piece.
                        var glow = imgRect;
                        glow.Inflate((int)(imgRect.Width * 0.16f), (int)(imgRect.Height * 0.16f));
                        spriteBatch.Draw(_circle, glow, HighlightColor * 0.6f);
                    }

                    var effects = zone.FlipHorizontal ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                    tex.Draw(spriteBatch, imgRect, null, Color.White, 0f, Vector2.Zero, effects, 0f);
                }
                else
                {
                    // Skin lacks the art: plain disc in the body colour.
                    spriteBatch.Draw(_circle, box, BodyColorFor(highlight));
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
            _hihat?.RemoveReference();
            _disposed = true;
        }
    }
}
