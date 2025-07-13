using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Song.Entities;

namespace DTX.Song
{
    /// <summary>
    /// DTX file metadata parser
    /// Based on DTXManiaNX CDTX parsing patterns
    /// </summary>
    public class DTXMetadataParser
    {
        #region Private Fields

        private readonly string[] _supportedExtensions = { ".dtx", ".gda", ".g2d", ".bms", ".bme", ".bml" };
        private static bool _encodingProviderRegistered = false;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes the DTX metadata parser
        /// </summary>
        public DTXMetadataParser()
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
                    Debug.WriteLine($"DTXMetadataParser: Failed to register encoding provider: {ex.Message}");
                }
            }
        }

        #endregion

        #region Public Methods

        // Legacy ParseMetadataAsync method removed - use ParseSongEntitiesAsync instead

        /// <summary>
        /// Parses a DTX file and returns Song and SongChart entities
        /// </summary>
        /// <param name="filePath">Path to the DTX file</param>
        /// <returns>Tuple containing Song and SongChart entities</returns>
        public async Task<(DTXMania.Game.Lib.Song.Entities.Song song, SongChart chart)> ParseSongEntitiesAsync(string filePath)
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
                Debug.WriteLine($"DTXMetadataParser: Failed to parse file header for {filePath}: {ex.Message}");
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
        public bool IsSupported(string extension)
        {
            return Array.Exists(_supportedExtensions, ext => 
                ext.Equals(extension, StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region Private Methods

        // Legacy ParseFileHeaderAsync method removed - using ParseFileHeaderToEntitiesAsync instead

        /// <summary>
        /// Parses the header section of a DTX file to Song and SongChart entities
        /// </summary>
        private async Task ParseFileHeaderToEntitiesAsync(string filePath, DTXMania.Game.Lib.Song.Entities.Song song, SongChart chart)
        {
            // Try different encodings for Japanese text support
            var encodings = new List<Encoding>
            {
                Encoding.UTF8,
                Encoding.Default
            };

            // Try to add Shift_JIS if available (requires System.Text.Encoding.CodePages)
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
            string bestEncodingName = null;

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
                    await CalculateDurationAsync(durationReader, tempChart, filePath);
                    
                    // Check if we successfully parsed some metadata (song info or chart levels)
                    if (!string.IsNullOrEmpty(tempSong.Title) || !string.IsNullOrEmpty(tempSong.Artist) ||
                        tempChart.DrumLevel > 0 || tempChart.GuitarLevel > 0 || tempChart.BassLevel > 0)
                    {
                        // Validate that the text is properly decoded (not corrupted)
                        if (IsTextProperlyDecoded(tempSong.Title) && IsTextProperlyDecoded(tempSong.Artist))
                        {
                            // This encoding produced valid text
                            bestSong = tempSong;
                            bestChart = tempChart;
                            bestEncodingName = encoding.EncodingName;
                            break;
                        }
                        else
                        {
                            // Keep this as fallback if no better encoding is found
                            if (bestSong == null)
                            {
                                bestSong = tempSong;
                                bestChart = tempChart;
                                bestEncodingName = encoding.EncodingName;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"DTXMetadataParser: Failed to parse with encoding {encoding.EncodingName}: {ex.Message}");
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

        // Legacy ParseHeaderLines method removed - using ParseHeaderLinesToEntitiesAsync instead
        
        /// <summary>
        /// Validates that text is properly decoded and not corrupted
        /// </summary>
        private bool IsTextProperlyDecoded(string text)
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
            // Look for sequences of characters that don't make sense in any language
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
        private async Task ParseHeaderLinesToEntitiesAsync(StreamReader reader, DTXMania.Game.Lib.Song.Entities.Song song, SongChart chart)
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
                if (line.StartsWith("*") || line.Contains("|") || line.StartsWith("["))
                    break;

                // Parse header commands
                if (line.StartsWith("#"))
                {
                    ParseHeaderCommandToEntities(line, song, chart);
                }
            }
        }

        // Legacy ParseHeaderCommand method removed - using ParseHeaderCommandToEntities instead

        /// <summary>
        /// Parses a single header command line to Song and SongChart entities
        /// </summary>
        private void ParseHeaderCommandToEntities(string line, DTXMania.Game.Lib.Song.Entities.Song song, SongChart chart)
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex == -1) return;

            var command = line.Substring(0, colonIndex).Trim().ToUpperInvariant();
            var value = line.Substring(colonIndex + 1).Trim();

            // Remove quotes if present
            if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length > 1)
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

        // Legacy ParseLevelData method removed - using ParseLevelDataToChart instead

        /// <summary>
        /// Parses level data in format "DRUMS:85,GUITAR:78,BASS:65" to SongChart
        /// </summary>
        private void ParseLevelDataToChart(string levelData, SongChart chart)
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
        private bool TryParseInt(string value, out int result)
        {
            result = 0;
            if (string.IsNullOrEmpty(value)) return false;

            // Handle decimal values by truncating
            if (value.Contains("."))
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
        /// Safely parses a double value
        /// </summary>
        private bool TryParseDouble(string value, out double result)
        {
            result = 0.0;
            if (string.IsNullOrEmpty(value)) return false;

            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        /// <summary>
        /// Calculates song duration by parsing measure data and tracking BPM/measure length changes
        /// Also counts notes for each instrument
        /// </summary>
        private async Task CalculateDurationAsync(StreamReader reader, SongChart chart, string filePath)
        {
            if (chart.Bpm <= 0)
            {
                return;
            }

            try
            {
                // Track timing information
                double currentBpm = chart.Bpm;
                int lastMeasureWithNotes = 0;
                int lastMeasureInFile = 0; // Track the actual last measure in the file (regardless of notes)
                var bpmChanges = new Dictionary<int, double>(); // measure -> BPM
                var measureLengths = new Dictionary<int, double>(); // measure -> length multiplier (default 1.0 = 4/4)
                var bpmDefinitions = new Dictionary<int, double>(); // BPM reference -> actual BPM value
                
                // Track note counts for each instrument
                int drumNoteCount = 0;
                int guitarNoteCount = 0;
                int bassNoteCount = 0;
                
                // Note: Instrument type determination removed as it was unused
                
                string line;
                bool inDataSection = false;
                
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    line = line.Trim();
                    
                    // Skip empty lines and comments
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                        continue;
                    
                    // Check if we've reached the data section
                    if (line.StartsWith("*") || line.StartsWith("[") || IsTimelineData(line))
                    {
                        inDataSection = true;
                    }
                    
                    if (!inDataSection)
                    {
                        // Still in header, look for additional BPM and measure length commands
                        if (line.StartsWith("#"))
                        {
                            ParseTimingCommand(line, bpmChanges, measureLengths, bpmDefinitions);
                        }
                        continue;
                    }
                    
                    // Handle inline timing commands (BPM changes and measure length changes) in the data section
                    if (line.StartsWith("#") && line.Contains(":"))
                    {
                        var colonIndex = line.IndexOf(':');
                        var measureChannelPart = line.Substring(1, colonIndex - 1);
                        if (measureChannelPart.Length == 5)
                        {
                            var channelStr = measureChannelPart.Substring(3, 2);
                            if (int.TryParse(channelStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int channel))
                            {
                                // Handle measure length changes (channel 02) and BPM definitions inline
                                if (channel == 0x02)
                                {
                                    // This is a measure length change like #09302: 0.75
                                    var measureStr = measureChannelPart.Substring(0, 3);
                                    if (int.TryParse(measureStr, out int measure))
                                    {
                                        var value = line.Substring(colonIndex + 1).Trim();
                                        if (TryParseDouble(value, out double length))
                                        {
                                            measureLengths[measure] = length;
                                        }
                                    }
                                    continue; // Skip processing as note data
                                }
                                // Handle other inline timing commands here if needed
                            }
                        }
                    }
                    
                    // Parse measure data: *MMCCC: NNNNNNNN or #MMCCC: NNNNNNNN
                    if ((line.StartsWith("*") || IsTimelineData(line)) && line.Contains(":"))
                    {
                        
                        var measure = ParseMeasureLine(line);
                        var hasNotes = HasNoteData(line);
                        
                        if (measure.HasValue)
                        {
                            // Always track the last measure in the file (regardless of notes)
                            lastMeasureInFile = Math.Max(lastMeasureInFile, measure.Value);
                            
                            // Track last measure with notes for reference
                            if (hasNotes)
                            {
                                lastMeasureWithNotes = Math.Max(lastMeasureWithNotes, measure.Value);
                            }
                            
                            // Temporary debug output - removed for cleaner output
                            
                            // Count notes and handle BPM changes based on channel
                            var channel = ParseChannelFromLine(line);
                            
                            if (channel.HasValue)
                            {
                                // Check for BPM change channel (channel 08)
                                if (channel.Value == 0x08 && hasNotes)
                                {
                                    // Parse BPM change data from the measure
                                    var bpmRef = ParseBPMChangeFromMeasure(line);
                                    if (bpmRef.HasValue && bpmDefinitions.ContainsKey((int)bpmRef.Value))
                                    {
                                        var actualBpm = bpmDefinitions[(int)bpmRef.Value];
                                        bpmChanges[measure.Value] = actualBpm;
                                    }
                                }
                                
                                // Check for measure length changes (channel 02)
                                if (channel.Value == 0x02 && hasNotes)
                                {
                                    // Parse measure length change data
                                    var lengthData = ParseMeasureLengthFromMeasure(line);
                                    if (lengthData.HasValue)
                                    {
                                        measureLengths[measure.Value] = lengthData.Value;
                                    }
                                }
                                
                                // Count notes for instruments
                                if (hasNotes)
                                {
                                    int noteCount = CountNotesInLine(line);
                                    
                                    if (noteCount > 0)
                                    {
                                        // Use correct DTX channel classification
                                        if (IsDrumChannel(channel.Value))
                                        {
                                            drumNoteCount += noteCount;
                                        }
                                        else if (IsGuitarChannel(channel.Value))
                                        {
                                            guitarNoteCount += noteCount;
                                        }
                                        else if (IsBassChannel(channel.Value))
                                        {
                                            bassNoteCount += noteCount;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                
                // Set note counts in the chart
                chart.DrumNoteCount = drumNoteCount;
                chart.GuitarNoteCount = guitarNoteCount;
                chart.BassNoteCount = bassNoteCount;
                
                
                // Calculate total duration using the actual last measure in the file
                if (lastMeasureInFile > 0)
                {
                    double totalDuration = CalculateTotalDuration(lastMeasureInFile, currentBpm, bpmChanges, measureLengths);
                    chart.Duration = totalDuration;
                    
                    Debug.WriteLine($"DTXMetadataParser: Final results - Duration: {totalDuration:F2}s, LastMeasure: {lastMeasureInFile}, LastWithNotes: {lastMeasureWithNotes}, Notes: D{drumNoteCount} G{guitarNoteCount} B{bassNoteCount}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DTXMetadataParser: Failed to calculate duration and notes: {ex.Message}");
                // Duration remains 0 on error
            }
        }
        
        /// <summary>
        /// Parses timing-related commands from the header section
        /// </summary>
        private void ParseTimingCommand(string line, Dictionary<int, double> bpmChanges, Dictionary<int, double> measureLengths, Dictionary<int, double> bpmDefinitions)
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex == -1) return;

            var command = line.Substring(0, colonIndex).Trim().ToUpperInvariant();
            var value = line.Substring(colonIndex + 1).Trim();

            // Remove quotes if present
            if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length > 1)
            {
                value = value.Substring(1, value.Length - 2);
            }

            // Handle BPM definitions: #BPM01: 150.0
            if (command.StartsWith("#BPM") && command.Length > 4)
            {
                var refStr = command.Substring(4);
                if (int.TryParse(refStr, out int bpmRef) && TryParseDouble(value, out double bpm))
                {
                    // Store BPM definition for later reference
                    bpmDefinitions[bpmRef] = bpm;
                }
            }
            
            // Handle measure length changes: #LENGTH01: 0.5 (half-length measure)
            else if (command.StartsWith("#LENGTH") && command.Length > 7)
            {
                var measureStr = command.Substring(7);
                if (int.TryParse(measureStr, out int measure) && TryParseDouble(value, out double length))
                {
                    measureLengths[measure] = length;
                }
            }
        }
        
        /// <summary>
        /// Checks if a line contains timeline data (measure data)
        /// </summary>
        private bool IsTimelineData(string line)
        {
            // Check for #MMCCC: format (5-digit hex after #)
            if (line.StartsWith("#") && line.Length >= 6)
            {
                var measurePart = line.Substring(1, 5);
                return measurePart.All(c => char.IsDigit(c) || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'));
            }
            return false;
        }
        
        /// <summary>
        /// Parses a measure line and returns the measure number
        /// </summary>
        private int? ParseMeasureLine(string line)
        {
            try
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex == -1) return null;
                
                // Format: *MMCCC: NNNNNNNN where MM is measure, CCC is channel
                if (line.StartsWith("*"))
                {
                    var measureChannelPart = line.Substring(1, colonIndex - 1);
                    if (measureChannelPart.Length >= 5) // At least MMCCC
                    {
                        var measureStr = measureChannelPart.Substring(0, measureChannelPart.Length - 3); // Remove CCC to get MM
                        if (int.TryParse(measureStr, out int measure))
                        {
                            return measure;
                        }
                    }
                }
                // Format: #MMCCC: NNNNNNNN where MMM is measure (3 digits), CC is channel (2 hex digits)
                else if (line.StartsWith("#"))
                {
                    var measureChannelPart = line.Substring(1, colonIndex - 1);
                    if (measureChannelPart.Length == 5) // Exactly MMMCC format
                    {
                        var measureStr = measureChannelPart.Substring(0, 3); // First 3 chars = measure
                        if (int.TryParse(measureStr, out int measure))
                        {
                            return measure;
                        }
                    }
                }
            }
            catch
            {
                // Invalid format, ignore
            }
            return null;
        }
        
        /// <summary>
        /// Checks if a measure line contains actual note data (not just empty)
        /// </summary>
        private bool HasNoteData(string line)
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex == -1 || colonIndex + 1 >= line.Length) return false;
            
            var noteData = line.Substring(colonIndex + 1).Trim();
            
            // Check if there's any non-zero note data
            return noteData.Any(c => c != '0' && c != ' ' && c != '\t');
        }
        
        /// <summary>
        /// Calculates total duration considering BPM changes and measure lengths
        /// </summary>
        private double CalculateTotalDuration(int lastMeasure, double baseBpm, Dictionary<int, double> bpmChanges, Dictionary<int, double> measureLengths)
        {
            double totalTime = 0.0;
            double currentBpm = baseBpm;
            
            for (int measure = 1; measure <= lastMeasure; measure++)
            {
                // Check for BPM change at this measure
                if (bpmChanges.ContainsKey(measure))
                {
                    currentBpm = bpmChanges[measure];
                }
                
                // Get measure length (default 1.0 = full 4/4 measure)
                double measureLength = measureLengths.ContainsKey(measure) ? measureLengths[measure] : 1.0;
                
                // Calculate time for this measure: (4 beats * measure_length * 60 seconds) / BPM
                double measureTime = (4.0 * measureLength * 60.0) / currentBpm;
                totalTime += measureTime;
            }
            
            // Add a small buffer for the final note to ring out (0.5 seconds)
            totalTime += 0.5;
            
            return totalTime;
        }
        
        /// <summary>
        /// Parses the channel number from a measure line
        /// </summary>
        private int? ParseChannelFromLine(string line)
        {
            try
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex == -1) return null;
                
                // Format: *MMCCC: NNNNNNNN where MM is measure, CCC is channel (hex)
                if (line.StartsWith("*"))
                {
                    var measureChannelPart = line.Substring(1, colonIndex - 1);
                    if (measureChannelPart.Length >= 5) // At least MMCCC
                    {
                        var channelStr = measureChannelPart.Substring(measureChannelPart.Length - 3); // Last 3 chars are CCC
                        if (int.TryParse(channelStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int channel))
                        {
                            return channel;
                        }
                    }
                }
                // Format: #MMCCC: NNNNNNNN where MMM is measure (3 digits), CC is channel (2 hex digits)
                else if (line.StartsWith("#"))
                {
                    var measureChannelPart = line.Substring(1, colonIndex - 1);
                    if (measureChannelPart.Length == 5) // Exactly MMMCC format
                    {
                        var channelStr = measureChannelPart.Substring(3, 2); // Last 2 chars are CC
                        if (int.TryParse(channelStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int channel))
                        {
                            return channel;
                        }
                    }
                }
            }
            catch
            {
                // Invalid format, ignore
            }
            return null;
        }
        
        /// <summary>
        /// Counts the number of notes in a measure line
        /// DTX format: each pair of characters represents one note position
        /// </summary>
        private int CountNotesInLine(string line)
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex == -1 || colonIndex + 1 >= line.Length) return 0;
            
            var noteData = line.Substring(colonIndex + 1).Trim();
            int noteCount = 0;
            
            // DTX uses pairs of characters to represent notes
            // Process in pairs: 00, 11, 22, etc.
            for (int i = 0; i < noteData.Length; i += 2)
            {
                if (i + 1 < noteData.Length)
                {
                    var pair = noteData.Substring(i, 2);
                    // Count non-zero pairs as notes (00 = no note, anything else = note)
                    if (pair != "00" && !string.IsNullOrWhiteSpace(pair))
                    {
                        noteCount++;
                    }
                }
            }
            
            return noteCount;
        }
        
        /// <summary>
        /// Checks if a channel is a drum channel
        /// DTX drum channels: 11, 12, 13, 14, 15, 16, 17, 18, 19, 1A, 1B, 1C (hex)
        /// </summary>
        private bool IsDrumChannel(int channel)
        {
            // Specific drum channels as provided by user
            return channel == 0x11 || channel == 0x12 || channel == 0x13 || channel == 0x14 ||
                   channel == 0x15 || channel == 0x16 || channel == 0x17 || channel == 0x18 ||
                   channel == 0x19 || channel == 0x1A || channel == 0x1B || channel == 0x1C;
        }
        
        /// <summary>
        /// Checks if a channel is a guitar channel
        /// Currently disabled since user reports all charts are drum-only
        /// </summary>
        private bool IsGuitarChannel(int channel)
        {
            return false; // All charts are drum-only, ignore guitar channels
        }
        
        /// <summary>
        /// Checks if a channel is a bass channel
        /// Currently disabled since user reports all charts are drum-only
        /// </summary>
        private bool IsBassChannel(int channel)
        {
            return false; // All charts are drum-only, ignore bass channels
        }
        
        /// <summary>
        /// Parses BPM change data from a measure line (channel 08)
        /// </summary>
        private double? ParseBPMChangeFromMeasure(string line)
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex == -1 || colonIndex + 1 >= line.Length) return null;
            
            var noteData = line.Substring(colonIndex + 1).Trim();
            
            // DTX BPM changes use pairs of characters that reference BPM01, BPM02, etc.
            // Find the first non-zero pair
            for (int i = 0; i < noteData.Length; i += 2)
            {
                if (i + 1 < noteData.Length)
                {
                    var pair = noteData.Substring(i, 2);
                    if (pair != "00" && !string.IsNullOrWhiteSpace(pair))
                    {
                        // Try to parse as hex to get BPM reference number
                        if (int.TryParse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int bpmRef))
                        {
                            // This references #BPM01, #BPM02, etc. - we need to look up the actual BPM value
                            // For now, return the reference number so we can match it with header BPM definitions
                            return bpmRef;
                        }
                    }
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Parses measure length change data from a measure line (channel 02)
        /// </summary>
        private double? ParseMeasureLengthFromMeasure(string line)
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex == -1 || colonIndex + 1 >= line.Length) return null;
            
            var noteData = line.Substring(colonIndex + 1).Trim();
            
            // DTX measure length changes use pairs that reference length definitions
            // Find the first non-zero pair
            for (int i = 0; i < noteData.Length; i += 2)
            {
                if (i + 1 < noteData.Length)
                {
                    var pair = noteData.Substring(i, 2);
                    if (pair != "00" && !string.IsNullOrWhiteSpace(pair))
                    {
                        // Try to parse as hex to get length reference number
                        if (int.TryParse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int lengthRef))
                        {
                            // This references length definitions - return the reference number
                            return lengthRef;
                        }
                    }
                }
            }
            
            return null;
        }

        #endregion

        #region Static Helper Methods

        /// <summary>
        /// Quick check if a file is a supported DTX format
        /// </summary>
        public static bool IsSupportedFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var supportedExtensions = new[] { ".dtx", ".gda", ".g2d", ".bms", ".bme", ".bml" };
            
            return Array.Exists(supportedExtensions, ext => ext == extension);
        }

        // Legacy GetBasicFileInfo method removed - use EF Core entities instead

        #endregion
    }
}
