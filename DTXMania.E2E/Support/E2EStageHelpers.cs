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
