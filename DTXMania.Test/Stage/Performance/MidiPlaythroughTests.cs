using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Test.Helpers;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

/// <summary>
/// MIDI automation: a scripted drummer plays parsed DTX charts on an NX-style e-drum kit
/// through the real MIDI input path, and each test checks what the player would see on the
/// result screen.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MidiPlaythroughTests : IDisposable
{
    // 120 BPM: a measure is 2000 ms, an 8-slot row is 250 ms per slot.
    private const string GrooveChart = """
        #TITLE: MIDI drummer groove
        #BPM: 120
        ; Measure 1: 8th-note groove, crash chord on the downbeat, open hi-hat at the end.
        #00116: 0100000000000000
        #00113: 0100000101000000
        #00111: 0001010101010100
        #00118: 0000000000000001
        #00112: 0000010000000100
        ; Measure 2: ride with hi-hat foot, then a 16th-note snare double into the toms.
        #00219: 0100010001000000
        #0021B: 0001000100010000
        #00213: 0100000001000000
        #00212: 00000000000000000000000001010000
        #00214: 00000000000000000000000000000100
        #00215: 00000000000000000000000000000001
        ; Measure 3: closing chord.
        #0031A: 01
        #00313: 01
        #00317: 01
        """;

    private const int GrooveNoteCount = 28;
    private const int GrooveRideNoteCount = 3;

    // Snare quarter notes, 500 ms apart: sparse enough that any offset judges its own note.
    private const string QuarterSnareChart = """
        #TITLE: MIDI drummer quarter snare
        #BPM: 120
        #00112: 01010101
        #00212: 01010101
        """;

    private const int QuarterSnareNoteCount = 8;

    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "dtxmaniacx-midi-playthrough-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Groove_PlayedOnTimeWithSharedCymbals_ShouldBeAllPerfectFullCombo(bool sendNoteOff)
    {
        using var drummer = new MidiDrummer(await ParseAsync(GrooveChart), config => config.ShareCymbalLanes = true);

        var result = drummer.PlayChart().Run(sendNoteOff);

        Assert.Equal(GrooveNoteCount, result.TotalNotes);
        Assert.Equal(GrooveNoteCount, result.StatusPerfectCount);
        Assert.Equal(0, result.StatusMissCount);
        Assert.Equal(GrooveNoteCount, result.MaxCombo);
        Assert.False(result.HasFailed);
    }

    [Fact]
    public async Task Groove_WithSeparateCymbals_ShouldMissOnlyRideNotesPlayedOnTheRideBow()
    {
        using var drummer = new MidiDrummer(await ParseAsync(GrooveChart));

        var result = drummer.PlayChart().Run();

        var rideNotes = drummer.Chart.Notes.Where(note => note.Channel == 0x19).ToList();
        Assert.Equal(GrooveRideNoteCount, rideNotes.Count);
        Assert.All(rideNotes, note => Assert.Equal(JudgementType.Miss, drummer.JudgementOf(note)!.Type));
        Assert.Equal(GrooveNoteCount - GrooveRideNoteCount, result.StatusPerfectCount);
        Assert.Equal(GrooveRideNoteCount, result.StatusMissCount);
    }

    [Fact]
    public async Task Groove_RideBellAndEdge_ShouldHitRideNotesWithSeparateCymbals()
    {
        using var drummer = new MidiDrummer(await ParseAsync(GrooveChart));
        var bell = true;

        var result = drummer
            .PlayChart(pickPad: note => note.Channel == 0x19 ? ((bell = !bell) ? 53 : 59) : MidiDrummer.KitPadFor(note))
            .Run();

        Assert.Equal(GrooveNoteCount, result.StatusPerfectCount);
        Assert.Equal(0, result.StatusMissCount);
    }

    [Theory]
    [InlineData(0, JudgementType.Perfect, QuarterSnareNoteCount)]
    [InlineData(30, JudgementType.Great, QuarterSnareNoteCount)]
    [InlineData(-45, JudgementType.Great, QuarterSnareNoteCount)]
    [InlineData(60, JudgementType.Good, QuarterSnareNoteCount)]
    [InlineData(-90, JudgementType.Good, QuarterSnareNoteCount)]
    [InlineData(110, JudgementType.Poor, 0)]
    [InlineData(-140, JudgementType.Poor, 0)]
    public async Task QuarterSnare_PlayedOffBeat_ShouldJudgeEveryNoteInThatWindow(
        int offsetMs, JudgementType expected, int expectedMaxCombo)
    {
        // Strikes land 0 to one frame late, so each offset sits inside its window either way.
        using var drummer = new MidiDrummer(await ParseAsync(QuarterSnareChart));

        var result = drummer.PlayChart(offsetMs).Run();

        Assert.All(drummer.Chart.Notes, note => Assert.Equal(expected, drummer.JudgementOf(note)!.Type));
        Assert.Equal(0, result.StatusMissCount);
        // Poor is a hit but breaks the combo.
        Assert.Equal(expectedMaxCombo, result.MaxCombo);
    }

    [Theory]
    [InlineData(-170)]
    [InlineData(170)]
    public async Task QuarterSnare_StrayHitOutsidePoorWindow_ShouldNotUseUpTheNote(int strayOffsetMs)
    {
        using var drummer = new MidiDrummer(await ParseAsync(QuarterSnareChart));
        foreach (var note in drummer.Chart.Notes)
            drummer.Strike(note.TimeMs + strayOffsetMs, pad: 38);

        var result = drummer.PlayChart().Run();

        Assert.Equal(QuarterSnareNoteCount, result.StatusPerfectCount);
        Assert.Equal(0, result.StatusMissCount);
    }

    [Theory]
    [InlineData(-170)]
    [InlineData(170)]
    public async Task QuarterSnare_OnlyHitOutsidePoorWindow_ShouldMissAtTimeoutNotOnTheHit(int offsetMs)
    {
        using var drummer = new MidiDrummer(await ParseAsync(QuarterSnareChart));
        var emptyHits = 0;
        drummer.Rules.JudgementManager!.PlayerHitResolved += (_, hit) =>
        {
            Assert.Null(hit.JudgedNote);
            emptyHits++;
        };

        var result = drummer.PlayChart(offsetMs).Run();

        Assert.Equal(QuarterSnareNoteCount, emptyHits);
        Assert.Equal(QuarterSnareNoteCount, result.StatusMissCount);
        // A hit that used the note up would judge it at the hit, about offsetMs away.
        Assert.All(drummer.Chart.Notes, note =>
            Assert.True(drummer.JudgementOf(note)!.DeltaMs > JudgementManager.MissTimeoutMs));
    }

    [Theory]
    [InlineData(0, 0, QuarterSnareNoteCount, 0)]
    [InlineData(200, 200, QuarterSnareNoteCount, 0)]
    [InlineData(200, 0, 0, QuarterSnareNoteCount)]
    public async Task QuarterSnare_AudioLatencyOffset_ShouldExpectHitsThatMuchLate(
        int latencyOffsetMs, int hitLateMs, int expectedPerfect, int expectedMiss)
    {
        using var drummer = new MidiDrummer(
            await ParseAsync(QuarterSnareChart), config => config.AudioLatencyOffsetMs = latencyOffsetMs);

        var result = drummer.PlayChart(hitLateMs).Run();

        Assert.Equal(expectedPerfect, result.StatusPerfectCount);
        Assert.Equal(expectedMiss, result.StatusMissCount);
    }

    [Theory]
    [InlineData(38, 15, false)]
    [InlineData(38, 20, false)]
    [InlineData(38, 21, true)]
    [InlineData(40, 100, true)]
    [InlineData(37, 100, true)]
    [InlineData(39, 100, false)]
    public async Task QuarterSnare_PadAndVelocity_ShouldOnlyJudgeBoundPadsAboveThreshold(
        int pad, int velocity, bool judged)
    {
        using var drummer = new MidiDrummer(
            await ParseAsync(QuarterSnareChart), config => config.MidiVelocityThresholds[38] = 20);

        var result = drummer.PlayChart(pickPad: _ => pad, velocity: velocity).Run();

        Assert.Equal(judged ? QuarterSnareNoteCount : 0, result.StatusPerfectCount);
        Assert.Equal(judged ? 0 : QuarterSnareNoteCount, result.StatusMissCount);
    }

    [Fact]
    public async Task Groove_FrameHitchDuringChord_ShouldStillJudgeEveryStrike()
    {
        using var drummer = new MidiDrummer(await ParseAsync(GrooveChart), config => config.ShareCymbalLanes = true);
        var chord = drummer.Chart.Notes.Where(note => note.TimeMs == 3000.0).ToList();
        Assert.Equal(2, chord.Count);

        // A 100 ms freeze swallows the BD + HH chord frame; both strikes wait in the MIDI queue.
        var result = drummer.PlayChart().Stall(2950, 3050).Run();

        Assert.Equal(GrooveNoteCount, result.StatusPerfectCount + result.StatusGreatCount
            + result.StatusGoodCount + result.StatusPoorCount);
        Assert.Equal(0, result.StatusMissCount);
        Assert.All(chord, note => Assert.True(drummer.JudgementOf(note)!.DeltaMs > 0));
    }

    private async Task<ParsedChart> ParseAsync(string chartText)
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".dtx");
        await File.WriteAllTextAsync(path, chartText.Replace("\r\n", "\n"));
        return await DTXChartParser.ParseAsync(path);
    }
}
