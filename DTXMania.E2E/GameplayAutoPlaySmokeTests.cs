using System.Text.Json;
using DTXMania.Automation.JsonRpc;
using DTXMania.Automation.Process;
using DTXMania.Automation.Support;
using DTXMania.Automation.Telemetry;
using DTXMania.E2E.Fixtures;
using DTXMania.E2E.Support;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.EntityFrameworkCore;
using static DTXMania.E2E.Support.E2EStageHelpers;

namespace DTXMania.E2E;

[Trait("Category", "E2E")]
public sealed class GameplayAutoPlaySmokeTests
{
    // Bar-1 boundary of the generated 120 BPM fixture chart: the second channel-54
    // #AVI trigger lands here, restarting the one-second background video.
    private const double BarOneVideoRetriggerSongTimeMs = 2000.0;

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
            await NavigateToSongSelectAsync(bundle, fixture, cancellation.Token);
            await SaveScreenshotAsync(client, fixture, "song-select-fullautoplay.png", cancellation.Token);
            await StartSongAsync(client, cancellation.Token);

            var fullyJudged = await WaitForAllNotesJudgedAsync(client, cancellation.Token);

            Assert.True(fullyJudged.AutoPlayEnabled);
            Assert.Equal(fullyJudged.TotalNotes, fullyJudged.TotalJudgements);

            var resultState = await WaitForCompletedResultAsync(client, cancellation.Token);

            Assert.Equal(E2EFixtureBuilder.SongTitle, resultState.SelectedSongTitle);
            // AutoPlayEnabled is only reported while the Performance stage owns
            // telemetry; the fullyJudged poll above already pinned it there.
            Assert.Equal(resultState.TotalNotes, resultState.TotalJudgements);
            Assert.Equal("SongComplete", resultState.CompletionReason);
            await E2EArtifactWriter.WriteJsonAsync(fixture, "final-state-fullautoplay.json", resultState);

            // HPA-16: the production Result draw must write exactly one PNG under
            // the game-owned Screenshots root (DTXMANIA_APPDATA_ROOT pins it to
            // fixture.AppDataRoot). StageCompleted is not screenshot readiness:
            // the capture is requested only after the Result reveal completes and
            // the PNG is written asynchronously, so the helper polls for a
            // complete image (signature + trailing IEND) rather than existence.
            var screenshotsRoot = Path.Combine(fixture.AppDataRoot, "Screenshots");
            await WaitForResultScreenshotAsync(fixture, cancellation.Token);

            // The accepted one-shot must not re-fire on later Result draws: let
            // further fully revealed frames run, then confirm the count is still one.
            await Task.Delay(TimeSpan.FromSeconds(2), cancellation.Token);
            Assert.Single(Directory.EnumerateFiles(screenshotsRoot, "result-*.png"));
        }
        catch
        {
            await TryWriteScreenshotAsync(client, fixture, "failure-fullautoplay.png");
            throw;
        }
        finally
        {
            E2EArtifactWriter.CopyFixtureFiles(fixture);
            await WriteRunLogsAsync(fixture, process, "fullautoplay");
        }
    }

    [Fact(Timeout = 240_000)]
    public async Task GameplayTabAutoPlay_ShouldJudgeEveryNoteInEffectiveTabModeAndComplete()
    {
        // HPA-411 black-box proof: persisted NoteDisplayStyle=Tab produces an effective
        // Tab run — reported live through the noteDisplayStyle telemetry field — with
        // the same AutoPlay judgements and Result completion as the Lanes smoke.
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(210));
        var repoRoot = E2EGameLaunch.ResolveRepoRoot();
        var runRoot = Path.Combine(Path.GetTempPath(), "dtxmaniacx-e2e-tabdisplay-" + Guid.NewGuid().ToString("N"));
        var apiPort = E2EGameLaunch.ResolveApiPort();
        var fixture = E2EFixtureBuilder.Build(
            runRoot,
            repoRoot,
            apiPort,
            noteDisplayStyle: DrumsNoteDisplayStyle.Tab,
            enableAutoPlayLanes: true,
            // The 07-tab-performance.png capture then doubles as strip-over-BGA evidence.
            includeBackgroundVideo: true,
            // The Tab tour capture must show the shipped CX Neon skin, not the
            // sandbox fallback skin.
            useBundledDefaultSkin: true,
            // The capture predicate also needs unjudged notes past the bar-1
            // retrigger: without a tail the last drum note judges at ~2.6s,
            // leaving under a second for the whole screenshot round-trip on a
            // slow runner. The tail repeats the drum pattern (and restarts the
            // one-second clip each bar) so the staff stays busy for ~8 more
            // seconds of capture headroom.
            drumNoteTailBars: 4);
        // Keep this test's logs, fixture copies and failure screenshot out of the
        // shared tour folder and out of the other tests' common artifact names.
        fixture = fixture with
        {
            ArtifactRoot = Path.Combine(fixture.ArtifactRoot, "tab-autoplay")
        };
        var tourRoot = ResolveTourArtifactRoot(fixture);
        await using var bundle = E2EGameLaunch.CreateClientBundle(fixture);
        var process = bundle.Process;
        var client = bundle.Client;

        try
        {
            await NavigateToSongSelectAsync(bundle, fixture, cancellation.Token);
            await StartSongAsync(client, cancellation.Token);

            // Photograph the strip mid-song: PerformanceReady is already true during
            // READY (before StartSong), when channel-54 video triggers have not been
            // scheduled and the frame cannot prove strip-over-BGA order. Wait for the
            // song clock to advance past the bar-1 #AVI retrigger (2000ms at 120 BPM;
            // the drum-note tail keeps restarting the one-second video each second)
            // while unjudged notes remain — after the last judgement the staff is
            // empty.
            await CaptureTabScreenshotAsync(
                client,
                fixture,
                "07-tab-performance.png",
                tourRoot,
                cancellation.Token);

            // Polling until Tab is part of the predicate asserts ACTUAL effective Tab
            // through the last judgement; a silent fallback to Lanes fails the poll.
            var tabRun = await WaitForAllNotesJudgedAsync(
                client, cancellation.Token, nameof(DrumsNoteDisplayStyle.Tab));

            Assert.Equal(nameof(DrumsNoteDisplayStyle.Tab), tabRun.NoteDisplayStyle);
            Assert.Equal(tabRun.TotalNotes, tabRun.TotalJudgements);

            var resultState = await WaitForCompletedResultAsync(client, cancellation.Token);

            Assert.Equal(E2EFixtureBuilder.SongTitle, resultState.SelectedSongTitle);
            // Judgement totals are unchanged by the display style: every note judged.
            Assert.Equal(resultState.TotalNotes, resultState.TotalJudgements);
            Assert.True(resultState.ClearFlag);
            Assert.Equal("SongComplete", resultState.CompletionReason);
            await E2EArtifactWriter.WriteJsonAsync(fixture, "final-state-tabautoplay.json", resultState);
        }
        catch
        {
            await TryWriteScreenshotAsync(client, fixture, "failure-tabautoplay.png");
            throw;
        }
        finally
        {
            E2EArtifactWriter.CopyFixtureFiles(fixture);
            await WriteRunLogsAsync(fixture, process, "tabautoplay");
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

            await StartSongViaStatusPanelAsync(client, E2EFixtureBuilder.SongTitle, cancellationToken);

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
            await TryWriteScreenshotAsync(
                client,
                fixture,
                $"failure-{artifactSuffix}.png");
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

    /// <summary>
    /// Shared journey head used by the AutoPlay smokes: launch the game process,
    /// wait for the API, then advance Title → SongSelect. Callers add their own
    /// stage artifacts before <see cref="StartSongAsync"/>.
    /// </summary>
    private static async Task NavigateToSongSelectAsync(
        E2EClientBundle bundle,
        E2EFixture fixture,
        CancellationToken cancellationToken)
    {
        bundle.Process.Start(E2EGameLaunch.CreateOptions(fixture));
        await bundle.Process.WaitForStartupAsync(
            bundle.Client.GetHealthAsync,
            TimeSpan.FromSeconds(60),
            TimeSpan.FromMilliseconds(500),
            cancellationToken);

        await WaitForStageAsync(bundle.Client, "Title", TimeSpan.FromSeconds(45), cancellationToken);
        await bundle.Client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellationToken);
        await WaitForStageAsync(bundle.Client, "SongSelect", TimeSpan.FromSeconds(45), cancellationToken);
    }

    /// <summary>Selects the highlighted song and difficulty, entering Performance.</summary>
    private static async Task StartSongAsync(
        JsonRpcGameClient client,
        CancellationToken cancellationToken)
    {
        await StartSongViaStatusPanelAsync(client, E2EFixtureBuilder.SongTitle, cancellationToken);
    }

    /// <summary>
    /// Captures the required Tab tour frame only while the run is genuinely
    /// mid-song in effective Tab notation. The qualifying snapshot alone is not
    /// enough: the game keeps running while this continuation awaits, so the
    /// full predicate is re-read and re-asserted immediately before and after
    /// the capture. A fallback to Lanes, a finished staff, Result or a pending
    /// transition fails the run instead of publishing a green empty-staff PNG.
    /// </summary>
    private static async Task CaptureTabScreenshotAsync(
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
            nameof(DrumsNoteDisplayStyle.Tab),
            BarOneVideoRetriggerSongTimeMs,
            TimeSpan.FromSeconds(120),
            TimeSpan.FromMilliseconds(100),
            "effective Tab mid-song with unjudged notes past the video retrigger and no pending transition",
            cancellationToken,
            requireAutoPlay: true,
            requireBundledSkin: true);
    }

    /// <summary>
    /// True only while the run is a real mid-song Tab frame: effective Tab
    /// notation, gameplay ready with AutoPlay, the bar-1 BGA retrigger already
    /// reached, chart notes still unjudged and no pending transition.
    /// </summary>
    internal static bool IsTabCaptureState(GameStateSnapshot state) =>
        IsMidSongCaptureState(
            state,
            nameof(DrumsNoteDisplayStyle.Tab),
            BarOneVideoRetriggerSongTimeMs,
            requireAutoPlay: true);

    /// <summary>
    /// Waits until every chart note has been automatically judged. When
    /// <paramref name="requiredNoteDisplayStyle"/> is set the run must report that
    /// effective display style through the last judgement — a mid-song fallback
    /// fails the poll timeout rather than silently passing.
    /// </summary>
    private static async Task<GameStateSnapshot> WaitForAllNotesJudgedAsync(
        JsonRpcGameClient client,
        CancellationToken cancellationToken,
        string? requiredNoteDisplayStyle = null)
    {
        return await Eventually.UntilAsync(
            token => client.GetGameStateAsync(token),
            state =>
                state.PerformanceReady &&
                state.AutoPlayEnabled &&
                (requiredNoteDisplayStyle == null ||
                    string.Equals(state.NoteDisplayStyle, requiredNoteDisplayStyle, StringComparison.Ordinal)) &&
                state.TotalNotes > 0 &&
                state.TotalJudgements == state.TotalNotes,
            TimeSpan.FromSeconds(120),
            TimeSpan.FromMilliseconds(250),
            requiredNoteDisplayStyle == null
                ? "all chart notes automatically judged"
                : $"all chart notes automatically judged while {requiredNoteDisplayStyle} notation is effective",
            cancellationToken);
    }

    private static async Task WriteRunLogsAsync(
        E2EFixture fixture,
        GameProcessDriver process,
        string artifactSuffix)
    {
        await E2EArtifactWriter.WriteTextAsync(fixture, $"game-stdout-{artifactSuffix}.log", process.StandardOutput);
        await E2EArtifactWriter.WriteTextAsync(fixture, $"game-stderr-{artifactSuffix}.log", process.StandardError);
    }

}

/// <summary>
/// Fast E2E-Support coverage for the Tab tour capture guard: the required
/// mid-song Tab screenshot must be refused once the staff is empty, notation
/// fell back to Lanes, the stage advanced, or a transition is pending.
/// </summary>
[Trait("Category", "E2E-Support")]
public sealed class GameplayAutoPlaySmokeTestsSupport
{
    [Fact]
    public void IsTabCaptureState_WhenMidSongEffectiveTabWithUnjudgedNotes_ShouldAccept()
    {
        Assert.True(GameplayAutoPlaySmokeTests.IsTabCaptureState(
            Snapshot(
                "Performance",
                performanceReady: true,
                autoPlayEnabled: true,
                isTransitioning: false)));
    }

    [Fact]
    public void IsTabCaptureState_WhenLanesNotationEffective_ShouldReject()
    {
        Assert.False(GameplayAutoPlaySmokeTests.IsTabCaptureState(
            Snapshot(
                "Performance",
                performanceReady: true,
                autoPlayEnabled: true,
                isTransitioning: false,
                noteDisplayStyle: "Lanes")));
    }

    [Fact]
    public void IsTabCaptureState_WhenTransitioning_ShouldReject()
    {
        Assert.False(GameplayAutoPlaySmokeTests.IsTabCaptureState(
            Snapshot(
                "Performance",
                performanceReady: true,
                autoPlayEnabled: true,
                isTransitioning: true)));
    }

    [Fact]
    public void IsTabCaptureState_WhenStageAdvancedToResult_ShouldReject()
    {
        Assert.False(GameplayAutoPlaySmokeTests.IsTabCaptureState(
            Snapshot(
                "Result",
                performanceReady: false,
                autoPlayEnabled: false,
                isTransitioning: false)));
    }

    [Fact]
    public void IsTabCaptureState_WhenEveryNoteAlreadyJudged_ShouldReject()
    {
        Assert.False(GameplayAutoPlaySmokeTests.IsTabCaptureState(
            Snapshot(
                "Performance",
                performanceReady: true,
                autoPlayEnabled: true,
                isTransitioning: false,
                totalNotes: 6,
                totalJudgements: 6)));
    }

    [Fact]
    public void IsTabCaptureState_WhenBeforeVideoRetrigger_ShouldReject()
    {
        Assert.False(GameplayAutoPlaySmokeTests.IsTabCaptureState(
            Snapshot(
                "Performance",
                performanceReady: true,
                autoPlayEnabled: true,
                isTransitioning: false,
                currentSongTimeMs: 1_000)));
    }

    [Fact]
    public void IsTabCaptureState_WhenPerformanceNotReady_ShouldReject()
    {
        Assert.False(GameplayAutoPlaySmokeTests.IsTabCaptureState(
            Snapshot(
                "Performance",
                performanceReady: false,
                autoPlayEnabled: true,
                isTransitioning: false)));
    }

    [Fact]
    public void IsTabCaptureState_WhenAutoPlayDisabled_ShouldReject()
    {
        Assert.False(GameplayAutoPlaySmokeTests.IsTabCaptureState(
            Snapshot(
                "Performance",
                performanceReady: true,
                autoPlayEnabled: false,
                isTransitioning: false)));
    }

    private static GameStateSnapshot Snapshot(
        string stageType,
        bool performanceReady,
        bool autoPlayEnabled,
        bool isTransitioning,
        string noteDisplayStyle = "Tab",
        int totalNotes = 6,
        int totalJudgements = 3,
        double currentSongTimeMs = 2_000) =>
        CreateTelemetrySnapshot(
            stageType,
            performanceReady,
            isTransitioning,
            noteDisplayStyle,
            totalNotes,
            totalJudgements,
            currentSongTimeMs,
            autoPlayEnabled);
}
