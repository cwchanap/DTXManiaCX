using System.Text;
using DTXMania.Automation.JsonRpc;
using DTXMania.Automation.Process;
using DTXMania.Automation.Support;
using DTXMania.Automation.Telemetry;
using DTXMania.E2E.Fixtures;
using DTXMania.E2E.Support;

namespace DTXMania.E2E;

/// <summary>
/// HPA-11 black-box smoke: a chart whose #AVI01 background video points at the
/// committed rawvideo fixture must flow launch -> SongSelect -> Performance ->
/// Result without hanging, with a screenshot artifact captured while the video
/// should be active. A machine without a usable ffmpeg still passes: failed or
/// missing media leaves the static-background fallback, so the run itself must
/// always complete. Deterministic child-process teardown is covered by the
/// Task 2 full-queue cancellation tests; the screenshot provides repeatable
/// visual evidence using the existing takeScreenshot endpoint.
/// </summary>
[Trait("Category", "E2E")]
public sealed class ChartBackgroundVideoSmokeTests
{
    public const string VideoSongTitle = "E2E Chart Video Smoke";
    private const string VideoFileName = "tiny-raw-bgr24.avi";
    private const double RetriggerSongTimeMs = 2000.0;

    [Fact(Timeout = 240_000)]
    public async Task ChartBackgroundVideo_ShouldPlayThroughPerformanceAndReachResult()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(210));
        var repoRoot = E2EGameLaunch.ResolveRepoRoot();
        var runRoot = Path.Combine(Path.GetTempPath(), "dtxmaniacx-e2e-video-" + Guid.NewGuid().ToString("N"));
        var apiPort = E2EGameLaunch.ResolveApiPort();
        var fixture = E2EFixtureBuilder.Build(runRoot, repoRoot, apiPort, enableAutoPlayLanes: true);
        StageVideoFixture(fixture, repoRoot);
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

            await WaitForPerformanceReadyAsync(client, TimeSpan.FromSeconds(60), cancellation.Token);

            // The second #AVI01 trigger sits at bar 1 (2000ms at 120 BPM). Poll for
            // the song clock to cross it, then capture the screenshot while the
            // video should have just restarted from media zero (the fixture is one
            // second long, so the evidence window closes at ~3000ms).
            await Eventually.UntilAsync(
                token => client.GetGameStateAsync(token),
                state => string.Equals(state.StageType, "Performance", StringComparison.Ordinal)
                    && state.CurrentSongTimeMs >= RetriggerSongTimeMs,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMilliseconds(50),
                "song clock crossing the bar-1 video retrigger",
                cancellation.Token);
            await SaveScreenshotAsync(client, fixture, "performance-video-active.png", cancellation.Token);

            await WaitForStageAsync(client, "Result", TimeSpan.FromSeconds(120), cancellation.Token);
            var resultState = await Eventually.UntilAsync(
                token => client.GetGameStateAsync(token),
                state =>
                    string.Equals(state.StageType, "Result", StringComparison.Ordinal) &&
                    state.StageCompleted,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMilliseconds(250),
                "Result stage completion",
                cancellation.Token);

            Assert.Equal(VideoSongTitle, resultState.SelectedSongTitle);
            Assert.Equal("SongComplete", resultState.CompletionReason);
            Assert.True(resultState.ClearFlag);
            await E2EArtifactWriter.WriteJsonAsync(fixture, "final-state-video.json", resultState);

            Assert.True(
                File.Exists(Path.Combine(fixture.ArtifactRoot, "performance-video-active.png")),
                "Expected the video-active performance screenshot artifact to exist.");
        }
        catch
        {
            await SaveScreenshotAsync(client, fixture, "failure-video-smoke.png", CancellationToken.None);
            throw;
        }
        finally
        {
            E2EArtifactWriter.CopyFixtureFiles(fixture);
            await E2EArtifactWriter.WriteTextAsync(fixture, "game-stdout-video.log", process.StandardOutput);
            await E2EArtifactWriter.WriteTextAsync(fixture, "game-stderr-video.log", process.StandardError);
        }
    }

    /// <summary>
    /// Stages the video fixture media before launch: copies the committed rawvideo
    /// AVI into the song directory (ResolveBGMPath resolves #AVI01 against it) and
    /// replaces the generated chart with a video-referencing variant. Uses the
    /// builder's audio file so audio preparation behaves like the standard fixture.
    /// </summary>
    private static void StageVideoFixture(E2EFixture fixture, string repoRoot)
    {
        var sourceVideoPath = Path.Combine(repoRoot, "DTXMania.Test", "TestData", "Video", VideoFileName);
        Assert.True(File.Exists(sourceVideoPath), $"Committed video fixture missing: {sourceVideoPath}");
        File.Copy(sourceVideoPath, Path.Combine(fixture.SongDirectory, VideoFileName), overwrite: true);
        File.WriteAllText(fixture.ChartPath, BuildVideoChart(), Encoding.UTF8);
    }

    private static string BuildVideoChart()
    {
        return string.Join('\n', new[]
        {
            $"#TITLE: {VideoSongTitle}",
            "#ARTIST: CI",
            "#BPM: 120",
            "#DLEVEL: 10",
            $"#WAV01: {E2EFixtureBuilder.AudioFileName}",
            $"#PREVIEW: {E2EFixtureBuilder.AudioFileName}",
            $"#AVI01: {VideoFileName}",
            string.Empty,
            "; Whole-file background video triggers: bar 0 start plus a mid-song retrigger at bar 1.",
            "#00054: 01",
            "#00154: 01",
            string.Empty,
            "#00011: 0100000000000000",
            "#00111: 0100000000000000",
            string.Empty
        });
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

    private static Task<GameStateSnapshot> WaitForPerformanceReadyAsync(
        JsonRpcGameClient client,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return Eventually.UntilAsync(
            token => client.GetGameStateAsync(token),
            state => string.Equals(state.StageType, "Performance", StringComparison.Ordinal)
                && state.PerformanceReady,
            timeout,
            TimeSpan.FromMilliseconds(250),
            "Performance ready with prepared audio",
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

            var imageBytes = Convert.FromBase64String(imageData);
            Directory.CreateDirectory(fixture.ArtifactRoot);
            await File.WriteAllBytesAsync(Path.Combine(fixture.ArtifactRoot, fileName), imageBytes, cancellation.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[E2E] Screenshot capture skipped for '{fileName}': {ex.GetType().Name}: {ex.Message}");
        }
    }
}
