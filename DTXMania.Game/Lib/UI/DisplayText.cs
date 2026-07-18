namespace DTXMania.Game.Lib.UI
{
    /// <summary>
    /// Helpers for text drawn in a skin's display face.
    ///
    /// Display faces (Orbitron and friends) ship as Latin-only SpriteFonts with a
    /// '*' default character, so every draw site that offers one must fall back to
    /// the CJK-capable serif when the string steps outside ASCII.
    /// </summary>
    internal static class DisplayText
    {
        /// <summary>
        /// True when every character is printable ASCII, i.e. the text can be
        /// rendered by a Latin-only display SpriteFont without glyph fallback.
        /// </summary>
        internal static bool IsAsciiDisplayable(string text)
        {
            foreach (var ch in text)
            {
                if (ch < 0x20 || ch > 0x7E)
                    return false;
            }
            return true;
        }
    }
}
