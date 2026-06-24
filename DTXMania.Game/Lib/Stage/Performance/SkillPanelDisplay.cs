#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Renders the left-side performance status panel: difficulty level,
    /// current skill percentage, and (optionally) the MAX badge sprite.
    /// </summary>
    public class SkillPanelDisplay : IDisposable
    {
        #region Private Fields

        private readonly GraphicsDevice _graphicsDevice;
        private readonly SongChart? _chart;
        private ManagedFont? _font;
        private ITexture? _maxBadgeTexture;
        private ITexture? _smallRateNumbersTexture;
        private ITexture? _largeRateNumbersTexture;
        private ITexture? _levelNumbersTexture;
        private ITexture? _ratePercentTexture;
        private ITexture? _difficultyPanelTexture;
        private bool _disposed;

        #endregion

        #region Properties

        /// <summary>
        /// Current skill percentage (0.0 - 100.0) rendered as the large number.
        /// </summary>
        public double Skill { get; set; }

        /// <summary>
        /// Whether the MAX badge sprite should be drawn over the skill number.
        /// </summary>
        public bool ShowMax { get; set; }

        public int PerfectCount { get; private set; }
        public int GreatCount { get; private set; }
        public int GoodCount { get; private set; }
        public int PoorCount { get; private set; }
        public int MissCount { get; private set; }
        public int MaxCombo { get; private set; }
        public int ProcessedJudgementCount => GetProcessedJudgementCount(
            PerfectCount,
            GreatCount,
            GoodCount,
            PoorCount,
            MissCount);

        #endregion

        #region Constructor

        public SkillPanelDisplay(IResourceManager resourceManager, GraphicsDevice graphicsDevice, SongChart? chart)
        {
            if (resourceManager == null) throw new ArgumentNullException(nameof(resourceManager));
            _graphicsDevice  = graphicsDevice  ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _chart           = chart;

            try
            {
                _font = ManagedFont.CreateFont(_graphicsDevice, "NotoSerifJP", 24, FontStyle.Bold);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SkillPanelDisplay: Failed to create font: {ex.Message}");
                _font = null;
            }

            try
            {
                _maxBadgeTexture = resourceManager.LoadTexture(TexturePath.SkillMax);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SkillPanelDisplay: Failed to load MAX badge: {ex.Message}");
                _maxBadgeTexture = null;
            }

            _smallRateNumbersTexture = TryLoadTexture(resourceManager, TexturePath.RateNumbersSmall, "small rate numbers");
            _largeRateNumbersTexture = TryLoadTexture(resourceManager, TexturePath.RateNumbersLarge, "large rate numbers");
            _levelNumbersTexture = TryLoadTexture(resourceManager, TexturePath.LevelNumbers, "level numbers");
            _ratePercentTexture = TryLoadTexture(resourceManager, TexturePath.RatePercent, "rate percent");
            _difficultyPanelTexture = TryLoadTexture(resourceManager, TexturePath.PerformanceDifficultyPanel, "difficulty badge");
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Update the panel state. No animation yet.
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last update</param>
        public void Update(double deltaTime) { /* no animation yet */ }

        public void ProcessJudgement(JudgementEvent judgementEvent, int maxCombo)
        {
            if (judgementEvent == null)
                return;

            switch (judgementEvent.Type)
            {
                case JudgementType.Perfect:
                    PerfectCount++;
                    break;
                case JudgementType.Great:
                    GreatCount++;
                    break;
                case JudgementType.Good:
                    GoodCount++;
                    break;
                case JudgementType.Poor:
                    PoorCount++;
                    break;
                case JudgementType.Miss:
                    MissCount++;
                    break;
            }

            MaxCombo = Math.Max(MaxCombo, maxCombo);
        }

        /// <summary>
        /// Draw the skill panel contents.
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        [ExcludeFromCodeCoverage]
        public void Draw(SpriteBatch spriteBatch)
        {
            if (_disposed || spriteBatch == null) return;

            int level    = _chart?.DrumLevel    ?? 0;
            int levelDec = _chart?.DrumLevelDec ?? 0;

            // Difficulty badge (7_Difficulty.png). NX draws the 60x60 cell for the chart's difficulty
            // label at (14 + n本体X, 266 + n本体Y), which equals DifficultyIcon.Bounds. The level number
            // is drawn on top of it just below.
            if (_difficultyPanelTexture != null)
            {
                var iconBounds = PerformanceUILayout.SkillPanel.DifficultyIcon.Bounds;
                _difficultyPanelTexture.Draw(
                    spriteBatch,
                    new Vector2(iconBounds.X, iconBounds.Y),
                    GetDifficultyPanelSourceRect(_chart?.DifficultyLabel));
            }

            string levelText = FormatLevelText(level, levelDec);
            if (_levelNumbersTexture != null && CanRenderWithLevelTexture(levelText))
            {
                DrawLevelNumberText(spriteBatch, levelText, PerformanceUILayout.SkillPanel.LevelNumber.StartPosition);
            }
            else
            {
                DrawFallbackText(spriteBatch, levelText, PerformanceUILayout.SkillPanel.LevelNumber.StartPosition);
            }

            string skillText = FormatSkillText(Skill);
            if (_largeRateNumbersTexture != null)
            {
                DrawLargeRateText(spriteBatch, skillText, PerformanceUILayout.SkillPanel.SkillPercent.NumbersPosition);
            }
            else
            {
                DrawFallbackText(spriteBatch, skillText, PerformanceUILayout.SkillPanel.SkillPercent.NumbersPosition);
            }

            if (_ratePercentTexture != null)
            {
                _ratePercentTexture.Draw(spriteBatch, PerformanceUILayout.SkillPanel.SkillPercent.PercentPosition);
            }
            else
            {
                DrawFallbackText(spriteBatch, "%", PerformanceUILayout.SkillPanel.SkillPercent.PercentPosition);
            }

            // Game skill ("曲別SKILL"): the level-weighted skill value NX draws near the bottom of
            // the panel via tCalculateGameSkillFromPlayingSkill. Without this the panel's SKILL slot
            // shows no number. Reuses the large rate-number sprite, mirroring NX t大文字表示.
            string gameSkillText = FormatSkillText(SongScore.CalculateGameSkill(Skill, level, levelDec));
            if (_largeRateNumbersTexture != null)
            {
                DrawLargeRateText(spriteBatch, gameSkillText, PerformanceUILayout.SkillPanel.GameSkill.NumbersPosition);
            }
            else
            {
                DrawFallbackText(spriteBatch, gameSkillText, PerformanceUILayout.SkillPanel.GameSkill.NumbersPosition);
            }

            DrawJudgementCounts(spriteBatch);

            if (ShowMax && _maxBadgeTexture != null)
            {
                _maxBadgeTexture.Draw(spriteBatch,
                    PerformanceUILayout.SkillPanel.SkillPercent.MaxBadgePosition);
            }
        }

        /// <summary>
        /// Format the difficulty level text using DTXManiaNX encoding rules.
        /// Match DTXManiaNX CScoreIni.tCalculateGameSkillFromPlayingSkill encoding:
        ///   level &gt;= 100 → displayed = level/100  (e.g. 850 → 8.50)
        ///   level &lt;  100 → displayed = level/10 + levelDec/100  (e.g. 80+50 → 8.50)
        /// </summary>
        public static string FormatLevelText(int level, int levelDec)
        {
            if (level <= 0) return "--";
            double actual = level >= 100
                ? level / 100.0
                : (level / 10.0) + (levelDec / 100.0);
            return actual.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Maps a difficulty label to its 60x60 source cell in 7_Difficulty.png (scene 7 of the
        /// default Script/difficult.dtxs). All cells share X=0; the label selects the Y offset.
        /// Unknown/empty labels fall back to the first cell ("DTX"), matching NX's default rect.
        /// </summary>
        public static Rectangle GetDifficultyPanelSourceRect(string? label)
        {
            const int cell = 60;
            int row = (label ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "DTX" => 0,
                "DEBUT" => 1,
                "NOVICE" => 2,
                "REGULAR" => 3,
                "EXPERT" => 4,
                "MASTER" => 5,
                "BASIC" => 6,
                "ADVANCED" => 7,
                "EXTREME" => 8,
                "RAW" => 9,
                "RWS" => 10,
                "REAL" => 11,
                _ => 0
            };
            return new Rectangle(0, row * cell, cell, cell);
        }

        /// <summary>
        /// Format the skill percentage as a 6-character right-aligned string with two decimals.
        /// </summary>
        public static string FormatSkillText(double skill)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0,6:##0.00}", skill);
        }

        public static string FormatJudgementCount(int count)
        {
            var clamped = Math.Clamp(count, 0, 9999);
            return string.Format(CultureInfo.InvariantCulture, "{0,4:###0}", clamped);
        }

        public static string FormatJudgementPercent(int count, int total)
        {
            var percent = total <= 0
                ? 0
                : (int)Math.Round(count * 100.0 / total, MidpointRounding.AwayFromZero);
            return string.Format(CultureInfo.InvariantCulture, "{0,3:##0}%", Math.Clamp(percent, 0, 100));
        }

        public static int GetProcessedJudgementCount(
            int perfectCount,
            int greatCount,
            int goodCount,
            int poorCount,
            int missCount)
        {
            return Math.Max(0, perfectCount)
                + Math.Max(0, greatCount)
                + Math.Max(0, goodCount)
                + Math.Max(0, poorCount)
                + Math.Max(0, missCount);
        }

        private static ITexture? TryLoadTexture(IResourceManager resourceManager, string path, string displayName)
        {
            try
            {
                return resourceManager.LoadTexture(path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SkillPanelDisplay: Failed to load {displayName}: {ex.Message}");
                return null;
            }
        }

        private void DrawJudgementCounts(SpriteBatch spriteBatch)
        {
            var total = ProcessedJudgementCount;
            DrawSmallRateText(spriteBatch, FormatJudgementCount(PerfectCount), PerformanceUILayout.SkillPanel.JudgementCounts.PerfectCountPos);
            DrawSmallRateText(spriteBatch, FormatJudgementCount(GreatCount), PerformanceUILayout.SkillPanel.JudgementCounts.GreatCountPos);
            DrawSmallRateText(spriteBatch, FormatJudgementCount(GoodCount), PerformanceUILayout.SkillPanel.JudgementCounts.GoodCountPos);
            DrawSmallRateText(spriteBatch, FormatJudgementCount(PoorCount), PerformanceUILayout.SkillPanel.JudgementCounts.PoorCountPos);
            DrawSmallRateText(spriteBatch, FormatJudgementCount(MissCount), PerformanceUILayout.SkillPanel.JudgementCounts.MissCountPos);
            DrawSmallRateText(spriteBatch, FormatJudgementCount(MaxCombo), PerformanceUILayout.SkillPanel.JudgementCounts.MaxComboCountPos);

            DrawSmallRateText(spriteBatch, FormatJudgementPercent(PerfectCount, total), PerformanceUILayout.SkillPanel.JudgementCounts.PerfectPercentPos);
            DrawSmallRateText(spriteBatch, FormatJudgementPercent(GreatCount, total), PerformanceUILayout.SkillPanel.JudgementCounts.GreatPercentPos);
            DrawSmallRateText(spriteBatch, FormatJudgementPercent(GoodCount, total), PerformanceUILayout.SkillPanel.JudgementCounts.GoodPercentPos);
            DrawSmallRateText(spriteBatch, FormatJudgementPercent(PoorCount, total), PerformanceUILayout.SkillPanel.JudgementCounts.PoorPercentPos);
            DrawSmallRateText(spriteBatch, FormatJudgementPercent(MissCount, total), PerformanceUILayout.SkillPanel.JudgementCounts.MissPercentPos);
            DrawSmallRateText(spriteBatch, FormatJudgementPercent(MaxCombo, total), PerformanceUILayout.SkillPanel.JudgementCounts.MaxComboPercentPos);
        }

        private void DrawSmallRateText(SpriteBatch spriteBatch, string text, Vector2 position)
        {
            if (_smallRateNumbersTexture == null)
            {
                DrawFallbackText(spriteBatch, text, position);
                return;
            }

            var x = position.X;
            foreach (var ch in text)
            {
                var source = GetSmallRateSourceRectangle(ch);
                if (source.HasValue)
                {
                    _smallRateNumbersTexture.Draw(spriteBatch, new Vector2(x, position.Y), source.Value);
                }

                x += 20;
            }
        }

        private void DrawLargeRateText(SpriteBatch spriteBatch, string text, Vector2 position)
        {
            var x = position.X;
            foreach (var ch in text)
            {
                var source = GetLargeRateSourceRectangle(ch);
                if (source.HasValue)
                {
                    _largeRateNumbersTexture!.Draw(spriteBatch, new Vector2(x, position.Y), source.Value);
                }

                x += ch == '.' ? 12 : 29;
            }
        }

        private void DrawLevelNumberText(SpriteBatch spriteBatch, string text, Vector2 position)
        {
            var x = position.X;
            foreach (var ch in text)
            {
                var source = GetLevelNumberSourceRectangle(ch);
                if (source.HasValue)
                {
                    _levelNumbersTexture!.Draw(spriteBatch, new Vector2(x, position.Y), source.Value);
                }

                x += ch == '.' ? 5 : 16;
            }
        }

        private static Rectangle? GetSmallRateSourceRectangle(char ch)
        {
            if (ch >= '0' && ch <= '9')
                return new Rectangle((ch - '0') * 20, 0, 20, 26);
            if (ch == '%')
                return new Rectangle(200, 0, 20, 26);
            if (ch == '.')
                return new Rectangle(210, 0, 10, 26);
            return null;
        }

        private static Rectangle? GetLargeRateSourceRectangle(char ch)
        {
            if (ch >= '0' && ch <= '9')
                return new Rectangle((ch - '0') * 28, 0, 28, 42);
            if (ch == '.')
                return new Rectangle(280, 0, 10, 42);
            return null;
        }

        private static Rectangle? GetLevelNumberSourceRectangle(char ch)
        {
            if (ch >= '0' && ch <= '9')
                return new Rectangle((ch - '0') * 16, 0, 16, 32);
            if (ch == '.')
                return new Rectangle(160, 0, 5, 32);
            return null;
        }

        /// <summary>
        /// Checks whether every character in the level text can be rendered
        /// using the level-number sprite sheet (digits and dot only).
        /// Falls back to font rendering for unsupported characters like '--'.
        /// </summary>
        private static bool CanRenderWithLevelTexture(string text)
        {
            foreach (var ch in text)
            {
                if (GetLevelNumberSourceRectangle(ch) == null)
                    return false;
            }
            return true;
        }

        private void DrawFallbackText(SpriteBatch spriteBatch, string text, Vector2 position)
        {
            _font?.DrawStringWithShadow(spriteBatch, text,
                position,
                Color.White,
                PerformanceUILayout.Visual.StandardShadowColor,
                PerformanceUILayout.Visual.StandardShadowOffset);
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                _font?.Dispose();
                _font = null;
                _maxBadgeTexture?.RemoveReference();
                _maxBadgeTexture = null;
                _smallRateNumbersTexture?.RemoveReference();
                _smallRateNumbersTexture = null;
                _largeRateNumbersTexture?.RemoveReference();
                _largeRateNumbersTexture = null;
                _levelNumbersTexture?.RemoveReference();
                _levelNumbersTexture = null;
                _ratePercentTexture?.RemoveReference();
                _ratePercentTexture = null;
                _difficultyPanelTexture?.RemoveReference();
                _difficultyPanelTexture = null;
            }
            _disposed = true;
        }

        #endregion
    }
}
