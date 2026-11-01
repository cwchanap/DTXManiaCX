using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AlphaTab;
using AlphaTab.Importer;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace DTXMania.Game.Lib.Song.Import
{
    /// <summary>
    /// Converts the drum part of a Guitar Pro (.gp/.gpx/.gp5/.gp4/.gp3) or
    /// MusicXML (.musicxml/.mxl) tab into DTX chart text (HPA-693), which
    /// <see cref="DTXChartParser"/> then parses like any other chart.
    ///
    /// AlphaTab reads the tab, its MIDI generator lays the score out in time
    /// (repeats and alternate endings expanded, tempo and time signature
    /// changes applied), and DryWetMidi reads that MIDI back so bars and notes
    /// can be written onto a 192-per-whole-note DTX grid. General MIDI drum
    /// notes map to DTX drum channels; notes with no drum lane (cowbell,
    /// tambourine, latin percussion, ...) are dropped. The result has no WAV
    /// definitions, so hits are silent unless the built-in metronome is on.
    /// </summary>
    public static class TabChartConverter
    {
        private static readonly string[] _extensions =
            { ".gp", ".gpx", ".gp5", ".gp4", ".gp3", ".musicxml", ".mxl" };

        /// <summary>
        /// Grid slots per whole note; matches the 192 ticks per measure that
        /// the notation tab mode quantizes against.
        /// </summary>
        private const int SlotsPerWholeNote = 192;

        /// <summary>Drum level given to every imported chart (tabs carry none).</summary>
        internal const int ImportedDrumLevel = 50;

        private const int BpmChannel = 0x08;

        /// <summary>DTXChartParser reads three decimal bar digits, so bars 000-999.</summary>
        private const int MaxChartBars = 1000;

        /// <summary>Two base-36 digits give #BPM01-#BPMZZ.</summary>
        private const int MaxBpmIds = 36 * 36 - 1;

        /// <summary>General MIDI drum note to DTX drum channel.</summary>
        private static readonly Dictionary<int, int> GmNoteToChannel = new()
        {
            [35] = 0x13, [36] = 0x13,                           // acoustic / electric bass drum
            [37] = 0x12, [38] = 0x12, [40] = 0x12,              // side stick, snares
            [42] = 0x11, [46] = 0x18, [44] = 0x1B,              // hi-hat closed, open, pedal
            [49] = 0x16,                                        // crash 1
            [57] = 0x1A, [52] = 0x1A, [55] = 0x1A,              // crash 2, china, splash -> left cymbal
            [51] = 0x19, [53] = 0x19, [59] = 0x19,              // ride, ride bell, ride 2
            [48] = 0x14, [50] = 0x14,                           // high toms
            [45] = 0x15, [47] = 0x15,                           // low / mid toms
            [41] = 0x17, [43] = 0x17,                           // floor toms
        };

        /// <summary>Tab file extensions this converter reads, with the leading dot.</summary>
        public static IReadOnlyCollection<string> Extensions => _extensions;

        /// <summary>Whether <paramref name="filePath"/> has a tab file extension.</summary>
        public static bool IsTabFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            var extension = Path.GetExtension(filePath);
            return Array.Exists(_extensions, ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Reads a tab file and converts its drum part to DTX text.</summary>
        public static string ConvertFileToDtx(string filePath) =>
            ConvertToDtx(File.ReadAllBytes(filePath), Path.GetFileNameWithoutExtension(filePath));

        /// <summary>
        /// Converts the first percussion track of a tab file to DTX text.
        /// </summary>
        /// <param name="data">Raw Guitar Pro or MusicXML file bytes.</param>
        /// <param name="fallbackTitle">#TITLE used when the tab has no title.</param>
        /// <exception cref="UnplayableTabException">
        /// The tab has no drum track or no hits on a DTX drum lane, or is too long or
        /// has too many tempos for a DTX chart.
        /// </exception>
        public static string ConvertToDtx(byte[] data, string fallbackTitle)
        {
            var settings = new Settings();
            var score = ScoreLoader.LoadScoreFromBytes(data, settings);

            var drumTrack = score.Tracks.FirstOrDefault(t => t.Staves.Any(s => s.IsPercussion))
                ?? throw new UnplayableTabException("The tab file has no drum track.");

            // Muting does not stop the MIDI generator emitting a track, so every
            // other track is removed and all generated notes belong to the drums.
            for (int i = score.Tracks.Count - 1; i >= 0; i--)
            {
                if (score.Tracks[i] != drumTrack)
                    score.Tracks.RemoveAt(i);
            }

            var midi = GenerateMidi(score, settings);
            var tempoMap = midi.GetTempoMap();
            var notes = midi.GetNotes().ToList();
            if (!notes.Any(n => GmNoteToChannel.ContainsKey(n.NoteNumber)))
                throw new UnplayableTabException("The tab's drum track has no hits on a DTX drum lane.");

            var bars = GetBars(tempoMap, notes.Count == 0 ? 0 : notes.Max(n => n.Time));
            if (bars.Count > MaxChartBars)
                throw new UnplayableTabException(
                    $"The tab is {bars.Count - 1} bars long after repeats; at most {MaxChartBars - 1} fit in a chart.");

            var title = CleanHeaderText(score.Title);
            var artist = CleanHeaderText(score.Artist);
            var baseBpm = tempoMap.GetTempoAtTime(new MidiTimeSpan(0)).BeatsPerMinute;
            var tempoChanges = tempoMap.GetTempoChanges()
                .Where(c => c.Time > 0)
                .Select(c => (Tick: c.Time, Bpm: Math.Round(c.Value.BeatsPerMinute, 3)))
                .ToList();

            var dtx = new StringBuilder();
            dtx.AppendLine("; Converted from a Guitar Pro / MusicXML tab by DTXManiaCX");
            dtx.AppendLine($"#TITLE: {(title.Length > 0 ? title : CleanHeaderText(fallbackTitle))}");
            if (artist.Length > 0)
                dtx.AppendLine($"#ARTIST: {artist}");
            dtx.AppendLine("#GENRE: Imported tab");
            dtx.AppendLine($"#BPM: {FormatNumber(baseBpm)}");
            dtx.AppendLine($"#DLEVEL: {ImportedDrumLevel}");

            // #BPMxx definitions referenced by channel 08, one id per distinct tempo.
            var bpmIds = new Dictionary<double, int>();
            foreach (var change in tempoChanges.Where(c => !bpmIds.ContainsKey(c.Bpm)))
            {
                if (bpmIds.Count == MaxBpmIds)
                    throw new UnplayableTabException($"The tab has more than {MaxBpmIds} distinct tempos.");
                bpmIds[change.Bpm] = bpmIds.Count + 1;
                dtx.AppendLine($"#BPM{ToBase36(bpmIds[change.Bpm])}: {FormatNumber(change.Bpm)}");
            }

            // Chart bar 000 is an empty 4/4 count-in so the first hit is not at time zero.
            dtx.AppendLine();
            dtx.AppendLine("#00011: 00");

            var lines = new SortedDictionary<(int Bar, int Channel), int[]>();
            int[] Cells(int bar, int channel)
            {
                if (!lines.TryGetValue((bar, channel), out var cells))
                    lines[(bar, channel)] = cells = new int[bars[bar].Slots];
                return cells;
            }

            foreach (var change in tempoChanges)
            {
                var bar = FindBar(bars, change.Tick);
                Cells(bar, BpmChannel)[bars[bar].SlotOf(change.Tick)] = bpmIds[change.Bpm];
            }

            foreach (var note in notes)
            {
                if (!GmNoteToChannel.TryGetValue(note.NoteNumber, out var channel))
                    continue;

                var bar = FindBar(bars, note.Time);
                Cells(bar, channel)[bars[bar].SlotOf(note.Time)] = 1;
            }

            var currentLength = 1.0;
            for (int bar = 0; bar < bars.Count; bar++)
            {
                // #xxx02 carries forward in DTX, so it is written only when the length changes.
                if (Math.Abs(bars[bar].Length - currentLength) > 1e-9)
                {
                    currentLength = bars[bar].Length;
                    dtx.AppendLine($"#{FormatBar(bar)}02: {FormatNumber(currentLength, "0.######")}");
                }

                foreach (var ((_, channel), cells) in lines.Where(l => l.Key.Bar == bar))
                    dtx.AppendLine($"#{FormatBar(bar)}{channel:X2}: {FormatCells(cells)}");
            }

            return dtx.ToString();
        }

        private static MidiFile GenerateMidi(AlphaTab.Model.Score score, Settings settings)
        {
            var alphaTabMidi = new AlphaTab.Midi.MidiFile();
            new AlphaTab.Midi.MidiFileGenerator(
                score, settings, new AlphaTab.Midi.AlphaSynthMidiFileHandler(alphaTabMidi, true)).Generate();

            using var stream = new MemoryStream(((IEnumerable<byte>)alphaTabMidi.ToBinary()).ToArray());
            return MidiFile.Read(stream);
        }

        /// <summary>
        /// One tab bar laid out in MIDI ticks, with its DTX length and grid size.
        /// </summary>
        private readonly record struct TabBar(long Start, long Ticks, double Length, int Slots)
        {
            public int SlotOf(long tick) =>
                Math.Min(Slots - 1, (int)Math.Round((tick - Start) * (double)Slots / Ticks));
        }

        /// <summary>
        /// Bars from the MIDI time signature map, enough to cover
        /// <paramref name="lastTick"/>. Chart bar n+1 holds tab bar n, so
        /// index 0 is the count-in bar ahead of tick 0.
        /// </summary>
        private static List<TabBar> GetBars(TempoMap tempoMap, long lastTick)
        {
            var ticksPerQuarter = ((TicksPerQuarterNoteTimeDivision)tempoMap.TimeDivision).TicksPerQuarterNote;
            var countInTicks = 4L * ticksPerQuarter;
            var bars = new List<TabBar> { new(-countInTicks, countInTicks, 1.0, SlotsPerWholeNote) };

            var start = 0L;
            for (int bar = 1; start <= lastTick; bar++)
            {
                var end = TimeConverter.ConvertFrom(new BarBeatTicksTimeSpan(bar, 0, 0), tempoMap);
                var length = (end - start) / (4.0 * ticksPerQuarter);
                bars.Add(new TabBar(start, end - start, length, Math.Max(1, (int)Math.Round(SlotsPerWholeNote * length))));
                start = end;
            }

            return bars;
        }

        private static int FindBar(List<TabBar> bars, long tick)
        {
            for (int bar = bars.Count - 1; bar > 0; bar--)
            {
                if (tick >= bars[bar].Start)
                    return bar;
            }

            return 1;
        }

        /// <summary>
        /// Writes cells as two-character DTX values, shrunk to the coarsest
        /// grid that still holds every hit (e.g. four quarter notes become 4 cells).
        /// </summary>
        private static string FormatCells(int[] cells)
        {
            var step = cells.Length;
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] != 0)
                    step = GreatestCommonDivisor(step, i);
            }

            var text = new StringBuilder(cells.Length / step * 2);
            for (int i = 0; i < cells.Length; i += step)
                text.Append(cells[i] == 0 ? "00" : ToBase36(cells[i]));
            return text.ToString();
        }

        private static int GreatestCommonDivisor(int a, int b) => b == 0 ? a : GreatestCommonDivisor(b, a % b);

        private static string ToBase36(int value)
        {
            const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            return $"{digits[value / 36 % 36]}{digits[value % 36]}";
        }

        /// <summary>
        /// One-line header text: AlphaTab can hand back non-breaking spaces and
        /// line breaks, which are collapsed to single spaces.
        /// </summary>
        private static string CleanHeaderText(string text) =>
            string.Join(' ', (text ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

        private static string FormatBar(int bar) => bar.ToString("000", CultureInfo.InvariantCulture);

        private static string FormatNumber(double value, string format = "0.###") =>
            value.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A tab that cannot become a drum chart as the file stands: no drum
    /// track, more bars than DTX can number, or more tempos than #BPM ids.
    /// The song scan skips such a file until it changes instead of retrying
    /// it like a read or parse error.
    /// </summary>
    public sealed class UnplayableTabException : Exception
    {
        public UnplayableTabException(string message) : base(message) { }
    }
}
