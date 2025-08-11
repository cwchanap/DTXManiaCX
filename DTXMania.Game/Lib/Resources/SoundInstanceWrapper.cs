using Microsoft.Xna.Framework.Audio;
using System;

namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Wrapper implementation of ISoundInstance that wraps MonoGame SoundEffectInstance
    /// Provides testable abstraction over the sealed SoundEffectInstance class
    /// </summary>
    public class SoundInstanceWrapper : ISoundInstance
    {
        private readonly SoundEffectInstance _instance;
        private bool _disposed = false;

        /// <summary>
        /// Initialize a new wrapper around a SoundEffectInstance
        /// </summary>
        /// <param name="instance">The SoundEffectInstance to wrap</param>
        public SoundInstanceWrapper(SoundEffectInstance instance)
        {
            _instance = instance ?? throw new ArgumentNullException(nameof(instance));
        }

        /// <summary>
        /// Gets or sets the volume for this sound instance
        /// </summary>
        public float Volume
        {
            get => _instance.Volume;
            set => _instance.Volume = value;
        }

        /// <summary>
        /// Gets or sets the pitch for this sound instance
        /// </summary>
        public float Pitch
        {
            get => _instance.Pitch;
            set => _instance.Pitch = value;
        }

        /// <summary>
        /// Gets or sets the pan for this sound instance
        /// </summary>
        public float Pan
        {
            get => _instance.Pan;
            set => _instance.Pan = value;
        }

        /// <summary>
        /// Gets the current state of the sound instance
        /// </summary>
        public SoundState State => _instance.State;

        /// <summary>
        /// Gets or sets whether the sound instance should loop
        /// </summary>
        public bool IsLooped
        {
            get => _instance.IsLooped;
            set => _instance.IsLooped = value;
        }

        /// <summary>
        /// Play or resume the sound instance
        /// </summary>
        public void Play()
        {
            _instance.Play();
        }

        /// <summary>
        /// Pause the sound instance
        /// </summary>
        public void Pause()
        {
            _instance.Pause();
        }

        /// <summary>
        /// Stop the sound instance
        /// </summary>
        public void Stop()
        {
            _instance.Stop();
        }

        /// <summary>
        /// Stop the sound instance immediately
        /// </summary>
        public void Stop(bool immediate)
        {
            _instance.Stop(immediate);
        }

        /// <summary>
        /// Dispose the wrapped sound instance
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected dispose pattern implementation
        /// </summary>
        /// <param name="disposing">True if disposing from Dispose() method</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _instance?.Dispose();
                _disposed = true;
            }
        }
    }
}