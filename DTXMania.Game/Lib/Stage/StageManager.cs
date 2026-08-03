using System;
using System.Collections.Generic;
using DTXMania.Game;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTXMania.Game.Lib.Stage
{
    internal enum StageTransitionRejectionReason
    {
        Disposed,
        AlreadyTransitioning
    }

    /// <summary>
    /// Enhanced stage manager with transition support and structured logging.
    /// Based on DTXManiaNX stage management patterns with eフェーズID handling.
    /// </summary>
    /// <remarks>
    /// Provides lazy stage initialization and smooth transitions between game stages.
    /// Supports optional ILogger for stage transition diagnostics.
    /// </remarks>
    public class StageManager : IStageManager
    {
        private readonly IStageGame _game;
        private readonly ILogger<StageManager> _logger;
        private readonly ICrashBreadcrumbSink _breadcrumbs;
        private readonly ICrashContextSink _contexts;
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

        public StageManager(
            IStageGame game,
            ILogger<StageManager>? logger = null,
            ICrashBreadcrumbSink? breadcrumbs = null,
            ICrashContextSink? contexts = null)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
            _logger = logger ?? NullLogger<StageManager>.Instance;
            _breadcrumbs = breadcrumbs ?? EmptyCrashBreadcrumbSink.Instance;
            _contexts = contexts ?? EmptyCrashContextSink.Instance;
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

            var isStartupOrTitle =
                stageType is StageType.Startup or StageType.Title;
            var criticalPathTrace =
                isStartupOrTitle
                    ? ResolveCriticalPathTrace()
                    : null;
            StartupCriticalPathMilestone? constructBegin = null;
            StartupCriticalPathMilestone? constructEnd = null;
            if (isStartupOrTitle)
            {
                constructBegin = stageType == StageType.Startup
                    ? StartupCriticalPathMilestone.StartupConstructBegin
                    : StartupCriticalPathMilestone.TitleConstructBegin;
                constructEnd = stageType == StageType.Startup
                    ? StartupCriticalPathMilestone.StartupConstructEnd
                    : StartupCriticalPathMilestone.TitleConstructEnd;
                criticalPathTrace?.RecordExactlyOnce(constructBegin.Value);
            }

            try
            {
                // Create stage on demand
                IStage stage = stageType switch
                {
                    StageType.Startup => new StartupStage(_game),
                    StageType.Title => new TitleStage(_game),
                    StageType.Config => new ConfigStage(_game),
                    StageType.SongSelect => new SongSelectionStage(_game),
                    StageType.SongTransition => new SongTransitionStage(_game),
                    StageType.Performance => new PerformanceStage(_game),
                    StageType.Result => new ResultStage(_game),
                    StageType.DrumConfig => new DrumConfigStage(_game),
                    _ => throw new ArgumentException($"Unknown stage type: {stageType}")
                };

                stage.StageManager = this;
                _stages[stageType] = stage;

                return stage;
            }
            finally
            {
                if (constructEnd.HasValue)
                {
                    criticalPathTrace?.RecordExactlyOnce(constructEnd.Value);
                }
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
                _logger.LogWarning("Cannot change to {StageType} - manager is disposed", stageType);
                RecordTransitionRejected(StageTransitionRejectionReason.Disposed);
                return;
            }

            if (_isTransitioning)
            {
                _logger.LogDebug("Already transitioning, ignoring change to {StageType}", stageType);
                RecordTransitionRejected(StageTransitionRejectionReason.AlreadyTransitioning);
                return;
            }

            // GetOrCreateStage throws ArgumentException for unknown types, so targetStage is never null
            _ = GetOrCreateStage(stageType);

            var previousStageType = _currentStage?.Type;
            
            // Store transition information
            _targetStageType = stageType;
            _pendingSharedData = sharedData;
            _currentTransition = transition ?? new InstantTransition();
            _isTransitioning = true;

            var safeTransitionFields = CreateTransitionFields(
                previousStageType ?? StageType.Startup,
                stageType);
            _breadcrumbs.Record("stage_transition_requested", safeTransitionFields);
            _logger.LogDebug(
                new EventId(5103, "stage_transition_requested"),
                "Stage transition requested: {PreviousStage} -> {TargetStage}",
                previousStageType ?? StageType.Startup,
                stageType);

            // Log transition details
            var transitionTypeName = _currentTransition.GetType().Name;
            var fadeOutAlpha = _currentTransition.GetFadeOutAlpha();
            var fadeInAlpha = _currentTransition.GetFadeInAlpha();
            var sharedDataCount = sharedData?.Count ?? 0;
            
            _logger.LogDebug("Stage transition: {PreviousStage} -> {TargetStage}", 
                previousStageType ?? StageType.Startup, stageType);
            _logger.LogDebug("Transition: {TransitionType} (Duration: {Duration:F3}s, FadeOut: {FadeOutAlpha:F3}, FadeIn: {FadeInAlpha:F3})",
                transitionTypeName, _currentTransition.Duration, fadeOutAlpha, fadeInAlpha);
            _logger.LogDebug("SharedData: {SharedDataCount} items", sharedDataCount);

            // Start transition
            var criticalPathTrace = IsStartupToTitleTransition
                ? ResolveCriticalPathTrace()
                : null;
            if (IsStartupToTitleTransition)
            {
                criticalPathTrace?.RecordExactlyOnce(
                    StartupCriticalPathMilestone.TransitionStart);
            }
            _currentTransition.Start();
            _breadcrumbs.Record("stage_transition_started", safeTransitionFields);
            _logger.LogDebug(
                new EventId(5104, "stage_transition_started"),
                "Stage transition started: {PreviousStage} -> {TargetStage}",
                previousStageType ?? StageType.Startup,
                stageType);

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
                if (IsStartupToTitleTransition)
                {
                    ResolveCriticalPathTrace()?.IncrementTransitionUpdate(deltaTime);
                }
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

            var isStartupToTitleTransition = IsStartupToTitleTransition;
            var criticalPathTrace = isStartupToTitleTransition
                ? ResolveCriticalPathTrace()
                : null;
            if (isStartupToTitleTransition)
            {
                criticalPathTrace?.RecordExactlyOnce(
                    StartupCriticalPathMilestone.TransitionComplete);
            }

            // Log transition completion details
            var previousStageType = _previousStage?.Type ?? _currentStage?.Type;
            var transitionTypeName = _currentTransition?.GetType().Name ?? "Unknown";
            var finalFadeOutAlpha = _currentTransition?.GetFadeOutAlpha() ?? 0.0f;
            var finalFadeInAlpha = _currentTransition?.GetFadeInAlpha() ?? 1.0f;

            _logger.LogDebug("Completing transition: {PreviousStage} -> {TargetStage}",
                previousStageType, _targetStageType);
            _logger.LogDebug("Final transition alphas - FadeOut: {FadeOutAlpha:F3}, FadeIn: {FadeInAlpha:F3}",
                finalFadeOutAlpha, finalFadeInAlpha);

            // Store previous stage for cleanup
            _previousStage = _currentStage;

            // Deactivate previous stage
            if (_previousStage != null)
            {
                _logger.LogDebug("Deactivating previous stage: {StageType}", _previousStage.Type);
                if (isStartupToTitleTransition)
                {
                    criticalPathTrace?.RecordExactlyOnce(
                        StartupCriticalPathMilestone.StartupDeactivateBegin);
                }
                try
                {
                    _previousStage.Deactivate();
                }
                finally
                {
                    if (isStartupToTitleTransition)
                    {
                        criticalPathTrace?.RecordExactlyOnce(
                            StartupCriticalPathMilestone.StartupDeactivateEnd);
                    }
                }
            }

            // Activate new stage
            if (isStartupToTitleTransition)
            {
                criticalPathTrace?.RecordTitleCompletionLookup(
                    _stages.ContainsKey(StageType.Title));
            }
            var newStage = GetOrCreateStage(_targetStageType);
            _currentStage = newStage;
            _logger.LogDebug("Activating new stage: {StageType}", _targetStageType);
            _currentStage.Activate(_pendingSharedData);
            _currentStage.OnTransitionIn(_currentTransition);
            _currentStage.OnTransitionComplete();

            var completedTransitionFields = CreateTransitionFields(
                previousStageType ?? StageType.Startup,
                _targetStageType);
            CrashContextPublisher.PublishStage(_contexts, _targetStageType, _stages.Count);
            _breadcrumbs.Record("stage_transition_completed", completedTransitionFields);
            _logger.LogInformation(
                new EventId(5105, "stage_transition_completed"),
                "Stage transition completed: {PreviousStage} -> {TargetStage}",
                previousStageType ?? StageType.Startup,
                _targetStageType);

            // Clean up transition state
            _isTransitioning = false;
            _currentTransition = null;
            _pendingSharedData = null;
            _previousStage = null;

            _logger.LogInformation("Stage transition to {StageType} completed", _targetStageType);
        }

        private void RecordTransitionRejected(StageTransitionRejectionReason reason)
        {
            var fields = new Dictionary<string, object?>
            {
                ["Reason"] = reason
            };
            _breadcrumbs.Record("stage_transition_rejected", fields);
            _logger.LogWarning(
                new EventId(5111, "stage_transition_rejected"),
                "Stage transition rejected: {Reason}",
                reason);
        }

        private static Dictionary<string, object?> CreateTransitionFields(
            StageType previousStage,
            StageType targetStage)
        {
            return new Dictionary<string, object?>
            {
                ["PreviousStage"] = previousStage,
                ["TargetStage"] = targetStage
            };
        }

        private bool IsStartupToTitleTransition =>
            _currentStage?.Type == StageType.Startup &&
            _targetStageType == StageType.Title;

        private StartupCriticalPathTrace ResolveCriticalPathTrace() =>
            StartupCriticalPathHost.TryResolve(_game);

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
                    // Log disposal information
                    var currentStageType = _currentStage?.Type;
                    var isCurrentlyTransitioning = _isTransitioning;
                    var currentTransitionType = _currentTransition?.GetType().Name;
                    
                    _logger.LogDebug("StageManager.Dispose: CurrentStage={CurrentStage}, IsTransitioning={IsTransitioning}, Transition={TransitionType}",
                        currentStageType, isCurrentlyTransitioning, currentTransitionType);
                    _logger.LogDebug("Disposing {StageCount} total stages", _stages.Count);

                    // Deactivate current stage before disposal
                    if (_currentStage != null)
                    {
                        _logger.LogDebug("Deactivating current stage before disposal: {StageType}", _currentStage.Type);
                        _currentStage.Deactivate();
                        _currentStage = null;
                    }

                    // Dispose all stages
                    foreach (var stage in _stages.Values)
                    {
                        if (stage != null)
                        {
                            _logger.LogDebug("Disposing stage: {StageType}", stage.Type);
                            stage.Dispose();
                        }
                    }
                    _stages.Clear();
                    
                    _logger.LogDebug("StageManager disposal completed");
                }
                _disposed = true;
            }
        }

        #endregion
    }
}
