#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// Parses a DTXManiaNX &lt;chart&gt;.score.ini (Shift-JIS) into the drum-only
    /// <see cref="NxScoreData"/>. Returns null when the file is absent, unreadable, or
    /// has no drum data. Only ASCII-valued fields are consumed, so a non-ASCII Title is
    /// never decoded/depended upon.
    /// </summary>
    public static class NxScoreIniParser
    {
        private static bool _encodingProviderRegistered;

        public static NxScoreData? Parse(string scoreIniPath)
        {
            if (string.IsNullOrEmpty(scoreIniPath) || !File.Exists(scoreIniPath))
                return null;

            Dictionary<string, Dictionary<string, string>> sections;
            try
            {
                sections = ReadSections(scoreIniPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NxScoreIniParser: failed to read {scoreIniPath}: {ex.Message}");
                return null;
            }

            var file = Section(sections, "File");
            var hiScore = Section(sections, "HiScore.Drums");
            var hiSkill = Section(sections, "HiSkill.Drums");
            var lastPlay = Section(sections, "LastPlay.Drums");

            var data = new NxScoreData
            {
                BestScore = GetInt(hiScore, "Score"),
                BestPerfect = GetInt(hiScore, "Perfect"),
                BestGreat = GetInt(hiScore, "Great"),
                BestGood = GetInt(hiScore, "Good"),
                BestPoor = GetInt(hiScore, "Poor"),
                BestMiss = GetInt(hiScore, "Miss"),
                BestMaxCombo = GetInt(hiScore, "MaxCombo"),
                TotalChips = GetInt(hiScore, "TotalChips"),
                BestAchievementRate = GetDouble(hiScore, "PlaySkill"),
                HighSkill = GetDouble(hiSkill, "Skill"),
                PlayCount = GetInt(file, "PlayCountDrums"),
                ClearCount = GetInt(file, "ClearCountDrums"),
                BestRankOrdinal = GetInt(file, "BestRankDrums", 99),
                LastScore = GetInt(lastPlay, "Score"),
                LastSkill = GetDouble(lastPlay, "Skill"),
                LastPlayedAt = ParseDateTime(GetString(lastPlay, "DateTime")),
                LastProgress = GetString(lastPlay, "Progress"),
                UsedKeyboard = GetInt(hiScore, "UseKeyboard") != 0,
                UsedMidi = GetInt(hiScore, "UseMIDIIN") != 0,
                UsedJoypad = GetInt(hiScore, "UseJoypad") != 0,
                UsedMouse = GetInt(hiScore, "UseMouse") != 0,
                History = ParseHistory(file),
            };

            return data.HasDrumData ? data : null;
        }

        private static Dictionary<string, Dictionary<string, string>> ReadSections(string path)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string>? current = null;

            if (!_encodingProviderRegistered)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                _encodingProviderRegistered = true;
            }
            // Shift-JIS: every consumed field is ASCII, so even a decode hiccup on Title is harmless.
            var encoding = Encoding.GetEncoding("Shift_JIS");
            foreach (var raw in File.ReadAllLines(path, encoding))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line[0] == '[' && line[^1] == ']')
                {
                    var name = line.Substring(1, line.Length - 2).Trim();
                    if (!result.TryGetValue(name, out current))
                    {
                        current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        result[name] = current;
                    }
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0 || current == null) continue;
                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();
                current[key] = value; // last value wins
            }
            return result;
        }

        private static Dictionary<string, string> Section(
            Dictionary<string, Dictionary<string, string>> sections, string name)
            => sections.TryGetValue(name, out var s) ? s : new Dictionary<string, string>();

        private static int GetInt(Dictionary<string, string> s, string key, int fallback = 0)
            => s.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;

        private static double GetDouble(Dictionary<string, string> s, string key)
            => s.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0.0;

        private static string GetString(Dictionary<string, string> s, string key)
            => s.TryGetValue(key, out var v) ? v : "";

        private static DateTime? ParseDateTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt : (DateTime?)null;
        }

        private static IReadOnlyList<NxHistoryLine> ParseHistory(Dictionary<string, string> file)
        {
            var list = new List<NxHistoryLine>();
            for (int i = 0; i < 5; i++)
            {
                var text = GetString(file, $"History{i}");
                if (string.IsNullOrWhiteSpace(text)) continue;
                list.Add(new NxHistoryLine { Text = text, Date = ParseHistoryDate(text) });
            }
            return list;
        }

        // Format: "{playIndex}.{yy}/{m}/{d} {status} ({rank}: {skill})"
        // e.g. "79.26/5/15 Cleared (S: 94.37)" -> 2026-05-15
        private static DateTime ParseHistoryDate(string text)
        {
            try
            {
                int dot = text.IndexOf('.');
                if (dot < 0) return DateTime.MinValue;
                var afterDot = text.Substring(dot + 1);
                int space = afterDot.IndexOf(' ');
                var dateToken = (space < 0 ? afterDot : afterDot.Substring(0, space)).Trim();
                var parts = dateToken.Split('/');
                if (parts.Length != 3) return DateTime.MinValue;
                int yy = int.Parse(parts[0], CultureInfo.InvariantCulture);
                int m = int.Parse(parts[1], CultureInfo.InvariantCulture);
                int d = int.Parse(parts[2], CultureInfo.InvariantCulture);
                return new DateTime(2000 + yy, m, d);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }
    }
}
