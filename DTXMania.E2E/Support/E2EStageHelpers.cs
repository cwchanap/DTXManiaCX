using System.Buffers.Binary;
using System.Text.Json;
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
    private const string TourArtifactFolderName = "hpa-797-tour";

    private const int VirtualWidth = 1280;
    private const int VirtualHeight = 720;

    private static readonly byte[] PngSignature =
        { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };

    private static readonly byte[] PngIEndChunk =
        { 0x00, 0x00, 0x00, 0x00, (byte)'I', (byte)'E', (byte)'N', (byte)'D', 0xAE, 0x42, 0x60, 0x82 };

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
    /// Waits until the stage is current, its transition has finished and it is
    /// in its normal phase. Stage type alone can be reported while a fade is
    /// still running, which would capture a half-faded frame.
    /// </summary>
    internal static async Task<GameStateSnapshot> WaitForSettledStageAsync(
        JsonRpcGameClient client,
        string expectedStageType,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await Eventually.UntilAsync(
            token => client.GetGameStateAsync(token),
            state =>
                string.Equals(state.StageType, expectedStageType, StringComparison.Ordinal)
                && !state.IsTransitioning
                && string.Equals(GetStagePhase(state), "Normal", StringComparison.Ordinal),
            timeout,
            TimeSpan.FromMilliseconds(250),
            $"{expectedStageType} active with its transition finished",
            cancellationToken);
    }

    internal static string GetStagePhase(GameStateSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Telemetry.TryGetProperty("stagePhase", out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "unknown"
            : "unknown";
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
    /// Waits for the completed Result stage. StageCompleted is NOT screenshot
    /// readiness: it is published as soon as Result has a PerformanceSummary,
    /// roughly 1.15 seconds before the reveal completes on an un-fast-forwarded
    /// result. Callers awaiting a game-owned screenshot file poll for it with
    /// <see cref="WaitForResultScreenshotAsync"/>; this wait exists for journey
    /// completion only.
    /// </summary>
    internal static async Task<GameStateSnapshot> WaitForCompletedResultAsync(
        JsonRpcGameClient client,
        CancellationToken cancellationToken)
    {
        await WaitForStageAsync(client, "Result", TimeSpan.FromSeconds(120), cancellationToken);
        return await Eventually.UntilAsync(
            token => client.GetGameStateAsync(token),
            state =>
                string.Equals(state.StageType, "Result", StringComparison.Ordinal) &&
                state.StageCompleted,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(250),
            "Result stage completion",
            cancellationToken);
    }

    /// <summary>
    /// Waits for the game-written automatic Result screenshot under the
    /// fixture's temp appdata Screenshots root. The production path writes the
    /// PNG with <c>File.WriteAllBytesAsync</c>, which opens with
    /// <c>FileShare.Read</c>, so a concurrent reader can observe an empty or
    /// partial file without throwing. The signature only proves it is a PNG;
    /// the trailing IEND chunk (length 0 + type "IEND" + its constant CRC)
    /// proves the write reached the end of the encoded image. Requiring exactly
    /// one file keeps a stale earlier result from passing.
    /// </summary>
    internal static async Task<string> WaitForResultScreenshotAsync(
        E2EFixture fixture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var screenshotsRoot = Path.Combine(fixture.AppDataRoot, "Screenshots");
        var screenshotPath = await Eventually.UntilAsync(
            async token =>
            {
                if (!Directory.Exists(screenshotsRoot))
                    return (string?)null;

                var paths = Directory.EnumerateFiles(screenshotsRoot, "result-*.png").ToList();
                if (paths.Count != 1)
                    return (string?)null;

                var path = paths[0];
                try
                {
                    var bytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
                    if (bytes.Length < PngSignature.Length + PngIEndChunk.Length)
                        return (string?)null;
                    if (!bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
                        return (string?)null;
                    if (!bytes.AsSpan(bytes.Length - PngIEndChunk.Length).SequenceEqual(PngIEndChunk))
                        return (string?)null;
                    return path;
                }
                catch (IOException)
                {
                    // Write still in progress / file locked: treat as
                    // not-yet-ready and let Eventually retry.
                    return (string?)null;
                }
            },
            path => path is not null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(250),
            "exactly one readable automatic result screenshot that is a complete PNG (signature + trailing IEND chunk) under the game-owned Screenshots root",
            cancellationToken);

        Assert.NotNull(screenshotPath);
        return screenshotPath!;
    }

    /// <summary>
    /// Shared HPA-797 screenshot-tour folder: a sibling of each test's fixture
    /// artifact root. Per-test roots stay isolated for logs and fixture
    /// evidence; only the seven required tour PNGs are written here.
    /// </summary>
    internal static string ResolveTourArtifactRoot(E2EFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(fixture.ArtifactRoot));
        if (string.IsNullOrEmpty(parent))
        {
            throw new InvalidOperationException(
                $"Fixture artifact root has no parent directory: {fixture.ArtifactRoot}");
        }

        return Path.Combine(parent, TourArtifactFolderName);
    }

    /// <summary>
    /// Captures a stage screenshot that is required evidence: a skipped capture,
    /// an empty frame or a truncated image fails the run. An explicit
    /// destination directory routes the file outside the fixture's own root.
    /// </summary>
    internal static async Task CaptureRequiredScreenshotAsync(
        JsonRpcGameClient client,
        E2EFixture fixture,
        string fileName,
        CancellationToken cancellationToken,
        string? destinationDirectory = null)
    {
        var path = await SaveScreenshotAsync(
            client, fixture, fileName, cancellationToken, destinationDirectory);
        Assert.True(path is not null, $"takeScreenshot returned no image data for {fileName}.");
        await AssertCompletePngAsync(path!, fileName, cancellationToken);
    }

    /// <summary>
    /// A solid-colour 1280x720 frame compresses to a couple of KB; rendered
    /// stage art is far larger, so the size floor stays deliberately loose.
    /// The dimension read assumes an IHDR at the canonical PNG offset.
    /// </summary>
    internal static async Task AssertCompletePngAsync(
        string path,
        string fileName,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        Assert.True(
            bytes.Length > 8 * 1024,
            $"{fileName} should contain rendered stage content ({bytes.Length} bytes).");
        Assert.True(
            bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature),
            $"{fileName} must be a PNG.");
        Assert.Equal(VirtualWidth, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
        Assert.Equal(VirtualHeight, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
        Assert.True(
            bytes.AsSpan(bytes.Length - PngIEndChunk.Length).SequenceEqual(PngIEndChunk),
            $"{fileName} must be a complete PNG (trailing IEND chunk).");
    }

    /// <summary>
    /// Copies a locally validated PNG to the shared tour folder and revalidates
    /// the copy, so a partial read of a game-written file cannot be published.
    /// </summary>
    internal static async Task CopyValidatedPngAsync(
        string sourcePath,
        string fileName,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        Directory.CreateDirectory(destinationDirectory);
        var destinationPath = Path.Combine(destinationDirectory, fileName);
        File.Copy(sourcePath, destinationPath, overwrite: true);
        await AssertCompletePngAsync(destinationPath, fileName, cancellationToken);
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
        CancellationToken cancellationToken,
        string? destinationDirectory = null)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);
        var imageData = await client.TakeScreenshotBase64Async(cancellation.Token);
        if (string.IsNullOrWhiteSpace(imageData))
            return null;

        var imageBytes = Convert.FromBase64String(imageData);
        var directory = destinationDirectory ?? fixture.ArtifactRoot;
        Directory.CreateDirectory(directory);
        var screenshotPath = Path.Combine(directory, fileName);
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
