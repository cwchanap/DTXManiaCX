using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Song.Components;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Tests for ParsedChart methods and statistics
    /// </summary>
    [Trait("Category", "Unit")]
    public class ParsedChartTests
    {
        #region Constructor Tests

        [Fact]
        public void DefaultConstructor_ShouldInitializeWithDefaults()
        {
            var chart = new ParsedChart();
            Assert.Equal(120.0, chart.Bpm);
            Assert.Equal("", chart.BackgroundAudioPath);
            Assert.Equal("", chart.BackgroundWavId);
            Assert.Equal("", chart.FilePath);
            Assert.Equal(0.0, chart.DurationMs);
            Assert.Empty(chart.Notes);
            Assert.Empty(chart.BGMEvents);
            Assert.Empty(chart.VideoEvents);
            Assert.Equal(0, chart.TotalNotes);
        }

        [Fact]
        public void FilePathConstructor_ShouldSetFilePath()
        {
            var chart = new ParsedChart("/test/song.dtx");
            Assert.Equal("/test/song.dtx", chart.FilePath);
        }

        [Fact]
        public void DefaultConstructor_ShouldInitialize10LaneSlots()
        {
            var chart = new ParsedChart();
            Assert.Equal(10, chart.NotesPerLane.Count);
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal(0, chart.NotesPerLane[i]);
            }
        }

        #endregion

        #region WavDefinitions Tests

        [Fact]
        public void WavDefinitions_ShouldDefaultToEmpty()
        {
            var chart = new ParsedChart();
            Assert.NotNull(chart.WavDefinitions);
            Assert.Empty(chart.WavDefinitions);
        }

        [Fact]
        public void SetWavDefinitions_ShouldStoreFrozenCopy()
        {
            var chart = new ParsedChart();
            var input = new Dictionary<string, string> { ["01"] = "/path/snare.wav" };

            chart.SetWavDefinitions(input);
            input["02"] = "/path/extra.wav"; // Mutate after set — must not affect chart

            Assert.Single(chart.WavDefinitions);
            Assert.Equal("/path/snare.wav", chart.WavDefinitions["01"]);
        }

        [Fact]
        public void SetWavDefinitions_WithNullInput_ShouldClearToEmpty()
        {
            var chart = new ParsedChart();
            chart.SetWavDefinitions(new Dictionary<string, string> { ["01"] = "x" });
            chart.SetWavDefinitions(null);
            Assert.Empty(chart.WavDefinitions);
        }

        #endregion

        #region WavVolumes / WavPans Tests

        [Fact]
        public void WavVolumesAndPans_ShouldDefaultToEmpty()
        {
            var chart = new ParsedChart();
            Assert.NotNull(chart.WavVolumes);
            Assert.Empty(chart.WavVolumes);
            Assert.NotNull(chart.WavPans);
            Assert.Empty(chart.WavPans);
        }

        [Fact]
        public void GetVolume_WithUndefinedId_ShouldReturnFullVolume()
        {
            var chart = new ParsedChart();
            Assert.Equal(1.0f, chart.GetVolume("01"));
            Assert.Equal(1.0f, chart.GetVolume(null!));
            Assert.Equal(1.0f, chart.GetVolume(""));
        }

        [Fact]
        public void GetPan_WithUndefinedId_ShouldReturnCentered()
        {
            var chart = new ParsedChart();
            Assert.Equal(0.0f, chart.GetPan("01"));
            Assert.Equal(0.0f, chart.GetPan(null!));
            Assert.Equal(0.0f, chart.GetPan(""));
        }

        [Fact]
        public void GetVolume_ShouldNormalizeDtxScaleToZeroToOne()
        {
            var chart = new ParsedChart();
            chart.SetWavVolumes(new Dictionary<string, int> { ["01"] = 50, ["02"] = 100, ["03"] = 0 });

            Assert.Equal(0.5f, chart.GetVolume("01"));
            Assert.Equal(1.0f, chart.GetVolume("02"));
            Assert.Equal(0.0f, chart.GetVolume("03"));
        }

        [Fact]
        public void GetPan_ShouldNormalizeDtxScaleToMinusOneToOne()
        {
            var chart = new ParsedChart();
            chart.SetWavPans(new Dictionary<string, int> { ["01"] = -100, ["02"] = 0, ["03"] = 100, ["04"] = 50 });

            Assert.Equal(-1.0f, chart.GetPan("01"));
            Assert.Equal(0.0f, chart.GetPan("02"));
            Assert.Equal(1.0f, chart.GetPan("03"));
            Assert.Equal(0.5f, chart.GetPan("04"));
        }

        [Fact]
        public void SetWavVolumes_ShouldStoreFrozenCopy()
        {
            var chart = new ParsedChart();
            var input = new Dictionary<string, int> { ["01"] = 80 };

            chart.SetWavVolumes(input);
            input["02"] = 40; // Mutate after set — must not affect chart

            Assert.Single(chart.WavVolumes);
            Assert.Equal(80, chart.WavVolumes["01"]);
        }

        [Fact]
        public void SetWavVolumes_WithNullInput_ShouldClearToEmpty()
        {
            var chart = new ParsedChart();
            chart.SetWavVolumes(new Dictionary<string, int> { ["01"] = 60 });
            chart.SetWavVolumes(null);
            Assert.Empty(chart.WavVolumes);
        }

        [Fact]
        public void SetWavPans_WithNullInput_ShouldClearToEmpty()
        {
            var chart = new ParsedChart();
            chart.SetWavPans(new Dictionary<string, int> { ["01"] = 50 });
            chart.SetWavPans(null);
            Assert.Empty(chart.WavPans);
        }

        #endregion

        #region AddNote Tests

        [Fact]
        public void AddNote_NullNote_ShouldNotThrow()
        {
            var chart = new ParsedChart();
            chart.AddNote(null); // Should not throw
            Assert.Equal(0, chart.TotalNotes);
        }

        [Fact]
        public void AddNote_SingleNote_ShouldIncreaseNoteCount()
        {
            var chart = new ParsedChart();
            var note = new Note(laneIndex: 3, bar: 0, tick: 0, channel: 0x14, value: "01");

            chart.AddNote(note);

            Assert.Equal(1, chart.TotalNotes);
            Assert.Equal(1, chart.NotesPerLane[3]);
        }

        [Fact]
        public void AddNote_ShouldLeaveDurationUntilFinalize()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            var note = new Note(3, 1, 0, 0x14, "01");

            chart.AddNote(note);

            Assert.Equal(0.0, chart.DurationMs);

            chart.FinalizeChart();

            Assert.Equal(2500.0, chart.DurationMs, precision: 3);
        }

        [Fact]
        public void AddNote_MultipleNotes_ShouldTrackMaxDuration()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            chart.AddNote(new Note(0, 0, 96, 0x1A, "01"));
            chart.AddNote(new Note(1, 1, 0, 0x18, "01"));
            chart.AddNote(new Note(2, 2, 0, 0x1B, "01"));

            chart.FinalizeChart();

            Assert.Equal(4500.0, chart.DurationMs);
            Assert.Equal(3, chart.TotalNotes);
        }

        [Fact]
        public void AddNote_OutOfRangeLane_ShouldNotUpdateLaneStats()
        {
            var chart = new ParsedChart();
            var note = new Note(-1, 0, 0, 0, "01");

            chart.AddNote(note);

            // Note is added but no lane stat is updated (lane index is out of range)
            Assert.Equal(1, chart.TotalNotes);
        }

        [Fact]
        public void AddNote_WithZeroTimeMs_ShouldResolveOnFinalize()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            var note = new Note(3, 1, 0, 0x14, "01") { TimeMs = 0.0 };

            chart.AddNote(note);
            Assert.Equal(0.0, note.TimeMs);

            chart.FinalizeChart();

            Assert.Equal(2000.0, note.TimeMs, precision: 3);
        }

        [Fact]
        public void AddNote_ShouldDeferTimeAndDurationUntilFinalize()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            var note = new Note(3, 1, 0, 0x12, "01");

            chart.AddNote(note);

            Assert.Equal(0.0, note.TimeMs);
            Assert.Equal(0.0, chart.DurationMs);
        }

        #endregion

        #region AddBGMEvent Tests

        [Fact]
        public void AddBGMEvent_NullEvent_ShouldNotThrow()
        {
            var chart = new ParsedChart();
            chart.AddBGMEvent(null); // Should not throw
            Assert.Empty(chart.BGMEvents);
        }

        [Fact]
        public void AddBGMEvent_ValidEvent_ShouldAddToList()
        {
            var chart = new ParsedChart();
            var bgmEvent = new BGMEvent(0, 0, "01") { TimeMs = 0.0 };

            chart.AddBGMEvent(bgmEvent);

            Assert.Single(chart.BGMEvents);
        }

        [Fact]
        public void AddBGMEvent_ShouldLeaveDurationUntilFinalize()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            var bgmEvent = new BGMEvent(5, 0, "01");

            chart.AddBGMEvent(bgmEvent);

            Assert.Equal(0.0, chart.DurationMs);

            chart.FinalizeChart();

            Assert.Equal(10500.0, chart.DurationMs, precision: 3);
        }

        [Fact]
        public void AddBGMEvent_WithZeroTimeMs_ShouldResolveOnFinalize()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            var bgmEvent = new BGMEvent(bar: 2, tick: 0, wavId: "01") { TimeMs = 0.0 };

            chart.AddBGMEvent(bgmEvent);
            Assert.Equal(0.0, bgmEvent.TimeMs);

            chart.FinalizeChart();

            Assert.Equal(4000.0, bgmEvent.TimeMs, precision: 3);
        }

        [Fact]
        public void AddBGMEvent_ShouldDeferTimeAndDurationUntilFinalize()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            var bgm = new BGMEvent(1, 0, "01");

            chart.AddBGMEvent(bgm);

            Assert.Equal(0.0, bgm.TimeMs);
            Assert.Equal(0.0, chart.DurationMs);
        }

        #endregion

        #region AddVideoEvent Tests

        [Fact]
        public void AddVideoEvent_NullEvent_ShouldNotThrow()
        {
            var chart = new ParsedChart();
            chart.AddVideoEvent(null); // Should not throw
            Assert.Empty(chart.VideoEvents);
        }

        [Fact]
        public void AddVideoEvent_ValidEvent_ShouldAddToList()
        {
            var chart = new ParsedChart();
            var videoEvent = new ChartVideoEvent(0, 0, "01");

            chart.AddVideoEvent(videoEvent);

            Assert.Single(chart.VideoEvents);
        }

        [Fact]
        public void AddVideoEvent_ShouldDeferTimeAndDurationUntilFinalize()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            var videoEvent = new ChartVideoEvent(1, 0, "01");

            chart.AddVideoEvent(videoEvent);

            Assert.Equal(0.0, videoEvent.TimeMs);
            Assert.Equal(0.0, chart.DurationMs);
        }

        #endregion

        #region FinalizeChart Tests

        [Fact]
        public void FinalizeChart_WithNotes_ShouldAddDurationBuffer()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            chart.AddNote(new Note(3, 1, 0, 0x14, "01"));

            chart.FinalizeChart();

            // Duration should have 500ms buffer added
            Assert.Equal(2500.0, chart.DurationMs, precision: 0);
        }

        [Fact]
        public void FinalizeChart_EmptyChart_ShouldNotChangeDuration()
        {
            var chart = new ParsedChart();
            chart.FinalizeChart();
            Assert.Equal(0.0, chart.DurationMs);
        }

        [Fact]
        public void FinalizeChart_DefaultMeasure_ShouldEmitQuarterNoteMarkers()
        {
            var chart = CreateChartWithNote(0);

            chart.FinalizeChart();

            Assert.Equal(
                new[] { 0.0, 500.0, 1000.0, 1500.0 },
                chart.BeatMarkers.Select(marker => marker.TimeMs));
            Assert.Equal(
                new[] { true, false, false, false },
                chart.BeatMarkers.Select(marker => marker.IsMeasureStart));
        }

        [Fact]
        public void FinalizeChart_ShortMeasure_ShouldEmitThreeQuarterNoteMarkers()
        {
            var chart = CreateChartWithNote(0);
            chart.TimingMap.SetMeasureLength(0, 0.75);

            chart.FinalizeChart();

            Assert.Collection(
                chart.BeatMarkers,
                marker => Assert.Equal(0.0, marker.TimeMs, 6),
                marker => Assert.Equal(500.0, marker.TimeMs, 6),
                marker => Assert.Equal(1000.0, marker.TimeMs, 6));
        }

        [Fact]
        public void FinalizeChart_ExtendedMeasure_ShouldEmitSixQuarterNoteMarkers()
        {
            var chart = CreateChartWithNote(0);
            chart.TimingMap.SetMeasureLength(0, 1.5);

            chart.FinalizeChart();

            Assert.Equal(
                new[] { 0.0, 500.0, 1000.0, 1500.0, 2000.0, 2500.0 },
                chart.BeatMarkers.Select(marker => marker.TimeMs));
        }

        [Fact]
        public void FinalizeChart_TwoAndHalfBeatMeasure_ShouldNotRoundBeatOffsets()
        {
            var chart = CreateChartWithNote(0);
            chart.TimingMap.SetMeasureLength(0, 0.625);

            chart.FinalizeChart();

            Assert.Collection(
                chart.BeatMarkers,
                marker => Assert.Equal(0.0, marker.TimeMs, 6),
                marker => Assert.Equal(500.0, marker.TimeMs, 6),
                marker => Assert.Equal(1000.0, marker.TimeMs, 6));
        }

        [Fact]
        public void FinalizeChart_BoundaryTempoChange_ShouldUseNewBpmForNextMeasure()
        {
            var chart = CreateChartWithNote(0);
            chart.AddNote(new Note(0, 1, 0, 0x11, "01"));
            chart.TimingMap.SetTempoChange(1, 0, 240.0);

            chart.FinalizeChart();

            Assert.Equal(
                new[]
                {
                    0.0, 500.0, 1000.0, 1500.0,
                    2000.0, 2250.0, 2500.0, 2750.0
                },
                chart.BeatMarkers.Select(marker => marker.TimeMs));
        }

        [Fact]
        public void FinalizeChart_InMeasureTempoChange_ShouldResolveLaterMarkersFromNewBpm()
        {
            var chart = CreateChartWithNote(0);
            chart.TimingMap.SetTempoChange(0, 96, 240.0);

            chart.FinalizeChart();

            Assert.Equal(
                new[] { 0.0, 500.0, 1000.0, 1250.0 },
                chart.BeatMarkers.Select(marker => marker.TimeMs));
        }

        [Fact]
        public void FinalizeChart_AdjacentMeasures_ShouldHaveOneSharedBoundaryAccent()
        {
            var chart = CreateChartWithNote(0);
            chart.AddNote(new Note(0, 1, 0, 0x11, "01"));

            chart.FinalizeChart();

            var boundaryMarkers = chart.BeatMarkers
                .Where(marker => marker.TimeMs == 2000.0)
                .ToArray();
            Assert.Single(boundaryMarkers);
            Assert.True(boundaryMarkers[0].IsMeasureStart);
        }

        [Fact]
        public void FinalizeChart_TerminalMeasureLine_ShouldNotCreateExtraMetronomeMeasure()
        {
            var chart = CreateChartWithNote(0);

            chart.FinalizeChart();

            Assert.Equal(new[] { 0, 1 }, chart.MeasureLines.Select(line => line.Bar));
            Assert.Equal(4, chart.BeatMarkers.Count);
            Assert.DoesNotContain(chart.BeatMarkers, marker => marker.TimeMs == 2000.0);
        }

        [Fact]
        public void FinalizeChart_EmptyChart_ShouldEmitNoBeatMarkers()
        {
            var chart = new ParsedChart();

            chart.FinalizeChart();

            Assert.Empty(chart.BeatMarkers);
        }

        [Fact]
        public void FinalizeChart_Repeated_ShouldNotDuplicateBeatMarkers()
        {
            var chart = CreateChartWithNote(0);
            chart.FinalizeChart();
            var firstMarkers = chart.BeatMarkers
                .Select(marker => (marker.TimeMs, marker.IsMeasureStart))
                .ToArray();

            chart.FinalizeChart();

            Assert.Equal(
                firstMarkers,
                chart.BeatMarkers
                    .Select(marker => (marker.TimeMs, marker.IsMeasureStart))
                    .ToArray());
        }

        [Fact]
        public void FinalizeChart_MarginallyLongMeasure_ShouldNotEmitNearBoundaryMarker()
        {
            var chart = CreateChartWithNote(0);
            chart.TimingMap.SetMeasureLength(0, 1.0000000001);

            chart.FinalizeChart();

            Assert.Equal(4, chart.BeatMarkers.Count);
        }

        [Fact]
        public void FinalizeChart_VeryShortPositiveMeasure_ShouldEmitMeasureStartAccent()
        {
            var chart = CreateChartWithNote(0);
            chart.TimingMap.SetMeasureLength(0, 0.000001);

            chart.FinalizeChart();

            var marker = Assert.Single(chart.BeatMarkers);
            Assert.Equal(0.0, marker.TimeMs);
            Assert.True(marker.IsMeasureStart);
        }

        [Fact]
        public void FinalizeChart_LargeMeasureMultiplier_ShouldBoundBeatMarkerMaterialization()
        {
            var chart = CreateChartWithNote(0);
            chart.TimingMap.SetMeasureLength(0, 1000.0);

            chart.FinalizeChart();

            Assert.Equal(256, chart.BeatMarkers.Count);
        }

        [Fact]
        public void FinalizeChart_ShouldSortNotesByTime()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            chart.Notes.Add(new Note(0, 1, 96, 0, "01"));
            chart.Notes.Add(new Note(0, 0, 48, 0, "01"));
            chart.Notes.Add(new Note(0, 0, 144, 0, "01"));

            chart.FinalizeChart();

            Assert.Equal(500.0, chart.Notes[0].TimeMs, precision: 3);
            Assert.Equal(1500.0, chart.Notes[1].TimeMs, precision: 3);
            Assert.Equal(3000.0, chart.Notes[2].TimeMs, precision: 3);
        }

        [Fact]
        public void FinalizeChart_ShouldSortBGMEventsByTime()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            chart.BGMEvents.Add(new BGMEvent(1, 96, "02"));
            chart.BGMEvents.Add(new BGMEvent(0, 48, "01"));

            chart.FinalizeChart();

            Assert.Equal(500.0, chart.BGMEvents[0].TimeMs, precision: 3);
            Assert.Equal(3000.0, chart.BGMEvents[1].TimeMs, precision: 3);
        }

        [Fact]
        public void FinalizeChart_SparseNotes_ShouldGenerateEveryBoundaryThroughTerminal()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            chart.AddNote(new Note(0, 0, 0, 0x11, "01"));
            chart.AddNote(new Note(0, 2, 0, 0x11, "01"));

            chart.FinalizeChart();

            Assert.Equal(new[] { 0, 1, 2, 3 },
                chart.MeasureLines.Select(line => line.Bar));
            Assert.Equal(new[] { 0.0, 2000.0, 4000.0, 6000.0 },
                chart.MeasureLines.Select(line => line.TimeMs));
        }

        [Fact]
        public void FinalizeChart_BgmOnly_ShouldGenerateMeasureBoundaries()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            chart.AddBGMEvent(new BGMEvent(1, 0, "01"));

            chart.FinalizeChart();

            Assert.Equal(new[] { 0, 1, 2 },
                chart.MeasureLines.Select(line => line.Bar));
        }

        [Fact]
        public void FinalizeChart_EmptyChart_ShouldGenerateNoMeasureBoundaries()
        {
            var chart = new ParsedChart();

            chart.FinalizeChart();

            Assert.Empty(chart.MeasureLines);
        }

        [Fact]
        public void FinalizeChart_WhenCalledTwice_ShouldNotDuplicateMeasureBoundaries()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            chart.AddNote(new Note(0, 1, 0, 0x11, "01"));
            chart.FinalizeChart();
            var firstBoundaries = chart.MeasureLines
                .Select(line => (line.Bar, line.TimeMs))
                .ToArray();

            chart.FinalizeChart();

            Assert.Equal(firstBoundaries,
                chart.MeasureLines.Select(line => (line.Bar, line.TimeMs)).ToArray());
        }

        [Fact]
        public void FinalizeChart_TerminalBoundary_ShouldNotExtendDuration()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            chart.AddNote(new Note(0, 2, 0, 0x11, "01"));

            chart.FinalizeChart();

            Assert.Equal(4500.0, chart.DurationMs, precision: 3);
            Assert.Equal(6000.0, chart.MeasureLines[^1].TimeMs, precision: 3);
        }

        [Fact]
        public void FinalizeChart_ShouldOverwriteSeededTimeFromAuthoredPosition()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            var note = new Note(0, 1, 0, 0x11, "01") { TimeMs = 12345.0 };
            chart.AddNote(note);

            chart.FinalizeChart();

            Assert.Equal(2000.0, note.TimeMs, 3);
        }

        [Fact]
        public void FinalizeChart_Repeated_ShouldKeepDurationStable()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            chart.AddNote(new Note(0, 1, 0, 0x11, "01"));

            chart.FinalizeChart();
            var first = chart.DurationMs;
            chart.FinalizeChart();

            Assert.Equal(first, chart.DurationMs, 3);
        }

        [Fact]
        public void FinalizeChart_TimeZeroNote_ShouldStillReceiveEndBuffer()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            chart.AddNote(new Note(0, 0, 0, 0x11, "01"));

            chart.FinalizeChart();

            Assert.Equal(0.0, chart.Notes[0].TimeMs, 3);
            Assert.Equal(500.0, chart.DurationMs, 3);
        }

        [Fact]
        public void FinalizeChart_OversizedTick_ShouldBuildThroughNormalizedBar()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            chart.AddNote(new Note(0, 0, 960, 0x11, "01"));

            chart.FinalizeChart();

            Assert.Equal(10000.0, chart.Notes[0].TimeMs, 3);
            Assert.Equal(10500.0, chart.DurationMs, 3);
            Assert.Equal(6, chart.MeasureLines[^1].Bar);
            Assert.Equal(12000.0, chart.MeasureLines[^1].TimeMs, 3);
        }

        [Fact]
        public void FinalizeChart_VideoEvent_ShouldResolveTimeAfterMeasureLengthChange()
        {
            // Channel 02 analog: a shortened measure before the video event
            var chart = new ParsedChart { Bpm = 120.0 };
            chart.TimingMap.SetMeasureLength(0, 0.5);
            chart.AddVideoEvent(new ChartVideoEvent(1, 0, "01"));

            chart.FinalizeChart();

            Assert.Equal(1000.0, chart.VideoEvents[0].TimeMs, 3);
        }

        [Fact]
        public void FinalizeChart_VideoEvent_ShouldResolveTimeAfterTempoChange()
        {
            // Channel 03/08 analog: a mid-measure tempo change before the video event
            var chart = new ParsedChart { Bpm = 120.0 };
            chart.TimingMap.SetTempoChange(0, 96, 240.0);
            chart.AddVideoEvent(new ChartVideoEvent(1, 0, "01"));

            chart.FinalizeChart();

            // Measure 0: first half at 120 BPM (1000ms), second half at 240 BPM (500ms)
            Assert.Equal(1500.0, chart.VideoEvents[0].TimeMs, 3);
        }

        [Fact]
        public void FinalizeChart_ShouldSortVideoEventsByTime()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            chart.AddVideoEvent(new ChartVideoEvent(1, 96, "02"));
            chart.AddVideoEvent(new ChartVideoEvent(0, 48, "01"));

            chart.FinalizeChart();

            Assert.Equal(500.0, chart.VideoEvents[0].TimeMs, 3);
            Assert.Equal(3000.0, chart.VideoEvents[1].TimeMs, 3);
        }

        [Fact]
        public void FinalizeChart_VideoBeyondLastNoteAndBgm_ShouldExtendEventHorizon()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            chart.AddNote(new Note(0, 0, 0, 0x11, "01"));
            chart.AddBGMEvent(new BGMEvent(1, 0, "01"));
            chart.AddVideoEvent(new ChartVideoEvent(3, 0, "01"));

            chart.FinalizeChart();

            Assert.Equal(6000.0, chart.VideoEvents[0].TimeMs, 3);
            Assert.Equal(6500.0, chart.DurationMs, 3);
        }

        #endregion

        private static ParsedChart CreateChartWithNote(int bar)
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            chart.AddNote(new Note(0, bar, 0, 0x11, "01"));
            return chart;
        }

        #region GetNotesInTimeRange Tests

        [Fact]
        public void GetNotesInTimeRange_ShouldReturnOnlyNotesInRange()
        {
            var chart = new ParsedChart();
            chart.Notes.Add(new Note(0, 0, 0, 0, "01") { TimeMs = 100.0 });
            chart.Notes.Add(new Note(1, 0, 0, 0, "01") { TimeMs = 500.0 });
            chart.Notes.Add(new Note(2, 0, 0, 0, "01") { TimeMs = 1000.0 });
            chart.Notes.Add(new Note(3, 0, 0, 0, "01") { TimeMs = 2000.0 });

            var inRange = chart.GetNotesInTimeRange(400.0, 1000.0).ToList();

            Assert.Equal(2, inRange.Count);
            Assert.All(inRange, n => Assert.True(n.TimeMs >= 400.0 && n.TimeMs <= 1000.0));
        }

        [Fact]
        public void GetNotesInTimeRange_WithEmptyRange_ShouldReturnNoNotes()
        {
            var chart = new ParsedChart();
            chart.Notes.Add(new Note(0, 0, 0, 0, "01") { TimeMs = 5000.0 });

            var inRange = chart.GetNotesInTimeRange(0.0, 100.0).ToList();

            Assert.Empty(inRange);
        }

        #endregion

        #region GetNotesForLane Tests

        [Fact]
        public void GetNotesForLane_ShouldReturnOnlyNotesInThatLane()
        {
            var chart = new ParsedChart();
            chart.Notes.Add(new Note(laneIndex: 0, 0, 0, 0, "01") { TimeMs = 100.0 });
            chart.Notes.Add(new Note(laneIndex: 0, 0, 0, 0, "01") { TimeMs = 500.0 });
            chart.Notes.Add(new Note(laneIndex: 3, 0, 0, 0, "01") { TimeMs = 300.0 });

            var lane0Notes = chart.GetNotesForLane(0).ToList();
            var lane3Notes = chart.GetNotesForLane(3).ToList();

            Assert.Equal(2, lane0Notes.Count);
            Assert.Single(lane3Notes);
        }

        [Fact]
        public void GetNotesForLane_WithEmptyLane_ShouldReturnEmpty()
        {
            var chart = new ParsedChart();
            chart.Notes.Add(new Note(0, 0, 0, 0, "01") { TimeMs = 100.0 });

            var lane5Notes = chart.GetNotesForLane(5).ToList();

            Assert.Empty(lane5Notes);
        }

        #endregion

        #region ToString Tests

        [Fact]
        public void ToString_ShouldContainBpmAndNoteCount()
        {
            var chart = new ParsedChart("/test/song.dtx") { Bpm = 145.0 };
            chart.Notes.Add(new Note(0, 0, 0, 0, "01") { TimeMs = 500.0 });

            var result = chart.ToString();

            Assert.Contains("145", result);
            Assert.Contains("1", result);
        }

        [Fact]
        public void ToString_EmptyChart_ShouldNotThrow()
        {
            var chart = new ParsedChart();
            var result = chart.ToString();
            Assert.NotNull(result);
        }

        #endregion
    }
}
