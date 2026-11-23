#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// Owns SET.def and box.def line reading, encoding fallback, normalization,
    /// parsing and metadata (HPA-801 Decision C). Holds no publication state and
    /// no DbContext: <see cref="SongLibrary"/> constructs exactly one reader and
    /// shares it with every cold-scan and warm-rebuild consumer, so one
    /// <see cref="ReadAllLines"/> seam and one set of fallback rules govern both
    /// paths.
    /// </summary>
    public sealed class SongDefinitionReader
    {
        public SongDefinitionReader(
            ILogger? logger = null,
            Func<string, Encoding, CancellationToken, Task<string[]>>? readAllLines = null)
        {
            _logger = logger ?? NullLogger.Instance;
            ReadAllLines = readAllLines
                ?? ((path, encoding, token) =>
                    File.ReadAllLinesAsync(path, encoding, token));
        }

        private readonly ILogger _logger;

        /// <summary>
        /// The one definition file-read seam (HPA-801 Decision E, migrated from
        /// SongLibrary.ReadAllLinesCoreAsync). The production default reads with
        /// <see cref="File.ReadAllLinesAsync"/>; tests inject read failures and
        /// cancellations here. Both the enumeration (scan) read and the
        /// synchronous database-load fallback chain go through this delegate.
        /// </summary>
        internal Func<string, Encoding, CancellationToken, Task<string[]>> ReadAllLines { get; }

        // Unreadable SET.def paths already reported; keeps the warning to one per file.
        private readonly object _setDefReportGate = new();
        private readonly HashSet<string> _reportedUnreadableSetDefs = new(StringComparer.Ordinal);

        // Compiled regex patterns for SET.def normalization (performance optimization)
        private static readonly Regex NullBytePattern = new Regex(@"\u0000", RegexOptions.Compiled);
        private static readonly Regex BomPattern = new Regex(@"[\uFEFF\u200B]+", RegexOptions.Compiled);
        private static readonly Regex HashSpacePattern = new Regex(@"#\s+([A-Z]+)\s+(.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SpacedCommandPattern = new Regex(@"#\s*([A-Z\s]+?)\s+(.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ExcessiveSpacesPattern = new Regex(@"\s+", RegexOptions.Compiled);

        /// <summary>
        /// Normalizes a SET.def line to handle corrupted/spaced formatting and UTF-16 encoding issues
        /// Optimized version using compiled regex patterns and StringBuilder for better performance
        /// </summary>
        internal string NormalizeSetDefLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return "";
            
            try
            {
                // Step 1: Remove BOMs and null bytes using compiled regex (faster than multiple Replace calls)
                string processedLine = BomPattern.Replace(line, "");
                processedLine = NullBytePattern.Replace(processedLine, "");
                processedLine = processedLine.Trim();
                
                // Quick check: if it's already a proper command line, return it.
                // A space right after '#' means the command itself is spaced out
                // ("# L 5 F I L E x.dtx"), which is NOT intact and must go
                // through reconstruction below.
                if (processedLine.StartsWith("#") && !processedLine.Contains("  ") &&
                    processedLine.Length > 1 && !char.IsWhiteSpace(processedLine[1]))
                {
                    return processedLine;
                }

                // Step 2: Handle spaced-out commands using compiled regex
                if (processedLine.Contains("#"))
                {
                    // Spaced command: '#' separated from the command letters
                    // ("# TITLE x", "# L 5 F I L E x.dtx") or a one-letter token
                    // with a spaced continuation ("#T I T L E x"). The
                    // single-token regexes below can only capture one fragment
                    // ("L"), so rebuild the command token-by-token.
                    var firstSpace = processedLine.IndexOf(' ');
                    if (processedLine.StartsWith("#") &&
                        processedLine.Length > 1 &&
                        (char.IsWhiteSpace(processedLine[1]) || firstSpace == 2))
                    {
                        return ReconstructCorruptedLine(processedLine);
                    }

                    // Try simple spaced pattern first: "# TITLE My Song" -> "#TITLE My Song"
                    var simpleMatch = HashSpacePattern.Match(processedLine);
                    if (simpleMatch.Success)
                    {
                        var command = simpleMatch.Groups[1].Value.Replace(" ", "");
                        var value = simpleMatch.Groups[2].Value;
                        return $"#{command} {value}";
                    }
                    
                    // Handle complex spaced patterns: "# T I T L E My Song" 
                    var complexMatch = SpacedCommandPattern.Match(processedLine);
                    if (complexMatch.Success)
                    {
                        var spacedCommand = complexMatch.Groups[1].Value;
                        var value = complexMatch.Groups[2].Value;
                        
                        // Remove spaces from command part using compiled regex
                        var command = ExcessiveSpacesPattern.Replace(spacedCommand, "");
                        
                        // Handle special patterns efficiently
                        var upperCommand = command.ToUpperInvariant();
                        if (IsKnownCommand(upperCommand))
                        {
                            return $"#{upperCommand} {value}";
                        }
                        
                        // Handle L#LABEL and L#FILE patterns
                        if (upperCommand.Length > 1 && char.IsDigit(upperCommand[1]) &&
                            (upperCommand.EndsWith("LABEL") || upperCommand.EndsWith("FILE")))
                        {
                            return $"#{upperCommand} {value}";
                        }
                        
                        return $"#{command} {value}";
                    }
                    
                    // Fallback: try to reconstruct manually for very corrupted lines
                    return ReconstructCorruptedLine(processedLine);
                }
                
                return processedLine;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error normalizing SET.def line '{Line}'", line);
                return "";
            }
        }
        
        /// <summary>
        /// Fast lookup for known SET.def commands to avoid string processing
        /// </summary>
        private static bool IsKnownCommand(string command)
        {
            // Use switch expression for optimal performance (faster than HashSet for small sets)
            return command switch
            {
                "TITLE" or "L1LABEL" or "L2LABEL" or "L3LABEL" or "L4LABEL" or "L5LABEL" or
                "L1FILE" or "L2FILE" or "L3FILE" or "L4FILE" or "L5FILE" => true,
                _ => false
            };
        }
        
        /// <summary>
        /// Fallback method for heavily corrupted lines using StringBuilder
        /// </summary>
        internal string ReconstructCorruptedLine(string line)
        {
            var hashIndex = line.IndexOf('#');
            if (hashIndex < 0) return line;
            
            var afterHash = line.Substring(hashIndex + 1).Trim();
            if (string.IsNullOrEmpty(afterHash)) return line;
            
            var parts = afterHash.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length < 2) return line;
            
            // Build command efficiently
            var commandBuilder = new StringBuilder(parts.Length * 2);
            var valueBuilder = new StringBuilder(afterHash.Length);
            var commandComplete = false;
            
            foreach (var part in parts)
            {
                if (!commandComplete)
                {
                    commandBuilder.Append(part);
                    var accumulated = commandBuilder.ToString().ToUpperInvariant();
                    
                    // Check if we've completed a known command
                    if (IsKnownCommand(accumulated) || 
                        (accumulated.Length > 2 && accumulated.EndsWith("LABEL")) ||
                        (accumulated.Length > 2 && accumulated.EndsWith("FILE")))
                    {
                        commandComplete = true;
                        continue;
                    }
                }
                else
                {
                    if (valueBuilder.Length > 0) valueBuilder.Append(' ');
                    valueBuilder.Append(part);
                }
            }
            
            if (commandComplete && valueBuilder.Length > 0)
            {
                return $"#{commandBuilder} {valueBuilder}";
            }
            
            // Final fallback
            return $"#{parts[0]} {string.Join(" ", parts.Skip(1))}";
        }

        /// <summary>
        /// Builds the list of encodings used to read SET.def files. Shared by
        /// <see cref="ReadSetDefLines"/> (synchronous database-load path) and the
        /// async enumeration read (via <see cref="ReadAllLines"/>) so the two never
        /// drift.
        /// </summary>
        /// <remarks>
        /// NOTE: this is <b>not</b> a true encoding fallback chain as written today.
        /// <see cref="Encoding.UTF8"/> uses <see cref="System.Text.DecoderReplacementFallback"/>, so
        /// <c>File.ReadAllLines(UTF-8)</c> never throws on bad bytes — it emits U+FFFD and
        /// "succeeds" on the first entry, and the subsequent system-default / Shift_JIS entries
        /// are never exercised for decoding reasons. (The <c>catch</c> below only catches I/O
        /// errors, which fail identically across the whole list.) Shift_JIS is retained so that a
        /// future switch to <c>new UTF8Encoding(false, throwOnInvalidBytes: true)</c> could enable a
        /// real fallback for legacy Japanese charts; it is added defensively because it needs a
        /// registered code-page provider on some runtimes. The actual repair of the
        /// BOM/null-byte/spaced artifacts produced by reading a Shift_JIS or UTF-16 SET.def as
        /// UTF-8 happens after the read, in <see cref="NormalizeSetDefLine"/>.
        /// </remarks>
        private static List<Encoding> BuildSetDefEncodings()
        {
            var encodings = new List<Encoding> { Encoding.UTF8, Encoding.Default };
            try
            {
                encodings.Add(Encoding.GetEncoding("Shift_JIS"));
            }
            catch (ArgumentException)
            {
            }
            return encodings;
        }

        /// <summary>
        /// Reads a SET.def file using <see cref="BuildSetDefEncodings"/>. Returns null only when the
        /// file cannot be read at all (it does not exist or an I/O error occurs). Because UTF-8 uses
        /// a replacement fallback, a read with invalid bytes never throws and the later encodings in
        /// the list are not retried — see the remarks on <see cref="BuildSetDefEncodings"/>.
        /// <see cref="NormalizeSetDefLine"/> repairs the BOM/null-byte/spaced artifacts that result
        /// from reading a Shift_JIS or UTF-16 SET.def as UTF-8.
        /// </summary>
        /// <remarks>
        /// Synchronous by design: the only caller is <see cref="GetSetDefLabelsByFile"/> and the
        /// freshness inventory, which run on the synchronous database-load path. The async
        /// enumeration path reads through <see cref="ReadAllLines"/> directly. Every encoding
        /// attempt goes through the same <see cref="ReadAllLines"/> seam (HPA-801 Decision E).
        /// </remarks>
        internal string[]? ReadSetDefLines(string setDefPath)
        {
            Exception? lastError = null;
            foreach (var encoding in BuildSetDefEncodings())
            {
                try
                {
                    var lines = ReadAllLines(setDefPath, encoding, CancellationToken.None)
                        .GetAwaiter().GetResult();
                    // A recovered file re-arms its warning for any later regression.
                    lock (_setDefReportGate)
                    {
                        _reportedUnreadableSetDefs.Remove(setDefPath);
                    }
                    return lines;
                }
                // Only realistic failures here are I/O (the path is pre-validated and UTF-8 uses
                // a replacement fallback, so encoding mismatches never throw). Let genuinely
                // unexpected exceptions (OOM, contract changes) propagate instead of being masked.
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    lastError = ex;
                }
            }
            // One record per unreadable SET.def file (not per call: the DB hierarchy build
            // and the freshness inventory both read it), after every encoding was retried.
            if (lastError is not null)
            {
                bool firstReport;
                lock (_setDefReportGate)
                {
                    firstReport = _reportedUnreadableSetDefs.Add(setDefPath);
                }

                if (firstReport)
                {
                    _logger.LogWarning(lastError, "Failed to read SET.def: {Path}", setDefPath);
                }
            }
            return null;
        }

        /// <summary>
        /// Parses the #TITLE and #Ln(LABEL|FILE) commands out of SET.def lines into the
        /// title plus a per-difficulty (label, file) map keyed by the L-slot number. Shared by
        /// enumeration and the database-load path so the difficulty badge can recover the
        /// authentic difficulty label from the SET.def.
        /// </summary>
        internal (string title, Dictionary<int, (string label, string file)> difficulties) ParseSetDefContent(
            string[] lines, CancellationToken cancellationToken)
        {
            string songTitle = "";
            var difficulties = new Dictionary<int, (string label, string file)>();

            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("//"))
                    continue;

                // Parse SET.def commands with robust parsing for corrupted/spaced text
                if (trimmedLine.StartsWith("#") || trimmedLine.Contains("#"))
                {
                    // Handle both normal and spaced-out command formats
                    string normalizedLine = NormalizeSetDefLine(trimmedLine);

                    if (string.IsNullOrEmpty(normalizedLine)) continue;

                    var parts = normalizedLine.Split(new char[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        var command = parts[0].Substring(1).ToUpperInvariant(); // Remove # and convert to uppercase
                        var value = parts[1].Trim();

                        if (command == "TITLE")
                        {
                            songTitle = value;
                        }
                        else if (command.StartsWith("L") &&
                                 (command.EndsWith("LABEL") || command.EndsWith("FILE")))
                        {
                            // Extract level number (L1LABEL -> 1, L1FILE -> 1).
                            // Guard the slice length: a malformed command such as "#LABEL"
                            // (length 5, LABEL suffix 6) would otherwise pass a negative length to
                            // Substring and crash enumeration with ArgumentOutOfRangeException.
                            int suffixLength = command.EndsWith("LABEL") ? 6 : 5;
                            int levelTokenLength = command.Length - suffixLength;
                            if (levelTokenLength > 0 &&
                                int.TryParse(command.Substring(1, levelTokenLength), out int level))
                            {
                                if (!difficulties.ContainsKey(level))
                                    difficulties[level] = ("", "");

                                if (command.EndsWith("LABEL"))
                                    difficulties[level] = (value, difficulties[level].file);
                                else
                                    difficulties[level] = (difficulties[level].label, value);
                            }
                        }
                    }
                }
            }

            return (songTitle, difficulties);
        }

        /// <summary>
        /// Reads the SET.def in <paramref name="directory"/> (if any) and returns a map of
        /// resolved chart path to its authentic #LnLABEL (e.g. "BASIC"). The database-load
        /// path uses this to recover difficulty-tier labels for the performance-stage
        /// difficulty badge when the persisted SongChart.DifficultyLabel is empty (legacy
        /// databases never stored it). Returns an empty map when there is no SET.def or it
        /// declares no labelled difficulties.
        /// </summary>
        internal Dictionary<string, string> GetSetDefLabelsByFile(string directory)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(directory))
                return result;

            // The chart referenced by #LnFILE may live in a subdirectory (e.g. "#L1FILE
            // charts/bas.dtx"), so the caller passes the chart's directory while set.def sits in
            // the song-folder root one level up. Walk up from the chart's directory until set.def
            // is found so subdirectory chart references resolve to the correct labels instead of
            // returning an empty map (which would make the badge fall back to "Level N"/DTX).
            var setDefPath = FindSetDefPath(directory);
            if (setDefPath == null)
                return result;

            var setDefDirectory = Path.GetDirectoryName(setDefPath);
            var lines = ReadSetDefLines(setDefPath);
            if (lines == null)
                return result;

            var (_, difficulties) = ParseSetDefContent(lines, CancellationToken.None);
            foreach (var (label, file) in difficulties.Values)
            {
                if (!string.IsNullOrEmpty(file) && !string.IsNullOrEmpty(label))
                {
                    // Key by the chart's resolved path, not its file name: a
                    // SET.def can list same-named charts in different
                    // subdirectories ("easy/song.dtx" vs "hard/song.dtx"), and a
                    // file-name key would let the last one overwrite the rest.
                    // ResolveDifficultyLabel looks up the chart's full path, so
                    // bare file names resolve relative to the set.def folder.
                    var resolved = Path.GetFullPath(
                        Path.Combine(setDefDirectory ?? "", file));
                    result[SongPathIdentity.Normalize(resolved)] = label;
                }
            }
            return result;
        }

        /// <summary>
        /// Searches <paramref name="directory"/> and its parent directories (bounded) for a
        /// <c>set.def</c> file, returning the first match. This lets the database-load path
        /// recover difficulty labels for charts that live in a subdirectory of the song folder.
        /// Returns null when no set.def is found within the search depth.
        /// </summary>
        private static string? FindSetDefPath(string directory)
        {
            var current = directory;
            for (int depth = 0; depth < 4 && !string.IsNullOrEmpty(current); depth++)
            {
                var candidate = Path.Combine(current, "set.def");
                if (File.Exists(candidate))
                    return candidate;

                var parent = Path.GetDirectoryName(current);
                if (parent == null || parent == current)
                    break;
                current = parent;
            }
            return null;
        }

        internal async Task<BoxDefinition?> ParseBoxDefinitionAsync(string boxDefPath, CancellationToken cancellationToken)
        {
            try
            {
                var lines = await ReadAllLines(boxDefPath, Encoding.UTF8, cancellationToken);
                var boxDef = new BoxDefinition();

                foreach (var trimmedLine in lines.Select(line => line.Trim()))
                {
                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("//"))
                        continue;

                    var parts = trimmedLine.Split(':', 2);
                    if (parts.Length == 2)
                    {
                        var command = parts[0].Trim().ToUpperInvariant();
                        var value = parts[1].Trim();

                        switch (command)
                        {
                            case "#TITLE":
                                boxDef.Title = value;
                                break;
                            case "#GENRE":
                                boxDef.Genre = value;
                                break;
                            case "#SKINPATH":
                                boxDef.SkinPath = value;
                                break;
                            case "#BGCOLOR":
                                if (TryParseColor(value, out var bgColor))
                                    boxDef.BackgroundColor = bgColor;
                                break;
                            case "#TEXTCOLOR":
                                if (TryParseColor(value, out var textColor))
                                    boxDef.TextColor = textColor;
                                break;
                        }
                    }
                }

                return boxDef;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing box.def {BoxDefPath}", boxDefPath);
                return null;
            }
        }

        internal bool TryParseColor(string? colorValue, out System.Drawing.Color color)
        {
            color = System.Drawing.Color.White;

            try
            {
                if (colorValue != null && colorValue.StartsWith("#"))
                {
                    // Hex color format
                    var hex = colorValue.Substring(1);
                    if (hex.Length == 6)
                    {
                        var r = Convert.ToByte(hex.Substring(0, 2), 16);
                        var g = Convert.ToByte(hex.Substring(2, 2), 16);
                        var b = Convert.ToByte(hex.Substring(4, 2), 16);
                        color = System.Drawing.Color.FromArgb(r, g, b);
                        return true;
                    }
                }
                else if (colorValue != null)
                {
                    // Named color
                    color = System.Drawing.Color.FromName(colorValue);
                    return color.IsKnownColor;
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return false;
        }
    }

    /// <summary>
    /// Box definition metadata from box.def files
    /// </summary>
    public class BoxDefinition
    {
        public string Title { get; set; } = "";
        public string Genre { get; set; } = "";
        public string SkinPath { get; set; } = "";
        public System.Drawing.Color BackgroundColor { get; set; } = System.Drawing.Color.Black;
        public System.Drawing.Color TextColor { get; set; } = System.Drawing.Color.White;
    }
}
