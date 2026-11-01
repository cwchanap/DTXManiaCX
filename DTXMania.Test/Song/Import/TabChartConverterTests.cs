using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AlphaTab;
using AlphaTab.Exporter;
using AlphaTab.Importer;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Import;
using Xunit;

namespace DTXMania.Test.Song.Import
{
    /// <summary>
    /// HPA-693: Guitar Pro / MusicXML drum tabs convert to DTX text that
    /// <see cref="DTXChartParser"/> plays. Fixtures are small MusicXML
    /// documents built in code; the Guitar Pro path round-trips one through
    /// AlphaTab's GP7 exporter so no binary fixture is needed.
    /// </summary>
    public class TabChartConverterTests
    {
        // General MIDI drum notes used by the fixtures.
        private const int Kick = 36, Snare = 38, ClosedHat = 42, PedalHat = 44, OpenHat = 46,
            Crash = 49, Ride = 51, HighTom = 50, LowTom = 47, FloorTom = 43, Splash = 55, Cowbell = 56;

        #region Mapping

        [Theory]
        [InlineData(Kick, "13")]
        [InlineData(Snare, "12")]
        [InlineData(ClosedHat, "11")]
        [InlineData(OpenHat, "18")]
        [InlineData(PedalHat, "1B")]
        [InlineData(Crash, "16")]
        [InlineData(Splash, "1A")]
        [InlineData(Ride, "19")]
        [InlineData(HighTom, "14")]
        [InlineData(LowTom, "15")]
        [InlineData(FloorTom, "17")]
        public void ConvertToDtx_SingleDrumHit_ShouldLandOnItsDtxChannel(int gmNote, string channel)
        {
            var lines = Convert(DrumScore(Measure(Hit("whole", gmNote))));

            Assert.Equal("01", lines[$"001{channel}"]);
            Assert.Single(DrumLines(lines));
        }

        [Fact]
        public void ConvertToDtx_RockGroove_ShouldWriteCompactLinesPerDrum()
        {
            var bar = string.Concat(Enumerable.Range(0, 8).Select(i => i switch
            {
                0 or 4 => Hit("eighth", Kick, ClosedHat),
                2 or 6 => Hit("eighth", Snare, ClosedHat),
                _ => Hit("eighth", ClosedHat),
            }));

            var lines = Convert(DrumScore(Measure(bar)));

            Assert.Equal("0101010101010101", lines["00111"]);
            Assert.Equal("00010001", lines["00112"]);
            Assert.Equal("0101", lines["00113"]);
        }

        [Fact]
        public void ConvertToDtx_UnmappedPercussion_ShouldBeDropped()
        {
            var lines = Convert(DrumScore(Measure(Hit("half", Cowbell, Kick) + Hit("half", Cowbell))));

            Assert.Equal("01", lines["00113"]);
            Assert.Single(DrumLines(lines));
        }

        [Fact]
        public void ConvertToDtx_ShouldStartWithEmptyCountInBar()
        {
            var lines = Convert(DrumScore(Measure(Hit("whole", Kick))));

            Assert.Equal("00", lines["00011"]);
            Assert.DoesNotContain(lines.Keys, key => key.StartsWith("000") && key != "00011");
        }

        #endregion

        #region Timing

        [Fact]
        public void ConvertToDtx_TempoChanges_ShouldWriteBpmDefinitionsAndChannel08()
        {
            var lines = Convert(DrumScore(
                Measure(Hit("whole", Kick), tempo: 120),
                Measure(Hit("half", Snare) + Tempo(150) + Hit("half", Snare))));

            Assert.Equal("120", lines["BPM"]);
            Assert.Equal("150", lines["BPM01"]);
            Assert.Equal("0001", lines["00208"]);
        }

        [Fact]
        public void ConvertToDtx_ThreeFourBars_ShouldWriteBarLengthOnlyWhenItChanges()
        {
            var threeQuarters = Hit("quarter", Snare) + Hit("quarter", Snare) + Hit("quarter", Snare);

            var lines = Convert(DrumScore(
                Measure(Hit("whole", Kick)),
                Measure(threeQuarters, beats: 3),
                Measure(threeQuarters),
                Measure(Hit("whole", Kick), beats: 4)));

            Assert.False(lines.ContainsKey("00102"));
            Assert.Equal("0.75", lines["00202"]);
            Assert.Equal("010101", lines["00212"]);
            Assert.False(lines.ContainsKey("00302"));
            Assert.Equal("010101", lines["00312"]);
            Assert.Equal("1", lines["00402"]);
        }

        [Fact]
        public void ConvertToDtx_EighthTriplets_ShouldLandOnTripletGrid()
        {
            var triplets = string.Concat(Enumerable.Range(0, 12).Select(_ => TripletHit("eighth", ClosedHat)));

            var lines = Convert(DrumScore(Measure(triplets)));

            Assert.Equal(string.Concat(Enumerable.Repeat("01", 12)), lines["00111"]);
        }

        [Fact]
        public void ConvertToDtx_RepeatBarlines_ShouldExpandRepeatedBars()
        {
            var lines = Convert(DrumScore(
                Measure(Hit("whole", Kick), repeatStart: true, repeatEnd: true),
                Measure(Hit("whole", Snare))));

            Assert.Equal("01", lines["00113"]);
            Assert.Equal("01", lines["00213"]);
            Assert.Equal("01", lines["00312"]);
            Assert.DoesNotContain(lines.Keys, key => key.StartsWith("004"));
        }

        #endregion

        #region Tracks and metadata

        [Fact]
        public void ConvertToDtx_ScoreWithGuitarAndDrums_ShouldKeepOnlyDrumNotes()
        {
            // E2 is MIDI 40, which would read as a snare hit if the guitar track were kept.
            var guitar = Part("PG", "Guitar", Measure(GuitarNote("E", 2), clef: "G"));
            var drums = DrumPart(Measure(Hit("whole", Kick)));

            var lines = Convert(Document("Song", "Band", guitar, drums));

            Assert.Equal("01", lines["00113"]);
            Assert.Single(DrumLines(lines));
        }

        [Fact]
        public void ConvertToDtx_ScoreWithoutDrumTrack_ShouldThrowUnplayableTab()
        {
            var xml = Document("Song", "Band", Part("PG", "Guitar", Measure(GuitarNote("E", 2), clef: "G")));

            Assert.Throws<UnplayableTabException>(() => TabChartConverter.ConvertToDtx(Bytes(xml), "fallback"));
        }

        [Fact]
        public void ConvertToDtx_MoreBarsThanDtxCanNumber_ShouldThrowUnplayableTab()
        {
            // 999 tab bars + the count-in fill bars 000-999; one more cannot be written.
            var measures = Enumerable.Repeat(Measure(Hit("whole", Kick)), 1000).ToArray();

            Assert.Throws<UnplayableTabException>(() => TabChartConverter.ConvertToDtx(Bytes(DrumScore(measures)), "long"));
        }

        [Fact]
        public void ConvertToDtx_MoreTemposThanBpmIds_ShouldThrowUnplayableTab()
        {
            // 82 bars x 16 sixteenths, each with its own tempo. The first is the base
            // #BPM; the other 1,311 changes need ids, but only #BPM01-#BPMZZ (1,295) exist.
            var bpm = 40;
            var measures = Enumerable.Range(0, 82).Select(_ => Measure(string.Concat(
                Enumerable.Range(0, 16).Select(__ => Tempo(bpm++) + Hit("16th", Kick))))).ToArray();

            Assert.Throws<UnplayableTabException>(() => TabChartConverter.ConvertToDtx(Bytes(DrumScore(measures)), "tempos"));
        }

        [Fact]
        public void ConvertToDtx_TitleAndArtist_ShouldBecomeDtxHeaders()
        {
            var lines = Convert(Document("Canon", "Pachelbel", DrumPart(Measure(Hit("whole", Kick)))));

            Assert.Equal("Canon", lines["TITLE"]);
            Assert.Equal("Pachelbel", lines["ARTIST"]);
            Assert.Equal("50", lines["DLEVEL"]);
        }

        [Fact]
        public void ConvertToDtx_UntitledScore_ShouldUseFallbackTitle()
        {
            // A line break in the file name must not inject a chart row.
            var lines = Convert(Document("", "", DrumPart(Measure(Hit("whole", Kick)))), "my\n#00116: 01 tab");

            Assert.Equal("my #00116: 01 tab", lines["TITLE"]);
            Assert.False(lines.ContainsKey("00116"));
            Assert.False(lines.ContainsKey("ARTIST"));
        }

        [Fact]
        public void ConvertToDtx_GuitarProFile_ShouldConvertLikeMusicXml()
        {
            var xml = DrumScore(Measure(Hit("half", Kick, Crash) + Hit("half", Snare)));
            var settings = new Settings();
            var score = ScoreLoader.LoadScoreFromBytes(Bytes(xml), settings);
            var guitarPro = ((IEnumerable<byte>)new Gp7Exporter().Export(score, settings)).ToArray();

            var lines = Parse(TabChartConverter.ConvertToDtx(guitarPro, "gp"));

            Assert.Equal("01", lines["00113"]);
            Assert.Equal("0001", lines["00112"]);
            Assert.Equal("01", lines["00116"]);
        }

        #endregion

        #region DTXChartParser integration

        [Theory]
        [InlineData("song.gp")]
        [InlineData("song.GPX")]
        [InlineData("song.gp5")]
        [InlineData("song.gp4")]
        [InlineData("song.gp3")]
        [InlineData("song.musicxml")]
        [InlineData("song.mxl")]
        public void IsSupportedFile_TabFile_ShouldBeTrue(string fileName)
        {
            Assert.True(DTXChartParser.IsSupportedFile(fileName));
            Assert.True(TabChartConverter.IsTabFile(fileName));
        }

        [Fact]
        public void IsTabFile_DtxFile_ShouldBeFalse()
        {
            Assert.False(TabChartConverter.IsTabFile("song.dtx"));
        }

        [Fact]
        public async Task ParseAsync_MusicXmlFile_ShouldParseConvertedDrumNotes()
        {
            var path = WriteTempTab(DrumScore(Measure(Hit("half", Kick, ClosedHat) + Hit("half", Snare, ClosedHat), tempo: 120)));
            try
            {
                var chart = await DTXChartParser.ParseAsync(path);

                Assert.Equal(4, chart.Notes.Count);
                Assert.Equal(120, chart.Bpm);
                // Bar 001 starts after the 2 s count-in bar at 120 BPM; beat 3 is 1 s later.
                var times = chart.Notes.Select(note => note.TimeMs).OrderBy(time => time).ToArray();
                Assert.Equal(new[] { 2000.0, 2000.0, 3000.0, 3000.0 }, times.Select(time => Math.Round(time)));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_MusicXmlFile_ShouldReadConvertedHeader()
        {
            var path = WriteTempTab(Document("Tab Song", "Tab Band", DrumPart(Measure(Hit("whole", Kick)))));
            try
            {
                var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(path);

                Assert.Equal("Tab Song", song.Title);
                Assert.Equal("Tab Band", song.Artist);
                Assert.Equal(50, chart.DrumLevel);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_TabWithoutDrumTrack_ShouldThrowSoTheScanSkipsIt()
        {
            var path = WriteTempTab(Document("Guitar Only", "Band", Part("PG", "Guitar", Measure(GuitarNote("E", 2), clef: "G"))));
            try
            {
                await Assert.ThrowsAsync<UnplayableTabException>(() => DTXChartParser.ParseSongEntitiesAsync(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        #endregion

        #region Helpers

        private static Dictionary<string, string> Convert(string musicXml, string fallbackTitle = "fallback") =>
            Parse(TabChartConverter.ConvertToDtx(Bytes(musicXml), fallbackTitle));

        /// <summary>DTX "#KEY: value" lines keyed by KEY (e.g. "00113", "BPM01", "TITLE").</summary>
        private static Dictionary<string, string> Parse(string dtx) =>
            dtx.Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith('#') && line.Contains(':'))
                .ToDictionary(line => line[1..line.IndexOf(':')], line => line[(line.IndexOf(':') + 1)..].Trim());

        /// <summary>Bar-data lines on drum channels 11-1C, excluding the count-in bar.</summary>
        private static List<string> DrumLines(Dictionary<string, string> lines) =>
            lines.Keys.Where(key => key.Length == 5 && !key.StartsWith("000") &&
                int.TryParse(key[3..], System.Globalization.NumberStyles.HexNumber, null, out var ch) &&
                ch >= 0x11 && ch <= 0x1C).ToList();

        private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

        private static string WriteTempTab(string musicXml)
        {
            var path = Path.Combine(Path.GetTempPath(), $"tab-import-{Guid.NewGuid():N}.musicxml");
            File.WriteAllText(path, musicXml);
            return path;
        }

        private const string DrumPartId = "PD";

        private static readonly Dictionary<string, int> Durations = new()
        {
            ["whole"] = 192, ["half"] = 96, ["quarter"] = 48, ["eighth"] = 24, ["16th"] = 12,
        };

        private static string DrumScore(params string[] measures) => Document("Groove", "Tester", DrumPart(measures));

        private static string DrumPart(params string[] measures) => Part(DrumPartId, "Drumset", measures);

        private static string Document(string title, string artist, params string[] parts)
        {
            var partList = string.Concat(parts.Select(p => p.Split("<!--split-->")[0]));
            var partBodies = string.Concat(parts.Select(p => p.Split("<!--split-->")[1]));
            var work = string.IsNullOrEmpty(title) ? "" : $"<work><work-title>{title}</work-title></work>";
            var creator = string.IsNullOrEmpty(artist) ? "" : $"<identification><creator type=\"composer\">{artist}</creator></identification>";
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<score-partwise version=\"4.0\">" + work + creator +
                $"<part-list>{partList}</part-list>{partBodies}</score-partwise>";
        }

        /// <summary>A score-part entry and its measures, joined by a split marker for <see cref="Document"/>.</summary>
        private static string Part(string id, string name, params string[] measures)
        {
            var scorePart = new StringBuilder($"<score-part id=\"{id}\"><part-name>{name}</part-name>");
            if (id == DrumPartId)
            {
                var notes = Enumerable.Range(35, 25).ToList();
                foreach (var gm in notes)
                    scorePart.Append($"<score-instrument id=\"{id}-I{gm + 1}\"><instrument-name>GM {gm}</instrument-name></score-instrument>");
                foreach (var gm in notes)
                    scorePart.Append($"<midi-instrument id=\"{id}-I{gm + 1}\"><midi-channel>10</midi-channel><midi-unpitched>{gm + 1}</midi-unpitched></midi-instrument>");
            }
            scorePart.Append("</score-part>");

            var body = new StringBuilder($"<part id=\"{id}\">");
            for (int i = 0; i < measures.Length; i++)
            {
                var measure = measures[i];
                if (i == 0)
                {
                    // Divisions, the first time signature and the clef live in the first measure.
                    var clef = measure.Contains("<!--clef:G-->") ? "<sign>G</sign><line>2</line>" : "<sign>percussion</sign>";
                    var time = measure.Contains("<time>") ? "" : "<time><beats>4</beats><beat-type>4</beat-type></time>";
                    measure = $"<attributes><divisions>48</divisions>{time}<clef>{clef}</clef></attributes>" + measure;
                }
                body.Append($"<measure number=\"{i + 1}\">{measure}</measure>");
            }
            body.Append("</part>");

            return scorePart + "<!--split-->" + body;
        }

        private static string Measure(string notes, int beats = 0, int tempo = 0,
            bool repeatStart = false, bool repeatEnd = false, string clef = "percussion")
        {
            var xml = new StringBuilder();
            if (clef == "G")
                xml.Append("<!--clef:G-->");
            if (repeatStart)
                xml.Append("<barline location=\"left\"><repeat direction=\"forward\"/></barline>");
            if (beats > 0)
                xml.Append($"<attributes><time><beats>{beats}</beats><beat-type>4</beat-type></time></attributes>");
            if (tempo > 0)
                xml.Append(Tempo(tempo));
            xml.Append(notes);
            if (repeatEnd)
                xml.Append("<barline location=\"right\"><bar-style>light-heavy</bar-style><repeat direction=\"backward\"/></barline>");
            return xml.ToString();
        }

        private static string Tempo(int bpm) =>
            "<direction placement=\"above\"><direction-type><metronome><beat-unit>quarter</beat-unit>" +
            $"<per-minute>{bpm}</per-minute></metronome></direction-type><sound tempo=\"{bpm}\"/></direction>";

        /// <summary>Drum hits sounding together, as a MusicXML chord.</summary>
        private static string Hit(string type, params int[] gmNotes) =>
            Chord(type, Durations[type], "", gmNotes);

        private static string TripletHit(string type, params int[] gmNotes) =>
            Chord(type, Durations[type] * 2 / 3,
                "<time-modification><actual-notes>3</actual-notes><normal-notes>2</normal-notes></time-modification>", gmNotes);

        private static string Chord(string type, int duration, string modification, int[] gmNotes) =>
            string.Concat(gmNotes.Select((gm, i) =>
                $"<note>{(i > 0 ? "<chord/>" : "")}<unpitched><display-step>C</display-step><display-octave>5</display-octave></unpitched>" +
                $"<duration>{duration}</duration><instrument id=\"{DrumPartId}-I{gm + 1}\"/><voice>1</voice><type>{type}</type>{modification}</note>"));

        private static string GuitarNote(string step, int octave) =>
            $"<note><pitch><step>{step}</step><octave>{octave}</octave></pitch><duration>192</duration><voice>1</voice><type>whole</type></note>";

        #endregion
    }
}
