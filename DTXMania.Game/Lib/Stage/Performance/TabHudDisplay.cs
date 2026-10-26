#nullable enable

using System;
using System.Collections.Generic;
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
        public int FastCount { get; init; }
        public int SlowCount { get; init; }

        /// <summary>
        /// Run-gate snapshot: the frozen ShowCombo=false must hide the Tab combo
        /// exactly like the Lanes combo draw (HPA-509 final review).
        /// </summary>
        public bool HideCombo { get; init; }
    }

    /// <summary>
    /// Compact Tab-mode HUD renderer (HPA-509): title/artist + score header, combo
    /// above the strip hit marker, newest-wins judgement popup + per-hit fast/slow
    /// line, horizontal LIFE, progress with elapsed/total and a bottom stats row.
    /// Presentation only — every value arrives via <see cref="TabHudFrameState"/>;
    /// the level-weighted game skill reuses the shared
    /// <see cref="SongScore.CalculateGameSkill"/> formula on the same chart values
    /// SkillPanelDisplay draws. Managed fonts are used for both score and combo
    /// (the Lanes sprite score is hard-positioned and the combo sprite digits exceed
    /// the Tab hit-feedback height). Fonts are built once and released on dispose;
    /// the white-pixel primitive is the stage's shared fallback texture, borrowed
    /// and never disposed here. When acquisition fails <see cref="IsReady"/> stays
    /// false and the stage rolls the strip back to a consistent Lanes fallback.
    /// </summary>
    public class TabHudDisplay : IDisposable
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly SongChart? _chart;
        private readonly string _title;
        private readonly string _artist;
        private readonly Texture2D? _whiteTexture;
        private ManagedFont? _titleFont;
        private ManagedFont? _scoreFont;
        private ManagedFont? _tileFont;
        private bool _resourcesReady;
        private bool _disposed;

        // Newest-wins judgement popup slot (HPA-509): each OnJudgementMade replaces
        // the previous popup — no queue/stack — reusing JudgementTextPopup's own
        // fade/rise/expiry. The fixed fast/slow line rides above it.
        private JudgementTextPopup? _popup;
        private Color _popupColor;
        private string? _popupTimingText;
        private bool _popupTimingIsSlow;

        // Per-lane key flash slots (HPA-509): one timer+color per lane, overwritten by
        // manual input / judged hits (gold) / Miss (red) — never a queue. Label y per
        // lane comes from the strip label anchors so flashes sit behind their label.
        private readonly float[] _flashTimers = new float[PerformanceUILayout.LaneCount];
        private readonly Color[] _flashColors = new Color[PerformanceUILayout.LaneCount];
        private readonly float[] _flashYsByLane = new float[PerformanceUILayout.LaneCount];

        // Per-frame string caches (HPA-509 review): rebuilt only when the underlying
        // value changes — the same convention ScoreDisplay and ComboDisplay use.
        private int _lastScore = -1;
        private string _cachedScoreText = string.Empty;
        private float _cachedScoreWidth;
        private float _lastHeaderTextMaxWidth = -1f;
        private string _cachedTitle = string.Empty;
        private string _cachedArtist = string.Empty;
        private int _lastCombo = -1;
        private string _cachedComboText = string.Empty;
        private Vector2 _cachedComboSize;
        private int _lastElapsedSec = -1;
        private int _lastTotalSec = -1;
        private string _cachedTimeText = string.Empty;
        private float _cachedTimeWidth;
        private string[]? _cachedStatLabels;
        private (int, int, int, int, int, int, int, int, double, double)? _lastStatsKey;
        private readonly string[] _cachedStatValues = new string[9];
        private string? _cachedTimingText;
        private Vector2 _cachedTimingSize;

        public TabHudDisplay(GraphicsDevice graphicsDevice, SongChart? chart, string? title, string? artist,
            Texture2D? whiteTexture = null)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _chart = chart;
            _title = title ?? string.Empty;
            _artist = artist ?? string.Empty;
            _whiteTexture = whiteTexture;

            // Lane→label-y anchors for the flash slots; all ten primary labels map
            // one-to-one onto lanes 0-9 (verified by test), so a plain array suffices.
            foreach (var anchor in NotationStripLayout.GetKeyColumnLabels())
            {
                if (anchor.Lane >= 0 && anchor.Lane < _flashYsByLane.Length)
                    _flashYsByLane[anchor.Lane] = anchor.YPx;
            }

            // Font acquisition needs a live GraphicsDevice, so it can never run
            // under headless coverage — excluded like the Draw paths.
            AcquireDrawResources();
        }

        /// <summary>
        /// True once every draw resource (fonts + the shared white pixel) is held
        /// and the display has not been disposed. The stage keys its single
        /// Tab-HUD/Lanes decision on this flag — a HUD that failed to load falls
        /// back to the normal Lanes display (HPA-509 review).
        /// </summary>
        public bool IsReady => _resourcesReady && !_disposed;

        /// <summary>
        /// Test seam: deterministic tests mark a GPU-less display ready so the
        /// stage's Tab routing paths can run without a real GraphicsDevice. The
        /// ctor's failed acquisition leaves the display disposed, so the seam
        /// restores both sides of the readiness state.
        /// </summary>
        internal void MarkResourcesReadyForTesting()
        {
            _resourcesReady = true;
            _disposed = false;
        }

        [ExcludeFromCodeCoverage]
        private void AcquireDrawResources()
        {
            try
            {
                _titleFont = ManagedFont.CreateFont(_graphicsDevice, "NotoSerifJP", PerformanceUILayout.TabHud.TitleFontSizePt, FontStyle.Bold);
                _scoreFont = ManagedFont.CreateFont(_graphicsDevice, "NotoSerifJP", PerformanceUILayout.TabHud.ScoreFontSizePt, FontStyle.Bold);
                _tileFont = ManagedFont.CreateFont(_graphicsDevice, "NotoSerifJP", PerformanceUILayout.TabHud.TileFontSizePt, FontStyle.Bold);
                _resourcesReady = _whiteTexture != null
                    && _titleFont != null && _scoreFont != null && _tileFont != null;
            }
            catch (Exception ex)
            {
                // Log and let the stage fall back to the Lanes HUD (IsReady stays
                // false). Console.WriteLine (not Debug.WriteLine) keeps the failure
                // diagnosable in Release builds, matching LogPerformanceError.
                Console.WriteLine($"[PerformanceError] TabHudDisplay: failed to acquire fonts: {ex.Message}");
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
                _titleFont == null || _scoreFont == null || _tileFont == null)
                return;

            DrawHeader(spriteBatch, state.Score);
            DrawCombo(spriteBatch, state);
            DrawTransientFeedback(spriteBatch);
            DrawLife(spriteBatch, state.Life);
            DrawProgress(spriteBatch, state.Progress, state.ElapsedMs, state.TotalMs);
            DrawStatsRow(spriteBatch, state);
        }

        /// <summary>
        /// Advance the popup slot and key flash decay. Called from the stage's update
        /// path next to the other presentation components.
        /// </summary>
        public void Update(double deltaTime)
        {
            if (_popup != null && !_popup.Update(deltaTime))
            {
                _popup = null;
                _popupTimingText = null;
            }

            float decay = (float)deltaTime * PerformanceUILayout.Animation.FlashDecayRate;
            for (int lane = 0; lane < _flashTimers.Length; lane++)
            {
                if (_flashTimers[lane] > 0f)
                    _flashTimers[lane] = MathF.Max(0f, _flashTimers[lane] - decay);
            }
        }

        /// <summary>
        /// Replace the judgement popup slot (newest wins) and optionally show the
        /// per-hit fast/slow line, gated by the stage's existing timing-feedback
        /// predicate. Word/color come from JudgementTextPopupManager's shared tables;
        /// fast/slow classification uses the shared rounded ProjectDelta.
        /// </summary>
        public void NotifyJudgement(string word, Color color, bool showTiming, double deltaMs)
        {
            // No _disposed guard: this is pure presentation state (no GPU resources)
            // and the draw paths separately skip when fonts failed to acquire.
            // Half-width centering needs a font; without one (acquisition failed) the
            // draw is skipped anyway, so fall back to the raw center x.
            float halfWidth = _titleFont != null ? _titleFont.MeasureString(word).X / 2f : 0f;
            _popup = new JudgementTextPopup(
                word,
                new Vector2(TabHud.HitFeedbackCenterX - halfWidth, TabHud.PopupWordTop),
                TabHud.PopupRiseDistance);
            _popupColor = color;

            if (showTiming)
            {
                var projection = PerformanceUILayout.HitTimingFeedback.ProjectDelta(deltaMs);
                _popupTimingText = projection.Text;
                _popupTimingIsSlow = projection.IsSlow;
            }
            else
            {
                _popupTimingText = null;
            }
        }

        /// <summary>
        /// Refresh (overwrite) a lane's key flash slot — manual input, judged hit or
        /// Miss. No queue: the newest event for a lane wins.
        /// </summary>
        public void TriggerKeyFlash(int lane, Color color)
        {
            if (lane < 0 || lane >= _flashTimers.Length)
                return;

            _flashTimers[lane] = 1f;
            _flashColors[lane] = color;
        }

        /// <summary>
        /// Draw the per-lane key flash bars in the BASE BackToFront pass, immediately
        /// after the notation strip: depth 0.35 sits between the key mask (0.40) and
        /// the labels (0.30) so each flash renders behind its label. The cached white
        /// pixel keeps the label legible (semi-transparent fill, no texture swap).
        /// </summary>
        [ExcludeFromCodeCoverage]
        public void DrawKeyFlashes(SpriteBatch spriteBatch)
        {
            if (_disposed || spriteBatch == null || _whiteTexture == null)
                return;

            for (int lane = 0; lane < _flashTimers.Length; lane++)
            {
                float timer = _flashTimers[lane];
                if (timer <= 0f)
                    continue;

                var rect = new Rectangle(
                    TabHud.KeyFlashLeft,
                    (int)(_flashYsByLane[lane] - TabHud.KeyFlashHeight / 2f),
                    PerformanceUILayout.NotationStrip.LabelRightX - TabHud.KeyFlashLeft,
                    TabHud.KeyFlashHeight);
                spriteBatch.Draw(
                    _whiteTexture,
                    rect,
                    null,
                    _flashColors[lane] * (TabHud.KeyFlashAlpha * timer),
                    0f,
                    Vector2.Zero,
                    SpriteEffects.None,
                    TabHud.DepthKeyFlash);
            }
        }

        /// <summary>
        /// Draw the newest-wins judgement word (fading/rising via the reused popup)
        /// and the fixed per-hit fast/slow line above it.
        /// </summary>
        [ExcludeFromCodeCoverage]
        private void DrawTransientFeedback(SpriteBatch spriteBatch)
        {
            if (_popup == null || !_popup.IsActive || _titleFont == null || _tileFont == null)
                return;

            var position = _popup.CurrentPosition;
            DrawShadowed(spriteBatch, _titleFont, _popup.Text, position, _popupColor * _popup.Alpha);

            if (_popupTimingText != null)
            {
                if (_popupTimingText != _cachedTimingText)
                {
                    _cachedTimingSize = _tileFont.MeasureString(_popupTimingText);
                    _cachedTimingText = _popupTimingText;
                }
                var timingColor = _popupTimingIsSlow ? TabHud.TimingSlowColor : TabHud.TimingFastColor;
                // The line rides a fixed gap above the word's current position so
                // the popup's shortened Tab rise can never sweep through it.
                float timingTop = position.Y - _cachedTimingSize.Y - TabHud.PopupTimingGap;
                DrawShadowed(
                    spriteBatch,
                    _tileFont,
                    _popupTimingText,
                    new Vector2(TabHud.HitFeedbackCenterX - _cachedTimingSize.X / 2f, timingTop),
                    timingColor * _popup.Alpha);
            }
        }

        [ExcludeFromCodeCoverage]
        private void DrawHeader(SpriteBatch spriteBatch, int score)
        {
            // Score right-aligned inside the header.
            if (score != _lastScore)
            {
                _cachedScoreText = score.ToString(CultureInfo.InvariantCulture);
                _cachedScoreWidth = _scoreFont!.MeasureString(_cachedScoreText).X;
                _lastScore = score;
            }
            DrawShadowed(spriteBatch, _scoreFont!, _cachedScoreText,
                new Vector2(TabHud.HeaderRight - _cachedScoreWidth, TabHud.HeaderTop), TabHud.PrimaryTextColor);

            // Title + artist on the left, ellipsized to the space left of the score.
            float textMaxWidth = TabHud.HeaderRight - _cachedScoreWidth - TabHud.HeaderLeft - TabHud.HeaderTextGap;
            if (textMaxWidth != _lastHeaderTextMaxWidth)
            {
                _cachedTitle = TextHelper.TruncateToWidth(_title, textMaxWidth, _titleFont!);
                _cachedArtist = TextHelper.TruncateToWidth(_artist, textMaxWidth, _tileFont!);
                _lastHeaderTextMaxWidth = textMaxWidth;
            }
            DrawShadowed(spriteBatch, _titleFont!, _cachedTitle, new Vector2(TabHud.HeaderLeft, TabHud.HeaderTop), TabHud.PrimaryTextColor);
            DrawShadowed(
                spriteBatch,
                _tileFont!,
                _cachedArtist,
                new Vector2(TabHud.HeaderLeft, TabHud.HeaderTop + _titleFont!.LineSpacing + TabHud.HeaderArtistGap),
                TabHud.SecondaryTextColor);
        }

        [ExcludeFromCodeCoverage]
        private void DrawCombo(SpriteBatch spriteBatch, in TabHudFrameState state)
        {
            // Same gate as the Lanes combo draw: ShowCombo=false hides it entirely.
            if (state.HideCombo || state.Combo <= 0)
                return;

            if (state.Combo != _lastCombo)
            {
                _cachedComboText = state.Combo.ToString(CultureInfo.InvariantCulture);
                _cachedComboSize = _scoreFont!.MeasureString(_cachedComboText);
                _lastCombo = state.Combo;
            }
            Vector2 position = new(
                TabHud.HitFeedbackCenterX - _cachedComboSize.X / 2f,
                TabHud.HitFeedbackTop);
            DrawShadowed(spriteBatch, _scoreFont!, _cachedComboText, position, TabHud.ComboColor);
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
                new Vector2(TabHud.LifeLabelLeft, TabHud.LifeTop + TabHud.LifeLabelInsetY),
                TabHud.SecondaryTextColor);

            var backRect = new Rectangle(
                TabHud.LifeLeft,
                TabHud.LifeTop,
                TabHud.LifeRight - TabHud.LifeLeft,
                TabHud.LifeBottom - TabHud.LifeTop);
            FillRectangle(spriteBatch, backRect, TabHud.BarBackColor, TabHud.DepthBarBack);

            float clamped = Clamp01(life);
            if (clamped > 0f)
            {
                FillRectangle(
                    spriteBatch,
                    new Rectangle(backRect.X, backRect.Y, (int)(backRect.Width * clamped), backRect.Height),
                    TabHud.LifeFillColor,
                    TabHud.DepthBarFill);
            }
        }

        [ExcludeFromCodeCoverage]
        private void DrawProgress(SpriteBatch spriteBatch, float progress, double elapsedMs, double totalMs)
        {
            // FormatDuration throws on NaN/overflow, so non-finite or absurd
            // millisecond inputs (e.g. a chart with a degenerate BPM) collapse to
            // zero first; elapsed is then clamped to the chart total so the header
            // can never read past the song's end (HPA-509 review).
            double totalSec = ToSafeSeconds(totalMs);
            int eSec = (int)Math.Min(ToSafeSeconds(elapsedMs), totalSec);
            int tSec = (int)totalSec;
            if (eSec != _lastElapsedSec || tSec != _lastTotalSec)
            {
                _cachedTimeText =
                    SongStatusPanel.FormatDuration(eSec) +
                    " / " +
                    SongStatusPanel.FormatDuration(tSec);
                _cachedTimeWidth = _tileFont!.MeasureString(_cachedTimeText).X;
                _lastElapsedSec = eSec;
                _lastTotalSec = tSec;
            }
            // Right-aligned so the fill's fixed left edge can never shift with the
            // proportional-width text.
            Vector2 timePosition = new(
                TabHud.ProgressBarLeft - TabHud.ProgressBarGap - _cachedTimeWidth,
                TabHud.ProgressTop + TabHud.ProgressTextInsetY);
            DrawShadowed(spriteBatch, _tileFont!, _cachedTimeText, timePosition, TabHud.SecondaryTextColor);

            // Thin horizontal fill, fixed at ProgressBarLeft.
            var backRect = new Rectangle(
                TabHud.ProgressBarLeft,
                TabHud.ProgressTop + TabHud.ProgressBarInsetY,
                Math.Max(0, TabHud.ProgressRight - TabHud.ProgressBarLeft),
                TabHud.ProgressBottom - TabHud.ProgressTop - 2 * TabHud.ProgressBarInsetY);
            FillRectangle(spriteBatch, backRect, TabHud.BarBackColor, TabHud.DepthBarBack);

            float clamped = Clamp01(progress);
            if (clamped > 0f && backRect.Width > 0)
            {
                FillRectangle(
                    spriteBatch,
                    new Rectangle(backRect.X, backRect.Y, (int)(backRect.Width * clamped), backRect.Height),
                    TabHud.AccentColor,
                    TabHud.DepthBarFill);
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

            // Task 2 adds the Fast/Slow tile — totals are always visible, independent
            // of the per-hit timing preference. Poor is presented as OK.
            // Trim the legacy 4-wide judgement padding so every value's left edge
            // aligns with its label like the other tiles (HPA-509 polish).
            float tileWidth = (TabHud.StatsRight - TabHud.StatsLeft) / (float)_cachedStatValues.Length;
            float valueMaxWidth = tileWidth - TabHud.StatsTileGap;
            var statsKey = (state.PerfectCount, state.GreatCount, state.GoodCount, state.PoorCount,
                state.MissCount, state.MaxCombo, state.FastCount, state.SlowCount,
                state.Achievement, gameSkill);
            if (statsKey != _lastStatsKey)
            {
                // Values get the same tile-width bound as labels — a four-digit
                // FAST/SLOW pair at 24pt would otherwise bleed into ACHIEVE
                // (PR #203 review).
                _cachedStatValues[0] = TextHelper.TruncateToWidth(SkillPanelDisplay.FormatJudgementCount(state.PerfectCount).Trim(), valueMaxWidth, _titleFont!);
                _cachedStatValues[1] = TextHelper.TruncateToWidth(SkillPanelDisplay.FormatJudgementCount(state.GreatCount).Trim(), valueMaxWidth, _titleFont!);
                _cachedStatValues[2] = TextHelper.TruncateToWidth(SkillPanelDisplay.FormatJudgementCount(state.GoodCount).Trim(), valueMaxWidth, _titleFont!);
                _cachedStatValues[3] = TextHelper.TruncateToWidth(SkillPanelDisplay.FormatJudgementCount(state.PoorCount).Trim(), valueMaxWidth, _titleFont!);
                _cachedStatValues[4] = TextHelper.TruncateToWidth(SkillPanelDisplay.FormatJudgementCount(state.MissCount).Trim(), valueMaxWidth, _titleFont!);
                _cachedStatValues[5] = TextHelper.TruncateToWidth(state.MaxCombo.ToString(CultureInfo.InvariantCulture), valueMaxWidth, _titleFont!);
                _cachedStatValues[6] = TextHelper.TruncateToWidth($"{state.FastCount.ToString(CultureInfo.InvariantCulture)}/{state.SlowCount.ToString(CultureInfo.InvariantCulture)}", valueMaxWidth, _titleFont!);
                _cachedStatValues[7] = TextHelper.TruncateToWidth(SkillPanelDisplay.FormatSkillText(state.Achievement).Trim() + "%", valueMaxWidth, _titleFont!);
                _cachedStatValues[8] = TextHelper.TruncateToWidth(SkillPanelDisplay.FormatSkillText(gameSkill).Trim(), valueMaxWidth, _titleFont!);
                _lastStatsKey = statsKey;
            }

            // The label strings and tile width are both fixed, so the truncation is
            // computed once and reused every frame.
            _cachedStatLabels ??= BuildStatLabels(tileWidth - TabHud.StatsTileGap);
            float labelY = TabHud.StatsTop;
            float valueY = TabHud.StatsTop + _tileFont!.LineSpacing + TabHud.StatsValueGap;
            for (int i = 0; i < _cachedStatValues.Length; i++)
            {
                float x = TabHud.StatsLeft + i * tileWidth;
                DrawShadowed(spriteBatch, _tileFont!, _cachedStatLabels[i], new Vector2(x, labelY), TabHud.SecondaryTextColor);
                DrawShadowed(spriteBatch, _titleFont!, _cachedStatValues[i], new Vector2(x, valueY), TabHud.PrimaryTextColor);
            }
        }

        [ExcludeFromCodeCoverage]
        private string[] BuildStatLabels(float maxWidth)
        {
            // Constrain every label inside its own tile: an unconstrained long
            // label bleeds into the next tile (ACHIEVEMENT previously merged
            // with SKILL at ~139px/tile).
            return new[]
            {
                TextHelper.TruncateToWidth("PERFECT", maxWidth, _tileFont!),
                TextHelper.TruncateToWidth("GREAT", maxWidth, _tileFont!),
                TextHelper.TruncateToWidth("GOOD", maxWidth, _tileFont!),
                TextHelper.TruncateToWidth("OK", maxWidth, _tileFont!),
                TextHelper.TruncateToWidth("MISS", maxWidth, _tileFont!),
                TextHelper.TruncateToWidth("MAX COMBO", maxWidth, _tileFont!),
                TextHelper.TruncateToWidth("FAST/SLOW", maxWidth, _tileFont!),
                TextHelper.TruncateToWidth("ACHIEVE", maxWidth, _tileFont!),
                TextHelper.TruncateToWidth("SKILL", maxWidth, _tileFont!),
            };
        }

        /// <summary>
        /// Draw Tab HUD text with the shared shadow convention. The Tab HUD sits
        /// directly over the BGA (unlike the Lanes HUD over the lane panel), so
        /// unshadowed text goes illegible on bright videos. Two explicit draw calls
        /// with their own planes replace DrawStringWithShadow — passing a depth
        /// through the shared helper shifted every legacy caller's shadow (HPA-509
        /// review). The shadow color tracks the text's alpha so fading popups do
        /// not leave a fixed-opacity ghost.
        /// </summary>
        [ExcludeFromCodeCoverage]
        private static void DrawShadowed(
            SpriteBatch spriteBatch,
            ManagedFont font,
            string text,
            Vector2 position,
            Color color)
        {
            Color shadow = PerformanceUILayout.Visual.StandardShadowColor * (color.A / 255f);
            font.DrawString(
                spriteBatch,
                text,
                position + PerformanceUILayout.Visual.StandardShadowOffset,
                shadow,
                0f,
                Vector2.Zero,
                Vector2.One,
                SpriteEffects.None,
                TabHud.DepthTextShadow);
            font.DrawString(
                spriteBatch,
                text,
                position,
                color,
                0f,
                Vector2.Zero,
                Vector2.One,
                SpriteEffects.None,
                TabHud.DepthText);
        }

        [ExcludeFromCodeCoverage]
        private void FillRectangle(SpriteBatch spriteBatch, Rectangle rect, Color color, float depth)
        {
            spriteBatch.Draw(_whiteTexture!, rect, null, color, 0f, Vector2.Zero, SpriteEffects.None, depth);
        }

        /// <summary>
        /// Milliseconds → bounded seconds for <see cref="SongStatusPanel.FormatDuration"/>:
        /// non-finite inputs return 0 and the result is clamped well below
        /// TimeSpan.FromSeconds' range so a degenerate chart can never throw.
        /// </summary>
        internal static double ToSafeSeconds(double ms)
        {
            if (!double.IsFinite(ms))
                return 0.0;
            return Math.Clamp(ms / 1000.0, 0.0, 359999.0);
        }

        internal static float Clamp01(float value)
        {
            // The stage already clamps progress; guard anyway so a NaN (e.g. a
            // zero-duration chart) can never become a garbage fill width.
            if (!float.IsFinite(value))
                return 0f;
            return Math.Clamp(value, 0f, 1f);
        }

        // HPA-509 test accessors: flash slots and the popup slot are plain state
        // (no GPU involvement), so tests observe them directly.
        internal JudgementTextPopup? PopupForTesting => _popup;
        internal (string? Text, bool IsSlow) PopupTimingForTesting => (_popupTimingText, _popupTimingIsSlow);
        internal IReadOnlyList<float> FlashTimersForTesting => _flashTimers;
        internal IReadOnlyList<Color> FlashColorsForTesting => _flashColors;

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
                _tileFont?.Dispose();
                _tileFont = null;
                // _whiteTexture belongs to the stage (the shared fallback pixel) —
                // this class borrows it and must not dispose it.
            }

            _resourcesReady = false;
            _disposed = true;
        }
    }
}
