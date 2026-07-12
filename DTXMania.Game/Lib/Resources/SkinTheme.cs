using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

#nullable enable

namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Per-skin theme values parsed from an optional Theme.ini at the skin root.
    /// Section headers ([Palette]/[Layout]/[Fonts]) only organize the file;
    /// lookup is by bare key, case-insensitive. Unknown keys and sections are
    /// ignored for forward compatibility. Parsing and loading never throw.
    /// </summary>
    public class SkinTheme : ISkinTheme
    {
        public static readonly SkinTheme Empty = new SkinTheme(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        /// <summary>File name looked up at the skin root.</summary>
        public const string ThemeFileName = "Theme.ini";

        private readonly Dictionary<string, string> _values;

        private SkinTheme(Dictionary<string, string> values)
        {
            _values = values;
        }

        /// <summary>
        /// Loads a theme file. Missing file or IO failure returns <see cref="Empty"/>.
        /// </summary>
        public static SkinTheme Load(string themeFilePath)
        {
            if (string.IsNullOrWhiteSpace(themeFilePath) || !File.Exists(themeFilePath))
                return Empty;

            try
            {
                return Parse(File.ReadAllLines(themeFilePath));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SkinTheme: Failed to read {themeFilePath}: {ex.Message}");
                return Empty;
            }
        }

        public static SkinTheme Parse(IEnumerable<string> lines)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("["))
                    continue;

                var separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                    continue;

                var key = line.Substring(0, separatorIndex).Trim();
                var value = line.Substring(separatorIndex + 1).Trim();
                values[key] = value;
            }
            return new SkinTheme(values);
        }

        public Color GetColor(string key, Color fallback)
        {
            if (!_values.TryGetValue(key, out var raw))
                return fallback;

            // #RRGGBB or #RRGGBBAA
            if (raw.Length != 7 && raw.Length != 9 || !raw.StartsWith("#"))
            {
                WarnMalformed(key, raw);
                return fallback;
            }

            if (!uint.TryParse(raw.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
            {
                WarnMalformed(key, raw);
                return fallback;
            }

            if (raw.Length == 7)
            {
                return new Color((int)((packed >> 16) & 0xFF), (int)((packed >> 8) & 0xFF), (int)(packed & 0xFF));
            }

            return new Color(
                (int)((packed >> 24) & 0xFF),
                (int)((packed >> 16) & 0xFF),
                (int)((packed >> 8) & 0xFF),
                (int)(packed & 0xFF));
        }

        public int GetInt(string key, int fallback)
        {
            if (!_values.TryGetValue(key, out var raw))
                return fallback;

            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return value;

            WarnMalformed(key, raw);
            return fallback;
        }

        public float GetFloat(string key, float fallback)
        {
            if (!_values.TryGetValue(key, out var raw))
                return fallback;

            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;

            WarnMalformed(key, raw);
            return fallback;
        }

        public Point GetPoint(string key, Point fallback)
        {
            if (!_values.TryGetValue(key, out var raw))
                return fallback;

            var parts = raw.Split(',');
            if (parts.Length == 2 &&
                int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) &&
                int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
            {
                return new Point(x, y);
            }

            WarnMalformed(key, raw);
            return fallback;
        }

        private static void WarnMalformed(string key, string raw)
        {
            Debug.WriteLine($"SkinTheme: Malformed value for '{key}': '{raw}' — using fallback");
        }
    }
}
