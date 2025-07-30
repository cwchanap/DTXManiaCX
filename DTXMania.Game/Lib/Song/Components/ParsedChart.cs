using System;
using System.Collections.Generic;
using System.Linq;

namespace DTX.Song.Components
{
    /// <summary>
    /// Represents a parsed DTX chart with notes and metadata
    /// Result of DTXChartParser.Parse() operation
    /// </summary>
    public class ParsedChart
    {
        #region Constants

        /// <summary>
        /// Buffer time added to chart duration to allow final notes to ring out
        /// </summary>
        private const double DurationEndBufferMs = 500.0;

        #endregion

        #region Properties

        /// <summary>
        /// List of all notes in the chart, sorted by time
        /// </summary>
        public List<Note> Notes { get; } = new List<Note>();

        /// <summary>
        /// List of all BGM events in the chart, sorted by time
        /// BGM events indicate when background music should start playing
        /// </summary>
        public List<BGMEvent> BGMEvents { get; } = new List<BGMEvent>();

        /// <summary>
        /// Base BPM of the song (from #BPM header)
        /// Default is 120 if not specified
        /// </summary>
        public double Bpm { get; set; } = 120.0;

        /// <summary>
        /// Path to the main background audio file (first #WAVxx referenced in lanes)
        /// Relative to the DTX file location
        /// </summary>
        public string BackgroundAudioPath { get; set; } = "";

        /// <summary>
        /// Original DTX file path
        /// </summary>
        public string FilePath { get; set; } = "";

        /// <summary>
        /// Total duration of the chart in milliseconds
        /// Calculated from the last note's timing
        /// </summary>
        public double DurationMs { get; set; }

        /// <summary>
        /// Number of notes per lane for statistics
        /// </summary>
        public Dictionary<int, int> NotesPerLane { get; private set; } = new Dictionary<int, int>();

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new ParsedChart
        /// </summary>
        public ParsedChart()
        {
            // Initialize notes per lane dictionary for all 9 lanes
            for (int i = 0; i < 9; i++)
            {
                NotesPerLane[i] = 0;
            }
        }

        /// <summary>
        /// Creates a new ParsedChart with specified file path
        /// </summary>
        /// <param name="filePath">Path to the DTX file</param>
        public ParsedChart(string filePath) : this()
        {
            FilePath = filePath;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Adds a note to the chart and updates statistics
        /// </summary>
        /// <param name="note">Note to add</param>
        public void AddNote(Note note)
        {
            if (note == null)
                return;

            // Calculate timing if not already set
            if (note.TimeMs == 0)
            {
                note.CalculateTimeMs(Bpm);
            }

            Notes.Add(note);

            // Update lane statistics
            if (note.LaneIndex >= 0 && note.LaneIndex < 9)
            {
                NotesPerLane[note.LaneIndex]++;
            }

            // Update duration
            DurationMs = Math.Max(DurationMs, note.TimeMs);
        }

        /// <summary>
        /// Adds a BGM event to the chart
        /// </summary>
        /// <param name="bgmEvent">BGM event to add</param>
        public void AddBGMEvent(BGMEvent bgmEvent)
        {
            if (bgmEvent == null)
                return;

            // Calculate timing if not already set
            if (bgmEvent.TimeMs == 0)
            {
                bgmEvent.CalculateTimeMs(Bpm);
            }

            BGMEvents.Add(bgmEvent);

            // Update duration (BGM events can also affect total duration)
            DurationMs = Math.Max(DurationMs, bgmEvent.TimeMs);
        }

        /// <summary>
        /// Finalizes the chart by sorting notes and calculating final statistics
        /// </summary>
        public void FinalizeChart()
        {
            // Sort notes by time for efficient rendering
            Notes.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));

            // Sort BGM events by time for efficient playback scheduling
            BGMEvents.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));

            // Debug: Report parsing summary
#if DEBUG
            var maxMeasure = Notes.Count > 0 ? Notes.Max(n => n.Bar) : 0;
            var totalNotes = Notes.Count;
            var totalBGMEvents = BGMEvents.Count;
            var lastNoteTime = Notes.Count > 0 ? Notes.Max(n => n.TimeMs) : 0;
            System.Diagnostics.Debug.WriteLine($"ParsedChart.FinalizeChart: {totalNotes} notes, {totalBGMEvents} BGM events, max measure: {maxMeasure}, last note time: {lastNoteTime:F1}ms, BPM: {Bpm}");
#endif

            // Add small buffer to duration for final note to ring out
            if (DurationMs > 0)
            {
                DurationMs += DurationEndBufferMs;
            }
        }

        /// <summary>
        /// Gets the total number of notes in the chart
        /// </summary>
        public int TotalNotes => Notes.Count;

        /// <summary>
        /// Gets notes within a specific time range
        /// </summary>
        /// <param name="startTimeMs">Start time in milliseconds</param>
        /// <param name="endTimeMs">End time in milliseconds</param>
        /// <returns>Notes within the time range</returns>
        public IEnumerable<Note> GetNotesInTimeRange(double startTimeMs, double endTimeMs)
        {
            return Notes.Where(n => n.TimeMs >= startTimeMs && n.TimeMs <= endTimeMs);
        }

        /// <summary>
        /// Gets notes for a specific lane
        /// </summary>
        /// <param name="laneIndex">Lane index (0-8)</param>
        /// <returns>Notes in the specified lane</returns>
        public IEnumerable<Note> GetNotesForLane(int laneIndex)
        {
            return Notes.Where(n => n.LaneIndex == laneIndex);
        }

        /// <summary>
        /// Returns a string representation of this chart
        /// </summary>
        public override string ToString()
        {
            return $"ParsedChart[{System.IO.Path.GetFileName(FilePath)}] BPM:{Bpm} Notes:{TotalNotes} Duration:{DurationMs/1000:F1}s";
        }

        #endregion
    }
}
