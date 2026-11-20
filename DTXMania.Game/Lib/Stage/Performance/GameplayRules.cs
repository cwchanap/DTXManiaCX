#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>Frozen gameplay policy and chart-bound state for one performance run.</summary>
    internal sealed class GameplayRules
    {
        private int _autoPlayNoteIndex;

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
        internal GaugeManager? GaugeManager { get; private set; }
        internal SkillManager? SkillManager { get; private set; }

        internal void BindChart(ParsedChart chart) => ChartManager = new ChartManager(chart);

        internal void ResetAutoPlay() => _autoPlayNoteIndex = 0;

        internal void ProcessAutoPlay(double currentSongTimeMs, Action<Note> feedback)
        {
            var chartManager = ChartManager;
            var judgementManager = JudgementManager;
            if (AutoPlayLanes.Count == 0 || chartManager == null || judgementManager == null)
                return;

            var allNotes = chartManager.AllNotes;
            // Advance past manual and processed notes; never skip overdue automated notes.
            while (_autoPlayNoteIndex < allNotes.Count)
            {
                var note = allNotes[_autoPlayNoteIndex];
                if (currentSongTimeMs - note.TimeMs < 0)
                    break;

                if (AutoPlayLanes.Contains(note.LaneIndex) &&
                    judgementManager.GetNoteRuntimeData(note.Id)?.Status == NoteStatus.Pending)
                {
                    judgementManager.ResolveAutoHit(note.Id);
                    feedback(note);
                }

                _autoPlayNoteIndex++;
            }
        }

        internal void UpdateJudgement(double compensatedTimeMs) =>
            JudgementManager?.Update(compensatedTimeMs, compensatedTimeMs);

        internal static double GetPlayerJudgementTimeMs(
            double currentAudioClockMs, int offsetMs, PlaybackModifiers playbackModifiers)
        {
            if (offsetMs <= 0)
                return currentAudioClockMs;

            var speed = playbackModifiers.PlaySpeedPercent > 0 ? playbackModifiers.Speed : 1.0;
            return Math.Max(0.0, currentAudioClockMs - offsetMs * speed);
        }

        internal Note? FindNearestNoteForChip(int laneIndex, double currentSongTimeMs)
        {
            var chartManager = ChartManager;
            var judgementManager = JudgementManager;
            if (chartManager == null || judgementManager == null)
                return null;

            // The Poor window excludes the Miss range of the wider judgement window.
            double windowMs = TimingConstants.PoorWindowMs;
            var allNotes = chartManager.AllNotes;
            int startIndex = chartManager.BinarySearchStartIndex(currentSongTimeMs - windowMs);
            Note? nearest = null;
            double nearestDistance = double.MaxValue;

            for (int i = startIndex; i < allNotes.Count; i++)
            {
                var note = allNotes[i];
                if (note.TimeMs - currentSongTimeMs > windowMs)
                    break;
                if (note.LaneIndex != laneIndex)
                    continue;

                var data = judgementManager.GetNoteRuntimeData(note.Id);
                if (data == null || data.Status != NoteStatus.Pending)
                    continue;

                var distance = Math.Abs(currentSongTimeMs - note.TimeMs);
                if (distance > windowMs)
                    continue;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = note;
                }
            }

            return nearest;
        }

        internal void InitializeJudgement(IInputManagerCompat inputManager)
        {
            var manager = new JudgementManager(inputManager, ChartManager!);
            manager.IsActive = false;
            // Automated lanes ignore physical input but remain available to autoplay.
            manager.SetIgnoredPlayerInputLanes(AutoPlayLanes);
            manager.JudgementMade += OnJudgementMade;
            JudgementManager = manager;
        }

        internal void ClearJudgement()
        {
            if (JudgementManager is { } manager)
            {
                manager.JudgementMade -= OnJudgementMade;
                manager.Dispose();
            }
            JudgementManager = null;
        }

        private void OnJudgementMade(object? sender, JudgementEvent e)
        {
            ScoreManager?.ProcessJudgement(e);
            ComboManager?.ProcessJudgement(e);
            if (!AutoPlayLanes.Contains(e.Lane) || AutoAddGauge)
                GaugeManager?.ProcessJudgement(e);
            SkillManager?.ProcessJudgement(e);
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

        internal void InitializeGaugeAndSkill()
        {
            GaugeManager = new GaugeManager(GaugeManager.StartingLife, DamageLevel, Risky, FailureEnabled);
            SkillManager = new SkillManager(ChartManager!.TotalNotes, ComboManager!);
        }

        internal void ClearGaugeAndSkill()
        {
            GaugeManager?.Dispose();
            GaugeManager = null;
            SkillManager?.Dispose();
            SkillManager = null;
        }

        internal void ClearChart() => ChartManager = null;
    }
}
