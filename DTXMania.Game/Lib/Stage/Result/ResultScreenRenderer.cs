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
            IFont? largeFont)
        {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _smallFont = smallFont;
            _normalFont = normalFont;
            _largeFont = largeFont;
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
                DrawTexture(spriteBatch, _plateTexture, ResultUILayout.ResultPlate.Position);
                if (model.PlateKind == ResultPlateKind.Failed)
                    DrawText(spriteBatch, _largeFont, "FAILED", ResultUILayout.ResultPlate.FailedTextPosition, Color.Red);

                DrawTexture(spriteBatch, _jacketPanelTexture, ResultUILayout.Jacket.PanelPosition);
                DrawPreview(spriteBatch);
                DrawTexture(spriteBatch, _skillPanelTexture, ResultUILayout.SkillPanel.PanelPosition);
                DrawModelText(spriteBatch, model);

                if (model.NewRecord)
                    DrawTexture(spriteBatch, _newRecordTexture, ResultUILayout.NewRecord.BadgePosition);
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

            var source = new Rectangle(
                0,
                0,
                _rankTexture.Width,
                visibleHeight);
            var destination = new Rectangle(
                (int)ResultUILayout.Rank.BadgePosition.X,
                (int)ResultUILayout.Rank.BadgePosition.Y + (_rankTexture.Height - visibleHeight),
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
        private void DrawPreview(SpriteBatch spriteBatch)
        {
            _previewTexture?.Draw(
                spriteBatch,
                ResultUILayout.Jacket.PreviewDestination,
                null,
                Color.White,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                0f);
        }

        [ExcludeFromCodeCoverage]
        private void DrawModelText(SpriteBatch spriteBatch, ResultScreenModel model)
        {
            DrawText(spriteBatch, _largeFont, model.ScoreText, ResultUILayout.Score.Position, Color.White);
            DrawText(spriteBatch, _normalFont, model.Title, ResultUILayout.SongInfo.TitlePosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.Artist, ResultUILayout.SongInfo.ArtistPosition, Color.LightGray);
            DrawText(spriteBatch, _normalFont, model.ChartLevelText, ResultUILayout.SkillPanel.LevelPosition, Color.White);
            DrawText(spriteBatch, _normalFont, model.PlayingSkillText, ResultUILayout.SkillPanel.PlayingSkillPosition, Color.White);
            DrawText(spriteBatch, _normalFont, model.GameSkillText, ResultUILayout.SkillPanel.GameSkillPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.PerfectCountText, ResultUILayout.SkillPanel.PerfectCountPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.GreatCountText, ResultUILayout.SkillPanel.GreatCountPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.GoodCountText, ResultUILayout.SkillPanel.GoodCountPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.PoorCountText, ResultUILayout.SkillPanel.PoorCountPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.MissCountText, ResultUILayout.SkillPanel.MissCountPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.MaxComboText, ResultUILayout.SkillPanel.MaxComboCountPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.PerfectPercentText, ResultUILayout.SkillPanel.PerfectPercentPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.GreatPercentText, ResultUILayout.SkillPanel.GreatPercentPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.GoodPercentText, ResultUILayout.SkillPanel.GoodPercentPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.PoorPercentText, ResultUILayout.SkillPanel.PoorPercentPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.MissPercentText, ResultUILayout.SkillPanel.MissPercentPosition, Color.White);
            DrawText(spriteBatch, _smallFont, model.MaxComboPercentText, ResultUILayout.SkillPanel.MaxComboPercentPosition, Color.White);
        }

        [ExcludeFromCodeCoverage]
        private static void DrawText(SpriteBatch spriteBatch, IFont? font, string text, Vector2 position, Color color)
        {
            if (font == null || string.IsNullOrEmpty(text))
                return;

            font.DrawString(spriteBatch, text, position, color);
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
