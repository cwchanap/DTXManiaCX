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

        public SpriteJudgementTextPopup(
            JudgementType judgementType,
            Rectangle sourceRectangle,
            Vector2 position,
            JudgementEvent? sourceJudgementEvent = null)
        {
            JudgementType = judgementType;
            SourceRectangle = sourceRectangle;
            Position = position;
            SourceJudgementEvent = sourceJudgementEvent;
            Alpha = 1f;
            Scale = PerformanceUILayout.SpriteJudgementTextAssets.InitialScale;
            IsActive = true;
        }

        public JudgementType JudgementType { get; }
        public Rectangle SourceRectangle { get; }
        public Vector2 Position { get; }
        internal JudgementEvent? SourceJudgementEvent { get; }
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
        private readonly IResourceManager? _resourceManager;
        private ITexture? _spriteTexture;
        private bool _reloadAttempted;
        private bool _disposed;

        public SpriteJudgementTextPopupManager(IResourceManager resourceManager, Action<JudgementEvent>? fontFallback = null)
            : this(LoadSpriteTexture(resourceManager), fontFallback, new List<SpriteJudgementTextPopup>(), resourceManager)
        {
        }

        private SpriteJudgementTextPopupManager(
            ITexture? spriteTexture,
            Action<JudgementEvent>? fontFallback,
            List<SpriteJudgementTextPopup> activePopups,
            IResourceManager? resourceManager = null)
        {
            _spriteTexture = spriteTexture;
            _fontFallback = fontFallback;
            _activePopups = activePopups;
            _resourceManager = resourceManager;
            // If no valid texture was available at construction, don't retry on every call;
            // reload is only attempted after a mid-stage invalidation of a previously valid
            // texture (see TryReloadSpriteTexture).
            _reloadAttempted = spriteTexture == null;
        }

        internal static SpriteJudgementTextPopupManager CreateForTesting(
            ITexture? spriteTexture,
            Action<JudgementEvent>? fontFallback = null,
            List<SpriteJudgementTextPopup>? activePopups = null,
            IResourceManager? resourceManager = null)
        {
            return new SpriteJudgementTextPopupManager(
                spriteTexture,
                fontFallback,
                activePopups ?? new List<SpriteJudgementTextPopup>(),
                resourceManager);
        }

        internal IReadOnlyList<SpriteJudgementTextPopup> ActivePopupsForTesting => _activePopups;

        public int ActivePopupCount => _activePopups.Count;

        public void SpawnPopup(JudgementEvent judgementEvent)
        {
            if (_disposed || judgementEvent == null)
                return;

            if (!TryEnsureSpriteTextureAvailable())
            {
                _fontFallback?.Invoke(judgementEvent);
                return;
            }

            Rectangle source;
            try
            {
                source = PerformanceUILayout.SpriteJudgementTextAssets.GetJudgementSource(judgementEvent.Type);
                var position = PerformanceUILayout.SpriteJudgementTextAssets.GetLaneTextPosition(judgementEvent.Lane, source);
                _activePopups.Add(new SpriteJudgementTextPopup(judgementEvent.Type, source, position, judgementEvent));
            }
            catch (ArgumentOutOfRangeException)
            {
                _fontFallback?.Invoke(judgementEvent);
                return;
            }
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
            if (_disposed)
                return;

            if (!TryEnsureSpriteTextureAvailable())
            {
                MigrateActivePopupsToFontFallback();
                return;
            }

            if (spriteBatch == null)
                return;

            foreach (var popup in _activePopups)
            {
                if (!popup.IsActive || popup.Alpha <= 0f)
                    continue;

                var spriteTexture = _spriteTexture;
                if (spriteTexture == null)
                    return;

                var source = popup.SourceRectangle;
                var width = Math.Max(1, (int)MathF.Round(source.Width * popup.Scale));
                var height = Math.Max(1, (int)MathF.Round(source.Height * popup.Scale));
                var dest = new Rectangle(
                    (int)MathF.Round(popup.Position.X - (width - source.Width) / 2f),
                    (int)MathF.Round(popup.Position.Y - (height - source.Height) / 2f),
                    width,
                    height);

                try
                {
                    spriteTexture.Draw(
                        spriteBatch,
                        dest,
                        source,
                        Color.White * popup.Alpha,
                        0f,
                        Vector2.Zero,
                        SpriteEffects.None,
                        0.5f);
                }
                catch (Exception ex)
                {
                    if (!HandleSpriteDrawFailure(spriteTexture, ex))
                        throw;

                    return;
                }
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
            ReleaseHeldSpriteTexture();
            _disposed = true;
        }

        private static ITexture? LoadSpriteTexture(IResourceManager resourceManager)
        {
            ArgumentNullException.ThrowIfNull(resourceManager);

            ITexture? texture = null;
            try
            {
                if (!resourceManager.ResourceExists(TexturePath.JudgeStringsXg))
                    return null;

                texture = resourceManager.LoadTexture(TexturePath.JudgeStringsXg);
                if (IsInvalidSpriteTexture(texture))
                {
                    var invalidTexture = texture;
                    texture = null;
                    invalidTexture.RemoveReference();
                    return null;
                }

                return texture;
            }
            catch (Exception ex)
            {
                texture?.RemoveReference();
                System.Diagnostics.Debug.WriteLine(
                    $"SpriteJudgementTextPopupManager: {ex.GetType().Name} loading {TexturePath.JudgeStringsXg}: {ex.Message}");
                return null;
            }
        }

        private bool TryEnsureSpriteTextureAvailable()
        {
            var spriteTexture = _spriteTexture;
            if (spriteTexture == null)
                return TryReloadSpriteTexture();

            try
            {
                if (!IsInvalidSpriteTexture(spriteTexture))
                    return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"SpriteJudgementTextPopupManager: {ex.GetType().Name} validating held {TexturePath.JudgeStringsXg}: {ex.Message}");
            }

            ReleaseHeldSpriteTexture();
            return TryReloadSpriteTexture();
        }

        private bool TryReloadSpriteTexture()
        {
            // After mid-stage invalidation (e.g. graphics device reset), attempt to reload
            // the sprite texture once before falling back to the font renderer. Without this
            // the manager would permanently route every judgement to the font fallback.
            // Retry at most once per invalidation episode to avoid loading on every call.
            if (_reloadAttempted || _resourceManager == null || _disposed)
                return false;

            _reloadAttempted = true;
            var reloaded = LoadSpriteTexture(_resourceManager);
            if (reloaded == null)
                return false;

            // A successful reload resets the guard so a future invalidation can retry.
            _reloadAttempted = false;
            _spriteTexture = reloaded;
            return true;
        }

        private void ReleaseHeldSpriteTexture()
        {
            var spriteTexture = _spriteTexture;
            if (spriteTexture == null)
                return;

            _spriteTexture = null;
            try
            {
                spriteTexture.RemoveReference();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"SpriteJudgementTextPopupManager: {ex.GetType().Name} releasing {TexturePath.JudgeStringsXg}: {ex.Message}");
            }
        }

        private void MigrateActivePopupsToFontFallback()
        {
            if (_activePopups.Count == 0)
                return;

            foreach (var popup in _activePopups)
            {
                if (popup.IsActive && popup.SourceJudgementEvent != null)
                    _fontFallback?.Invoke(popup.SourceJudgementEvent);
            }

            _activePopups.Clear();
        }

        private bool HandleSpriteDrawFailure(ITexture spriteTexture, Exception exception)
        {
            var invalid = true;
            try
            {
                invalid = IsInvalidSpriteTexture(spriteTexture);
            }
            catch
            {
            }

            if (!invalid)
                return false;

            System.Diagnostics.Debug.WriteLine(
                $"SpriteJudgementTextPopupManager: {exception.GetType().Name} drawing {TexturePath.JudgeStringsXg}: {exception.Message}");
            ReleaseHeldSpriteTexture();
            MigrateActivePopupsToFontFallback();
            return true;
        }

        private static bool IsInvalidSpriteTexture(ITexture texture)
        {
            if (texture.IsDisposed)
                return true;

            if (texture.Width < PerformanceUILayout.SpriteJudgementTextAssets.RequiredTextureWidth
                || texture.Height < PerformanceUILayout.SpriteJudgementTextAssets.RequiredTextureHeight)
                return true;

            var spriteTexture = texture.Texture;
            return spriteTexture == null || spriteTexture.IsDisposed;
        }
    }
}
