using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Notation;
using Xunit;

namespace DTXMania.Test.Song.Notation
{
    /// <summary>
    /// Tests NotationChartBuilder. Most charts go through the real parser (DTX text to ParseAsync, which finalizes);
    /// the rest are hand-built and finalized with FinalizeChart.
    /// </summary>
    [Trait("Category", "Song")]
    public class NotationChartBuilderTests : IDisposable
    {
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"DTXMania_NotationBuilderTest_{Guid.NewGuid():N}");

        public NotationChartBuilderTests()
        {
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        #region Real-parser fixtures

        [Fact]
        public async Task SevenEighthEighths_ShouldPlaceOnsetsOnAuthoredSlotsAndKeepLegacyTiming()
        {
            ParsedChart parsed = await ParseDtxAsync(Dtx(
                "#BPM: 120",
                "#00002: 0.875",
                "#00011: 01010101010101"));

            NotationMeasure measure = Assert.Single(NotationChartBuilder.Build(parsed).Measures);

            Assert.Equal(168, measure.MeasureTicks);
            Assert.Equal(new[] { 0, 24, 48, 72, 96, 120, 144 }, measure.Entries.Select(e => e.StartTick));
            Assert.All(measure.Entries, entry => Assert.Equal(24, entry.DurationTicks));
            Assert.Empty(measure.Tuplets);

            // Gameplay timing is unchanged: the parser still truncates to canonical ticks.
            Assert.Equal(new[] { 0, 27, 54, 82, 109, 137, 164 }, parsed.Notes.Select(n => n.Tick));
            Assert.Equal(246.09375, parsed.Notes[1].TimeMs, 6);
            Assert.Equal(parsed.Notes.Select(n => n.TimeMs), measure.Entries.Select(e => e.TimeMs));
        }

        [Fact]
        public async Task SevenEighthSixteenths_ShouldPlaceFourteenOnsetsEvenly()
        {
            ParsedChart parsed = await ParseDtxAsync(Dtx(
                "#BPM: 120",
                "#00002: 0.875",
                $"#00011: {string.Concat(Enumerable.Repeat("01", 14))}"));

            NotationMeasure measure = Assert.Single(NotationChartBuilder.Build(parsed).Measures);

            Assert.Equal(
                new[] { 0, 12, 24, 36, 48, 60, 72, 84, 96, 108, 120, 132, 144, 156 },
                measure.Entries.Select(e => e.StartTick));
            Assert.All(measure.Entries, entry => Assert.Equal(12, entry.DurationTicks));
            Assert.Empty(measure.Tuplets);
        }

        [Fact]
        public async Task FiveFourEighthTriplets_ShouldDetectFortyEightTickGroupsOnAuthoredSlots()
        {
            ParsedChart parsed = await ParseDtxAsync(Dtx(
                "#BPM: 120",
                "#00002: 1.25",
                $"#00011: {string.Concat(Enumerable.Repeat("01", 15))}"));

            NotationMeasure measure = Assert.Single(NotationChartBuilder.Build(parsed).Measures);

            Assert.Equal(240, measure.MeasureTicks);
            Assert.Equal(
                Enumerable.Range(0, 15).Select(i => i * 16).ToArray(),
                measure.Entries.Select(e => e.StartTick));
            Assert.All(measure.Entries, entry => Assert.Equal(16, entry.DurationTicks));

            // Five 48-tick triplets (3 slots of 16), each starting on a multiple of 3 entries.
            Assert.Equal(new[] { 0, 3, 6, 9, 12 }, measure.Tuplets.Select(t => t.StartIndex));
            Assert.All(measure.Tuplets, tuplet =>
            {
                Assert.Equal(3, tuplet.Count);
                Assert.Equal(16, tuplet.SlotTicks);
            });

            // Parser Tick values are the legacy truncations of i * 192 / 15.
            Assert.Equal(
                new[] { 0, 12, 25, 38, 51, 64, 76, 89, 102, 115, 128, 140, 153, 166, 179 },
                parsed.Notes.Select(n => n.Tick));
            Assert.Equal(156.25, parsed.Notes[1].TimeMs, 6);
        }

        [Fact]
        public async Task HeldThreeQuarterBar_ShouldKeepMeasureTicksAndResolveTimesFromCanonicalPositions()
        {
            // Bar 0 sets 0.75, and bar 1 inherits it (DTX carry-forward).
            ParsedChart parsed = await ParseDtxAsync(Dtx(
                "#BPM: 120",
                "#00002: 0.75",
                "#00011: 0100",
                "#00111: 0001"));

            NotationChart chart = NotationChartBuilder.Build(parsed);

            Assert.Equal(new[] { 144, 144 }, chart.Measures.Select(m => m.MeasureTicks));
            Assert.Equal(new[] { 0, 96 }, chart.Measures[0].Entries.Select(e => e.StartTick));
            Assert.Equal(new[] { 0, 48, 72, 120 }, chart.Measures[1].Entries.Select(e => e.StartTick));

            // Canonical tick 96 is half of bar 1, which begins at 1500 ms. Notation tick 72 must not be
            // scaled again: the hit is at 2250 ms, not 2062.5 ms.
            NotationEntry hat = chart.Measures[1].Entries.Single(e => !e.IsRest);
            Assert.Equal(72, hat.StartTick);
            AssertTimes(chart.Measures[1].Entries, 1500.0, 2000.0, 2250.0, 2750.0);
            Assert.Equal(parsed.Notes[1].TimeMs, hat.TimeMs);
            Assert.Equal(96, parsed.Notes[1].Tick);
            Assert.Equal(parsed.Notes.Count, SourceNotes(chart).Count());
        }

        [Fact]
        public async Task LaterBarLengthOverride_ShouldApplyFromItsBarOnward()
        {
            ParsedChart parsed = await ParseDtxAsync(Dtx(
                "#BPM: 120",
                "#00002: 0.75",
                "#00202: 0.5",
                "#00211: 0001"));

            NotationChart chart = NotationChartBuilder.Build(parsed);

            Assert.Equal(new[] { 144, 144, 96 }, chart.Measures.Select(m => m.MeasureTicks));
            NotationMeasure third = chart.Measures[2];
            Assert.Equal(new[] { 0, 48 }, third.Entries.Select(e => e.StartTick));
            // Bar 2 starts at 3000 ms and is 2 beats long, so canonical tick 96 is 500 ms later.
            AssertTimes(third.Entries, 3000.0, 3500.0);
        }

        [Fact]
        public async Task InBarTempoChange_ShouldKeepEntryTimesMonotonicAndMatchTimingMap()
        {
            ParsedChart parsed = await ParseDtxAsync(Dtx(
                "#BPM: 120",
                "#00003: 0000B400",   // channel 03 direct BPM 0xB4 = 180 at pair 2 of 4 (tick 96)
                "#00011: 01000001"));  // hats on tick 0 and tick 144

            NotationMeasure measure = Assert.Single(NotationChartBuilder.Build(parsed).Measures);

            Assert.Equal(new[] { 0, 96, 144 }, measure.Entries.Select(e => e.StartTick));
            // 96 ticks at 120 BPM is 1000 ms. Ticks 96-144 run at 180 BPM, so tick 144 is 1333.33 ms.
            AssertTimes(measure.Entries, 0.0, 1000.0, 1333.333333333);
            Assert.True(measure.Entries.Zip(measure.Entries.Skip(1), (a, b) => a.TimeMs <= b.TimeMs).All(ok => ok));
        }

        [Fact]
        public async Task FourFourBar_ShouldYieldOneHundredNinetyTwoTicks()
        {
            ParsedChart parsed = await ParseDtxAsync(Dtx("#BPM: 120", "#00011: 0001"));   // hat on pair 1 of 2 (tick 96)

            NotationMeasure measure = Assert.Single(NotationChartBuilder.Build(parsed).Measures);

            Assert.Equal(192, measure.MeasureTicks);
            Assert.Equal(new[] { 0, 96 }, measure.Entries.Select(e => e.StartTick));
            Assert.Equal(new[] { true, false }, measure.Entries.Select(e => e.IsRest));
            AssertTimes(measure.Entries, 0.0, 1000.0);
        }

        [Fact]
        public async Task EmptyBarInsideCompiledChart_ShouldEmitRestWithBarStartTime()
        {
            ParsedChart parsed = await ParseDtxAsync(Dtx("#BPM: 120", "#00011: 01", "#00211: 01"));

            NotationChart chart = NotationChartBuilder.Build(parsed);

            Assert.Equal(3, chart.Measures.Count);
            NotationMeasure empty = chart.Measures[1];
            NotationEntry rest = Assert.Single(empty.Entries);
            Assert.True(rest.IsRest);
            Assert.Equal(192, rest.DurationTicks);
            Assert.Equal(2000.0, rest.TimeMs, 6);
            Assert.Empty(empty.Tuplets);
        }

        [Fact]
        public async Task HeaderOnlyFile_ShouldReturnNoMeasures()
        {
            ParsedChart parsed = await ParseDtxAsync(Dtx("#BPM: 120"));

            Assert.Empty(NotationChartBuilder.Build(parsed).Measures);
        }

        [Fact]
        public async Task OpenAndClosedHiHatAtOneOnset_ShouldStayDistinctHeads()
        {
            ParsedChart parsed = await ParseDtxAsync(Dtx("#BPM: 120", "#00011: 01", "#00018: 01"));

            NotationEntry chord = Assert.Single(Assert.Single(NotationChartBuilder.Build(parsed).Measures).Entries);

            Assert.Equal(2, chord.Heads.Count);
            NotationHead closed = chord.Heads.Single(h => h.Position == DrumStaffMapping.GetPosition(0x11));
            NotationHead open = chord.Heads.Single(h => h.Position == DrumStaffMapping.GetPosition(0x18));
            Assert.Equal(new[] { parsed.Notes.Single(n => n.Channel == 0x11) }, closed.Notes);
            Assert.Equal(new[] { parsed.Notes.Single(n => n.Channel == 0x18) }, open.Notes);
        }

        [Fact]
        public async Task BassAndLeftBassAtOneOnset_ShouldShareHeadAndKeepBothNotes()
        {
            ParsedChart parsed = await ParseDtxAsync(Dtx("#BPM: 120", "#00013: 01", "#0001C: 01"));

            NotationEntry chord = Assert.Single(Assert.Single(NotationChartBuilder.Build(parsed).Measures).Entries);

            NotationHead head = Assert.Single(chord.Heads);
            Assert.Equal(DrumStaffMapping.GetPosition(0x13), head.Position);
            Assert.Equal(2, head.Notes.Count);
            Assert.Contains(parsed.Notes.Single(n => n.Channel == 0x13), head.Notes);
            Assert.Contains(parsed.Notes.Single(n => n.Channel == 0x1C), head.Notes);
        }

        #endregion

        #region Hand-built and boundary cases

        [Fact]
        public void EmptyChart_ShouldReturnNoMeasures()
        {
            var parsed = new ParsedChart();
            parsed.FinalizeChart();

            Assert.Empty(NotationChartBuilder.Build(parsed).Measures);
        }

        [Fact]
        public void HandBuiltNotesWithoutSlotData_ShouldUseCanonicalTickFraction()
        {
            var parsed = new ParsedChart();
            parsed.AddNote(new Note(1, 0, 96, 0x11, "01"));   // canonical half bar
            parsed.AddNote(new Note(3, 0, 200, 0x12, "01"));  // oversized: normalizes to bar 1, tick 8
            parsed.FinalizeChart();

            NotationChart chart = NotationChartBuilder.Build(parsed);

            Assert.Equal(2, chart.Measures.Count);
            Assert.Equal(96, chart.Measures[0].Entries.Single(e => !e.IsRest).StartTick);
            Note snareNote = parsed.Notes.Single(n => n.Channel == 0x12);
            NotationEntry snare = chart.Measures[1].Entries.Single(e => !e.IsRest);
            Assert.Equal(8, snare.StartTick);
            Assert.Same(snareNote, snare.Heads.Single().Notes.Single());
            Assert.Equal(snareNote.TimeMs, snare.TimeMs);
        }

        [Fact]
        public void DegenerateMultiplier_ShouldFloorMeasureTicksAtOne()
        {
            var parsed = new ParsedChart();
            parsed.TimingMap.SetMeasureLength(0, 0.001);   // 192 * 0.001 rounds to 0
            parsed.AddNote(new Note(0, 0, 0, 0x11, "01"));
            parsed.FinalizeChart();

            NotationMeasure measure = Assert.Single(NotationChartBuilder.Build(parsed).Measures);

            Assert.Equal(1, measure.MeasureTicks);
            NotationEntry chord = Assert.Single(measure.Entries.Where(e => !e.IsRest));
            Assert.Equal(0, chord.StartTick);
        }

        [Fact]
        public void PairPositionAtBarEnd_ShouldClampOnsetInsideBar()
        {
            // Slot 383 of 384 is 191.5 of 192 notation ticks. It rounds up to the bar end, so the clamp must hold it at 191.
            var parsed = new ParsedChart();
            var note = new Note(0, 0, 191, 0x11, "01") { PairIndex = 383, PairCount = 384 };
            parsed.AddNote(note);
            parsed.FinalizeChart();

            NotationMeasure measure = Assert.Single(NotationChartBuilder.Build(parsed).Measures);

            Assert.Equal(192, measure.MeasureTicks);
            NotationEntry last = measure.Entries[^1];
            Assert.Equal(191, last.StartTick);
            Assert.Same(note, last.Heads.Single().Notes.Single());
        }

        [Fact]
        public void CanonicalTickAtBarEnd_ShouldClampOnsetInsideHalfBar()
        {
            // Canonical 191 of 192 in a 96-tick bar is 95.5, which rounds to 96. The clamp must hold it at 95.
            var parsed = new ParsedChart();
            parsed.TimingMap.SetMeasureLength(0, 0.5);
            var note = new Note(0, 0, 191, 0x11, "01");
            parsed.AddNote(note);
            parsed.FinalizeChart();

            NotationMeasure measure = Assert.Single(NotationChartBuilder.Build(parsed).Measures);

            Assert.Equal(96, measure.MeasureTicks);
            NotationEntry last = measure.Entries[^1];
            Assert.Equal(95, last.StartTick);
            Assert.Same(note, last.Heads.Single().Notes.Single());
        }

        [Fact]
        public void SourcesCollapsingToOneTick_ShouldUseEarliestTimeAndKeepEveryNote()
        {
            // A quarter-length bar has 48 notation ticks. Canonical ticks 0 and 1 both round to notation tick 0.
            var parsed = new ParsedChart();
            parsed.TimingMap.SetMeasureLength(0, 0.25);
            var first = new Note(0, 0, 0, 0x11, "01");
            var second = new Note(0, 0, 1, 0x11, "01");
            parsed.AddNote(first);
            parsed.AddNote(second);
            parsed.FinalizeChart();

            NotationMeasure measure = Assert.Single(NotationChartBuilder.Build(parsed).Measures);

            NotationEntry chord = Assert.Single(measure.Entries.Where(e => !e.IsRest));
            Assert.Equal(0, chord.StartTick);
            Assert.Equal(first.TimeMs, chord.TimeMs);
            Assert.Equal(new[] { first, second }, Assert.Single(chord.Heads).Notes);
        }

        [Fact]
        public void UnsupportedChannel_ShouldThrowInsteadOfDroppingNote()
        {
            var parsed = new ParsedChart();
            parsed.AddNote(new Note(0, 0, 0, 0x20, "01"));
            parsed.FinalizeChart();

            Assert.Throws<ArgumentOutOfRangeException>(() => NotationChartBuilder.Build(parsed));
        }

        #endregion

        #region Source-note lifecycle

        private const string LifecycleChart =
            "#BPM: 120\n" +
            "#00011: 0101\n" +   // hi-hat on tick 0 and tick 96
            "#00012: 0001\n";    // snare on tick 96

        [Fact]
        public async Task BuildBeforeChartManager_ShouldExposeIdsThatGameplayAssignsLater()
        {
            ParsedChart parsed = await ParseDtxAsync(LifecycleChart);
            foreach (Note note in parsed.Notes)
            {
                note.Id = -1;   // sentinel: building notation must not assign or renumber IDs
            }

            NotationChart notation = NotationChartBuilder.Build(parsed);

            Assert.All(parsed.Notes, note => Assert.Equal(-1, note.Id));
            var manager = new ChartManager(parsed);
            AssertHeadsReferenceGameplayNotes(notation, manager);
        }

        [Fact]
        public async Task ChartManagerBeforeBuild_ShouldExposeSameIdsOnBuiltReferences()
        {
            ParsedChart parsed = await ParseDtxAsync(LifecycleChart);
            var manager = new ChartManager(parsed);

            NotationChart notation = NotationChartBuilder.Build(parsed);

            AssertHeadsReferenceGameplayNotes(notation, manager);
        }

        private static void AssertHeadsReferenceGameplayNotes(NotationChart notation, ChartManager manager)
        {
            IReadOnlyList<Note> gameplayNotes = manager.AllNotes;
            List<Note> referenced = SourceNotes(notation).ToList();

            Assert.Equal(gameplayNotes.Count, referenced.Count);
            for (int id = 0; id < gameplayNotes.Count; id++)
            {
                Assert.Same(gameplayNotes[id], referenced.Single(note => note.Id == id));
            }
        }

        #endregion

        #region Helpers

        private async Task<ParsedChart> ParseDtxAsync(string text)
        {
            var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.dtx");
            File.WriteAllText(path, text);
            return await DTXChartParser.ParseAsync(path);
        }

        private static string Dtx(params string[] lines) => string.Join("\n", lines) + "\n";

        private static IEnumerable<Note> SourceNotes(NotationChart chart) =>
            chart.Measures.SelectMany(m => m.Entries).SelectMany(e => e.Heads).SelectMany(h => h.Notes);

        private static void AssertTimes(IReadOnlyList<NotationEntry> entries, params double[] expectedMs)
        {
            Assert.Equal(expectedMs.Length, entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                Assert.Equal(expectedMs[i], entries[i].TimeMs, 6);
            }
        }

        #endregion
    }
}
