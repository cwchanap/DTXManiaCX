#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Manages hit detection, timing judgements, and note state tracking during gameplay.
    /// Subscribes to lane hit events from the modular input system.
    /// Each Update tick:
    /// 1. Process any pending lane hit events from the input system.
    /// 2. For each lane hit, find the nearest pending note the lane may judge within the Poor
    ///    window (±150 ms) and decide judgement by abs Δ. A hit outside the Poor window is an
    ///    empty hit: like DTXManiaNX, it judges nothing and leaves the note for a later hit.
    /// 3. Mark note as Hit, emit JudgementEvent via C# event or IEventBus.
    /// Maintain per-note state (enum: Pending/Hit/Missed) inside a NoteRuntimeData dictionary keyed by Note.Id to avoid double hits.
    /// </summary>
    public class JudgementManager : IDisposable
    {
        #region Private Fields

        private readonly IInputManagerCompat _inputManager;
        private readonly ChartManager _chartManager;
        private readonly Dictionary<int, NoteRuntimeData> _noteRuntimeData;
        private readonly List<LaneHitEventArgs> _pendingLaneHits;
        private readonly int[] _statusCounts = new int[5];
        private HashSet<int> _ignoredPlayerInputLanes = new();
        private bool _disposed = false;
        
        // Miss detection optimization: Track the next note index to check for misses
        private int _missDetectionIndex = 0;

        /// <summary>
        /// A pending note is judged Miss once the judgement clock passes it by this much.
        /// </summary>
        public const double MissTimeoutMs = TimingConstants.MissThresholdMs;

        #endregion

        #region Events

        /// <summary>
        /// Raised when a judgement is made (hit or miss)
        /// </summary>
        public event EventHandler<JudgementEvent>? JudgementMade;

        /// <summary>
        /// Raised for every processed player lane hit, including empty hits, with the note the
        /// hit judged. Drives hit sounds, so each hit of a chord plays its own note.
        /// </summary>
        public event EventHandler<PlayerHitResolvedEventArgs>? PlayerHitResolved;

        #endregion

        #region Properties

        /// <summary>
        /// Whether the judgement manager is currently processing input.
        /// Note: Miss detection always runs regardless of this setting to ensure notes don't get stuck.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// When true, a hit on the CY or RD lane may judge a note on either lane
        /// (DTXManiaNX CYGroup "shared"). Frozen per run by <see cref="GameplayRules"/>.
        /// </summary>
        internal bool ShareCymbalLanes { get; set; }

        internal void SetIgnoredPlayerInputLanes(IEnumerable<int> lanes)
        {
            _ignoredPlayerInputLanes = new HashSet<int>(lanes);
        }

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new JudgementManager
        /// </summary>
        /// <param name="inputManager">Input manager for receiving lane hit events</param>
        /// <param name="chartManager">Chart manager containing note data</param>
        public JudgementManager(IInputManagerCompat inputManager, ChartManager chartManager)
        {
            _inputManager = inputManager ?? throw new ArgumentNullException(nameof(inputManager));
            _chartManager = chartManager ?? throw new ArgumentNullException(nameof(chartManager));
            _noteRuntimeData = new Dictionary<int, NoteRuntimeData>();
            _pendingLaneHits = new List<LaneHitEventArgs>();

            // Subscribe to lane hit events from the modular input system
            _inputManager.ModularInputManager.OnLaneHit += OnLaneHit;

            // Initialize note runtime data for all notes in the chart
            InitializeNoteRuntimeData();
            
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Updates the judgement manager, processing input and checking for missed notes
        /// </summary>
        /// <param name="currentSongTimeMs">Current song time in milliseconds</param>
        public void Update(double currentSongTimeMs)
        {
            Update(currentSongTimeMs, currentSongTimeMs);
        }

        /// <summary>
        /// Updates hit processing and miss scanning, optionally from separate
        /// clocks. Pending player hits use the latency-compensated logical time.
        /// Miss scanning must also use the compensated clock so the player
        /// retains the full hit window after a note becomes audible; passing a
        /// raw clock for misses would mark notes missed before the player hears
        /// them under output latency. The two-argument form is retained for
        /// tests and callers that need to drive the clocks independently.
        /// </summary>
        /// <param name="pendingHitTimeMs">
        /// Latency-compensated logical time used for queued player hits.
        /// </param>
        /// <param name="missScanTimeMs">
        /// Logical chart time used for timeout-based miss detection. In
        /// production this is the same compensated clock as
        /// <paramref name="pendingHitTimeMs"/>.
        /// </param>
        public void Update(double pendingHitTimeMs, double missScanTimeMs)
        {
            if (_disposed)
                return;

            // Process pending lane hit events from the input system (only when active)
            if (IsActive)
            {
                ProcessPendingLaneHits(pendingHitTimeMs);
            }
            if (_disposed)
                return;

            // Check for missed notes (timeout scanner) - always run regardless of IsActive
            // Miss detection must run even during ready countdown to prevent notes getting stuck
            ProcessMissedNotes(missScanTimeMs);
        }

        /// <summary>
        /// Gets the runtime data of a specific note
        /// </summary>
        /// <param name="noteId">Note ID to query</param>
        /// <returns>Runtime data of the note, or null if not found</returns>
        public NoteRuntimeData? GetNoteRuntimeData(int noteId)
        {
            return _noteRuntimeData.TryGetValue(noteId, out var data) ? data : null;
        }

        /// <summary>
        /// Gets statistics about note states
        /// </summary>
        /// <returns>Statistics object</returns>
        public JudgementStatistics GetStatistics()
        {
            var stats = new JudgementStatistics();
            
            foreach (var data in _noteRuntimeData.Values)
            {
                switch (data.Status)
                {
                    case NoteStatus.Hit:
                        if (data.JudgementEvent != null)
                        {
                            switch (data.JudgementEvent.Type)
                            {
                                case JudgementType.Perfect: stats.PerfectCount++; break;
                                case JudgementType.Great: stats.GreatCount++; break;
                                case JudgementType.Good: stats.GoodCount++; break;
                                case JudgementType.Poor: stats.PoorCount++; break;
                            }
                        }
                        break;
                    case NoteStatus.Missed:
                        stats.MissCount++;
                        break;
                }
            }

            return stats;
        }

        /// <summary>
        /// Gets the count of a specific judgement type
        /// </summary>
        /// <param name="judgementType">Type of judgement to count</param>
        /// <returns>Count of the specified judgement type</returns>
        public int GetJudgementCount(JudgementType judgementType)
        {
            int count = 0;
            
            foreach (var data in _noteRuntimeData.Values)
            {
                if (judgementType == JudgementType.Miss)
                {
                    if (data.Status == NoteStatus.Missed)
                        count++;
                }
                else if (data.Status == NoteStatus.Hit && data.JudgementEvent?.Type == judgementType)
                {
                    count++;
                }
            }

            return count;
        }

        internal int GetStatusCount(JudgementType type) => _statusCounts[(int)type];

        #endregion

        #region Direct Hit Enqueue

        /// <summary>
        /// Directly enqueues a lane hit event into the pending queue.
        /// Bypasses the ignored-lane gate — used by AutoPlay and unit tests.
        /// Player-initiated events go through OnLaneHit which respects the gate.
        /// </summary>
        /// <param name="lane">Lane index that was hit</param>
        /// <param name="buttonId">Optional button ID for the event</param>
        public void EnqueueLaneHit(int lane, string buttonId = "AutoPlay")
        {
            if (!IsActive || _disposed) return;

            var buttonState = new DTXMania.Game.Lib.Input.ButtonState(buttonId, true, 1.0f);
            var hitArgs = new LaneHitEventArgs(lane, buttonState);

            // Bypass the ignored-lane gate by enqueuing directly. Used by AutoPlay
            // and unit tests; player events go through OnLaneHit which respects the gate.
            lock (_pendingLaneHits)
            {
                _pendingLaneHits.Add(hitArgs);
            }
        }

        #endregion

        #region AutoPlay Direct Resolution

        /// <summary>
        /// Directly resolves a specific note as Hit with Perfect timing, bypassing
        /// the hit detection window entirely. Used exclusively by AutoPlay to ensure
        /// notes are always resolved regardless of frame timing, GC pauses, or hitches.
        /// Unlike EnqueueLaneHit which goes through FindNearestPendingNote (window-limited),
        /// this resolves by note ID directly and deterministically.
        /// </summary>
        /// <param name="noteId">The ID of the note to resolve</param>
        /// <returns>True if the note was resolved as Hit, false if already processed or not found</returns>
        public bool ResolveAutoHit(int noteId)
        {
            if (!IsActive || _disposed) return false;

            if (!_noteRuntimeData.TryGetValue(noteId, out var noteData))
                return false;

            if (noteData.Status != NoteStatus.Pending)
                return false;

            // Autoplay always hits at perfect timing (delta = 0)
            var judgementEvent = new JudgementEvent(
                noteData.NoteId,
                noteData.Note.LaneIndex,
                0.0, // Perfect timing — autoplay hits at exact note time
                JudgementType.Perfect
            );

            noteData.Status = NoteStatus.Hit;
            noteData.JudgementEvent = judgementEvent;
            _statusCounts[(int)JudgementType.Perfect]++;

            JudgementMade?.Invoke(this, judgementEvent);
            return true;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Initializes runtime data for all notes in the chart
        /// /summary
        private void InitializeNoteRuntimeData()
        {
            var allNotes = _chartManager.AllNotes;
            
            foreach (var note in allNotes)
            {
                _noteRuntimeData[note.Id] = new NoteRuntimeData
                {
                    NoteId = note.Id,
                    Note = note,
                    Status = NoteStatus.Pending,
                    JudgementEvent = null
                };
            }
        }

        /// <summary>
        /// Handles lane hit events from the modular input system
        /// </summary>
        /// <param name="sender">Event sender</param>
        /// <param name="args">Lane hit event arguments</param>
        private void OnLaneHit(object? sender, LaneHitEventArgs args)
        {
            // Only process events when active and the lane accepts player input
            if (!IsActive || _disposed || _ignoredPlayerInputLanes.Contains(args.Lane))
                return;

            // Add lane hit event to pending list for processing in next Update
            lock (_pendingLaneHits)
            {
                _pendingLaneHits.Add(args);
            }
        }

        /// <summary>
        /// Processes pending lane hit events
        /// </summary>
        /// <param name="currentSongTimeMs">Current song time in milliseconds</param>
        private void ProcessPendingLaneHits(double currentSongTimeMs)
        {
            LaneHitEventArgs[] pending;
            lock (_pendingLaneHits)
            {
                if (_pendingLaneHits.Count == 0)
                    return;
                pending = _pendingLaneHits.ToArray();
                _pendingLaneHits.Clear();
            }

            foreach (LaneHitEventArgs laneHitEvent in pending)
            {
                if (_disposed)
                    break;
                ProcessLaneInput(laneHitEvent, currentSongTimeMs);
                if (_disposed)
                    break;
            }
        }

        /// <summary>
        /// Processes input for a specific lane using full event data
        /// </summary>
        /// <param name="laneHitEvent">Lane hit event containing lane, button state, and timestamp</param>
        /// <param name="currentSongTimeMs">Current song time in milliseconds</param>
        private void ProcessLaneInput(LaneHitEventArgs laneHitEvent, double currentSongTimeMs)
        {
            var nearestNote = FindNearestPendingNote(laneHitEvent.Lane, currentSongTimeMs);
            if (nearestNote == null)
            {
                PlayerHitResolved?.Invoke(
                    this, new PlayerHitResolvedEventArgs(laneHitEvent.Lane, null, currentSongTimeMs));
                return;
            }

            var deltaMs = currentSongTimeMs - nearestNote.Note.TimeMs;
            var judgementType = TimingConstants.GetJudgementType(deltaMs);

            // Report the judged note's lane: with shared cymbal lanes a CY hit can judge an RD note.
            var judgementEvent = new JudgementEvent(
                nearestNote.NoteId,
                nearestNote.Note.LaneIndex,
                deltaMs,
                judgementType
            )
            {
                Timestamp = laneHitEvent.Timestamp
            };

            nearestNote.Status = NoteStatus.Hit;
            nearestNote.JudgementEvent = judgementEvent;
            _statusCounts[(int)judgementType]++;

            PlayerHitResolved?.Invoke(
                this, new PlayerHitResolvedEventArgs(laneHitEvent.Lane, nearestNote.Note, currentSongTimeMs));
            if (_disposed)
                return;

            JudgementMade?.Invoke(this, judgementEvent);
        }

        /// <summary>
        /// Finds the nearest pending note that a hit on <paramref name="hitLane"/> may judge
        /// within the Poor window.
        /// </summary>
        /// <param name="hitLane">Lane the player hit</param>
        /// <param name="currentSongTimeMs">Judgement clock time of the hit</param>
        /// <returns>Nearest pending note, or null for an empty hit</returns>
        internal NoteRuntimeData? FindNearestPendingNote(int hitLane, double currentSongTimeMs)
        {
            const double windowMs = TimingConstants.PoorWindowMs;
            NoteRuntimeData? nearestNote = null;
            double nearestDistance = double.MaxValue;

            var allNotes = _chartManager.AllNotes;
            var startIndex = _chartManager.BinarySearchStartIndex(currentSongTimeMs - windowMs);

            for (int i = startIndex; i < allNotes.Count; i++)
            {
                var note = allNotes[i];

                // Notes are time-sorted, so nothing later can be closer.
                if (note.TimeMs - currentSongTimeMs > windowMs)
                    break;

                if (!CanJudge(hitLane, note.LaneIndex))
                    continue;

                if (!_noteRuntimeData.TryGetValue(note.Id, out var noteData) ||
                    noteData.Status != NoteStatus.Pending)
                    continue;

                var timeDifference = Math.Abs(currentSongTimeMs - note.TimeMs);
                if (timeDifference <= windowMs && timeDifference < nearestDistance)
                {
                    nearestDistance = timeDifference;
                    nearestNote = noteData;
                }
            }

            return nearestNote;
        }

        private bool CanJudge(int hitLane, int noteLane)
        {
            if (noteLane == hitLane)
                return true;

            // A shared cymbal hit never takes a note from a lane the player handed to autoplay.
            return ShareCymbalLanes
                && IsCymbalLane(hitLane)
                && IsCymbalLane(noteLane)
                && !_ignoredPlayerInputLanes.Contains(noteLane);
        }

        private static bool IsCymbalLane(int lane) =>
            lane == (int)PerformanceUILayout.LaneType.CY || lane == (int)PerformanceUILayout.LaneType.RD;

        /// <summary>
        /// Processes missed notes (timeout scanner) using time-indexed optimization.
        /// Uses _missDetectionIndex to track progress and avoid scanning all notes every frame.
        /// </summary>
        /// <param name="currentSongTimeMs">Current song time in milliseconds</param>
        private void ProcessMissedNotes(double currentSongTimeMs)
        {
            var allNotes = _chartManager.AllNotes;
            int processedThisFrame = 0;

            // Start from the last known position and scan forward
            while (_missDetectionIndex < allNotes.Count)
            {
                var note = allNotes[_missDetectionIndex];
                
                // Safety check: ensure runtime data exists for this note
                if (!_noteRuntimeData.TryGetValue(note.Id, out var noteData))
                {
                    // Create missing runtime data on-demand to prevent crash
                    noteData = new NoteRuntimeData
                    {
                        NoteId = note.Id,
                        Note = note,
                        Status = NoteStatus.Pending,
                        JudgementEvent = null
                    };
                    _noteRuntimeData[note.Id] = noteData;
                }
                
                // Calculate time difference to check if note is missed
                var timeSinceNote = currentSongTimeMs - note.TimeMs;
                
                if (timeSinceNote < 0)
                {
                    // Note is in the future, can't be missed yet
                    // Since notes are time-sorted, all subsequent notes will also be in the future
                    break;
                }
                
                // If note is already processed, just advance the index
                if (noteData.Status != NoteStatus.Pending)
                {
                    _missDetectionIndex++;
                    processedThisFrame++;
                    continue;
                }

                // Check if note has passed the miss threshold
                // Only mark as missed if we're past the note time AND beyond the hit window
                if (timeSinceNote > MissTimeoutMs)
                {
                    // Mark as missed and create miss judgement event
                    var missEvent = new JudgementEvent(
                        noteData.NoteId,
                        noteData.Note.LaneIndex,
                        timeSinceNote,
                        JudgementType.Miss
                    );

                    noteData.Status = NoteStatus.Missed;
                    noteData.JudgementEvent = missEvent;
                    _statusCounts[(int)JudgementType.Miss]++;

                    // Raise judgement event
                    JudgementMade?.Invoke(this, missEvent);
                    if (_disposed)
                        return;
                    
                    // Advance the index only when note is missed (fully processed)
                    _missDetectionIndex++;
                    processedThisFrame++;
                }
                else
                {
                    // Note is not yet missable, don't advance the index
                    // We'll check this note again in future updates
                    break;
                }
                
                // Limit processing per frame to avoid frame drops with large charts
                if (processedThisFrame >= 100)
                {
                    break;
                }
            }
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the judgement manager
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
                IsActive = false;
                
                // Unsubscribe from lane hit events
                if (_inputManager?.ModularInputManager != null)
                {
                    _inputManager.ModularInputManager.OnLaneHit -= OnLaneHit;
                }
                
                _noteRuntimeData.Clear();
                Array.Clear(_statusCounts);
                _disposed = true;
            }
        }

        #endregion
    }

    #region Supporting Classes

    /// <summary>
    /// Runtime data of a note during gameplay (keyed by Note.Id)
    /// </summary>
    public class NoteRuntimeData
    {
        /// <summary>
        /// Unique identifier for the note
        /// </summary>
        public int NoteId { get; set; }

        /// <summary>
        /// Reference to the original note
        /// </summary>
        public Note Note { get; set; } = null!;

        /// <summary>
        /// Current status of the note
        /// </summary>
        public NoteStatus Status { get; set; }

        /// <summary>
        /// Judgement event if the note has been processed
        /// </summary>
        public JudgementEvent? JudgementEvent { get; set; }
    }

    /// <summary>
    /// Runtime state of a note during gameplay (legacy compatibility)
    /// </summary>
    public class NoteRuntimeState
    {
        /// <summary>
        /// Unique identifier for the note
        /// </summary>
        public int NoteId { get; set; }

        /// <summary>
        /// Reference to the original note
        /// </summary>
        public Note Note { get; set; } = null!;

        /// <summary>
        /// Current status of the note
        /// </summary>
        public NoteStatus Status { get; set; }

        /// <summary>
        /// Judgement event if the note has been processed
        /// </summary>
        public JudgementEvent? JudgementEvent { get; set; }
    }

    /// <summary>
    /// Status of a note during gameplay
    /// </summary>
    public enum NoteStatus
    {
        /// <summary>
        /// Note is waiting to be hit or missed
        /// </summary>
        Pending,

        /// <summary>
        /// Note was successfully hit
        /// </summary>
        Hit,

        /// <summary>
        /// Note was missed (timed out)
        /// </summary>
        Missed
    }

    /// <summary>
    /// Statistics about judgements made during gameplay
    /// </summary>
    public class JudgementStatistics
    {
        public int PerfectCount { get; set; }
        public int GreatCount { get; set; }
        public int GoodCount { get; set; }
        public int PoorCount { get; set; }
        public int MissCount { get; set; }

        /// <summary>
        /// Total number of notes processed
        /// </summary>
        public int TotalNotes => PerfectCount + GreatCount + GoodCount + PoorCount + MissCount;

        /// <summary>
        /// Total number of hits (non-misses)
        /// </summary>
        public int TotalHits => PerfectCount + GreatCount + GoodCount + PoorCount;

        /// <summary>
        /// Accuracy percentage
        /// </summary>
        public double Accuracy => TotalNotes > 0 ? (double)TotalHits / TotalNotes * 100.0 : 0.0;
    }

    #endregion
}
