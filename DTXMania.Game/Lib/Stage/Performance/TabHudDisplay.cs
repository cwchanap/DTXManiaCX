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
using DTXMania.Game.Lib.UI;
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
    /// Compact Tab-mode HUD renderer (HPA-509), styled after the skin's neon panel
    /// language: a title panel and a SCORE panel on top, combo + judgement above the
    /// strip hit marker, a segmented LIFE gauge, a thin progress rail and a bottom
    /// stats panel. Presentation only — every value arrives via
    /// <see cref="TabHudFrameState"/>; the level-weighted game skill reuses the shared
    /// <see cref="SongScore.CalculateGameSkill"/> formula on the same chart values
    /// SkillPanelDisplay draws. Colors come from the theme-resolved
    /// <see cref="TabModePalette"/>; panels use the skin's nine-slice
    /// <see cref="TabModeArt.Panel"/> when present. Fonts are built once and released on dispose;
    /// the white-pixel primitive is the stage's shared fallback texture, borrowed
    /// and never disposed here. When acquisition fails <see cref="IsReady"/> stays
    /// false and the stage rolls the strip back to a consistent Lanes fallback.
    /// </summary>
    public class TabHudDisplay : IDisposable
    {
        // Upper-case judgement words in the skin's display caps, built once from the
        // shared popup table so the Tab HUD never allocates a word per judgement.
        private static readonly Dictionary<JudgementType, string> JudgementWords = BuildJudgementWords();

        private readonly GraphicsDevice _graphicsDevice;
        private readonly SongChart? _chart;
        private readonly string _title;
        private readonly string _artist;
        private readonly Texture2D? _whiteTexture;
        private readonly TabModeArt _art;
        private ManagedFont? _comboFont;
        private ManagedFont? _scoreFont;
        private ManagedFont? _titleFont;
        private ManagedFont? _artistFont;
        private ManagedFont? _valueFont;
        private ManagedFont? _captionFont;
        private ManagedFont? _monoFont;
        private bool _resourcesReady;
        private bool _disposed;

        // Newest-wins judgement popup slot (HPA-509): each judgement replaces the
        // previous popup — no queue/stack — reusing JudgementTextPopup's own
        // fade/rise/expiry. The fixed fast/slow line rides above it.
        private JudgementTextPopup? _popup;
        private Color _popupColor;
        private string? _popupTimingText;
        private bool _popupTimingIsSlow;

        // Per-lane key flash slots (HPA-509): one timer+color per lane, overwritten by
        // manual input / judged hits (lane color) / Miss (danger) — never a queue.
        // Each lane lights the key cap its strip label sits in.
        private readonly float[] _flashTimers = new float[PerformanceUILayout.LaneCount];
        private readonly Color[] _flashColors = new Color[PerformanceUILayout.LaneCount];
        private readonly Rectangle[] _keyCapsByLane = new Rectangle[PerformanceUILayout.LaneCount];

        // Per-frame string caches (HPA-509 review): rebuilt only when the underlying
        // value changes — the same convention ScoreDisplay and ComboDisplay use.
        private int _lastScore = -1;
        private string _cachedScoreText = string.Empty;
        private float _cachedScoreWidth;
        private string? _cachedTitle;
        private string? _cachedArtist;
        private int _lastCombo = -1;
        private string _cachedComboText = string.Empty;
        private Vector2 _cachedComboSize;
        private int _lastElapsedSec = -1;
        private int _lastTotalSec = -1;
        private string _cachedTimeText = string.Empty;
        private Vector2 _cachedTimeSize;
        private string[]? _cachedStatLabels;
        private (int, int, int, int, int, int, int, int, double, double)? _lastStatsKey;
        private readonly string[] _cachedStatValues = new string[9];
        private string? _cachedTimingText;
        private Vector2 _cachedTimingSize;

        public TabHudDisplay(GraphicsDevice graphicsDevice, SongChart? chart, string? title, string? artist,
            Texture2D? whiteTexture = null, TabModePalette? palette = null, TabModeArt? art = null)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _chart = chart;
            _title = title ?? string.Empty;
            _artist = artist ?? string.Empty;
            _whiteTexture = whiteTexture;
            Palette = palette ?? TabModePalette.Default;
            _art = art ?? TabModeArt.None;

            // Lane→key-cap anchors for the flash slots; all ten primary labels map
            // one-to-one onto lanes 0-9 (verified by test), so a plain array suffices.
            foreach (var anchor in NotationStripLayout.GetKeyColumnLabels())
            {
                if (anchor.Lane >= 0 && anchor.Lane < _keyCapsByLane.Length)
                    _keyCapsByLane[anchor.Lane] = NotationStripLayout.GetKeyCapRect(anchor);
            }

            // Font acquisition needs a live GraphicsDevice, so it can never run
            // under headless coverage — excluded like the Draw paths.
            AcquireDrawResources();
        }

        /// <summary>Theme-resolved colors shared with the notation strip.</summary>
        public TabModePalette Palette { get; }

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

        private bool HasDrawResources =>
            _whiteTexture != null
            && _comboFont != null && _scoreFont != null && _valueFont != null
            && _captionFont != null && _monoFont != null
            && _titleFont != null && _artistFont != null;

        [ExcludeFromCodeCoverage]
        private void AcquireDrawResources()
        {
            try
            {
                _comboFont = ManagedFont.CreateFont(_graphicsDevice, TabHud.DisplayFontFamily, TabHud.ComboFontSizePt);
                _scoreFont = ManagedFont.CreateFont(_graphicsDevice, TabHud.DisplayFontFamily, TabHud.ScoreFontSizePt);
                _valueFont = ManagedFont.CreateFont(_graphicsDevice, TabHud.DisplayFontFamily, TabHud.ValueFontSizePt);
                _captionFont = ManagedFont.CreateFont(_graphicsDevice, TabHud.DisplayFontFamily, TabHud.CaptionFontSizePt);
                _monoFont = ManagedFont.CreateFont(_graphicsDevice, TabHud.MonoFontFamily, TabHud.MonoFontSizePt);

                // Orbitron is Latin-only: non-ASCII titles/artists keep the CJK serif.
                _titleFont = DisplayText.IsAsciiDisplayable(_title)
                    ? ManagedFont.CreateFont(_graphicsDevice, TabHud.DisplayFontFamily, TabHud.TitleFontSizePt)
                    : ManagedFont.CreateFont(_graphicsDevice, TabHud.SerifFontFamily, TabHud.SerifTitleFontSizePt);
                _artistFont = DisplayText.IsAsciiDisplayable(_artist)
                    ? ManagedFont.CreateFont(_graphicsDevice, TabHud.MonoFontFamily, TabHud.MonoFontSizePt)
                    : ManagedFont.CreateFont(_graphicsDevice, TabHud.SerifFontFamily, TabHud.SerifSmallFontSizePt);

                _resourcesReady = HasDrawResources;
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
            if (_disposed || spriteBatch == null || !HasDrawResources)
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
        /// predicate. The word comes from JudgementTextPopupManager's shared table in
        /// display caps, its color from the palette's judgement ramp; fast/slow
        /// classification uses the shared rounded ProjectDelta.
        /// </summary>
        public void NotifyJudgement(JudgementType type, bool showTiming, double deltaMs)
        {
            if (!JudgementWords.TryGetValue(type, out var word))
                return;

            // No _disposed guard: this is pure presentation state (no GPU resources)
            // and the draw paths separately skip when fonts failed to acquire.
            // Half-width centering needs a font; without one (acquisition failed) the
            // draw is skipped anyway, so fall back to the raw center x.
            float halfWidth = _valueFont != null ? _valueFont.MeasureString(word).X / 2f : 0f;
            _popup = new JudgementTextPopup(
                word,
                new Vector2(TabHud.HitFeedbackCenterX - halfWidth, TabHud.PopupWordTop),
                TabHud.PopupRiseDistance);
            _popupColor = Palette.ForJudgement(type);

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
        /// Refresh (overwrite) a lane's key flash slot — manual input or a judged hit
        /// lights the cap in its lane color, a Miss in the danger color. No queue: the
        /// newest event for a lane wins.
        /// </summary>
        public void TriggerKeyFlash(int lane, bool isMiss = false)
        {
            if (lane < 0 || lane >= _flashTimers.Length)
                return;

            _flashTimers[lane] = 1f;
            _flashColors[lane] = isMiss ? Palette.Danger : PerformanceUILayout.GetLaneColor(lane);
        }

        /// <summary>
        /// Draw the per-lane key flashes in the BASE BackToFront pass, immediately
        /// after the notation strip: depth 0.35 sits between the key-cap fill (0.38)
        /// and the cap border (0.33), so a lit cap keeps its outline and its label
        /// (0.30) on top.
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

                FillRectangle(
                    spriteBatch,
                    _keyCapsByLane[lane],
                    _flashColors[lane] * (TabHud.KeyFlashAlpha * timer),
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
            if (_popup == null || !_popup.IsActive)
                return;

            var position = _popup.CurrentPosition;
            DrawShadowed(spriteBatch, _valueFont!, _popup.Text, position, _popupColor * _popup.Alpha);

            if (_popupTimingText != null)
            {
                if (_popupTimingText != _cachedTimingText)
                {
                    _cachedTimingSize = _monoFont!.MeasureString(_popupTimingText);
                    _cachedTimingText = _popupTimingText;
                }
                var timingColor = _popupTimingIsSlow ? Palette.Danger : Palette.Accent;
                // The line rides a fixed gap above the word's current position so
                // the popup's shortened Tab rise can never sweep through it.
                float timingTop = position.Y - _cachedTimingSize.Y - TabHud.PopupTimingGap;
                DrawShadowed(
                    spriteBatch,
                    _monoFont!,
                    _popupTimingText,
                    new Vector2(TabHud.HitFeedbackCenterX - _cachedTimingSize.X / 2f, timingTop),
                    timingColor * _popup.Alpha);
            }
        }

        [ExcludeFromCodeCoverage]
        private void DrawHeader(SpriteBatch spriteBatch, int score)
        {
            int height = TabHud.HeaderBottom - TabHud.HeaderTop;

            // Title panel: title in the display face (serif for CJK), artist below it.
            var titlePanel = new Rectangle(TabHud.HeaderLeft, TabHud.HeaderTop, TabHud.TitlePanelRight - TabHud.HeaderLeft, height);
            DrawPanel(spriteBatch, titlePanel, TabHud.PanelChamfer);
            float textMaxWidth = titlePanel.Width - 2 * TabHud.PanelPaddingX;
            _cachedTitle ??= TextHelper.TruncateToWidth(_title, textMaxWidth, _titleFont!);
            _cachedArtist ??= TextHelper.TruncateToWidth(_artist, textMaxWidth, _artistFont!);
            float textX = titlePanel.X + TabHud.PanelPaddingX;
            DrawText(
                spriteBatch,
                _titleFont!,
                _cachedTitle,
                new Vector2(textX, MathF.Round(TabHud.HeaderTitleCenterY - _titleFont!.LineSpacing / 2f)),
                Palette.TextPrimary);
            DrawText(
                spriteBatch,
                _artistFont!,
                _cachedArtist,
                new Vector2(textX, MathF.Round(TabHud.HeaderArtistCenterY - _artistFont!.LineSpacing / 2f)),
                Palette.TextSecondary);

            // SCORE panel: caption pill on the left, zero-padded digits right-aligned
            // like the Lanes score counter.
            var scorePanel = new Rectangle(TabHud.ScorePanelLeft, TabHud.HeaderTop, TabHud.HeaderRight - TabHud.ScorePanelLeft, height);
            DrawPanel(spriteBatch, scorePanel, TabHud.PanelChamfer);
            DrawCaption(spriteBatch, "SCORE", scorePanel.X + TabHud.PanelPaddingX, scorePanel.Center.Y, alignRight: false);

            if (score != _lastScore)
            {
                _cachedScoreText = Math.Max(0, score).ToString("D" + TabHud.ScoreDigits, CultureInfo.InvariantCulture);
                _cachedScoreWidth = _scoreFont!.MeasureString(_cachedScoreText).X;
                _lastScore = score;
            }
            var scorePosition = new Vector2(
                scorePanel.Right - TabHud.PanelPaddingX - _cachedScoreWidth,
                scorePanel.Center.Y - _scoreFont!.LineSpacing / 2f);
            DrawGlowing(spriteBatch, _scoreFont!, _cachedScoreText, scorePosition, Palette.TextPrimary);
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
                _cachedComboSize = _comboFont!.MeasureString(_cachedComboText);
                _lastCombo = state.Combo;
            }
            Vector2 position = new(
                TabHud.HitFeedbackCenterX - _cachedComboSize.X / 2f,
                TabHud.HitFeedbackTop);
            DrawGlowing(spriteBatch, _comboFont!, _cachedComboText, position, Palette.TextPrimary);

            const string caption = "COMBO";
            float captionWidth = _captionFont!.MeasureString(caption).X;
            DrawShadowed(
                spriteBatch,
                _captionFont!,
                caption,
                new Vector2(TabHud.HitFeedbackCenterX - captionWidth / 2f, position.Y + _cachedComboSize.Y + TabHud.ComboCaptionGap),
                Palette.Accent);
        }

        [ExcludeFromCodeCoverage]
        private void DrawLife(SpriteBatch spriteBatch, float life)
        {
            int height = TabHud.LifeBottom - TabHud.LifeTop;
            DrawCaption(spriteBatch, "LIFE", TabHud.LifeLabelRight, TabHud.LifeTop + height / 2, alignRight: true);

            // Segmented gauge in a small chamfered frame; each segment's color follows
            // its position (danger quarter red→amber, then accent→success) and only the
            // segments under the current life are lit — the last one partially.
            var frame = new Rectangle(TabHud.LifeLeft, TabHud.LifeTop, TabHud.LifeRight - TabHud.LifeLeft, height);
            DrawPanel(spriteBatch, frame, TabHud.CaptionChamfer);

            var inner = new Rectangle(
                frame.X + TabHud.LifeInset,
                frame.Y + TabHud.LifeInset,
                frame.Width - 2 * TabHud.LifeInset,
                frame.Height - 2 * TabHud.LifeInset);
            int count = TabHud.LifeSegmentCount;
            float segmentWidth = (inner.Width - (count - 1) * TabHud.LifeSegmentGap) / (float)count;
            float lit = Clamp01(life) * count;
            for (int i = 0; i < count; i++)
            {
                int x = inner.X + (int)MathF.Round(i * (segmentWidth + TabHud.LifeSegmentGap));
                int width = (int)MathF.Round(segmentWidth);
                Color color = Palette.GaugeSegment((i + 0.5f) / count);
                FillRectangle(spriteBatch, new Rectangle(x, inner.Y, width, inner.Height), color * TabHud.LifeUnlitAlpha, TabHud.DepthBarBack);

                float fill = Math.Clamp(lit - i, 0f, 1f);
                if (fill > 0f)
                {
                    FillRectangle(
                        spriteBatch,
                        new Rectangle(x, inner.Y, Math.Max(1, (int)(width * fill)), inner.Height),
                        color,
                        TabHud.DepthBarFill);
                }
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
                _cachedTimeSize = _monoFont!.MeasureString(_cachedTimeText);
                _lastElapsedSec = eSec;
                _lastTotalSec = tSec;
            }
            // Right-aligned so the rail's fixed left edge can never shift with the
            // text width.
            float centerY = (TabHud.ProgressTop + TabHud.ProgressBottom) / 2f;
            Vector2 timePosition = new(
                TabHud.ProgressBarLeft - TabHud.ProgressBarGap - _cachedTimeSize.X,
                centerY - _cachedTimeSize.Y / 2f);
            DrawShadowed(spriteBatch, _monoFont!, _cachedTimeText, timePosition, Palette.TextSecondary);

            // Thin rail fixed at ProgressBarLeft with a bright head at the fill end.
            var track = new Rectangle(
                TabHud.ProgressBarLeft,
                (int)(centerY - TabHud.ProgressBarHeight / 2f),
                Math.Max(0, TabHud.ProgressRight - TabHud.ProgressBarLeft),
                TabHud.ProgressBarHeight);
            FillRectangle(spriteBatch, track, Palette.TextFaint * TabHud.ProgressTrackAlpha, TabHud.DepthBarBack);

            float clamped = Clamp01(progress);
            if (clamped > 0f && track.Width > 0)
            {
                int fillWidth = (int)(track.Width * clamped);
                FillRectangle(spriteBatch, new Rectangle(track.X, track.Y, fillWidth, track.Height), Palette.Accent, TabHud.DepthBarFill);
                FillRectangle(
                    spriteBatch,
                    new Rectangle(track.X + fillWidth - 2, track.Y - 3, 2, track.Height + 6),
                    Palette.TextPrimary,
                    TabHud.DepthText);
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

            var panel = new Rectangle(TabHud.StatsLeft, TabHud.StatsTop, TabHud.StatsRight - TabHud.StatsLeft, TabHud.StatsBottom - TabHud.StatsTop);
            DrawPanel(spriteBatch, panel, TabHud.PanelChamfer);

            // Fast/Slow totals are always visible, independent of the per-hit timing
            // preference. Poor is presented as OK. Trim the legacy 4-wide judgement
            // padding so every value's left edge aligns with its label.
            float tileWidth = panel.Width / (float)_cachedStatValues.Length;
            float textMaxWidth = tileWidth - 2 * TabHud.StatsTilePadX;
            var statsKey = (state.PerfectCount, state.GreatCount, state.GoodCount, state.PoorCount,
                state.MissCount, state.MaxCombo, state.FastCount, state.SlowCount,
                state.Achievement, gameSkill);
            if (statsKey != _lastStatsKey)
            {
                // Values get the same tile-width bound as labels — a four-digit
                // FAST/SLOW pair would otherwise bleed into ACHIEVE (PR #203 review).
                _cachedStatValues[0] = TextHelper.TruncateToWidth(SkillPanelDisplay.FormatJudgementCount(state.PerfectCount).Trim(), textMaxWidth, _valueFont!);
                _cachedStatValues[1] = TextHelper.TruncateToWidth(SkillPanelDisplay.FormatJudgementCount(state.GreatCount).Trim(), textMaxWidth, _valueFont!);
                _cachedStatValues[2] = TextHelper.TruncateToWidth(SkillPanelDisplay.FormatJudgementCount(state.GoodCount).Trim(), textMaxWidth, _valueFont!);
                _cachedStatValues[3] = TextHelper.TruncateToWidth(SkillPanelDisplay.FormatJudgementCount(state.PoorCount).Trim(), textMaxWidth, _valueFont!);
                _cachedStatValues[4] = TextHelper.TruncateToWidth(SkillPanelDisplay.FormatJudgementCount(state.MissCount).Trim(), textMaxWidth, _valueFont!);
                _cachedStatValues[5] = TextHelper.TruncateToWidth(state.MaxCombo.ToString(CultureInfo.InvariantCulture), textMaxWidth, _valueFont!);
                _cachedStatValues[6] = TextHelper.TruncateToWidth($"{state.FastCount.ToString(CultureInfo.InvariantCulture)}/{state.SlowCount.ToString(CultureInfo.InvariantCulture)}", textMaxWidth, _valueFont!);
                _cachedStatValues[7] = TextHelper.TruncateToWidth(SkillPanelDisplay.FormatSkillText(state.Achievement).Trim() + "%", textMaxWidth, _valueFont!);
                _cachedStatValues[8] = TextHelper.TruncateToWidth(SkillPanelDisplay.FormatSkillText(gameSkill).Trim(), textMaxWidth, _valueFont!);
                _lastStatsKey = statsKey;
            }

            // The label strings and tile width are both fixed, so the truncation is
            // computed once and reused every frame.
            _cachedStatLabels ??= BuildStatLabels(textMaxWidth);
            float labelY = panel.Y + TabHud.StatsLabelInsetY;
            float valueY = labelY + _monoFont!.LineSpacing + TabHud.StatsValueGap;
            for (int i = 0; i < _cachedStatValues.Length; i++)
            {
                float x = panel.X + i * tileWidth;
                if (i > 0)
                {
                    FillRectangle(
                        spriteBatch,
                        new Rectangle((int)x, panel.Y + TabHud.StatsDividerInsetY, 1, panel.Height - 2 * TabHud.StatsDividerInsetY),
                        Palette.TextFaint * TabHud.StatsDividerAlpha,
                        TabHud.DepthPanelBorder);
                }

                float textX = x + TabHud.StatsTilePadX;
                DrawText(spriteBatch, _monoFont!, _cachedStatLabels[i], new Vector2(textX, labelY), StatLabelColor(i));
                DrawText(spriteBatch, _valueFont!, _cachedStatValues[i], new Vector2(textX, valueY), i == 8 ? Palette.Accent : Palette.TextPrimary);
            }
        }

        // Judgement tiles carry the judgement ramp (as the result ledger does);
        // the remaining tiles read on the secondary text step.
        private Color StatLabelColor(int tile) => tile switch
        {
            0 => Palette.Perfect,
            1 => Palette.Great,
            2 => Palette.Good,
            3 => Palette.Poor,
            4 => Palette.Miss,
            _ => Palette.TextSecondary,
        };

        [ExcludeFromCodeCoverage]
        private string[] BuildStatLabels(float maxWidth)
        {
            // Constrain every label inside its own tile: an unconstrained long
            // label bleeds into the next tile.
            return new[]
            {
                TextHelper.TruncateToWidth("PERFECT", maxWidth, _monoFont!),
                TextHelper.TruncateToWidth("GREAT", maxWidth, _monoFont!),
                TextHelper.TruncateToWidth("GOOD", maxWidth, _monoFont!),
                TextHelper.TruncateToWidth("OK", maxWidth, _monoFont!),
                TextHelper.TruncateToWidth("MISS", maxWidth, _monoFont!),
                TextHelper.TruncateToWidth("MAX COMBO", maxWidth, _monoFont!),
                TextHelper.TruncateToWidth("FAST/SLOW", maxWidth, _monoFont!),
                TextHelper.TruncateToWidth("ACHIEVE", maxWidth, _monoFont!),
                TextHelper.TruncateToWidth("SKILL", maxWidth, _monoFont!),
            };
        }

        /// <summary>
        /// Caption pill (SCORE / LIFE): accent-outlined chamfered tag with the caption
        /// in the display face, anchored at <paramref name="x"/> (its left edge, or its
        /// right edge when <paramref name="alignRight"/>), centered on <paramref name="centerY"/>.
        /// The fill is opaque so a bright backdrop never shows through the caption.
        /// </summary>
        [ExcludeFromCodeCoverage]
        private void DrawCaption(SpriteBatch spriteBatch, string text, int x, int centerY, bool alignRight)
        {
            var size = _captionFont!.MeasureString(text);
            int width = (int)MathF.Ceiling(size.X) + 2 * TabHud.CaptionPadX;
            var pill = new Rectangle(
                alignRight ? x - width : x,
                centerY - TabHud.CaptionHeight / 2,
                width,
                TabHud.CaptionHeight);
            DrawChamfered(spriteBatch, pill, TabHud.CaptionChamfer, Palette.KeyColumn, Palette.Accent);
            DrawText(
                spriteBatch,
                _captionFont!,
                text,
                new Vector2(pill.X + TabHud.CaptionPadX, MathF.Round(centerY - _captionFont!.LineSpacing / 2f)),
                Palette.TextPrimary);
        }

        [ExcludeFromCodeCoverage]
        private void DrawPanel(SpriteBatch spriteBatch, Rectangle rect, int chamfer)
        {
            var panel = _art.Panel?.Texture;
            if (panel == null)
            {
                DrawChamfered(spriteBatch, rect, chamfer, Palette.Panel, Palette.Accent * TabHud.PanelBorderAlpha);
                return;
            }

            // Skin panel art: the border and glow are baked, so the whole plate sits
            // on the panel plane.
            Span<Rectangle> sources = stackalloc Rectangle[9];
            Span<Rectangle> destinations = stackalloc Rectangle[9];
            TabModeArt.GetNineSlice(rect, panel.Width, panel.Height, TabHud.PanelArtCorner, sources, destinations);
            for (int i = 0; i < 9; i++)
            {
                if (destinations[i].Width > 0 && destinations[i].Height > 0)
                    spriteBatch.Draw(panel, destinations[i], sources[i], Color.White, 0f, Vector2.Zero, SpriteEffects.None, TabHud.DepthPanel);
            }
        }

        /// <summary>
        /// Chamfered box from 1px primitives: the fill steps in one pixel per row
        /// inside each 45° corner cut, and the border runs the four edges plus the
        /// four diagonal cuts. Fill sits on DepthPanel, border on DepthPanelBorder.
        /// </summary>
        [ExcludeFromCodeCoverage]
        private void DrawChamfered(SpriteBatch spriteBatch, Rectangle r, int c, Color fill, Color border)
        {
            FillRectangle(spriteBatch, new Rectangle(r.X, r.Y + c, r.Width, r.Height - 2 * c), fill, TabHud.DepthPanel);
            for (int i = 0; i < c; i++)
            {
                int inset = c - i;
                FillRectangle(spriteBatch, new Rectangle(r.X + inset, r.Y + i, r.Width - 2 * inset, 1), fill, TabHud.DepthPanel);
                FillRectangle(spriteBatch, new Rectangle(r.X + inset, r.Bottom - 1 - i, r.Width - 2 * inset, 1), fill, TabHud.DepthPanel);
            }

            FillRectangle(spriteBatch, new Rectangle(r.X + c, r.Y, r.Width - 2 * c, 1), border, TabHud.DepthPanelBorder);
            FillRectangle(spriteBatch, new Rectangle(r.X + c, r.Bottom - 1, r.Width - 2 * c, 1), border, TabHud.DepthPanelBorder);
            FillRectangle(spriteBatch, new Rectangle(r.X, r.Y + c, 1, r.Height - 2 * c), border, TabHud.DepthPanelBorder);
            FillRectangle(spriteBatch, new Rectangle(r.Right - 1, r.Y + c, 1, r.Height - 2 * c), border, TabHud.DepthPanelBorder);
            for (int i = 0; i < c; i++)
            {
                FillRectangle(spriteBatch, new Rectangle(r.X + i, r.Y + c - 1 - i, 1, 1), border, TabHud.DepthPanelBorder);
                FillRectangle(spriteBatch, new Rectangle(r.Right - 1 - i, r.Y + c - 1 - i, 1, 1), border, TabHud.DepthPanelBorder);
                FillRectangle(spriteBatch, new Rectangle(r.X + i, r.Bottom - c + i, 1, 1), border, TabHud.DepthPanelBorder);
                FillRectangle(spriteBatch, new Rectangle(r.Right - 1 - i, r.Bottom - c + i, 1, 1), border, TabHud.DepthPanelBorder);
            }
        }

        [ExcludeFromCodeCoverage]
        private static void DrawText(SpriteBatch spriteBatch, ManagedFont font, string text, Vector2 position, Color color)
        {
            font.DrawString(spriteBatch, text, position, color, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, TabHud.DepthText);
        }

        /// <summary>
        /// Big digits (score, combo) with a soft accent glow: four faint accent copies
        /// offset around the text on the shadow plane, then the text itself.
        /// </summary>
        [ExcludeFromCodeCoverage]
        private void DrawGlowing(SpriteBatch spriteBatch, ManagedFont font, string text, Vector2 position, Color color)
        {
            Color glow = Palette.Accent * TabHud.GlowAlpha;
            float o = TabHud.GlowOffset;
            for (int i = 0; i < 4; i++)
            {
                var offset = new Vector2(i < 2 ? (i == 0 ? -o : o) : 0f, i >= 2 ? (i == 2 ? -o : o) : 0f);
                font.DrawString(spriteBatch, text, position + offset, glow, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, TabHud.DepthTextShadow);
            }
            DrawText(spriteBatch, font, text, position, color);
        }

        /// <summary>
        /// Draw Tab HUD text that floats over the BGA (combo caption, judgement,
        /// timing, progress time) with the shared shadow convention. Two explicit
        /// draw calls with their own planes replace DrawStringWithShadow — passing a
        /// depth through the shared helper shifted every legacy caller's shadow
        /// (HPA-509 review). The shadow color tracks the text's alpha so fading
        /// popups do not leave a fixed-opacity ghost.
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
            DrawText(spriteBatch, font, text, position, color);
        }

        [ExcludeFromCodeCoverage]
        private void FillRectangle(SpriteBatch spriteBatch, Rectangle rect, Color color, float depth)
        {
            spriteBatch.Draw(_whiteTexture!, rect, null, color, 0f, Vector2.Zero, SpriteEffects.None, depth);
        }

        private static Dictionary<JudgementType, string> BuildJudgementWords()
        {
            var words = new Dictionary<JudgementType, string>();
            foreach (var (type, text) in JudgementTextPopupManager.JudgementTexts)
                words[type] = text.ToUpperInvariant();
            return words;
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
        internal Color PopupColorForTesting => _popupColor;
        internal (string? Text, bool IsSlow) PopupTimingForTesting => (_popupTimingText, _popupTimingIsSlow);
        internal IReadOnlyList<float> FlashTimersForTesting => _flashTimers;
        internal IReadOnlyList<Color> FlashColorsForTesting => _flashColors;
        internal IReadOnlyList<Rectangle> KeyCapsByLaneForTesting => _keyCapsByLane;

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
                _comboFont?.Dispose();
                _comboFont = null;
                _scoreFont?.Dispose();
                _scoreFont = null;
                _titleFont?.Dispose();
                _titleFont = null;
                _artistFont?.Dispose();
                _artistFont = null;
                _valueFont?.Dispose();
                _valueFont = null;
                _captionFont?.Dispose();
                _captionFont = null;
                _monoFont?.Dispose();
                _monoFont = null;
                // _whiteTexture belongs to the stage (the shared fallback pixel) —
                // this class borrows it and must not dispose it.
            }

            _resourcesReady = false;
            _disposed = true;
        }
    }
}
