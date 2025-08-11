using System;

namespace DTXMania.Game.Lib.Song.Components
{
    /// <summary>
    /// Represents a single note in a DTX chart
    /// Used for gameplay note scrolling and timing
    /// </summary>
    public class Note
    {
        #region Properties

        /// <summary>
        /// Unique identifier for this note (used for runtime state tracking)
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Lane index (0-8 for the 9 NX lanes)
        /// Maps to DTX channels: 0x11=0 (LC), 0x12=1 (LP), 0x13=2 (HH), 0x14=3 (SD), 0x15=4 (BD), 0x16=5 (HT), 0x17=6 (LT), 0x18=7 (FT), 0x19=8 (CY)
        /// </summary>
        public int LaneIndex { get; set; }

        /// <summary>
        /// Bar number in the DTX file (0-based)
        /// </summary>
        public int Bar { get; set; }

        /// <summary>
        /// Tick position within the bar (0-191, 192 ticks per measure)
        /// </summary>
        public int Tick { get; set; }

        /// <summary>
        /// Absolute time in milliseconds when this note should be hit
        /// Calculated using: (((bar*192)+tick)/192) * (60000/BPM) * 4
        /// </summary>
        public double TimeMs { get; set; }

        /// <summary>
        /// DTX channel number (11-19 for NX lanes)
        /// </summary>
        public int Channel { get; set; }

        /// <summary>
        /// Note value from DTX file (usually hex pair like "01", "02", etc.)
        /// Used for different note types or sound references
        /// </summary>
        public string Value { get; set; } = "";

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new Note
        /// </summary>
        public Note()
        {
        }

        /// <summary>
        /// Creates a new Note with specified parameters
        /// </summary>
        /// <param name="laneIndex">Lane index (0-8)</param>
        /// <param name="bar">Bar number</param>
        /// <param name="tick">Tick position</param>
        /// <param name="channel">DTX channel number</param>
        /// <param name="value">Note value</param>
        public Note(int laneIndex, int bar, int tick, int channel, string value)
        {
            LaneIndex = laneIndex;
            Bar = bar;
            Tick = tick;
            Channel = channel;
            Value = value;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Calculates the absolute time in milliseconds for this note
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
        /// Gets the lane name for display purposes
        /// </summary>
        public string GetLaneName()
        {
            return LaneIndex switch
            {
                0 => "LC",  // Left Cymbal
                1 => "LP",  // Left Pedal
                2 => "HH",  // Hi-Hat
                3 => "SD",  // Snare Drum
                4 => "BD",  // Bass Drum
                5 => "HT",  // High Tom
                6 => "LT",  // Low Tom
                7 => "FT",  // Floor Tom
                8 => "CY",  // Cymbal
                _ => "??"
            };
        }

        /// <summary>
        /// Returns a string representation of this note
        /// </summary>
        public override string ToString()
        {
            return $"Note[{GetLaneName()}] Bar:{Bar} Tick:{Tick} Time:{TimeMs:F1}ms Value:{Value}";
        }

        #endregion
    }
}
