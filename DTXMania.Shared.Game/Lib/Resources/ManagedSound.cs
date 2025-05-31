using Microsoft.Xna.Framework.Audio;
using System;
using System.IO;
using System.Threading;
using NVorbis;

namespace DTX.Resources
{
    /// <summary>
    /// Managed sound implementation with reference counting
    /// Based on DTXMania's sound management patterns
    /// </summary>
    public class ManagedSound : ISound
    {
        #region Private Fields

        private SoundEffect _soundEffect;
        private readonly string _sourcePath;
        private int _referenceCount = 0;
        private bool _disposed = false;

        #endregion

        #region Properties

        public SoundEffect SoundEffect => _soundEffect;
        public string SourcePath => _sourcePath;
        public TimeSpan Duration => _soundEffect?.Duration ?? TimeSpan.Zero;
        public bool IsDisposed => _disposed;
        public int ReferenceCount => _referenceCount;

        #endregion

        #region Constructor

        /// <summary>
        /// Create sound from file path
        /// </summary>
        public ManagedSound(string filePath, string sourcePath = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            _sourcePath = sourcePath ?? filePath;

            try
            {
                LoadSoundFromFile(filePath);
            }
            catch (Exception ex)
            {
                throw new SoundLoadException(_sourcePath, $"Failed to load sound from {filePath}", ex);
            }
        }

        /// <summary>
        /// Create sound from existing SoundEffect
        /// </summary>
        public ManagedSound(SoundEffect soundEffect, string sourcePath)
        {
            _soundEffect = soundEffect ?? throw new ArgumentNullException(nameof(soundEffect));
            _sourcePath = sourcePath ?? "Unknown";
        }

        #endregion

        #region Reference Counting

        public void AddReference()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ManagedSound));

            Interlocked.Increment(ref _referenceCount);
        }

        public void RemoveReference()
        {
            if (_disposed)
                return;

            var newCount = Interlocked.Decrement(ref _referenceCount);
            if (newCount <= 0)
            {
                Dispose();
            }
        }

        #endregion

        #region Playback

        public SoundEffectInstance Play()
        {
            if (_disposed || _soundEffect == null)
                return null;

            return _soundEffect.CreateInstance();
        }

        public SoundEffectInstance Play(float volume)
        {
            var instance = Play();
            if (instance != null)
            {
                instance.Volume = Math.Clamp(volume, 0.0f, 1.0f);
                instance.Play();
            }
            return instance;
        }

        public SoundEffectInstance Play(float volume, float pitch, float pan)
        {
            var instance = Play();
            if (instance != null)
            {
                instance.Volume = Math.Clamp(volume, 0.0f, 1.0f);
                instance.Pitch = Math.Clamp(pitch, -1.0f, 1.0f);
                instance.Pan = Math.Clamp(pan, -1.0f, 1.0f);
                instance.Play();
            }
            return instance;
        }

        public SoundEffectInstance CreateInstance()
        {
            if (_disposed || _soundEffect == null)
                return null;

            return _soundEffect.CreateInstance();
        }

        #endregion

        #region Private Methods

        private void LoadSoundFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Sound file not found: {filePath}");

            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            switch (extension)
            {
                case ".wav":
                    LoadWavFile(filePath);
                    break;
                case ".ogg":
                    LoadOggFile(filePath);
                    break;
                default:
                    throw new NotSupportedException($"Audio format not supported: {extension}");
            }
        }

        private void LoadWavFile(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            _soundEffect = SoundEffect.FromStream(stream);
        }

        private void LoadOggFile(string filePath)
        {
            using var vorbisReader = new VorbisReader(filePath);

            // Get audio properties
            var sampleRate = vorbisReader.SampleRate;
            var channels = vorbisReader.Channels;
            var totalSamples = (int)vorbisReader.TotalSamples;

            // Read all samples into a float array
            var floatBuffer = new float[totalSamples * channels];
            var samplesRead = vorbisReader.ReadSamples(floatBuffer, 0, floatBuffer.Length);

            // Convert float samples to 16-bit PCM
            var pcmData = new byte[samplesRead * 2]; // 16-bit = 2 bytes per sample
            for (int i = 0; i < samplesRead; i++)
            {
                // Clamp and convert float [-1.0, 1.0] to short [-32768, 32767]
                var sample = Math.Clamp(floatBuffer[i], -1.0f, 1.0f);
                var pcmSample = (short)(sample * 32767);

                // Write as little-endian 16-bit
                pcmData[i * 2] = (byte)(pcmSample & 0xFF);
                pcmData[i * 2 + 1] = (byte)((pcmSample >> 8) & 0xFF);
            }

            // Create SoundEffect from PCM data
            var audioChannels = channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo;
            _soundEffect = new SoundEffect(pcmData, sampleRate, audioChannels);
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
                    _soundEffect?.Dispose();
                    _soundEffect = null;
                }
                _disposed = true;
            }
        }

        #endregion
    }

    /// <summary>
    /// Exception thrown when sound loading fails
    /// </summary>
    public class SoundLoadException : Exception
    {
        public string SoundPath { get; }

        public SoundLoadException(string soundPath, string message) : base(message)
        {
            SoundPath = soundPath;
        }

        public SoundLoadException(string soundPath, string message, Exception innerException) : base(message, innerException)
        {
            SoundPath = soundPath;
        }
    }
}
