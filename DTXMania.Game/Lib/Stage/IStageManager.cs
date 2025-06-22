using System;
using System.Collections.Generic;

namespace DTX.Stage
{
    /// <summary>
    /// Stage manager interface with transition support
    /// Based on DTXManiaNX CStage patterns with eフェーズID (phase ID) management
    /// </summary>
    public interface IStageManager : IDisposable
    {
        IStage CurrentStage { get; }
        StagePhase CurrentPhase { get; }
        bool IsTransitioning { get; }

        void ChangeStage(StageType stageType);
        void ChangeStage(StageType stageType, IStageTransition transition);
        void ChangeStage(StageType stageType, IStageTransition transition, Dictionary<string, object> sharedData);
        void Update(double deltaTime);
        void Draw(double deltaTime);
    }    /// <summary>
    /// Stage types available in DTXManiaCX
    /// </summary>
    public enum StageType
    {
        Startup,
        Title,
        Config,
        SongSelect,
        Performance,
        Result
    }

    /// <summary>
    /// Stage phases based on DTXManiaNX eフェーズID pattern
    /// Represents the current state of stage lifecycle and transitions
    /// </summary>
    public enum StagePhase
    {
        /// <summary>
        /// Stage is not active (b活性化してない equivalent)
        /// </summary>
        Inactive,

        /// <summary>
        /// Stage is fading in (Common_FadeIn equivalent)
        /// </summary>
        FadeIn,

        /// <summary>
        /// Stage is in normal operation (Common_DefaultState equivalent)
        /// </summary>
        Normal,

        /// <summary>
        /// Stage is fading out (Common_FadeOut equivalent)
        /// </summary>
        FadeOut,

        /// <summary>
        /// Special fade from startup (タイトル_起動画面からのフェードイン equivalent)
        /// </summary>
        FadeInFromStartup
    }

    /// <summary>
    /// Enhanced stage interface with transition and phase support
    /// Based on DTXManiaNX CStage patterns
    /// </summary>
    public interface IStage : IDisposable
    {
        StageType Type { get; }
        StagePhase CurrentPhase { get; }
        IStageManager StageManager { get; set; }

        void Activate();
        void Activate(Dictionary<string, object> sharedData);
        void Deactivate();
        void Update(double deltaTime);
        void Draw(double deltaTime);

        // Transition lifecycle methods
        void OnTransitionIn(IStageTransition transition);
        void OnTransitionOut(IStageTransition transition);
        void OnTransitionComplete();
    }

    /// <summary>
    /// Stage transition interface for fade effects and animations
    /// Based on DTXManiaNX CActFIFO patterns
    /// </summary>
    public interface IStageTransition
    {
        /// <summary>
        /// Duration of the transition in seconds
        /// </summary>
        double Duration { get; }

        /// <summary>
        /// Current progress of the transition (0.0 to 1.0)
        /// </summary>
        double Progress { get; }

        /// <summary>
        /// Whether the transition has completed
        /// </summary>
        bool IsComplete { get; }

        /// <summary>
        /// Start the transition
        /// </summary>
        void Start();

        /// <summary>
        /// Update the transition progress
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last update</param>
        void Update(double deltaTime);

        /// <summary>
        /// Get the fade alpha for the outgoing stage (1.0 = fully visible, 0.0 = fully transparent)
        /// </summary>
        float GetFadeOutAlpha();

        /// <summary>
        /// Get the fade alpha for the incoming stage (1.0 = fully visible, 0.0 = fully transparent)
        /// </summary>
        float GetFadeInAlpha();

        /// <summary>
        /// Reset the transition to initial state
        /// </summary>
        void Reset();
    }
}