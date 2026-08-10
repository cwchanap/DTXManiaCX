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
        public int Id { get; internal set; }

        /// <summary>
        /// Lane index (0-9 for the 10 NX lanes)
        /// UPDATED mapping to match gameplay order LC, HH/HHC, LP, SN, HT, DB, LT, FT, CY, RD:
        /// 0x1A=0 (LC), 0x11=1 (HH), 0x1B=2 (LP), 0x12=3 (SN), 0x14=4 (HT), 0x13=5 (DB), 0x15=6 (LT), 0x16=7 (FT), 0x19=8 (CY), 0x16=9 (RD)
        /// </summary>
        public int LaneIndex { get; set; }

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
        /// Absolute time in milliseconds when this note should be hit.
        /// Assigned by <see cref="ParsedChart.FinalizeChart"/>.
        /// </summary>
        public double TimeMs { get; set; }

        /// <summary>
        /// DTX channel number (11-1C for NX lanes)
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
        /// <param name="laneIndex">Lane index (0-9)</param>
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
        /// Gets the lane name for display purposes
        /// Updated to match CORRECT gameplay order from left to right: LC, HH, LP, SN, HT, DB, LT, FT, CY, RD
        /// </summary>
        public string GetLaneName()
        {
            return LaneIndex switch
            {
                0 => "LC",  // Left Crash
                1 => "HH",  // Hi-Hat/Hi-Hat Close
                2 => "LP",  // Left Pedal
                3 => "SN",  // Snare Drum
                4 => "HT",  // High Tom
                5 => "DB",  // Bass Drum (Drum Bass)
                6 => "LT",  // Low Tom
                7 => "FT",  // Floor Tom
                8 => "CY",  // Cymbal (Right Crash)
                9 => "RD",  // Ride
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
