using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.Stage;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// High-performance Effects Manager with object pooling for stress testing
    /// Replaces the original EffectsManager with pooled EffectInstance management
    /// Designed for 100k+ note charts with minimal allocation overhead
    /// </summary>
    public class PooledEffectsManager : IDisposable
    {
        private readonly ConcurrentQueue<PooledEffectInstance> _effectPool;
        private readonly List<PooledEffectInstance> _activeEffects;
        private readonly ManagedSpriteTexture _hitEffectTexture;
        private readonly object _activeLock = new object();
        
        // Performance tracking
        private long _totalRequests;
        private long _poolHits;
        private long _poolMisses;
        
        // Configuration
        private const int InitialPoolSize = 100;
        private const int MaxPoolSize = 500;
        private const int FrameWidth = 8;
        private const int FrameHeight = 32;
        private const double FrameDuration = 1.0 / 60.0; // 60 fps animation

        public PooledEffectsManager(GraphicsDevice graphicsDevice, IResourceManager resourceManager)
        {
            _effectPool = new ConcurrentQueue<PooledEffectInstance>();
            _activeEffects = new List<PooledEffectInstance>();
            
            var texture = resourceManager.LoadTexture("Graphics/hit_fx.png");
            _hitEffectTexture = new ManagedSpriteTexture(graphicsDevice, texture.Texture, "Graphics/hit_fx.png", FrameWidth, FrameHeight);
            
            // Pre-populate the pool
            InitializePool();
        }

        /// <summary>
        /// Pre-populate the effect instance pool to avoid allocations during gameplay
        /// </summary>
        private void InitializePool()
        {
            for (int i = 0; i < InitialPoolSize; i++)
            {
                var instance = new PooledEffectInstance(_hitEffectTexture.TotalSprites);
                _effectPool.Enqueue(instance);
            }
        }

        /// <summary>
        /// Spawns a hit effect using pooled instances
        /// </summary>
        public void SpawnHitEffect(int lane)
        {
            _totalRequests++;
            
            var position = new Vector2(PerformanceUILayout.GetLaneX(lane), PerformanceUILayout.JudgementLineY);
            
            // Try to get an instance from the pool
            if (_effectPool.TryDequeue(out var pooledInstance))
            {
                _poolHits++;
                pooledInstance.Reset(position);
            }
            else
            {
                _poolMisses++;
                // Pool is empty, create new instance if under max pool size
                if (_activeEffects.Count < MaxPoolSize)
                {
                    pooledInstance = new PooledEffectInstance(_hitEffectTexture.TotalSprites);
                    pooledInstance.Reset(position);
                }
                else
                {
                    // Pool exhausted and at max size, skip this effect
                    Debug.WriteLine("PooledEffectsManager: Effect pool exhausted, skipping effect");
                    return;
                }
            }

            lock (_activeLock)
            {
                _activeEffects.Add(pooledInstance);
            }
        }

        /// <summary>
        /// Updates all active effects and returns expired ones to the pool
        /// </summary>
        public void Update(double deltaTime)
        {
            lock (_activeLock)
            {
                for (int i = _activeEffects.Count - 1; i >= 0; i--)
                {
                    var effect = _activeEffects[i];
                    effect.Update(deltaTime);

                    if (effect.IsExpired)
                    {
                        _activeEffects.RemoveAt(i);
                        
                        // Return to pool if there's space
                        if (_effectPool.Count < MaxPoolSize)
                        {
                            _effectPool.Enqueue(effect);
                        }
                        // If pool is full, let the instance be garbage collected
                    }
                }
            }
        }

        /// <summary>
        /// Draws all active effects
        /// </summary>
        public void Draw(SpriteBatch spriteBatch)
        {
            lock (_activeLock)
            {
                foreach (var effect in _activeEffects)
                {
                    var frameIndex = effect.FrameIndex;
                    _hitEffectTexture.DrawSprite(spriteBatch, frameIndex, effect.Position);
                }
            }
        }

        /// <summary>
        /// Gets current pooling statistics for performance analysis
        /// </summary>
        public EffectPoolingStats GetPoolingStats()
        {
            lock (_activeLock)
            {
                return new EffectPoolingStats
                {
                    PoolSize = _effectPool.Count,
                    ActiveInstances = _activeEffects.Count,
                    TotalRequests = _totalRequests,
                    PoolHits = _poolHits,
                    PoolMisses = _poolMisses
                };
            }
        }

        /// <summary>
        /// Resets pooling statistics (useful for benchmarking)
        /// </summary>
        public void ResetStats()
        {
            _totalRequests = 0;
            _poolHits = 0;
            _poolMisses = 0;
        }

        /// <summary>
        /// Clears all active effects and returns them to the pool
        /// </summary>
        public void ClearAllEffects()
        {
            lock (_activeLock)
            {
                foreach (var effect in _activeEffects)
                {
                    if (_effectPool.Count < MaxPoolSize)
                    {
                        _effectPool.Enqueue(effect);
                    }
                }
                _activeEffects.Clear();
            }
        }

        public void Dispose()
        {
            ClearAllEffects();
            _hitEffectTexture?.Dispose();
        }

        /// <summary>
        /// Pooled effect instance that can be reused to minimize allocations
        /// </summary>
        private class PooledEffectInstance
        {
            private const double FrameDuration = 1.0 / 60.0; // 60 fps
            private Vector2 _position;
            private double _lifeTime;
            private int _frameIndex;
            private readonly int _totalFrames;

            public PooledEffectInstance(int totalFrames)
            {
                _totalFrames = totalFrames;
                Reset(Vector2.Zero);
            }

            public Vector2 Position => _position;
            public int FrameIndex => _frameIndex;
            public bool IsExpired => _frameIndex >= _totalFrames;

            /// <summary>
            /// Resets the instance for reuse (instead of creating new objects)
            /// </summary>
            public void Reset(Vector2 position)
            {
                _position = position;
                _lifeTime = 0;
                _frameIndex = 0;
            }

            public void Update(double deltaTime)
            {
                _lifeTime += deltaTime;
                _frameIndex = (int)(_lifeTime / FrameDuration);
            }
        }
    }

    /// <summary>
    /// Statistics for Effect instance pooling performance analysis
    /// </summary>
    public class EffectPoolingStats
    {
        public int PoolSize { get; set; }
        public int ActiveInstances { get; set; }
        public long TotalRequests { get; set; }
        public long PoolHits { get; set; }
        public long PoolMisses { get; set; }
    }
}
