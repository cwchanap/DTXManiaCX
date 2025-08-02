using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using DTX.Resources;

namespace DTX.Stage.Performance
{
    public class EffectsManager
    {
        private List<EffectInstance> _activeEffects;
        private ManagedSpriteTexture _hitEffectTexture;
        private const int FrameWidth = 8;
        private const int FrameHeight = 32;
        private const double FrameDuration = 1.0 / 60.0; // 60 fps animation

        public EffectsManager(GraphicsDevice graphicsDevice, ResourceManager resourceManager)
        {
            _activeEffects = new List<EffectInstance>();
            var texture = resourceManager.LoadTexture("Graphics/hit_fx.png");
            _hitEffectTexture = new ManagedSpriteTexture(graphicsDevice, texture.Texture, "Graphics/hit_fx.png", FrameWidth, FrameHeight);
        }

        public void SpawnHitEffect(int lane)
        {
            var position = new Vector2(PerformanceUILayout.GetLaneX(lane), PerformanceUILayout.JudgementLineY);
            _activeEffects.Add(new EffectInstance(position, _hitEffectTexture.TotalSprites));
        }

        public void Update(double deltaTime)
        {
            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                _activeEffects[i].Update(deltaTime);

                if (_activeEffects[i].IsExpired)
                {
                    _activeEffects.RemoveAt(i);
                }
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            foreach (var effect in _activeEffects)
            {
                var frameIndex = effect.FrameIndex;
                _hitEffectTexture.DrawSprite(spriteBatch, frameIndex, effect.Position);
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
                _totalFrames = totalFrames;
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

