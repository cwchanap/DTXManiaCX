using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

        protected readonly IStageGame _game;
        protected StagePhase _currentPhase = StagePhase.Inactive;
        protected bool _disposed = false;
        protected bool _isFirstUpdate = true;
        // Payload contract is nullability-annotated; the rest of this legacy file stays in its original (oblivious) nullable context.
        #nullable enable
        protected StageTransitionPayload? _transitionPayload;
        #nullable restore

        private ILogger _logger;

        // Background system
        private ITexture _stageBackgroundTexture;
        private bool _backgroundLoadAttempted = false;

        // Stage-local background work (HPA-799): fresh scope per activation,
        // drained at the start of Update, retired synchronously before OnDeactivate.
        #nullable enable
        private StageBackgroundWorkScope? _backgroundWork;
        #nullable restore

        #endregion

        #region Properties

        /// <summary>
        /// Stage logger, categorized by the concrete stage type. Resolved lazily from
        /// <see cref="IStageGame.LoggerFactory"/>; a no-op logger when none is provided.
        /// </summary>
        protected ILogger Logger =>
            _logger ??= _game?.LoggerFactory?.CreateLogger(GetType()) ?? NullLogger.Instance;

        public abstract StageType Type { get; }
        public StagePhase CurrentPhase => _currentPhase;
        public IStageManager StageManager { get; set; }

        /// <summary>
        /// Whether the stage is active (opposite of b活性化してない)
        /// </summary>
        public bool IsActive => _currentPhase != StagePhase.Inactive;

        /// <summary>
        /// Stage-local background work scope for the current activation (HPA-799).
        /// Created on Activate, drained by Update before stage logic, and retired
        /// synchronously before OnDeactivate; null outside an active activation.
        /// Internal because the scope type is internal; all stage subclasses and
        /// test fixtures live inside this assembly (plus InternalsVisibleTo).
        /// </summary>
        #nullable enable
        internal StageBackgroundWorkScope? BackgroundWork => _backgroundWork;
        #nullable restore

        #endregion

        #region Constructor

        protected BaseStage(IStageGame game)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
        }

        #endregion

        #region IStage Implementation

        public virtual void Activate()
        {
            Activate(null);
        }

        #nullable enable
        public virtual void Activate(StageTransitionPayload? payload)
        #nullable restore
        {
            if (_currentPhase != StagePhase.Inactive)
            {
                return;
            }

            // Store typed transition payload
            _transitionPayload = payload;

            // Reset state
            _isFirstUpdate = true;
            _currentPhase = StagePhase.FadeIn;

            // Fresh stage-local scope per activation, available to OnActivate.
            // Retire any leftover scope so queued owned results cannot leak.
            RetireBackgroundWork();
            _backgroundWork = CreateBackgroundWorkScope();

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


            // Retire queued background work before stage teardown so dropped
            // results cannot reach disposed UI/resources (HPA-799).
            RetireBackgroundWork();

            // Perform stage-specific deactivation
            OnDeactivate();

            // Cleanup background
            CleanupStageBackground();

            // Reset state
            _currentPhase = StagePhase.Inactive;
            _isFirstUpdate = true;
            _transitionPayload = null;
        }

        public virtual void Update(double deltaTime)
        {
            if (_currentPhase == StagePhase.Inactive)
                return;

            // Apply background-work results before stage logic and input,
            // capped at a bounded budget per frame (HPA-799).
            _backgroundWork?.Drain();

            // A drained callback may have triggered a synchronous
            // InstantTransition/Deactivate; never continue while inactive.
            if (_currentPhase == StagePhase.Inactive)
                return;

            // Handle first update
            if (_isFirstUpdate)
            {
                _isFirstUpdate = false;
                OnFirstUpdate(deltaTime);
            }

            // OnFirstUpdate may also have deactivated the stage synchronously.
            if (_currentPhase == StagePhase.Inactive)
                return;

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
        /// Change to another stage with optional transition
        /// </summary>
        protected void ChangeStage(StageType stageType, IStageTransition transition = null)
        {
            StageManager?.ChangeStage(stageType, transition ?? new InstantTransition());
        }

        /// <summary>
        /// Change to another stage with a typed transition payload
        /// </summary>
        #nullable enable
        protected void ChangeStage(StageType stageType, IStageTransition transition, StageTransitionPayload? payload)
        #nullable restore
        {
            StageManager?.ChangeStage(stageType, transition ?? new InstantTransition(), payload);
        }

        /// <summary>
        /// Retires the current background-work scope synchronously (HPA-799); idempotent.
        /// Called by Deactivate before OnDeactivate; stages overriding Deactivate with
        /// their own teardown call this first so dropped results cannot reach disposed
        /// resources.
        /// </summary>
        protected void RetireBackgroundWork()
        {
            var scope = _backgroundWork;
            _backgroundWork = null;
            scope?.Retire();
        }

        /// <summary>
        /// Internal headless-test seam (HPA-799): installs a fresh scope without
        /// activation for stages that cannot call Activate under test (GPU-dependent
        /// construction). Any previous scope is retired synchronously. Never used by
        /// production code and intentionally the only such seam.
        /// </summary>
        internal StageBackgroundWorkScope InstallTestBackgroundScope()
        {
            RetireBackgroundWork();
            var scope = CreateBackgroundWorkScope();
            _backgroundWork = scope;
            return scope;
        }

        /// <summary>
        /// Creates a scope whose update-thread callback failures are logged instead of
        /// escaping Drain into Game.Update (HPA-799).
        /// </summary>
        private StageBackgroundWorkScope CreateBackgroundWorkScope() =>
            new(exception => Logger.LogError(
                exception,
                "{Stage}: a background-work callback failed on the update thread",
                GetType().Name));

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
                    _game.ResourceManager.TryLoadTexture(texturePath, out var texture);
                    _stageBackgroundTexture = texture;
                }
            }
            catch (Exception)
            {
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

            // Clear transition payload
            _transitionPayload = null;
                }
                _disposed = true;
            }
        }

        #endregion
    }
}
