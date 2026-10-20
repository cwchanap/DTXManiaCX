using System;
using System.Linq;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Notation;
using Xunit;

namespace DTXMania.Test.Song.Notation
{
    /// <summary>
    /// Ports DTXWeb quantize.test.ts (quantizeMeasure and ticksToDurations cases) to NotationQuantizer.
    /// TS position fractions become notation ticks (position 16/192 in a 192-tick bar is tick 16).
    /// Channels go through DrumStaffMapping, as the chart builder does.
    /// </summary>
    [Trait("Category", "Song")]
    public class NotationQuantizerTests
    {
        private static readonly DrumStaffPosition SnarePosition = DrumStaffMapping.GetPosition(0x12);

        [Fact]
        public void TicksToBinaryDurations_ShouldDecomposeLargestFirst()
        {
            Assert.Equal(new[] { 192 }, NotationQuantizer.TicksToBinaryDurations(192));
            Assert.Equal(new[] { 48 }, NotationQuantizer.TicksToBinaryDurations(48));
            Assert.Equal(new[] { 24 }, NotationQuantizer.TicksToBinaryDurations(24));
            Assert.Equal(new[] { 12 }, NotationQuantizer.TicksToBinaryDurations(12));
            Assert.Equal(new[] { 48, 24 }, NotationQuantizer.TicksToBinaryDurations(72));
            Assert.Equal(new[] { 48, 12 }, NotationQuantizer.TicksToBinaryDurations(60));
            Assert.Equal(new[] { 96, 24, 12 }, NotationQuantizer.TicksToBinaryDurations(132));
            // A 1-tick remainder has no binary value and is not represented.
            Assert.Equal(new[] { 24 }, NotationQuantizer.TicksToBinaryDurations(25));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        [InlineData(2)]
        public void TicksToBinaryDurations_ShouldReturnNothingBelowThreeTicks(int ticks)
        {
            Assert.Empty(NotationQuantizer.TicksToBinaryDurations(ticks));
        }

        [Theory]
        [InlineData(192, new[] { 192 })]
        [InlineData(144, new[] { 96, 48 })]
        [InlineData(96, new[] { 96 })]
        [InlineData(48, new[] { 48 })]
        public void EmptyBar_ShouldFillWithBinaryRestsThatSumToBarLength(int measureTicks, int[] expectedRests)
        {
            NotationMeasure measure = Quantize(measureTicks);

            Assert.Equal(measureTicks, measure.MeasureTicks);
            Assert.Equal(expectedRests, measure.Entries.Select(e => e.DurationTicks).ToArray());
            Assert.All(measure.Entries, e => Assert.True(e.IsRest));
            Assert.Empty(measure.Tuplets);
        }

        [Fact]
        public void TwoTickEmptyBar_ShouldKeepOneRestEntry()
        {
            // Two ticks cannot hold a binary rest, so the single-entry fallback keeps the bar renderable.
            Assert.Equal(new[] { Rest(0, 2) }, Shape(Quantize(2)));
        }

        [Fact]
        public void QuarterNotePattern_ShouldProduceFourNotesOf48TicksWithoutTuplets()
        {
            NotationMeasure measure = Quantize(192, SnareAt(0), SnareAt(48), SnareAt(96), SnareAt(144));

            Assert.Equal(new[] { Hit(0, 48), Hit(48, 48), Hit(96, 48), Hit(144, 48) }, Shape(measure));
            Assert.All(measure.Entries, e => Assert.Equal(new[] { SnarePosition }, e.Heads.Select(h => h.Position)));
            Assert.All(measure.Entries, e => Assert.Equal(0.0, e.TimeMs));
            Assert.Empty(measure.Tuplets);
        }

        [Fact]
        public void SingleOnset_ShouldFillWholeBarWithOneNote()
        {
            Assert.Equal(new[] { Hit(0, 192) }, Shape(Quantize(192, SnareAt(0))));
        }

        [Fact]
        public void LeadingGap_ShouldEmitRestBeforeFirstOnset()
        {
            Assert.Equal(new[] { Rest(0, 96), Hit(96, 96) }, Shape(Quantize(192, SnareAt(96))));
        }

        [Fact]
        public void NoteFollowedByGap_ShouldFillRemainderWithRests()
        {
            // Span 144 after the second onset: a half note (96) and a quarter rest (48) to the barline.
            Assert.Equal(
                new[] { Hit(0, 48), Hit(48, 96), Rest(144, 48) },
                Shape(Quantize(192, SnareAt(0), SnareAt(48))));
        }

        [Theory]
        [InlineData(72, 48, 24)] // span 72 -> note 48 + rest 24
        [InlineData(60, 48, 12)] // span 60 -> note 48 + rest 12
        public void NonBinarySpan_ShouldSplitNoteAndRestLargestFirst(int nextOnset, int noteTicks, int restTicks)
        {
            NotationMeasure measure = Quantize(192, SnareAt(0), SnareAt(nextOnset));

            Assert.Equal(new[] { Hit(0, noteTicks), Rest(noteTicks, restTicks) }, Shape(measure).Take(2).ToArray());
        }

        [Fact]
        public void SimultaneousHeads_ShouldMergeIntoOneChordOrderedByStaffStep()
        {
            // The hi-hat is listed first on purpose: output order follows staff step, not input order.
            NotationMeasure measure = Quantize(192, OnsetAt(0, 0x11), OnsetAt(0, 0x13));

            NotationEntry chord = Assert.Single(measure.Entries);
            Assert.Equal(
                new[] { DrumStaffMapping.GetPosition(0x13), DrumStaffMapping.GetPosition(0x11) },
                chord.Heads.Select(h => h.Position));
        }

        [Fact]
        public void OpenAndClosedHiHat_ShouldStayDistinctHeadsAtOneOnset()
        {
            NotationMeasure measure = Quantize(192, OnsetAt(0, 0x11), OnsetAt(0, 0x18));

            NotationHead[] heads = Assert.Single(measure.Entries).Heads.ToArray();
            Assert.Equal(
                new[] { DrumStaffMapping.GetPosition(0x11), DrumStaffMapping.GetPosition(0x18) },
                heads.Select(h => h.Position));
        }

        [Fact]
        public void BassAndLeftBass_ShouldShareOneHeadAndKeepBothSourceNotes()
        {
            var bass = new Note { Channel = 0x13 };
            var leftBass = new Note { Channel = 0x1C };

            NotationMeasure measure = Quantize(192, OnsetAt(0, 0x13, bass), OnsetAt(0, 0x1C, leftBass));

            NotationHead head = Assert.Single(Assert.Single(measure.Entries).Heads);
            Assert.Equal(DrumStaffMapping.GetPosition(0x13), head.Position);
            Assert.Equal(2, head.Notes.Count);
            Assert.Same(bass, head.Notes[0]);
            Assert.Same(leftBass, head.Notes[1]);
        }

        [Fact]
        public void OnsetsSharingOneQuantizedTick_ShouldMergeIntoOneChord()
        {
            var first = new Note { Channel = 0x12 };
            var second = new Note { Channel = 0x12 };

            NotationMeasure measure = Quantize(192, SnareAt(48, first), SnareAt(48, second));

            NotationEntry chord = measure.Entries.Single(e => !e.IsRest);
            Assert.Equal(48, chord.StartTick);
            NotationHead head = Assert.Single(chord.Heads);
            Assert.Equal(SnarePosition, head.Position);
            Assert.Equal(2, head.Notes.Count);
            Assert.Same(first, head.Notes[0]);
            Assert.Same(second, head.Notes[1]);
        }

        [Fact]
        public void EighthNoteTriplet_ShouldDetectTupletOverThreeSlots()
        {
            NotationMeasure measure = Quantize(192, SnareAt(0), SnareAt(16), SnareAt(32));

            Assert.Equal(new[] { Hit(0, 16), Hit(16, 16), Hit(32, 16), Rest(48, 96), Rest(144, 48) }, Shape(measure));
            Assert.Equal(new[] { new NotationTuplet(0, 3, 16) }, measure.Tuplets);
        }

        [Fact]
        public void TripletRestInsideGroup_ShouldKeepEmptySlotInsideTuplet()
        {
            NotationMeasure measure = Quantize(192, SnareAt(0), SnareAt(32));

            Assert.Equal(new[] { Hit(0, 16), Rest(16, 16), Hit(32, 16), Rest(48, 96), Rest(144, 48) }, Shape(measure));
            Assert.Equal(new[] { new NotationTuplet(0, 3, 16) }, measure.Tuplets);
        }

        [Fact]
        public void TripletWithObservedEnd_ShouldUseTrailingRestInsideGroup()
        {
            // The onset at 48 marks the group end, so the 48-tick group wins and slot 2 becomes a rest.
            NotationMeasure measure = Quantize(192, SnareAt(0), SnareAt(16), SnareAt(48));

            Assert.Equal(new[] { Hit(0, 16), Hit(16, 16), Rest(32, 16), Hit(48, 96), Rest(144, 48) }, Shape(measure));
            Assert.Equal(new[] { new NotationTuplet(0, 3, 16) }, measure.Tuplets);
        }

        [Fact]
        public void TripletAfterBinaryQuarter_ShouldIndexTupletFromSecondEntry()
        {
            NotationMeasure measure = Quantize(192, SnareAt(0), SnareAt(48), SnareAt(64), SnareAt(80));

            Assert.Equal(new[] { Hit(0, 48), Hit(48, 16), Hit(64, 16), Hit(80, 16), Rest(96, 96) }, Shape(measure));
            Assert.Equal(new[] { new NotationTuplet(1, 3, 16) }, measure.Tuplets);
        }

        [Fact]
        public void TripletAfterLeadingRest_ShouldIndexTupletAfterRest()
        {
            NotationMeasure measure = Quantize(192, SnareAt(48), SnareAt(64), SnareAt(80));

            Assert.Equal(new[] { Rest(0, 48), Hit(48, 16), Hit(64, 16), Hit(80, 16), Rest(96, 96) }, Shape(measure));
            Assert.Equal(new[] { new NotationTuplet(1, 3, 16) }, measure.Tuplets);
        }

        [Fact]
        public void QuarterNoteTriplet_ShouldDetectGroupOf96Ticks()
        {
            NotationMeasure measure = Quantize(192, SnareAt(0), SnareAt(32), SnareAt(64));

            Assert.Equal(new[] { Hit(0, 32), Hit(32, 32), Hit(64, 32), Rest(96, 96) }, Shape(measure));
            Assert.Equal(new[] { new NotationTuplet(0, 3, 32) }, measure.Tuplets);
        }

        [Fact]
        public void HalfNoteTriplet_ShouldDetectGroupOf192Ticks()
        {
            NotationMeasure measure = Quantize(192, SnareAt(0), SnareAt(64), SnareAt(128));

            Assert.Equal(new[] { Hit(0, 64), Hit(64, 64), Hit(128, 64) }, Shape(measure));
            Assert.Equal(new[] { new NotationTuplet(0, 3, 64) }, measure.Tuplets);
        }

        [Fact]
        public void SixteenthTriplet_ShouldDetectGroupOf24TicksBeforeFollowingNote()
        {
            NotationMeasure measure = Quantize(192, SnareAt(0), SnareAt(8), SnareAt(16), SnareAt(24));

            Assert.Equal(
                new[] { Hit(0, 8), Hit(8, 8), Hit(16, 8), Hit(24, 96), Rest(120, 48), Rest(168, 24) },
                Shape(measure));
            Assert.Equal(new[] { new NotationTuplet(0, 3, 8) }, measure.Tuplets);
        }

        [Fact]
        public void SixteenthTripletWithoutObservedEnd_ShouldRestUntilNextOnset()
        {
            NotationMeasure measure = Quantize(192, SnareAt(0), SnareAt(8), SnareAt(16), SnareAt(48));

            Assert.Equal(
                new[] { Hit(0, 8), Hit(8, 8), Hit(16, 8), Rest(24, 24), Hit(48, 96), Rest(144, 48) },
                Shape(measure));
            Assert.Equal(new[] { new NotationTuplet(0, 3, 8) }, measure.Tuplets);
        }

        [Fact]
        public void IsolatedSixteenthPair_ShouldChooseSixteenthTripletWithMiddleRest()
        {
            // The 48-tick group has an empty last slot and no onset at its end, so it is rejected.
            NotationMeasure measure = Quantize(192, SnareAt(0), SnareAt(16));

            Assert.Equal(
                new[] { Hit(0, 8), Rest(8, 8), Hit(16, 8), Rest(24, 96), Rest(120, 48), Rest(168, 24) },
                Shape(measure));
            Assert.Equal(new[] { new NotationTuplet(0, 3, 8) }, measure.Tuplets);
        }

        [Fact]
        public void RestNoteNoteTriplet_ShouldAcceptEmptyFirstSlot()
        {
            NotationMeasure measure = Quantize(192, SnareAt(16), SnareAt(32));

            Assert.Equal(new[] { Rest(0, 16), Hit(16, 16), Hit(32, 16), Rest(48, 96), Rest(144, 48) }, Shape(measure));
            Assert.Equal(new[] { new NotationTuplet(0, 3, 16) }, measure.Tuplets);
        }

        [Fact]
        public void TripletTie_ShouldPreferGroupEndingOnObservedOnset()
        {
            // The 48- and 96-tick groups both hold two onsets; the 96-tick group wins because its end (96) is an onset.
            NotationMeasure measure = Quantize(192, SnareAt(0), SnareAt(32), SnareAt(96));

            Assert.Equal(new[] { Hit(0, 32), Hit(32, 32), Rest(64, 32), Hit(96, 96) }, Shape(measure));
            Assert.Equal(new[] { new NotationTuplet(0, 3, 32) }, measure.Tuplets);
        }

        [Fact]
        public void ConsecutiveEighthTriplets_ShouldDetectTwoTupletsOfThreeSlots()
        {
            NotationMeasure measure = Quantize(
                192,
                SnareAt(0), SnareAt(16), SnareAt(32), SnareAt(48), SnareAt(64), SnareAt(80));

            Assert.Equal(
                new[] { Hit(0, 16), Hit(16, 16), Hit(32, 16), Hit(48, 16), Hit(64, 16), Hit(80, 16), Rest(96, 96) },
                Shape(measure));
            Assert.Equal(new[] { new NotationTuplet(0, 3, 16), new NotationTuplet(3, 3, 16) }, measure.Tuplets);
        }

        [Fact]
        public void NinetySixTripletAtBarEnd_ShouldDetectGroupAfterHalfNote()
        {
            NotationMeasure measure = Quantize(192, SnareAt(0), SnareAt(96), SnareAt(128), SnareAt(160));

            Assert.Equal(new[] { Hit(0, 96), Hit(96, 32), Hit(128, 32), Hit(160, 32) }, Shape(measure));
            Assert.Equal(new[] { new NotationTuplet(1, 3, 32) }, measure.Tuplets);
        }

        [Fact]
        public void ChordInsideTripletGroup_ShouldKeepOneEntryPerSlotWithAllHeads()
        {
            NotationMeasure measure = Quantize(
                192,
                OnsetAt(0, 0x11), OnsetAt(0, 0x12),
                OnsetAt(16, 0x11), OnsetAt(16, 0x12),
                OnsetAt(32, 0x11), OnsetAt(32, 0x12));

            Assert.Equal(new[] { Hit(0, 16), Hit(16, 16), Hit(32, 16), Rest(48, 96), Rest(144, 48) }, Shape(measure));
            Assert.Equal(new[] { new NotationTuplet(0, 3, 16) }, measure.Tuplets);
            var expectedHeads = new[] { SnarePosition, DrumStaffMapping.GetPosition(0x11) };
            Assert.All(measure.Entries.Take(3), e => Assert.Equal(expectedHeads, e.Heads.Select(h => h.Position)));
        }

        [Fact]
        public void ThreeQuarterBar_ShouldFitTripletInside144Ticks()
        {
            NotationMeasure measure = Quantize(144, SnareAt(0), SnareAt(32), SnareAt(64));

            Assert.Equal(144, measure.MeasureTicks);
            Assert.Equal(new[] { Hit(0, 32), Hit(32, 32), Hit(64, 32), Rest(96, 48) }, Shape(measure));
            Assert.Equal(new[] { new NotationTuplet(0, 3, 32) }, measure.Tuplets);
        }

        [Fact]
        public void TwoQuarterBar_ShouldFitTripletInside96Ticks()
        {
            NotationMeasure measure = Quantize(96, SnareAt(0), SnareAt(16), SnareAt(32));

            Assert.Equal(96, measure.MeasureTicks);
            Assert.Equal(new[] { Hit(0, 16), Hit(16, 16), Hit(32, 16), Rest(48, 48) }, Shape(measure));
            Assert.Equal(new[] { new NotationTuplet(0, 3, 16) }, measure.Tuplets);
        }

        [Fact]
        public void ThreeTickSpan_ShouldEmitSixtyFourthNoteWithoutRemainder()
        {
            Assert.Equal(Hit(0, 3), Shape(Quantize(192, SnareAt(0), SnareAt(3)))[0]);
        }

        [Fact]
        public void TwoTickSpan_ShouldKeepRawTwoTickNote_DocumentedException()
        {
            // No binary value fits 2 ticks, so the note keeps its raw span length. No 2-tick glyph is invented.
            Assert.Equal(Hit(0, 2), Shape(Quantize(192, SnareAt(0), SnareAt(2)))[0]);
        }

        [Fact]
        public void SubThreeTickRemainderAfterNote_ShouldBeDropped_DocumentedException()
        {
            // The first note (48) leaves a 1-tick gap at [48, 49) with no rest to absorb it, so it is dropped.
            // The second note's 2-tick tail folds into its last rest, which becomes a 5-tick rest.
            NotationMeasure measure = Quantize(192, SnareAt(0), SnareAt(49));

            Assert.Equal(
                new[] { Hit(0, 48), Hit(49, 96), Rest(145, 24), Rest(169, 12), Rest(181, 6), Rest(187, 5) },
                Shape(measure));
        }

        [Fact]
        public void OffGridOnset_ShouldFoldRemainderIntoRestSoBarIsCovered()
        {
            // Tick 17 (about 1/11 of a bar). The 2-tick leftover after the 12+3 leading rests folds into the 3-tick rest.
            NotationMeasure measure = Quantize(192, SnareAt(17));

            Assert.Equal(
                new[] { Rest(0, 12), Rest(12, 5), Hit(17, 96), Rest(113, 48), Rest(161, 24), Rest(185, 7) },
                Shape(measure));
            AssertContiguousCover(measure);
        }

        [Fact]
        public void OneTickLeadingGap_ShouldBeDroppedAndFirstOnsetStaysAtTickOne_DocumentedException()
        {
            NotationMeasure measure = Quantize(192, SnareAt(1));

            Assert.Equal(
                new[] { Hit(1, 96), Rest(97, 48), Rest(145, 24), Rest(169, 12), Rest(181, 6), Rest(187, 5) },
                Shape(measure));
        }

        [Theory]
        [InlineData(192)]
        [InlineData(144)]
        [InlineData(96)]
        [InlineData(48)]
        public void GridAlignedOnsets_ShouldCoverBarContiguously(int measureTicks)
        {
            // Onsets on multiples of 3 keep every span representable, so no remainder is dropped.
            var random = new Random(measureTicks);
            NotationOnset[] onsets = Enumerable.Range(0, measureTicks / 3)
                .Where(_ => random.Next(2) == 0)
                .Select(i => SnareAt(i * 3))
                .ToArray();

            AssertContiguousCover(Quantize(measureTicks, onsets));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(10)]
        [InlineData(11)]
        [InlineData(12)]
        [InlineData(13)]
        [InlineData(14)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(18)]
        [InlineData(19)]
        [InlineData(20)]
        public void RandomOnsets_ShouldStayOrderedInsideBarAndReferenceEverySourceNote(int seed)
        {
            var random = new Random(seed);
            int[] barLengths = { 192, 144, 96, 48 };
            int[] channels = { 0x11, 0x12, 0x13, 0x14, 0x18, 0x19 };
            int measureTicks = barLengths[random.Next(barLengths.Length)];
            NotationOnset[] onsets = Enumerable.Range(0, random.Next(0, 25))
                .Select(_ => OnsetAt(random.Next(measureTicks), channels[random.Next(channels.Length)]))
                .ToArray();

            NotationMeasure measure = Quantize(measureTicks, onsets);

            int previousEnd = 0;
            foreach (NotationEntry entry in measure.Entries)
            {
                Assert.True(entry.DurationTicks >= 1);
                Assert.True(entry.StartTick >= previousEnd, $"Entry at {entry.StartTick} overlaps the previous entry.");
                Assert.True(entry.StartTick + entry.DurationTicks <= measureTicks);
                previousEnd = entry.StartTick + entry.DurationTicks;
            }

            foreach (int tick in onsets.Select(o => o.Tick).Distinct())
            {
                Assert.Contains(measure.Entries, e => e.StartTick == tick && !e.IsRest);
            }

            var referenced = measure.Entries.SelectMany(e => e.Heads).SelectMany(h => h.Notes).ToList();
            Assert.Equal(onsets.Length, referenced.Count);
            foreach (NotationOnset onset in onsets)
            {
                Assert.Contains(referenced, note => ReferenceEquals(note, onset.Note));
            }

            foreach (NotationTuplet tuplet in measure.Tuplets)
            {
                Assert.True(tuplet.StartIndex + tuplet.Count <= measure.Entries.Count);
                NotationEntry first = measure.Entries[tuplet.StartIndex];
                for (int i = 0; i < tuplet.Count; i++)
                {
                    NotationEntry slot = measure.Entries[tuplet.StartIndex + i];
                    Assert.Equal(tuplet.SlotTicks, slot.DurationTicks);
                    Assert.Equal(first.StartTick + i * tuplet.SlotTicks, slot.StartTick);
                }
            }
        }

        [Fact]
        public void Quantize_ShouldNotMutateSourceNotesOrAssignIds()
        {
            var bass = new Note(5, 0, 96, 0x13, "01") { TimeMs = 2250 };
            var leftBass = new Note(2, 0, 96, 0x1C, "02") { TimeMs = 2250 };

            NotationMeasure measure = Quantize(192, OnsetAt(48, 0x13, bass), OnsetAt(48, 0x1C, leftBass));

            Assert.Equal(0, bass.Id);
            Assert.Equal(0, leftBass.Id);
            Assert.Equal((5, 96, 2250.0, 0x13), (bass.LaneIndex, bass.Tick, bass.TimeMs, bass.Channel));
            Assert.Equal((2, 96, 2250.0, 0x1C), (leftBass.LaneIndex, leftBass.Tick, leftBass.TimeMs, leftBass.Channel));
            NotationHead head = Assert.Single(measure.Entries.Single(e => !e.IsRest).Heads);
            Assert.Same(bass, head.Notes[0]);
            Assert.Same(leftBass, head.Notes[1]);
        }

        [Fact]
        public void Quantize_ShouldOrderHeadsIndependentlyOfOnsetOrder()
        {
            NotationOnset[] forward = { OnsetAt(0, 0x13), OnsetAt(0, 0x11), SnareAt(48), OnsetAt(48, 0x19) };
            NotationOnset[] reversed = forward.Reverse().ToArray();

            NotationMeasure first = Quantize(192, forward);
            NotationMeasure second = Quantize(192, reversed);

            Assert.Equal(Shape(first), Shape(second));
            Assert.Equal(Describe(first), Describe(second));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(192)]
        public void OnsetOutsideBar_ShouldThrowInsteadOfDroppingTheNote(int tick)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Quantize(192, SnareAt(tick)));
        }

        [Fact]
        public void NonPositiveBarLength_ShouldThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Quantize(0));
        }

        private static NotationOnset SnareAt(int tick, Note? note = null) => OnsetAt(tick, 0x12, note);

        private static NotationOnset OnsetAt(int tick, int channel, Note? note = null) =>
            new(tick, DrumStaffMapping.GetPosition(channel), note ?? new Note { Channel = channel });

        private static NotationMeasure Quantize(int measureTicks, params NotationOnset[] onsets) =>
            NotationQuantizer.QuantizeMeasure(0, measureTicks, onsets);

        private static (int Start, int Duration, bool IsRest) Hit(int start, int duration) => (start, duration, false);

        private static (int Start, int Duration, bool IsRest) Rest(int start, int duration) => (start, duration, true);

        // (StartTick, DurationTicks, IsRest) per entry, for compact shape assertions.
        private static (int Start, int Duration, bool IsRest)[] Shape(NotationMeasure measure) =>
            measure.Entries.Select(e => (e.StartTick, e.DurationTicks, e.IsRest)).ToArray();

        private static string[] Describe(NotationMeasure measure) =>
            measure.Entries
                .Select(e => $"{e.StartTick}+{e.DurationTicks}:" +
                             string.Join(",", e.Heads.Select(h => $"{h.Position.StaffStep}{h.Position.Shape}")))
                .ToArray();

        // Entries are contiguous from tick 0 and fill the whole bar.
        private static void AssertContiguousCover(NotationMeasure measure)
        {
            int cursor = 0;
            foreach (NotationEntry entry in measure.Entries)
            {
                Assert.Equal(cursor, entry.StartTick);
                cursor += entry.DurationTicks;
            }

            Assert.Equal(measure.MeasureTicks, cursor);
        }
    }
}
