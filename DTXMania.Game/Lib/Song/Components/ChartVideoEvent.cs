namespace DTXMania.Game.Lib.Song.Components
{
    /// <summary>
    /// Represents a whole-file background video trigger with timing information
    /// Video events are parsed from DTX channels 54 and 5A (#AVIxx / #VIDEOxx
    /// definitions) and indicate when the chart's background video should start
    /// </summary>
    public class ChartVideoEvent
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
        /// Absolute time in milliseconds when this video should start.
        /// Assigned by <see cref="ParsedChart.FinalizeChart"/>.
        /// </summary>
        public double TimeMs { get; set; }

        /// <summary>
        /// Video reference ID from DTX file (e.g., "01", "02", etc.)
        /// References a #AVIxx / #VIDEOxx definition in the DTX file
        /// </summary>
        public string VideoId { get; set; } = "";

        /// <summary>
        /// Resolved file path to the video file
        /// Set during parsing based on the final video definitions.
        /// Empty when the referenced definition is missing.
        /// </summary>
        public string VideoFilePath { get; set; } = "";

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new ChartVideoEvent
        /// </summary>
        public ChartVideoEvent()
        {
        }

        /// <summary>
        /// Creates a new ChartVideoEvent with specified parameters
        /// </summary>
        /// <param name="bar">Bar number</param>
        /// <param name="tick">Tick position</param>
        /// <param name="videoId">Video reference ID</param>
        public ChartVideoEvent(int bar, int tick, string videoId)
        {
            Bar = bar;
            Tick = tick;
            VideoId = videoId;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Returns a string representation of this video event
        /// </summary>
        public override string ToString()
        {
            return $"ChartVideoEvent[{VideoId}] Bar:{Bar} Tick:{Tick} Time:{TimeMs:F1}ms Path:{System.IO.Path.GetFileName(VideoFilePath)}";
        }

        #endregion
    }
}
