#nullable enable

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Config;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>Frozen gameplay policy for one performance run.</summary>
    internal sealed class GameplayRules
    {
        internal GameplayRules(ConfigData? config)
        {
            AutoPlayLanes = (config?.AutoPlayLanes ?? Enumerable.Empty<int>()).ToFrozenSet();
            AutoAddGauge = config?.AutoAddGauge ?? true;
            DamageLevel = config?.DamageLevel ?? GaugeDamageLevel.Normal;
            Risky = RiskyRange.Clamp(config?.Risky ?? RiskyRange.Default);
            FailureEnabled = !(config?.NoFail ?? false);
        }

        internal IReadOnlySet<int> AutoPlayLanes { get; }
        internal bool AutoAddGauge { get; }
        internal GaugeDamageLevel DamageLevel { get; }
        internal int Risky { get; }
        internal bool FailureEnabled { get; }
    }
}
