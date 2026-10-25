using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Notation;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI.Layout;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

/// <summary>
/// Deterministic, GPU-free engraving tests for the notation strip (HPA-410 Task 2). Expected pixels
/// derive from the pinned Bravura 1.482 metrics at StaffSpace=20 (comments show each derivation).
/// </summary>
public class NotationStripEngravingTests
{
    private const float StaffSpace = 20f;
    private const float StemThickness = 2f;
    private const float BeamThickness = 5f;
    private const float BeamLevelSpacing = 8f;

    private static NotationHead Head(int staffStep, NoteheadShape shape = NoteheadShape.Normal) =>
        new(new DrumStaffPosition(staffStep, shape), Array.Empty<Note>());

    private static NotationEntry NoteEntry(int startTick, int durationTicks, double timeMs, params NotationHead[] heads) =>
        new(startTick, durationTicks, timeMs, heads);

    private static NotationEntry RestEntry(int startTick, int durationTicks, double timeMs = 0) =>
        new(startTick, durationTicks, timeMs, Array.Empty<NotationHead>());

    private static NotationMeasure Measure(params NotationEntry[] entries) =>
        new(0, 192, entries, Array.Empty<NotationTuplet>());

    private static float StaffStepToY(int step) => 510f - step * 10f;

    #region Stems

    [Fact]
    public void EngraveMeasure_LowQuarterChord_ShouldAnchorStemAtLowestHeadStemUpSE()
    {
        // BD = F4 step 1, Normal shape. StemUpSE (1.18, 0.168): dx 23.6, bottom 500 - 3.36 = 496.64;
        // top = step 1+7 = 430; height 66.64.
        var engraving = NotationStripLayout.EngraveMeasure(Measure(NoteEntry(0, 48, 0, Head(1))));

        var glyph = Assert.Single(engraving.Glyphs);
        Assert.Equal(NotationGlyphs.ToChar(NotationGlyph.NoteheadBlack), glyph.Glyph);
        Assert.Equal(0f, glyph.DxPx, 3);
        Assert.Equal(500f, glyph.YPx, 3);

        var stem = Assert.Single(engraving.Rects);
        Assert.Null(stem.EndTimeMs);
        Assert.Equal(1.18f * StaffSpace, stem.StartDxPx, 3);
        Assert.Equal(1.18f * StaffSpace + StemThickness, stem.EndDxPx, 3);
        Assert.Equal(StaffStepToY(8), stem.TopPx, 3);
        Assert.Equal(496.64f - 430f, stem.HeightPx, 3);
    }

    [Fact]
    public void EngraveMeasure_MiddleQuarterChord_ShouldAnchorStemAtXHeadStemUpSE()
    {
        // SN = C5 step 5, Normal shape. Top = step 12 -> 390; bottom 460 - 3.36 = 456.64.
        var engraving = NotationStripLayout.EngraveMeasure(Measure(NoteEntry(0, 48, 0, Head(5))));

        Assert.Equal(NotationGlyphs.ToChar(NotationGlyph.NoteheadBlack), Assert.Single(engraving.Glyphs).Glyph);
        var stem = Assert.Single(engraving.Rects);
        Assert.Equal(1.18f * StaffSpace, stem.StartDxPx, 3);
        Assert.Equal(StaffStepToY(12), stem.TopPx, 3);
        Assert.Equal(456.64f - 390f, stem.HeightPx, 3);
    }

    [Fact]
    public void EngraveMeasure_HighQuarterChord_ShouldAnchorStemAboveStaff()
    {
        // HT = E5 step 7. Top = step 14 -> 370; bottom 440 - 3.36 = 436.64.
        var engraving = NotationStripLayout.EngraveMeasure(Measure(NoteEntry(0, 48, 0, Head(7))));

        var stem = Assert.Single(engraving.Rects);
        Assert.Equal(StaffStepToY(14), stem.TopPx, 3);
        Assert.Equal(436.64f - 370f, stem.HeightPx, 3);
    }

    [Fact]
    public void EngraveMeasure_MixedChord_ShouldShareOneStemFromLowestHead()
    {
        // BD step 1 + SN step 5 + CY step 10 (X): stem x/bottom from the LOWEST head (BD, black
        // StemUpSE), top from the highest step 10+7=17 -> 340. CY head also gets a ledger line.
        var engraving = NotationStripLayout.EngraveMeasure(Measure(NoteEntry(0, 48, 0, Head(1), Head(5), Head(10, NoteheadShape.X))));

        Assert.Equal(3, engraving.Glyphs.Count);
        Assert.Equal(new[] { 500f, 460f, 410f }, engraving.Glyphs.Select(g => g.YPx).ToArray());

        Assert.Equal(2, engraving.Rects.Count);
        var stem = engraving.Rects.Single(r => r.HeightPx > BeamThickness);
        Assert.Equal(1.18f * StaffSpace, stem.StartDxPx, 3);
        Assert.Equal(340f, stem.TopPx, 3);
        Assert.Equal(496.64f - 340f, stem.HeightPx, 3);
    }

    [Fact]
    public void EngraveMeasure_WholeNote_ShouldHaveNoStem()
    {
        var engraving = NotationStripLayout.EngraveMeasure(Measure(NoteEntry(0, 192, 0, Head(5, NoteheadShape.X))));

        var glyph = Assert.Single(engraving.Glyphs);
        Assert.Equal(NotationGlyphs.ToChar(NotationGlyph.NoteheadXWhole), glyph.Glyph);
        Assert.Empty(engraving.Rects);
    }

    #endregion

    #region Rests

    [Theory]
    // Whole hangs below the D5 line (top edge on the line: 450 + 0.036*20 = 450.72); half sits on
    // B4 (bottom edge on the line: 470 - 0.008*20 = 469.84); others anchor their origin on B4.
    [InlineData(192, 450.72f, (char)NotationGlyph.RestWhole)]
    [InlineData(96, 469.84f, (char)NotationGlyph.RestHalf)]
    [InlineData(48, 470f, (char)NotationGlyph.RestQuarter)]
    [InlineData(24, 470f, (char)NotationGlyph.Rest8th)]
    public void EngraveMeasure_RestDurations_ShouldAnchorByMetrics(int durationTicks, float expectedY, char expectedGlyph)
    {
        var engraving = NotationStripLayout.EngraveMeasure(Measure(RestEntry(0, durationTicks)));

        var glyph = Assert.Single(engraving.Glyphs);
        Assert.Equal(expectedGlyph, glyph.Glyph);
        Assert.Equal(expectedY, glyph.YPx, 3);
        Assert.Empty(engraving.Rects);
    }

    [Fact]
    public void EngraveMeasure_EmptyBarWholeRest_ShouldHangFromD5Line()
    {
        // The quantizer emits a single Rest(0, measureTicks) for an empty bar (192 ticks).
        var engraving = NotationStripLayout.EngraveMeasure(Measure(RestEntry(0, 192)));

        var glyph = Assert.Single(engraving.Glyphs);
        Assert.Equal(NotationGlyphs.ToChar(NotationGlyph.RestWhole), glyph.Glyph);
        Assert.Equal(PerformanceUILayout.NotationStrip.D5LineY + NotationGlyphs.GetMetrics(NotationGlyph.RestWhole).BBoxNE.Y * StaffSpace, glyph.YPx, 3);
        Assert.Empty(engraving.Rects);
    }

    #endregion

    #region Ordinary beaming

    [Fact]
    public void EngraveMeasure_TwoEighthsInOneBeat_ShouldDrawOnePrimaryBeam()
    {
        var engraving = NotationStripLayout.EngraveMeasure(Measure(
            NoteEntry(0, 24, 0, Head(5)),
            NoteEntry(24, 24, 120, Head(5))));

        Assert.Equal(2, engraving.Glyphs.Count);
        Assert.Equal(3, engraving.Rects.Count); // 2 stems + 1 primary beam

        var beam = engraving.Rects.Single(r => r.HeightPx == BeamThickness);
        Assert.Equal(0, beam.TimeMs);
        Assert.Equal(120, beam.EndTimeMs);
        Assert.Equal(1.18f * StaffSpace, beam.StartDxPx, 3);
        Assert.Equal(1.18f * StaffSpace + StemThickness, beam.EndDxPx, 3);
        Assert.Equal(StaffStepToY(12), beam.TopPx, 3);
        Assert.Equal(BeamThickness, beam.HeightPx, 3);

        foreach (var stem in engraving.Rects.Where(r => r.HeightPx != BeamThickness))
            Assert.Equal(StaffStepToY(12), stem.TopPx, 3); // stems reach the beam
    }

    [Fact]
    public void EngraveMeasure_FourSixteenths_ShouldDrawPrimaryAndSecondaryBeams()
    {
        var engraving = NotationStripLayout.EngraveMeasure(Measure(
            NoteEntry(0, 12, 0, Head(5)),
            NoteEntry(12, 12, 60, Head(5)),
            NoteEntry(24, 12, 120, Head(5)),
            NoteEntry(36, 12, 180, Head(5))));

        var beams = engraving.Rects.Where(r => r.HeightPx == BeamThickness).ToList();
        Assert.Equal(2, beams.Count);
        Assert.Equal(390f, beams[0].TopPx, 3); // primary
        Assert.Equal(390f + BeamLevelSpacing, beams[1].TopPx, 3); // secondary
        Assert.All(beams, b => Assert.Equal((0, 180), (b.TimeMs, b.EndTimeMs)));
    }

    [Fact]
    public void EngraveMeasure_EighthPlusTwoSixteenths_ShouldDrawPartialSecondaryBeam()
    {
        var engraving = NotationStripLayout.EngraveMeasure(Measure(
            NoteEntry(0, 24, 0, Head(5)),
            NoteEntry(24, 12, 240, Head(5)),
            NoteEntry(36, 12, 360, Head(5))));

        var beams = engraving.Rects.Where(r => r.HeightPx == BeamThickness).ToList();
        Assert.Equal(2, beams.Count);
        Assert.Equal((0, 360, 390f), (beams[0].TimeMs, beams[0].EndTimeMs, beams[0].TopPx));
        // Secondary only spans the two sixteenths.
        Assert.Equal((240, 360, 398f), (beams[1].TimeMs, beams[1].EndTimeMs, beams[1].TopPx));
    }

    [Fact]
    public void EngraveMeasure_SixteenthPlusEighthPlusSixteenth_ShouldDrawTwoStubs()
    {
        var engraving = NotationStripLayout.EngraveMeasure(Measure(
            NoteEntry(0, 12, 0, Head(5)),
            NoteEntry(12, 24, 120, Head(5)),
            NoteEntry(36, 12, 360, Head(5))));

        var beams = engraving.Rects.Where(r => r.HeightPx == BeamThickness).ToList();
        Assert.Equal(3, beams.Count);

        // Primary across the group.
        Assert.Equal((0, 360, 390f), (beams[0].TimeMs, beams[0].EndTimeMs, beams[0].TopPx));
        // Stub on the first sixteenth points right; stub on the last points left.
        Assert.Equal(0, beams[1].TimeMs, 6);
        Assert.Null(beams[1].EndTimeMs);
        Assert.Equal(23.6f, beams[1].StartDxPx, 3);
        Assert.Equal(31.6f, beams[1].EndDxPx, 3);
        Assert.Equal(398f, beams[1].TopPx, 3);
        Assert.Equal(360, beams[2].TimeMs, 6);
        Assert.Null(beams[2].EndTimeMs);
        Assert.Equal(15.6f, beams[2].StartDxPx, 3);
        Assert.Equal(23.6f, beams[2].EndDxPx, 3);
        Assert.Equal(398f, beams[2].TopPx, 3);
    }

    [Theory]
    [InlineData(6, 390f, 398f, 406f)]  // four 32nds: 3 beam levels
    [InlineData(3, 390f, 398f, 406f, 414f)]  // four 64ths: 4 beam levels
    public void EngraveMeasure_ShortNotesInOneBeat_ShouldStackBeamLevels(int durationTicks, params float[] expectedTopYs)
    {
        var entries = new List<NotationEntry>();
        for (int i = 0; i < 4; i++)
            entries.Add(NoteEntry(i * durationTicks, durationTicks, i * durationTicks * 5.0, Head(5)));

        var engraving = NotationStripLayout.EngraveMeasure(new NotationMeasure(0, 192, entries, Array.Empty<NotationTuplet>()));

        var beams = engraving.Rects.Where(r => r.HeightPx == BeamThickness).ToList();
        Assert.Equal(expectedTopYs.Length, beams.Count);
        for (int i = 0; i < expectedTopYs.Length; i++)
            Assert.Equal(expectedTopYs[i], beams[i].TopPx, 3);
    }

    [Fact]
    public void EngraveMeasure_IsolatedShortNote_ShouldGetFlagAtStemTop()
    {
        // 8th SN: stem top 390; Flag8thUp StemUpNW (0, -0.04): origin y = 390 - 0.8 = 389.2.
        var engraving = NotationStripLayout.EngraveMeasure(Measure(NoteEntry(0, 24, 0, Head(5))));

        Assert.Equal(2, engraving.Glyphs.Count);
        Assert.Contains(engraving.Glyphs, g => g.Glyph == NotationGlyphs.ToChar(NotationGlyph.Flag8thUp));
        var flag = engraving.Glyphs.Single(g => g.Glyph == NotationGlyphs.ToChar(NotationGlyph.Flag8thUp));
        Assert.Equal(1.18f * StaffSpace, flag.DxPx, 3);
        Assert.Equal(390f - 0.04f * StaffSpace, flag.YPx, 3);

        Assert.Single(engraving.Rects); // stem only, no beams
    }

    [Fact]
    public void EngraveMeasure_IsolatedSixteenth_ShouldGetSixteenthFlag()
    {
        var engraving = NotationStripLayout.EngraveMeasure(Measure(NoteEntry(0, 12, 0, Head(5))));

        Assert.Contains(engraving.Glyphs, g => g.Glyph == NotationGlyphs.ToChar(NotationGlyph.Flag16thUp));
    }

    [Fact]
    public void EngraveMeasure_RestInsideBeat_ShouldBreakBeamGroup()
    {
        // 16th, 16th rest, 16th, 16th in beat 0: the first note is isolated (flag), the last two beam.
        var engraving = NotationStripLayout.EngraveMeasure(Measure(
            NoteEntry(0, 12, 0, Head(5)),
            RestEntry(12, 12, 60),
            NoteEntry(24, 12, 240, Head(5)),
            NoteEntry(36, 12, 360, Head(5))));

        Assert.Contains(engraving.Glyphs, g => g.Glyph == NotationGlyphs.ToChar(NotationGlyph.Flag16thUp));
        // The two 16ths beam together with primary + secondary; the isolated 16th gets a flag.
        var beams = engraving.Rects.Where(r => r.HeightPx == BeamThickness).ToList();
        Assert.Equal(2, beams.Count);
        Assert.All(beams, b => Assert.Equal((240, 360), (b.TimeMs, b.EndTimeMs)));
        Assert.Equal(390f, beams[0].TopPx, 3);
        Assert.Equal(398f, beams[1].TopPx, 3);
        Assert.Equal(5, engraving.Rects.Count); // 3 stems + 2 beams
    }

    [Fact]
    public void EngraveMeasure_BeatBoundary_ShouldSplitBeamGroups()
    {
        var engraving = NotationStripLayout.EngraveMeasure(Measure(
            NoteEntry(0, 24, 0, Head(5)),
            NoteEntry(24, 24, 120, Head(5)),
            NoteEntry(48, 24, 240, Head(5)),
            NoteEntry(72, 24, 360, Head(5))));

        var beams = engraving.Rects.Where(r => r.HeightPx == BeamThickness).ToList();
        Assert.Equal(2, beams.Count);
        Assert.Equal((0, 120), (beams[0].TimeMs, beams[0].EndTimeMs));
        Assert.Equal((240, 360), (beams[1].TimeMs, beams[1].EndTimeMs));
        Assert.All(beams, b => Assert.Equal(390f, b.TopPx, 3));
    }

    #endregion

    #region Tuplets

    [Fact]
    public void EngraveMeasure_CrossBeatTriplet_ShouldStayWholeWithSingleBeamAndNumeral()
    {
        // Quarter @0, 48-tick triplet group (3 x 16 ticks) starting at tick 24 crossing tick 48,
        // then quarters. SlotTicks*3/2 = 24 -> eighth display: exactly ONE beam level through all
        // three slots (a raw FromTicks(16) would have stacked two).
        var engraving = NotationStripLayout.EngraveMeasure(new NotationMeasure(0, 192,
            new List<NotationEntry>
            {
                NoteEntry(0, 48, 0, Head(5)),
                NoteEntry(24, 16, 240, Head(10, NoteheadShape.X)),
                NoteEntry(40, 16, 400, Head(10, NoteheadShape.X)),
                NoteEntry(56, 16, 560, Head(10, NoteheadShape.X)),
                NoteEntry(72, 48, 720, Head(5)),
                NoteEntry(120, 48, 1200, Head(5)),
                NoteEntry(168, 48, 1680, Head(5)),
            },
            new List<NotationTuplet> { new(1, 3, 16) }));

        // One beam across the full triplet span at its own beam top (CY step 10: top = step 17 -> 340).
        var beams = engraving.Rects.Where(r => r.HeightPx == BeamThickness).ToList();
        var beam = Assert.Single(beams);
        Assert.Equal((240, 560, 340f), (beam.TimeMs, beam.EndTimeMs, beam.TopPx));

        // Stems: 3 triplet stems reach the beam; the 4 quarters keep natural tops (390).
        Assert.Equal(11, engraving.Rects.Count); // 7 stems + 1 beam + 3 ledgers (CY step 10)
        Assert.Equal(3, engraving.Rects.Count(r => r.HeightPx != BeamThickness && r.TopPx == 340f));
        Assert.Equal(4, engraving.Rects.Count(r => r.HeightPx != BeamThickness && r.TopPx == 390f));

        // One numeral 3: bottom edge one staff space above the beam top
        // (y = 340 - 20 + (-0.032*20) = 319.36), centered over the span
        // (x mid 24.2 - half width 11.84 = 12.36, at mid time 400).
        var numeral = engraving.Glyphs.Single(g => g.Glyph == NotationGlyphs.ToChar(NotationGlyph.Tuplet3));
        Assert.Equal(400, numeral.TimeMs, 6);
        Assert.Equal(12.36f, numeral.DxPx, 3);
        Assert.Equal(319.36f, numeral.YPx, 3);
    }

    [Fact]
    public void EngraveMeasure_QuarterTriplet_ShouldDisplaySlotTicksTimesThreeHalvesWithoutBeams()
    {
        // 96-tick triplet group: slots are 32 ticks (raw FromTicks(32) = eighth), but the display
        // base is 32*3/2 = 48 = quarter: stems and numeral only, no beams and no flags.
        var engraving = NotationStripLayout.EngraveMeasure(new NotationMeasure(0, 192,
            new List<NotationEntry>
            {
                NoteEntry(0, 48, 0, Head(5)),
                NoteEntry(48, 32, 480, Head(7)),
                NoteEntry(80, 32, 800, Head(7)),
                NoteEntry(112, 32, 1120, Head(7)),
                NoteEntry(144, 48, 1440, Head(5)),
            },
            new List<NotationTuplet> { new(1, 3, 32) }));

        Assert.DoesNotContain(engraving.Glyphs, g => NotationGlyphs.GetMetrics((NotationGlyph)g.Glyph).StemUpNW is not null); // no flags
        Assert.DoesNotContain(engraving.Rects, r => r.HeightPx == BeamThickness); // no beams or stubs
        Assert.Equal(5, engraving.Rects.Count); // 5 stems

        // Triplet stems keep natural tops (HT step 7 + 7 = 14 -> 370).
        Assert.Equal(3, engraving.Rects.Count(r => r.TopPx == 370f));

        var numeral = engraving.Glyphs.Single(g => g.Glyph == NotationGlyphs.ToChar(NotationGlyph.Tuplet3));
        Assert.Equal(800, numeral.TimeMs, 6);
        Assert.Equal(370f - 20f - 0.64f, numeral.YPx, 3);
    }

    [Fact]
    public void EngraveMeasure_TripletWithRestSlot_ShouldBeamOnlyConsecutiveNotes()
    {
        var engraving = NotationStripLayout.EngraveMeasure(new NotationMeasure(0, 192,
            new List<NotationEntry>
            {
                NoteEntry(24, 16, 240, Head(5)),
                RestEntry(40, 16, 400),
                NoteEntry(56, 16, 560, Head(5)),
            },
            new List<NotationTuplet> { new(0, 3, 16) }));

        var beam = Assert.Single(engraving.Rects, r => r.HeightPx == BeamThickness);
        Assert.Equal((240, 560), (beam.TimeMs, beam.EndTimeMs));
        // Numeral still drawn once for the group.
        Assert.Single(engraving.Glyphs, g => g.Glyph == NotationGlyphs.ToChar(NotationGlyph.Tuplet3));
    }

    #endregion

    #region Ledger lines

    [Theory]
    // CY (step 10) and LC (step 11) take one ledger at step 10; step 12 takes ledgers at 10 and 12.
    [InlineData(10, new[] { 10 })]
    [InlineData(11, new[] { 10 })]
    [InlineData(12, new[] { 10, 12 })]
    public void EngraveMeasure_HeadsAboveStaff_ShouldAddLedgerLinesAtEvenSteps(int staffStep, int[] expectedSteps)
    {
        var engraving = NotationStripLayout.EngraveMeasure(Measure(NoteEntry(0, 48, 0, Head(staffStep, NoteheadShape.X))));

        var ledgers = engraving.Rects.Where(r => r.HeightPx == PerformanceUILayout.NotationStrip.StaffLineThickness).ToList();
        Assert.Equal(expectedSteps.Length, ledgers.Count);
        for (int i = 0; i < expectedSteps.Length; i++)
        {
            Assert.Equal(StaffStepToY(expectedSteps[i]), ledgers[i].TopPx, 3);
            // Centered on the head (X black width 1.16 -> center 11.6), 34px wide, 1px thick.
            Assert.Equal(11.6f - 17f, ledgers[i].StartDxPx, 3);
            Assert.Equal(11.6f + 17f, ledgers[i].EndDxPx, 3);
        }
    }

    [Fact]
    public void EngraveMeasure_HeadBelowStaff_ShouldAddLedgerLineAtEvenStep()
    {
        var engraving = NotationStripLayout.EngraveMeasure(Measure(NoteEntry(0, 48, 0, Head(-3))));

        var ledger = Assert.Single(engraving.Rects, r => r.HeightPx == PerformanceUILayout.NotationStrip.StaffLineThickness);
        Assert.Equal(StaffStepToY(-2), ledger.TopPx, 3);
        Assert.Equal(11.8f - 17f, ledger.StartDxPx, 3);
        Assert.Equal(11.8f + 17f, ledger.EndDxPx, 3);
    }

    [Fact]
    public void EngraveMeasure_PedalHiHatBelowStaff_ShouldNeedNoLedgerLine()
    {
        // LP = D4 step -1 hangs from the bottom line without a ledger.
        var engraving = NotationStripLayout.EngraveMeasure(Measure(NoteEntry(0, 48, 0, Head(-1, NoteheadShape.X))));

        Assert.DoesNotContain(engraving.Rects, r => r.HeightPx == PerformanceUILayout.NotationStrip.StaffLineThickness);
    }

    #endregion

    #region Key column labels

    [Fact]
    public void GetKeyColumnLabels_AllTenLanes_ShouldUseDrumStaffMappingHeights()
    {
        var labels = NotationStripLayout.GetKeyColumnLabels();

        var expected = new[]
        {
            ("LC", 400f), ("CY", 410f), ("HH", 420f), ("RD", 430f), ("HT", 440f),
            ("LT", 450f), ("SN", 460f), ("FT", 480f), ("BD", 500f), ("LP", 520f),
        };
        Assert.Equal(expected.Select(e => e.Item1), labels.Select(l => l.Text));
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i].Item2, labels[i].YPx, 3);
    }

    [Fact]
    public void GetKeyColumnLabels_AllTenLanes_ShouldMapOneToOneOntoLanes0Through9()
    {
        var labels = NotationStripLayout.GetKeyColumnLabels();

        // 0–9 bijection: every lane has exactly one primary label and vice versa.
        Assert.Equal(10, labels.Count);
        Assert.Equal(Enumerable.Range(0, 10), labels.Select(l => l.Lane).OrderBy(l => l));
    }

    [Theory]
    [InlineData("LC", 0)]
    [InlineData("HH", 1)]
    [InlineData("LP", 2)]
    [InlineData("SN", 3)]
    [InlineData("HT", 4)]
    [InlineData("BD", 5)]
    [InlineData("LT", 6)]
    [InlineData("FT", 7)]
    [InlineData("CY", 8)]
    [InlineData("RD", 9)]
    public void GetKeyColumnLabels_EachInstrument_ShouldCarryItsCanonicalLane(string text, int expectedLane)
    {
        var labels = NotationStripLayout.GetKeyColumnLabels();

        var anchor = labels.Single(l => l.Text == text);
        Assert.Equal(expectedLane, anchor.Lane);
    }

    [Fact]
    public void TryGetLane_LeftBassChannel_ShouldResolveToLpLane2()
    {
        // 0x1C left bass is engraved at the BD staff position but is lane 2 input:
        // its flash must light the LP label, never the engraved BD line.
        Assert.True(DTXChartParser.TryGetLane(0x1C, out int lane));
        Assert.Equal(2, lane);

        // Same lane as the LP primary channel's anchor identity.
        var lp = NotationStripLayout.GetKeyColumnLabels().Single(l => l.Text == "LP");
        Assert.Equal(lp.Lane, lane);
    }

    [Theory]
    [InlineData(0x11, 1)] // hi-hat close
    [InlineData(0x12, 3)] // snare
    [InlineData(0x13, 5)] // bass drum
    [InlineData(0x14, 4)] // high tom
    [InlineData(0x15, 6)] // low tom
    [InlineData(0x16, 8)] // crash
    [InlineData(0x17, 7)] // floor tom
    [InlineData(0x18, 1)] // hi-hat open (HH variant)
    [InlineData(0x19, 9)] // ride
    [InlineData(0x1A, 0)] // left crash
    [InlineData(0x1B, 2)] // left pedal
    [InlineData(0x1C, 2)] // left bass (LP lane)
    public void TryGetLane_KnownChannels_ShouldReturnParserLane(int channel, int expectedLane)
    {
        Assert.True(DTXChartParser.TryGetLane(channel, out int lane));
        Assert.Equal(expectedLane, lane);
    }

    [Theory]
    [InlineData(0x10)]
    [InlineData(0x20)]
    [InlineData(0x99)]
    public void TryGetLane_UnknownChannels_ShouldReturnFalse(int channel)
    {
        Assert.False(DTXChartParser.TryGetLane(channel, out int lane));
        Assert.Equal(0, lane);
    }

    #endregion
}
