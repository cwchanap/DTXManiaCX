using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Notation;
using DTXMania.Game.Lib.UI.Layout;
using static DTXMania.Game.Lib.UI.Layout.PerformanceUILayout;
// using-static above pulls PerformanceUILayout.MeasureLine into scope; the chart type wins.
using MeasureLine = DTXMania.Game.Lib.Song.Components.MeasureLine;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Thin scrolling-notation strip renderer (HPA-410). Owns the pinned 60pt Bravura font, the
    /// ShareTechMono-14 label font and a 1x1 white pixel texture, and draws the strip in the
    /// caller's SpriteSortMode.BackToFront batch with the contracted explicit layer depths
    /// (larger depth = farther back). All placement math is delegated to the GPU-free
    /// <see cref="NotationStripLayout"/>; glyphs and labels always go through the nine-argument
    /// IFont.DrawString so no text can default to depth 0 and bypass the key-column mask.
    /// Colors come from the theme-resolved <see cref="TabModePalette"/>: an opaque navy strip
    /// framed by accent rules, a glowing accent hit marker, and lane-colored key caps.
    /// HPA-411 owns activation, chart readiness, judgement tinting and fallbacks.
    /// </summary>
    internal sealed class NotationStripRenderer : IDisposable
    {
        // Font family keys resolved by ManagedFont (path-independent).
        private const string BravuraFontFamily = "Bravura-Notation";
        private const string LabelFontFamily = "ShareTechMono";

        // Task 0 specimen: ShareTechMono-14 at 0.7 (~10px labels) stays sharp and fits the
        // 10px diatonic step grid.
        private const float LabelFontScale = 0.7f;

        private readonly IResourceManager _resourceManager;
        private readonly NotationChart _chart;
        private readonly IReadOnlyList<MeasureLine> _measureLines;
        private readonly TabModePalette _palette;

        // Engraving is pure over the immutable chart, so each measure is engraved at
        // most once; re-engraving every visible measure each frame was steady GC churn.
        private readonly Dictionary<int, NotationStripMeasureEngraving> _engravingCache = new();

        // Staff line ys, key-column labels and barline anchors are pure functions of the
        // immutable chart constants; precompute them once instead of reallocating the
        // LINQ-built arrays/lists every frame.
        private readonly IReadOnlyList<float> _staffLineYs;
        private readonly IReadOnlyList<NotationStripLabelAnchor> _keyColumnLabels;
        private readonly Rectangle[] _keyCapRects;
        private readonly NotationStripAnchor[] _barlineAnchors;

        // Label draw positions depend only on the fixed label font, so they are measured
        // once on the first draw instead of every frame.
        private Vector2[] _labelPositions;

        // SMuFL glyph chars are a small fixed set; cache one string per char so drawing
        // a glyph doesn't allocate a fresh single-char string every frame.
        private readonly Dictionary<char, string> _glyphStrings = new();

        private IFont _bravuraFont;
        private IFont _labelFont;
        private Texture2D _whiteTexture;
        private bool _disposed;

        /// <summary>True when both fonts are acquired and the Bravura coverage check passed.</summary>
        public bool IsReady => !_disposed && _bravuraFont != null && _labelFont != null;

        /// <summary>
        /// Acquires the notation fonts and validates Bravura glyph coverage. On any failure
        /// everything already acquired is released and <see cref="IsReady"/> stays false; the
        /// constructor never throws for a load failure.
        /// </summary>
        /// <param name="resourceManager">Resource manager used to load the fonts.</param>
        /// <param name="chart">The finalized notation chart to engrave.</param>
        /// <param name="measureLines">Compiled measure boundaries; MeasureLines[^1] is the terminal end bound.</param>
        /// <param name="palette">Theme-resolved Tab colors; null uses the built-in defaults.</param>
        public NotationStripRenderer(
            IResourceManager resourceManager,
            NotationChart chart,
            IReadOnlyList<MeasureLine> measureLines,
            TabModePalette palette = null)
        {
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
            _chart = chart ?? throw new ArgumentNullException(nameof(chart));
            _measureLines = measureLines ?? throw new ArgumentNullException(nameof(measureLines));
            _palette = palette ?? TabModePalette.Default;

            _staffLineYs = NotationStripLayout.GetStaffLineYs();
            _keyColumnLabels = NotationStripLayout.GetKeyColumnLabels();
            _keyCapRects = new Rectangle[_keyColumnLabels.Count];
            for (int i = 0; i < _keyCapRects.Length; i++)
                _keyCapRects[i] = NotationStripLayout.GetKeyCapRect(_keyColumnLabels[i]);
            // One barline anchor per measure; MeasureLines[^1] is only an end bound.
            _barlineAnchors = new NotationStripAnchor[Math.Max(0, measureLines.Count - 1)];
            for (int i = 0; i < _barlineAnchors.Length; i++)
            {
                _barlineAnchors[i] = new NotationStripAnchor(
                    measureLines[i].TimeMs,
                    dxPx: 0f,
                    yPx: NotationStrip.StaffTopY);
            }

            try
            {
                _bravuraFont = _resourceManager.LoadFont(
                    BravuraFontFamily, (int)NotationStrip.BravuraFontSizePt);
                _labelFont = _resourceManager.LoadFont(
                    LabelFontFamily, (int)NotationStrip.LabelFontSize);

                // LoadFont silently falls back to NotoSerifJP when the Bravura asset is missing
                // or partial, so coverage must be verified on the returned font before use.
                if (!NotationGlyphs.IsComplete(_bravuraFont.SupportsCharacter))
                    ReleaseResources();
            }
            catch
            {
                ReleaseResources();
            }
        }

        /// <summary>
        /// Draws the strip for the given playback time and scroll speed into the caller's
        /// BackToFront batch. No-op when not ready, the batch is null, or the speed is invalid.
        /// </summary>
        public void Draw(SpriteBatch spriteBatch, double nowMs, int speedPercent)
        {
            if (!IsReady || spriteBatch == null || speedPercent <= 0)
                return;

            if (_whiteTexture == null && !EnsureWhiteTexture(spriteBatch.GraphicsDevice))
                return;

            double pxPerMs = NotationStrip.ScrollPixelsPerMs * (speedPercent / 100.0);
            double leftMs = nowMs + (NotationStrip.StripLeft - NotationStrip.HitMarkerX) / pxPerMs;
            double rightMs = nowMs + (NotationStrip.StripRight - NotationStrip.HitMarkerX) / pxPerMs;

            DrawBackground(spriteBatch);
            DrawStaffLines(spriteBatch);
            DrawBarlines(spriteBatch, leftMs, rightMs, nowMs, speedPercent);
            DrawHitMarker(spriteBatch);
            DrawMeasures(spriteBatch, leftMs, rightMs, nowMs, speedPercent);
            DrawKeyColumnMask(spriteBatch);
            DrawFrame(spriteBatch);
            DrawKeyCaps(spriteBatch);
            DrawLabels(spriteBatch);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            ReleaseResources();
            GC.SuppressFinalize(this);
        }

        private void ReleaseResources()
        {
            _bravuraFont?.RemoveReference();
            _bravuraFont = null;
            _labelFont?.RemoveReference();
            _labelFont = null;
            _whiteTexture?.Dispose();
            _whiteTexture = null;
        }

        private bool EnsureWhiteTexture(GraphicsDevice graphicsDevice)
        {
            try
            {
                _whiteTexture = new Texture2D(graphicsDevice, 1, 1);
                _whiteTexture.SetData(new[] { Color.White });
                return true;
            }
            catch (Exception)
            {
                _whiteTexture = null;
                return false;
            }
        }

        private void DrawRect(SpriteBatch spriteBatch, Rectangle destination, Color color, float depth)
        {
            spriteBatch.Draw(_whiteTexture, destination, null, color, 0f, Vector2.Zero, SpriteEffects.None, depth);
        }

        private void DrawBackground(SpriteBatch spriteBatch)
        {
            DrawRect(
                spriteBatch,
                new Rectangle(
                    NotationStrip.StripLeft,
                    NotationStrip.StripTop,
                    NotationStrip.StripRight - NotationStrip.StripLeft,
                    NotationStrip.StripBottom - NotationStrip.StripTop),
                _palette.StripBackground,
                NotationStrip.DepthBackground);
        }

        private void DrawStaffLines(SpriteBatch spriteBatch)
        {
            foreach (float y in _staffLineYs)
            {
                DrawRect(
                    spriteBatch,
                    new Rectangle(
                        NotationStrip.StripLeft,
                        (int)y,
                        NotationStrip.StripRight - NotationStrip.StripLeft,
                        (int)NotationStrip.StaffLineThickness),
                    _palette.TextFaint * NotationStrip.StaffLineAlpha,
                    NotationStrip.DepthStaffAndBarlines);
            }
        }

        private void DrawBarlines(
            SpriteBatch spriteBatch,
            double leftMs,
            double rightMs,
            double nowMs,
            int speedPercent)
        {
            var window = NotationStripLayout.SelectVisibleMeasures(_measureLines, leftMs, rightMs);
            for (int i = window.FirstIndex; i <= window.LastIndexInclusive; i++)
            {
                var anchor = _barlineAnchors[i];
                int x = (int)NotationStripLayout.ToScreenX(anchor.TimeMs, nowMs, speedPercent);
                if (x + NotationStrip.BarlineThickness < NotationStrip.StripLeft || x > NotationStrip.StripRight)
                    continue;

                DrawRect(
                    spriteBatch,
                    new Rectangle(
                        x,
                        NotationStrip.StaffTopY,
                        (int)NotationStrip.BarlineThickness,
                        NotationStrip.StaffBottomY - NotationStrip.StaffTopY),
                    _palette.TextTertiary,
                    NotationStrip.DepthStaffAndBarlines);
            }
        }

        private void DrawHitMarker(SpriteBatch spriteBatch)
        {
            int height = NotationStrip.StripBottom - NotationStrip.StripTop;
            int glowLeft = NotationStrip.HitMarkerX + NotationStrip.HitMarkerWidth / 2 - NotationStrip.HitMarkerGlowWidth / 2;
            DrawRect(
                spriteBatch,
                new Rectangle(glowLeft, NotationStrip.StripTop, NotationStrip.HitMarkerGlowWidth, height),
                _palette.Accent * NotationStrip.HitMarkerGlowAlpha,
                NotationStrip.DepthHitMarkerGlow);
            DrawRect(
                spriteBatch,
                new Rectangle(
                    NotationStrip.HitMarkerX,
                    NotationStrip.StripTop,
                    NotationStrip.HitMarkerWidth,
                    height),
                _palette.Accent,
                NotationStrip.DepthHitMarker);
        }

        private void DrawMeasures(
            SpriteBatch spriteBatch,
            double leftMs,
            double rightMs,
            double nowMs,
            int speedPercent)
        {
            var window = NotationStripLayout.SelectVisibleMeasures(_measureLines, leftMs, rightMs);
            int last = Math.Min(window.LastIndexInclusive, _chart.Measures.Count - 1);
            for (int i = window.FirstIndex; i <= last; i++)
            {
                if (!_engravingCache.TryGetValue(i, out var engraving))
                {
                    engraving = NotationStripLayout.EngraveMeasure(_chart.Measures[i]);
                    _engravingCache[i] = engraving;
                }

                foreach (var rect in engraving.Rects)
                    DrawEngravedRect(spriteBatch, rect, nowMs, speedPercent);

                foreach (var glyph in engraving.Glyphs)
                    DrawEngravedGlyph(spriteBatch, glyph, nowMs, speedPercent);
            }
        }

        private void DrawEngravedRect(
            SpriteBatch spriteBatch,
            NotationStripRectAnchor rect,
            double nowMs,
            int speedPercent)
        {
            float leftX = (float)NotationStripLayout.ToScreenX(rect.TimeMs, nowMs, speedPercent) + rect.StartDxPx;
            float rightX = rect.EndTimeMs.HasValue
                ? (float)NotationStripLayout.ToScreenX(rect.EndTimeMs.Value, nowMs, speedPercent) + rect.EndDxPx
                : leftX - rect.StartDxPx + rect.EndDxPx;

            if (rightX < NotationStrip.StripLeft || leftX > NotationStrip.StripRight)
                return;

            var destination = new Rectangle(
                (int)leftX,
                (int)rect.TopPx,
                Math.Max(1, (int)(rightX - leftX)),
                Math.Max(1, (int)rect.HeightPx));
            DrawRect(spriteBatch, destination, _palette.TextPrimary, NotationStrip.DepthBeamsAndStems);
        }

        private void DrawEngravedGlyph(
            SpriteBatch spriteBatch,
            NotationStripGlyphAnchor glyph,
            double nowMs,
            int speedPercent)
        {
            float x = (float)NotationStripLayout.ToScreenX(glyph.TimeMs, nowMs, speedPercent) + glyph.DxPx;
            if (x > NotationStrip.StripRight || x < -60f) // -60 clears the widest baked glyph (~1.7 staff spaces)
                return;

            // Task 0 calibration: the SMuFL baseline sits at drawY + LineSpacing / 2, so the
            // glyph origin lands on the staff position when drawn at y - LineSpacing / 2.
            float y = glyph.YPx - _bravuraFont.LineSpacing / 2f;
            _bravuraFont.DrawString(
                spriteBatch,
                GlyphText(glyph.Glyph),
                new Vector2(x, y),
                _palette.TextPrimary,
                0f,
                Vector2.Zero,
                Vector2.One,
                SpriteEffects.None,
                NotationStrip.DepthGlyphs);
        }

        private string GlyphText(char glyph)
        {
            if (!_glyphStrings.TryGetValue(glyph, out var text))
            {
                text = glyph.ToString();
                _glyphStrings[glyph] = text;
            }
            return text;
        }

        private void DrawKeyColumnMask(SpriteBatch spriteBatch)
        {
            // Opaque mask in front of all moving ink; the caps and labels draw on top of it.
            DrawRect(
                spriteBatch,
                new Rectangle(
                    NotationStrip.StripLeft,
                    NotationStrip.StripTop,
                    NotationStrip.KeyColumnWidth,
                    NotationStrip.StripBottom - NotationStrip.StripTop),
                _palette.KeyColumn,
                NotationStrip.DepthKeyMask);
        }

        private void DrawFrame(SpriteBatch spriteBatch)
        {
            // Accent rules along the strip's top and bottom edges and the key-column
            // divider, each over a soft wider glow — the skin's neon panel edge.
            int width = NotationStrip.StripRight - NotationStrip.StripLeft;
            int glowPad = (NotationStrip.FrameGlowThickness - NotationStrip.FrameLineThickness) / 2;
            Color line = _palette.Accent;
            Color glow = _palette.Accent * NotationStrip.FrameGlowAlpha;
            for (int edge = 0; edge < 2; edge++)
            {
                int y = edge == 0 ? NotationStrip.StripTop : NotationStrip.StripBottom - NotationStrip.FrameLineThickness;
                DrawRect(spriteBatch, new Rectangle(NotationStrip.StripLeft, y - glowPad, width, NotationStrip.FrameGlowThickness), glow, NotationStrip.DepthFrame);
                DrawRect(spriteBatch, new Rectangle(NotationStrip.StripLeft, y, width, NotationStrip.FrameLineThickness), line, NotationStrip.DepthFrame);
            }

            int dividerX = NotationStrip.KeyColumnWidth - 1;
            int dividerHeight = NotationStrip.StripBottom - NotationStrip.StripTop;
            DrawRect(spriteBatch, new Rectangle(dividerX - glowPad, NotationStrip.StripTop, NotationStrip.FrameGlowThickness, dividerHeight), glow, NotationStrip.DepthFrame);
            DrawRect(spriteBatch, new Rectangle(dividerX, NotationStrip.StripTop, 1, dividerHeight), line * 0.6f, NotationStrip.DepthFrame);
        }

        private void DrawKeyCaps(SpriteBatch spriteBatch)
        {
            for (int i = 0; i < _keyColumnLabels.Count; i++)
            {
                var label = _keyColumnLabels[i];
                var cap = _keyCapRects[i];
                Color laneColor = LaneColor(label.Lane);

                DrawRect(spriteBatch, cap, laneColor * NotationStrip.KeyCapFillAlpha, NotationStrip.DepthKeyCap);
                DrawOutline(spriteBatch, cap, laneColor * NotationStrip.KeyCapBorderAlpha, NotationStrip.DepthKeyCapBorder);

                // Outer caps sit on staff-line rows; a faint leader carries the row across
                // the inner column (through the gap between its caps) to the staff edge.
                if (label.Outer)
                {
                    int leaderLeft = cap.Right + 2;
                    DrawRect(
                        spriteBatch,
                        new Rectangle(leaderLeft, (int)label.YPx, NotationStrip.KeyColumnWidth - 1 - leaderLeft, 1),
                        laneColor * NotationStrip.KeyCapBorderAlpha * 0.6f,
                        NotationStrip.DepthKeyCap);
                }
            }
        }

        private void DrawOutline(SpriteBatch spriteBatch, Rectangle r, Color color, float depth)
        {
            DrawRect(spriteBatch, new Rectangle(r.X, r.Y, r.Width, 1), color, depth);
            DrawRect(spriteBatch, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), color, depth);
            DrawRect(spriteBatch, new Rectangle(r.X, r.Y + 1, 1, r.Height - 2), color, depth);
            DrawRect(spriteBatch, new Rectangle(r.Right - 1, r.Y + 1, 1, r.Height - 2), color, depth);
        }

        private Color LaneColor(int lane) =>
            lane >= 0 && lane < LaneCount ? GetLaneColor(lane) : _palette.TextTertiary;

        private void DrawLabels(SpriteBatch spriteBatch)
        {
            var scale = new Vector2(NotationStrip.KeyCapLabelScale);
            if (_labelPositions == null)
            {
                _labelPositions = new Vector2[_keyColumnLabels.Count];
                for (int i = 0; i < _labelPositions.Length; i++)
                {
                    // Centered in the cap; LineSpacing centers the font's full cell.
                    var size = _labelFont.MeasureString(_keyColumnLabels[i].Text) * NotationStrip.KeyCapLabelScale;
                    var cap = _keyCapRects[i];
                    _labelPositions[i] = new Vector2(
                        MathF.Round(cap.X + (cap.Width - size.X) / 2f),
                        MathF.Round(_keyColumnLabels[i].YPx - _labelFont.LineSpacing * NotationStrip.KeyCapLabelScale / 2f));
                }
            }

            for (int i = 0; i < _keyColumnLabels.Count; i++)
            {
                _labelFont.DrawString(
                    spriteBatch,
                    _keyColumnLabels[i].Text,
                    _labelPositions[i],
                    _palette.TextPrimary,
                    0f,
                    Vector2.Zero,
                    scale,
                    SpriteEffects.None,
                    NotationStrip.DepthLabels);
            }
        }
    }
}
