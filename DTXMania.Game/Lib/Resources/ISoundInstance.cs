using Microsoft.Xna.Framework.Audio;
using System;

namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Sound instance abstraction interface for DTXManiaCX
    /// Abstracts MonoGame SoundEffectInstance for better testability
    /// </summary>
    public interface ISoundInstance : IDisposable
    {
        /// <summary>
        /// Gets or sets the volume for this sound instance
        /// </summary>
        float Volume { get; set; }

        /// <summary>
        /// Gets or sets the pitch for this sound instance
        /// </summary>
        float Pitch { get; set; }

        /// <summary>
        /// Gets or sets the pan for this sound instance
        /// </summary>
        float Pan { get; set; }

        /// <summary>
        /// Gets the current state of the sound instance
        /// </summary>
        SoundState State { get; }

        /// <summary>
        /// Gets or sets whether the sound instance should loop
        /// </summary>
        bool IsLooped { get; set; }

        /// <summary>
        /// Play or resume the sound instance
        /// </summary>
        void Play();

        /// <summary>
        /// Pause the sound instance
        /// </summary>
        void Pause();

        /// <summary>
        /// Stop the sound instance
        /// </summary>
        void Stop();

        /// <summary>
        /// Stop the sound instance immediately
        /// </summary>
        void Stop(bool immediate);
    }
}