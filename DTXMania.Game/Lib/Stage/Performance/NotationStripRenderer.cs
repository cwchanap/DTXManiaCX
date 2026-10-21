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
        public NotationStripRenderer(
            IResourceManager resourceManager,
            NotationChart chart,
            IReadOnlyList<MeasureLine> measureLines)
        {
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
            _chart = chart ?? throw new ArgumentNullException(nameof(chart));
            _measureLines = measureLines ?? throw new ArgumentNullException(nameof(measureLines));

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
                NotationStrip.BackgroundColor,
                NotationStrip.DepthBackground);
        }

        private void DrawStaffLines(SpriteBatch spriteBatch)
        {
            for (int y = NotationStrip.StaffTopY; y <= NotationStrip.StaffBottomY; y += NotationStrip.DiatonicStep)
            {
                DrawRect(
                    spriteBatch,
                    new Rectangle(
                        NotationStrip.StripLeft,
                        y,
                        NotationStrip.StripRight - NotationStrip.StripLeft,
                        (int)NotationStrip.StaffLineThickness),
                    NotationStrip.StaffColor,
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
            foreach (var anchor in NotationStripLayout.GetVisibleBarlineAnchors(_measureLines, leftMs, rightMs))
            {
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
                    NotationStrip.BarlineColor,
                    NotationStrip.DepthStaffAndBarlines);
            }
        }

        private void DrawHitMarker(SpriteBatch spriteBatch)
        {
            DrawRect(
                spriteBatch,
                new Rectangle(
                    NotationStrip.HitMarkerX,
                    NotationStrip.StripTop,
                    NotationStrip.HitMarkerWidth,
                    NotationStrip.StripBottom - NotationStrip.StripTop),
                NotationStrip.HitMarkerColor,
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
                var engraving = NotationStripLayout.EngraveMeasure(_chart.Measures[i]);

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
            DrawRect(spriteBatch, destination, NotationStrip.NotationColor, NotationStrip.DepthBeamsAndStems);
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
                glyph.Glyph.ToString(),
                new Vector2(x, y),
                NotationStrip.NotationColor,
                0f,
                Vector2.Zero,
                Vector2.One,
                SpriteEffects.None,
                NotationStrip.DepthGlyphs);
        }

        private void DrawKeyColumnMask(SpriteBatch spriteBatch)
        {
            // Opaque mask in front of all moving ink; the labels draw on top of it.
            DrawRect(
                spriteBatch,
                new Rectangle(
                    NotationStrip.StripLeft,
                    NotationStrip.StripTop,
                    NotationStrip.KeyColumnWidth,
                    NotationStrip.StripBottom - NotationStrip.StripTop),
                NotationStrip.BackgroundColor,
                NotationStrip.DepthKeyMask);
        }

        private void DrawLabels(SpriteBatch spriteBatch)
        {
            foreach (var label in NotationStripLayout.GetKeyColumnLabels())
            {
                float textWidth = _labelFont.MeasureString(label.Text).X * LabelFontScale;
                var position = new Vector2(
                    NotationStrip.LabelRightX - textWidth,
                    label.YPx - _labelFont.LineSpacing * LabelFontScale / 2f);
                _labelFont.DrawString(
                    spriteBatch,
                    label.Text,
                    position,
                    NotationStrip.LabelColor,
                    0f,
                    Vector2.Zero,
                    new Vector2(LabelFontScale),
                    SpriteEffects.None,
                    NotationStrip.DepthLabels);
            }
        }
    }
}
