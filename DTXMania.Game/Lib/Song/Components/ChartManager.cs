using System;
using System.Collections.Generic;
using System.Linq;

namespace DTX.Song.Components
{
    /// <summary>
    /// Runtime container for managing chart notes during gameplay
    /// Provides efficient access to notes based on current song time
    /// </summary>
    public class ChartManager
    {
        #region Private Fields

        private readonly List<Note> _notes;
        private readonly double _bpm;
        private int _lastActiveIndex = 0; // Optimization for sequential access

        #endregion

        #region Properties

        /// <summary>
        /// Total number of notes in the chart
        /// </summary>
        public int TotalNotes => _notes.Count;

        /// <summary>
        /// BPM of the chart
        /// </summary>
        public double Bpm => _bpm;

        /// <summary>
        /// Duration of the chart in milliseconds
        /// </summary>
        public double DurationMs { get; private set; }

        /// <summary>
        /// All notes in the chart (read-only)
        /// </summary>
        public IReadOnlyList<Note> AllNotes => _notes.AsReadOnly();

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new ChartManager from a ParsedChart
        /// </summary>
        /// <param name="parsedChart">The parsed chart data</param>
        public ChartManager(ParsedChart parsedChart)
        {
            if (parsedChart == null)
                throw new ArgumentNullException(nameof(parsedChart));

            _bpm = parsedChart.Bpm;
            _notes = new List<Note>(parsedChart.Notes);
            DurationMs = parsedChart.DurationMs;

            // Ensure all notes have calculated timing
            foreach (var note in _notes)
            {
                if (note.TimeMs == 0)
                {
                    note.CalculateTimeMs(_bpm);
                }
            }

            // Sort notes by time for efficient access
            _notes.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
            AssignNoteIds();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Gets notes that are currently active (visible on screen)
        /// </summary>
        /// <param name="songTimeMs">Current song time in milliseconds</param>
        /// <param name="lookAheadMs">How far ahead to look for notes (in milliseconds)</param>
        /// <returns>Notes that should be visible on screen</returns>
        public IEnumerable<Note> GetActiveNotes(double songTimeMs, double lookAheadMs = 2000.0)
        {
            if (_notes.Count == 0)
                yield break;

            var startTime = songTimeMs;
            var endTime = songTimeMs + lookAheadMs;

            // Optimization: start from last known position for sequential access
            var startIndex = FindStartIndex(startTime);

            for (int i = startIndex; i < _notes.Count; i++)
            {
                var note = _notes[i];

                // If note is too far in the future, stop searching
                if (note.TimeMs > endTime)
                    break;

                // If note is in the active range, yield it
                if (note.TimeMs >= startTime)
                {
                    yield return note;
                }
            }
        }

        /// <summary>
        /// Gets notes for a specific lane within a time range
        /// </summary>
        /// <param name="laneIndex">Lane index (0-8)</param>
        /// <param name="songTimeMs">Current song time in milliseconds</param>
        /// <param name="lookAheadMs">How far ahead to look for notes (in milliseconds)</param>
        /// <returns>Notes in the specified lane within the time range</returns>
        public IEnumerable<Note> GetActiveNotesForLane(int laneIndex, double songTimeMs, double lookAheadMs = 2000.0)
        {
            return GetActiveNotes(songTimeMs, lookAheadMs)
                .Where(note => note.LaneIndex == laneIndex);
        }

        /// <summary>
        /// Gets the next note after the specified time
        /// </summary>
        /// <param name="songTimeMs">Current song time in milliseconds</param>
        /// <returns>The next note, or null if no more notes</returns>
        public Note GetNextNote(double songTimeMs)
        {
            if (_notes.Count == 0)
                return null;

            // Find the first note after the given time using binary search
            var startIndex = BinarySearchStartIndex(songTimeMs + 0.001); // Add small epsilon to ensure we get notes AFTER songTimeMs
            
            return startIndex < _notes.Count ? _notes[startIndex] : null;
        }

        /// <summary>
        /// Gets the next note in a specific lane after the specified time
        /// </summary>
        /// <param name="laneIndex">Lane index (0-8)</param>
        /// <param name="songTimeMs">Current song time in milliseconds</param>
        /// <returns>The next note in the lane, or null if no more notes</returns>
        public Note GetNextNoteInLane(int laneIndex, double songTimeMs)
        {
            if (_notes.Count == 0)
                return null;

            // Find the first note after the given time using binary search
            var startIndex = BinarySearchStartIndex(songTimeMs + 0.001); // Add small epsilon to ensure we get notes AFTER songTimeMs
            
            // Search forward from the start index for a note in the specified lane
            for (int i = startIndex; i < _notes.Count; i++)
            {
                var note = _notes[i];
                if (note.LaneIndex == laneIndex)
                {
                    return note;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets notes that have passed the judgement line (for cleanup)
        /// </summary>
        /// <param name="songTimeMs">Current song time in milliseconds</param>
        /// <param name="gracePeriodMs">Grace period after judgement time (default 100ms)</param>
        /// <returns>Notes that have passed and can be cleaned up</returns>
        public IEnumerable<Note> GetPassedNotes(double songTimeMs, double gracePeriodMs = 100.0)
        {
            var cutoffTime = songTimeMs - gracePeriodMs;
            return _notes.Where(note => note.TimeMs < cutoffTime);
        }

        /// <summary>
        /// Gets statistics about the chart
        /// </summary>
        /// <returns>Chart statistics</returns>
        public ChartStatistics GetStatistics()
        {
            var stats = new ChartStatistics
            {
                TotalNotes = TotalNotes,
                DurationMs = DurationMs,
                Bpm = Bpm
            };

            // Calculate notes per lane - optimized single pass
            foreach (var note in _notes)
            {
                if (note.LaneIndex >= 0 && note.LaneIndex < 9)
                {
                    stats.NotesPerLane[note.LaneIndex]++;
                }
            }

            // Calculate note density (notes per second)
            if (DurationMs > 0)
            {
                stats.NoteDensity = TotalNotes / (DurationMs / 1000.0);
            }

            return stats;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Assign unique IDs to each note in the chart
        /// </summary>
        private void AssignNoteIds()
        {
            for (int i = 0; i < _notes.Count; i++)
            {
                _notes[i].Id = i;
            }
        }

        /// <summary>
        /// Finds the starting index for searching notes at a given time
        /// Uses optimization to avoid searching from the beginning each time
        /// </summary>
        private int FindStartIndex(double startTime)
        {
            // Start from last known position for sequential access optimization
            if (_lastActiveIndex < _notes.Count && _notes[_lastActiveIndex].TimeMs <= startTime)
            {
                // Search forward from last position
                while (_lastActiveIndex < _notes.Count && _notes[_lastActiveIndex].TimeMs < startTime)
                {
                    _lastActiveIndex++;
                }
            }
            else
            {
                // Binary search for the starting position
                _lastActiveIndex = BinarySearchStartIndex(startTime);
            }

            return _lastActiveIndex;
        }

        /// <summary>
        /// Binary search to find the first note at or after the given time
        /// </summary>
        public int BinarySearchStartIndex(double targetTime)
        {
            int left = 0;
            int right = _notes.Count - 1;
            int result = _notes.Count; // Default to end if not found

            while (left <= right)
            {
                int mid = left + (right - left) / 2;

                if (_notes[mid].TimeMs >= targetTime)
                {
                    result = mid;
                    right = mid - 1;
                }
                else
                {
                    left = mid + 1;
                }
            }

            return result;
        }

        #endregion
    }

    /// <summary>
    /// Statistics about a chart
    /// </summary>
    public class ChartStatistics
    {
        public int TotalNotes { get; set; }
        public double DurationMs { get; set; }
        public double Bpm { get; set; }
        public double NoteDensity { get; set; } // Notes per second
        public int[] NotesPerLane { get; set; } = new int[9]; // 9 lanes (0-8)

        public override string ToString()
        {
            return $"Chart Stats: {TotalNotes} notes, {DurationMs/1000:F1}s, {Bpm} BPM, {NoteDensity:F1} notes/sec";
        }
    }
}
