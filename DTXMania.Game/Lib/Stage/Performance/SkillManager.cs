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
        #region Private Fields

        private readonly int _totalNotes;
        private readonly ComboManager _comboManager;
        private int _perfect;
        private int _great;
        private int _good;
        private int _poor;
        private int _miss;
        private double _currentSkill;
        private bool _disposed;

        #endregion

        #region Events

        /// <summary>
        /// Raised when the current skill value changes
        /// </summary>
        public event EventHandler<SkillChangedEventArgs>? SkillChanged;

        #endregion

        #region Properties

        /// <summary>
        /// Number of Perfect judgements recorded
        /// </summary>
        public int PerfectCount => _perfect;

        /// <summary>
        /// Number of Great judgements recorded
        /// </summary>
        public int GreatCount => _great;

        /// <summary>
        /// Number of Good judgements recorded
        /// </summary>
        public int GoodCount => _good;

        /// <summary>
        /// Number of Poor judgements recorded
        /// </summary>
        public int PoorCount => _poor;

        /// <summary>
        /// Number of Miss judgements recorded
        /// </summary>
        public int MissCount => _miss;

        /// <summary>
        /// Total notes in the chart, used as the denominator for skill calculation
        /// </summary>
        public int TotalNotes => _totalNotes;

        /// <summary>
        /// Current playing skill value (0.0-100.0)
        /// </summary>
        public double CurrentSkill => _currentSkill;

        /// <summary>
        /// Whether the current skill has reached the maximum (100.0)
        /// </summary>
        public bool IsMax => _currentSkill >= 100.0;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new SkillManager bound to a ComboManager for MaxCombo access
        /// </summary>
        /// <param name="totalNotes">Total notes in the chart</param>
        /// <param name="comboManager">Combo manager providing MaxCombo state</param>
        public SkillManager(int totalNotes, ComboManager comboManager)
        {
            if (totalNotes < 0)
                throw new ArgumentException("Total notes cannot be negative", nameof(totalNotes));
            _totalNotes = totalNotes;
            _comboManager = comboManager ?? throw new ArgumentNullException(nameof(comboManager));
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Processes a judgement event and updates the skill value
        /// </summary>
        /// <param name="judgementEvent">Judgement event to process</param>
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

            if (previous != _currentSkill)
            {
                SkillChanged?.Invoke(this, new SkillChangedEventArgs
                {
                    PreviousSkill = previous,
                    CurrentSkill = _currentSkill,
                    IsMax = IsMax,
                    JudgementType = judgementEvent.Type
                });
            }
        }

        /// <summary>
        /// Resets all judgement counts and the current skill value to zero
        /// </summary>
        public void Reset()
        {
            if (_disposed) return;
            double previous = _currentSkill;
            _perfect = _great = _good = _poor = _miss = 0;
            _currentSkill = 0.0;
            SkillChanged?.Invoke(this, new SkillChangedEventArgs
            {
                PreviousSkill = previous,
                CurrentSkill = 0.0,
                IsMax = false,
                JudgementType = null
            });
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the skill manager
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected dispose method
        /// </summary>
        /// <param name="disposing">Whether disposing from Dispose() call</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Clear all event subscribers to prevent memory leaks
                SkillChanged = null;

                _disposed = true;
            }
        }

        #endregion
    }

    #region Supporting Classes

    /// <summary>
    /// Event payload for SkillManager.SkillChanged.
    /// </summary>
    public class SkillChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Skill value before the change
        /// </summary>
        public double PreviousSkill { get; set; }

        /// <summary>
        /// Skill value after the change
        /// </summary>
        public double CurrentSkill { get; set; }

        /// <summary>
        /// Whether the current skill has reached the maximum (100.0)
        /// </summary>
        public bool IsMax { get; set; }

        /// <summary>
        /// Judgement type that caused this skill change, or null when raised by Reset
        /// </summary>
        public JudgementType? JudgementType { get; set; }
    }

    #endregion
}
