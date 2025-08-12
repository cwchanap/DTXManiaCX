using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Resources;

namespace DTXMania.Game.Lib.Stage.Performance
{
    public class EffectsManager
    {
        private List<EffectInstance> _activeEffects;
        private ManagedSpriteTexture _hitEffectTexture;
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
                var texture = resourceManager.LoadTexture("Graphics/hit_fx.png");
                
                if (texture?.Texture == null)
                    throw new ArgumentException("Failed to load hit effect texture - texture is null", nameof(resourceManager));
                _hitEffectTexture = new ManagedSpriteTexture(graphicsDevice, texture.Texture, "Graphics/hit_fx.png", FrameWidth, FrameHeight);
                
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
                System.Diagnostics.Debug.WriteLine($"[EffectsManager] Failed to load hit effect texture: {ex.Message}");
                
                try
                {
                    // Create a fallback texture that matches the expected sprite dimensions
                    int textureWidth = FrameWidth;  // 8 pixels
                    int textureHeight = FrameHeight; // 32 pixels
                    
                    var fallbackTexture = new Texture2D(graphicsDevice, textureWidth, textureHeight);
                    var colorData = new Color[textureWidth * textureHeight];
                    for (int i = 0; i < colorData.Length; i++)
                    {
                        colorData[i] = Color.White;
                    }
                    fallbackTexture.SetData(colorData);
                    
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
                
                // Validate frame index before drawing - this is the critical check
                if (frameIndex >= 0 && frameIndex < _hitEffectTexture.TotalSprites)
                {
                    _hitEffectTexture.DrawSprite(spriteBatch, frameIndex, effect.Position);
                }
                // Don't log errors here to avoid spam, just silently skip invalid frames
            }
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
            _hitEffectTexture?.Dispose();
            _hitEffectTexture = null;
            _activeEffects?.Clear();
        }
    }
}

