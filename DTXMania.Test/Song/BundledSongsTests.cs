using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Test.TestData;
using Xunit;

namespace DTXMania.Test.Song;

[Trait("Category", "Unit")]
public sealed class BundledSongsTests : IDisposable
{
    // Must match ACCENT_CLICK / BEAT_CLICK in tools/practicegen/practicegen.py.
    private static readonly string[] ClickWavIds = { "01", "02" };

    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(), "BundledSongsTests", Guid.NewGuid().ToString("N"));

    public static IEnumerable<object[]> PracticeCharts() =>
        Directory.EnumerateFiles(PracticeSourceDir, "*.dtx", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => new object[] { Path.GetRelativePath(PracticeSourceDir, path) });

    private static string PracticeSourceDir =>
        Path.Combine(RepoPaths.FindRepoRoot(), "DTXMania.Game", BundledSongs.DirectoryName, "Practice");

    [Fact]
    public void AppendTo_WhenBundledRootExists_ShouldAppendItAfterConfiguredRoots()
    {
        Directory.CreateDirectory(_tempRoot);

        var roots = BundledSongs.AppendTo(new[] { "A", "B" }, _tempRoot);

        Assert.Equal(new[] { "A", "B", _tempRoot }, roots);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("missing")]
    public void AppendTo_WhenBundledRootIsUnavailable_ShouldReturnConfiguredRoots(string? bundledRoot)
    {
        var path = bundledRoot == "missing" ? Path.Combine(_tempRoot, "missing") : bundledRoot;

        var roots = BundledSongs.AppendTo(new[] { "A" }, path);

        Assert.Equal(new[] { "A" }, roots);
    }

    [Fact]
    public void Root_ShouldShipThePracticeBoxNextToTheExecutable()
    {
        Assert.True(
            File.Exists(Path.Combine(BundledSongs.Root, "Practice", "box.def")),
            $"Practice box missing from {BundledSongs.Root}; check the BundledSongs item in the game csproj.");
    }

    [Fact]
    public void PracticeCharts_ShouldIncludeEveryGenre()
    {
        Assert.Equal(
            new[]
            {
                Path.Combine("01-pop-8-beat", "01-pop-8-beat.dtx"),
                Path.Combine("02-jazz-swing", "02-jazz-swing.dtx"),
                Path.Combine("03-punk-beat", "03-punk-beat.dtx"),
                Path.Combine("04-metal-double-bass", "04-metal-double-bass.dtx"),
            },
            PracticeCharts().Select(row => (string)row[0]));
    }

    [Theory]
    [MemberData(nameof(PracticeCharts))]
    public async Task PracticeChart_ShouldPlayASteadyMetronomeAsItsOnlyAudio(string relativePath)
    {
        var chart = await DTXChartParser.ParseAsync(Path.Combine(PracticeSourceDir, relativePath));

        Assert.NotEmpty(chart.Notes);
        Assert.True(string.IsNullOrEmpty(chart.BackgroundAudioPath));
        Assert.DoesNotContain(chart.Notes, note => ClickWavIds.Contains(note.Value));

        Assert.NotEmpty(chart.BGMEvents);
        Assert.All(chart.BGMEvents, click =>
        {
            Assert.Contains(click.WavId, ClickWavIds);
            Assert.True(File.Exists(click.AudioFilePath), $"Missing click audio {click.AudioFilePath}");
        });
        var beatMs = 60_000.0 / chart.Bpm;
        Assert.All(
            chart.BGMEvents.Zip(chart.BGMEvents.Skip(1), (previous, next) => next.TimeMs - previous.TimeMs),
            gapMs => Assert.Equal(beatMs, gapMs, precision: 3));
        Assert.True(
            chart.BGMEvents[^1].TimeMs >= chart.Notes[^1].TimeMs,
            "The metronome should keep clicking until the last note.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
