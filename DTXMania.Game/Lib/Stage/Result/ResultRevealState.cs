#nullable enable

using System;

namespace DTXMania.Game.Lib.Stage.Result
{
    public sealed class ResultRevealState
    {
        public const double RankRevealSeconds = 0.5;
        public const double PanelDelaySeconds = 0.15;
        public const double PanelRevealSeconds = 0.5;
        public const double TotalRevealSeconds = RankRevealSeconds + PanelDelaySeconds + PanelRevealSeconds;

        public double ElapsedSeconds { get; private set; }

        public bool IsComplete => ElapsedSeconds >= TotalRevealSeconds;

        public float RankProgress => Clamp01(ElapsedSeconds / RankRevealSeconds);

        public float PanelProgress => Clamp01(
            (ElapsedSeconds - RankRevealSeconds - PanelDelaySeconds) / PanelRevealSeconds);

        public void Update(double deltaSeconds)
        {
            if (deltaSeconds <= 0.0)
                return;

            ElapsedSeconds = Math.Min(TotalRevealSeconds, ElapsedSeconds + deltaSeconds);
        }

        public void Complete()
        {
            ElapsedSeconds = TotalRevealSeconds;
        }

        public void Reset()
        {
            ElapsedSeconds = 0.0;
        }

        private static float Clamp01(double value)
        {
            if (value <= 0.0)
                return 0.0f;

            if (value >= 1.0)
                return 1.0f;

            return (float)value;
        }
    }
}
