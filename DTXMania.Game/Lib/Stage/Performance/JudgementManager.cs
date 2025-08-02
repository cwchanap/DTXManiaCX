using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using DTX.Input;
using DTX.Song.Components;
using DTXMania.Game.Lib.Song.Entities;

namespace DTX.Stage.Performance
{
    /// <summary>
    /// Manages hit detection, timing judgements, and note state tracking during gameplay.
    /// Maps keyboard keys A S D F Space J K L ; → lane indices 0-8.
    /// Each Update tick:
    /// 1. Read freshly-pressed keys from InputManager.
    /// 2. For each lane key pressed, query ChartManager for the nearest *unhit* note within ±90 ms (use BinarySearchStartIndex) and decide judgement by abs Δ.
    /// 3. Mark note as Hit, emit JudgementEvent via C# event or IEventBus.
    /// Maintain per-note state (enum: Pending/Hit/Missed) inside a NoteRuntimeData dictionary keyed by Note.Id to avoid double hits.
    /// </summary>
    public class JudgementManager : IDisposable
    {
        #region Private Fields

        private readonly InputManager _inputManager;
        private readonly ChartManager _chartManager;
        private readonly Dictionary<int, NoteRuntimeData> _noteRuntimeData;
        private bool _disposed = false;

        // Keyboard mapping for 9-lane drum layout: A S D F Space J K L ; → lane indices 0-8
        private readonly Keys[] _laneKeys = new Keys[]
        {
            Keys.A,         // Lane 0 - LC (Left Cymbal)
            Keys.S,         // Lane 1 - LP (Left Pedal)
            Keys.D,         // Lane 2 - HH (Hi-Hat)
            Keys.F,         // Lane 3 - SD (Snare Drum)
            Keys.Space,     // Lane 4 - BD (Bass Drum)
            Keys.J,         // Lane 5 - HT (High Tom)
            Keys.K,         // Lane 6 - LT (Low Tom)
            Keys.L,         // Lane 7 - FT (Floor Tom)
            Keys.OemSemicolon // Lane 8 - CY (Right Cymbal)
        };

        // Timing window for hit detection (±90ms as specified)
        private const double HitDetectionWindowMs = 90.0;

        #endregion

        #region Events

        /// <summary>
        /// Raised when a judgement is made (hit or miss)
        /// </summary>
        public event EventHandler<JudgementEvent>? JudgementMade;

        #endregion

        #region Properties

        /// <summary>
        /// Whether the judgement manager is currently processing input
        /// </summary>
        public bool IsActive { get; set; } = true;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new JudgementManager
        /// </summary>
        /// <param name="inputManager">Input manager for reading keyboard state</param>
        /// <param name="chartManager">Chart manager containing note data</param>
        public JudgementManager(InputManager inputManager, ChartManager chartManager)
        {
            _inputManager = inputManager ?? throw new ArgumentNullException(nameof(inputManager));
            _chartManager = chartManager ?? throw new ArgumentNullException(nameof(chartManager));
            _noteRuntimeData = new Dictionary<int, NoteRuntimeData>();

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
            if (!IsActive || _disposed)
                return;

            // Process keyboard input for hit detection
            ProcessKeyboardInput(currentSongTimeMs);

            // Check for missed notes (timeout scanner)
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
        /// Processes keyboard input for hit detection
        /// </summary>
        /// <param name="currentSongTimeMs">Current song time in milliseconds</param>
        private void ProcessKeyboardInput(double currentSongTimeMs)
        {
            // Check each lane key for fresh presses
            for (int laneIndex = 0; laneIndex < _laneKeys.Length; laneIndex++)
            {
                var key = _laneKeys[laneIndex];
                
                // Only process freshly pressed keys (not held)
                if (_inputManager.IsKeyPressed((int)key))
                {
                    ProcessLaneInput(laneIndex, currentSongTimeMs);
                }
            }
        }

        /// <summary>
        /// Processes input for a specific lane
        /// </summary>
        /// <param name="laneIndex">Lane index (0-8)</param>
        /// <param name="currentSongTimeMs">Current song time in milliseconds</param>
        private void ProcessLaneInput(int laneIndex, double currentSongTimeMs)
        {
            // Find the nearest unhit note in this lane within the hit detection window
            var nearestNote = FindNearestUnhitNote(laneIndex, currentSongTimeMs);
            
            if (nearestNote != null)
            {
                var deltaMs = currentSongTimeMs - nearestNote.Note.TimeMs;
                var judgementType = TimingConstants.GetJudgementType(deltaMs);

                // Create judgement event
                var judgementEvent = new JudgementEvent(
                    nearestNote.NoteId,
                    laneIndex,
                    deltaMs,
                    judgementType
                );

                // Mark note as hit and store judgement
                nearestNote.Status = NoteStatus.Hit;
                nearestNote.JudgementEvent = judgementEvent;

                // Raise judgement event
                JudgementMade?.Invoke(this, judgementEvent);

                System.Diagnostics.Debug.WriteLine($"Hit detected: {judgementEvent}");
            }
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

            // Use BinarySearch to find start index
            var startIndex = _chartManager.BinarySearchStartIndex(currentSongTimeMs - HitDetectionWindowMs);

            // Search forward from the start index for the nearest unhit note in this lane
            for (int i = startIndex; i < _chartManager.AllNotes.Count; i++)
            {
                var note = _chartManager.AllNotes[i];
                if (note.LaneIndex != laneIndex)
                    continue;

                var noteData = _noteRuntimeData[note.Id];

                if (noteData.Status != NoteStatus.Pending)
                    continue;

                var timeDifference = Math.Abs(currentSongTimeMs - note.TimeMs);

                if (timeDifference > HitDetectionWindowMs)
                    continue;

                if (timeDifference < nearestDistance)
                {
                    nearestDistance = timeDifference;
                    nearestNote = noteData;
                }
            }

            return nearestNote;
        }

        /// <summary>
        /// Processes missed notes (timeout scanner)
        /// </summary>
        /// <param name="currentSongTimeMs">Current song time in milliseconds</param>
        private void ProcessMissedNotes(double currentSongTimeMs)
        {
            foreach (var noteData in _noteRuntimeData.Values)
            {
                // Skip if note is already processed
                if (noteData.Status != NoteStatus.Pending)
                    continue;

                // Check if note has passed the miss threshold
                var timeSinceNote = currentSongTimeMs - noteData.Note.TimeMs;
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

                    System.Diagnostics.Debug.WriteLine($"Note missed: {missEvent}");
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
