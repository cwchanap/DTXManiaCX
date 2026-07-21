#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Stage.Result
{
    public sealed class ResultScreenRenderer : IDisposable
    {
        private readonly IResourceManager _resources;
        private readonly IFont? _smallFont;
        private readonly IFont? _normalFont;
        private readonly IFont? _largeFont;
        private readonly IFont? _valueFont;
        private readonly IFont? _countFont;
        private readonly List<ITexture> _loadedTextures = new();

        private ITexture? _backgroundTexture;
        private ITexture? _rankBackgroundTexture;
        private ITexture? _rankTexture;
        private ITexture? _plateTexture;
        private ITexture? _jacketPanelTexture;
        private ITexture? _skillPanelTexture;
        private ITexture? _previewTexture;
        private ITexture? _newRecordTexture;
        private bool _disposed;
        private List<(ITexture texture, int original)>? _panelOriginalTransparency;

        public ResultScreenRenderer(
            IResourceManager resources,
            IFont? smallFont,
            IFont? normalFont,
            IFont? largeFont,
            IFont? valueFont = null,
            IFont? countFont = null)
        {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _smallFont = smallFont;
            _normalFont = normalFont;
            _largeFont = largeFont;
            _valueFont = valueFont;
            _countFont = countFont;
        }

        public static Matrix CreateViewportTransform(Viewport viewport)
        {
            var scaleX = viewport.Width / (float)ResultUILayout.NXViewport.Width;
            var scaleY = viewport.Height / (float)ResultUILayout.NXViewport.Height;
            var scale = Math.Min(scaleX, scaleY);
            var offsetX = viewport.X + (viewport.Width - ResultUILayout.NXViewport.Width * scale) / 2.0f;
            var offsetY = viewport.Y + (viewport.Height - ResultUILayout.NXViewport.Height * scale) / 2.0f;

            return Matrix.CreateScale(scale, scale, 1.0f)
                * Matrix.CreateTranslation(offsetX, offsetY, 0.0f);
        }

        public void Load(ResultScreenModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            ObjectDisposedException.ThrowIf(_disposed, this);

            ReleaseLoadedTextures();

            _backgroundTexture = LoadTextureIfAvailable(TexturePath.ResultBackground);
            _rankBackgroundTexture = LoadTextureIfAvailable(GetRankBackgroundPath(model.Rank));
            _rankTexture = LoadTextureIfAvailable(GetRankTexturePath(model.Rank));
            _plateTexture = model.PlateKind == ResultPlateKind.Failed
                ? null
                : LoadTextureIfAvailable(GetPlateTexturePath(model.PlateKind));
            _jacketPanelTexture = LoadTextureIfAvailable(TexturePath.ResultJacketPanel);
            _skillPanelTexture = LoadTextureIfAvailable(TexturePath.ResultSkillPanel);
            _previewTexture = LoadTextureIfAvailable(model.PreviewImagePath)
                ?? LoadTextureIfAvailable(TexturePath.ResultDefaultPreview);
            _newRecordTexture = model.NewRecord
                ? LoadTextureIfAvailable(TexturePath.ResultNewRecord)
                : null;
        }

        [ExcludeFromCodeCoverage]
        public void Draw(SpriteBatch spriteBatch, ResultScreenModel model, ResultRevealState reveal)
        {
            if (_disposed || spriteBatch == null || model == null || reveal == null)
                return;

            DrawTexture(spriteBatch, _backgroundTexture, Vector2.Zero);
            DrawTexture(spriteBatch, _rankBackgroundTexture, Vector2.Zero);
            DrawRank(spriteBatch, reveal);

            if (reveal.PanelProgress <= 0.0f)
                return;

            // Apply panel reveal alpha so visuals match the animation progress.
            // ITexture.Transparency (0-255) is multiplied into every draw call.
            var panelAlpha = Math.Clamp(reveal.PanelProgress, 0.0f, 1.0f);
            var transparency = (int)Math.Round(panelAlpha * 255);

            ApplyPanelTransparency(transparency);

            try
            {
                var theme = _resources.CurrentTheme ?? SkinTheme.Empty;
                DrawTexture(spriteBatch, _plateTexture, ResolvePlatePosition(theme));
                if (model.PlateKind == ResultPlateKind.Failed)
                    DrawText(spriteBatch, _largeFont, "FAILED", ResolveFailedTextPosition(theme), Color.Red);

                DrawTexture(spriteBatch, _jacketPanelTexture, ResolveJacketPanelPosition(theme));
                DrawPreview(spriteBatch, theme);
                DrawTexture(spriteBatch, _skillPanelTexture, ResultUILayout.SkillPanel.PanelPosition);
                DrawModelText(spriteBatch, model);

                if (model.NewRecord)
                    DrawTexture(spriteBatch, _newRecordTexture, ResolveNewRecordPosition(theme));
            }
            finally
            {
                RestorePanelTransparency();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            ReleaseLoadedTextures();
            _disposed = true;
        }

        private ITexture? LoadTextureIfAvailable(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !ResourceExists(path))
                return null;

            try
            {
                var texture = _resources.LoadTexture(path);
                if (texture != null)
                    _loadedTextures.Add(texture);

                return texture;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"ResultScreenRenderer: Failed to load texture '{path}': {ex.Message}");
                return null;
            }
        }

        private bool ResourceExists(string path)
        {
            try
            {
                return _resources.ResourceExists(path);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"ResultScreenRenderer: Failed to check texture '{path}': {ex.Message}");
                return false;
            }
        }

        private void ReleaseLoadedTextures()
        {
            // Copy and clear immediately so the list is always reset even if
            // RemoveReference throws during iteration.
            var textures = new List<ITexture>(_loadedTextures);
            _loadedTextures.Clear();
            _backgroundTexture = null;
            _rankBackgroundTexture = null;
            _rankTexture = null;
            _plateTexture = null;
            _jacketPanelTexture = null;
            _skillPanelTexture = null;
            _previewTexture = null;
            _newRecordTexture = null;

            foreach (var texture in textures)
            {
                texture.RemoveReference();
            }
        }

        [ExcludeFromCodeCoverage]
        private static void DrawTexture(SpriteBatch spriteBatch, ITexture? texture, Vector2 position)
        {
            texture?.Draw(spriteBatch, position);
        }

        /// <summary>
        /// Applies panel reveal transparency to all panel textures.
        /// Stored original values are kept for restoration via <see cref="RestorePanelTransparency"/>.
        /// </summary>
        private void ApplyPanelTransparency(int transparency)
        {
            _panelOriginalTransparency = new List<(ITexture texture, int original)>();

            foreach (var texture in new[] { _plateTexture, _jacketPanelTexture, _previewTexture, _skillPanelTexture, _newRecordTexture })
            {
                if (texture != null)
                {
                    _panelOriginalTransparency.Add((texture, texture.Transparency));
                    texture.Transparency = (int)Math.Round(texture.Transparency * (transparency / 255.0));
                }
            }
        }

        private void RestorePanelTransparency()
        {
            if (_panelOriginalTransparency == null)
                return;

            foreach (var (texture, original) in _panelOriginalTransparency)
            {
                if (!texture.IsDisposed)
                    texture.Transparency = original;
            }

            _panelOriginalTransparency.Clear();
        }

        [ExcludeFromCodeCoverage]
        private void DrawRank(SpriteBatch spriteBatch, ResultRevealState reveal)
        {
            if (_rankTexture == null || reveal.RankProgress <= 0.0f)
                return;

            var visibleHeight = Math.Clamp(
                (int)Math.Round(_rankTexture.Height * reveal.RankProgress),
                0,
                _rankTexture.Height);

            if (visibleHeight <= 0)
                return;

            var badgePosition = ResolveRankBadgePosition(_resources.CurrentTheme ?? SkinTheme.Empty);
            var source = new Rectangle(
                0,
                0,
                _rankTexture.Width,
                visibleHeight);
            var destination = new Rectangle(
                (int)badgePosition.X,
                (int)badgePosition.Y + (_rankTexture.Height - visibleHeight),
                _rankTexture.Width,
                visibleHeight);

            _rankTexture.Draw(
                spriteBatch,
                destination,
                source,
                Color.White,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                0f);
        }

        [ExcludeFromCodeCoverage]
        private void DrawPreview(SpriteBatch spriteBatch, ISkinTheme theme)
        {
            var previewPosition = ResolveJacketPreviewPosition(theme);
            var destination = new Rectangle(
                (int)previewPosition.X,
                (int)previewPosition.Y,
                ResultUILayout.Jacket.PreviewDestination.Width,
                ResultUILayout.Jacket.PreviewDestination.Height);

            _previewTexture?.Draw(
                spriteBatch,
                destination,
                null,
                Color.White,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                0f);
        }

        /// <summary>
        /// Song title position on the result screen: NX-absolute (500, 630) by
        /// default; the Y crosses the CX Neon jacket panel's bottom border, so a
        /// skin may shift it via the "Result.TitleY" layout key.
        /// </summary>
        internal static Vector2 ResolveTitlePosition(ISkinTheme theme) => new(
            ResultUILayout.SongInfo.TitlePosition.X,
            theme.GetInt("Result.TitleY", (int)ResultUILayout.SongInfo.TitlePosition.Y));

        /// <summary>
        /// Artist position under the title: "Result.ArtistY" → NX (500, 665).
        /// </summary>
        internal static Vector2 ResolveArtistPosition(ISkinTheme theme) => new(
            ResultUILayout.SongInfo.ArtistPosition.X,
            theme.GetInt("Result.ArtistY", (int)ResultUILayout.SongInfo.ArtistPosition.Y));

        /// <summary>
        /// Chart level value position: NX draws it at a diagonal slot baked into the
        /// NX skill-panel art; a skin with its own panel layout may relocate it via
        /// "Result.LevelX" / "Result.LevelY".
        /// </summary>
        internal static Vector2 ResolveLevelPosition(ISkinTheme theme) => new(
            theme.GetInt("Result.LevelX", (int)ResultUILayout.SkillPanel.LevelPosition.X),
            theme.GetInt("Result.LevelY", (int)ResultUILayout.SkillPanel.LevelPosition.Y));

        /// <summary>
        /// Playing-skill value position: "Result.PlayingSkillX" / "Result.PlayingSkillY"
        /// → NX (238, 537).
        /// </summary>
        internal static Vector2 ResolvePlayingSkillPosition(ISkinTheme theme) => new(
            theme.GetInt("Result.PlayingSkillX", (int)ResultUILayout.SkillPanel.PlayingSkillPosition.X),
            theme.GetInt("Result.PlayingSkillY", (int)ResultUILayout.SkillPanel.PlayingSkillPosition.Y));

        /// <summary>
        /// FAILED text position: NX places it over the (undrawn) plate slot at (420, 156),
        /// which collides with a skin's rank badge art; "Result.FailedTextX" /
        /// "Result.FailedTextY" relocate it.
        /// </summary>
        internal static Vector2 ResolveFailedTextPosition(ISkinTheme theme) => new(
            theme.GetInt("Result.FailedTextX", (int)ResultUILayout.ResultPlate.FailedTextPosition.X),
            theme.GetInt("Result.FailedTextY", (int)ResultUILayout.ResultPlate.FailedTextPosition.Y));

        /// <summary>
        /// Score text position: "Result.ScoreX" / "Result.ScoreY" → NX (30, 58).
        /// </summary>
        internal static Vector2 ResolveScorePosition(ISkinTheme theme) => new(
            theme.GetInt("Result.ScoreX", (int)ResultUILayout.Score.Position.X),
            theme.GetInt("Result.ScoreY", (int)ResultUILayout.Score.Position.Y));

        /// <summary>
        /// Rank badge position: "Result.RankBadgeX" / "Result.RankBadgeY" → NX (480, 0).
        /// The clear plate at NX (315, 100) overlaps a 274px badge there, so skins with
        /// large badge art can move the badge out of the plate slot.
        /// </summary>
        internal static Vector2 ResolveRankBadgePosition(ISkinTheme theme) => new(
            theme.GetInt("Result.RankBadgeX", (int)ResultUILayout.Rank.BadgePosition.X),
            theme.GetInt("Result.RankBadgeY", (int)ResultUILayout.Rank.BadgePosition.Y));

        /// <summary>
        /// Clear plate position: "Result.PlateX" / "Result.PlateY" → NX (315, 100).
        /// </summary>
        internal static Vector2 ResolvePlatePosition(ISkinTheme theme) => new(
            theme.GetInt("Result.PlateX", (int)ResultUILayout.ResultPlate.Position.X),
            theme.GetInt("Result.PlateY", (int)ResultUILayout.ResultPlate.Position.Y));

        /// <summary>
        /// Point size for the large result font (score / FAILED): "Result.FontLarge"
        /// → NX 32. NX draws the score alone at the top-left where 32pt reads fine;
        /// a skin that folds the score into a stats panel can scale it down.
        /// </summary>
        internal static int ResolveLargeFontSize(ISkinTheme theme) =>
            theme.GetInt("Result.FontLarge", ResultUILayout.Fonts.Large);

        /// <summary>
        /// Point size for the normal result font (title / level / skills):
        /// "Result.FontNormal" → NX 20. NotoSerifJP renders 20pt glyphs ~23px tall
        /// against the panel's ~12px baked labels; a skin can step values down to
        /// keep its type scale even.
        /// </summary>
        internal static int ResolveNormalFontSize(ISkinTheme theme) =>
            theme.GetInt("Result.FontNormal", ResultUILayout.Fonts.Normal);

        /// <summary>
        /// Game skill value position: "Result.GameSkillX" / "Result.GameSkillY"
        /// → NX (268, 623).
        /// </summary>
        internal static Vector2 ResolveGameSkillPosition(ISkinTheme theme) => new(
            theme.GetInt("Result.GameSkillX", (int)ResultUILayout.SkillPanel.GameSkillPosition.X),
            theme.GetInt("Result.GameSkillY", (int)ResultUILayout.SkillPanel.GameSkillPosition.Y));

        /// <summary>
        /// Jacket panel position: "Result.JacketPanelX" / "Result.JacketPanelY"
        /// → NX (467, 287). A skin can align its top edge with the skill panel's.
        /// </summary>
        internal static Vector2 ResolveJacketPanelPosition(ISkinTheme theme) => new(
            theme.GetInt("Result.JacketPanelX", (int)ResultUILayout.Jacket.PanelPosition.X),
            theme.GetInt("Result.JacketPanelY", (int)ResultUILayout.Jacket.PanelPosition.Y));

        /// <summary>
        /// Jacket preview position (the NX 245x245 size is kept):
        /// "Result.JacketPreviewX" / "Result.JacketPreviewY" → NX (519, 338).
        /// When preview overrides are absent, falls back to the resolved jacket
        /// panel position plus the NX panel→preview offset so the preview stays
        /// aligned when only the panel is relocated.
        /// </summary>
        internal static Vector2 ResolveJacketPreviewPosition(ISkinTheme theme)
        {
            var panel = ResolveJacketPanelPosition(theme);
            var defaultOffsetX = ResultUILayout.Jacket.PreviewDestination.X
                - (int)ResultUILayout.Jacket.PanelPosition.X;
            var defaultOffsetY = ResultUILayout.Jacket.PreviewDestination.Y
                - (int)ResultUILayout.Jacket.PanelPosition.Y;
            return new Vector2(
                theme.GetInt("Result.JacketPreviewX", (int)panel.X + defaultOffsetX),
                theme.GetInt("Result.JacketPreviewY", (int)panel.Y + defaultOffsetY));
        }

        /// <summary>
        /// NEW RECORD badge position: "Result.NewRecordX" / "Result.NewRecordY"
        /// → NX (298, 582), which lands inside the CX skill panel's LEVEL/PLAY
        /// footer; skins relocate it next to their skill value.
        /// </summary>
        internal static Vector2 ResolveNewRecordPosition(ISkinTheme theme) => new(
            theme.GetInt("Result.NewRecordX", (int)ResultUILayout.NewRecord.BadgePosition.X),
            theme.GetInt("Result.NewRecordY", (int)ResultUILayout.NewRecord.BadgePosition.Y));

        /// <summary>
        /// Horizontal centering axis for the song title: "Result.TitleCenterX",
        /// 0 = disabled (NX left-aligned). Centering keeps the caption on the same
        /// axis as the jacket regardless of the title's length.
        /// </summary>
        internal static int ResolveTitleCenterX(ISkinTheme theme) =>
            theme.GetInt("Result.TitleCenterX", 0);

        /// <summary>
        /// Horizontal centering axis for the artist: "Result.ArtistCenterX",
        /// 0 = disabled (NX left-aligned).
        /// </summary>
        internal static int ResolveArtistCenterX(ISkinTheme theme) =>
            theme.GetInt("Result.ArtistCenterX", 0);

        /// <summary>
        /// Left-aligned x unless a centering axis is set, in which case the text
        /// centers on that axis.
        /// </summary>
        internal static float ApplyCenterX(float x, int centerX, float textWidth) =>
            centerX > 0 ? centerX - textWidth / 2f : x;

        /// <summary>
        /// Draw scale for normal-font texts (title / level / skills):
        /// "Result.FontNormalScale" percent → NX 100. NotoSerifJP SpriteFonts are
        /// baked only at 14/24/48px, so intermediate optical sizes come from
        /// drawing the 24px asset scaled down (e.g. 75 → ~18px glyphs).
        /// </summary>
        internal static float ResolveNormalFontScale(ISkinTheme theme) =>
            theme.GetInt("Result.FontNormalScale", 100) / 100f;

        /// <summary>
        /// Draw scale for the score value only (FAILED keeps the full-size large
        /// font as banner text): "Result.ScoreScale" percent → NX 100.
        /// </summary>
        internal static float ResolveScoreScale(ISkinTheme theme) =>
            theme.GetInt("Result.ScoreScale", 100) / 100f;

        /// <summary>
        /// Score value color: "Result.ScoreText" → NX white. The bold NotoSerifJP
        /// asset only exists at 14px, so a skin marks its score header with an
        /// accent color instead of weight.
        /// </summary>
        internal static Color ResolveScoreColor(ISkinTheme theme) =>
            theme.GetColor("Result.ScoreText", Color.White);

        /// <summary>
        /// Right edge for score/level/play/skill values: "Result.ValueRightX",
        /// 0 = disabled (NX left-aligned). A shared right edge turns the value
        /// column into a ledger against the panel's left-aligned labels.
        /// </summary>
        internal static int ResolveValueRightX(ISkinTheme theme) =>
            theme.GetInt("Result.ValueRightX", 0);

        /// <summary>
        /// Right edge for judgement counts: "Result.CountRightX", 0 = disabled.
        /// </summary>
        internal static int ResolveCountRightX(ISkinTheme theme) =>
            theme.GetInt("Result.CountRightX", 0);

        /// <summary>
        /// Right edge for judgement percentages: "Result.PercentRightX", 0 = disabled.
        /// </summary>
        internal static int ResolvePercentRightX(ISkinTheme theme) =>
            theme.GetInt("Result.PercentRightX", 0);

        /// <summary>
        /// Left-aligned x unless a right edge is set, in which case the text's
        /// right edge anchors to it.
        /// </summary>
        internal static float ApplyRightX(float x, int rightX, float textWidth) =>
            rightX > 0 ? rightX - textWidth : x;

        /// <summary>
        /// Judgement count color: "Result.{judgement}Text" (Perfect/Great/Good/
        /// Poor/Miss/Combo) → NX white. Skins can tint each count to match its
        /// baked row label.
        /// </summary>
        internal static Color ResolveCountColor(ISkinTheme theme, string judgement) =>
            theme.GetColor($"Result.{judgement}Text", Color.White);

        /// <summary>
        /// Judgement percentage color: "Result.PercentText" → NX white. Skins
        /// can dim the derived percentages against the primary counts.
        /// </summary>
        internal static Color ResolvePercentColor(ISkinTheme theme) =>
            theme.GetColor("Result.PercentText", Color.White);

        /// <summary>
        /// Colour for the numeric values that are not judgement counts (level,
        /// play %, skill). NX draws them plain white; skins with their own
        /// neutral ramp keep them on the same step as the rest of the screen.
        /// </summary>
        internal static Color ResolveValueColor(ISkinTheme theme) =>
            theme.GetColor("Result.ValueText", Color.White);

        /// <summary>
        /// Song title color: "Result.TitleText" → NX white.
        /// </summary>
        internal static Color ResolveTitleColor(ISkinTheme theme) =>
            theme.GetColor("Result.TitleText", Color.White);

        /// <summary>
        /// Artist color: "Result.ArtistText" → NX light gray.
        /// </summary>
        internal static Color ResolveArtistColor(ISkinTheme theme) =>
            theme.GetColor("Result.ArtistText", Color.LightGray);

        /// <summary>
        /// Font family for numeric values (score/counts/percents/level/skills):
        /// "Result.FontValueFamily", empty = disabled (NX draws every text with
        /// NotoSerifJP). Values are ASCII, so skins can render them with a
        /// Latin display family while titles keep the CJK-capable serif.
        /// </summary>
        internal static string ResolveValueFontFamily(ISkinTheme theme) =>
            theme.GetString("Result.FontValueFamily", string.Empty);

        /// <summary>
        /// Point size for the value font (score/level/play/skill): "Result.FontValueSize" → 18.
        /// Only consulted when "Result.FontValueFamily" is set.
        /// </summary>
        internal static int ResolveValueFontSize(ISkinTheme theme) =>
            theme.GetInt("Result.FontValueSize", 18);

        /// <summary>
        /// Point size for the count font (judgement counts/percents):
        /// "Result.FontValueSmallSize" → 14. Only consulted when
        /// "Result.FontValueFamily" is set.
        /// </summary>
        internal static int ResolveCountFontSize(ISkinTheme theme) =>
            theme.GetInt("Result.FontValueSmallSize", 14);

        /// <summary>
        /// Y offset added to every judgement count/percent row:
        /// "Result.CountYOffset" → 0. Compensates for a value font whose glyph
        /// metrics differ from NotoSerifJP's without 12 per-row keys.
        /// </summary>
        internal static int ResolveCountYOffset(ISkinTheme theme) =>
            theme.GetInt("Result.CountYOffset", 0);

        /// <summary>
        /// Right edge for the difficulty-tier name ("BASIC", "MASTER", ...) on
        /// the level row: "Result.LevelLabelRightX" → 0 = not drawn (NX shows
        /// only the numeric level).
        /// </summary>
        internal static int ResolveLevelLabelRightX(ISkinTheme theme) =>
            theme.GetInt("Result.LevelLabelRightX", 0);

        /// <summary>
        /// Color for the difficulty-tier name: "Result.LevelLabelText" → light gray.
        /// </summary>
        internal static Color ResolveLevelLabelColor(ISkinTheme theme) =>
            theme.GetColor("Result.LevelLabelText", Color.LightGray);

        [ExcludeFromCodeCoverage]
        private void DrawModelText(SpriteBatch spriteBatch, ResultScreenModel model)
        {
            var theme = _resources.CurrentTheme ?? SkinTheme.Empty;
            var normalScale = ResolveNormalFontScale(theme);

            // Numeric texts prefer the skin's value/count fonts (Latin display
            // family baked at target size, drawn unscaled); without them the
            // NX fonts and scales apply unchanged.
            var valueFont = _valueFont ?? _normalFont;
            var valueScale = _valueFont != null ? 1.0f : normalScale;
            var scoreFont = _valueFont ?? _largeFont;
            var scoreScale = _valueFont != null ? 1.0f : ResolveScoreScale(theme);
            var countFont = _countFont ?? _smallFont;
            var valueRightX = ResolveValueRightX(theme);
            var countRightX = ResolveCountRightX(theme);
            var percentRightX = ResolvePercentRightX(theme);
            var countYOffset = ResolveCountYOffset(theme);
            var percentColor = ResolvePercentColor(theme);

            DrawValue(spriteBatch, scoreFont, model.ScoreText, ResolveScorePosition(theme),
                valueRightX, ResolveScoreColor(theme), scoreScale);

            var titlePosition = ResolveTitlePosition(theme);
            if (_normalFont != null && !string.IsNullOrEmpty(model.Title))
                titlePosition.X = ApplyCenterX(
                    titlePosition.X, ResolveTitleCenterX(theme),
                    _normalFont.MeasureString(model.Title).X * normalScale);
            DrawText(spriteBatch, _normalFont, model.Title, titlePosition, ResolveTitleColor(theme), normalScale);

            var artistPosition = ResolveArtistPosition(theme);
            if (_smallFont != null && !string.IsNullOrEmpty(model.Artist))
                artistPosition.X = ApplyCenterX(
                    artistPosition.X, ResolveArtistCenterX(theme), _smallFont.MeasureString(model.Artist).X);
            DrawText(spriteBatch, _smallFont, model.Artist, artistPosition, ResolveArtistColor(theme));

            DrawValue(spriteBatch, valueFont, model.ChartLevelText, ResolveLevelPosition(theme),
                valueRightX, ResolveValueColor(theme), valueScale);

            // Difficulty-tier name ("BASIC"/"MASTER"/...) beside the level value,
            // in the count font so it reads as an annotation of the number.
            var levelLabelRightX = ResolveLevelLabelRightX(theme);
            if (levelLabelRightX > 0 && model.DifficultyLabelText.Length > 0)
            {
                var labelPosition = ResolveLevelPosition(theme);
                labelPosition.Y += countYOffset;
                DrawValue(spriteBatch, countFont, model.DifficultyLabelText, labelPosition,
                    levelLabelRightX, ResolveLevelLabelColor(theme), 1.0f);
            }
            DrawValue(spriteBatch, valueFont, model.PlayingSkillText, ResolvePlayingSkillPosition(theme),
                valueRightX, ResolveValueColor(theme), valueScale);
            DrawValue(spriteBatch, valueFont, model.GameSkillText, ResolveGameSkillPosition(theme),
                valueRightX, ResolveValueColor(theme), valueScale);

            var countRows = new[]
            {
                (count: model.PerfectCountText, countPos: ResultUILayout.SkillPanel.PerfectCountPosition,
                 percent: model.PerfectPercentText, percentPos: ResultUILayout.SkillPanel.PerfectPercentPosition,
                 judgement: "Perfect"),
                (count: model.GreatCountText, countPos: ResultUILayout.SkillPanel.GreatCountPosition,
                 percent: model.GreatPercentText, percentPos: ResultUILayout.SkillPanel.GreatPercentPosition,
                 judgement: "Great"),
                (count: model.GoodCountText, countPos: ResultUILayout.SkillPanel.GoodCountPosition,
                 percent: model.GoodPercentText, percentPos: ResultUILayout.SkillPanel.GoodPercentPosition,
                 judgement: "Good"),
                (count: model.PoorCountText, countPos: ResultUILayout.SkillPanel.PoorCountPosition,
                 percent: model.PoorPercentText, percentPos: ResultUILayout.SkillPanel.PoorPercentPosition,
                 judgement: "Poor"),
                (count: model.MissCountText, countPos: ResultUILayout.SkillPanel.MissCountPosition,
                 percent: model.MissPercentText, percentPos: ResultUILayout.SkillPanel.MissPercentPosition,
                 judgement: "Miss"),
                (count: model.MaxComboText, countPos: ResultUILayout.SkillPanel.MaxComboCountPosition,
                 percent: model.MaxComboPercentText, percentPos: ResultUILayout.SkillPanel.MaxComboPercentPosition,
                 judgement: "Combo"),
            };

            foreach (var row in countRows)
            {
                DrawValue(spriteBatch, countFont, row.count,
                    new Vector2(row.countPos.X, row.countPos.Y + countYOffset),
                    countRightX, ResolveCountColor(theme, row.judgement), 1.0f);
                DrawValue(spriteBatch, countFont, row.percent,
                    new Vector2(row.percentPos.X, row.percentPos.Y + countYOffset),
                    percentRightX, percentColor, 1.0f);
            }
        }

        /// <summary>
        /// Draws a value text left-aligned at <paramref name="position"/>, or
        /// with its right edge anchored to <paramref name="rightX"/> when the
        /// skin sets one.
        /// </summary>
        [ExcludeFromCodeCoverage]
        private static void DrawValue(SpriteBatch spriteBatch, IFont? font, string text,
            Vector2 position, int rightX, Color color, float scale)
        {
            if (font == null || string.IsNullOrEmpty(text))
                return;

            position.X = ApplyRightX(position.X, rightX, font.MeasureString(text).X * scale);
            DrawText(spriteBatch, font, text, position, color, scale);
        }

        [ExcludeFromCodeCoverage]
        private static void DrawText(SpriteBatch spriteBatch, IFont? font, string text, Vector2 position, Color color)
        {
            if (font == null || string.IsNullOrEmpty(text))
                return;

            font.DrawString(spriteBatch, text, position, color);
        }

        [ExcludeFromCodeCoverage]
        private static void DrawText(SpriteBatch spriteBatch, IFont? font, string text, Vector2 position, Color color, float scale)
        {
            if (font == null || string.IsNullOrEmpty(text))
                return;

            if (scale == 1.0f)
            {
                font.DrawString(spriteBatch, text, position, color);
                return;
            }

            font.DrawString(spriteBatch, text, position, color,
                0f, Vector2.Zero, new Vector2(scale, scale), SpriteEffects.None, 0f);
        }

        private static string GetRankTexturePath(ResultRank rank)
        {
            return rank switch
            {
                ResultRank.SS => TexturePath.ResultRankSS,
                ResultRank.S => TexturePath.ResultRankS,
                ResultRank.A => TexturePath.ResultRankA,
                ResultRank.B => TexturePath.ResultRankB,
                ResultRank.C => TexturePath.ResultRankC,
                ResultRank.D => TexturePath.ResultRankD,
                ResultRank.E => TexturePath.ResultRankE,
                _ => TexturePath.ResultRankE
            };
        }

        private static string GetRankBackgroundPath(ResultRank rank)
        {
            return rank switch
            {
                ResultRank.SS => TexturePath.ResultBackgroundRankSS,
                ResultRank.S => TexturePath.ResultBackgroundRankS,
                ResultRank.A => TexturePath.ResultBackgroundRankA,
                ResultRank.B => TexturePath.ResultBackgroundRankB,
                ResultRank.C => TexturePath.ResultBackgroundRankC,
                ResultRank.D => TexturePath.ResultBackgroundRankD,
                ResultRank.E => TexturePath.ResultBackgroundRankE,
                _ => TexturePath.ResultBackgroundRankE
            };
        }

        private static string GetPlateTexturePath(ResultPlateKind plateKind)
        {
            return plateKind switch
            {
                ResultPlateKind.Excellent => TexturePath.ResultPlateExcellent,
                ResultPlateKind.FullCombo => TexturePath.ResultPlateFullCombo,
                ResultPlateKind.StageCleared => TexturePath.ResultPlateStageCleared,
                _ => TexturePath.ResultPlateStageCleared
            };
        }
    }
}
