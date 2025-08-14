using System;
using System.Collections.Generic;
using DTXMania.Game;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Enhanced stage manager with transition support
    /// Based on DTXManiaNX stage management patterns with eフェーズID handling
    /// </summary>
    public class StageManager : IStageManager
    {
        private readonly BaseGame _game;
        private readonly Dictionary<StageType, IStage> _stages;
        private IStage _currentStage;
        private IStage _previousStage;
        private IStageTransition _currentTransition;
        private bool _disposed = false;

        // Transition state
        private bool _isTransitioning = false;
        private StageType _targetStageType;
        private Dictionary<string, object> _pendingSharedData;

        public IStage CurrentStage => _currentStage;
        public StagePhase CurrentPhase => _currentStage?.CurrentPhase ?? StagePhase.Inactive;
        public bool IsTransitioning => _isTransitioning;

        public StageManager(BaseGame game)
        {
            _game = game;
            _stages = new Dictionary<StageType, IStage>();
            // Don't initialize stages immediately - use lazy initialization
        }

        /// <summary>
        /// Gets or creates a stage for the specified type (lazy initialization)
        /// </summary>
        private IStage GetOrCreateStage(StageType stageType)
        {
            if (_stages.TryGetValue(stageType, out var existingStage))
            {
                return existingStage;
            }

            // Create stage on demand
            IStage stage = stageType switch
            {
                StageType.Startup => new StartupStage(_game),
                StageType.Title => new TitleStage(_game),
                StageType.Config => new ConfigStage(_game),
                StageType.SongSelect => new SongSelectionStage(_game),
                StageType.SongTransition => new SongTransitionStage(_game),
                StageType.Performance => new PerformanceStage(_game),
                _ => throw new ArgumentException($"Unknown stage type: {stageType}")
            };

            stage.StageManager = this;
            _stages[stageType] = stage;

            return stage;
        }

        public void ChangeStage(StageType stageType)
        {
            ChangeStage(stageType, new InstantTransition(), null);
        }

        public void ChangeStage(StageType stageType, IStageTransition transition)
        {
            ChangeStage(stageType, transition, null);
        }

        public void ChangeStage(StageType stageType, IStageTransition transition, Dictionary<string, object> sharedData)
        {
            if (_disposed)
            {
                System.Diagnostics.Debug.WriteLine($"StageManager: Cannot change to {stageType} - manager is disposed");
                return;
            }

            if (_isTransitioning)
            {
                System.Diagnostics.Debug.WriteLine($"StageManager: Already transitioning, ignoring change to {stageType}");
                return;
            }

            var targetStage = GetOrCreateStage(stageType);
            if (targetStage == null)
            {
                System.Diagnostics.Debug.WriteLine($"StageManager: Stage {stageType} not found");
                return;
            }

            var previousStageType = _currentStage?.Type;
            
            // Store transition information
            _targetStageType = stageType;
            _pendingSharedData = sharedData;
            _currentTransition = transition ?? new InstantTransition();
            _isTransitioning = true;

            // DEBUG: Detailed transition audit information
            var transitionTypeName = _currentTransition.GetType().Name;
            var fadeOutAlpha = _currentTransition.GetFadeOutAlpha();
            var fadeInAlpha = _currentTransition.GetFadeInAlpha();
            var sharedDataStatus = sharedData == null ? "null" : (sharedData.Count == 0 ? "empty" : $"contains {sharedData.Count} keys: [{string.Join(", ", sharedData.Keys)}]");
            
            System.Diagnostics.Debug.WriteLine($"[STAGE_AUDIT] ChangeStage: {previousStageType ?? StageType.Startup} -> {stageType}");
            System.Diagnostics.Debug.WriteLine($"[STAGE_AUDIT] Transition: {transitionTypeName} (Duration: {_currentTransition.Duration:F3}s, FadeOut: {fadeOutAlpha:F3}, FadeIn: {fadeInAlpha:F3})");
            System.Diagnostics.Debug.WriteLine($"[STAGE_AUDIT] SharedData: {sharedDataStatus}");

            // Start transition
            _currentTransition.Start();

            // Notify current stage of transition out
            if (_currentStage != null)
            {
                _currentStage.OnTransitionOut(_currentTransition);
            }

            // For instant transitions, complete immediately
            if (_currentTransition is InstantTransition)
            {
                CompleteTransition();
            }
        }

        public void Update(double deltaTime)
        {
            if (_disposed)
                return;

            // Update transition if in progress
            if (_isTransitioning && _currentTransition != null)
            {
                _currentTransition.Update(deltaTime);

                // Check if transition is complete
                if (_currentTransition.IsComplete)
                {
                    CompleteTransition();
                }
            }

            // Update current stage
            _currentStage?.Update(deltaTime);
        }

        public void Draw(double deltaTime)
        {
            if (_disposed)
                return;

            // During transition, we may need to draw both stages with fade effects
            if (_isTransitioning && _currentTransition != null)
            {
                DrawTransition(deltaTime);
            }
            else
            {
                // Normal drawing
                _currentStage?.Draw(deltaTime);
            }
        }

        private void CompleteTransition()
        {
            if (!_isTransitioning)
                return;

            // DEBUG: Detailed completion audit
            var previousStageType = _previousStage?.Type ?? _currentStage?.Type;
            var transitionTypeName = _currentTransition?.GetType().Name ?? "Unknown";
            var finalFadeOutAlpha = _currentTransition?.GetFadeOutAlpha() ?? 0.0f;
            var finalFadeInAlpha = _currentTransition?.GetFadeInAlpha() ?? 1.0f;
            var sharedDataStatus = _pendingSharedData == null ? "null" : (_pendingSharedData.Count == 0 ? "empty" : $"contains {_pendingSharedData.Count} keys: [{string.Join(", ", _pendingSharedData.Keys)}]");

            System.Diagnostics.Debug.WriteLine($"[STAGE_AUDIT] CompleteTransition: {previousStageType} -> {_targetStageType}");
            System.Diagnostics.Debug.WriteLine($"[STAGE_AUDIT] Final transition alphas - FadeOut: {finalFadeOutAlpha:F3}, FadeIn: {finalFadeInAlpha:F3}");
            System.Diagnostics.Debug.WriteLine($"[STAGE_AUDIT] Passing SharedData: {sharedDataStatus}");

            // Store previous stage for cleanup
            _previousStage = _currentStage;

            // Deactivate previous stage
            if (_previousStage != null)
            {
                System.Diagnostics.Debug.WriteLine($"[STAGE_AUDIT] Deactivating previous stage: {_previousStage.Type}");
                _previousStage.Deactivate();
            }

            // Activate new stage
            var newStage = GetOrCreateStage(_targetStageType);
            if (newStage != null)
            {
                _currentStage = newStage;
                System.Diagnostics.Debug.WriteLine($"[STAGE_AUDIT] Activating new stage: {_targetStageType}");
                _currentStage.Activate(_pendingSharedData);
                _currentStage.OnTransitionIn(_currentTransition);
                _currentStage.OnTransitionComplete();
            }

            // Clean up transition state
            _isTransitioning = false;
            _currentTransition = null;
            _pendingSharedData = null;
            _previousStage = null;

            System.Diagnostics.Debug.WriteLine($"[STAGE_AUDIT] Transition to {_targetStageType} completed - stage is now active");
        }

        private void DrawTransition(double deltaTime)
        {
            // For now, just draw the current stage
            // In a more advanced implementation, we could apply fade effects here
            // by rendering to render targets and blending them based on transition alpha values

            if (_currentStage != null)
            {
                // Draw outgoing stage with fade out alpha
                float fadeOutAlpha = _currentTransition.GetFadeOutAlpha();
                if (fadeOutAlpha > 0.0f)
                {
                    // TODO: Apply fade out alpha to rendering
                    _currentStage.Draw(deltaTime);
                }
            }

            // For crossfade transitions, we would also draw the incoming stage here
            // with the fade in alpha from _currentTransition.GetFadeInAlpha()
        }

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
                    // DEBUG: Audit stage manager disposal
                    var currentStageType = _currentStage?.Type;
                    var isCurrentlyTransitioning = _isTransitioning;
                    var currentTransitionType = _currentTransition?.GetType().Name;
                    
                    System.Diagnostics.Debug.WriteLine($"[STAGE_AUDIT] StageManager.Dispose: CurrentStage={currentStageType}, IsTransitioning={isCurrentlyTransitioning}, Transition={currentTransitionType}");
                    System.Diagnostics.Debug.WriteLine($"[STAGE_AUDIT] Disposing {_stages.Count} total stages");

                    // Deactivate current stage before disposal
                    if (_currentStage != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[STAGE_AUDIT] Deactivating current stage before disposal: {_currentStage.Type}");
                        _currentStage.Deactivate();
                        _currentStage = null;
                    }

                    // Dispose all stages
                    foreach (var stage in _stages.Values)
                    {
                        if (stage != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[STAGE_AUDIT] Disposing stage: {stage.Type}");
                            stage.Dispose();
                        }
                    }
                    _stages.Clear();
                    
                    System.Diagnostics.Debug.WriteLine($"[STAGE_AUDIT] StageManager disposal completed");
                }
                _disposed = true;
            }
        }

        #endregion
    }
}