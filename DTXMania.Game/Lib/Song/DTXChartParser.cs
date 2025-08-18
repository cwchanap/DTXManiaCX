using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// DTX chart parser for gameplay note data and metadata
    /// Parses DTX files to extract notes for the 9 NX lanes (channels 11-19) and song metadata
    /// Consolidates functionality from DTXMetadataParser
    /// Based on DTXManiaNX parsing patterns
    /// </summary>
    public class DTXChartParser
    {
        #region Private Fields

        private static readonly string[] _supportedExtensions = { ".dtx", ".gda", ".g2d", ".bms", ".bme", ".bml" };
        private static bool _encodingProviderRegistered;

        #endregion

        #region Constants

        /// <summary>
        /// DTX channel to lane index mapping
        /// Original mapping that maintains UI display order
        /// </summary>
        private static readonly Dictionary<int, int> ChannelToLaneMap = new Dictionary<int, int>
        {
            // Lane 0: 1A (Splash/Crash)
            { 0x1A, 0 },

            // Lane 1: 18&11 (Floor Tom & Left Cymbal)
            { 0x18, 1 }, // FT - Floor Tom
            { 0x11, 1 }, // LC - Left Cymbal

            // Lane 2: 1B&1C (Hi-Hat Foot & Left Crash)
            { 0x1B, 2 }, // Hi-Hat Foot Pedal
            { 0x1C, 2 }, // Left Crash

            // Lane 3: 12 (Left Pedal)
            { 0x12, 3 }, // LP - Left Pedal

            // Lane 4: 14 (Snare Drum)
            { 0x14, 4 }, // SD - Snare Drum

            // Lane 5: 13 (Hi-Hat)
            { 0x13, 5 }, // HH - Hi-Hat

            // Lane 6: 15 (Bass Drum)
            { 0x15, 6 }, // BD - Bass Drum

            // Lane 7: 16 (High Tom)
            { 0x16, 7 }, // HT - High Tom

            // Lane 8: 17&19 (Low Tom & Right Cymbal)
            { 0x17, 8 }, // LT - Low Tom
            { 0x19, 8 }, // CY - Right Cymbal
        };

        /// <summary>
        /// Ticks per measure in DTX format
        /// </summary>
        private const int TicksPerMeasure = 192;



        #endregion

        #region Constructor

        /// <summary>
        /// Static constructor to initialize encoding provider
        /// </summary>
        static DTXChartParser()
        {
            // Register encoding provider for Shift_JIS support (only once)
            if (!_encodingProviderRegistered)
            {
                try
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    _encodingProviderRegistered = true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"DTXChartParser: Failed to register encoding provider: {ex.Message}");
                }
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Parses a DTX file and returns a ParsedChart with notes and metadata
        /// </summary>
        /// <param name="filePath">Path to the DTX file</param>
        /// <returns>ParsedChart containing notes and metadata</returns>
        public static async Task<ParsedChart> ParseAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException($"DTX file not found: {filePath}");

            var chart = new ParsedChart(filePath);
            var wavDefinitions = new Dictionary<string, string>(); // WAV ID -> file path

            // Try different encodings for Japanese text support
            var encodings = new List<Encoding>
            {
                Encoding.UTF8,
                Encoding.Default
            };

            // Try to add Shift_JIS if available
            try
            {
                encodings.Add(Encoding.GetEncoding("Shift_JIS"));
            }
            catch (ArgumentException)
            {
                // Shift_JIS not available, continue with available encodings
            }

            Exception lastException = null;

            foreach (var encoding in encodings)
            {
                try
                {
                    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var reader = new StreamReader(stream, encoding);

                    await ParseFileContentAsync(reader, chart, wavDefinitions);
                    
                    // If we got here without exception, parsing succeeded
                    break;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    // Try next encoding
                    continue;
                }
            }

            // If all encodings failed, throw the last exception
            if (chart.Notes.Count == 0 && lastException != null)
            {
                throw new InvalidOperationException($"Failed to parse DTX file with any encoding: {filePath}", lastException);
            }

            // Find background audio file
            FindBackgroundAudio(chart, wavDefinitions, filePath);

            // Finalize the chart
            chart.FinalizeChart();

            return chart;
        }

        /// <summary>
        /// Parses a DTX file and returns Song and SongChart entities (metadata parsing)
        /// Consolidates functionality from DTXMetadataParser
        /// </summary>
        /// <param name="filePath">Path to the DTX file</param>
        /// <returns>Tuple containing Song and SongChart entities</returns>
        public static async Task<(DTXMania.Game.Lib.Song.Entities.Song song, SongChart chart)> ParseSongEntitiesAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException($"DTX file not found: {filePath}");

            var fileInfo = new FileInfo(filePath);

            // Create Song entity (shared metadata)
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "",
                Artist = "",
                Genre = "",
                Comment = "",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Create SongChart entity (file-specific data)
            var chart = new SongChart
            {
                FilePath = filePath,
                FileSize = fileInfo.Length,
                LastModified = fileInfo.LastWriteTime,
                FileFormat = fileInfo.Extension.ToLowerInvariant(),
                Duration = 0.0,
                BGMAdjust = 0
            };

            // Validate file extension
            if (!IsSupported(chart.FileFormat))
            {
                // Set fallback title for unsupported files
                song.Title = Path.GetFileNameWithoutExtension(filePath);
                return (song, chart);
            }

            try
            {
                await ParseFileHeaderToEntitiesAsync(filePath, song, chart);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DTXChartParser: Failed to parse file header for {filePath}: {ex.Message}");
                // Return entities with basic file info even if parsing fails
                if (string.IsNullOrEmpty(song.Title))
                {
                    song.Title = Path.GetFileNameWithoutExtension(filePath);
                }
            }

            return (song, chart);
        }

        /// <summary>
        /// Checks if the file format is supported
        /// </summary>
        public static bool IsSupported(string extension)
        {
            return Array.Exists(_supportedExtensions, ext =>
                ext.Equals(extension, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Quick check if a file is a supported DTX format
        /// </summary>
        public static bool IsSupportedFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return Array.Exists(_supportedExtensions, ext => ext == extension);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Parses the content of a DTX file
        /// </summary>
        private static async Task ParseFileContentAsync(StreamReader reader, ParsedChart chart, Dictionary<string, string> wavDefinitions)
        {
            string line;
            bool inDataSection = false;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                line = line.Trim();

                // Skip empty lines and comments
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                    continue;

                // Check if we've reached the data section
                if (line.StartsWith('*') || line.StartsWith('[') || IsTimelineData(line))
                {
                    inDataSection = true;
                }

                if (!inDataSection)
                {
                    // Parse header commands
                    ParseHeaderCommand(line, chart, wavDefinitions);
                }
                else
                {
                    // Parse measure data
                    ParseMeasureData(line, chart);
                }
            }
        }

        /// <summary>
        /// Parses header commands like #BPM and #WAV definitions
        /// </summary>
        private static void ParseHeaderCommand(string line, ParsedChart chart, Dictionary<string, string> wavDefinitions)
        {
            if (!line.StartsWith('#'))
                return;

            var colonIndex = line.IndexOf(':');
            if (colonIndex == -1)
                return;

            var command = line.Substring(0, colonIndex).Trim().ToUpperInvariant();
            var value = line.Substring(colonIndex + 1).Trim();

            // Remove quotes if present
            if (value.StartsWith('"') && value.EndsWith('"') && value.Length > 1)
            {
                value = value.Substring(1, value.Length - 2);
            }

            switch (command)
            {
                case "#BPM":
                    if (TryParseDouble(value, out var bpm))
                        chart.Bpm = bpm;
                    break;

                default:
                    // Check for WAV definitions: #WAV01, #WAV02, etc.
                    if (command.StartsWith("#WAV") && command.Length > 4)
                    {
                        var wavId = command.Substring(4);
                        wavDefinitions[wavId] = value;
                        // Found WAV definition
                    }
                    break;
            }
        }

        /// <summary>
        /// Parses measure data lines for note information
        /// </summary>
        private static void ParseMeasureData(string line, ParsedChart chart)
        {
            if (!IsTimelineData(line) || !line.Contains(':'))
                return;

            var colonIndex = line.IndexOf(':');
            var measureChannelPart = line.Substring(1, colonIndex - 1); // Remove # or *
            var noteData = line.Substring(colonIndex + 1).Trim();

            // Parse measure and channel
            if (!TryParseMeasureAndChannel(measureChannelPart, out int measure, out int channel))
                return;

            // Check if this is BGM channel (01)
            if (channel == 0x01)
            {
                // Parse BGM events from channel 01
                ParseBGMEvents(noteData, measure, chart);
                return;
            }

            // Check if this is a drum lane channel (11-19, 1A-1C)
            if (!ChannelToLaneMap.TryGetValue(channel, out var laneIndex))
            {
                // Skipping unmapped channel
                return;
            }


            // Parse notes from the data
            var notesBefore = chart.Notes.Count;
            ParseNotesFromData(noteData, measure, channel, laneIndex, chart);
            var notesAdded = chart.Notes.Count - notesBefore;

        }

        /// <summary>
        /// Parses BGM events from channel 01 measure data
        /// </summary>
        private static void ParseBGMEvents(string noteData, int measure, ParsedChart chart)
        {
            if (string.IsNullOrWhiteSpace(noteData))
                return;

            // DTX uses pairs of characters to represent BGM events
            // Each pair represents one BGM event position within the measure
            var pairCount = noteData.Length / 2;
            if (pairCount == 0)
                return;

            for (int i = 0; i < pairCount; i++)
            {
                if (i * 2 + 1 >= noteData.Length)
                    break;

                var pair = noteData.Substring(i * 2, 2);

                // Skip empty BGM events (00)
                if (pair == "00" || string.IsNullOrWhiteSpace(pair))
                    continue;

                // Calculate tick position within the measure
                var tick = (int)((double)i / pairCount * TicksPerMeasure);

                // Create BGM event
                var bgmEvent = new BGMEvent(measure, tick, pair);
                chart.AddBGMEvent(bgmEvent);
            }
        }

        /// <summary>
        /// Parses individual notes from measure data
        /// </summary>
        private static void ParseNotesFromData(string noteData, int measure, int channel, int laneIndex, ParsedChart chart)
        {
            if (string.IsNullOrWhiteSpace(noteData))
                return;

            // DTX uses pairs of characters to represent notes
            // Each pair represents one note position within the measure
            var pairCount = noteData.Length / 2;
            if (pairCount == 0)
                return;

            for (int i = 0; i < pairCount; i++)
            {
                if (i * 2 + 1 >= noteData.Length)
                    break;

                var pair = noteData.Substring(i * 2, 2);

                // Skip empty notes (00)
                if (pair == "00" || string.IsNullOrWhiteSpace(pair))
                    continue;

                // Calculate tick position within the measure
                // Fixed: Use i (pair index) and pairCount for proper positioning
                var tick = (int)((double)i / pairCount * TicksPerMeasure);

                // Debug logging for timing validation
#if DEBUG
                if (chart.Notes.Count < 10) // Only log first 10 notes
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"DTX Parse: Measure {measure}, Channel {channel:X2}, Pair {i}/{pairCount}, Tick {tick}, Value '{pair}'");
                }
#endif

                // Create note
                var note = new Note(laneIndex, measure, tick, channel, pair);
                chart.AddNote(note);
            }
        }

        /// <summary>
        /// Checks if a line contains timeline data (measure data)
        /// </summary>
        private static bool IsTimelineData(string line)
        {
            if (line.StartsWith('#') && line.Length >= 6)
            {
                var measurePart = line.Substring(1, 5);
                return measurePart.All(c => char.IsDigit(c) || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'));
            }
            return false;
        }

        /// <summary>
        /// Tries to parse measure and channel from measure data line
        /// </summary>
        private static bool TryParseMeasureAndChannel(string measureChannelPart, out int measure, out int channel)
        {
            measure = 0;
            channel = 0;

            if (measureChannelPart.Length != 5) // MMMCC format
                return false;

            var measureStr = measureChannelPart.Substring(0, 3); // First 3 chars = measure
            var channelStr = measureChannelPart.Substring(3, 2); // Last 2 chars = channel

            return int.TryParse(measureStr, out measure) &&
                   int.TryParse(channelStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out channel);
        }

        /// <summary>
        /// Finds the background audio file from WAV definitions and resolves BGM event file paths
        /// </summary>
        private static void FindBackgroundAudio(ParsedChart chart, Dictionary<string, string> wavDefinitions, string dtxFilePath)
        {
            // Resolve BGM event file paths first
            foreach (var bgmEvent in chart.BGMEvents)
            {
                if (wavDefinitions.TryGetValue(bgmEvent.WavId, out string wavPath))
                {
                    bgmEvent.AudioFilePath = ResolveBGMPath(wavPath, dtxFilePath);
                }
            }

            // Find the background music WAV file for legacy compatibility
            // Look for common background music filenames first
            string backgroundWav = null;

            // Strategy 1: Look for common background music filenames
            var commonBgmNames = new[] { "bgm.ogg", "bgm.wav", "bgm.mp3", "background.ogg", "background.wav", "background.mp3" };
            foreach (var bgmName in commonBgmNames)
            {
                var matchingWav = wavDefinitions.Values.FirstOrDefault(wav =>
                    string.Equals(Path.GetFileName(wav), bgmName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(matchingWav))
                {
                    backgroundWav = matchingWav;
                    // Found background music by filename
                    break;
                }
            }

            // Strategy 2: If no common BGM name found and no BGM events, use the first WAV as fallback
            if (string.IsNullOrEmpty(backgroundWav) && chart.BGMEvents.Count == 0)
            {
                backgroundWav = wavDefinitions.Values.FirstOrDefault();
                // Using first WAV as background music fallback
            }

            if (!string.IsNullOrEmpty(backgroundWav))
            {
                chart.BackgroundAudioPath = ResolveBGMPath(backgroundWav, dtxFilePath);
                // Background audio path resolved
            }
        }

        /// <summary>
        /// Resolves a BGM file path relative to the DTX file location
        /// </summary>
        private static string ResolveBGMPath(string wavPath, string dtxFilePath)
        {
            // Check if the WAV path is already absolute
            if (Path.IsPathRooted(wavPath))
            {
                return wavPath;
            }

            // Try different resolution strategies

            // Strategy 1: Path as-is (relative to working directory)
            if (File.Exists(wavPath))
            {
                return wavPath;
            }

            // Strategy 2: Relative to DTX file directory
            var dtxDirectory = Path.GetDirectoryName(dtxFilePath) ?? "";
            var dtxRelativePath = Path.Combine(dtxDirectory, wavPath);

            if (File.Exists(dtxRelativePath))
            {
                return dtxRelativePath;
            }

            // Strategy 3: Use the path as-is even if file doesn't exist (let AudioLoader handle the error)
            return wavPath;
        }

        /// <summary>
        /// Safely parses a double value
        /// </summary>
        private static bool TryParseDouble(string value, out double result)
        {
            result = 0.0;
            if (string.IsNullOrEmpty(value))
                return false;

            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        /// <summary>
        /// Parses the header section of a DTX file to Song and SongChart entities
        /// </summary>
        private static async Task ParseFileHeaderToEntitiesAsync(string filePath, DTXMania.Game.Lib.Song.Entities.Song song, SongChart chart)
        {
            // Try different encodings for Japanese text support
            var encodings = new List<Encoding>
            {
                Encoding.UTF8,
                Encoding.Default
            };

            // Try to add Shift_JIS if available
            try
            {
                encodings.Add(Encoding.GetEncoding("Shift_JIS"));
            }
            catch (ArgumentException)
            {
                // Shift_JIS not available, continue with available encodings
            }

            DTXMania.Game.Lib.Song.Entities.Song bestSong = null;
            SongChart bestChart = null;

            foreach (var encoding in encodings)
            {
                try
                {
                    var tempSong = new DTXMania.Game.Lib.Song.Entities.Song();
                    var tempChart = new SongChart();

                    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var reader = new StreamReader(stream, encoding);

                    await ParseHeaderLinesToEntitiesAsync(reader, tempSong, tempChart);

                    // Calculate duration by parsing measure data
                    stream.Seek(0, SeekOrigin.Begin); // Reset to beginning
                    using var durationReader = new StreamReader(stream, encoding);
                    await CalculateDurationAsync(durationReader, tempChart);

                    // Check if we successfully parsed some metadata
                    if (!string.IsNullOrEmpty(tempSong.Title) || !string.IsNullOrEmpty(tempSong.Artist) ||
                        tempChart.DrumLevel > 0 || tempChart.GuitarLevel > 0 || tempChart.BassLevel > 0)
                    {
                        // Validate that the text is properly decoded
                        if (IsTextProperlyDecoded(tempSong.Title) && IsTextProperlyDecoded(tempSong.Artist))
                        {
                            bestSong = tempSong;
                            bestChart = tempChart;
                            break;
                        }
                        else if (bestSong == null)
                        {
                            bestSong = tempSong;
                            bestChart = tempChart;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"DTXChartParser: Failed to parse with encoding {encoding.EncodingName}: {ex.Message}");
                    continue;
                }
            }

            // Use the best result found
            if (bestSong != null)
            {
                song.Title = bestSong.Title;
                song.Artist = bestSong.Artist;
                song.Genre = bestSong.Genre;
                song.Comment = bestSong.Comment;

                // Copy chart properties
                chart.Bpm = bestChart.Bpm;
                chart.Duration = bestChart.Duration;
                chart.BGMAdjust = bestChart.BGMAdjust;
                chart.DrumLevel = bestChart.DrumLevel;
                chart.DrumLevelDec = bestChart.DrumLevelDec;
                chart.GuitarLevel = bestChart.GuitarLevel;
                chart.GuitarLevelDec = bestChart.GuitarLevelDec;
                chart.BassLevel = bestChart.BassLevel;
                chart.BassLevelDec = bestChart.BassLevelDec;
                chart.HasDrumChart = bestChart.HasDrumChart;
                chart.HasGuitarChart = bestChart.HasGuitarChart;
                chart.HasBassChart = bestChart.HasBassChart;
                chart.DrumNoteCount = bestChart.DrumNoteCount;
                chart.GuitarNoteCount = bestChart.GuitarNoteCount;
                chart.BassNoteCount = bestChart.BassNoteCount;
                chart.PreviewFile = bestChart.PreviewFile;
                chart.PreviewImage = bestChart.PreviewImage;
                chart.BackgroundFile = bestChart.BackgroundFile;
                chart.StageFile = bestChart.StageFile;
                chart.FileFormat = bestChart.FileFormat;
            }

            // Set fallback title if none was found
            if (string.IsNullOrEmpty(song.Title))
            {
                song.Title = Path.GetFileNameWithoutExtension(filePath);
            }
        }

        /// <summary>
        /// Validates that text is properly decoded and not corrupted
        /// </summary>
        private static bool IsTextProperlyDecoded(string text)
        {
            if (string.IsNullOrEmpty(text))
                return true; // Empty text is considered valid

            // Check for common encoding corruption indicators
            if (text.Contains("�") || // Unicode replacement character
                text.Contains("??") || // Multiple question marks
                text.Contains("???")) // Multiple question marks
            {
                return false;
            }

            // Check for invalid character sequences that indicate encoding issues
            int consecutiveInvalidChars = 0;
            foreach (char c in text)
            {
                // Check for characters that often appear in encoding corruption
                if (c == '�' || c == '\uFFFD' || // Unicode replacement characters
                    (c >= 0x80 && c <= 0x9F) || // Control characters in ISO-8859-1 range
                    c == '?' && consecutiveInvalidChars > 0) // Multiple question marks
                {
                    consecutiveInvalidChars++;
                    if (consecutiveInvalidChars >= 2)
                        return false;
                }
                else if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')
                {
                    // Invalid control characters
                    return false;
                }
                else
                {
                    consecutiveInvalidChars = 0;
                }
            }

            return true;
        }

        /// <summary>
        /// Parses header lines from the DTX file to Song and SongChart entities
        /// </summary>
        private static async Task ParseHeaderLinesToEntitiesAsync(StreamReader reader, DTXMania.Game.Lib.Song.Entities.Song song, SongChart chart)
        {
            string line;
            const int maxHeaderLines = 200; // Limit header parsing to first 200 lines
            var processedHeaderLines = 0;

            while ((line = await reader.ReadLineAsync()) != null && processedHeaderLines < maxHeaderLines)
            {
                processedHeaderLines++;

                // Skip empty lines and comments
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                    continue;

                // Stop parsing when we reach the data section
                if (line.StartsWith('*') || line.Contains('|') || line.StartsWith('['))
                    break;

                // Parse header commands
                if (line.StartsWith('#'))
                {
                    ParseHeaderCommandToEntities(line, song, chart);
                }
            }
        }

        /// <summary>
        /// Parses a single header command line to Song and SongChart entities
        /// </summary>
        private static void ParseHeaderCommandToEntities(string line, DTXMania.Game.Lib.Song.Entities.Song song, SongChart chart)
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex == -1) return;

            var command = line.Substring(0, colonIndex).Trim().ToUpperInvariant();
            var value = line.Substring(colonIndex + 1).Trim();

            // Remove quotes if present
            if (value.StartsWith('"') && value.EndsWith('"') && value.Length > 1)
            {
                value = value.Substring(1, value.Length - 2);
            }

            switch (command)
            {
                // Song-level metadata (shared across charts)
                case "#TITLE":
                    song.Title = value;
                    break;

                case "#ARTIST":
                    song.Artist = value;
                    break;

                case "#GENRE":
                    song.Genre = value;
                    break;

                case "#COMMENT":
                    song.Comment = value;
                    break;

                // Chart-level metadata (file-specific)
                case "#BPM":
                    if (TryParseDouble(value, out var bpm))
                        chart.Bpm = bpm;
                    break;

                case "#LEVEL":
                    ParseLevelDataToChart(value, chart);
                    break;

                case "#DLEVEL":
                    if (TryParseInt(value, out var drumLevel))
                    {
                        chart.DrumLevel = drumLevel;
                        chart.HasDrumChart = drumLevel > 0;
                    }
                    break;

                case "#GLEVEL":
                    if (TryParseInt(value, out var guitarLevel))
                    {
                        chart.GuitarLevel = guitarLevel;
                        chart.HasGuitarChart = guitarLevel > 0;
                    }
                    break;

                case "#BLEVEL":
                    if (TryParseInt(value, out var bassLevel))
                    {
                        chart.BassLevel = bassLevel;
                        chart.HasBassChart = bassLevel > 0;
                    }
                    break;

                case "#PREVIEW":
                    chart.PreviewFile = value;
                    break;

                case "#PREIMAGE":
                    chart.PreviewImage = value;
                    break;

                case "#BACKGROUND":
                case "#WALL":
                    chart.BackgroundFile = value;
                    break;

                case "#STAGEFILE":
                    chart.StageFile = value;
                    break;

                // Difficulty labels stored in chart
                case "#DLABEL":
                    chart.DifficultyLabels["DRUMS"] = value;
                    break;

                case "#GLABEL":
                    chart.DifficultyLabels["GUITAR"] = value;
                    break;

                case "#BLABEL":
                    chart.DifficultyLabels["BASS"] = value;
                    break;
            }
        }

        /// <summary>
        /// Parses level data in format "DRUMS:85,GUITAR:78,BASS:65" to SongChart
        /// </summary>
        private static void ParseLevelDataToChart(string levelData, SongChart chart)
        {
            if (string.IsNullOrEmpty(levelData)) return;

            var parts = levelData.Split(',');
            foreach (var part in parts)
            {
                var instrumentLevel = part.Split(':');
                if (instrumentLevel.Length == 2)
                {
                    var instrument = instrumentLevel[0].Trim().ToUpperInvariant();
                    if (TryParseInt(instrumentLevel[1].Trim(), out var level))
                    {
                        switch (instrument)
                        {
                            case "DRUMS":
                            case "DRUM":
                                chart.DrumLevel = level;
                                chart.HasDrumChart = level > 0;
                                break;
                            case "GUITAR":
                                chart.GuitarLevel = level;
                                chart.HasGuitarChart = level > 0;
                                break;
                            case "BASS":
                                chart.BassLevel = level;
                                chart.HasBassChart = level > 0;
                                break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Safely parses an integer value
        /// </summary>
        private static bool TryParseInt(string value, out int result)
        {
            result = 0;
            if (string.IsNullOrEmpty(value)) return false;

            // Handle decimal values by truncating
            if (value.Contains('.'))
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
                {
                    result = (int)Math.Round(doubleValue);
                    return true;
                }
            }

            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        /// <summary>
        /// Calculates song duration by parsing measure data (simplified version)
        /// </summary>
        private static async Task CalculateDurationAsync(StreamReader reader, SongChart chart)
        {
            if (chart.Bpm <= 0)
            {
                return;
            }

            try
            {
                double currentBpm = chart.Bpm;
                int lastMeasureWithNotes = 0;
                int drumNoteCount = 0;
                int guitarNoteCount = 0;
                int bassNoteCount = 0;

                string line;
                bool inDataSection = false;

                while ((line = await reader.ReadLineAsync()) != null)
                {
                    line = line.Trim();

                    // Skip empty lines and comments
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                        continue;

                    // Check if we've reached the data section
                    if (line.StartsWith('*') || line.StartsWith('[') || IsTimelineDataForDuration(line))
                    {
                        inDataSection = true;
                    }

                    if (!inDataSection)
                        continue;

                    // Parse measure data
                    if ((line.StartsWith('*') || IsTimelineDataForDuration(line)) && line.Contains(':'))
                    {
                        var measure = ParseMeasureLineForDuration(line);
                        var hasNotes = HasNoteDataForDuration(line);

                        if (measure.HasValue && hasNotes)
                        {
                            lastMeasureWithNotes = Math.Max(lastMeasureWithNotes, measure.Value);

                            // Count notes
                            var channel = ParseChannelFromLineForDuration(line);
                            if (channel.HasValue)
                            {
                                int noteCount = CountNotesInLineForDuration(line);
                                if (noteCount > 0)
                                {
                                    if (IsDrumChannelForDuration(channel.Value))
                                        drumNoteCount += noteCount;
                                    else if (IsGuitarChannelForDuration(channel.Value))
                                        guitarNoteCount += noteCount;
                                    else if (IsBassChannelForDuration(channel.Value))
                                        bassNoteCount += noteCount;
                                }
                            }
                        }
                    }
                }

                // Set note counts
                chart.DrumNoteCount = drumNoteCount;
                chart.GuitarNoteCount = guitarNoteCount;
                chart.BassNoteCount = bassNoteCount;

                // Calculate duration (simplified: 4 beats per measure * 60 seconds / BPM)
                if (lastMeasureWithNotes > 0)
                {
                    double totalDuration = (lastMeasureWithNotes + 1) * 4.0 * 60.0 / currentBpm + 0.5; // Add 0.5s buffer
                    chart.Duration = totalDuration;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DTXChartParser: Failed to calculate duration: {ex.Message}");
            }
        }

        // Helper methods for duration calculation (simplified versions)
        private static bool IsTimelineDataForDuration(string line)
        {
            if (line.StartsWith('#') && line.Length >= 6)
            {
                var measurePart = line.Substring(1, 5);
                return measurePart.All(c => char.IsDigit(c) || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'));
            }
            return false;
        }

        private static int? ParseMeasureLineForDuration(string line)
        {
            try
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex == -1) return null;

                if (line.StartsWith('#'))
                {
                    var measureChannelPart = line.Substring(1, colonIndex - 1);
                    if (measureChannelPart.Length == 5)
                    {
                        var measureStr = measureChannelPart.Substring(0, 3);
                        if (int.TryParse(measureStr, out int measure))
                            return measure;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DTXChartParser] Error parsing measure line '{line}': {ex.Message}");
            }
            return null;
        }

        private static bool HasNoteDataForDuration(string line)
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex == -1 || colonIndex + 1 >= line.Length) return false;

            var noteData = line.Substring(colonIndex + 1).Trim();
            return noteData.Any(c => c != '0' && c != ' ' && c != '\t');
        }

        private static int? ParseChannelFromLineForDuration(string line)
        {
            try
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex == -1) return null;

                if (line.StartsWith('#'))
                {
                    var measureChannelPart = line.Substring(1, colonIndex - 1);
                    if (measureChannelPart.Length == 5)
                    {
                        var channelStr = measureChannelPart.Substring(3, 2);
                        if (int.TryParse(channelStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int channel))
                            return channel;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DTXChartParser] Error parsing channel from line '{line}': {ex.Message}");
            }
            return null;
        }

        private static int CountNotesInLineForDuration(string line)
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex == -1 || colonIndex + 1 >= line.Length) return 0;

            var noteData = line.Substring(colonIndex + 1).Trim();
            int noteCount = 0;

            for (int i = 0; i < noteData.Length; i += 2)
            {
                if (i + 1 < noteData.Length)
                {
                    var pair = noteData.Substring(i, 2);
                    if (pair != "00" && !string.IsNullOrWhiteSpace(pair))
                        noteCount++;
                }
            }

            return noteCount;
        }

        private static bool IsDrumChannelForDuration(int channel)
        {
            return channel == 0x11 || channel == 0x12 || channel == 0x13 || channel == 0x14 ||
                   channel == 0x15 || channel == 0x16 || channel == 0x17 || channel == 0x18 ||
                   channel == 0x19 || channel == 0x1A || channel == 0x1B || channel == 0x1C;
        }

        private static bool IsGuitarChannelForDuration(int _) => false; // All charts are drum-only

        private static bool IsBassChannelForDuration(int _) => false; // All charts are drum-only

        #endregion
    }
}
