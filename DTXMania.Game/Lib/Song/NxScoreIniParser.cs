#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// Parses a DTXManiaNX &lt;chart&gt;.score.ini (Shift-JIS) into the drum-only
    /// <see cref="NxScoreData"/>. Returns null when the file is absent or
    /// has no drum data. Throws on I/O or decode failures so the caller can
    /// distinguish them from a legitimate absent-file skip.
    /// Only ASCII-valued fields are consumed, so a non-ASCII Title is
    /// never decoded/depended upon.
    /// </summary>
    public static class NxScoreIniParser
    {
        private static int _encodingProviderRegistered;

        public static NxScoreData? Parse(string scoreIniPath)
        {
            if (string.IsNullOrEmpty(scoreIniPath) || !File.Exists(scoreIniPath))
                return null;

            // Let I/O and decode exceptions propagate so the caller can count them as errors
            // rather than silently treating them as "no data" (Skipped).
            var sections = ReadSections(scoreIniPath);

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

            if (Interlocked.CompareExchange(ref _encodingProviderRegistered, 1, 0) == 0)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
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

            // NX writes timestamps via DateTime.Now.ToString() (no format specifier), so the
            // output depends on the Windows user locale.  Try known NX locales FIRST because
            // the file was written by NX under one of these locales.  This avoids current-culture
            // preemption: if CX runs under an MM/dd culture (e.g. en-US) but the NX file was
            // written under a dd/MM locale (e.g. en-GB), an ambiguous date like "05/06/2026"
            // must resolve as dd/MM (June 5), not MM/dd (May 6).  CurrentCulture is tried after
            // known NX locales to cover unusual locales not in the list.  InvariantCulture is
            // the last resort.  A null return means "no parsable date".
            foreach (var culture in _knownNxCultures)
            {
                if (DateTime.TryParse(value, culture, DateTimeStyles.None, out var dt))
                    return dt;
            }

            // CurrentCulture covers locales not in the known list (e.g. pt-BR, pl-PL) where
            // the user imported their own NX files written under that same locale.
            if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt2))
                return dt2;

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt3))
                return dt3;

            return null;
        }

        // Pre-resolved cultures for common DTXManiaNX Windows locales.  Cached as static
        // fields to avoid repeated allocation on every parse call.
        private static readonly CultureInfo[] _knownNxCultures =
        {
            CultureInfo.GetCultureInfo("de-DE"),  // German:   dd.MM.yyyy HH:mm:ss
            CultureInfo.GetCultureInfo("en-GB"),  // British:  dd/MM/yyyy HH:mm:ss
            CultureInfo.GetCultureInfo("ja-JP"),  // Japanese: yyyy/MM/dd HH:mm:ss
            CultureInfo.GetCultureInfo("fr-FR"),  // French:   dd/MM/yyyy HH:mm:ss
            CultureInfo.GetCultureInfo("it-IT"),  // Italian:  dd/MM/yyyy HH:mm:ss
            CultureInfo.GetCultureInfo("es-ES"),  // Spanish:  dd/MM/yyyy HH:mm:ss
            CultureInfo.GetCultureInfo("ko-KR"),  // Korean:   yyyy-MM-dd HH:mm:ss
            CultureInfo.GetCultureInfo("zh-CN"),  // Chinese:  yyyy/M/d H:mm:ss
        };

        private static IReadOnlyList<NxHistoryLine> ParseHistory(Dictionary<string, string> file)
        {
            var list = new List<NxHistoryLine>();
            for (int i = 0; i < 5; i++)
            {
                var text = GetString(file, $"History{i}");
                if (string.IsNullOrWhiteSpace(text)) continue;
                // Preserve the imported NX row text verbatim (design goal #3 of the
                // play-history badge spec). NX legitimately writes "Failed" alongside a
                // grade/skill for runs where the life gauge hit zero, so the status must
                // not be rewritten on import.
                list.Add(new NxHistoryLine
                {
                    Text = text,
                    Date = ParseHistoryDate(text)
                });
            }
            return list;
        }

        // Format: "{playIndex}.{yy}/{m}/{d} {status} ({rank}: {skill})"
        // e.g. "79.26/5/15 Cleared (S: 94.37)" -> 2026-05-15
        // Unparseable dates return DateTime.MinValue, which sorts below every real
        // date and is therefore effectively excluded from the top-5 recent display.
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
                // Assumes 21st century; NX score.ini uses 2-digit years and the NX
                // lineage is a 2000s-era product, so this is valid for 2000-2099.
                return new DateTime(2000 + yy, m, d);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }
    }
}
