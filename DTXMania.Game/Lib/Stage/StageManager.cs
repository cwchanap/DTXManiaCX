using System;
using System.Collections.Generic;
using DTXMania.Game;

namespace DTX.Stage
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
            InitializeStages();
        }        private void InitializeStages()
        {
            // Initialize all available stages and set stage manager reference
            var stages = new Dictionary<StageType, IStage>
            {
                [StageType.Startup] = new StartupStage(_game),
                [StageType.Title] = new TitleStage(_game),
                [StageType.Config] = new ConfigStage(_game),
                [StageType.SongSelect] = new SongSelectionStage(_game)
            };

            foreach (var kvp in stages)
            {
                kvp.Value.StageManager = this;
                _stages[kvp.Key] = kvp.Value;
            }
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

            if (!_stages.TryGetValue(stageType, out var targetStage))
            {
                System.Diagnostics.Debug.WriteLine($"StageManager: Stage {stageType} not found");
                return;
            }

            var previousStageType = _currentStage?.Type;
            System.Diagnostics.Debug.WriteLine($"StageManager: Starting transition from {previousStageType} to {stageType}");

            // Store transition information
            _targetStageType = stageType;
            _pendingSharedData = sharedData;
            _currentTransition = transition ?? new InstantTransition();
            _isTransitioning = true;

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

            System.Diagnostics.Debug.WriteLine($"StageManager: Completing transition to {_targetStageType}");

            // Store previous stage for cleanup
            _previousStage = _currentStage;

            // Deactivate previous stage
            if (_previousStage != null)
            {
                _previousStage.Deactivate();
            }

            // Activate new stage
            if (_stages.TryGetValue(_targetStageType, out var newStage))
            {
                _currentStage = newStage;
                _currentStage.Activate(_pendingSharedData);
                _currentStage.OnTransitionIn(_currentTransition);
                _currentStage.OnTransitionComplete();
            }

            // Clean up transition state
            _isTransitioning = false;
            _currentTransition = null;
            _pendingSharedData = null;
            _previousStage = null;

            System.Diagnostics.Debug.WriteLine($"StageManager: Transition to {_targetStageType} completed");
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
                    // Deactivate current stage before disposal
                    _currentStage?.Deactivate();
                    _currentStage = null;

                    // Dispose all stages
                    foreach (var stage in _stages.Values)
                    {
                        stage?.Dispose();
                    }
                    _stages.Clear();
                }
                _disposed = true;
            }
        }

        #endregion
    }
}