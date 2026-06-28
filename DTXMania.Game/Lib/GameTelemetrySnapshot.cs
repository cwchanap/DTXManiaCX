#nullable enable

using System;
using DTXMania.Game.Lib.Stage.Performance;

namespace DTXMania.Game.Lib;

public sealed class GameTelemetrySnapshot
{
    public string StageName { get; set; } = "Unknown";
    public string StageType { get; set; } = "Unknown";
    public string StagePhase { get; set; } = "Unknown";
    public bool IsTransitioning { get; set; }

    public string? SelectedSongTitle { get; set; }
    public int? SelectedDifficulty { get; set; }
    public bool? InStatusPanel { get; set; }
    public bool? ChartLoaded { get; set; }

    public bool? PerformanceReady { get; set; }
    public bool? AutoPlayEnabled { get; set; }
    public bool? StageCompleted { get; set; }
    public double? CurrentSongTimeMs { get; set; }
    public int? Score { get; set; }
    public int? CurrentCombo { get; set; }
    public int? MaxCombo { get; set; }
    public float? Gauge { get; set; }
    public bool? HasFailed { get; set; }

    public int? PerfectCount { get; set; }
    public int? GreatCount { get; set; }
    public int? GoodCount { get; set; }
    public int? PoorCount { get; set; }
    public int? MissCount { get; set; }
    public int? TotalNotes { get; set; }
    public int? LastLaneHitLane { get; set; }
    public string? LastLaneHitButtonId { get; set; }
    public double? LastLaneHitSongTimeMs { get; set; }
    public bool? ClearFlag { get; set; }
    public string? CompletionReason { get; set; }

    public int TotalJudgements =>
        (PerfectCount ?? 0) +
        (GreatCount ?? 0) +
        (GoodCount ?? 0) +
        (PoorCount ?? 0) +
        (MissCount ?? 0);

    public void ApplyPerformanceSummary(PerformanceSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        Score = summary.Score;
        MaxCombo = summary.MaxCombo;
        ClearFlag = summary.ClearFlag;
        PerfectCount = summary.PerfectCount;
        GreatCount = summary.GreatCount;
        GoodCount = summary.GoodCount;
        PoorCount = summary.PoorCount;
        MissCount = summary.MissCount;
        TotalNotes = summary.TotalNotes;
        Gauge = summary.FinalLife;
        CompletionReason = summary.CompletionReason.ToString();
    }
}
