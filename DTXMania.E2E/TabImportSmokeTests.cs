using System.Text;
using DTXMania.Automation.JsonRpc;
using DTXMania.Automation.Support;
using DTXMania.Automation.Telemetry;
using DTXMania.E2E.Fixtures;
using DTXMania.E2E.Support;
using DTXMania.Game.Lib.Config;

namespace DTXMania.E2E;

/// <summary>
/// HPA-693 black-box smoke: a song folder holding only a MusicXML drum tab
/// (no .dtx) must be found by the song scan, converted to a chart, and
/// auto-played in tab display mode from launch to a cleared Result with
/// every converted note judged.
/// </summary>
[Trait("Category", "E2E")]
public sealed class TabImportSmokeTests
{
    public const string TabSongTitle = "E2E Tab Import Smoke";
    private const string TabFileName = "tab-import-smoke.musicxml";

    // Bar 1: eight closed hi-hats with kicks on 1 and 3 and snares on 2 and 4.
    // Bar 2: kick + crash on beat 1. 14 drum notes in total.
    private const int ExpectedNoteCount = 14;

    [Fact(Timeout = 240_000)]
    public async Task MusicXmlTab_ShouldAutoPlayInTabModeAndReachClearedResult()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(210));
        var repoRoot = E2EGameLaunch.ResolveRepoRoot();
        var runRoot = Path.Combine(Path.GetTempPath(), "dtxmaniacx-e2e-tab-" + Guid.NewGuid().ToString("N"));
        var apiPort = E2EGameLaunch.ResolveApiPort();
        var fixture = E2EFixtureBuilder.Build(
            runRoot, repoRoot, apiPort, enableAutoPlayLanes: true, noteDisplayStyle: DrumsNoteDisplayStyle.Tab);

        // Replace the generated .dtx so the tab is the only chart in the library.
        File.Delete(fixture.ChartPath);
        var tabPath = Path.Combine(fixture.SongDirectory, TabFileName);
        File.WriteAllText(tabPath, BuildMusicXmlTab(), Encoding.UTF8);

        await using var bundle = E2EGameLaunch.CreateClientBundle(fixture);
        var process = bundle.Process;
        var client = bundle.Client;

        try
        {
            process.Start(E2EGameLaunch.CreateOptions(fixture));
            await process.WaitForStartupAsync(
                client.GetHealthAsync,
                TimeSpan.FromSeconds(60),
                TimeSpan.FromMilliseconds(500),
                cancellation.Token);

            await WaitForStageAsync(client, "Title", TimeSpan.FromSeconds(45), cancellation.Token);
            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellation.Token);
            await WaitForStageAsync(client, "SongSelect", TimeSpan.FromSeconds(45), cancellation.Token);

            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellation.Token);
            await Task.Delay(500, cancellation.Token);
            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellation.Token);

            await Eventually.UntilAsync(
                token => client.GetGameStateAsync(token),
                state => string.Equals(state.StageType, "Performance", StringComparison.Ordinal)
                    && state.PerformanceReady
                    && string.Equals(state.NoteDisplayStyle, nameof(DrumsNoteDisplayStyle.Tab), StringComparison.Ordinal)
                    && state.CurrentSongTimeMs >= 2500,
                TimeSpan.FromSeconds(60),
                TimeSpan.FromMilliseconds(100),
                "Performance playing the first tab bar",
                cancellation.Token);
            await SaveScreenshotAsync(client, fixture, "performance-tab-import.png", cancellation.Token);

            var resultState = await Eventually.UntilAsync(
                token => client.GetGameStateAsync(token),
                state => string.Equals(state.StageType, "Result", StringComparison.Ordinal)
                    && state.StageCompleted,
                TimeSpan.FromSeconds(90),
                TimeSpan.FromMilliseconds(250),
                "Result stage completion",
                cancellation.Token);
            await E2EArtifactWriter.WriteJsonAsync(fixture, "final-state-tab-import.json", resultState);

            Assert.Equal(TabSongTitle, resultState.SelectedSongTitle);
            Assert.Equal("SongComplete", resultState.CompletionReason);
            Assert.True(resultState.ClearFlag);
            Assert.Equal(ExpectedNoteCount, resultState.TotalNotes);
            Assert.Equal(ExpectedNoteCount, resultState.TotalJudgements);
            Assert.Equal(0, resultState.MissCount);
        }
        catch
        {
            await SaveScreenshotAsync(client, fixture, "failure-tab-import.png", CancellationToken.None);
            throw;
        }
        finally
        {
            E2EArtifactWriter.CopyFixtureFiles(fixture);
            File.Copy(tabPath, Path.Combine(fixture.ArtifactRoot, TabFileName), overwrite: true);
            await E2EArtifactWriter.WriteTextAsync(fixture, "game-stdout-tab-import.log", process.StandardOutput);
            await E2EArtifactWriter.WriteTextAsync(fixture, "game-stderr-tab-import.log", process.StandardError);
        }
    }

    /// <summary>
    /// Two 4/4 bars at 120 BPM in MusicXML. midi-unpitched is 1-based, so
    /// 37 = GM 36 kick, 39 = GM 38 snare, 43 = GM 42 closed hi-hat, 50 = GM 49 crash.
    /// </summary>
    private static string BuildMusicXmlTab()
    {
        static string Note(int instrument, string type, int duration, bool chord = false) =>
            $"<note>{(chord ? "<chord/>" : "")}<unpitched><display-step>C</display-step><display-octave>5</display-octave></unpitched>" +
            $"<duration>{duration}</duration><instrument id=\"P1-I{instrument}\"/><voice>1</voice><type>{type}</type></note>";

        var groove = new StringBuilder();
        for (var eighth = 0; eighth < 8; eighth++)
        {
            groove.Append(Note(43, "eighth", 24));
            if (eighth % 4 == 0)
                groove.Append(Note(37, "eighth", 24, chord: true));
            else if (eighth % 4 == 2)
                groove.Append(Note(39, "eighth", 24, chord: true));
        }

        var instruments = new[] { 37, 39, 43, 50 };
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<score-partwise version=\"4.0\">" +
            $"<work><work-title>{TabSongTitle}</work-title></work>" +
            "<identification><creator type=\"composer\">CI</creator></identification>" +
            "<part-list><score-part id=\"P1\"><part-name>Drumset</part-name>" +
            string.Concat(instruments.Select(i => $"<score-instrument id=\"P1-I{i}\"><instrument-name>GM {i - 1}</instrument-name></score-instrument>")) +
            string.Concat(instruments.Select(i => $"<midi-instrument id=\"P1-I{i}\"><midi-channel>10</midi-channel><midi-unpitched>{i}</midi-unpitched></midi-instrument>")) +
            "</score-part></part-list><part id=\"P1\">" +
            "<measure number=\"1\"><attributes><divisions>48</divisions><time><beats>4</beats><beat-type>4</beat-type></time>" +
            "<clef><sign>percussion</sign></clef></attributes>" +
            "<direction placement=\"above\"><direction-type><metronome><beat-unit>quarter</beat-unit><per-minute>120</per-minute>" +
            "</metronome></direction-type><sound tempo=\"120\"/></direction>" +
            groove +
            "</measure>" +
            "<measure number=\"2\">" + Note(37, "whole", 192) + Note(50, "whole", 192, chord: true) + "</measure>" +
            "</part></score-partwise>";
    }

    private static Task<GameStateSnapshot> WaitForStageAsync(
        JsonRpcGameClient client,
        string expectedStageType,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return Eventually.UntilAsync(
            token => client.GetGameStateAsync(token),
            state => string.Equals(state.StageType, expectedStageType, StringComparison.Ordinal),
            timeout,
            TimeSpan.FromMilliseconds(500),
            expectedStageType,
            cancellationToken);
    }

    private static async Task SaveScreenshotAsync(
        JsonRpcGameClient client,
        E2EFixture fixture,
        string fileName,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);
            var imageData = await client.TakeScreenshotBase64Async(cancellation.Token);
            if (string.IsNullOrWhiteSpace(imageData))
                return;

            Directory.CreateDirectory(fixture.ArtifactRoot);
            await File.WriteAllBytesAsync(
                Path.Combine(fixture.ArtifactRoot, fileName), Convert.FromBase64String(imageData), cancellation.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[E2E] Screenshot capture skipped for '{fileName}': {ex.GetType().Name}: {ex.Message}");
        }
    }
}
