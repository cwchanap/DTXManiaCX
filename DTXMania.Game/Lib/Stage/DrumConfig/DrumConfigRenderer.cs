#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.Utilities;
using L = DTXMania.Game.Lib.UI.Layout.DrumConfigUILayout;

namespace DTXMania.Game.Lib.Stage.DrumConfig
{
    public enum DrumStatusTone { Hint, Accent, Success, Danger }

    /// <summary>
    /// The status line under the bindings list: <see cref="Text"/> in the tone color, then an
    /// optional <see cref="Emphasis"/> in primary text and a <see cref="Suffix"/> in the tone color.
    /// </summary>
    public readonly record struct DrumStatusLine(string Text, DrumStatusTone Tone, string? Emphasis = null, string? Suffix = null);

    /// <summary>Everything the renderer needs for one frame; built by the stage.</summary>
    public sealed class DrumConfigView
    {
        public IReadOnlyList<DrumBindingRow> Rows { get; init; } = Array.Empty<DrumBindingRow>();
        public int SelectedLane { get; init; } = -1;
        public bool ResetFocused { get; init; }
        public int HoveredLane { get; init; } = -1;
        public bool ResetHovered { get; init; }
        public int ListeningLane { get; init; } = -1;
        /// <summary>Per lane: 1 right after a hit, fading to 0.</summary>
        public IReadOnlyList<float> Flash { get; init; } = Array.Empty<float>();
        /// <summary>Per lane: the last hit's readout ("HIT  MIDI 38  VEL 96").</summary>
        public IReadOnlyList<string?> FlashText { get; init; } = Array.Empty<string?>();
        public DrumStatusLine Status { get; init; }
        public (string Text, bool Connected) Midi { get; init; }
        public bool Listening => ListeningLane >= 0;
        public float FlashOf(int lane) => lane >= 0 && lane < Flash.Count ? Flash[lane] : 0f;
        public string? FlashTextOf(int lane) => lane >= 0 && lane < FlashText.Count ? FlashText[lane] : null;
    }

    /// <summary>
    /// Paints the Drum Key Mapping page in the CX Neon look: chamfered navy panels over the
    /// backdrop, the kit illustration with lane tags, the bindings rows and the footer hints.
    /// Owns its fonts, textures and the 1px/circle/ring/glow primitives. Skin art is optional:
    /// a missing backdrop falls back to the strip navy, a missing kit picture to lane-colored discs.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public sealed class DrumConfigRenderer : IDisposable
    {
        private readonly IResourceManager _resourceManager;
        private readonly ILogger? _logger;
        private readonly SkinPalette _palette;
        private readonly Texture2D _white;
        private readonly Texture2D _circle;
        private readonly Texture2D _glow;
        private readonly Texture2D _ring;
        private readonly IFont? _titleFont;
        private readonly IFont? _labelFont;
        private readonly IFont? _monoFont;
        private readonly IFont? _serifFont;
        private readonly ITexture? _background;
        private readonly ITexture? _kitArt;
        private bool _disposed;

        public DrumConfigRenderer(GraphicsDevice device, IResourceManager resourceManager, ILogger? logger = null)
        {
            _resourceManager = resourceManager;
            _logger = logger;
            _palette = SkinPalette.Resolve(resourceManager.CurrentTheme);

            _white = new Texture2D(device, 1, 1);
            _white.SetData(new[] { Color.White });
            _circle = CreateRadialTexture(device, 64, soft: false);
            _glow = CreateRadialTexture(device, 128, soft: true);
            _ring = CreateRingTexture(device, 128, thickness: 3f);

            _titleFont = TryLoadFont(L.DisplayFontFamily, L.TitleFontSize);
            _labelFont = TryLoadFont(L.DisplayFontFamily, L.LabelFontSize);
            _monoFont = TryLoadFont(L.MonoFontFamily, L.MonoFontSize);
            _serifFont = TryLoadFont(L.SerifFontFamily, L.SerifFontSize);

            _background = TryLoadTexture(TexturePath.StartupBackground);
            _kitArt = TryLoadTexture(TexturePath.DrumKitSet);
        }

        /// <summary>Width of <paramref name="text"/> in the mono face (chip layout uses it).</summary>
        public float MeasureMono(string text) => _monoFont?.MeasureString(text).X ?? text.Length * 8f;

        public void Draw(SpriteBatch sb, DrumConfigView view)
        {
            DrawBackdrop(sb);
            DrawHeader(sb, view);
            DrawKit(sb, view);
            DrawBindings(sb, view);
            DrawFooter(sb, view);
        }

        // ---- Sections ----

        private void DrawBackdrop(SpriteBatch sb)
        {
            var full = new Rectangle(0, 0, GameConstants.Display.VirtualWidth, GameConstants.Display.VirtualHeight);
            if (_background?.Texture != null)
                sb.Draw(_background.Texture, full, Color.White);
            else
                sb.Draw(_white, full, _palette.StripBackground);
            sb.Draw(_white, full, L.BackdropScrim);
        }

        private void DrawHeader(SpriteBatch sb, DrumConfigView view)
        {
            Panel(sb, L.HeaderPanel, L.PanelChamfer);
            Text(sb, _titleFont, L.Title, L.TitlePosition, _palette.TextPrimary);
            Text(sb, _monoFont, L.Subtitle, L.SubtitlePosition, _palette.TextTertiary);

            // MIDI caption, status dot and device name, right-aligned to MidiGroupRight. The mono
            // face is Latin-only, so a non-ASCII name (a Japanese device) uses the serif.
            var (deviceText, connected) = view.Midi;
            var nameFont = DisplayText.IsAsciiDisplayable(deviceText) ? _monoFont : _serifFont ?? _monoFont;
            var name = nameFont != null ? TextHelper.TruncateToWidth(deviceText, L.MidiDeviceMaxWidth, nameFont) : deviceText;
            float nameWidth = nameFont?.MeasureString(name).X ?? name.Length * 8f;
            int captionWidth = CaptionWidth("MIDI");
            int x = L.MidiGroupRight - (int)MathF.Ceiling(nameWidth) - 10 - L.StatusDotSize - 10 - captionWidth;
            Caption(sb, "MIDI", x, L.MidiGroupCenterY - (L.CaptionHeight / 2));
            x += captionWidth + 10;
            var dot = new Rectangle(x, L.MidiGroupCenterY - (L.StatusDotSize / 2), L.StatusDotSize, L.StatusDotSize);
            if (connected)
                sb.Draw(_glow, Inflate(dot, 6), _palette.Success * 0.5f);
            sb.Draw(_circle, dot, connected ? _palette.Success : _palette.TextFaint);
            x += L.StatusDotSize + 10;
            TextCenteredY(sb, nameFont, name, x, L.MidiGroupCenterY, _palette.TextSecondary);

            // Reset to defaults: a danger-outlined button; focus adds a full-strength ring.
            var reset = L.ResetButton;
            bool hot = view.ResetFocused || view.ResetHovered;
            if (view.ResetFocused)
                ChamferedBox.Draw(sb, _white, Inflate(reset, 3), 8, Color.Transparent, _palette.Danger * 0.45f);
            ChamferedBox.Draw(sb, _white, reset, 6, new Color(0x10, 0x19, 0x2B) * 0.92f,
                _palette.Danger * (hot ? 1f : 0.65f));
            TextCentered(sb, _labelFont, L.ResetLabel, reset, hot ? _palette.TextPrimary : _palette.TextSecondary);
        }

        private void DrawKit(SpriteBatch sb, DrumConfigView view)
        {
            Panel(sb, L.KitPanel, L.PanelChamfer);
            int captionWidth = Caption(sb, "KIT", L.KitCaption.X, L.KitCaption.Y);
            TextCenteredY(sb, _monoFont, L.KitHint, L.KitCaption.X + captionWidth + 12,
                L.KitCaption.Y + (L.CaptionHeight / 2), _palette.TextTertiary);

            // Halos go under the picture so they read as a glow around the piece's edges;
            // rings, hit tints and tags go over it.
            foreach (var piece in DrumKitLayout.Pieces)
            {
                int lane = piece.Lane;
                var box = piece.Bounds;
                float flash = view.FlashOf(lane);
                if (lane == view.SelectedLane)
                    sb.Draw(_glow, Inflate(box, box.Width / 4), _palette.Accent * 0.5f);
                else if (lane == view.HoveredLane)
                    sb.Draw(_glow, Inflate(box, box.Width / 5), _palette.TextSecondary * 0.2f);
                if (flash > 0)
                    sb.Draw(_glow, Inflate(box, box.Width / 4), PerformanceUILayout.GetLaneColor(lane) * (0.8f * flash));
            }

            if (_kitArt?.Texture != null)
                sb.Draw(_kitArt.Texture, L.KitArt, Color.White);

            foreach (var piece in DrumKitLayout.Pieces)
            {
                int lane = piece.Lane;
                var laneColor = PerformanceUILayout.GetLaneColor(lane);
                var box = piece.Bounds;
                bool selected = lane == view.SelectedLane;
                float flash = view.FlashOf(lane);

                if (_kitArt?.Texture == null)
                    sb.Draw(_circle, Inflate(box, -box.Width / 6), laneColor * 0.35f);
                if (flash > 0)
                    sb.Draw(_glow, box, laneColor * (0.45f * flash));
                if (selected)
                {
                    // The art already has cyan rims, so selection adds a tint and a double ring.
                    sb.Draw(_glow, box, _palette.Accent * 0.3f);
                    sb.Draw(_ring, Inflate(box, 4), _palette.Accent);
                    sb.Draw(_ring, Inflate(box, 10), _palette.Accent * 0.35f);
                }
                else if (flash > 0)
                    sb.Draw(_ring, box, laneColor * flash);
                else if (lane == view.HoveredLane)
                    sb.Draw(_ring, box, _palette.TextSecondary * 0.45f);

                LaneTag(sb, piece.TagRect, lane, selected, flash > 0);
            }
        }

        private void DrawBindings(SpriteBatch sb, DrumConfigView view)
        {
            Panel(sb, L.ListPanel, L.PanelChamfer);
            int captionWidth = Caption(sb, "BINDINGS", L.ListCaption.X, L.ListCaption.Y);
            TextCenteredY(sb, _monoFont, L.ListHint, L.ListCaption.X + captionWidth + 12,
                L.ListCaption.Y + (L.CaptionHeight / 2), _palette.TextTertiary);

            foreach (var row in view.Rows)
                DrawRow(sb, view, row);

            DrawStatus(sb, view.Status);
        }

        private void DrawRow(SpriteBatch sb, DrumConfigView view, DrumBindingRow row)
        {
            int lane = row.Lane;
            var laneColor = PerformanceUILayout.GetLaneColor(lane);
            bool selected = lane == view.SelectedLane;
            bool hovered = lane == view.HoveredLane;
            float flash = view.FlashOf(lane);
            var r = row.Bounds;

            Color fill = selected ? L.SelectedRowFill : new Color(0x10, 0x19, 0x2B) * 0.6f;
            Color border = selected ? _palette.Accent
                : hovered ? _palette.TextTertiary * 0.7f
                : _palette.TextTertiary * 0.2f;
            if (flash > 0)
            {
                fill = Color.Lerp(fill, laneColor * 0.6f, 0.3f * flash);
                if (!selected)
                    border = Color.Lerp(border, laneColor, flash);
            }
            if (selected)
                ChamferedBox.Draw(sb, _white, Inflate(r, 2), L.RowChamfer + 2, Color.Transparent, _palette.Accent * 0.25f);
            ChamferedBox.Draw(sb, _white, r, L.RowChamfer, fill, border);

            var tag = new Rectangle(r.X + L.RowTag.X, r.Y + L.RowTag.Y, L.RowTag.Width, L.RowTag.Height);
            LaneTag(sb, tag, lane, false, false);

            Color nameColor = selected ? _palette.TextPrimary
                : flash > 0 ? Color.Lerp(_palette.TextSecondary, laneColor, flash)
                : _palette.TextSecondary;
            TextCenteredY(sb, _labelFont, KeyBindings.GetLaneName(lane), r.X + L.RowNameOffsetX, r.Center.Y, nameColor);

            foreach (var chip in row.Chips)
                DrawChip(sb, chip);

            if (row.HiddenCount > 0)
                SmallText(sb, $"+{row.HiddenCount}", row.OverflowPosition.X, r.Center.Y, _palette.TextTertiary);

            // Live hit readout at the row's right end, over a backing plate so it stays legible
            // on a full row.
            if (flash > 0 && view.FlashTextOf(lane) is { } flashText)
            {
                float w = MeasureMono(flashText) * L.SmallTextScale;
                int plateX = r.Right - L.FlashTextRightInset - (int)MathF.Ceiling(w) - 8;
                sb.Draw(_white, new Rectangle(plateX, r.Y + 6, r.Right - 4 - plateX, r.Height - 12), L.TagFill * flash);
                var readout = Color.Lerp(laneColor, Color.White, 0.35f) * Math.Min(1f, flash * 1.5f);
                SmallText(sb, flashText, plateX + 8, r.Center.Y, readout);
            }
        }

        private void DrawChip(SpriteBatch sb, DrumChip chip)
        {
            var b = chip.Bounds;
            int cy = b.Center.Y;

            if (chip.Kind == DrumChipKind.Listening)
            {
                ChamferedBox.Draw(sb, _white, b, L.ChipChamfer, L.SelectedTagFill, _palette.Accent);
                int dotX = b.X + L.ChipPadX;
                var dot = new Rectangle(dotX, cy - (L.ListeningDotSize / 2), L.ListeningDotSize, L.ListeningDotSize);
                sb.Draw(_glow, Inflate(dot, 5), _palette.Accent * 0.6f);
                sb.Draw(_circle, dot, _palette.Accent);
                TextCenteredY(sb, _monoFont, chip.Value, dotX + L.ListeningDotSize + L.ListeningDotGap, cy, _palette.Accent);
                return;
            }

            bool midi = chip.Kind == DrumChipKind.Midi;
            Color border = chip.Focused ? _palette.Accent
                : midi ? _palette.Accent * 0.4f
                : _palette.TextSecondary * 0.5f;
            Color fill = chip.Focused ? L.FocusedChipFill : midi ? new Color(0x12, 0x20, 0x3A) : L.ChipFill;
            ChamferedBox.Draw(sb, _white, b, L.ChipChamfer, fill, border);

            float x = b.X + L.ChipPadX;
            x += SmallText(sb, chip.Prefix, x, cy, midi ? _palette.Accent : _palette.TextFaint) + L.ChipPrefixGap;
            x += TextCenteredY(sb, _monoFont, chip.Value, x, cy, _palette.TextPrimary);
            if (chip.Velocity != null)
                SmallText(sb, chip.Velocity, x + L.ChipVelocityGap, cy, chip.Focused ? _palette.TextSecondary : _palette.TextTertiary);

            if (!chip.Focused)
                return;
            if (midi)
            {
                Control(sb, chip.Decrement, "-", L.ControlFill, _palette.TextSecondary * 0.5f);
                Control(sb, chip.Increment, "+", L.ControlFill, _palette.TextSecondary * 0.5f);
            }
            Control(sb, chip.Remove, "x", L.RemoveFill, _palette.Danger * 0.8f);
        }

        private void DrawStatus(SpriteBatch sb, DrumStatusLine status)
        {
            Color tone = status.Tone switch
            {
                DrumStatusTone.Accent => _palette.Accent,
                DrumStatusTone.Success => _palette.Success,
                DrumStatusTone.Danger => _palette.Danger,
                _ => _palette.TextTertiary,
            };
            float x = L.StatusPosition.X;
            float y = L.StatusPosition.Y;
            x += Text(sb, _monoFont, status.Text, new Vector2(x, y), tone);
            if (status.Emphasis != null)
                x += Text(sb, _monoFont, status.Emphasis, new Vector2(x, y), _palette.TextPrimary);
            if (status.Suffix != null)
                Text(sb, _monoFont, status.Suffix, new Vector2(x, y), tone);
        }

        private void DrawFooter(SpriteBatch sb, DrumConfigView view)
        {
            var f = L.FooterPanel;
            Panel(sb, f, L.FooterChamfer);
            int x = f.X + L.FooterPadLeft;
            int cy = f.Center.Y;
            foreach (var (keys, label) in view.Listening ? L.ListeningHints : L.BrowseHints)
            {
                foreach (var key in keys)
                {
                    int w = Math.Max(L.KeycapMinWidth, (int)MathF.Ceiling(MeasureMono(key)) + (L.KeycapPadX * 2));
                    var cap = new Rectangle(x, cy - (L.KeycapHeight / 2), w, L.KeycapHeight);
                    ChamferedBox.Draw(sb, _white, cap, 3, L.ChipFill, _palette.TextSecondary * 0.55f);
                    TextCentered(sb, _monoFont, key, cap, _palette.TextPrimary);
                    x += w + L.KeycapGap;
                }
                x += L.KeycapLabelGap;
                x += (int)MathF.Ceiling(TextCenteredY(sb, _monoFont, label, x, cy, _palette.TextTertiary)) + L.FooterGroupGap;
            }
        }

        // ---- Pieces ----

        private void Panel(SpriteBatch sb, Rectangle r, int chamfer) =>
            ChamferedBox.Draw(sb, _white, r, chamfer, _palette.Panel, _palette.Accent * L.PanelBorderAlpha);

        private int CaptionWidth(string text) =>
            (int)MathF.Ceiling(_labelFont?.MeasureString(text).X ?? text.Length * 9f) + (2 * L.CaptionPadX);

        /// <summary>Accent-outlined caption pill (KIT / BINDINGS / MIDI); returns its width.</summary>
        private int Caption(SpriteBatch sb, string text, int x, int y)
        {
            int w = CaptionWidth(text);
            var pill = new Rectangle(x, y, w, L.CaptionHeight);
            ChamferedBox.Draw(sb, _white, pill, L.CaptionChamfer, _palette.KeyColumn, _palette.Accent);
            TextCentered(sb, _labelFont, text, pill, _palette.TextPrimary);
            return w;
        }

        /// <summary>Lane code tag ("SN") in the lane color; selected/lit tags fill brighter.</summary>
        private void LaneTag(SpriteBatch sb, Rectangle r, int lane, bool selected, bool lit)
        {
            var laneColor = PerformanceUILayout.GetLaneColor(lane);
            Color fill = selected ? L.SelectedTagFill : lit ? Color.Lerp(L.TagFill, laneColor, 0.35f) : L.TagFill;
            ChamferedBox.Draw(sb, _white, r, L.ChipChamfer, fill, selected ? _palette.Accent : laneColor);
            TextCentered(sb, _labelFont, L.LaneCodes[lane], r, selected || lit ? _palette.TextPrimary : laneColor);
        }

        private void Control(SpriteBatch sb, Rectangle r, string glyph, Color fill, Color border)
        {
            if (r.IsEmpty)
                return;
            ChamferedBox.Draw(sb, _white, r, L.ControlChamfer, fill, border);
            TextCentered(sb, _monoFont, glyph, r, _palette.TextPrimary);
        }

        // ---- Text helpers (all return the drawn width) ----

        private static float Text(SpriteBatch sb, IFont? font, string text, Vector2 position, Color color)
        {
            if (font == null || string.IsNullOrEmpty(text))
                return 0f;
            font.DrawString(sb, text, new Vector2(MathF.Round(position.X), MathF.Round(position.Y)), color);
            return font.MeasureString(text).X;
        }

        private static float TextCenteredY(SpriteBatch sb, IFont? font, string text, float x, float centerY, Color color)
        {
            if (font == null)
                return 0f;
            return Text(sb, font, text, new Vector2(x, centerY - (font.LineSpacing / 2f)), color);
        }

        private static void TextCentered(SpriteBatch sb, IFont? font, string text, Rectangle r, Color color)
        {
            if (font == null)
                return;
            var size = font.MeasureString(text);
            Text(sb, font, text, new Vector2(r.Center.X - (size.X / 2f), r.Center.Y - (font.LineSpacing / 2f)), color);
        }

        /// <summary>Mono text at the small-text scale, vertically centered.</summary>
        private float SmallText(SpriteBatch sb, string text, float x, float centerY, Color color)
        {
            if (_monoFont == null || string.IsNullOrEmpty(text))
                return 0f;
            float s = L.SmallTextScale;
            var pos = new Vector2(MathF.Round(x), MathF.Round(centerY - (_monoFont.LineSpacing * s / 2f)));
            _monoFont.DrawString(sb, text, pos, color, 0f, Vector2.Zero, new Vector2(s), SpriteEffects.None, 0f);
            return _monoFont.MeasureString(text).X * s;
        }

        // ---- Geometry ----

        private static Rectangle Inflate(Rectangle r, int amount)
        {
            r.Inflate(amount, amount);
            return r;
        }

        // ---- Resources ----

        private IFont? TryLoadFont(string family, int size)
        {
            try { return _resourceManager.LoadFont(family, size); }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "DrumConfigRenderer: font {Family} {Size} unavailable", family, size);
                return null;
            }
        }

        /// <summary>
        /// Optional skin art. ResourceManager returns a 1x1 white fallback instead of throwing for a
        /// missing asset; stretched, that would wash the page white, so it counts as missing.
        /// </summary>
        private ITexture? TryLoadTexture(string path)
        {
            try
            {
                var texture = _resourceManager.LoadTexture(path);
                if (texture != null && texture.Width > 1 && texture.Height > 1)
                    return texture;
                texture?.RemoveReference();
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "DrumConfigRenderer: optional texture {Path} unavailable", path);
                return null;
            }
            _logger?.LogDebug("DrumConfigRenderer: optional texture {Path} missing or 1x1 fallback", path);
            return null;
        }

        /// <summary>A filled circle, or (soft) a radial glow fading to transparent. Premultiplied.</summary>
        private static Texture2D CreateRadialTexture(GraphicsDevice device, int size, bool soft)
        {
            var data = new Color[size * size];
            float r = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - r + 0.5f;
                    float dy = y - r + 0.5f;
                    float d = MathF.Sqrt((dx * dx) + (dy * dy)) / r;
                    float a = soft
                        ? MathF.Pow(Math.Clamp(1f - d, 0f, 1f), 2f)
                        : Math.Clamp((1f - d) * r, 0f, 1f); // 1px anti-aliased edge
                    data[(y * size) + x] = Color.White * a;
                }
            }
            var texture = new Texture2D(device, size, size);
            texture.SetData(data);
            return texture;
        }

        /// <summary>An anti-aliased circle outline; stretched over a piece's box it becomes an ellipse ring.</summary>
        private static Texture2D CreateRingTexture(GraphicsDevice device, int size, float thickness)
        {
            var data = new Color[size * size];
            float r = size / 2f;
            float mid = r - 1f - (thickness / 2f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - r + 0.5f;
                    float dy = y - r + 0.5f;
                    float d = MathF.Sqrt((dx * dx) + (dy * dy));
                    float a = Math.Clamp((thickness / 2f) + 0.5f - MathF.Abs(d - mid), 0f, 1f);
                    data[(y * size) + x] = Color.White * a;
                }
            }
            var texture = new Texture2D(device, size, size);
            texture.SetData(data);
            return texture;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _white.Dispose();
            _circle.Dispose();
            _glow.Dispose();
            _ring.Dispose();
            _titleFont?.RemoveReference();
            _labelFont?.RemoveReference();
            _monoFont?.RemoveReference();
            _serifFont?.RemoveReference();
            _background?.RemoveReference();
            _kitArt?.RemoveReference();
        }
    }
}
