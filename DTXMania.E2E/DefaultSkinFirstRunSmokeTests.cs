using System.Text.Json;
using DTXMania.Automation.JsonRpc;
using DTXMania.Automation.Process;
using DTXMania.Automation.Support;
using DTXMania.Automation.Telemetry;
using DTXMania.E2E.Fixtures;
using DTXMania.E2E.Support;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using static DTXMania.E2E.Support.E2EStageHelpers;

namespace DTXMania.E2E;

/// <summary>
/// HPA-798 clean-first-run proof: a sandbox that never selected a fixture skin
/// renders the bundled CX Neon <c>Default</c> skin and persists the logical
/// <c>Default</c> token. The only validating skin on disk is the bundled
/// <c>System</c> root copied next to the game by its project file, so rendered
/// Title/Config/Performance screenshots necessarily come from it. The same
/// journey supplies six of the seven HPA-797 tour screenshots.
/// </summary>
[Trait("Category", "E2E")]
public sealed class DefaultSkinFirstRunSmokeTests
{
    [Fact(Timeout = 300_000)]
    public async Task FirstRun_ShouldRenderBundledDefaultSkinAndPersistDefaultToken()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(270));
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
            // Repeat the drum pattern past bar 1 so the Lanes capture keeps
            // unjudged notes for ~8 extra seconds on slow runners, then add
            // extraChartBars of BGM tail for Result completion headroom.
            drumNoteTailBars: 4,
            extraChartBars: 4);
        // The CI artifact root is shared by every E2E fixture, whose generic
        // log/config names would overwrite this run's provenance. Scope this
        // test's artifacts to its own subdirectory (uploaded recursively).
        fixture = fixture with
        {
            ArtifactRoot = Path.Combine(fixture.ArtifactRoot, "default-skin")
        };
        // Only the required tour PNGs go to the shared sibling folder; logs,
        // fixture copies and failure screenshots stay in this test's root.
        var tourRoot = ResolveTourArtifactRoot(fixture);
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
            AssertBundledSystemSkin(fixture, activeSkinPath);

            await CaptureRequiredScreenshotAsync(
                client, fixture, "01-title.png", cancellation.Token, tourRoot);

            // Title menu: Down moves GAME START -> CONFIG, Enter opens it.
            await client.SendKeyAsync("Down", TimeSpan.FromMilliseconds(50), cancellation.Token);
            await Task.Delay(300, cancellation.Token);
            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellation.Token);
            await WaitForStageAsync(client, "Config", TimeSpan.FromSeconds(45), cancellation.Token);
            await Task.Delay(1_000, cancellation.Token);
            await CaptureRequiredScreenshotAsync(
                client, fixture, "02-config.png", cancellation.Token, tourRoot);

            // Jump to the drum-mapping stage through the API (the menu wiring is
            // unit-tested), then wait out the fade so the capture is not a
            // half-transitioned frame.
            await client.ChangeStageAsync("DrumConfig", cancellation.Token);
            await WaitForSettledStageAsync(
                client, "DrumConfig", TimeSpan.FromSeconds(45), cancellation.Token);
            await CaptureRequiredScreenshotAsync(
                client, fixture, "03-drum-config.png", cancellation.Token, tourRoot);

            // Back exits DrumConfig and returns to Config, flushing any deferred
            // binding save on the way out.
            await client.SendKeyAsync("Escape", TimeSpan.FromMilliseconds(50), cancellation.Token);
            await WaitForSettledStageAsync(
                client, "Config", TimeSpan.FromSeconds(45), cancellation.Token);

            // Back returns to Title through the real Config exit path, then the
            // normal Title -> SongSelect -> Performance journey renders gameplay.
            await client.SendKeyAsync("Escape", TimeSpan.FromMilliseconds(50), cancellation.Token);
            await WaitForStageAsync(client, "Title", TimeSpan.FromSeconds(45), cancellation.Token);
            await Task.Delay(500, cancellation.Token);
            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellation.Token);
            await WaitForSettledStageAsync(
                client, "SongSelect", TimeSpan.FromSeconds(45), cancellation.Token);
            // SongSelect publishes its stage type before the async song list has
            // selected a node; wait for the real selection so the capture shows
            // the populated list rather than an empty one.
            await Eventually.UntilAsync(
                token => client.GetGameStateAsync(token),
                state => string.Equals(state.StageType, "SongSelect", StringComparison.Ordinal)
                    && !state.InStatusPanel
                    && string.Equals(
                        state.SelectedSongTitle,
                        E2EFixtureBuilder.SongTitle,
                        StringComparison.Ordinal),
                TimeSpan.FromSeconds(45),
                TimeSpan.FromMilliseconds(250),
                $"SongSelect showing '{E2EFixtureBuilder.SongTitle}' before the status panel",
                cancellation.Token);
            await CaptureRequiredScreenshotAsync(
                client, fixture, "04-song-select.png", cancellation.Token, tourRoot);

            await StartSongViaStatusPanelAsync(client, E2EFixtureBuilder.SongTitle, cancellation.Token);

            await CaptureLanesScreenshotAsync(
                client,
                fixture,
                "05-lanes-performance.png",
                tourRoot,
                cancellation.Token);

            // AutoPlay finishes the extended fixture chart. StageCompleted is not
            // screenshot readiness: the game writes result-*.png only after the
            // Result reveal completes, so that file is polled independently.
            var resultState = await WaitForCompletedResultAsync(client, cancellation.Token);
            Assert.Equal(E2EFixtureBuilder.SongTitle, resultState.SelectedSongTitle);
            Assert.True(resultState.TotalNotes > 0, "Expected generated chart to contain notes.");
            Assert.Equal(resultState.TotalNotes, resultState.TotalJudgements);
            var gameResultScreenshotPath = await WaitForResultScreenshotAsync(
                fixture, cancellation.Token);
            await CopyValidatedPngAsync(
                gameResultScreenshotPath, "06-result.png", tourRoot, cancellation.Token);

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
    /// Captures the Lanes tour frame only while the run is genuinely mid-song in
    /// effective Lanes notation. PerformanceReady alone is also true during the
    /// READY count-in, and AutoPlay can finish the chart and reach Result while
    /// a delayed CI continuation is pending, so the accepted state is rechecked
    /// immediately before and after the capture.
    /// </summary>
    private static async Task CaptureLanesScreenshotAsync(
        JsonRpcGameClient client,
        E2EFixture fixture,
        string fileName,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        await CaptureMidSongScreenshotAsync(
            client,
            fixture,
            fileName,
            destinationDirectory,
            nameof(DrumsNoteDisplayStyle.Lanes),
            minSongTimeMs: 1_000,
            TimeSpan.FromSeconds(90),
            TimeSpan.FromMilliseconds(250),
            "mid-song Lanes gameplay with unjudged notes and no pending transition",
            cancellationToken,
            requireAutoPlay: false);
    }

    /// <summary>
    /// True only when the game is mid-song in effective Lanes notation with
    /// chart notes still unjudged: a frame captured now is genuine gameplay
    /// with visible notes, not READY or the completed Result.
    /// </summary>
    internal static bool IsLanesCaptureState(GameStateSnapshot state) =>
        IsMidSongCaptureState(
            state,
            nameof(DrumsNoteDisplayStyle.Lanes),
            minSongTimeMs: 1_000,
            requireAutoPlay: false);
}

/// <summary>
/// Fast E2E-Support coverage for the gameplay capture guard: the graphical
/// smoke must refuse a Lanes screenshot when the run is not genuinely mid-song
/// in effective Lanes notation (READY, Tab fallback, the completed Result, or a
/// pending transition would all otherwise pass every PNG assertion).
/// </summary>
[Trait("Category", "E2E-Support")]
public sealed class DefaultSkinFirstRunSmokeTestsSupport
{
    [Fact]
    public void IsLanesCaptureState_WhenMidSongLanesWithUnjudgedNotes_ShouldAccept()
    {
        Assert.True(DefaultSkinFirstRunSmokeTests.IsLanesCaptureState(
            Snapshot("Performance", performanceReady: true, isTransitioning: false)));
    }

    [Fact]
    public void IsLanesCaptureState_WhenTabNotationEffective_ShouldReject()
    {
        Assert.False(DefaultSkinFirstRunSmokeTests.IsLanesCaptureState(
            Snapshot(
                "Performance",
                performanceReady: true,
                isTransitioning: false,
                noteDisplayStyle: "Tab")));
    }

    [Fact]
    public void IsLanesCaptureState_WhenSongClockNotStarted_ShouldReject()
    {
        Assert.False(DefaultSkinFirstRunSmokeTests.IsLanesCaptureState(
            Snapshot(
                "Performance",
                performanceReady: true,
                isTransitioning: false,
                currentSongTimeMs: 0)));
    }

    [Fact]
    public void IsLanesCaptureState_WhenEveryNoteAlreadyJudged_ShouldReject()
    {
        Assert.False(DefaultSkinFirstRunSmokeTests.IsLanesCaptureState(
            Snapshot(
                "Performance",
                performanceReady: true,
                isTransitioning: false,
                totalNotes: 14,
                totalJudgements: 14)));
    }

    [Fact]
    public void IsLanesCaptureState_WhenTransitioning_ShouldReject()
    {
        Assert.False(DefaultSkinFirstRunSmokeTests.IsLanesCaptureState(
            Snapshot("Performance", performanceReady: true, isTransitioning: true)));
    }

    [Fact]
    public void IsLanesCaptureState_WhenStageAdvancedToResult_ShouldReject()
    {
        Assert.False(DefaultSkinFirstRunSmokeTests.IsLanesCaptureState(
            Snapshot("Result", performanceReady: false, isTransitioning: false)));
    }

    [Fact]
    public void IsLanesCaptureState_WhenPerformanceNotReady_ShouldReject()
    {
        Assert.False(DefaultSkinFirstRunSmokeTests.IsLanesCaptureState(
            Snapshot("Performance", performanceReady: false, isTransitioning: false)));
    }

    [Fact]
    public void AssertBundledSystemSkin_WhenAppDataOrSandboxSystem_ShouldReject()
    {
        var root = Path.Combine(Path.GetTempPath(), "dtx-skin-guard-" + Guid.NewGuid().ToString("N"));
        var externalRoot = Path.Combine(Path.GetTempPath(), "dtx-bundled-skin-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fixture = E2EFixtureBuilder.Build(root, Directory.GetCurrentDirectory(), apiPort: 18080);

            var appDataSystem = Path.Combine(fixture.AppDataRoot, "System");
            Directory.CreateDirectory(Path.Combine(appDataSystem, "Graphics"));
            File.WriteAllText(Path.Combine(appDataSystem, "Graphics", "1_background.jpg"), "fake");

            File.WriteAllText(Path.Combine(fixture.SkinRoot, "Graphics", "1_background.jpg"), "fake");

            Assert.ThrowsAny<Exception>(() =>
                AssertBundledSystemSkin(fixture, appDataSystem));
            Assert.ThrowsAny<Exception>(() =>
                AssertBundledSystemSkin(fixture, fixture.SkinRoot));

            var bundledRoot = Path.Combine(externalRoot, "System");
            Directory.CreateDirectory(Path.Combine(bundledRoot, "Graphics"));
            File.WriteAllText(Path.Combine(bundledRoot, "Graphics", "1_background.jpg"), "fake");

            AssertBundledSystemSkin(fixture, bundledRoot);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
            if (Directory.Exists(externalRoot))
                Directory.Delete(externalRoot, recursive: true);
        }
    }

    private static GameStateSnapshot Snapshot(
        string stageType,
        bool performanceReady,
        bool isTransitioning,
        string noteDisplayStyle = "Lanes",
        int totalNotes = 14,
        int totalJudgements = 3,
        double currentSongTimeMs = 2_000) =>
        CreateTelemetrySnapshot(
            stageType,
            performanceReady,
            isTransitioning,
            noteDisplayStyle,
            totalNotes,
            totalJudgements,
            currentSongTimeMs);
}
