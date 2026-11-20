#nullable enable

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>Immutable gameplay values shared by the Lanes and Tab HUDs.</summary>
    public readonly struct PerformanceFrameState
    {
        public int Score { get; init; }
        public int Combo { get; init; }
        public int MaxCombo { get; init; }
        public float Life { get; init; }
        public bool IsDanger { get; init; }
        public double Achievement { get; init; }
        public bool IsMaxSkill { get; init; }
        public int PerfectCount { get; init; }
        public int GreatCount { get; init; }
        public int GoodCount { get; init; }
        public int PoorCount { get; init; }
        public int MissCount { get; init; }
        public int FastCount { get; init; }
        public int SlowCount { get; init; }
        public float Progress { get; init; }
        public double ElapsedMs { get; init; }
        public double TotalMs { get; init; }
    }
}
