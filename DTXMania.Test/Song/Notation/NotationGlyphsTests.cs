using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Song.Notation;
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
    /// the HPA-409 spike; see .superpowers/sdd/2026-10-19-hpa-409-bravura-
    /// notation-font/spike/ for the recorded specimen and measurements. It is
    /// deliberately not a permanent test: the Mac suite excludes GPU tests.
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
            // The 32 code points above must be exactly what the SpriteFont bakes:
            // no extra members (they would fail SupportsCharacter validation) and
            // no duplicates (distinct SMuFL code points).
            var members = Enum.GetValues<NotationGlyph>();
            Assert.Equal(32, members.Length);
            Assert.Equal(32, new HashSet<char>(members.Select(NotationGlyphs.ToChar)).Count);
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
            Assert.Equal(Enum.Parse<NoteValue>(expectedValueName), NoteValue.FromTicks(ticks));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-192)]
        public void FromTicks_ShouldRejectNonPositiveTicks(int ticks)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => NoteValue.FromTicks(ticks));
        }

        // ------------------------------------------------------------------
        // Tuplet display: the renderer derives the displayed base as
        // SlotTicks * 3 / 2 BEFORE FromTicks (DTXWeb rule), so a 48-tick
        // triplet slot displays as a 72-tick quarter.
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(96, "Half")]    // half triplet slot → 144 ticks
        [InlineData(48, "Quarter")] // quarter triplet slot → 72 ticks
        [InlineData(24, "Eighth")]  // eighth triplet slot → 36 ticks
        public void FromTicks_AfterTupletDisplayedBaseConversion_ShouldShowTripletBaseValue(
            int slotTicks, string expectedValueName)
        {
            Assert.Equal(Enum.Parse<NoteValue>(expectedValueName), NoteValue.FromTicks(slotTicks * 3 / 2));
        }

        // ------------------------------------------------------------------
        // Pinned Bravura 1.482 metrics (redist/Bravura.json): bBox width and
        // SW origin in staff spaces, plus stemUpSE/stemUpNW anchors. Constants
        // below are copied from the pinned release, independently of the
        // implementation's own table.
        // ------------------------------------------------------------------

        private static readonly IReadOnlyDictionary<string, NotationGlyphMetrics> PinnedBravura1482Metrics =
            new Dictionary<string, NotationGlyphMetrics>
            {
                ["UnpitchedPercussionClef1"] = new(1.528f, new GlyphPoint(0f, -1f), null, null),
                ["TimeSig0"] = new(1.72f, new GlyphPoint(0.08f, -1f), null, null),
                ["TimeSig1"] = new(1.176f, new GlyphPoint(0.08f, -1f), null, null),
                ["TimeSig2"] = new(1.624f, new GlyphPoint(0.08f, -1.028f), null, null),
                ["TimeSig3"] = new(1.524f, new GlyphPoint(0.08f, -1.004f), null, null),
                ["TimeSig4"] = new(1.72f, new GlyphPoint(0.08f, -1f), null, null),
                ["TimeSig5"] = new(1.452f, new GlyphPoint(0.08f, -1.004f), null, null),
                ["TimeSig6"] = new(1.576f, new GlyphPoint(0.08f, -0.996f), null, null),
                ["TimeSig7"] = new(1.604f, new GlyphPoint(0.08f, -1f), null, null),
                ["TimeSig8"] = new(1.584f, new GlyphPoint(0.08f, -1.036f), null, null),
                ["TimeSig9"] = new(1.576f, new GlyphPoint(0.08f, -0.996f), null, null),
                ["NoteheadWhole"] = new(1.688f, new GlyphPoint(0f, -0.5f), null, null),
                ["NoteheadHalf"] = new(1.18f, new GlyphPoint(0f, -0.5f), new GlyphPoint(1.18f, 0.168f), null),
                ["NoteheadBlack"] = new(1.18f, new GlyphPoint(0f, -0.5f), new GlyphPoint(1.18f, 0.168f), null),
                ["NoteheadXWhole"] = new(1.508f, new GlyphPoint(0f, -0.5f), null, null),
                ["NoteheadXHalf"] = new(1.336f, new GlyphPoint(0f, -0.5f), new GlyphPoint(1.336f, 0.412f), null),
                ["NoteheadXBlack"] = new(1.16f, new GlyphPoint(0f, -0.5f), new GlyphPoint(1.16f, 0.444f), null),
                ["NoteheadCircleXWhole"] = new(0.996f, new GlyphPoint(0f, -0.5f), null, null),
                ["NoteheadCircleXHalf"] = new(1f, new GlyphPoint(0f, -0.5f), new GlyphPoint(1f, 0f), null),
                ["NoteheadCircleX"] = new(0.996f, new GlyphPoint(0f, -0.5f), new GlyphPoint(0.996f, 0f), null),
                ["Flag8thUp"] = new(1.056f, new GlyphPoint(0f, -3.24f), null, new GlyphPoint(0f, -0.04f)),
                ["Flag16thUp"] = new(1.116f, new GlyphPoint(0f, -3.252f), null, new GlyphPoint(0f, -0.088f)),
                ["Flag32ndUp"] = new(1.044f, new GlyphPoint(0f, -3.248f), null, new GlyphPoint(0f, 0.376f)),
                ["Flag64thUp"] = new(1.044f, new GlyphPoint(0f, -3.248f), null, new GlyphPoint(0f, 1.172f)),
                ["RestWhole"] = new(1.128f, new GlyphPoint(0f, -0.54f), null, null),
                ["RestHalf"] = new(1.128f, new GlyphPoint(0f, -0.008f), null, null),
                ["RestQuarter"] = new(1.076f, new GlyphPoint(0.004f, -1.5f), null, null),
                ["Rest8th"] = new(0.988f, new GlyphPoint(0f, -1.004f), null, null),
                ["Rest16th"] = new(1.28f, new GlyphPoint(0f, -2f), null, null),
                ["Rest32nd"] = new(1.452f, new GlyphPoint(0f, -2f), null, null),
                ["Rest64th"] = new(1.692f, new GlyphPoint(0f, -3.012f), null, null),
                ["Tuplet3"] = new(1.184f, new GlyphPoint(0.04f, -0.032f), null, null)
            };

        public static IEnumerable<object[]> PinnedMetricGlyphNames() =>
            PinnedBravura1482Metrics.Keys.Select(name => new object[] { name });

        [Theory]
        [MemberData(nameof(PinnedMetricGlyphNames))]
        public void GetMetrics_ShouldMatchPinnedBravura1482Metadata(string glyphName)
        {
            var glyph = Enum.Parse<NotationGlyph>(glyphName);

            Assert.Equal(PinnedBravura1482Metrics[glyphName], NotationGlyphs.GetMetrics(glyph));
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
