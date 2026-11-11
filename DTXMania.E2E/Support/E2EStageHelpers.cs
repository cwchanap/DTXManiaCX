using DTXMania.Automation.JsonRpc;
using DTXMania.Automation.Support;
using DTXMania.Automation.Telemetry;
using DTXMania.E2E.Fixtures;

namespace DTXMania.E2E.Support;

/// <summary>
/// Shared stage polling and screenshot helpers for the out-of-process E2E
/// smoke tests.
/// </summary>
internal static class E2EStageHelpers
{
    internal static async Task<GameStateSnapshot> WaitForStageAsync(
        JsonRpcGameClient client,
        string expectedStageType,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        int pollIntervalMilliseconds = 500)
    {
        return await Eventually.UntilAsync(
            token => client.GetGameStateAsync(token),
            state => string.Equals(state.StageType, expectedStageType, StringComparison.Ordinal),
            timeout,
            TimeSpan.FromMilliseconds(pollIntervalMilliseconds),
            expectedStageType,
            cancellationToken);
    }

    /// <summary>
    /// Starts the highlighted song through the status-panel flow: the first
    /// Enter opens the panel, the second starts the song. Each press is gated
    /// on telemetry instead of a fixed delay. SongSelect reports its stage
    /// type as soon as the transition completes, but the selected node can
    /// still be unset while the async song-list publication is applied on the
    /// update thread — HandleActivateInput silently swallows Activate in that
    /// window, which is how a bare Enter;delay;Enter sequence can leave the
    /// game sitting in SongSelect with the panel half-opened.
    /// </summary>
    internal static async Task StartSongViaStatusPanelAsync(
        JsonRpcGameClient client,
        string expectedSongTitle,
        CancellationToken cancellationToken)
    {
        await Eventually.UntilAsync(
            token => client.GetGameStateAsync(token),
            state => string.Equals(state.StageType, "SongSelect", StringComparison.Ordinal)
                && string.Equals(state.SelectedSongTitle, expectedSongTitle, StringComparison.Ordinal),
            TimeSpan.FromSeconds(45),
            TimeSpan.FromMilliseconds(250),
            $"SongSelect with '{expectedSongTitle}' selected",
            cancellationToken);

        await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellationToken);

        await Eventually.UntilAsync(
            token => client.GetGameStateAsync(token),
            state => string.Equals(state.StageType, "SongSelect", StringComparison.Ordinal)
                && state.InStatusPanel
                && !state.IsTransitioning,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(100),
            "song status panel open",
            cancellationToken);

        // The panel gate proves the first Enter landed. The remaining delay
        // covers the 0.5s stage-transition debounce counted from the Title ->
        // SongSelect mark, so SelectSong cannot reject the second Enter.
        await Task.Delay(500, cancellationToken);
        await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellationToken);
    }

    /// <summary>
    /// Captures a screenshot and returns its artifact path, or null when the
    /// game returned no image. Transport/RPC failures propagate so a missing
    /// required artifact keeps its real cause.
    /// </summary>
    internal static async Task<string?> SaveScreenshotAsync(
        JsonRpcGameClient client,
        E2EFixture fixture,
        string fileName,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);
        var imageData = await client.TakeScreenshotBase64Async(cancellation.Token);
        if (string.IsNullOrWhiteSpace(imageData))
            return null;

        var imageBytes = Convert.FromBase64String(imageData);
        Directory.CreateDirectory(fixture.ArtifactRoot);
        var screenshotPath = Path.Combine(fixture.ArtifactRoot, fileName);
        await File.WriteAllBytesAsync(screenshotPath, imageBytes, cancellation.Token);
        return screenshotPath;
    }

    /// <summary>
    /// Best-effort failure artifact: never hides the original assertion or
    /// launch error.
    /// </summary>
    internal static async Task TryWriteScreenshotAsync(
        JsonRpcGameClient client,
        E2EFixture fixture,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SaveScreenshotAsync(client, fixture, fileName, cancellationToken);
        }
        catch
        {
            // Failure artifacts must never hide the original E2E assertion.
        }
    }
}
