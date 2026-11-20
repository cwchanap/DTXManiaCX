#nullable enable

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>Frozen gameplay policy and chart-bound state for one performance run.</summary>
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

        internal ChartManager? ChartManager { get; private set; }
        internal JudgementManager? JudgementManager { get; private set; }
        internal ScoreManager? ScoreManager { get; private set; }
        internal ComboManager? ComboManager { get; private set; }

        internal void BindChart(ParsedChart chart) => ChartManager = new ChartManager(chart);

        internal void InitializeJudgement(IInputManagerCompat inputManager)
        {
            var manager = new JudgementManager(inputManager, ChartManager!);
            manager.IsActive = false;
            // Automated lanes ignore physical input but remain available to autoplay.
            manager.SetIgnoredPlayerInputLanes(AutoPlayLanes);
            JudgementManager = manager;
        }

        internal void ClearJudgement()
        {
            JudgementManager?.Dispose();
            JudgementManager = null;
        }

        internal void InitializeScoreAndCombo()
        {
            ScoreManager = new ScoreManager(ChartManager!.TotalNotes);
            ComboManager = new ComboManager();
        }

        internal void ClearScoreAndCombo()
        {
            ScoreManager?.Dispose();
            ScoreManager = null;
            ComboManager?.Dispose();
            ComboManager = null;
        }

        internal void ClearChart() => ChartManager = null;
    }
}
