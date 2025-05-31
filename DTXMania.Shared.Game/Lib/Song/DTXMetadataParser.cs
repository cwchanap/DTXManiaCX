using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;

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

        /// <summary>
        /// Parses metadata from a DTX file
        /// </summary>
        /// <param name="filePath">Path to the DTX file</param>
        /// <returns>Parsed song metadata</returns>
        public async Task<SongMetadata> ParseMetadataAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException($"DTX file not found: {filePath}");

            var fileInfo = new FileInfo(filePath);
            var metadata = new SongMetadata
            {
                FilePath = filePath,
                FileSize = fileInfo.Length,
                LastModified = fileInfo.LastWriteTime,
                FileFormat = fileInfo.Extension.ToLowerInvariant()
            };

            // Validate file extension
            if (!IsSupported(metadata.FileFormat))
            {
                Debug.WriteLine($"DTXMetadataParser: Unsupported file format: {metadata.FileFormat}");
                // Set fallback title for unsupported files
                metadata.Title = Path.GetFileNameWithoutExtension(filePath);
                return metadata;
            }

            try
            {
                await ParseFileHeaderAsync(filePath, metadata);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DTXMetadataParser: Error parsing {filePath}: {ex.Message}");
                // Return metadata with basic file info even if parsing fails
            }

            return metadata;
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

        /// <summary>
        /// Parses the header section of a DTX file
        /// </summary>
        private async Task ParseFileHeaderAsync(string filePath, SongMetadata metadata)
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
                Debug.WriteLine("DTXMetadataParser: Shift_JIS encoding not available, using fallback encodings");
            }

            foreach (var encoding in encodings)
            {
                try
                {
                    using var reader = new StreamReader(filePath, encoding);
                    await ParseHeaderLines(reader, metadata);
                    
                    // If we successfully parsed some metadata, we're done
                    if (!string.IsNullOrEmpty(metadata.Title) || !string.IsNullOrEmpty(metadata.Artist))
                        break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"DTXMetadataParser: Failed with encoding {encoding.EncodingName}: {ex.Message}");
                    continue;
                }
            }

            // Set fallback title if none was found
            if (string.IsNullOrEmpty(metadata.Title))
            {
                metadata.Title = Path.GetFileNameWithoutExtension(filePath);
            }
        }

        /// <summary>
        /// Parses header lines from the DTX file
        /// </summary>
        private async Task ParseHeaderLines(StreamReader reader, SongMetadata metadata)
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
                    ParseHeaderCommand(line, metadata);
                }
            }
        }

        /// <summary>
        /// Parses a single header command line
        /// </summary>
        private void ParseHeaderCommand(string line, SongMetadata metadata)
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
                case "#TITLE":
                    metadata.Title = value;
                    break;

                case "#ARTIST":
                    metadata.Artist = value;
                    break;

                case "#GENRE":
                    metadata.Genre = value;
                    break;

                case "#COMMENT":
                    metadata.Comment = value;
                    break;

                case "#BPM":
                    if (TryParseDouble(value, out var bpm))
                        metadata.BPM = bpm;
                    break;

                case "#LEVEL":
                    ParseLevelData(value, metadata);
                    break;

                case "#DLEVEL":
                    if (TryParseInt(value, out var drumLevel))
                        metadata.DrumLevel = drumLevel;
                    break;

                case "#GLEVEL":
                    if (TryParseInt(value, out var guitarLevel))
                        metadata.GuitarLevel = guitarLevel;
                    break;

                case "#BLEVEL":
                    if (TryParseInt(value, out var bassLevel))
                        metadata.BassLevel = bassLevel;
                    break;

                case "#PREVIEW":
                    metadata.PreviewFile = value;
                    break;

                case "#PREIMAGE":
                    metadata.PreviewImage = value;
                    break;

                case "#BACKGROUND":
                case "#WALL":
                    metadata.BackgroundImage = value;
                    break;

                case "#STAGEFILE":
                    metadata.StageFile = value;
                    break;

                // Difficulty labels
                case "#DLABEL":
                    metadata.DifficultyLabels["DRUMS"] = value;
                    break;

                case "#GLABEL":
                    metadata.DifficultyLabels["GUITAR"] = value;
                    break;

                case "#BLABEL":
                    metadata.DifficultyLabels["BASS"] = value;
                    break;
            }
        }

        /// <summary>
        /// Parses level data in format "DRUMS:85,GUITAR:78,BASS:65"
        /// </summary>
        private void ParseLevelData(string levelData, SongMetadata metadata)
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
                                metadata.DrumLevel = level;
                                break;
                            case "GUITAR":
                                metadata.GuitarLevel = level;
                                break;
                            case "BASS":
                                metadata.BassLevel = level;
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

        /// <summary>
        /// Gets basic file info without parsing content
        /// </summary>
        public static SongMetadata GetBasicFileInfo(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            return new SongMetadata
            {
                FilePath = filePath,
                Title = Path.GetFileNameWithoutExtension(filePath),
                FileSize = fileInfo.Length,
                LastModified = fileInfo.LastWriteTime,
                FileFormat = fileInfo.Extension.ToLowerInvariant()
            };
        }

        #endregion
    }
}
