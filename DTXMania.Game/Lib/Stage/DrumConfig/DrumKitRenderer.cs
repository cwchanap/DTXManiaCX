#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;

namespace DTXMania.Game.Lib.Stage.DrumConfig
{
    /// <summary>
    /// The three "highlight a zone" lanes the renderer draws a yellow glow for: the selected
    /// (popup-open), keyboard-focused, and mouse-hovered lane. Each defaults to -1 (no highlight).
    /// </summary>
    /// <remarks>
    /// Bundled as a struct with <c>init</c>-only properties (not a positional constructor) so the
    /// caller must name each lane at construction — eliminating the transposition hazard that three
    /// adjacent <c>int</c> parameters on <see cref="DrumKitRenderer.Draw"/> carried (swapping, say,
    /// focused and hovered would compile silently). <see cref="IsHighlighted"/> co-locates the
    /// glow test so the three lanes are read in exactly one place.
    /// </remarks>
    public readonly struct LaneHighlights
    {
        // Explicit parameterless constructor is required (CS8983) because the init properties have
        // initializers; it runs them so every LaneHighlights starts with all three lanes at -1.
        public LaneHighlights() { }

        /// <summary>Lane whose piece popup is open, or -1 for none.</summary>
        public int SelectedLane { get; init; } = -1;
        /// <summary>Lane under keyboard focus, or -1 for none.</summary>
        public int FocusedLane { get; init; } = -1;
        /// <summary>Lane under the mouse cursor, or -1 for none.</summary>
        public int HoveredLane { get; init; } = -1;

        /// <summary>True when <paramref name="lane"/> matches any of the three highlight lanes.</summary>
        public bool IsHighlighted(int lane)
            => lane == SelectedLane || lane == FocusedLane || lane == HoveredLane;
    }

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

        public void Draw(SpriteBatch spriteBatch, KeyBindings bindings, IFont? font,
                         Texture2D? whitePixel, int viewportWidth, int viewportHeight,
                         in LaneHighlights highlights)
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

                bool highlight = highlights.IsHighlighted(zone.Lane);

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

                // At-a-glance binding label for this zone (e.g. "S", "Space", "MIDI 36", or
                // "Unbound"), drawn from the working KeyBindings the stage passes each frame. This
                // is the parameter the renderer previously received but never read; users can now
                // see every mapping without opening each popup. Skipped when the font/pixel are
                // unavailable; a translucent dark pill keeps the text legible over any artwork.
                DrawZoneLabel(spriteBatch, font, whitePixel, bindings, box, viewportHeight, zone.Lane);
            }
        }

        // Backdrop colour for a binding label: translucent black so white text reads over art.
        private static readonly Color LabelBackdrop = new(0, 0, 0, 165);
        private const int LabelPadX = 6;
        private const int LabelPadY = 3;
        private const int LabelGap = 6; // px between the piece's box and the label pill

        /// <summary>
        /// Draws one zone's binding description as a centered label just below its piece (flipped
        /// above when it would clip the bottom of the viewport). Graphics-only; exercised via the
        /// stage's <see cref="Draw"/>. The pure geometry lives in <see cref="LabelRectFor"/> so it
        /// is unit-testable without a GraphicsDevice.
        /// </summary>
        private static void DrawZoneLabel(SpriteBatch spriteBatch, IFont? font, Texture2D? whitePixel,
            KeyBindings? bindings, Rectangle anchor, int viewportHeight, int lane)
        {
            if (font == null || whitePixel == null || bindings == null)
                return;

            var text = bindings.GetLaneDescription(lane);
            var size = font.MeasureString(text);
            if (size.X <= 0 || size.Y <= 0)
                return;

            var rect = LabelRectFor(anchor, size, viewportHeight);
            spriteBatch.Draw(whitePixel, rect, LabelBackdrop);
            var pos = new Vector2(rect.Center.X - (size.X / 2f), rect.Center.Y - (size.Y / 2f));
            font.DrawString(spriteBatch, text, pos, Color.White);
        }

        /// <summary>
        /// Viewport-space rectangle of the translucent label pill for a zone, given the piece's
        /// draw box and the measured text size. Sits just below the piece; flips above it when the
        /// pill would run past the bottom of the viewport (the bass/hi-hat pedals are near the
        /// bottom edge). Pure so the placement is unit-testable without a GraphicsDevice.
        /// </summary>
        private static Rectangle LabelRectFor(Rectangle anchor, Vector2 textSize, int viewportHeight)
        {
            int w = Math.Max(1, (int)textSize.X + (LabelPadX * 2));
            int h = Math.Max(1, (int)textSize.Y + (LabelPadY * 2));
            int x = anchor.Center.X - (w / 2);
            int y = anchor.Bottom + LabelGap;
            if (y + h > viewportHeight)        // would clip below the viewport: flip above the piece
                y = anchor.Top - h - LabelGap;
            if (y < 0) y = 0;
            return new Rectangle(x, y, w, h);
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
