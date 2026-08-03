#nullable enable

using System;

namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// Thrown when song enumeration is requested while another enumeration is
    /// already in progress. Derives from <see cref="InvalidOperationException"/>
    /// so existing handlers that catch <c>InvalidOperationException</c> continue
    /// to classify this condition, while callers that need to distinguish a busy
    /// enumeration from other invalid-operation failures can catch this type
    /// directly instead of string-matching the message.
    /// </summary>
    public sealed class SongEnumerationBusyException : InvalidOperationException
    {
        public SongEnumerationBusyException()
            : base("Song enumeration is already in progress.")
        {
        }

        public SongEnumerationBusyException(string message)
            : base(message)
        {
        }

        public SongEnumerationBusyException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
