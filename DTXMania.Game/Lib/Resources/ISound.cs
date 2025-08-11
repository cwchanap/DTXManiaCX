using Microsoft.Xna.Framework.Audio;
using System;

namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Sound abstraction interface for DTXManiaCX
    /// Wraps MonoGame SoundEffect with reference counting and disposal tracking
    /// Based on DTXMania's sound management patterns
    /// </summary>
    public interface ISound : IDisposable
    {
        #region Properties

        /// <summary>
        /// Underlying MonoGame SoundEffect
        /// </summary>
        SoundEffect SoundEffect { get; }

        /// <summary>
        /// Source path used to load this sound
        /// </summary>
        string SourcePath { get; }

        /// <summary>
        /// Duration of the sound in seconds
        /// </summary>
        TimeSpan Duration { get; }

        /// <summary>
        /// Whether this sound is disposed
        /// </summary>
        bool IsDisposed { get; }

        /// <summary>
        /// Current reference count
        /// </summary>
        int ReferenceCount { get; }

        #endregion

        #region Reference Counting

        /// <summary>
        /// Add a reference to this sound
        /// </summary>
        void AddReference();

        /// <summary>
        /// Remove a reference from this sound
        /// </summary>
        void RemoveReference();

        #endregion

        #region Playback

        /// <summary>
        /// Play the sound once
        /// </summary>
        /// <returns>Sound effect instance for advanced control</returns>
        SoundEffectInstance Play();

        /// <summary>
        /// Play the sound with volume control
        /// </summary>
        /// <param name="volume">Volume (0.0 to 1.0)</param>
        /// <returns>Sound effect instance for advanced control</returns>
        SoundEffectInstance Play(float volume);

        /// <summary>
        /// Play the sound with full control
        /// </summary>
        /// <param name="volume">Volume (0.0 to 1.0)</param>
        /// <param name="pitch">Pitch (-1.0 to 1.0)</param>
        /// <param name="pan">Pan (-1.0 to 1.0)</param>
        /// <returns>Sound effect instance for advanced control</returns>
        SoundEffectInstance Play(float volume, float pitch, float pan);

        /// <summary>
        /// Create a sound effect instance for advanced playback control
        /// </summary>
        /// <returns>Sound effect instance</returns>
        SoundEffectInstance CreateInstance();

        #endregion
    }
}
