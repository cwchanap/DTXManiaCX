using System.Net;
using System.Net.Sockets;
using DTXMania.E2E.Fixtures;
using DTXMania.E2E.JsonRpc;
using DTXMania.E2E.Process;
using DTXMania.E2E.Support;
using DTXMania.E2E.Telemetry;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.EntityFrameworkCore;

namespace DTXMania.E2E;

[Trait("Category", "E2E")]
public sealed class GameplayAutoPlaySmokeTests
{
    [Fact(Timeout = 420_000)]
    public async Task AutoPlaySmoke_ShouldPersistIndependentSpeedBucketsAndReuseBucketAcrossPitches()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(7));
        var repoRoot = FindRepoRoot();
        var runRoot = Path.Combine(Path.GetTempPath(), "dtxmaniacx-e2e-" + Guid.NewGuid().ToString("N"));
        var projectPath = Environment.GetEnvironmentVariable("DTXMANIA_E2E_GAME_PROJECT")
            ?? (OperatingSystem.IsWindows()
                ? "DTXMania.Game/DTXMania.Game.Windows.csproj"
                : "DTXMania.Game/DTXMania.Game.Mac.csproj");
        var profiles = new[]
        {
            new PlaybackProfile(75, 3, ExpectedBucketPlayCount: 1),
            new PlaybackProfile(125, 0, ExpectedBucketPlayCount: 1),
            new PlaybackProfile(75, -4, ExpectedBucketPlayCount: 2),
        };
        E2EFixture? lastFixture = null;

        try
        {
            for (var profileIndex = 0; profileIndex < profiles.Length; profileIndex++)
            {
                var profile = profiles[profileIndex];
                var apiPort = profileIndex == 0
                    ? GetPortFromEnvironmentOrDefault()
                    : GetAvailablePort();
                var fixture = E2EFixtureBuilder.Build(
                    runRoot,
                    repoRoot,
                    apiPort,
                    profile.PlaySpeedPercent,
                    profile.PitchSemitones);
                lastFixture = fixture;

                await RunProfileAsync(
                    repoRoot,
                    projectPath,
                    fixture,
                    profile,
                    profileIndex,
                    cancellation.Token);
            }

            Assert.NotNull(lastFixture);
            var evidence = await LoadPersistenceEvidenceAsync(lastFixture!, cancellation.Token);
            await E2EArtifactWriter.WriteJsonAsync(lastFixture!, "score-bucket-evidence.json", evidence);

            Assert.Equal(2, evidence.Scores.Count);
            var slowBucket = Assert.Single(
                evidence.Scores,
                score => score.PlaySpeedPercent == 75);
            var fastBucket = Assert.Single(
                evidence.Scores,
                score => score.PlaySpeedPercent == 125);

            Assert.Equal(2, slowBucket.PlayCount);
            Assert.Equal(1, fastBucket.PlayCount);
            Assert.Equal(-4, Assert.Single(
                slowBucket.History,
                history => history.DisplayOrder == 1).PitchSemitones);
            Assert.Contains(slowBucket.History, history => history.PitchSemitones == 3);
            Assert.Contains(slowBucket.History, history => history.PitchSemitones == -4);
            Assert.Contains(fastBucket.History, history => history.PitchSemitones == 0);

            E2EArtifactWriter.CopyFixtureFiles(lastFixture!);
            File.Copy(
                Path.Combine(lastFixture!.AppDataRoot, "songs.db"),
                Path.Combine(lastFixture.ArtifactRoot, "songs.db"),
                overwrite: true);
        }
        catch (Exception ex)
        {
            if (lastFixture != null)
                await E2EArtifactWriter.WriteTextAsync(lastFixture, "failure.txt", ex.ToString());
            throw;
        }
    }

    private static async Task RunProfileAsync(
        string repoRoot,
        string projectPath,
        E2EFixture fixture,
        PlaybackProfile profile,
        int profileIndex,
        CancellationToken cancellationToken)
    {
        var artifactSuffix =
            $"{profileIndex + 1}-{profile.PlaySpeedPercent}-pitch-{profile.PitchSemitones}";
        await E2EArtifactWriter.WriteTextAsync(
            fixture,
            $"config-{artifactSuffix}.ini",
            await File.ReadAllTextAsync(fixture.ConfigPath, cancellationToken));
        await using var process = new GameProcessDriver();
        using var httpClient = new HttpClient(new SocketsHttpHandler { UseCookies = false })
        {
            BaseAddress = fixture.ApiBaseUri,
            Timeout = TimeSpan.FromSeconds(5)
        };
        var client = new JsonRpcGameClient(httpClient, fixture.ApiKey);

        try
        {
            process.Start(repoRoot, projectPath, fixture);

            await Eventually.UntilAsync(
                _ => client.IsHealthyAsync(cancellationToken),
                healthy => healthy,
                TimeSpan.FromSeconds(60),
                TimeSpan.FromMilliseconds(500),
                "JSON-RPC health",
                cancellationToken);

            await WaitForStageAsync(client, "Title", TimeSpan.FromSeconds(45), cancellationToken);
            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellationToken);
            await WaitForStageAsync(client, "SongSelect", TimeSpan.FromSeconds(45), cancellationToken);
            await SaveScreenshotAsync(
                client,
                fixture,
                $"song-select-{artifactSuffix}.png",
                cancellationToken);

            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellationToken);
            await Task.Delay(500, cancellationToken);
            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellationToken);

            await WaitForStageAsync(client, "Performance", TimeSpan.FromSeconds(60), cancellationToken);
            var performanceState = await Eventually.UntilAsync(
                token => client.GetGameStateAsync(token),
                state =>
                    string.Equals(state.StageType, "Performance", StringComparison.Ordinal) &&
                    state.PerformanceReady,
                TimeSpan.FromSeconds(60),
                TimeSpan.FromMilliseconds(250),
                "Performance ready with prepared audio",
                cancellationToken);
            Assert.Equal(profile.PlaySpeedPercent, performanceState.PlaySpeedPercent);
            Assert.Equal(profile.PitchSemitones, performanceState.PitchSemitones);
            Assert.True(performanceState.PlaybackProfileFrozen);
            Assert.True(performanceState.AudioPreparationTotal > 0);
            Assert.Equal(
                performanceState.AudioPreparationTotal,
                performanceState.AudioPreparationCompleted);
            Assert.InRange(
                performanceState.AudioPreparationCacheHits,
                0,
                performanceState.AudioPreparationTotal);
            Assert.True(performanceState.PreparedAudioBytes > 0);
            await WaitForStageAsync(
                client,
                "Result",
                TimeSpan.FromSeconds(120),
                cancellationToken);
            var resultState = await Eventually.UntilAsync(
                token => client.GetGameStateAsync(token),
                state =>
                    string.Equals(state.StageType, "Result", StringComparison.Ordinal) &&
                    string.Equals(state.ScoreSaveStatus, "Saved", StringComparison.Ordinal),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMilliseconds(250),
                "Result score save",
                cancellationToken);
            await E2EArtifactWriter.WriteJsonAsync(
                fixture,
                $"final-state-{artifactSuffix}.json",
                resultState);

            Assert.Equal(E2EFixtureBuilder.SongTitle, resultState.SelectedSongTitle);
            Assert.True(resultState.PlaybackProfileFrozen);
            Assert.Equal(profile.PlaySpeedPercent, resultState.PlaySpeedPercent);
            Assert.Equal(profile.PitchSemitones, resultState.PitchSemitones);
            Assert.True(resultState.StageCompleted);
            Assert.True(resultState.TotalNotes > 0, "Expected generated chart to contain notes.");
            Assert.Equal(resultState.TotalNotes, resultState.TotalJudgements);
            Assert.True(resultState.ClearFlag);
            Assert.True(resultState.Score > 0);
            Assert.Equal("SongComplete", resultState.CompletionReason);
            Assert.Equal("Saved", resultState.ScoreSaveStatus);
            Assert.Null(resultState.ScoreSaveError);

            await Eventually.UntilAsync(
                token => LoadPersistenceEvidenceAsync(fixture, token),
                evidence => evidence.Scores.Any(score =>
                    score.PlaySpeedPercent == profile.PlaySpeedPercent &&
                    score.PlayCount == profile.ExpectedBucketPlayCount &&
                    score.History.Any(history =>
                        history.DisplayOrder == 1 &&
                        history.PitchSemitones == profile.PitchSemitones)),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMilliseconds(250),
                $"score save {profile.PlaySpeedPercent}/{profile.PitchSemitones}",
                cancellationToken);
        }
        catch
        {
            await SaveScreenshotAsync(
                client,
                fixture,
                $"failure-{artifactSuffix}.png",
                CancellationToken.None);
            throw;
        }
        finally
        {
            await E2EArtifactWriter.WriteTextAsync(
                fixture,
                $"game-stdout-{artifactSuffix}.log",
                process.StandardOutput);
            await E2EArtifactWriter.WriteTextAsync(
                fixture,
                $"game-stderr-{artifactSuffix}.log",
                process.StandardError);
        }
    }

    private static async Task<PersistenceEvidence> LoadPersistenceEvidenceAsync(
        E2EFixture fixture,
        CancellationToken cancellationToken)
    {
        var databasePath = Path.Combine(fixture.AppDataRoot, "songs.db");
        if (!File.Exists(databasePath))
            return new PersistenceEvidence([]);

        try
        {
            var options = new DbContextOptionsBuilder<SongDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            await using var database = new SongDbContext(options);
            var scores = await database.SongScores
                .AsNoTracking()
                .Include(score => score.Chart)
                .ThenInclude(chart => chart.Song)
                .Where(score => score.Chart.Song.Title == E2EFixtureBuilder.SongTitle)
                // Song discovery materializes an unplayed 1.00x metadata slot. The
                // persistence proof is about aggregates created by completed runs.
                .Where(score => score.PlayCount > 0)
                .OrderBy(score => score.PlaySpeedPercent)
                .ToListAsync(cancellationToken);
            var scoreIds = scores.Select(score => score.Id).ToArray();
            var history = await database.PerformanceHistory
                .AsNoTracking()
                .Where(row => row.SongScoreId.HasValue && scoreIds.Contains(row.SongScoreId.Value))
                .OrderBy(row => row.SongScoreId)
                .ThenBy(row => row.DisplayOrder)
                .ToListAsync(cancellationToken);

            return new PersistenceEvidence(
                scores.Select(score => new ScoreEvidence(
                    score.Id,
                    score.PlaySpeedPercent,
                    score.PlayCount,
                    history
                        .Where(row => row.SongScoreId == score.Id)
                        .Select(row => new HistoryEvidence(
                            row.DisplayOrder,
                            row.PitchSemitones,
                            row.HistoryLine))
                        .ToArray()))
                    .ToArray());
        }
        catch (Exception ex) when (
            ex is IOException or
            Microsoft.Data.Sqlite.SqliteException)
        {
            return new PersistenceEvidence([]);
        }
    }

    private sealed record PlaybackProfile(
        int PlaySpeedPercent,
        int PitchSemitones,
        int ExpectedBucketPlayCount);

    private sealed record PersistenceEvidence(IReadOnlyList<ScoreEvidence> Scores);

    private sealed record ScoreEvidence(
        int Id,
        int PlaySpeedPercent,
        int PlayCount,
        IReadOnlyList<HistoryEvidence> History);

    private sealed record HistoryEvidence(
        int DisplayOrder,
        int PitchSemitones,
        string HistoryLine);

    private static async Task<E2EGameState> WaitForStageAsync(
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

    /// <summary>
    /// Captures a screenshot via the JSON-RPC takeScreenshot endpoint and saves it
    /// as a PNG artifact. Used both for proactive stage screenshots (e.g. SongSelect
    /// status-panel visual verification) and for failure diagnostics.
    /// </summary>
    private static async Task SaveScreenshotAsync(
        JsonRpcGameClient client,
        E2EFixture fixture,
        string fileName,
        CancellationToken cancellationToken)
    {
        try
        {
            // The timer-backed CTS must have its own 'using' — wrapping it inline in
            // CreateLinkedTokenSource leaks it because only the linked CTS is disposed.
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
            // Screenshot artifacts should never hide the original E2E assertion or launch error,
            // but log the reason so a missing CI artifact is explainable (e.g. the test's own
            // cancellation budget surfaced here as OperationCanceledException).
            Console.WriteLine($"[E2E] Screenshot capture skipped for '{fileName}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int GetPortFromEnvironmentOrDefault()
    {
        var raw = Environment.GetEnvironmentVariable("DTXMANIA_E2E_API_PORT");
        if (int.TryParse(raw, out var port))
            return port;

        return GetAvailablePort();
    }

    private static int GetAvailablePort()
    {
        // Bind an ephemeral port and verify it is usable to avoid TOCTOU races
        const int maxAttempts = 5;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var chosen = ((IPEndPoint)listener.LocalEndpoint).Port;

            // Quick bind check: release and immediately re-bind to confirm availability
            listener.Stop();

            try
            {
                var verify = new TcpListener(IPAddress.Loopback, chosen);
                verify.Start();
                verify.Stop();
                return chosen;
            }
            catch (SocketException)
            {
                // Port was snatched up; try again
            }
        }

        // Fallback: just return a fresh ephemeral port
        using var fallback = new TcpListener(IPAddress.Loopback, 0);
        fallback.Start();
        return ((IPEndPoint)fallback.LocalEndpoint).Port;
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DTXMania.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from current directory.");
    }
}
