using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using DTXMania.Game.Lib.Song.Notation;
using DTXMania.Test.TestData;
using Xunit;

namespace DTXMania.Test.Song.Notation
{
    /// <summary>
    /// Pure contract for the HPA-409 notation glyph set: SMuFL code points,
    /// duration → glyph selection (binary, tuplet and non-binary folded
    /// durations), pinned Bravura 1.482 metrics, and IsComplete validation.
    /// GPU-free by design — no GraphicsDevice, no SpriteFont, no IFont.
    ///
    /// Internal enums/records stay out of theory signatures (they would be
    /// less accessible than the public test methods): rows carry member names
    /// or code points and parse them inside the test body, as
    /// DrumStaffMappingTests does for NoteheadShape.
    ///
    /// The real-render smoke (compiled SpriteFont drawn through
    /// IFont.DrawString, px per staff-space ≈ 13.4–13.65, baseline offset
    /// ≈ (−0.36, +108) px, anchors visually verified) was performed once in
    /// the HPA-409 spike against pinned Bravura 1.482 metadata; the measured
    /// values above (and the release pin in the design spec) are the durable
    /// record. It is deliberately not a permanent test: the Mac suite
    /// excludes GPU tests.
    /// </summary>
    [Trait("Category", "Song")]
    public class NotationGlyphsTests
    {
        // ------------------------------------------------------------------
        // Glyph → SMuFL code point mapping (enum values ARE the code points).
        // ------------------------------------------------------------------

        [Theory]
        [InlineData("UnpitchedPercussionClef1", 0xE069)]
        [InlineData("TimeSig0", 0xE080)]
        [InlineData("TimeSig1", 0xE081)]
        [InlineData("TimeSig2", 0xE082)]
        [InlineData("TimeSig3", 0xE083)]
        [InlineData("TimeSig4", 0xE084)]
        [InlineData("TimeSig5", 0xE085)]
        [InlineData("TimeSig6", 0xE086)]
        [InlineData("TimeSig7", 0xE087)]
        [InlineData("TimeSig8", 0xE088)]
        [InlineData("TimeSig9", 0xE089)]
        [InlineData("NoteheadWhole", 0xE0A2)]
        [InlineData("NoteheadHalf", 0xE0A3)]
        [InlineData("NoteheadBlack", 0xE0A4)]
        [InlineData("NoteheadXWhole", 0xE0A7)]
        [InlineData("NoteheadXHalf", 0xE0A8)]
        [InlineData("NoteheadXBlack", 0xE0A9)]
        [InlineData("NoteheadCircleXWhole", 0xE0B1)]
        [InlineData("NoteheadCircleXHalf", 0xE0B2)]
        [InlineData("NoteheadCircleX", 0xE0B3)]
        [InlineData("Flag8thUp", 0xE240)]
        [InlineData("Flag16thUp", 0xE242)]
        [InlineData("Flag32ndUp", 0xE244)]
        [InlineData("Flag64thUp", 0xE246)]
        [InlineData("RestWhole", 0xE4E3)]
        [InlineData("RestHalf", 0xE4E4)]
        [InlineData("RestQuarter", 0xE4E5)]
        [InlineData("Rest8th", 0xE4E6)]
        [InlineData("Rest16th", 0xE4E7)]
        [InlineData("Rest32nd", 0xE4E8)]
        [InlineData("Rest64th", 0xE4E9)]
        [InlineData("Tuplet3", 0xE883)]
        public void ToChar_ShouldMapGlyphToSmuflCodePoint(string glyphName, int expectedCodePoint)
        {
            var glyph = Enum.Parse<NotationGlyph>(glyphName);

            Assert.Equal(expectedCodePoint, (int)glyph);
            Assert.Equal((char)expectedCodePoint, NotationGlyphs.ToChar(glyph));
        }

        [Fact]
        public void NotationGlyphEnum_ShouldContainExactlyTheBakedGlyphSet()
        {
            // The enum must cover exactly the code points baked into
            // Bravura-Notation.spritefont: no extra members (they would fail
            // SupportsCharacter validation) and no duplicates (distinct SMuFL
            // code points). Read the real asset descriptor instead of just
            // counting enum members, so a drifted table fails loudly.
            var bakedCodePoints = LoadBakedSpriteFontCodePoints();
            var members = Enum.GetValues<NotationGlyph>();
            var enumCodePoints = members.Select(glyph => (int)NotationGlyphs.ToChar(glyph)).ToHashSet();

            Assert.Equal(32, members.Length);
            Assert.Equal(members.Length, enumCodePoints.Count);
            Assert.True(
                enumCodePoints.SetEquals(bakedCodePoints),
                "Enum code points differ from Bravura-Notation.spritefont regions: " +
                $"missing in enum [{string.Join(", ", bakedCodePoints.Except(enumCodePoints).Select(c => $"0x{c:X4}"))}], " +
                $"missing in spritefont [{string.Join(", ", enumCodePoints.Except(bakedCodePoints).Select(c => $"0x{c:X4}"))}]");
        }

        // Expands the spritefont's <CharacterRegion> Start/End ranges into the
        // full set of baked code points.
        private static HashSet<int> LoadBakedSpriteFontCodePoints()
        {
            var path = Path.Combine(RepoPaths.GameContentDir, "Bravura-Notation.spritefont");
            var doc = XDocument.Load(path);
            var codePoints = new HashSet<int>();

            foreach (var region in doc.Descendants("CharacterRegion"))
            {
                int start = (int)region.Element("Start")!.Value[0];
                int end = (int)region.Element("End")!.Value[0];
                for (int code = start; code <= end; code++)
                {
                    codePoints.Add(code);
                }
            }

            return codePoints;
        }

        // ------------------------------------------------------------------
        // Notehead selection: all three NoteheadShape values across the
        // Whole/Half (open heads) and Quarter-or-shorter (black heads) split.
        // ------------------------------------------------------------------

        [Theory]
        [InlineData("Normal", "Whole", "NoteheadWhole")]
        [InlineData("Normal", "Half", "NoteheadHalf")]
        [InlineData("Normal", "Quarter", "NoteheadBlack")]
        [InlineData("Normal", "Eighth", "NoteheadBlack")]
        [InlineData("Normal", "Sixteenth", "NoteheadBlack")]
        [InlineData("Normal", "ThirtySecond", "NoteheadBlack")]
        [InlineData("Normal", "SixtyFourth", "NoteheadBlack")]
        [InlineData("X", "Whole", "NoteheadXWhole")]
        [InlineData("X", "Half", "NoteheadXHalf")]
        [InlineData("X", "Quarter", "NoteheadXBlack")]
        [InlineData("X", "Eighth", "NoteheadXBlack")]
        [InlineData("X", "Sixteenth", "NoteheadXBlack")]
        [InlineData("X", "ThirtySecond", "NoteheadXBlack")]
        [InlineData("X", "SixtyFourth", "NoteheadXBlack")]
        [InlineData("CircledX", "Whole", "NoteheadCircleXWhole")]
        [InlineData("CircledX", "Half", "NoteheadCircleXHalf")]
        [InlineData("CircledX", "Quarter", "NoteheadCircleX")]
        [InlineData("CircledX", "Eighth", "NoteheadCircleX")]
        [InlineData("CircledX", "Sixteenth", "NoteheadCircleX")]
        [InlineData("CircledX", "ThirtySecond", "NoteheadCircleX")]
        [InlineData("CircledX", "SixtyFourth", "NoteheadCircleX")]
        public void Notehead_ShouldPickOpenHeadsForWholeAndHalfAndBlackHeadsForShorter(
            string shapeName, string valueName, string expectedGlyphName)
        {
            var shape = Enum.Parse<NoteheadShape>(shapeName);
            var value = Enum.Parse<NoteValue>(valueName);
            var expected = Enum.Parse<NotationGlyph>(expectedGlyphName);

            Assert.Equal(expected, NotationGlyphs.Notehead(shape, value));
        }

        [Fact]
        public void Notehead_WithUndefinedNoteValue_ShouldThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => NotationGlyphs.Notehead(NoteheadShape.Normal, (NoteValue)999));
        }

        [Fact]
        public void Notehead_WithUndefinedShape_ShouldThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => NotationGlyphs.Notehead((NoteheadShape)999, NoteValue.Quarter));
        }

        // ------------------------------------------------------------------
        // Rest selection.
        // ------------------------------------------------------------------

        [Theory]
        [InlineData("Whole", "RestWhole")]
        [InlineData("Half", "RestHalf")]
        [InlineData("Quarter", "RestQuarter")]
        [InlineData("Eighth", "Rest8th")]
        [InlineData("Sixteenth", "Rest16th")]
        [InlineData("ThirtySecond", "Rest32nd")]
        [InlineData("SixtyFourth", "Rest64th")]
        public void Rest_ShouldMapEveryNoteValueToItsRestGlyph(string valueName, string expectedGlyphName)
        {
            var value = Enum.Parse<NoteValue>(valueName);
            var expected = Enum.Parse<NotationGlyph>(expectedGlyphName);

            Assert.Equal(expected, NotationGlyphs.Rest(value));
        }

        // ------------------------------------------------------------------
        // Stem-up flags: none for quarter and longer, one per shorter value.
        // ------------------------------------------------------------------

        [Theory]
        [InlineData("Eighth", "Flag8thUp")]
        [InlineData("Sixteenth", "Flag16thUp")]
        [InlineData("ThirtySecond", "Flag32ndUp")]
        [InlineData("SixtyFourth", "Flag64thUp")]
        public void Flag_ShouldReturnTheStemUpFlagGlyphForShorterValues(string valueName, string expectedGlyphName)
        {
            var value = Enum.Parse<NoteValue>(valueName);
            var expected = Enum.Parse<NotationGlyph>(expectedGlyphName);

            Assert.Equal(expected, NotationGlyphs.Flag(value));
        }

        [Theory]
        [InlineData("Whole")]
        [InlineData("Half")]
        [InlineData("Quarter")]
        public void Flag_ShouldReturnNullForQuarterAndLonger(string valueName)
        {
            Assert.Null(NotationGlyphs.Flag(Enum.Parse<NoteValue>(valueName)));
        }

        // ------------------------------------------------------------------
        // Duration mapping: binary ladder plus non-binary folded durations.
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(192, "Whole")]
        [InlineData(384, "Whole")] // ≥ boundary: longer than a whole still folds to whole
        [InlineData(96, "Half")]
        [InlineData(48, "Quarter")]
        [InlineData(24, "Eighth")]
        [InlineData(12, "Sixteenth")]
        [InlineData(6, "ThirtySecond")]
        [InlineData(3, "SixtyFourth")]
        [InlineData(5, "SixtyFourth")]  // non-binary folded rest
        [InlineData(7, "ThirtySecond")] // non-binary folded rest
        [InlineData(1, "SixtyFourth")]  // sub-ladder fallback
        [InlineData(2, "SixtyFourth")]  // sub-ladder fallback
        public void FromTicks_ShouldMapDurationsOntoTheValueLadder(int ticks, string expectedValueName)
        {
            Assert.Equal(Enum.Parse<NoteValue>(expectedValueName), NotationGlyphs.FromTicks(ticks));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-192)]
        public void FromTicks_ShouldRejectNonPositiveTicks(int ticks)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => NotationGlyphs.FromTicks(ticks));
        }

        // ------------------------------------------------------------------
        // Tuplet display: the renderer derives the displayed base as
        // SlotTicks * 3 / 2 BEFORE FromTicks (DTXWeb rule), so a 32-tick
        // triplet slot displays as a 48-tick quarter.
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(64, "Half")]        // 64-tick slot → 96 ticks displayed
        [InlineData(32, "Quarter")]     // 32-tick slot → 48 ticks displayed
        [InlineData(16, "Eighth")]      // 16-tick slot → 24 ticks displayed
        [InlineData(8, "Sixteenth")]    // 8-tick slot → 12 ticks displayed
        public void FromTicks_AfterTupletDisplayedBaseConversion_ShouldShowTripletBaseValue(
            int slotTicks, string expectedValueName)
        {
            // SlotTicks only ever comes out of the quantizer as 8/16/32/64
            // (TripletGroupTicks 24/48/96/192 ÷ 3).
            Assert.Equal(Enum.Parse<NoteValue>(expectedValueName), NotationGlyphs.FromTicks(slotTicks * 3 / 2));
        }

        // ------------------------------------------------------------------
        // Pinned Bravura 1.482 metrics: bBox corners and stemUpSE/stemUpNW
        // anchors in staff spaces, loaded from the checked-in excerpt of the
        // official release metadata (TestData/Bravura-1.482.json). An
        // independent source — not a second copy of the production table — so
        // a mistyped constant in NotationGlyphs fails here.
        // ------------------------------------------------------------------

        private static readonly IReadOnlyDictionary<NotationGlyph, NotationGlyphMetrics> PinnedBravura1482Metrics =
            LoadPinnedBravura1482Metrics();

        private static IReadOnlyDictionary<NotationGlyph, NotationGlyphMetrics> LoadPinnedBravura1482Metrics()
        {
            var path = Path.Combine(RepoPaths.TestDataDir, "Bravura-1.482.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var bboxes = root.GetProperty("glyphBBoxes");
            var anchors = root.GetProperty("glyphsWithAnchors");

            var metrics = new Dictionary<NotationGlyph, NotationGlyphMetrics>();
            foreach (var entry in bboxes.EnumerateObject())
            {
                // Enum member names are the SMuFL names with an upper-case first letter.
                var glyph = Enum.Parse<NotationGlyph>(entry.Name, ignoreCase: true);
                GlyphPoint? stemUpSE = null, stemUpNW = null;
                if (anchors.TryGetProperty(entry.Name, out var glyphAnchors))
                {
                    if (glyphAnchors.TryGetProperty("stemUpSE", out var se))
                    {
                        stemUpSE = ToGlyphPoint(se);
                    }
                    if (glyphAnchors.TryGetProperty("stemUpNW", out var nw))
                    {
                        stemUpNW = ToGlyphPoint(nw);
                    }
                }

                metrics[glyph] = new NotationGlyphMetrics(
                    ToGlyphPoint(entry.Value.GetProperty("bBoxSW")),
                    ToGlyphPoint(entry.Value.GetProperty("bBoxNE")),
                    stemUpSE,
                    stemUpNW);
            }

            return metrics;
        }

        private static GlyphPoint ToGlyphPoint(JsonElement point) =>
            new(point[0].GetSingle(), point[1].GetSingle());

        [Fact]
        public void PinnedBravuraMetadata_ShouldCoverEveryGlyphInTheEnum()
        {
            var enumGlyphs = Enum.GetValues<NotationGlyph>().ToHashSet();

            Assert.True(PinnedBravura1482Metrics.Keys.ToHashSet().SetEquals(enumGlyphs),
                "Pinned metadata and NotationGlyph enum have drifted apart.");
        }

        public static IEnumerable<object[]> AllGlyphsForMetrics() =>
            // Iterate the enum, not the dictionary, so a missing pinned entry fails here.
            Enum.GetValues<NotationGlyph>().Select(glyph => new object[] { glyph.ToString() });

        [Theory]
        [MemberData(nameof(AllGlyphsForMetrics))]
        public void GetMetrics_ShouldMatchPinnedBravura1482Metadata(string glyphName)
        {
            var glyph = Enum.Parse<NotationGlyph>(glyphName);

            Assert.Equal(PinnedBravura1482Metrics[glyph], NotationGlyphs.GetMetrics(glyph));
        }

        // ------------------------------------------------------------------
        // IsComplete: every required glyph present vs any single one missing.
        // ------------------------------------------------------------------

        public static IEnumerable<object[]> AllGlyphNames() =>
            Enum.GetValues<NotationGlyph>().Select(glyph => new object[] { glyph.ToString() });

        [Fact]
        public void IsComplete_WithEveryRequiredGlyphPresent_ShouldReturnTrue()
        {
            var baked = new HashSet<char>(Enum.GetValues<NotationGlyph>().Select(NotationGlyphs.ToChar));

            Assert.True(NotationGlyphs.IsComplete(baked.Contains));
        }

        [Theory]
        [MemberData(nameof(AllGlyphNames))]
        public void IsComplete_WithASingleGlyphMissing_ShouldReturnFalse(string glyphName)
        {
            var missingChar = NotationGlyphs.ToChar(Enum.Parse<NotationGlyph>(glyphName));

            Assert.False(NotationGlyphs.IsComplete(c => c != missingChar));
        }

        [Fact]
        public void IsComplete_WithEmptyFont_ShouldReturnFalse()
        {
            Assert.False(NotationGlyphs.IsComplete(_ => false));
        }
    }
}
