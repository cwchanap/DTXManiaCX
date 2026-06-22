using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.Performance
{
    public class EffectsManager
    {
        private List<EffectInstance> _activeEffects;
        private ManagedSpriteTexture _hitEffectTexture;
        private ITexture _cachedTextureSource;
        private bool _effectsEnabled = false;
        private const int FrameWidth = 8;
        private const int FrameHeight = 32;
        private const double FrameDuration = 1.0 / 60.0; // 60 fps animation

        public EffectsManager(GraphicsDevice graphicsDevice, IResourceManager resourceManager)
        {
            if (graphicsDevice == null)
                throw new ArgumentNullException(nameof(graphicsDevice));
            if (resourceManager == null)
                throw new ArgumentNullException(nameof(resourceManager));

            _activeEffects = new List<EffectInstance>();
            _effectsEnabled = false;
            
            try
            {
                var texture = resourceManager.LoadTexture(TexturePath.HitFx);

                if (texture?.Texture == null)
                {
                    // Balance the AddReference() performed inside LoadTexture before
                    // falling through to the fallback path, otherwise the cached
                    // entry leaks a reference count.
                    texture?.RemoveReference();
                    throw new ArgumentException("Failed to load hit effect texture - texture is null", nameof(resourceManager));
                }

                try
                {
                    _hitEffectTexture = new ManagedSpriteTexture(graphicsDevice, texture.Texture, TexturePath.HitFx, FrameWidth, FrameHeight);
                    // Only retain the cache reference once the sprite view is constructed
                    // successfully. The sprite view shares the underlying Texture2D with
                    // the ResourceManager cache, so EffectsManager must NOT dispose it;
                    // it only owns a reference that must be released on Dispose.
                    _cachedTextureSource = texture;
                }
                catch
                {
                    // Sprite-sheet construction failed (e.g. invalid dimensions); release
                    // the cached reference and let the fallback path run.
                    texture.RemoveReference();
                    throw;
                }
                
                // Validate the texture has valid sprites
                if (_hitEffectTexture.TotalSprites <= 0)
                {
                    throw new InvalidOperationException($"Hit effect texture has invalid sprite count: {_hitEffectTexture.TotalSprites}");
                }
                
                _effectsEnabled = true;
                System.Diagnostics.Debug.WriteLine($"[EffectsManager] Loaded hit effect texture with {_hitEffectTexture.TotalSprites} sprites");
            }
            catch (Exception ex)
            {
                // The hit-effect sprite sheet is an optional gameplay visual. If the
                // asset is missing or corrupt (e.g. an incomplete custom skin, or a
                // bad TexturePath.HitFx), degrade gracefully rather than aborting
                // PerformanceStage construction: try to synthesize a minimal fallback
                // texture, and if that also fails, just disable effects. Gameplay is
                // unaffected either way; the failure is surfaced via Debug.WriteLine.
                System.Diagnostics.Debug.WriteLine($"[EffectsManager] Failed to load hit effect texture: {ex.Message}");

                // If we got far enough to retain a cache reference (LoadTexture
                // succeeded but sprite construction/validation failed), release it
                // before entering the fallback path. The fallback synthesizes its
                // own Texture2D and must not carry a dangling cache reference —
                // otherwise Dispose() would later call RemoveReference() on a
                // source we no longer use.
                _cachedTextureSource?.RemoveReference();
                _cachedTextureSource = null;
                _hitEffectTexture = null;

                try
                {
                    // Create a fallback texture that matches the expected sprite dimensions
                    int textureWidth = FrameWidth;  // 8 pixels
                    int textureHeight = FrameHeight; // 32 pixels

                    var fallbackTexture = CreateFallbackTexture(graphicsDevice, textureWidth, textureHeight);

                    // Create sprite texture with exactly 1 sprite of the expected dimensions
                    _hitEffectTexture = new ManagedSpriteTexture(graphicsDevice, fallbackTexture, "fallback", FrameWidth, FrameHeight);

                    // Double-check the fallback worked
                    if (_hitEffectTexture.TotalSprites > 0)
                    {
                        _effectsEnabled = true;
                        System.Diagnostics.Debug.WriteLine($"[EffectsManager] Created fallback texture with {_hitEffectTexture.TotalSprites} sprites");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[EffectsManager] Fallback texture also failed, disabling effects");
                        _hitEffectTexture = null;
                    }
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[EffectsManager] Fallback texture creation failed: {fallbackEx.Message}, disabling effects");
                    _hitEffectTexture = null;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[EffectsManager] Initialized with effects enabled: {_effectsEnabled}");
        }

        /// <summary>
        /// Creates a solid-white fallback texture matching the hit-effect sprite dimensions.
        /// Extracted as a seam so tests can substitute a tracking texture without a live
        /// graphics device; the default implementation is the original behavior.
        /// </summary>
        protected virtual Texture2D CreateFallbackTexture(GraphicsDevice graphicsDevice, int width, int height)
        {
            var fallbackTexture = new Texture2D(graphicsDevice, width, height);
            var colorData = new Color[width * height];
            for (int i = 0; i < colorData.Length; i++)
            {
                colorData[i] = Color.White;
            }
            fallbackTexture.SetData(colorData);
            return fallbackTexture;
        }

        public void SpawnHitEffect(int lane)
        {
            // Don't spawn effects if disabled or texture is invalid
            if (!_effectsEnabled || _hitEffectTexture == null || _hitEffectTexture.TotalSprites <= 0)
            {
                return; // Silently skip if effects are disabled
            }
            
            var position = new Vector2(PerformanceUILayout.GetLaneX(lane), PerformanceUILayout.JudgementLineY);
            _activeEffects.Add(new EffectInstance(position, _hitEffectTexture.TotalSprites));
        }

        public void Update(double deltaTime)
        {
            if (!_effectsEnabled)
                return;
                
            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                _activeEffects[i].Update(deltaTime);

                if (_activeEffects[i].IsExpired)
                {
                    _activeEffects.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Clears all active effects
        /// </summary>
        public void ClearAllEffects()
        {
            _activeEffects.Clear();
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (!_effectsEnabled || _hitEffectTexture == null || _hitEffectTexture.TotalSprites <= 0)
                return;

            foreach (var effect in _activeEffects)
            {
                var frameIndex = effect.FrameIndex;

                if (IsValidFrameIndex(frameIndex, _hitEffectTexture.TotalSprites))
                {
                    _hitEffectTexture.DrawSprite(spriteBatch, frameIndex, effect.Position);
                }
            }
        }

        internal static bool IsValidFrameIndex(int frameIndex, int totalSprites)
        {
            return frameIndex >= 0 && frameIndex < totalSprites;
        }

        private class EffectInstance
        {
            private const double FrameDuration = 1.0 / 60.0; // 60 fps
            private Vector2 _position;
            private double _lifeTime;
            private int _frameIndex;
            private int _totalFrames;

            public EffectInstance(Vector2 position, int totalFrames)
            {
                _position = position;
                _lifeTime = 0;
                _frameIndex = 0;
                
                // Ensure we have at least 1 frame to prevent index errors
                _totalFrames = Math.Max(1, totalFrames);
                
                if (totalFrames <= 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[EffectInstance] Warning: Created with invalid totalFrames {totalFrames}, using 1 instead");
                }
            }

            public Vector2 Position => _position;

            public int FrameIndex => _frameIndex;

            public bool IsExpired => _frameIndex >= _totalFrames;

            public void Update(double deltaTime)
            {
                _lifeTime += deltaTime;
                _frameIndex = (int)(_lifeTime / FrameDuration);
            }
        }

        public void Dispose()
        {
            _effectsEnabled = false;

            if (_cachedTextureSource != null)
            {
                // Texture came from the ResourceManager cache. Release the reference
                // that LoadTexture added, but do NOT dispose the underlying Texture2D —
                // it is shared with the cache and will be reused on the next
                // PerformanceStage activation. Disposing it here would cause the cached
                // entry to point at a disposed Texture2D.
                _cachedTextureSource.RemoveReference();
                _cachedTextureSource = null;
                _hitEffectTexture = null;
            }
            else
            {
                // Fallback path: EffectsManager synthesized its own Texture2D, so it
                // owns the texture and is responsible for disposing it.
                _hitEffectTexture?.Dispose();
                _hitEffectTexture = null;
            }

            _activeEffects?.Clear();
        }
    }
}
