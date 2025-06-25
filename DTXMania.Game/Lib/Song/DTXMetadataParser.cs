using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
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
                Bpm = 120.0, // Default BPM
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

            foreach (var encoding in encodings)
            {
                try
                {
                    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var reader = new StreamReader(stream, encoding);
                    
                    await ParseHeaderLinesToEntitiesAsync(reader, song, chart);
                    
                    // If we successfully parsed some metadata, we're done
                    if (!string.IsNullOrEmpty(song.Title) || !string.IsNullOrEmpty(song.Artist))
                        break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"DTXMetadataParser: Failed with encoding {encoding.EncodingName}: {ex.Message}");
                    continue;
                }
            }

            // Set fallback title if none was found
            if (string.IsNullOrEmpty(song.Title))
            {
                song.Title = Path.GetFileNameWithoutExtension(filePath);
            }
        }

        // Legacy ParseHeaderLines method removed - using ParseHeaderLinesToEntitiesAsync instead

        /// <summary>
        /// Parses header lines from the DTX file to Song and SongChart entities
        /// </summary>
        private async Task ParseHeaderLinesToEntitiesAsync(StreamReader reader, DTXMania.Game.Lib.Song.Entities.Song song, SongChart chart)
        {
            string? line;
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
