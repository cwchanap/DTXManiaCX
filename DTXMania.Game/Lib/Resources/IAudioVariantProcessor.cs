#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Stage.Performance;

namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Prepares non-default playback audio as normalized signed 16-bit PCM.
    /// </summary>
    public interface IAudioVariantProcessor
    {
        Task<PreparedAudioArtifact> PrepareAsync(
            string sourcePath,
            PlaybackModifiers modifiers,
            CancellationToken cancellationToken);
    }

    public enum AudioVariantPreparationFailure
    {
        DefaultProfile,
        SourceNotFound,
        UnsupportedFormat,
        RuntimeUnavailable,
        DecodeFailed,
        ProbeFailed,
        TransformFailed,
        TimedOut,
        InvalidOutput,
    }

    /// <summary>
    /// A preparation failure carrying the source and immutable requested profile.
    /// Caller-requested cancellation remains an OperationCanceledException.
    /// </summary>
    public sealed class AudioVariantPreparationException : Exception
    {
        public AudioVariantPreparationException(
            AudioVariantPreparationFailure failure,
            string sourcePath,
            PlaybackModifiers modifiers,
            string message,
            Exception? innerException = null)
            : base(message, innerException)
        {
            Failure = failure;
            SourcePath = sourcePath;
            Modifiers = modifiers;
        }

        public AudioVariantPreparationFailure Failure { get; }

        public string SourcePath { get; }

        public PlaybackModifiers Modifiers { get; }
    }
}