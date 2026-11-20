#nullable enable

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>Immutable gameplay values shared by the HUDs and stage telemetry.</summary>
    public readonly struct PerformanceFrameState
    {
        public int Score { get; init; }
        public int Combo { get; init; }
        public int MaxCombo { get; init; }
        public float Life { get; init; }
        /// <summary>Raw life for telemetry; unlike normalized Life, defaults to zero without a gauge.</summary>
        public float Gauge { get; init; }
        public bool HasFailed { get; init; }
        public bool IsDanger { get; init; }
        public double Achievement { get; init; }
        public bool IsMaxSkill { get; init; }
        public int PerfectCount { get; init; }
        public int GreatCount { get; init; }
        public int GoodCount { get; init; }
        public int PoorCount { get; init; }
        public int MissCount { get; init; }
        // Runtime note statuses differ from HUD events during failure and late manual misses.
        public int StatusPerfectCount { get; init; }
        public int StatusGreatCount { get; init; }
        public int StatusGoodCount { get; init; }
        public int StatusPoorCount { get; init; }
        public int StatusMissCount { get; init; }
        public int TotalNotes { get; init; }
        public int FastCount { get; init; }
        public int SlowCount { get; init; }
        public float Progress { get; init; }
        public double ElapsedMs { get; init; }
        public double TotalMs { get; init; }
    }
}
