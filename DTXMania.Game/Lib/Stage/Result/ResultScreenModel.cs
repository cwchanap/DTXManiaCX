#nullable enable

using System;
using System.Globalization;
using System.IO;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;

namespace DTXMania.Game.Lib.Stage.Result
{
    public enum ResultRank
    {
        SS = 0,
        S = 1,
        A = 2,
        B = 3,
        C = 4,
        D = 5,
        E = 6
    }

    public enum ResultPlateKind
    {
        Excellent,
        FullCombo,
        StageCleared,
        Failed
    }

    public sealed class ResultScreenModel
    {
        private const string UnknownSongTitle = "Unknown Song";
        private const string UnknownArtistName = "Unknown Artist";

        private ResultScreenModel()
        {
        }

        public PerformanceSummary Summary { get; private init; } = new();
        public ResultRank Rank { get; private init; }
        public string RankLabel { get; private init; } = "E";
        public ResultPlateKind PlateKind { get; private init; }
        public string ScoreText { get; private init; } = "0000000";
        public string MaxComboText { get; private init; } = "0";
        public string PerfectCountText { get; private init; } = "0";
        public string GreatCountText { get; private init; } = "0";
        public string GoodCountText { get; private init; } = "0";
        public string PoorCountText { get; private init; } = "0";
        public string MissCountText { get; private init; } = "0";
        public string PerfectPercentText { get; private init; } = "0%";
        public string GreatPercentText { get; private init; } = "0%";
        public string GoodPercentText { get; private init; } = "0%";
        public string PoorPercentText { get; private init; } = "0%";
        public string MissPercentText { get; private init; } = "0%";
        public string MaxComboPercentText { get; private init; } = "0%";
        public string PlayingSkillText { get; private init; } = "0.00";
        public string GameSkillText { get; private init; } = "0.00";
        public string ChartLevelText { get; private init; } = "--";
        public string Title { get; private init; } = UnknownSongTitle;
        public string Artist { get; private init; } = UnknownArtistName;
        public string PreviewImagePath { get; private init; } = TexturePath.ResultDefaultPreview;
        public bool NewRecord { get; private init; }

        public static ResultScreenModel Create(
            PerformanceSummary? summary,
            SongListNode? selectedSong,
            int selectedDifficulty,
            SongChart? chart,
            SongScore? previousScore)
        {
            var safeSummary = summary ?? new PerformanceSummary();
            var resolvedChart = ResolveChart(selectedSong, selectedDifficulty, chart);
            var rank = ComputeRank(safeSummary.PlayingSkill);

            return new ResultScreenModel
            {
                Summary = safeSummary,
                Rank = rank,
                RankLabel = GetRankLabel(rank),
                PlateKind = ComputePlateKind(safeSummary),
                ScoreText = FormatScore(safeSummary.Score),
                MaxComboText = FormatCount(safeSummary.MaxCombo),
                PerfectCountText = FormatCount(safeSummary.PerfectCount),
                GreatCountText = FormatCount(safeSummary.GreatCount),
                GoodCountText = FormatCount(safeSummary.GoodCount),
                PoorCountText = FormatCount(safeSummary.PoorCount),
                MissCountText = FormatCount(safeSummary.MissCount),
                PerfectPercentText = FormatPercent(safeSummary.PerfectCount, safeSummary.TotalNotes),
                GreatPercentText = FormatPercent(safeSummary.GreatCount, safeSummary.TotalNotes),
                GoodPercentText = FormatPercent(safeSummary.GoodCount, safeSummary.TotalNotes),
                PoorPercentText = FormatPercent(safeSummary.PoorCount, safeSummary.TotalNotes),
                MissPercentText = FormatPercent(safeSummary.MissCount, safeSummary.TotalNotes),
                MaxComboPercentText = FormatPercent(safeSummary.MaxCombo, safeSummary.TotalNotes),
                PlayingSkillText = FormatDecimal(safeSummary.PlayingSkill),
                GameSkillText = FormatDecimal(safeSummary.GameSkill),
                ChartLevelText = FormatLevel(
                    resolvedChart?.DrumLevel ?? safeSummary.ChartLevel,
                    resolvedChart?.DrumLevelDec ?? safeSummary.ChartLevelDec),
                Title = ResolveTitle(selectedSong),
                Artist = ResolveArtist(selectedSong),
                PreviewImagePath = ResolvePreviewImagePath(resolvedChart),
                NewRecord = IsNewRecord(safeSummary, previousScore)
            };
        }

        public static ResultRank ComputeRank(double playingSkill)
        {
            if (playingSkill >= 95.0) return ResultRank.SS;
            if (playingSkill >= 80.0) return ResultRank.S;
            if (playingSkill >= 73.0) return ResultRank.A;
            if (playingSkill >= 63.0) return ResultRank.B;
            if (playingSkill >= 53.0) return ResultRank.C;
            if (playingSkill >= 45.0) return ResultRank.D;
            return ResultRank.E;
        }

        public static ResultPlateKind ComputePlateKind(PerformanceSummary summary)
        {
            if (IsExcellent(summary))
                return ResultPlateKind.Excellent;

            if (summary.ClearFlag && Math.Max(0, summary.PoorCount) == 0 && Math.Max(0, summary.MissCount) == 0)
                return ResultPlateKind.FullCombo;

            if (summary.ClearFlag)
                return ResultPlateKind.StageCleared;

            return ResultPlateKind.Failed;
        }

        public static string GetRankLabel(ResultRank rank)
        {
            return rank switch
            {
                ResultRank.SS => "SS",
                ResultRank.S => "S",
                ResultRank.A => "A",
                ResultRank.B => "B",
                ResultRank.C => "C",
                ResultRank.D => "D",
                ResultRank.E => "E",
                _ => "E"
            };
        }

        public static string FormatScore(int score)
        {
            return Math.Clamp(score, 0, 9_999_999).ToString("0000000", CultureInfo.InvariantCulture);
        }

        public static string FormatCount(int count)
        {
            return Math.Max(0, count).ToString(CultureInfo.InvariantCulture);
        }

        public static string FormatPercent(int value, int total)
        {
            if (total <= 0)
                return "0%";

            var safeValue = Math.Max(0, value);
            var percent = (int)Math.Round(safeValue * 100.0 / total, MidpointRounding.AwayFromZero);
            return $"{Math.Clamp(percent, 0, 100)}%";
        }

        public static string FormatDecimal(double value)
        {
            return Math.Max(0.0, value).ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Formats a DTX difficulty level using its two encoding schemes:
        /// <list type="bullet">
        ///   <item>level >= 100: hundredths encoding (e.g. 120 → 1.20)</item>
        ///   <item>level &lt; 100: tenths from level + hundredths from levelDec (e.g. level=78, levelDec=33 → 8.13)</item>
        /// </list>
        /// Returns "--" when level <= 0. Uses InvariantCulture for consistent formatting.
        /// </summary>
        public static string FormatLevel(int level, int levelDec)
        {
            if (level <= 0)
                return "--";

            var actualLevel = level >= 100
                ? level / 100.0
                : (level / 10.0) + (Math.Max(0, levelDec) / 100.0);

            return actualLevel.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static bool IsExcellent(PerformanceSummary summary)
        {
            return summary.TotalNotes > 0
                && Math.Max(0, summary.PerfectCount) == summary.TotalNotes
                && Math.Max(0, summary.GreatCount) == 0
                && Math.Max(0, summary.GoodCount) == 0
                && Math.Max(0, summary.PoorCount) == 0
                && Math.Max(0, summary.MissCount) == 0;
        }

        private static SongChart? ResolveChart(SongListNode? selectedSong, int selectedDifficulty, SongChart? chart)
        {
            if (chart != null)
                return chart;

            if (selectedSong == null)
                return null;

            return selectedSong.GetCurrentDifficultyChart(selectedDifficulty) ?? selectedSong.DatabaseChart;
        }

        private static string ResolveTitle(SongListNode? selectedSong)
        {
            if (selectedSong == null)
                return UnknownSongTitle;

            var databaseSong = selectedSong.DatabaseSong;
            if (databaseSong != null && !string.IsNullOrWhiteSpace(databaseSong.Title))
                return databaseSong.Title;

            if (HasText(selectedSong.Title))
                return selectedSong.Title;

            if (databaseSong != null && HasMeaningfulTitle(databaseSong.DisplayTitle))
                return databaseSong.DisplayTitle;

            if (HasMeaningfulTitle(selectedSong.DisplayTitle))
                return selectedSong.DisplayTitle;

            return UnknownSongTitle;
        }

        private static string ResolveArtist(SongListNode? selectedSong)
        {
            var artist = selectedSong?.DatabaseSong?.Artist;
            if (!string.IsNullOrWhiteSpace(artist))
                return artist;

            return UnknownArtistName;
        }

        private static string ResolvePreviewImagePath(SongChart? chart)
        {
            if (chart == null || !HasText(chart.PreviewImage))
                return TexturePath.ResultDefaultPreview;

            var previewImagePath = NormalizePreviewImagePath(chart.PreviewImage);

            if (IsRootedPreviewImagePath(previewImagePath))
                return previewImagePath;

            var chartDirectory = HasText(chart.FilePath)
                ? Path.GetDirectoryName(chart.FilePath)
                : null;

            return HasText(chartDirectory)
                ? Path.Combine(chartDirectory!, previewImagePath)
                : previewImagePath;
        }

        private static bool IsNewRecord(PerformanceSummary summary, SongScore? previousScore)
        {
            if (previousScore == null)
                return false;

            if (!previousScore.HasBeenPlayed)
                return summary.Score > 0 || summary.GameSkill > 0.0;

            return summary.Score > previousScore.BestScore
                || summary.GameSkill > previousScore.HighSkill;
        }

        private static bool HasMeaningfulTitle(string? value)
        {
            return HasText(value)
                && !string.Equals(value, "Unknown", StringComparison.Ordinal)
                && !string.Equals(value, UnknownSongTitle, StringComparison.Ordinal);
        }

        private static bool HasText(string? value)
        {
            return !string.IsNullOrWhiteSpace(value);
        }

        private static string NormalizePreviewImagePath(string previewImagePath)
        {
            return previewImagePath.Replace('\\', Path.DirectorySeparatorChar);
        }

        private static bool IsRootedPreviewImagePath(string previewImagePath)
        {
            return Path.IsPathRooted(previewImagePath) || IsWindowsAbsolutePath(previewImagePath);
        }

        private static bool IsWindowsAbsolutePath(string previewImagePath)
        {
            return previewImagePath.Length >= 3
                && char.IsLetter(previewImagePath[0])
                && previewImagePath[1] == ':'
                && (previewImagePath[2] == '/' || previewImagePath[2] == '\\');
        }
    }
}
