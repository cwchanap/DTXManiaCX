#nullable enable

using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Stage.Performance
{
    public sealed class SpriteJudgementTextPopup
    {
        private double _elapsedSeconds;

        public SpriteJudgementTextPopup(JudgementType judgementType, Rectangle sourceRectangle, Vector2 position)
        {
            JudgementType = judgementType;
            SourceRectangle = sourceRectangle;
            Position = position;
            Alpha = 1f;
            Scale = PerformanceUILayout.SpriteJudgementTextAssets.InitialScale;
            IsActive = true;
        }

        public JudgementType JudgementType { get; }
        public Rectangle SourceRectangle { get; }
        public Vector2 Position { get; }
        public float Alpha { get; private set; }
        public float Scale { get; private set; }
        public bool IsActive { get; private set; }

        public bool Update(double deltaTime)
        {
            if (!IsActive)
                return false;

            _elapsedSeconds += Math.Max(0.0, deltaTime);
            var totalDuration = PerformanceUILayout.SpriteJudgementTextAssets.TotalDurationSeconds;
            if (_elapsedSeconds >= totalDuration)
            {
                Alpha = 0f;
                Scale = PerformanceUILayout.SpriteJudgementTextAssets.SettledScale;
                IsActive = false;
                return false;
            }

            var popDuration = PerformanceUILayout.SpriteJudgementTextAssets.PopDurationSeconds;
            if (_elapsedSeconds < popDuration)
            {
                var popProgress = (float)(_elapsedSeconds / popDuration);
                Scale = MathHelper.Lerp(
                    PerformanceUILayout.SpriteJudgementTextAssets.InitialScale,
                    PerformanceUILayout.SpriteJudgementTextAssets.SettledScale,
                    popProgress);
            }
            else
            {
                Scale = PerformanceUILayout.SpriteJudgementTextAssets.SettledScale;
            }

            var fadeStart = popDuration;
            var fadeProgress = (float)((_elapsedSeconds - fadeStart) / (totalDuration - fadeStart));
            Alpha = MathHelper.Clamp(1f - fadeProgress, 0f, 1f);
            return true;
        }
    }

    public sealed class SpriteJudgementTextPopupManager : IDisposable
    {
        private readonly List<SpriteJudgementTextPopup> _activePopups;
        private readonly Action<JudgementEvent>? _fontFallback;
        private ITexture? _spriteTexture;
        private bool _disposed;

        public SpriteJudgementTextPopupManager(IResourceManager resourceManager, Action<JudgementEvent>? fontFallback = null)
            : this(LoadSpriteTexture(resourceManager), fontFallback, new List<SpriteJudgementTextPopup>())
        {
        }

        private SpriteJudgementTextPopupManager(
            ITexture? spriteTexture,
            Action<JudgementEvent>? fontFallback,
            List<SpriteJudgementTextPopup> activePopups)
        {
            _spriteTexture = spriteTexture;
            _fontFallback = fontFallback;
            _activePopups = activePopups;
        }

        internal static SpriteJudgementTextPopupManager CreateForTesting(
            ITexture? spriteTexture,
            Action<JudgementEvent>? fontFallback = null,
            List<SpriteJudgementTextPopup>? activePopups = null)
        {
            return new SpriteJudgementTextPopupManager(
                spriteTexture,
                fontFallback,
                activePopups ?? new List<SpriteJudgementTextPopup>());
        }

        internal IReadOnlyList<SpriteJudgementTextPopup> ActivePopupsForTesting => _activePopups;

        public int ActivePopupCount => _activePopups.Count;

        public void SpawnPopup(JudgementEvent judgementEvent)
        {
            if (_disposed || judgementEvent == null)
                return;

            if (_spriteTexture == null)
            {
                _fontFallback?.Invoke(judgementEvent);
                return;
            }

            Rectangle source;
            try
            {
                source = PerformanceUILayout.SpriteJudgementTextAssets.GetJudgementSource(judgementEvent.Type);
            }
            catch (ArgumentOutOfRangeException)
            {
                return;
            }

            var position = PerformanceUILayout.SpriteJudgementTextAssets.GetLaneTextPosition(judgementEvent.Lane, source);
            _activePopups.Add(new SpriteJudgementTextPopup(judgementEvent.Type, source, position));
        }

        public void Update(double deltaTime)
        {
            if (_disposed)
                return;

            for (int i = _activePopups.Count - 1; i >= 0; i--)
            {
                if (!_activePopups[i].Update(deltaTime))
                {
                    _activePopups.RemoveAt(i);
                }
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (_disposed || spriteBatch == null || _spriteTexture?.Texture == null)
                return;

            foreach (var popup in _activePopups)
            {
                if (!popup.IsActive || popup.Alpha <= 0f)
                    continue;

                var source = popup.SourceRectangle;
                var width = Math.Max(1, (int)MathF.Round(source.Width * popup.Scale));
                var height = Math.Max(1, (int)MathF.Round(source.Height * popup.Scale));
                var dest = new Rectangle(
                    (int)MathF.Round(popup.Position.X - (width - source.Width) / 2f),
                    (int)MathF.Round(popup.Position.Y - (height - source.Height) / 2f),
                    width,
                    height);

                _spriteTexture.Draw(
                    spriteBatch,
                    dest,
                    source,
                    Color.White * popup.Alpha,
                    0f,
                    Vector2.Zero,
                    SpriteEffects.None,
                    0.5f);
            }
        }

        public void ClearAll()
        {
            _activePopups.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _activePopups.Clear();
            _spriteTexture?.RemoveReference();
            _spriteTexture = null;
            _disposed = true;
        }

        private static ITexture? LoadSpriteTexture(IResourceManager resourceManager)
        {
            ArgumentNullException.ThrowIfNull(resourceManager);

            try
            {
                if (!resourceManager.ResourceExists(TexturePath.JudgeStringsXg))
                    return null;

                var texture = resourceManager.LoadTexture(TexturePath.JudgeStringsXg);
                if (texture.Width < 242 || texture.Height < 169)
                {
                    texture.RemoveReference();
                    return null;
                }

                return texture;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"SpriteJudgementTextPopupManager: {ex.GetType().Name} loading {TexturePath.JudgeStringsXg}: {ex.Message}");
                return null;
            }
        }
    }
}
