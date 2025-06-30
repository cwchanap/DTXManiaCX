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
                    Debug.WriteLine("DTXMetadataParser: Registered CodePages encoding provider for Shift_JIS support");
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
                Debug.WriteLine($"DTXMetadataParser: Unsupported file format: {chart.FileFormat}");
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
                Debug.WriteLine($"DTXMetadataParser: Error parsing {filePath}: {ex.Message}");
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
                    Debug.WriteLine($"DTXMetadataParser: Starting duration calculation for {filePath}");
                    stream.Seek(0, SeekOrigin.Begin); // Reset to beginning
                    using var durationReader = new StreamReader(stream, encoding);
                    await CalculateDurationAsync(durationReader, tempChart);
                    Debug.WriteLine($"DTXMetadataParser: Duration calculation completed. Result: {tempChart.Duration:F2} seconds");
                    
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
                            Debug.WriteLine($"DTXMetadataParser: Successfully parsed with encoding {encoding.EncodingName}");
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
                                Debug.WriteLine($"DTXMetadataParser: Using {encoding.EncodingName} as fallback (text may be corrupted)");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"DTXMetadataParser: Failed with encoding {encoding.EncodingName}: {ex.Message}");
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
                chart.PreviewFile = bestChart.PreviewFile;
                chart.PreviewImage = bestChart.PreviewImage;
                chart.BackgroundFile = bestChart.BackgroundFile;
                chart.StageFile = bestChart.StageFile;
                chart.FileFormat = bestChart.FileFormat;
                
                Debug.WriteLine($"DTXMetadataParser: Final result using {bestEncodingName} - Title: {song.Title}, Artist: {song.Artist}");
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
            var lineCount = 0;
            const int maxHeaderLines = 200; // Limit header parsing to first 200 lines

            while ((line = await reader.ReadLineAsync()) != null && lineCount < maxHeaderLines)
            {
                lineCount++;
                
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
        /// </summary>
        private async Task CalculateDurationAsync(StreamReader reader, SongChart chart)
        {
            if (chart.Bpm <= 0)
            {
                Debug.WriteLine("DTXMetadataParser: Cannot calculate duration - no BPM specified");
                return;
            }

            try
            {
                // Track timing information
                double currentBpm = chart.Bpm;
                int lastMeasureWithNotes = 0;
                var bpmChanges = new Dictionary<int, double>(); // measure -> BPM
                var measureLengths = new Dictionary<int, double>(); // measure -> length multiplier (default 1.0 = 4/4)
                
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
                            ParseTimingCommand(line, bpmChanges, measureLengths);
                        }
                        continue;
                    }
                    
                    // Parse measure data: *MMCCC: NNNNNNNN or #MMCCC: NNNNNNNN
                    if ((line.StartsWith("*") || IsTimelineData(line)) && line.Contains(":"))
                    {
                        var measure = ParseMeasureLine(line);
                        if (measure.HasValue && HasNoteData(line))
                        {
                            lastMeasureWithNotes = Math.Max(lastMeasureWithNotes, measure.Value);
                        }
                    }
                }
                
                // Calculate total duration
                if (lastMeasureWithNotes > 0)
                {
                    double totalDuration = CalculateTotalDuration(lastMeasureWithNotes, currentBpm, bpmChanges, measureLengths);
                    chart.Duration = totalDuration;
                    
                    Debug.WriteLine($"DTXMetadataParser: Calculated duration {totalDuration:F2} seconds for {lastMeasureWithNotes} measures");
                }
                else
                {
                    Debug.WriteLine("DTXMetadataParser: No measures with notes found");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DTXMetadataParser: Error calculating duration: {ex.Message}");
                // Duration remains 0 on error
            }
        }
        
        /// <summary>
        /// Parses timing-related commands from the header section
        /// </summary>
        private void ParseTimingCommand(string line, Dictionary<int, double> bpmChanges, Dictionary<int, double> measureLengths)
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

            // Handle BPM changes at specific measures: #BPM01: 150.0
            if (command.StartsWith("#BPM") && command.Length > 4)
            {
                var measureStr = command.Substring(4);
                if (int.TryParse(measureStr, out int measure) && TryParseDouble(value, out double bpm))
                {
                    bpmChanges[measure] = bpm;
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
                // Format: *MMCCC: NNNNNNNN where MM is measure, CCC is channel
                if (line.Length >= 6 && line.StartsWith("*"))
                {
                    var measureStr = line.Substring(1, 2);
                    if (int.TryParse(measureStr, out int measure))
                    {
                        return measure;
                    }
                }
                // Format: #MMCCC: NNNNNNNN where MM is measure (first 3 digits), CCC is channel (last 2 digits)
                else if (line.Length >= 6 && line.StartsWith("#"))
                {
                    var measurePart = line.Substring(1, 3); // First 3 digits are measure
                    if (int.TryParse(measurePart, out int measure))
                    {
                        return measure;
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
