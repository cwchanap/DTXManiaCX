#nullable enable

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.Utilities;
using TabHud = DTXMania.Game.Lib.UI.Layout.PerformanceUILayout.TabHud;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Per-frame Tab HUD values snapshotted by PerformanceStage from the existing
    /// gameplay owners (HPA-509). <see cref="TabHudDisplay"/> renders the snapshot
    /// read-only; it never recalculates score, combo, life, progress or the song clock.
    /// </summary>
    public readonly struct TabHudFrameState
    {
        public int Score { get; init; }
        public int Combo { get; init; }
        public float Life { get; init; }
        public float Progress { get; init; }
        public double ElapsedMs { get; init; }
        public double TotalMs { get; init; }
        public double Achievement { get; init; }
        public int PerfectCount { get; init; }
        public int GreatCount { get; init; }
        public int GoodCount { get; init; }
        public int PoorCount { get; init; }
        public int MissCount { get; init; }
        public int MaxCombo { get; init; }
    }

    /// <summary>
    /// Compact Tab-mode HUD renderer (HPA-509): title/artist + score header, combo
    /// above the strip hit marker, horizontal LIFE, progress with elapsed/total and
    /// a bottom stats row. Presentation only — every value arrives via
    /// <see cref="TabHudFrameState"/>; the level-weighted game skill reuses the shared
    /// <see cref="SongScore.CalculateGameSkill"/> formula on the same chart values
    /// SkillPanelDisplay draws. Managed fonts are used for both score and combo
    /// (the Lanes sprite score is hard-positioned and the combo sprite digits exceed
    /// the Tab hit-feedback height). Fonts and the white-pixel primitive are built
    /// once and released on dispose; on acquisition failure the HUD draw is skipped
    /// (log only — no second/minimal HUD mode).
    /// </summary>
    public class TabHudDisplay : IDisposable
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly SongChart? _chart;
        private readonly string _title;
        private readonly string _artist;
        private ManagedFont? _titleFont;
        private ManagedFont? _scoreFont;
        private ManagedFont? _comboFont;
        private ManagedFont? _tileFont;
        private Texture2D? _whiteTexture;
        private bool _disposed;

        public TabHudDisplay(GraphicsDevice graphicsDevice, SongChart? chart, string? title, string? artist)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _chart = chart;
            _title = title ?? string.Empty;
            _artist = artist ?? string.Empty;

            try
            {
                _titleFont = ManagedFont.CreateFont(_graphicsDevice, "NotoSerifJP", PerformanceUILayout.TabHud.TitleFontSizePt, FontStyle.Bold);
                _scoreFont = ManagedFont.CreateFont(_graphicsDevice, "NotoSerifJP", PerformanceUILayout.TabHud.ScoreFontSizePt, FontStyle.Bold);
                _comboFont = ManagedFont.CreateFont(_graphicsDevice, "NotoSerifJP", PerformanceUILayout.TabHud.ComboFontSizePt, FontStyle.Bold);
                _tileFont = ManagedFont.CreateFont(_graphicsDevice, "NotoSerifJP", PerformanceUILayout.TabHud.TileFontSizePt, FontStyle.Bold);
                _whiteTexture = new Texture2D(_graphicsDevice, 1, 1);
                _whiteTexture.SetData(new[] { Color.White });
            }
            catch (Exception ex)
            {
                // Log and skip the HUD draw — no fallback/minimal Tab HUD mode.
                Debug.WriteLine($"TabHudDisplay: failed to acquire fonts/primitive: {ex.Message}");
                Dispose(true);
            }
        }

        /// <summary>
        /// Draw the whole Tab HUD from the given snapshot. Skipped entirely when
        /// resources failed to acquire.
        /// </summary>
        [ExcludeFromCodeCoverage]
        public void Draw(SpriteBatch spriteBatch, in TabHudFrameState state)
        {
            if (_disposed || spriteBatch == null || _whiteTexture == null ||
                _titleFont == null || _scoreFont == null || _comboFont == null || _tileFont == null)
                return;

            DrawHeader(spriteBatch, state.Score);
            DrawCombo(spriteBatch, state.Combo);
            DrawLife(spriteBatch, state.Life);
            DrawProgress(spriteBatch, state.Progress, state.ElapsedMs, state.TotalMs);
            DrawStatsRow(spriteBatch, state);
        }

        [ExcludeFromCodeCoverage]
        private void DrawHeader(SpriteBatch spriteBatch, int score)
        {
            // Score right-aligned inside the header.
            string scoreText = score.ToString(CultureInfo.InvariantCulture);
            Vector2 scoreSize = _scoreFont!.MeasureString(scoreText);
            DrawShadowed(spriteBatch, _scoreFont, scoreText,
                new Vector2(TabHud.HeaderRight - scoreSize.X, TabHud.HeaderTop), TabHud.PrimaryTextColor);

            // Title + artist on the left, ellipsized to the space left of the score.
            float textMaxWidth = TabHud.HeaderRight - scoreSize.X - TabHud.HeaderLeft - 24f;
            string title = TextHelper.TruncateToWidth(_title, textMaxWidth, _titleFont!);
            DrawShadowed(spriteBatch, _titleFont, title, new Vector2(TabHud.HeaderLeft, TabHud.HeaderTop), TabHud.PrimaryTextColor);
            string artist = TextHelper.TruncateToWidth(_artist, textMaxWidth, _tileFont!);
            DrawShadowed(
                spriteBatch,
                _tileFont,
                artist,
                new Vector2(TabHud.HeaderLeft, TabHud.HeaderTop + _titleFont.LineSpacing + 4),
                TabHud.SecondaryTextColor);
        }

        [ExcludeFromCodeCoverage]
        private void DrawCombo(SpriteBatch spriteBatch, int combo)
        {
            if (combo <= 0)
                return;

            string text = combo.ToString(CultureInfo.InvariantCulture);
            Vector2 size = _comboFont!.MeasureString(text);
            Vector2 position = new(
                TabHud.HitFeedbackCenterX - size.X / 2f,
                TabHud.HitFeedbackTop);
            DrawShadowed(spriteBatch, _comboFont, text, position, TabHud.ComboColor);
        }

        [ExcludeFromCodeCoverage]
        private void DrawLife(SpriteBatch spriteBatch, float life)
        {
            // LIFE label in the left key column, gauge from the key column edge to
            // the right playfield edge.
            DrawShadowed(
                spriteBatch,
                _tileFont!,
                "LIFE",
                new Vector2(16, TabHud.LifeTop + 2),
                TabHud.SecondaryTextColor);

            var backRect = new Rectangle(
                TabHud.LifeLeft,
                TabHud.LifeTop,
                TabHud.LifeRight - TabHud.LifeLeft,
                TabHud.LifeBottom - TabHud.LifeTop);
            FillRectangle(spriteBatch, backRect, TabHud.BarBackColor);

            float clamped = Clamp01(life);
            if (clamped > 0f)
            {
                FillRectangle(
                    spriteBatch,
                    new Rectangle(backRect.X, backRect.Y, (int)(backRect.Width * clamped), backRect.Height),
                    TabHud.LifeFillColor);
            }
        }

        [ExcludeFromCodeCoverage]
        private void DrawProgress(SpriteBatch spriteBatch, float progress, double elapsedMs, double totalMs)
        {
            // Elapsed/total reusing the Song Select duration formatter (m:ss).
            string timeText =
                SongStatusPanel.FormatDuration(Math.Max(0, elapsedMs / 1000.0)) +
                " / " +
                SongStatusPanel.FormatDuration(Math.Max(0, totalMs / 1000.0));
            Vector2 timeSize = _tileFont!.MeasureString(timeText);
            Vector2 timePosition = new(TabHud.ProgressLeft, TabHud.ProgressTop + 2);
            DrawShadowed(spriteBatch, _tileFont, timeText, timePosition, TabHud.SecondaryTextColor);

            // Thin horizontal fill to the right of the elapsed/total text.
            int barX = (int)(TabHud.ProgressLeft + timeSize.X + 12);
            var backRect = new Rectangle(
                barX,
                TabHud.ProgressTop + 8,
                Math.Max(0, TabHud.ProgressRight - barX),
                TabHud.ProgressBottom - TabHud.ProgressTop - 16);
            FillRectangle(spriteBatch, backRect, TabHud.BarBackColor);

            float clamped = Clamp01(progress);
            if (clamped > 0f && backRect.Width > 0)
            {
                FillRectangle(
                    spriteBatch,
                    new Rectangle(backRect.X, backRect.Y, (int)(backRect.Width * clamped), backRect.Height),
                    TabHud.AccentColor);
            }
        }

        [ExcludeFromCodeCoverage]
        private void DrawStatsRow(SpriteBatch spriteBatch, in TabHudFrameState state)
        {
            // Level-weighted game skill: same shared formula and same chart values
            // SkillPanelDisplay.Draw uses — no new skill formula here.
            double gameSkill = SongScore.CalculateGameSkill(
                state.Achievement,
                _chart?.DrumLevel ?? 0,
                _chart?.DrumLevelDec ?? 0);

            // Task 1 renders eight tiles; the Fast/Slow tile arrives with Task 2
            // (its counters do not exist yet). Poor is presented as OK.
            (string Label, string Value)[] tiles =
            {
                ("PERFECT", SkillPanelDisplay.FormatJudgementCount(state.PerfectCount)),
                ("GREAT", SkillPanelDisplay.FormatJudgementCount(state.GreatCount)),
                ("GOOD", SkillPanelDisplay.FormatJudgementCount(state.GoodCount)),
                ("OK", SkillPanelDisplay.FormatJudgementCount(state.PoorCount)),
                ("MISS", SkillPanelDisplay.FormatJudgementCount(state.MissCount)),
                ("MAX COMBO", state.MaxCombo.ToString(CultureInfo.InvariantCulture)),
                ("ACHIEVEMENT", SkillPanelDisplay.FormatSkillText(state.Achievement).Trim() + "%"),
                ("SKILL", SkillPanelDisplay.FormatSkillText(gameSkill).Trim()),
            };

            float tileWidth = (TabHud.StatsRight - TabHud.StatsLeft) / (float)tiles.Length;
            float labelY = TabHud.StatsTop;
            float valueY = TabHud.StatsTop + _tileFont!.LineSpacing + 6;
            for (int i = 0; i < tiles.Length; i++)
            {
                float x = TabHud.StatsLeft + i * tileWidth;
                DrawShadowed(spriteBatch, _tileFont!, tiles[i].Label, new Vector2(x, labelY), TabHud.SecondaryTextColor);
                DrawShadowed(spriteBatch, _titleFont!, tiles[i].Value, new Vector2(x, valueY), TabHud.PrimaryTextColor);
            }
        }

        /// <summary>
        /// Draw Tab HUD text with the shared shadow convention. The Tab HUD sits
        /// directly over the BGA (unlike the Lanes HUD over the lane panel), so
        /// unshadowed text goes illegible on bright videos.
        /// </summary>
        [ExcludeFromCodeCoverage]
        private static void DrawShadowed(
            SpriteBatch spriteBatch,
            ManagedFont font,
            string text,
            Vector2 position,
            Color color)
        {
            font.DrawStringWithShadow(
                spriteBatch,
                text,
                position,
                color,
                PerformanceUILayout.Visual.StandardShadowColor,
                PerformanceUILayout.Visual.StandardShadowOffset);
        }

        [ExcludeFromCodeCoverage]
        private void FillRectangle(SpriteBatch spriteBatch, Rectangle rect, Color color)
        {
            spriteBatch.Draw(_whiteTexture!, rect, color);
        }

        private static float Clamp01(float value)
        {
            // The stage already clamps progress; guard anyway so a NaN (e.g. a
            // zero-duration chart) can never become a garbage fill width.
            if (!float.IsFinite(value))
                return 0f;
            return Math.Clamp(value, 0f, 1f);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _titleFont?.Dispose();
                _titleFont = null;
                _scoreFont?.Dispose();
                _scoreFont = null;
                _comboFont?.Dispose();
                _comboFont = null;
                _tileFont?.Dispose();
                _tileFont = null;
                _whiteTexture?.Dispose();
                _whiteTexture = null;
            }

            _disposed = true;
        }
    }
}
