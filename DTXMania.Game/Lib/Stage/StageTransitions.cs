using System;

namespace DTX.Stage
{
    /// <summary>
    /// Base class for stage transitions with common functionality
    /// Based on DTXManiaNX CActFIFO patterns
    /// </summary>
    public abstract class BaseStageTransition : IStageTransition
    {
        protected double _duration;
        protected double _elapsedTime;
        protected bool _isStarted;

        public double Duration => _duration;
        public double Progress => _isStarted ? Math.Min(_elapsedTime / _duration, 1.0) : 0.0;
        public bool IsComplete => _isStarted && _elapsedTime >= _duration;

        protected BaseStageTransition(double duration)
        {
            _duration = Math.Max(duration, 0.001); // Minimum duration to prevent division by zero
            Reset();
        }

        public virtual void Start()
        {
            _isStarted = true;
            _elapsedTime = 0.0;
        }

        public virtual void Update(double deltaTime)
        {
            if (_isStarted && !IsComplete)
            {
                _elapsedTime += deltaTime;
            }
        }

        public virtual void Reset()
        {
            _isStarted = false;
            _elapsedTime = 0.0;
        }

        public abstract float GetFadeOutAlpha();
        public abstract float GetFadeInAlpha();
    }

    /// <summary>
    /// Instant transition with no fade effects
    /// Used for immediate stage changes
    /// </summary>
    public class InstantTransition : BaseStageTransition
    {
        public InstantTransition() : base(0.001) // Very short duration for instant effect
        {
        }

        public override void Start()
        {
            base.Start();
            // For instant transition, immediately mark as complete
            _elapsedTime = _duration;
        }

        public override float GetFadeOutAlpha()
        {
            return IsComplete ? 0.0f : 1.0f;
        }

        public override float GetFadeInAlpha()
        {
            return IsComplete ? 1.0f : 0.0f;
        }
    }

    /// <summary>
    /// Fade transition that fades out current stage then fades in new stage
    /// Based on DTXManiaNX fade patterns
    /// </summary>
    public class FadeTransition : BaseStageTransition
    {
        private readonly double _fadeOutDuration;
        private readonly double _fadeInDuration;

        public FadeTransition(double fadeOutDuration = 0.5, double fadeInDuration = 0.5) 
            : base(fadeOutDuration + fadeInDuration)
        {
            _fadeOutDuration = fadeOutDuration;
            _fadeInDuration = fadeInDuration;
        }

        public override float GetFadeOutAlpha()
        {
            if (!_isStarted)
                return 1.0f;

            if (_elapsedTime <= _fadeOutDuration)
            {
                // Fade out phase: 1.0 -> 0.0
                double fadeProgress = _elapsedTime / _fadeOutDuration;
                return (float)(1.0 - fadeProgress);
            }
            else
            {
                // Fade in phase: stay at 0.0
                return 0.0f;
            }
        }

        public override float GetFadeInAlpha()
        {
            if (!_isStarted)
                return 0.0f;

            if (_elapsedTime <= _fadeOutDuration)
            {
                // Fade out phase: stay at 0.0
                return 0.0f;
            }
            else
            {
                // Fade in phase: 0.0 -> 1.0
                double fadeInElapsed = _elapsedTime - _fadeOutDuration;
                double fadeProgress = fadeInElapsed / _fadeInDuration;
                return (float)Math.Min(fadeProgress, 1.0);
            }
        }
    }

    /// <summary>
    /// Crossfade transition that simultaneously fades out current stage and fades in new stage
    /// Provides smooth visual transition between stages
    /// </summary>
    public class CrossfadeTransition : BaseStageTransition
    {
        public CrossfadeTransition(double duration = 0.5) : base(duration)
        {
        }

        public override float GetFadeOutAlpha()
        {
            if (!_isStarted)
                return 1.0f;

            // Fade out: 1.0 -> 0.0
            return (float)(1.0 - Progress);
        }

        public override float GetFadeInAlpha()
        {
            if (!_isStarted)
                return 0.0f;

            // Fade in: 0.0 -> 1.0
            return (float)Progress;
        }
    }

    /// <summary>
    /// DTXMania-style fade transition with easing curves
    /// Matches the original DTXMania fade timing and feel
    /// </summary>
    public class DTXManiaFadeTransition : BaseStageTransition
    {
        private readonly bool _useEasing;

        public DTXManiaFadeTransition(double duration = 0.7, bool useEasing = true) : base(duration)
        {
            _useEasing = useEasing;
        }

        public override float GetFadeOutAlpha()
        {
            if (!_isStarted)
                return 1.0f;

            double progress = Progress;
            if (_useEasing)
            {
                // DTXMania-style easing: smooth fade out
                progress = 1.0 - Math.Cos(progress * Math.PI * 0.5);
            }

            return (float)(1.0 - progress);
        }

        public override float GetFadeInAlpha()
        {
            if (!_isStarted)
                return 0.0f;

            double progress = Progress;
            if (_useEasing)
            {
                // DTXMania-style easing: smooth fade in
                progress = Math.Sin(progress * Math.PI * 0.5);
            }

            return (float)progress;
        }
    }

    /// <summary>
    /// Special transition for startup to title stage
    /// Based on DTXManiaNX タイトル_起動画面からのフェードイン pattern
    /// </summary>
    public class StartupToTitleTransition : BaseStageTransition
    {
        public StartupToTitleTransition(double duration = 1.0) : base(duration)
        {
        }

        public override float GetFadeOutAlpha()
        {
            if (!_isStarted)
                return 1.0f;

            // Slower fade out for startup screen
            double progress = Math.Min(Progress * 1.5, 1.0);
            return (float)(1.0 - progress);
        }

        public override float GetFadeInAlpha()
        {
            if (!_isStarted)
                return 0.0f;

            // Delayed fade in for title screen
            double delayedProgress = Math.Max(0.0, (Progress - 0.3) / 0.7);
            return (float)Math.Min(delayedProgress, 1.0);
        }
    }
}
