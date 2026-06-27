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

            int left = 0;
            int right = text.Length;
            string bestFit = string.Empty;

            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                string candidate = text.Substring(0, mid) + "...";

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

            int left = 0;
            int right = text.Length;
            string bestFit = string.Empty;

            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                string candidate = text.Substring(0, mid) + "...";

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
    }
}
