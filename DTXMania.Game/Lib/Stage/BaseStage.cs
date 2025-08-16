using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game;
using DTXMania.Game.Lib.Resources;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Abstract base class for all stages implementing common functionality
    /// Based on DTXManiaNX CStage patterns with phase management
    /// </summary>
    public abstract class BaseStage : IStage
    {
        #region Fields

        protected readonly BaseGame _game;
        protected StagePhase _currentPhase = StagePhase.Inactive;
        protected bool _disposed = false;
        protected bool _isFirstUpdate = true;
        protected Dictionary<string, object> _sharedData;

        // Background system
        private ITexture _stageBackgroundTexture;
        private bool _backgroundLoadAttempted = false;

        #endregion

        #region Properties

        public abstract StageType Type { get; }
        public StagePhase CurrentPhase => _currentPhase;
        public IStageManager StageManager { get; set; }

        /// <summary>
        /// Whether the stage is active (opposite of b活性化してない)
        /// </summary>
        public bool IsActive => _currentPhase != StagePhase.Inactive;

        #endregion

        #region Constructor

        protected BaseStage(BaseGame game)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
        }

        #endregion

        #region IStage Implementation

        public virtual void Activate()
        {
            Activate(null);
        }

        public virtual void Activate(Dictionary<string, object> sharedData)
        {
        if (_currentPhase != StagePhase.Inactive)
        {
            return;
        }

            // Store shared data
            _sharedData = sharedData ?? new Dictionary<string, object>();
            
            // Reset state
            _isFirstUpdate = true;
            _currentPhase = StagePhase.FadeIn;


            // Load stage background
            LoadStageBackground();

            // Perform stage-specific activation
            OnActivate();
        }

        public virtual void Deactivate()
        {
        if (_currentPhase == StagePhase.Inactive)
        {
            return;
        }


            // Perform stage-specific deactivation
            OnDeactivate();

            // Cleanup background
            CleanupStageBackground();

            // Reset state
            _currentPhase = StagePhase.Inactive;
            _isFirstUpdate = true;
            _sharedData?.Clear();
        }

        public virtual void Update(double deltaTime)
        {
            if (_currentPhase == StagePhase.Inactive)
                return;


            // Handle first update
            if (_isFirstUpdate)
            {
                _isFirstUpdate = false;
                OnFirstUpdate(deltaTime);
            }

            // Update phase-specific logic
            UpdatePhase(deltaTime);

            // Perform stage-specific update
            OnUpdate(deltaTime);
        }

        public virtual void Draw(double deltaTime)
        {
            if (_currentPhase == StagePhase.Inactive)
                return;


            OnDraw(deltaTime);
        }

        public virtual void OnTransitionIn(IStageTransition transition)
        {
            
            _currentPhase = StagePhase.FadeIn;
            OnTransitionInStarted(transition);
        }

        public virtual void OnTransitionOut(IStageTransition transition)
        {
            
            _currentPhase = StagePhase.FadeOut;
            OnTransitionOutStarted(transition);
        }

        public virtual void OnTransitionComplete()
        {
            _currentPhase = StagePhase.Normal;
            OnTransitionCompleted();
        }

        #endregion

        #region Protected Virtual Methods

        /// <summary>
        /// Called when the stage is activated
        /// </summary>
        protected virtual void OnActivate() { }

        /// <summary>
        /// Called when the stage is deactivated
        /// </summary>
        protected virtual void OnDeactivate() { }

        /// <summary>
        /// Called on the first update after activation
        /// </summary>
        protected virtual void OnFirstUpdate(double deltaTime) { }

        /// <summary>
        /// Called every update
        /// </summary>
        protected virtual void OnUpdate(double deltaTime) { }

        /// <summary>
        /// Called every draw
        /// </summary>
        protected virtual void OnDraw(double deltaTime) { }

        /// <summary>
        /// Called when a transition in starts
        /// </summary>
        protected virtual void OnTransitionInStarted(IStageTransition transition) { }

        /// <summary>
        /// Called when a transition out starts
        /// </summary>
        protected virtual void OnTransitionOutStarted(IStageTransition transition) { }

        /// <summary>
        /// Called when a transition completes
        /// </summary>
        protected virtual void OnTransitionCompleted() { }


        #endregion

        #region Protected Helper Methods

        /// <summary>
        /// Get shared data of specified type
        /// </summary>
        protected T GetSharedData<T>(string key, T defaultValue = default(T))
        {
            if (string.IsNullOrEmpty(key) || _sharedData == null || !_sharedData.ContainsKey(key))
                return defaultValue;

            try
            {
                return (T)_sharedData[key];
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Set shared data
        /// </summary>
        protected void SetSharedData(string key, object value)
        {
            if (string.IsNullOrEmpty(key))
                return;

            _sharedData ??= new Dictionary<string, object>();
            _sharedData[key] = value;
        }

        /// <summary>
        /// Check if shared data exists
        /// </summary>
        protected bool HasSharedData(string key)
        {
            return !string.IsNullOrEmpty(key) && _sharedData != null && _sharedData.ContainsKey(key);
        }

        /// <summary>
        /// Change to another stage with optional transition
        /// </summary>
        protected void ChangeStage(StageType stageType, IStageTransition transition = null)
        {
            StageManager?.ChangeStage(stageType, transition ?? new InstantTransition());
        }

        /// <summary>
        /// Change to another stage with shared data
        /// </summary>
        protected void ChangeStage(StageType stageType, IStageTransition transition, Dictionary<string, object> sharedData)
        {
            StageManager?.ChangeStage(stageType, transition ?? new InstantTransition(), sharedData);
        }

        #endregion

        #region Background Management

        /// <summary>
        /// Gets the background texture path for this stage type
        /// Override to provide custom background texture path
        /// </summary>
        protected virtual string GetBackgroundTexturePath()
        {
            return Type switch
            {
                StageType.Startup => TexturePath.StartupBackground,
                StageType.Title => TexturePath.TitleBackground,
                StageType.SongSelect => TexturePath.SongSelectionBackground,
                StageType.SongTransition => TexturePath.SongTransitionBackground,
                StageType.Performance => TexturePath.PerformanceBackground,
                StageType.Result => TexturePath.ResultBackground,
                _ => null
            };
        }

        /// <summary>
        /// Load the background texture for this stage
        /// </summary>
        private void LoadStageBackground()
        {
            if (_backgroundLoadAttempted)
                return;

            _backgroundLoadAttempted = true;

            try
            {
                var texturePath = GetBackgroundTexturePath();
                if (!string.IsNullOrEmpty(texturePath))
                {
                    _stageBackgroundTexture = _game.ResourceManager.LoadTexture(texturePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BaseStage: Failed to load background for {Type}: {ex.Message}");
                _stageBackgroundTexture = null;
            }
        }

        /// <summary>
        /// Cleanup the background texture
        /// </summary>
        private void CleanupStageBackground()
        {
            _stageBackgroundTexture?.RemoveReference();
            _stageBackgroundTexture = null;
            _backgroundLoadAttempted = false;
        }

        /// <summary>
        /// Draw the stage background
        /// Call this in your OnDraw implementation before drawing other content
        /// </summary>
        protected void DrawStageBackground(SpriteBatch spriteBatch)
        {
            DrawStageBackground(spriteBatch, Vector2.Zero);
        }

        /// <summary>
        /// Draw the stage background at a specific position
        /// </summary>
        protected void DrawStageBackground(SpriteBatch spriteBatch, Vector2 position)
        {
            if (_stageBackgroundTexture != null)
            {
                _stageBackgroundTexture.Draw(spriteBatch, position);
            }
        }

        /// <summary>
        /// Draw the stage background with custom parameters
        /// </summary>
        protected void DrawStageBackground(SpriteBatch spriteBatch, Rectangle destinationRectangle)
        {
            if (_stageBackgroundTexture != null)
            {
                spriteBatch.Draw(_stageBackgroundTexture.Texture, destinationRectangle, Color.White);
            }
        }

        /// <summary>
        /// Check if the background texture is loaded and ready
        /// </summary>
        protected bool IsBackgroundReady => _stageBackgroundTexture != null;

        #endregion

        #region Private Methods

        private void UpdatePhase(double deltaTime)
        {
            // Handle automatic phase transitions
            switch (_currentPhase)
            {
                case StagePhase.FadeIn:
                    // Transition to normal after fade in completes
                    // This will be handled by the transition system
                    break;
                    
                case StagePhase.Normal:
                    // Normal operation
                    break;
                    
                case StagePhase.FadeOut:
                    // Transition out in progress
                    break;
            }
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
            if (!_disposed)
            {
                if (disposing)
                {
            // Deactivate if still active
            if (_currentPhase != StagePhase.Inactive)
            {
                Deactivate();
            }

            // Clear shared data
            _sharedData?.Clear();
            _sharedData = null;
                }
                _disposed = true;
            }
        }

        #endregion
    }
}
