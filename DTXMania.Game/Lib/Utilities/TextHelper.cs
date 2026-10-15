using System;
using System.Globalization;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;

namespace DTXMania.Game.Lib.Utilities
{
    /// <summary>
    /// Text layout helpers shared across rendering components. Consolidates the binary-search
    /// ellipsis truncation that was previously duplicated in SongListDisplay (artist names) and
    /// ConfigStage (right-aligned item values).
    /// </summary>
    public static class TextHelper
    {
        /// <summary>
        /// Truncate text to fit within <paramref name="maxWidth"/> using binary search, appending
        /// an ellipsis ("...") when truncation is needed. Returns the original text unchanged when
        /// it already fits or when the inputs are null/empty.
        /// </summary>
        /// <param name="text">Text to truncate.</param>
        /// <param name="maxWidth">Maximum rendered width in pixels.</param>
        /// <param name="font">MonoGame SpriteFont used to measure the text.</param>
        /// <returns>The original text if it fits, otherwise the longest prefix + "..." that fits.</returns>
        public static string TruncateToWidth(string text, float maxWidth, SpriteFont font)
        {
            if (string.IsNullOrEmpty(text) || font == null)
                return text;

            if (font.MeasureString(text).X <= maxWidth)
                return text;

            int[] elementStarts = StringInfo.ParseCombiningCharacters(text);
            int elementCount = elementStarts.Length;
            int left = 0;
            int right = elementCount;
            string bestFit = string.Empty;

            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                int charIndex = mid < elementCount ? elementStarts[mid] : text.Length;
                string candidate = text.Substring(0, charIndex) + "...";

                if (font.MeasureString(candidate).X <= maxWidth)
                {
                    bestFit = candidate;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }

            return bestFit;
        }

        /// <summary>
        /// Truncate text to fit within <paramref name="maxWidth"/> using binary search, appending
        /// an ellipsis ("...") when truncation is needed. Returns the original text unchanged when
        /// it already fits or when the inputs are null/empty.
        /// </summary>
        /// <param name="text">Text to truncate.</param>
        /// <param name="maxWidth">Maximum rendered width in pixels.</param>
        /// <param name="font">IFont used to measure the text.</param>
        /// <returns>The original text if it fits, otherwise the longest prefix + "..." that fits.</returns>
        public static string TruncateToWidth(string text, float maxWidth, IFont font)
        {
            if (string.IsNullOrEmpty(text) || font == null)
                return text;

            if (font.MeasureString(text).X <= maxWidth)
                return text;

            int[] elementStarts = StringInfo.ParseCombiningCharacters(text);
            int elementCount = elementStarts.Length;
            int left = 0;
            int right = elementCount;
            string bestFit = string.Empty;

            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                int charIndex = mid < elementCount ? elementStarts[mid] : text.Length;
                string candidate = text.Substring(0, charIndex) + "...";

                if (font.MeasureString(candidate).X <= maxWidth)
                {
                    bestFit = candidate;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }

            return bestFit;
        }

        /// <summary>
        /// Shorten a filesystem path to fit within <paramref name="maxWidth"/> while keeping its
        /// distinguishing tail visible: the root is kept and leading directories collapse into
        /// "..." (e.g. <c>C:\...\Games\SongsA</c>). When even the last segment alone is too wide,
        /// its start is ellipsized instead (<c>...ongsA</c>). Returns the original path unchanged
        /// when it already fits or when the inputs are null/empty.
        /// </summary>
        /// <param name="path">Path to shorten.</param>
        /// <param name="maxWidth">Maximum rendered width in pixels.</param>
        /// <param name="font">IFont used to measure the text.</param>
        public static string TruncatePathToWidth(string path, float maxWidth, IFont font)
        {
            if (string.IsNullOrEmpty(path) || font == null)
                return path;

            if (font.MeasureString(path).X <= maxWidth)
                return path;

            var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
            var lastSeparatorIndex = path.LastIndexOfAny(separators);
            var separator = lastSeparatorIndex >= 0 ? path[lastSeparatorIndex] : Path.DirectorySeparatorChar;
            var root = Path.GetPathRoot(path) ?? string.Empty;
            var segments = path.Substring(root.Length)
                .Split(separators, StringSplitOptions.RemoveEmptyEntries);

            string bestFit = null;
            var tail = string.Empty;
            for (var index = segments.Length - 1; index >= 0; index--)
            {
                tail = tail.Length == 0 ? segments[index] : segments[index] + separator + tail;
                var candidate = root + "..." + separator + tail;
                if (font.MeasureString(candidate).X > maxWidth)
                    break;

                bestFit = candidate;
            }

            if (bestFit != null)
                return bestFit;

            // The last segment alone is too wide: keep the longest suffix of it that fits.
            var lastSegment = segments.Length > 0 ? segments[^1] : path;
            int[] elementStarts = StringInfo.ParseCombiningCharacters(lastSegment);
            int left = 1;
            int right = elementStarts.Length;
            var suffixFit = string.Empty;
            while (left <= right)
            {
                int dropped = left + (right - left) / 2;
                int charIndex = dropped < elementStarts.Length ? elementStarts[dropped] : lastSegment.Length;
                var candidate = "..." + lastSegment.Substring(charIndex);
                if (font.MeasureString(candidate).X <= maxWidth)
                {
                    suffixFit = candidate;
                    right = dropped - 1;
                }
                else
                {
                    left = dropped + 1;
                }
            }

            return suffixFit;
        }
    }
}
