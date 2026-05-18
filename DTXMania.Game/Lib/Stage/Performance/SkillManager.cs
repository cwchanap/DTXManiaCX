#nullable enable

using System;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Computes DTXManiaNX-faithful Playing Skill (0.0-100.0) live during gameplay.
    /// Subscribes via PerformanceStage to JudgementManager events and reads MaxCombo
    /// from the injected ComboManager, so combo state is never duplicated here.
    /// Reference: DTXManiaNX CScoreIni.tCalculatePlayingSkill (Score,Song/CScoreIni.cs:1641).
    /// </summary>
    public class SkillManager : IDisposable
    {
        private readonly int _totalNotes;
        private readonly ComboManager _comboManager;
        private int _perfect;
        private int _great;
        private int _good;
        private int _poor;
        private int _miss;
        private double _currentSkill;
        private bool _disposed;

        public event EventHandler<SkillChangedEventArgs>? SkillChanged;

        public int  PerfectCount => _perfect;
        public int  GreatCount   => _great;
        public int  GoodCount    => _good;
        public int  PoorCount    => _poor;
        public int  MissCount    => _miss;
        public int  TotalNotes   => _totalNotes;
        public double CurrentSkill => _currentSkill;
        public bool IsMax => _currentSkill >= 100.0;

        public SkillManager(int totalNotes, ComboManager comboManager)
        {
            if (totalNotes <= 0)
                throw new ArgumentException("Total notes must be greater than 0", nameof(totalNotes));
            _totalNotes = totalNotes;
            _comboManager = comboManager ?? throw new ArgumentNullException(nameof(comboManager));
        }

        public void ProcessJudgement(JudgementEvent? judgementEvent)
        {
            if (_disposed || judgementEvent == null) return;

            switch (judgementEvent.Type)
            {
                case JudgementType.Perfect: _perfect++; break;
                case JudgementType.Great:   _great++;   break;
                case JudgementType.Good:    _good++;    break;
                case JudgementType.Poor:    _poor++;    break;
                case JudgementType.Miss:    _miss++;    break;
            }

            double previous = _currentSkill;
            _currentSkill = SongScore.CalculatePlayingSkill(
                _totalNotes, _perfect, _great, _comboManager.MaxCombo);

            SkillChanged?.Invoke(this, new SkillChangedEventArgs
            {
                PreviousSkill = previous,
                CurrentSkill  = _currentSkill,
                IsMax         = IsMax,
                JudgementType = judgementEvent.Type
            });
        }

        public void Reset()
        {
            if (_disposed) return;
            double previous = _currentSkill;
            _perfect = _great = _good = _poor = _miss = 0;
            _currentSkill = 0.0;
            SkillChanged?.Invoke(this, new SkillChangedEventArgs
            {
                PreviousSkill = previous,
                CurrentSkill  = 0.0,
                IsMax         = false,
                JudgementType = null
            });
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing) _disposed = true;
        }
    }

    /// <summary>
    /// Event payload for SkillManager.SkillChanged.
    /// </summary>
    public class SkillChangedEventArgs : EventArgs
    {
        public double PreviousSkill { get; set; }
        public double CurrentSkill  { get; set; }
        public bool   IsMax         { get; set; }
        public JudgementType? JudgementType { get; set; }
    }
}
