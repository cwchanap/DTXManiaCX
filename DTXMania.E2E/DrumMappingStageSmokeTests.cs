using DTXMania.Automation.JsonRpc;
using DTXMania.Automation.Process;
using DTXMania.Automation.Support;
using DTXMania.Automation.Telemetry;
using DTXMania.E2E.Fixtures;
using DTXMania.E2E.Support;

namespace DTXMania.E2E;

/// <summary>
/// Black-box smoke for the visual drum-mapping stage (<c>StageType.DrumConfig</c>): navigate to
/// the stage, open a piece's capture popup, hit a key to bind it, then Back (= save &amp; exit),
/// and assert the new binding was persisted to the sandbox <c>config.db</c> (the authoritative
/// SQLite store since HPA-190). This exercises the live render + capture + save round-trip that
/// the headless unit suite structurally cannot reach.
/// </summary>
[Trait("Category", "E2E")]
public sealed class DrumMappingStageSmokeTests
{
    // Authored visual zone 0 is the Hi-Hat (lane 5). We append "Key.Z", a key not otherwise bound
    // and not reserved for navigation, so the capture is accepted (append model).
    private const int DefaultFocusedLane = 5;
    private const string BindKey = "Z";
    private const string ExpectedBindingId = "Key.Z";

    [Fact(Timeout = 180_000)]
    public async Task DrumMapping_BindKeyThenBack_PersistsBindingToConfig()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var repoRoot = E2EGameLaunch.ResolveRepoRoot();
        var runRoot = Path.Combine(Path.GetTempPath(), "dtxmaniacx-e2e-drum-" + Guid.NewGuid().ToString("N"));
        var apiPort = E2EGameLaunch.ResolveApiPort();
        var fixture = E2EFixtureBuilder.Build(runRoot, repoRoot, apiPort);
        await using var bundle = E2EGameLaunch.CreateClientBundle(fixture);
        var process = bundle.Process;
        var client = bundle.Client;

        try
        {
            var startOptions = E2EGameLaunch.CreateOptions(fixture);
            process.Start(startOptions);
            await process.WaitForStartupAsync(
                client.GetHealthAsync,
                TimeSpan.FromSeconds(60),
                TimeSpan.FromMilliseconds(500),
                cancellation.Token);

            // Let the game boot fully before jumping stages.
            await WaitForStageAsync(client, "Title", TimeSpan.FromSeconds(45), cancellation.Token);

            // Jump straight to the drum-mapping stage via the API (the menu wiring itself is
            // covered by unit tests); this keeps the smoke independent of menu ordering.
            await client.ChangeStageAsync("DrumConfig", cancellation.Token);
            await WaitForStageAsync(client, "DrumConfig", TimeSpan.FromSeconds(45), cancellation.Token);
            // The stage reports its type as soon as the transition is queued; this settle covers
            // the fade transition + OnActivate (popup/focus/skip-flag init) before we send input.
            await Task.Delay(500, cancellation.Token);

            // Activate authored visual zone 0 (Hi-Hat / lane 5) to open its capture popup.
            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellation.Token);
            // Let the popup open and the one-frame capture-skip clear before sending the bind key.
            await Task.Delay(700, cancellation.Token);

            // Hit the key to bind — captured and appended to the focused lane.
            await client.SendKeyAsync(BindKey, TimeSpan.FromMilliseconds(50), cancellation.Token);
            // Let the capture register before closing the popup.
            await Task.Delay(700, cancellation.Token);

            // First Back closes the popup (acts as "Done")...
            await client.SendKeyAsync("Escape", TimeSpan.FromMilliseconds(50), cancellation.Token);
            // Let the popup Close() propagate before the second Back triggers flush-and-exit.
            await Task.Delay(700, cancellation.Token);

            // ...second Back exits the stage; the capture was already live-applied to Config,
            // and Back flushes the deferred save on the way out.
            await client.SendKeyAsync("Escape", TimeSpan.FromMilliseconds(50), cancellation.Token);

            // Returning to Config proves the flush-on-exit path ran.
            await WaitForStageAsync(client, "Config", TimeSpan.FromSeconds(45), cancellation.Token);

            // ExitStage flushes the pending save before changing stage, so by the time we are
            // back in Config the authoritative config database already contains the binding.
            // The helper asserts the database exists before loading, so this cannot pass by
            // falling back to (re-importing) the bootstrap INI.
            var persisted = E2EConfigPersistence.LoadPersistedConfig(fixture);
            await E2EArtifactWriter.WriteTextAsync(
                fixture,
                "drum-config-bindings.txt",
                FormatKeyBindings(persisted));

            // Assert the lane too so an entry bound to a different lane can't pass.
            Assert.True(persisted.Config.KeyBindings.TryGetValue(ExpectedBindingId, out var boundLane));
            Assert.Equal(DefaultFocusedLane, boundLane);
        }
        catch (Exception ex)
        {
            await E2EArtifactWriter.WriteTextAsync(fixture, "failure.txt", ex.ToString());
            await TryWriteScreenshotAsync(client, fixture);
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
    /// Black-box smoke for the keyboard-reachable Reset-to-defaults flow on the drum-mapping
    /// stage: bind a non-default key to the default focused zone, advance keyboard focus onto the
    /// Reset action, Activate to reset Config back to defaults (live-applied), then Back (= flush
    /// &amp; exit), and assert the custom binding was NOT persisted (i.e. Reset overwrote it before
    /// the flush). Exercises the live focus + Activate-dispatch + reset + flush round-trip the
    /// headless unit suite cannot reach.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task DrumMapping_ResetViaKeyboard_WipesCustomBindingOnSave()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var repoRoot = E2EGameLaunch.ResolveRepoRoot();
        var runRoot = Path.Combine(Path.GetTempPath(), "dtxmaniacx-e2e-reset-" + Guid.NewGuid().ToString("N"));
        var apiPort = E2EGameLaunch.ResolveApiPort();
        var fixture = E2EFixtureBuilder.Build(runRoot, repoRoot, apiPort);
        await using var bundle = E2EGameLaunch.CreateClientBundle(fixture);
        var process = bundle.Process;
        var client = bundle.Client;

        try
        {
            var startOptions = E2EGameLaunch.CreateOptions(fixture);
            process.Start(startOptions);
            await process.WaitForStartupAsync(
                client.GetHealthAsync,
                TimeSpan.FromSeconds(60),
                TimeSpan.FromMilliseconds(500),
                cancellation.Token);

            await WaitForStageAsync(client, "Title", TimeSpan.FromSeconds(45), cancellation.Token);

            // Jump straight to the drum-mapping stage via the API (menu wiring is unit-tested).
            await client.ChangeStageAsync("DrumConfig", cancellation.Token);
            await WaitForStageAsync(client, "DrumConfig", TimeSpan.FromSeconds(45), cancellation.Token);
            // Settle past the fade + OnActivate (focus/skip-flag init) before sending input.
            await Task.Delay(500, cancellation.Token);

            // Focus starts on authored visual zone 0 (Hi-Hat / lane 5). Open its capture popup
            // and bind the non-default key.
            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellation.Token);
            await Task.Delay(700, cancellation.Token);
            await client.SendKeyAsync(BindKey, TimeSpan.FromMilliseconds(50), cancellation.Token);
            await Task.Delay(700, cancellation.Token);
            // First Back closes the popup (acts as "Done"); focus stays on visual zone 0.
            await client.SendKeyAsync("Escape", TimeSpan.FromMilliseconds(50), cancellation.Token);
            await Task.Delay(700, cancellation.Token);

            // Advance focus through all ten authored visual zones onto the Reset action (index 10).
            // Tab is read via ConsumePressedButtons, which records every injected press event
            // regardless of a same-frame release, and parses server-side via Enum.TryParse<Keys>.
            for (int i = 0; i < 10; i++)
            {
                await client.SendKeyAsync("Tab", TimeSpan.FromMilliseconds(40), cancellation.Token);
                await Task.Delay(60, cancellation.Token);
            }

            // Activate on the focused Reset action live-applies the default bindings to Config.
            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellation.Token);
            await Task.Delay(500, cancellation.Token);

            // Back = flush & exit: persists the (now reset) Config to disk.
            await client.SendKeyAsync("Escape", TimeSpan.FromMilliseconds(50), cancellation.Token);
            await WaitForStageAsync(client, "Config", TimeSpan.FromSeconds(45), cancellation.Token);

            // Reset overwrote the custom binding in Config before the flush, so it must not be
            // in the authoritative config database. The helper asserts the database exists
            // before loading, so this cannot pass by falling back to the bootstrap INI (which
            // also lacks the binding).
            var persisted = E2EConfigPersistence.LoadPersistedConfig(fixture);
            await E2EArtifactWriter.WriteTextAsync(
                fixture,
                "drum-reset-config-bindings.txt",
                FormatKeyBindings(persisted));
            Assert.False(persisted.Config.KeyBindings.TryGetValue(ExpectedBindingId, out _));
        }
        catch (Exception ex)
        {
            await E2EArtifactWriter.WriteTextAsync(fixture, "failure.txt", ex.ToString());
            await TryWriteScreenshotAsync(client, fixture);
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
    /// Serializes the persisted key bindings ("<buttonId>=<lane>" per line, ordinal order) as
    /// the artifact evidence for the database-backed persistence assertions.
    /// </summary>
    private static string FormatKeyBindings(DTXMania.Game.Lib.Config.ConfigManager manager)
    {
        return string.Join('\n', manager.Config.KeyBindings
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => $"{kvp.Key}={kvp.Value}"));
    }

    private static async Task<GameStateSnapshot> WaitForStageAsync(
        JsonRpcGameClient client,
        string expectedStageType,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await Eventually.UntilAsync(
            token => client.GetGameStateAsync(token),
            state => string.Equals(state.StageType, expectedStageType, StringComparison.Ordinal),
            timeout,
            TimeSpan.FromMilliseconds(500),
            expectedStageType,
            cancellationToken);
    }

    private static async Task TryWriteScreenshotAsync(JsonRpcGameClient client, E2EFixture fixture)
    {
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var imageData = await client.TakeScreenshotBase64Async(cancellation.Token);
            if (string.IsNullOrWhiteSpace(imageData))
                return;

            var imageBytes = Convert.FromBase64String(imageData);
            Directory.CreateDirectory(fixture.ArtifactRoot);
            await File.WriteAllBytesAsync(Path.Combine(fixture.ArtifactRoot, "drum-failure-screenshot.png"), imageBytes, cancellation.Token);
        }
        catch
        {
            // Failure artifacts should never hide the original E2E assertion or launch error.
        }
    }

}
