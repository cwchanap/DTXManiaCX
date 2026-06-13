#nullable enable
using System.Text.RegularExpressions;

namespace DTXMania.Game.Lib.Song
{
    internal static class PlayHistoryLineFormatter
    {
        private static readonly Regex FailedWithScorePattern = new(
            @"\bFailed(?=\s+\([A-Z]{1,2}:\s*[0-9]+(?:\.[0-9]+)?\))",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string Normalize(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return line;

            return FailedWithScorePattern.Replace(line, "Cleared", 1);
        }
    }
}
