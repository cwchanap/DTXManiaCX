using Microsoft.Xna.Framework;

#nullable enable

namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Read access to the active skin's optional Theme.ini values.
    /// Every getter returns the caller-supplied fallback when the key is absent
    /// or malformed, so skins without a theme file behave identically to the
    /// built-in (NX) defaults.
    /// </summary>
    public interface ISkinTheme
    {
        Color GetColor(string key, Color fallback);
        int GetInt(string key, int fallback);
        float GetFloat(string key, float fallback);
        Point GetPoint(string key, Point fallback);
    }
}
