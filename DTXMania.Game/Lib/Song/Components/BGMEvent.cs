using System;

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
        /// Tick position within the bar (0-191, 192 ticks per measure)
        /// </summary>
        public int Tick { get; set; }

        /// <summary>
        /// Absolute time in milliseconds when this BGM should start
        /// Calculated using: (((bar*192)+tick)/192) * (60000/BPM) * 4
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
        /// Calculates the absolute time in milliseconds for this BGM event
        /// </summary>
        /// <param name="bpm">Base BPM of the song</param>
        public void CalculateTimeMs(double bpm)
        {
            if (bpm <= 0)
                throw new ArgumentException("BPM must be greater than 0", nameof(bpm));

            // Formula: (((bar*192)+tick)/192) * (60000/BPM) * 4 (4 beats per measure in 4/4 time)
            var totalTicks = (Bar * 192) + Tick;
            var measures = totalTicks / 192.0;  // 192 ticks = 1 measure
            TimeMs = measures * (60000.0 / bpm) * 4.0;  // 4 beats per measure
        }

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