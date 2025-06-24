namespace DTXMania.Game.Lib.Song.Entities
{
    /// <summary>
    /// Instrument part enumeration for DTXMania
    /// Based on DTXManiaNX EInstrumentPart patterns
    /// </summary>
    public enum EInstrumentPart
    {
        /// <summary>
        /// Drums instrument
        /// </summary>
        DRUMS = 0,

        /// <summary>
        /// Guitar instrument
        /// </summary>
        GUITAR = 1,

        /// <summary>
        /// Bass instrument
        /// </summary>
        BASS = 2
    }

    /// <summary>
    /// Node type enumeration for song hierarchy
    /// Based on DTXManiaNX patterns
    /// </summary>
    public enum ENodeType
    {
        /// <summary>
        /// Individual song file
        /// </summary>
        Song = 0,

        /// <summary>
        /// Folder container (BOX)
        /// </summary>
        Box = 1,

        /// <summary>
        /// Back/parent navigation item
        /// </summary>
        BackBox = 2,

        /// <summary>
        /// Random song selection placeholder
        /// </summary>
        Random = 3
    }
}
