using System.Buffers.Binary;
using System.Text.Json;
using DTXMania.Automation.JsonRpc;
using DTXMania.Automation.Process;
using DTXMania.Automation.Support;
using DTXMania.Automation.Telemetry;
using DTXMania.E2E.Fixtures;
using DTXMania.E2E.Support;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using static DTXMania.E2E.Support.E2EStageHelpers;

namespace DTXMania.E2E;

/// <summary>
/// HPA-798 clean-first-run proof: a sandbox that never selected a fixture skin
/// renders the bundled CX Neon <c>Default</c> skin and persists the logical
/// <c>Default</c> token. The only validating skin on disk is the bundled
/// <c>System</c> root copied next to the game by its project file, so rendered
/// Title/Config/Performance screenshots necessarily come from it.
/// </summary>
[Trait("Category", "E2E")]
public sealed class DefaultSkinFirstRunSmokeTests
{
    private const int VirtualWidth = 1280;
    private const int VirtualHeight = 720;

    private static readonly byte[] PngSignature =
        { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };

    private static readonly byte[] PngIEndChunk =
        { 0x00, 0x00, 0x00, 0x00, (byte)'I', (byte)'E', (byte)'N', (byte)'D', 0xAE, 0x42, 0x60, 0x82 };

    [Fact(Timeout = 240_000)]
    public async Task FirstRun_ShouldRenderBundledDefaultSkinAndPersistDefaultToken()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(210));
        var repoRoot = E2EGameLaunch.ResolveRepoRoot();
        var runRoot = Path.Combine(
            Path.GetTempPath(),
            "dtxmaniacx-e2e-defaultskin-" + Guid.NewGuid().ToString("N"));
        var apiPort = E2EGameLaunch.ResolveApiPort();
        var fixture = E2EFixtureBuilder.Build(
            runRoot,
            repoRoot,
            apiPort,
            useBundledDefaultSkin: true,
            // The generated chart runs ~4s; this test needs a wide window to
            // capture a Performance frame on a slow runner without the stage
            // reaching Result first.
            extraChartBars: 8);
        // The CI artifact root is shared by every E2E fixture, whose generic
        // log/config names would overwrite this run's provenance. Scope this
        // test's artifacts to its own subdirectory (uploaded recursively).
        fixture = fixture with
        {
            ArtifactRoot = Path.Combine(fixture.ArtifactRoot, "default-skin")
        };
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

            // Nothing but the bundled System root can provide skin assets: the
            // fixture skin directory was never created and app-data holds no skin.
            Assert.False(Directory.Exists(fixture.SkinRoot));
            Assert.False(File.Exists(Path.Combine(fixture.AppDataRoot, "System", "Theme.ini")));

            // Prove the game actually resolved that bundled root. Without this,
            // missing build-output art would render placeholders and still pass
            // the PNG size checks below.
            var titleState = await client.GetGameStateAsync(cancellation.Token);
            var activeSkinPath = titleState.SkinPath;
            Assert.False(
                string.IsNullOrWhiteSpace(activeSkinPath),
                "Telemetry did not report the active skin path.");
            Assert.True(
                SkinManager.ValidateSkinPath(activeSkinPath!),
                $"The active skin root has no valid skin assets: {activeSkinPath}");
            Assert.Equal(
                "System",
                Path.GetFileName(Path.TrimEndingDirectorySeparator(activeSkinPath!)));
            var appDataPrefix = Path.GetFullPath(fixture.AppDataRoot) + Path.DirectorySeparatorChar;
            Assert.False(
                Path.GetFullPath(activeSkinPath!).StartsWith(
                    appDataPrefix, StringComparison.OrdinalIgnoreCase),
                $"The game fell back to the empty app-data skin root: {activeSkinPath}");

            await CaptureRequiredScreenshotAsync(
                client, fixture, "title-default-skin.png", cancellation.Token);

            // Title menu: Down moves GAME START -> CONFIG, Enter opens it.
            await client.SendKeyAsync("Down", TimeSpan.FromMilliseconds(50), cancellation.Token);
            await Task.Delay(300, cancellation.Token);
            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellation.Token);
            await WaitForStageAsync(client, "Config", TimeSpan.FromSeconds(45), cancellation.Token);
            await Task.Delay(1_000, cancellation.Token);
            await CaptureRequiredScreenshotAsync(
                client, fixture, "config-default-skin.png", cancellation.Token);

            // Back returns to Title through the real Config exit path, then the
            // normal Title -> SongSelect -> Performance journey renders gameplay.
            await client.SendKeyAsync("Escape", TimeSpan.FromMilliseconds(50), cancellation.Token);
            await WaitForStageAsync(client, "Title", TimeSpan.FromSeconds(45), cancellation.Token);
            await Task.Delay(500, cancellation.Token);
            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellation.Token);
            await StartSongViaStatusPanelAsync(client, E2EFixtureBuilder.SongTitle, cancellation.Token);

            await CapturePerformanceScreenshotAsync(
                client, fixture, "performance-default-skin.png", cancellation.Token);

            // Read the raw stored rows before any normalizing reader. ConfigManager
            // .LoadConfig supplies defaults and rewrites the database when values
            // migrate, so using it here could manufacture the very "Default" row this
            // assertion exists to prove. SqliteConfigStore.Load opens read-only.
            var storedEntries = new SqliteConfigStore(fixture.ConfigDatabasePath).Load();
            Assert.True(
                storedEntries.ContainsKey("SkinPath"),
                "The game did not persist a SkinPath row.");
            Assert.Equal(ConfigManager.DefaultSkinPathToken, storedEntries["SkinPath"]);
            Assert.True(
                storedEntries.ContainsKey("SystemSkinRoot"),
                "The game did not persist a SystemSkinRoot row.");
            var storedSystemSkinRoot = storedEntries["SystemSkinRoot"];
            Assert.Equal(
                Path.GetFullPath(Path.Combine(fixture.AppDataRoot, "System")),
                Path.GetFullPath(storedSystemSkinRoot));
            await E2EArtifactWriter.WriteJsonAsync(
                fixture,
                "default-skin-config.json",
                new
                {
                    StoredSkinPath = storedEntries["SkinPath"],
                    StoredSystemSkinRoot = storedSystemSkinRoot,
                    ActiveSkinPath = activeSkinPath,
                    FixtureSkinRootExists = Directory.Exists(fixture.SkinRoot),
                    AppDataThemeIniExists = File.Exists(
                        Path.Combine(fixture.AppDataRoot, "System", "Theme.ini"))
                });
        }
        catch (Exception ex)
        {
            await E2EArtifactWriter.WriteTextAsync(fixture, "failure.txt", ex.ToString());
            await TryWriteScreenshotAsync(client, fixture, "default-skin-failure.png");
            throw;
        }
        finally
        {
            E2EArtifactWriter.CopyFixtureFiles(fixture);
            await E2EArtifactWriter.WriteTextAsync(fixture, "game-stdout.log", process.StandardOutput);
            await E2EArtifactWriter.WriteTextAsync(fixture, "game-stderr.log", process.StandardError);
        }
    }

    /// <summary>
    /// Captures the gameplay evidence only while Performance is actually current.
    /// AutoPlay can finish the fixture chart and reach Result while a delayed CI
    /// continuation is pending, and a Result frame would pass every PNG assertion.
    /// The state is therefore rechecked immediately before and after the capture.
    /// </summary>
    private static async Task CapturePerformanceScreenshotAsync(
        JsonRpcGameClient client,
        E2EFixture fixture,
        string fileName,
        CancellationToken cancellationToken)
    {
        await Eventually.UntilAsync(
            token => client.GetGameStateAsync(token),
            IsPerformanceCaptureState,
            TimeSpan.FromSeconds(60),
            TimeSpan.FromMilliseconds(250),
            "Performance ready with the bundled Default skin and no pending transition",
            cancellationToken);
        await Task.Delay(500, cancellationToken);

        AssertPerformanceCaptureState(
            await client.GetGameStateAsync(cancellationToken),
            "before capture");
        await CaptureRequiredScreenshotAsync(client, fixture, fileName, cancellationToken);
        AssertPerformanceCaptureState(
            await client.GetGameStateAsync(cancellationToken),
            "after capture");
    }

    /// <summary>
    /// True only when the game is in Performance, has prepared audio, and is not
    /// transitioning — i.e. a frame captured now is genuinely gameplay.
    /// </summary>
    internal static bool IsPerformanceCaptureState(GameStateSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return string.Equals(state.StageType, "Performance", StringComparison.Ordinal)
            && state.PerformanceReady
            && !IsTransitioning(state);
    }

    private static void AssertPerformanceCaptureState(GameStateSnapshot state, string phase)
    {
        Assert.True(
            IsPerformanceCaptureState(state),
            $"Refusing {phase} gameplay evidence: expected Performance (ready, not transitioning), " +
            $"observed stage='{state.StageType}', performanceReady={state.PerformanceReady}, " +
            $"isTransitioning={IsTransitioning(state)}, stagePhase='{GetStagePhase(state)}'.");
    }

    private static bool IsTransitioning(GameStateSnapshot state)
    {
        return state.Telemetry.TryGetProperty("isTransitioning", out var value)
            && value.ValueKind == JsonValueKind.True;
    }

    private static string GetStagePhase(GameStateSnapshot state)
    {
        return state.Telemetry.TryGetProperty("stagePhase", out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "unknown"
            : "unknown";
    }

    /// <summary>
    /// Captures a stage screenshot that is required evidence: a skipped capture,
    /// an empty frame or a truncated image fails the run.
    /// </summary>
    private static async Task CaptureRequiredScreenshotAsync(
        JsonRpcGameClient client,
        E2EFixture fixture,
        string fileName,
        CancellationToken cancellationToken)
    {
        var path = await SaveScreenshotAsync(client, fixture, fileName, cancellationToken);
        Assert.True(path is not null, $"takeScreenshot returned no image data for {fileName}.");

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        // A solid-colour 1280x720 frame compresses to a couple of KB; rendered
        // stage art is far larger. The exact threshold is deliberately loose.
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

}

/// <summary>
/// Fast E2E-Support coverage for the gameplay capture guard: the graphical
/// smoke must refuse a Performance screenshot when the stage already advanced
/// (the AutoPlay chart can reach Result while a delayed test continuation is
/// still pending).
/// </summary>
[Trait("Category", "E2E-Support")]
public sealed class DefaultSkinFirstRunSmokeTestsSupport
{
    [Fact]
    public void IsPerformanceCaptureState_WhenReadyWithoutTransition_ShouldAccept()
    {
        Assert.True(DefaultSkinFirstRunSmokeTests.IsPerformanceCaptureState(
            Snapshot("Performance", performanceReady: true, isTransitioning: false)));
    }

    [Fact]
    public void IsPerformanceCaptureState_WhenTransitioning_ShouldReject()
    {
        Assert.False(DefaultSkinFirstRunSmokeTests.IsPerformanceCaptureState(
            Snapshot("Performance", performanceReady: true, isTransitioning: true)));
    }

    [Fact]
    public void IsPerformanceCaptureState_WhenStageAdvancedToResult_ShouldReject()
    {
        Assert.False(DefaultSkinFirstRunSmokeTests.IsPerformanceCaptureState(
            Snapshot("Result", performanceReady: false, isTransitioning: false)));
    }

    [Fact]
    public void IsPerformanceCaptureState_WhenPerformanceNotReady_ShouldReject()
    {
        Assert.False(DefaultSkinFirstRunSmokeTests.IsPerformanceCaptureState(
            Snapshot("Performance", performanceReady: false, isTransitioning: false)));
    }

    private static GameStateSnapshot Snapshot(
        string stageType,
        bool performanceReady,
        bool isTransitioning)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            stageType,
            performanceReady,
            isTransitioning
        }));
        return new GameStateSnapshot
        {
            CustomData = new Dictionary<string, JsonElement>
            {
                ["telemetry"] = document.RootElement.Clone()
            }
        };
    }
}
