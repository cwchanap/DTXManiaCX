#nullable enable

namespace DTXMania.Game.Lib.Config
{
    /// <summary>
    /// Composes the invariant user-facing playback-profile label shared by the
    /// song-selection, song-transition, and result screens. Callers are
    /// responsible for snapping/clamping the raw config values before formatting
    /// so the label matches gameplay and telemetry.
    /// </summary>
    public static class PlaybackProfileFormatter
    {
        public static string Format(int playSpeedPercent, int pitchSemitones)
            => $"PLAY {PlaySpeedRange.Format(playSpeedPercent)} · PITCH {PitchRange.Format(pitchSemitones)}";
    }
}
