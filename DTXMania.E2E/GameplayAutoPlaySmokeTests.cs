using DTXMania.Automation.JsonRpc;
using DTXMania.Automation.Process;
using DTXMania.Automation.Support;
using DTXMania.Automation.Telemetry;
using DTXMania.E2E.Fixtures;
using DTXMania.E2E.Support;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.EntityFrameworkCore;

namespace DTXMania.E2E;

[Trait("Category", "E2E")]
public sealed class GameplayAutoPlaySmokeTests
{
    [Fact(Timeout = 420_000)]
    public async Task GameplaySmoke_ShouldPersistIndependentSpeedBucketsAndReuseBucketAcrossPitches()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(390));
        var repoRoot = E2EGameLaunch.ResolveRepoRoot();
        var runRoot = Path.Combine(Path.GetTempPath(), "dtxmaniacx-e2e-" + Guid.NewGuid().ToString("N"));
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
                var profileApiPort = E2EGameLaunch.ResolveApiPort();
                var fixture = E2EFixtureBuilder.Build(
                    runRoot,
                    repoRoot,
                    profileApiPort,
                    profile.PlaySpeedPercent,
                    profile.PitchSemitones,
                    enableAutoPlayLanes: false);
                lastFixture = fixture;

                // Later profiles share the RunRoot (songs.db score persistence
                // spans launches), but each launch must bootstrap its playback
                // profile from this iteration's fresh INI: remove the config.db
                // the previous launch wrote, or the DB (authoritative once it
                // exists) would replay the first profile's values forever.
                if (profileIndex > 0 && File.Exists(fixture.ConfigDatabasePath))
                    File.Delete(fixture.ConfigDatabasePath);

                await RunProfileAsync(
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

    [Fact(Timeout = 240_000)]
    public async Task GameplayFullAutoPlay_ShouldJudgeEveryNoteAndComplete()
    {
        // Black-box proof of the core HPA-18 path: persisted AutoPlay.0..9 ->
        // frozen performance lanes -> automatic judgements -> completed Result.
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(210));
        var repoRoot = E2EGameLaunch.ResolveRepoRoot();
        var runRoot = Path.Combine(Path.GetTempPath(), "dtxmaniacx-e2e-fullautoplay-" + Guid.NewGuid().ToString("N"));
        var apiPort = E2EGameLaunch.ResolveApiPort();
        var fixture = E2EFixtureBuilder.Build(runRoot, repoRoot, apiPort, enableAutoPlayLanes: true);
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
            await SaveScreenshotAsync(client, fixture, "song-select-fullautoplay.png", cancellation.Token);

            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellation.Token);
            await Task.Delay(500, cancellation.Token);
            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellation.Token);

            var fullyJudged = await Eventually.UntilAsync(
                token => client.GetGameStateAsync(token),
                state =>
                    state.PerformanceReady &&
                    state.AutoPlayEnabled &&
                    state.TotalNotes > 0 &&
                    state.TotalJudgements == state.TotalNotes,
                TimeSpan.FromSeconds(120),
                TimeSpan.FromMilliseconds(250),
                "all chart notes automatically judged",
                cancellation.Token);

            Assert.True(fullyJudged.AutoPlayEnabled);
            Assert.Equal(fullyJudged.TotalNotes, fullyJudged.TotalJudgements);

            await WaitForStageAsync(client, "Result", TimeSpan.FromSeconds(120), cancellation.Token);
            // StageCompleted is NOT screenshot readiness: it is published as soon
            // as Result has a PerformanceSummary, roughly 1.15 seconds before the
            // reveal completes on an un-fast-forwarded result. Keep this wait for
            // its existing journey-completion purpose; the game-owned screenshot
            // file is awaited independently below.
            var resultState = await Eventually.UntilAsync(
                token => client.GetGameStateAsync(token),
                state =>
                    string.Equals(state.StageType, "Result", StringComparison.Ordinal) &&
                    state.StageCompleted,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMilliseconds(250),
                "Result stage completion",
                cancellation.Token);

            Assert.Equal(E2EFixtureBuilder.SongTitle, resultState.SelectedSongTitle);
            // AutoPlayEnabled is only reported while the Performance stage owns
            // telemetry; the fullyJudged poll above already pinned it there.
            Assert.Equal(resultState.TotalNotes, resultState.TotalJudgements);
            Assert.Equal("SongComplete", resultState.CompletionReason);
            await E2EArtifactWriter.WriteJsonAsync(fixture, "final-state-fullautoplay.json", resultState);

            // HPA-16: the production Result draw must write exactly one PNG under
            // the game-owned Screenshots root (DTXMANIA_APPDATA_ROOT pins it to
            // fixture.AppDataRoot). The budget covers the ~1.15s of reveal still
            // pending after StageCompleted, PNG encoding on the draw thread, and
            // the asynchronous directory/file write.
            var screenshotsRoot = Path.Combine(fixture.AppDataRoot, "Screenshots");
            // The production path writes the PNG with File.WriteAllBytesAsync,
            // so the directory entry can become enumerable before the write
            // task has completed. Poll until the file is fully readable and
            // begins with the PNG signature, not merely until it exists;
            // otherwise the read can race with an in-progress write and see an
            // empty/partial file or a transient sharing error.
            var pngSignature = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };
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
                        if (bytes.Length < pngSignature.Length)
                            return (string?)null;
                        if (!bytes.AsSpan(0, pngSignature.Length).SequenceEqual(pngSignature))
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
                "exactly one readable automatic result screenshot with a valid PNG signature under the game-owned Screenshots root",
                cancellation.Token);

            Assert.NotNull(screenshotPath);

            // The accepted one-shot must not re-fire on later Result draws: let
            // further fully revealed frames run, then confirm the count is still one.
            await Task.Delay(TimeSpan.FromSeconds(2), cancellation.Token);
            Assert.Single(Directory.EnumerateFiles(screenshotsRoot, "result-*.png"));
        }
        catch
        {
            await SaveScreenshotAsync(client, fixture, "failure-fullautoplay.png", CancellationToken.None);
            throw;
        }
        finally
        {
            E2EArtifactWriter.CopyFixtureFiles(fixture);
            await E2EArtifactWriter.WriteTextAsync(fixture, "game-stdout-fullautoplay.log", process.StandardOutput);
            await E2EArtifactWriter.WriteTextAsync(fixture, "game-stderr-fullautoplay.log", process.StandardError);
        }
    }

    private static async Task RunProfileAsync(
        E2EFixture fixture,
        PlaybackProfile profile,
        int profileIndex,
        CancellationToken cancellationToken)
    {
        var artifactSuffix =
            $"{profileIndex + 1}-{profile.PlaySpeedPercent}-pitch-{profile.PitchSemitones}";
        await E2EArtifactWriter.WriteTextAsync(
            fixture,
            $"bootstrap-config-{artifactSuffix}.ini",
            await File.ReadAllTextAsync(fixture.LegacyConfigPath, cancellationToken));
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
            // Manual play with no input: every note misses, so the saved score
            // is zero. Assisted (AutoPlay) runs would score but are never
            // persisted since HPA-18, which is why this fixture runs manual.
            Assert.Equal(0, resultState.Score);
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

}
