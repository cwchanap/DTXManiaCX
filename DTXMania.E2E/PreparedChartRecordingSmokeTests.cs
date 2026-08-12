using DTXMania.Automation.JsonRpc;
using DTXMania.Automation.Process;
using DTXMania.Automation.Support;
using DTXMania.Automation.Telemetry;
using DTXMania.E2E.Fixtures;
using DTXMania.E2E.Support;

namespace DTXMania.E2E;

[Trait("Category", "AudioE2E")]
public sealed class PreparedChartRecordingSmokeTests
{
    [Fact(Timeout = 180_000)]
    public async Task PreparedChartRecording_ShouldHoldPreviewUntilStartedAndActivateThroughSongTransition()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(165));
        var repoRoot = E2EGameLaunch.ResolveRepoRoot();
        var runRoot = Path.Combine(
            Path.GetTempPath(),
            "dtxmaniacx-e2e-prepared-chart-" + Guid.NewGuid().ToString("N"));
        var fixture = E2EFixtureBuilder.Build(
            runRoot,
            repoRoot,
            E2EGameLaunch.ResolveApiPort());

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

            var songSelect = await Eventually.UntilAsync(
                token => client.GetGameStateAsync(token),
                state => string.Equals(state.StageType, "SongSelect", StringComparison.Ordinal)
                    && string.Equals(
                        state.SelectedSongTitle,
                        E2EFixtureBuilder.SongTitle,
                        StringComparison.Ordinal),
                TimeSpan.FromSeconds(45),
                TimeSpan.FromMilliseconds(250),
                "SongSelect fixture chart",
                cancellation.Token);
            Assert.Equal(E2EFixtureBuilder.SongTitle, songSelect.SelectedSongTitle);

            await client.PrepareVideoChartAsync(fixture.ChartPath, cancellation.Token);

            var screenshotBase64 = await client.TakeScreenshotBase64Async(cancellation.Token);
            Assert.False(
                string.IsNullOrWhiteSpace(screenshotBase64),
                "Prepared SongSelect screenshot should contain image data.");
            var screenshotBytes = Convert.FromBase64String(screenshotBase64!);
            Assert.NotEmpty(screenshotBytes);
            Directory.CreateDirectory(fixture.ArtifactRoot);
            await File.WriteAllBytesAsync(
                Path.Combine(fixture.ArtifactRoot, "prepared-song-select.png"),
                screenshotBytes,
                cancellation.Token);

            // The generated fixture uses the normal one-second automatic preview delay.
            // Waiting past it proves preparation does not arm the interactive auto-start path.
            await Task.Delay(TimeSpan.FromSeconds(2), cancellation.Token);
            var prepared = await Eventually.UntilAsync(
                token => client.GetGameStateAsync(token),
                state => string.Equals(state.StageType, "SongSelect", StringComparison.Ordinal)
                    && string.Equals(state.PreparedPreviewState, "Prepared", StringComparison.Ordinal)
                    && state.PreparedPreviewElapsedMs == 0.0,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(250),
                "prepared preview remains stopped",
                cancellation.Token);
            Assert.Equal("Prepared", prepared.PreparedPreviewState);
            Assert.Equal(0.0, prepared.PreparedPreviewElapsedMs);
            Assert.False(string.IsNullOrWhiteSpace(prepared.PreparedChartIdentity));
            Assert.DoesNotContain(
                Path.GetFullPath(fixture.ChartPath),
                prepared.PreparedChartIdentity,
                StringComparison.OrdinalIgnoreCase);
            await E2EArtifactWriter.WriteJsonAsync(fixture, "prepared-state.json", prepared);

            await client.StartPreparedPreviewAsync(cancellation.Token);
            var playing = await Eventually.UntilAsync(
                token => client.GetGameStateAsync(token),
                state => string.Equals(state.StageType, "SongSelect", StringComparison.Ordinal)
                    && string.Equals(state.PreparedPreviewState, "Playing", StringComparison.Ordinal)
                    && state.PreparedPreviewElapsedMs >= 10_000.0,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMilliseconds(250),
                "prepared preview reaches ten seconds",
                cancellation.Token);
            Assert.Equal("Playing", playing.PreparedPreviewState);
            Assert.True(playing.PreparedPreviewElapsedMs >= 10_000.0);
            await E2EArtifactWriter.WriteJsonAsync(fixture, "playing-state.json", playing);

            await client.ActivatePreparedChartAsync(cancellation.Token);
            var transition = await WaitForStageAsync(
                client,
                "SongTransition",
                TimeSpan.FromSeconds(5),
                cancellation.Token);
            Assert.Equal("SongTransition", transition.StageType);
            await E2EArtifactWriter.WriteJsonAsync(fixture, "song-transition-state.json", transition);

            var performance = await WaitForStageAsync(
                client,
                "Performance",
                TimeSpan.FromSeconds(15),
                cancellation.Token);
            Assert.Equal("Performance", performance.StageType);
            await E2EArtifactWriter.WriteJsonAsync(fixture, "performance-state.json", performance);
        }
        catch (Exception exception)
        {
            await E2EArtifactWriter.WriteTextAsync(fixture, "failure.txt", exception.ToString());
            try
            {
                var failureState = await client.GetGameStateAsync(CancellationToken.None);
                await E2EArtifactWriter.WriteJsonAsync(fixture, "failure-state.json", failureState);
            }
            catch
            {
                // Preserve the original failure if the game has already exited.
            }

            throw;
        }
        finally
        {
            E2EArtifactWriter.CopyFixtureFiles(fixture);
            await E2EArtifactWriter.WriteTextAsync(fixture, "game-stdout.log", process.StandardOutput);
            await E2EArtifactWriter.WriteTextAsync(fixture, "game-stderr.log", process.StandardError);
        }
    }

    private static Task<GameStateSnapshot> WaitForStageAsync(
        JsonRpcGameClient client,
        string expectedStageType,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        Eventually.UntilAsync(
            token => client.GetGameStateAsync(token),
            state => string.Equals(state.StageType, expectedStageType, StringComparison.Ordinal),
            timeout,
            TimeSpan.FromMilliseconds(100),
            expectedStageType,
            cancellationToken);
}