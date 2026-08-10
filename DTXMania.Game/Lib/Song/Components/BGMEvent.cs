namespace DTXMania.Game.Lib.Song.Components
{
    /// <summary>
    /// Represents a BGM (Background Music) event with timing information
    /// BGM events are parsed from DTX channel 01 and indicate when background audio should start
    /// </summary>
    public class BGMEvent
    {
        #region Properties

        /// <summary>
        /// Bar number in the DTX file (0-based)
        /// </summary>
        public int Bar { get; set; }

        /// <summary>
        /// Authored tick position within the bar. Canonical positions are 0-191;
        /// oversized values are normalized during chart finalization.
        /// </summary>
        public int Tick { get; set; }

        /// <summary>
        /// Absolute time in milliseconds when this BGM should start.
        /// Assigned by <see cref="ParsedChart.FinalizeChart"/>.
        /// </summary>
        public double TimeMs { get; set; }

        /// <summary>
        /// WAV reference ID from DTX file (e.g., "01", "02", etc.)
        /// References a #WAVxx definition in the DTX header
        /// </summary>
        public string WavId { get; set; } = "";

        /// <summary>
        /// Resolved file path to the BGM audio file
        /// Set during parsing based on WAV definitions
        /// </summary>
        public string AudioFilePath { get; set; } = "";

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new BGMEvent
        /// </summary>
        public BGMEvent()
        {
        }

        /// <summary>
        /// Creates a new BGMEvent with specified parameters
        /// </summary>
        /// <param name="bar">Bar number</param>
        /// <param name="tick">Tick position</param>
        /// <param name="wavId">WAV reference ID</param>
        public BGMEvent(int bar, int tick, string wavId)
        {
            Bar = bar;
            Tick = tick;
            WavId = wavId;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Returns a string representation of this BGM event
        /// </summary>
        public override string ToString()
        {
            return $"BGMEvent[{WavId}] Bar:{Bar} Tick:{Tick} Time:{TimeMs:F1}ms Path:{System.IO.Path.GetFileName(AudioFilePath)}";
        }

        #endregion
    }
}
