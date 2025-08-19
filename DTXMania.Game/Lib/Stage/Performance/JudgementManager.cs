using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Manages hit detection, timing judgements, and note state tracking during gameplay.
    /// Subscribes to lane hit events from the modular input system.
    /// Each Update tick:
    /// 1. Process any pending lane hit events from the input system.
    /// 2. For each lane hit, query ChartManager for the nearest *unhit* note within ±150 ms and decide judgement by abs Δ.
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
        private bool _disposed = false;
        
        // Miss detection optimization: Track the next note index to check for misses
        private int _missDetectionIndex = 0;

        // Timing window for hit detection (±200ms to cover Miss threshold range)
        private const double HitDetectionWindowMs = 200.0;

        #endregion

        #region Events

        /// <summary>
        /// Raised when a judgement is made (hit or miss)
        /// </summary>
        public event EventHandler<JudgementEvent>? JudgementMade;

        #endregion

        #region Properties

        /// <summary>
        /// Whether the judgement manager is currently processing input.
        /// Note: Miss detection always runs regardless of this setting to ensure notes don't get stuck.
        /// </summary>
        public bool IsActive { get; set; } = true;

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
            if (_disposed)
                return;

            // Process pending lane hit events from the input system (only when active)
            if (IsActive)
            {
                ProcessPendingLaneHits(currentSongTimeMs);
            }

            // Check for missed notes (timeout scanner) - always run regardless of IsActive
            // Miss detection must run even during ready countdown to prevent notes getting stuck
            ProcessMissedNotes(currentSongTimeMs);
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
                                case JudgementType.Just: stats.JustCount++; break;
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

        #endregion

        #region Test Support Methods

        /// <summary>
        /// Test-friendly method to directly simulate a lane hit event.
        /// This bypasses the normal input system for unit testing purposes.
        /// </summary>
        /// <param name="lane">Lane index that was hit</param>
        /// <param name="buttonId">Optional button ID for the event</param>
        public void TestTriggerLaneHit(int lane, string buttonId = "TestButton")
        {
            var buttonState = new DTXMania.Game.Lib.Input.ButtonState(buttonId, true, 1.0f);
            var hitArgs = new LaneHitEventArgs(lane, buttonState);
            
            // Directly call the OnLaneHit method that normally receives events from input system
            OnLaneHit(this, hitArgs);
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
            // Only process events when active
            if (!IsActive || _disposed)
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
            lock (_pendingLaneHits)
            {
                foreach (LaneHitEventArgs laneHitEvent in _pendingLaneHits)
                {
                    ProcessLaneInput(laneHitEvent, currentSongTimeMs);
                }
                _pendingLaneHits.Clear();
            }
        }

        /// <summary>
        /// Processes input for a specific lane using full event data
        /// </summary>
        /// <param name="laneHitEvent">Lane hit event containing lane, button state, and timestamp</param>
        /// <param name="currentSongTimeMs">Current song time in milliseconds</param>
        private void ProcessLaneInput(LaneHitEventArgs laneHitEvent, double currentSongTimeMs)
        {
            var laneIndex = laneHitEvent.Lane;
            var buttonVelocity = laneHitEvent.Button.Velocity;
            var eventTimestamp = laneHitEvent.Timestamp;
            
            // Find the nearest unhit note in this lane within the hit detection window
            var nearestNote = FindNearestUnhitNote(laneIndex, currentSongTimeMs);
            
            if (nearestNote != null)
            {
                var deltaMs = currentSongTimeMs - nearestNote.Note.TimeMs;
                var judgementType = TimingConstants.GetJudgementType(deltaMs);

                // Create judgement event with enhanced data
                var judgementEvent = new JudgementEvent(
                    nearestNote.NoteId,
                    laneIndex,
                    deltaMs,
                    judgementType
                )
                {
                    Timestamp = eventTimestamp
                };

                // Mark note as hit and store judgement
                nearestNote.Status = NoteStatus.Hit;
                nearestNote.JudgementEvent = judgementEvent;

                // Raise judgement event
                JudgementMade?.Invoke(this, judgementEvent);
            }
        }
        
        /// <summary>
        /// Processes input for a specific lane (legacy overload)
        /// </summary>
        /// <param name="laneIndex">Lane index (0-8)</param>
        /// <param name="currentSongTimeMs">Current song time in milliseconds</param>
        private void ProcessLaneInput(int laneIndex, double currentSongTimeMs)
        {
            // Create a synthetic event for backwards compatibility
            var syntheticButton = new DTXMania.Game.Lib.Input.ButtonState($"Lane{laneIndex}", true, 1.0f);
            var syntheticEvent = new LaneHitEventArgs(laneIndex, syntheticButton);
            ProcessLaneInput(syntheticEvent, currentSongTimeMs);
        }

        /// <summary>
        /// Finds the nearest unhit note in a specific lane within the hit detection window
        /// </summary>
        /// <param name="laneIndex">Lane index to search</param>
        /// <param name="currentSongTimeMs">Current song time</param>
        /// <returns>Nearest unhit note state, or null if none found</returns>
        private NoteRuntimeData? FindNearestUnhitNote(int laneIndex, double currentSongTimeMs)
        {
            NoteRuntimeData? nearestNote = null;
            double nearestDistance = double.MaxValue;
            int candidatesFound = 0;
            int notesInLane = 0;
            int pendingNotesInLane = 0;

            // Use BinarySearch to find start index
            var searchTime = currentSongTimeMs - HitDetectionWindowMs;
            var startIndex = _chartManager.BinarySearchStartIndex(searchTime);

            // Search forward from the start index for the nearest unhit note in this lane
            for (int i = startIndex; i < _chartManager.AllNotes.Count; i++)
            {
                var note = _chartManager.AllNotes[i];
                
                // Early break: if note is too far in future, stop scanning since notes are time-sorted
                if (note.TimeMs - currentSongTimeMs > HitDetectionWindowMs)
                {
                    break;
                }
                
                if (note.LaneIndex != laneIndex)
                    continue;

                notesInLane++;
                var noteData = _noteRuntimeData[note.Id];

                if (noteData.Status != NoteStatus.Pending)
                {
                    continue;
                }

                pendingNotesInLane++;
                var timeDifference = Math.Abs(currentSongTimeMs - note.TimeMs);

                if (timeDifference > HitDetectionWindowMs)
                {
                    continue;
                }

                candidatesFound++;

                if (timeDifference < nearestDistance)
                {
                    nearestDistance = timeDifference;
                    nearestNote = noteData;
                }
            }

            return nearestNote;
        }

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
                if (timeSinceNote > HitDetectionWindowMs)
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

                    // Raise judgement event
                    JudgementMade?.Invoke(this, missEvent);
                    
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
        public int JustCount { get; set; }
        public int GreatCount { get; set; }
        public int GoodCount { get; set; }
        public int PoorCount { get; set; }
        public int MissCount { get; set; }

        /// <summary>
        /// Total number of notes processed
        /// </summary>
        public int TotalNotes => JustCount + GreatCount + GoodCount + PoorCount + MissCount;

        /// <summary>
        /// Total number of hits (non-misses)
        /// </summary>
        public int TotalHits => JustCount + GreatCount + GoodCount + PoorCount;

        /// <summary>
        /// Accuracy percentage
        /// </summary>
        public double Accuracy => TotalNotes > 0 ? (double)TotalHits / TotalNotes * 100.0 : 0.0;
    }

    #endregion
}
