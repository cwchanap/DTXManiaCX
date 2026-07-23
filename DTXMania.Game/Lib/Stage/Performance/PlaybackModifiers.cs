using System;
using DTXMania.Game.Lib.Config;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Immutable gameplay-speed and pitch profile captured for one performance.
    /// </summary>
    public readonly record struct PlaybackModifiers(
        int PlaySpeedPercent,
        int PitchSemitones)
    {
        public double Speed => PlaySpeedPercent / 100.0;

        public double PitchFactor => Math.Pow(2.0, PitchSemitones / 12.0);

        public double FfmpegTempoFactor => Speed / PitchFactor;

        public float MonoGamePitch => PitchSemitones / 12.0f;

        public bool IsDefault =>
            PlaySpeedPercent == PlaySpeedRange.Default &&
            PitchSemitones == PitchRange.Default;
    }
}