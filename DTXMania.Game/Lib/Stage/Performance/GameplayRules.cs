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
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>Frozen gameplay policy and chart-bound state for one performance run.</summary>
    internal sealed class GameplayRules
    {
        private int _autoPlayNoteIndex;
        private double? _chartEndReachedRealTimeSeconds;
        private int _fastCount;
        private int _slowCount;

        internal GameplayRules(ConfigData? config)
        {
            AutoPlayLanes = (config?.AutoPlayLanes ?? Enumerable.Empty<int>()).ToFrozenSet();
            AutoAddGauge = config?.AutoAddGauge ?? true;
            DamageLevel = config?.DamageLevel ?? GaugeDamageLevel.Normal;
            Risky = RiskyRange.Clamp(config?.Risky ?? RiskyRange.Default);
            FailureEnabled = !(config?.NoFail ?? false);
            ShareCymbalLanes = config?.ShareCymbalLanes ?? false;
        }

        internal IReadOnlySet<int> AutoPlayLanes { get; }
        internal bool AutoAddGauge { get; }
        internal GaugeDamageLevel DamageLevel { get; }
        internal int Risky { get; }
        internal bool FailureEnabled { get; }
        internal bool ShareCymbalLanes { get; }

        internal ChartManager? ChartManager { get; private set; }
        internal JudgementManager? JudgementManager { get; private set; }
        internal ScoreManager? ScoreManager { get; private set; }
        internal ComboManager? ComboManager { get; private set; }
        internal GaugeManager? GaugeManager { get; private set; }
        internal SkillManager? SkillManager { get; private set; }
        internal bool IsCompleted { get; private set; }

        internal PerformanceFrameState BuildFrameState(double rawMs, float? retainedProgress = null)
        {
            var gauge = GaugeManager?.CurrentLife;
            var life = (gauge ?? PerformanceUILayout.GaugeSettings.StartingLife) / 100f;
            var totalMs = ChartManager?.DurationMs ?? 0.0;
            return new PerformanceFrameState
            {
                Score = ScoreManager?.CurrentScore ?? 0,
                Combo = ComboManager?.CurrentCombo ?? 0,
                MaxCombo = ComboManager?.MaxCombo ?? 0,
                Life = life,
                Gauge = gauge ?? 0f,
                HasFailed = GaugeManager?.HasFailed ?? false,
                IsDanger = life < PerformanceUILayout.GaugeSettings.DangerThreshold / 100f,
                Achievement = SkillManager?.CurrentSkill ?? 0.0,
                IsMaxSkill = SkillManager?.IsMax ?? false,
                PerfectCount = SkillManager?.PerfectCount ?? 0,
                GreatCount = SkillManager?.GreatCount ?? 0,
                GoodCount = SkillManager?.GoodCount ?? 0,
                PoorCount = SkillManager?.PoorCount ?? 0,
                // HUD counts judgement events; Result counts missed note statuses.
                MissCount = SkillManager?.MissCount ?? 0,
                StatusPerfectCount = JudgementManager?.GetStatusCount(JudgementType.Perfect) ?? 0,
                StatusGreatCount = JudgementManager?.GetStatusCount(JudgementType.Great) ?? 0,
                StatusGoodCount = JudgementManager?.GetStatusCount(JudgementType.Good) ?? 0,
                StatusPoorCount = JudgementManager?.GetStatusCount(JudgementType.Poor) ?? 0,
                StatusMissCount = JudgementManager?.GetStatusCount(JudgementType.Miss) ?? 0,
                TotalNotes = ChartManager?.TotalNotes ?? 0,
                FastCount = _fastCount,
                SlowCount = _slowCount,
                Progress = retainedProgress ?? (totalMs > 0 ? (float)Math.Clamp(rawMs / totalMs, 0.0, 1.0) : 0f),
                ElapsedMs = rawMs,
                TotalMs = totalMs
            };
        }

        internal void BindChart(ParsedChart chart) => ChartManager = new ChartManager(chart);

        internal void ResetAutoPlay() => _autoPlayNoteIndex = 0;

        internal void ResetSongEndBuffer() => _chartEndReachedRealTimeSeconds = null;

        internal void ResetCompletion()
        {
            IsCompleted = false;
            ResetSongEndBuffer();
        }

        internal CompletionReason? CheckCompletion(double currentTimeMs, double? chartDurationMs, double realTimeSeconds)
        {
            if (IsCompleted || chartDurationMs == null)
                return null;

            if (currentTimeMs < chartDurationMs.Value)
                ResetSongEndBuffer();
            else
            {
                // Start the real-time delay on the observed crossing, not at the logical chart end.
                _chartEndReachedRealTimeSeconds ??= realTimeSeconds;
                if (realTimeSeconds - _chartEndReachedRealTimeSeconds.Value >=
                    GameConstants.Performance.SongEndBufferSeconds)
                    return CompletionReason.SongComplete;
            }

            return GaugeManager?.HasFailed == true ? CompletionReason.PlayerFailed : null;
        }

        internal bool TryComplete()
        {
            if (IsCompleted)
                return false;
            IsCompleted = true;
            return true;
        }

        internal PerformanceSummary BuildSummary(
            CompletionReason reason, PlaybackModifiers playbackModifiers, int chartLevel, int chartLevelDec)
        {
            // Gauge failure is synchronous: the note status has changed, but skill has not.
            double playingSkill = SkillManager?.CurrentSkill ?? 0.0;
            return new PerformanceSummary
            {
                RunId = Guid.NewGuid(),
                UsedAutoPlay = AutoPlayLanes.Count > 0,
                PlaySpeedPercent = playbackModifiers.PlaySpeedPercent,
                PitchSemitones = playbackModifiers.PitchSemitones,
                Score = ScoreManager?.CurrentScore ?? 0,
                MaxCombo = ComboManager?.MaxCombo ?? 0,
                ClearFlag = reason == CompletionReason.SongComplete,
                PerfectCount = JudgementManager?.GetStatusCount(JudgementType.Perfect) ?? 0,
                GreatCount = JudgementManager?.GetStatusCount(JudgementType.Great) ?? 0,
                GoodCount = JudgementManager?.GetStatusCount(JudgementType.Good) ?? 0,
                PoorCount = JudgementManager?.GetStatusCount(JudgementType.Poor) ?? 0,
                MissCount = JudgementManager?.GetStatusCount(JudgementType.Miss) ?? 0,
                TotalNotes = ChartManager?.TotalNotes ?? 0,
                FinalLife = GaugeManager?.CurrentLife ?? 0.0f,
                CompletionReason = reason,
                PlayingSkill = playingSkill,
                GameSkill = SongScore.CalculateGameSkill(playingSkill, chartLevel, chartLevelDec),
                ChartLevel = chartLevel,
                ChartLevelDec = chartLevelDec
            };
        }

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
                    if (!ReferenceEquals(judgementManager, JudgementManager))
                        return;
                    feedback(note);
                    if (!ReferenceEquals(judgementManager, JudgementManager))
                        return;
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

        /// <summary>
        /// The note whose chip sound a player hit plays: the note the hit judged, or for an
        /// empty hit the lane's nearest note, hit or not (DTXManiaNX parity). With shared
        /// cymbals, a cymbal lane with no notes falls back to the other cymbal, like NX CYGroup.
        /// </summary>
        internal Note? FindChipNoteForHit(PlayerHitResolvedEventArgs hit)
        {
            if (hit.JudgedNote != null || ChartManager == null)
                return hit.JudgedNote;

            var note = ChartManager.FindNearestNoteInLane(hit.Lane, hit.JudgementTimeMs);
            if (note == null && ShareCymbalLanes && OtherCymbalLane(hit.Lane) is int otherLane)
                note = ChartManager.FindNearestNoteInLane(otherLane, hit.JudgementTimeMs);
            return note;
        }

        private static int? OtherCymbalLane(int lane) => lane switch
        {
            (int)PerformanceUILayout.LaneType.CY => (int)PerformanceUILayout.LaneType.RD,
            (int)PerformanceUILayout.LaneType.RD => (int)PerformanceUILayout.LaneType.CY,
            _ => null,
        };

        internal void InitializeJudgement(IInputManagerCompat inputManager)
        {
            var manager = new JudgementManager(inputManager, ChartManager!);
            manager.IsActive = false;
            // Automated lanes ignore physical input but remain available to autoplay.
            manager.SetIgnoredPlayerInputLanes(AutoPlayLanes);
            manager.ShareCymbalLanes = ShareCymbalLanes;
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
            if (!ReferenceEquals(sender, JudgementManager))
                return;
            ComboManager?.ProcessJudgement(e);
            if (!ReferenceEquals(sender, JudgementManager))
                return;
            if (!AutoPlayLanes.Contains(e.Lane) || AutoAddGauge)
                GaugeManager?.ProcessJudgement(e);
            // Any manager callback can synchronously tear down or replace the run.
            if (!ReferenceEquals(sender, JudgementManager))
                return;
            SkillManager?.ProcessJudgement(e);
            if (!ReferenceEquals(sender, JudgementManager) || SkillManager == null ||
                !e.IsHit() || AutoPlayLanes.Contains(e.Lane))
                return;

            var rounded = PerformanceUILayout.HitTimingFeedback.ProjectDelta(e.DeltaMs).RoundedMilliseconds;
            if (rounded < 0)
                _fastCount++;
            else if (rounded > 0)
                _slowCount++;
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
            _fastCount = _slowCount = 0;
            GaugeManager = new GaugeManager(GaugeManager.StartingLife, DamageLevel, Risky, FailureEnabled);
            SkillManager = new SkillManager(ChartManager!.TotalNotes, ComboManager!);
        }

        internal void ClearGaugeAndSkill()
        {
            GaugeManager?.Dispose();
            GaugeManager = null;
            SkillManager?.Dispose();
            SkillManager = null;
            _fastCount = _slowCount = 0;
        }

        internal void ClearChart() => ChartManager = null;
    }
}
