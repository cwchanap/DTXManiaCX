#nullable enable
using System;
using System.Collections.Generic;

namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// One recent-play history line parsed from an NX score.ini [File] section.
    /// </summary>
    public sealed class NxHistoryLine
    {
        public string Text { get; init; } = "";
        public DateTime Date { get; init; }
    }

    /// <summary>
    /// Drum-only projection of a DTXManiaNX &lt;chart&gt;.score.ini file. Holds only the
    /// fields the importer consumes. See docs/superpowers/specs/2026-06-08-nx-score-import-design.md.
    /// </summary>
    public sealed class NxScoreData
    {
        // Best-by-score ([HiScore.Drums])
        public int BestScore { get; set; }
        public int BestPerfect { get; set; }
        public int BestGreat { get; set; }
        public int BestGood { get; set; }
        public int BestPoor { get; set; }
        public int BestMiss { get; set; }
        public int BestMaxCombo { get; set; }
        public int TotalChips { get; set; }
        public double BestAchievementRate { get; set; } // [HiScore.Drums] PlaySkill

        // Best-by-skill ([HiSkill.Drums])
        public double HighSkill { get; set; } // [HiSkill.Drums] Skill

        // [File]
        public int PlayCount { get; set; }
        public int ClearCount { get; set; }
        public int BestRankOrdinal { get; set; } = 99; // NX ERANK; 99 == UNKNOWN

        // [LastPlay.Drums]
        public int LastScore { get; set; }
        public double LastSkill { get; set; }
        public DateTime? LastPlayedAt { get; set; }
        public string LastProgress { get; set; } = "";

        // Input flags (from [HiScore.Drums])
        public bool UsedKeyboard { get; set; }
        public bool UsedMidi { get; set; }
        public bool UsedJoypad { get; set; }
        public bool UsedMouse { get; set; }

        public IReadOnlyList<NxHistoryLine> History { get; set; } = Array.Empty<NxHistoryLine>();

        /// <summary>True when there is meaningful drum data worth importing.</summary>
        public bool HasDrumData => PlayCount > 0 || BestScore > 0;
    }
}
