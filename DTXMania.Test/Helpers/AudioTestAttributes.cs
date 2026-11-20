using System;
using Microsoft.Xna.Framework.Audio;
using Xunit;

namespace DTXMania.Test.Helpers
{
    /// <summary>
    /// Capability probe for tests that need a real playing
    /// <see cref="SoundEffectInstance"/>. The canonical suite runs under
    /// ALSOFT_DRIVERS=null, which provides a working OpenAL context; where no
    /// context can be created the dependent tests report Skipped instead of
    /// fabricating playback state.
    /// </summary>
    public static class AudioTestGate
    {
        public const string SkipReason =
            "Requires a working OpenAL context (e.g. ALSOFT_DRIVERS=null or a real audio device).";

        private static readonly Lazy<bool> s_isAvailable = new(ProbeAvailability);

        public static bool IsAvailable => s_isAvailable.Value;

        private static bool ProbeAvailability()
        {
            try
            {
                using var effect = PlayingSoundInstance.CreateEffect();
                using var instance = effect.CreateInstance();
                instance.Play();
                return instance.State == SoundState.Playing;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Fact for a real-audio test: Skipped (never passed) when the platform
    /// cannot produce a playing SoundEffectInstance.
    /// </summary>
    public sealed class AudioFactAttribute : FactAttribute
    {
        public AudioFactAttribute()
        {
            if (!AudioTestGate.IsAvailable)
            {
                Skip = AudioTestGate.SkipReason;
            }
        }
    }

    /// <summary>
    /// A real playing <see cref="SoundEffectInstance"/> over a silent buffer,
    /// together with the <see cref="SoundEffect"/> that owns its audio data.
    /// Disposing releases both.
    /// </summary>
    public sealed class PlayingSoundInstance : IDisposable
    {
        // One second of mono PCM16 silence: a real source needs a real buffer,
        // and the duration outlives any test assertion window.
        private static readonly byte[] s_silencePcm = new byte[88200];

        private readonly SoundEffect _effect;

        private PlayingSoundInstance(SoundEffect effect, SoundEffectInstance instance)
        {
            _effect = effect;
            Instance = instance;
        }

        public SoundEffectInstance Instance { get; }

        internal static SoundEffect CreateEffect()
            => new(s_silencePcm, 44100, AudioChannels.Mono);

        /// <summary>
        /// Creates a real playing instance. Throws (rather than faking state)
        /// when the platform cannot produce one — callers should be gated by
        /// <see cref="AudioFactAttribute"/>.
        /// </summary>
        public static PlayingSoundInstance Create()
        {
            var effect = CreateEffect();
            try
            {
                var instance = effect.CreateInstance();
                instance.Play();
                if (instance.State != SoundState.Playing)
                {
                    instance.Dispose();
                    throw new InvalidOperationException(
                        "SoundEffectInstance did not reach the Playing state.");
                }
                return new PlayingSoundInstance(effect, instance);
            }
            catch
            {
                effect.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Instance.Dispose();
            _effect.Dispose();
        }
    }
}
