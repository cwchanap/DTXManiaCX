using System;
using System.Collections.Generic;

namespace DTX.Song
{
    /// <summary>
    /// Song metadata extracted from DTX files
    /// Based on DTXManiaNX CDTX metadata patterns
    /// </summary>
    public class SongMetadata
    {
        #region Basic Information

        /// <summary>
        /// Song title from #TITLE tag
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Artist name from #ARTIST tag
        /// </summary>
        public string Artist { get; set; } = "";

        /// <summary>
        /// Genre classification from #GENRE tag
        /// </summary>
        public string Genre { get; set; } = "";

        /// <summary>
        /// Song comment from #COMMENT tag
        /// </summary>
        public string Comment { get; set; } = "";

        #endregion

        #region Timing Information

        /// <summary>
        /// Base BPM from #BPM tag
        /// </summary>
        public double? BPM { get; set; }

        /// <summary>
        /// Song duration in seconds (calculated during parsing)
        /// </summary>
        public double? Duration { get; set; }

        #endregion

        #region Difficulty Levels

        /// <summary>
        /// Drum difficulty level (0-100)
        /// </summary>
        public int? DrumLevel { get; set; }

        /// <summary>
        /// Guitar difficulty level (0-100)
        /// </summary>
        public int? GuitarLevel { get; set; }

        /// <summary>
        /// Bass difficulty level (0-100)
        /// </summary>
        public int? BassLevel { get; set; }

        /// <summary>
        /// Difficulty labels for each instrument
        /// </summary>
        public Dictionary<string, string> DifficultyLabels { get; set; } = new();

        #endregion

        #region Media Files

        /// <summary>
        /// Preview sound file from #PREVIEW tag
        /// </summary>
        public string? PreviewFile { get; set; }

        /// <summary>
        /// Preview image file from #PREIMAGE tag
        /// </summary>
        public string? PreviewImage { get; set; }

        /// <summary>
        /// Background image file from #BACKGROUND tag
        /// </summary>
        public string? BackgroundImage { get; set; }

        /// <summary>
        /// Stage file from #STAGEFILE tag
        /// </summary>
        public string? StageFile { get; set; }

        #endregion

        #region File Information

        /// <summary>
        /// Source file path
        /// </summary>
        public string FilePath { get; set; } = "";

        /// <summary>
        /// File size in bytes
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// Last modified timestamp
        /// </summary>
        public DateTime LastModified { get; set; }

        /// <summary>
        /// File format extension (.dtx, .gda, .bms, etc.)
        /// </summary>
        public string FileFormat { get; set; } = "";

        #endregion

        #region Calculated Properties

        /// <summary>
        /// Gets the highest difficulty level across all instruments
        /// </summary>
        public int MaxDifficultyLevel
        {
            get
            {
                var levels = new[] { DrumLevel, GuitarLevel, BassLevel };
                var maxLevel = 0;
                foreach (var level in levels)
                {
                    if (level.HasValue && level.Value > maxLevel)
                        maxLevel = level.Value;
                }
                return maxLevel;
            }
        }

        /// <summary>
        /// Gets available instruments based on difficulty levels
        /// </summary>
        public List<string> AvailableInstruments
        {
            get
            {
                var instruments = new List<string>();
                if (DrumLevel.HasValue && DrumLevel.Value > 0) instruments.Add("DRUMS");
                if (GuitarLevel.HasValue && GuitarLevel.Value > 0) instruments.Add("GUITAR");
                if (BassLevel.HasValue && BassLevel.Value > 0) instruments.Add("BASS");
                return instruments;
            }
        }

        /// <summary>
        /// Gets display title (falls back to filename if title is empty)
        /// </summary>
        public string DisplayTitle
        {
            get
            {
                if (!string.IsNullOrEmpty(Title))
                    return Title;
                
                if (!string.IsNullOrEmpty(FilePath))
                    return System.IO.Path.GetFileNameWithoutExtension(FilePath);
                
                return "Unknown Song";
            }
        }

        /// <summary>
        /// Gets display artist (falls back to "Unknown" if empty)
        /// </summary>
        public string DisplayArtist => string.IsNullOrEmpty(Artist) ? "Unknown Artist" : Artist;

        /// <summary>
        /// Gets display genre (falls back to "Unknown" if empty)
        /// </summary>
        public string DisplayGenre => string.IsNullOrEmpty(Genre) ? "Unknown Genre" : Genre;

        #endregion

        #region Methods

        /// <summary>
        /// Creates a copy of this metadata
        /// </summary>
        public SongMetadata Clone()
        {
            return new SongMetadata
            {
                Title = Title,
                Artist = Artist,
                Genre = Genre,
                Comment = Comment,
                BPM = BPM,
                Duration = Duration,
                DrumLevel = DrumLevel,
                GuitarLevel = GuitarLevel,
                BassLevel = BassLevel,
                DifficultyLabels = new Dictionary<string, string>(DifficultyLabels),
                PreviewFile = PreviewFile,
                PreviewImage = PreviewImage,
                BackgroundImage = BackgroundImage,
                StageFile = StageFile,
                FilePath = FilePath,
                FileSize = FileSize,
                LastModified = LastModified,
                FileFormat = FileFormat
            };
        }

        /// <summary>
        /// Gets difficulty level for specified instrument
        /// </summary>
        public int? GetDifficultyLevel(string instrument)
        {
            return instrument.ToUpperInvariant() switch
            {
                "DRUMS" => DrumLevel,
                "GUITAR" => GuitarLevel,
                "BASS" => BassLevel,
                _ => null
            };
        }

        /// <summary>
        /// Sets difficulty level for specified instrument
        /// </summary>
        public void SetDifficultyLevel(string instrument, int level)
        {
            switch (instrument.ToUpperInvariant())
            {
                case "DRUMS":
                    DrumLevel = level;
                    break;
                case "GUITAR":
                    GuitarLevel = level;
                    break;
                case "BASS":
                    BassLevel = level;
                    break;
            }
        }

        #endregion
    }
}
