using System.Text.Json;
using DTXMania.Automation.Process;
using DTXMania.Automation.Telemetry;
using DTXMania.VideoRecorder.Obs;
using DTXMania.VideoRecorder.Workflow;

namespace DTXMania.VideoRecorder.Tests.Workflow;

public sealed class RecordWorkflowTests
{
    [Fact]
    public async Task RunAsync_HappyJourney_ShouldFollowReadinessOrderAndOwnCleanup()
    {
        var game = new FakeGame(
            Title(),
            SongSelect("indexed chart"),
            Preview(),
            Transition(),
            Performance(),
            Result());
        var obs = new FakeObs(game.Events);
        var chartPath = Path.Combine(Path.GetTempPath(), "chart.dtx");
        var options = FastOptions(game.Events);
        var workflow = new RecordWorkflow(
            game,
            obs,
            chartPath,
            CreateStartOptions(),
            options);

        var outputPath = await workflow.RunAsync(CancellationToken.None);

        Assert.Equal("raw-output.mp4", outputPath);
        Assert.Equal(1, game.PrepareCount);
        Assert.Equal(
            new[]
            {
                "start",
                "wait-startup",
                "state:Title",
                "key:Enter:00:00:00.0500000",
                "state:SongSelect",
                "prepare",
                "screenshot",
                "obs:connect",
                "obs:status",
                "obs:start",
                "start-preview",
                "state:SongSelect",
                "activate",
                "state:SongTransition",
                "state:Performance",
                "state:Result",
                "screenshot",
                "delay:00:00:05",
                "obs:stop",
                "dispose",
                "obs:dispose"
            },
            game.Events);
        Assert.Equal(1, obs.DisposeCallCount);
    }

    [Fact]
    public async Task RunAsync_ShouldWaitForPopulatedSongSelectBeforePreparing()
    {
        var game = new FakeGame(
            Title(),
            SongSelect(),
            SongSelect("indexed chart"),
            Preview(),
            Transition(),
            Performance(),
            Result());
        var obs = new FakeObs();
        var workflow = new RecordWorkflow(
            game,
            obs,
            "chart.dtx",
            CreateStartOptions(),
            FastOptions(game.Events));

        await workflow.RunAsync(CancellationToken.None);

        Assert.Equal(
            new string?[] { null, "indexed chart" },
            game.ObservedSongSelectTitles.Take(2));
        Assert.Equal(1, game.PrepareCount);
        Assert.Equal("indexed chart", game.PrepareSelectedSongTitle);
    }

    [Fact]
    public async Task RunAsync_UnexpectedStageOrder_ShouldFailBeforePerformanceAndStopOwnedObs()
    {
        var game = new FakeGame(
            Title(),
            SongSelect("indexed chart"),
            Preview(),
            Performance());
        var obs = new FakeObs();
        var workflow = new RecordWorkflow(
            game,
            obs,
            "chart.dtx",
            CreateStartOptions(),
            FastOptions(game.Events) with
            {
                StageTimeout = TimeSpan.FromMilliseconds(25)
            });

        await Assert.ThrowsAsync<TimeoutException>(
            () => workflow.RunAsync(CancellationToken.None));

        Assert.Contains("activate", game.Events);
        Assert.DoesNotContain("state:Result", game.Events);
        Assert.Equal(1, obs.StopCallCount);
        Assert.Equal(1, obs.DisposeCallCount);
    }

    [Fact]
    public async Task AutomationGameRecordingControl_DisposeAsync_ShouldBeIdempotent()
    {
        var game = new AutomationGameRecordingControl(12345, "api-key");

        await game.DisposeAsync();
        await game.DisposeAsync();
    }

    [Fact]
    public async Task RunAsync_PreExistingObsRecording_ShouldNotStartOrStop()
    {
        var game = new FakeGame(Title(), SongSelect("indexed chart"));
        var obs = new FakeObs { IsRecording = true };
        var workflow = new RecordWorkflow(
            game,
            obs,
            "chart.dtx",
            CreateStartOptions(),
            FastOptions(game.Events));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => workflow.RunAsync(CancellationToken.None));

        Assert.Contains("already recording", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("obs:start", obs.Events);
        Assert.DoesNotContain("obs:stop", obs.Events);
        Assert.Contains("dispose", game.Events);
    }

    [Fact]
    public async Task RunAsync_StartFailure_ShouldNotStopPreExistingOwnership()
    {
        var game = new FakeGame(Title(), SongSelect("indexed chart"),
            new GameStateSnapshot());
        var obs = new FakeObs { ThrowOnStart = true };
        var workflow = new RecordWorkflow(
            game,
            obs,
            "chart.dtx",
            CreateStartOptions(),
            FastOptions(game.Events));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workflow.RunAsync(CancellationToken.None));

        Assert.Contains("obs:start", obs.Events);
        Assert.DoesNotContain("obs:stop", obs.Events);
        Assert.Contains("dispose", game.Events);
    }

    [Fact]
    public async Task RunAsync_CancellationAfterStart_ShouldStopOwnedObsAndDisposeGame()
    {
        using var cancellation = new CancellationTokenSource();
        var game = new FakeGame(
            Title(),
            SongSelect("indexed chart"),
            Preview(),
            Transition(),
            Performance(),
            Result())
        {
            CancelAt = "start-preview",
            Cancellation = cancellation
        };
        var obs = new FakeObs();
        var workflow = new RecordWorkflow(
            game,
            obs,
            "chart.dtx",
            CreateStartOptions(),
            FastOptions(game.Events));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workflow.RunAsync(cancellation.Token));

        Assert.Contains("obs:stop", obs.Events);
        Assert.Contains("dispose", game.Events);
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    public async Task RunAsync_InvalidPerformanceReadiness_ShouldStopOwnedObs(
        bool autoPlay,
        int totalNotes)
    {
        var game = new FakeGame(
            Title(),
            SongSelect("indexed chart"),
            Preview(),
            Transition(),
            Performance(autoPlay, totalNotes));
        var obs = new FakeObs();
        var workflow = new RecordWorkflow(
            game,
            obs,
            "chart.dtx",
            CreateStartOptions(),
            FastOptions(game.Events) with
            {
                StageTimeout = TimeSpan.FromMilliseconds(25)
            });

        await Assert.ThrowsAsync<TimeoutException>(
            () => workflow.RunAsync(CancellationToken.None));

        Assert.Contains("obs:stop", obs.Events);
        Assert.Contains("dispose", game.Events);
    }

    [Fact]
    public async Task RunAsync_IncompleteResult_ShouldStopOwnedObs()
    {
        var game = new FakeGame(
            Title(),
            SongSelect("indexed chart"),
            Preview(),
            Transition(),
            Performance(),
            Snapshot(
                "Result",
                "\"stageCompleted\":true,\"clearFlag\":false,\"completionReason\":\"SongComplete\",\"totalNotes\":3,\"totalJudgements\":2"));
        var obs = new FakeObs();
        var workflow = new RecordWorkflow(
            game,
            obs,
            "chart.dtx",
            CreateStartOptions(),
            FastOptions(game.Events) with
            {
                PerformanceTimeout = TimeSpan.FromMilliseconds(25)
            });

        await Assert.ThrowsAsync<TimeoutException>(
            () => workflow.RunAsync(CancellationToken.None));

        Assert.Contains("obs:stop", obs.Events);
    }

    [Fact]
    public async Task RunAsync_CancellationDuringPerformance_ShouldStopOwnedObs()
    {
        using var cancellation = new CancellationTokenSource();
        var game = new FakeGame(
            Title(),
            SongSelect("indexed chart"),
            Preview(),
            Transition(),
            Performance(),
            Result())
        {
            CancelAt = "Performance",
            Cancellation = cancellation
        };
        var obs = new FakeObs();
        var workflow = new RecordWorkflow(
            game,
            obs,
            "chart.dtx",
            CreateStartOptions(),
            FastOptions(game.Events));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workflow.RunAsync(cancellation.Token));

        Assert.Contains("obs:stop", obs.Events);
    }

    [Fact]
    public async Task RunAsync_CancellationDuringResultHold_ShouldStopOwnedObsWithoutInput()
    {
        using var cancellation = new CancellationTokenSource();
        var game = new FakeGame(
            Title(),
            SongSelect("indexed chart"),
            Preview(),
            Transition(),
            Performance(),
            Result());
        var obs = new FakeObs();
        var options = FastOptions(game.Events) with
        {
            DelayAsync = (_, token) =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        };
        var workflow = new RecordWorkflow(
            game,
            obs,
            "chart.dtx",
            CreateStartOptions(),
            options);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workflow.RunAsync(cancellation.Token));

        Assert.DoesNotContain("key:", game.Events);
        Assert.Contains("obs:stop", obs.Events);
    }

    private static RecordWorkflowOptions FastOptions(List<string> events) =>
        new()
        {
            SetupTimeout = TimeSpan.FromSeconds(1),
            StageTimeout = TimeSpan.FromSeconds(1),
            PerformanceTimeout = TimeSpan.FromSeconds(1),
            ExternalIoTimeout = TimeSpan.FromSeconds(1),
            PollInterval = TimeSpan.FromMilliseconds(1),
            DelayAsync = (delay, _) =>
            {
                events.Add($"delay:{delay}");
                return Task.CompletedTask;
            }
        };

    private static GameProcessStartOptions CreateStartOptions() =>
        new(
            Directory.GetCurrentDirectory(),
            GameLaunchTarget.Project("DTXMania.Game/DTXMania.Game.Windows.csproj"),
            Path.GetTempPath(),
            "launch-token");

    private static GameStateSnapshot Title() => Snapshot("Title");

    private static GameStateSnapshot SongSelect(string title) =>
        Snapshot("SongSelect", $"\"selectedSongTitle\":{JsonSerializer.Serialize(title)}");

    private static GameStateSnapshot SongSelect() => Snapshot("SongSelect");

    private static GameStateSnapshot Preview() =>
        Snapshot(
            "SongSelect",
            "\"preparedPreviewState\":\"Playing\",\"preparedPreviewElapsedMs\":10000");

    private static GameStateSnapshot Transition() => Snapshot("SongTransition");

    private static GameStateSnapshot Performance(bool autoPlay = true, int totalNotes = 3) =>
        Snapshot(
            "Performance",
            $"\"performanceReady\":true,\"autoPlayEnabled\":{autoPlay.ToString().ToLowerInvariant()},\"totalNotes\":{totalNotes}");

    private static GameStateSnapshot Result() =>
        Snapshot(
            "Result",
            "\"stageCompleted\":true,\"clearFlag\":true,\"completionReason\":\"SongComplete\",\"totalNotes\":3,\"totalJudgements\":3");

    private static GameStateSnapshot Snapshot(string stage, string extra = "")
    {
        var suffix = string.IsNullOrWhiteSpace(extra) ? string.Empty : "," + extra;
        var telemetry = JsonDocument.Parse($"{{\"stageType\":{JsonSerializer.Serialize(stage)}{suffix}}}")
            .RootElement
            .Clone();
        return new GameStateSnapshot
        {
            CustomData = new Dictionary<string, JsonElement> { ["telemetry"] = telemetry }
        };
    }

    private sealed class FakeGame : IGameRecordingControl
    {
        private readonly Queue<GameStateSnapshot> _states;
        public FakeGame(params GameStateSnapshot[] states) => _states = new(states);
        public List<string> Events { get; } = new();
        public List<string?> ObservedSongSelectTitles { get; } = new();
        public int PrepareCount { get; private set; }
        public string? PrepareSelectedSongTitle { get; private set; }
        private string? LastSelectedSongTitle { get; set; }
        public string? CancelAt { get; init; }
        public CancellationTokenSource? Cancellation { get; init; }
        public string StandardOutput => "stdout";
        public string StandardError => "stderr";

        public void Start(GameProcessStartOptions options) => Events.Add("start");

        public Task WaitForStartupAsync(TimeSpan timeout, CancellationToken token)
        {
            Events.Add("wait-startup");
            return Task.CompletedTask;
        }

        public Task<GameStateSnapshot> GetGameStateAsync(CancellationToken token)
        {
            var state = _states.Count > 0 ? _states.Dequeue() : new GameStateSnapshot();
            Events.Add($"state:{state.StageType}");
            LastSelectedSongTitle = state.SelectedSongTitle;
            if (string.Equals(state.StageType, "SongSelect", StringComparison.Ordinal))
                ObservedSongSelectTitles.Add(state.SelectedSongTitle);
            if (CancelAt == state.StageType)
                Cancellation?.Cancel();
            return Task.FromResult(state);
        }

        public Task SendKeyAsync(string key, TimeSpan hold, CancellationToken token)
        {
            Events.Add($"key:{key}:{hold}");
            return Task.CompletedTask;
        }

        public Task PrepareVideoChartAsync(string chartPath, CancellationToken token)
        {
            PrepareCount++;
            PrepareSelectedSongTitle = LastSelectedSongTitle;
            Events.Add("prepare");
            return Task.CompletedTask;
        }

        public Task<string?> TakeScreenshotBase64Async(CancellationToken token)
        {
            Events.Add("screenshot");
            return Task.FromResult<string?>("c2NyZWVuc2hvdA==");
        }

        public Task StartPreparedPreviewAsync(CancellationToken token)
        {
            Events.Add("start-preview");
            CancelIfRequested();
            return Task.CompletedTask;
        }

        public Task ActivatePreparedChartAsync(CancellationToken token)
        {
            Events.Add("activate");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Events.Add("dispose");
            return ValueTask.CompletedTask;
        }

        private void CancelIfRequested()
        {
            if (CancelAt == "start-preview")
                Cancellation?.Cancel();
        }
    }

    private sealed class FakeObs : IObsRecorder
    {
        public FakeObs(List<string>? events = null)
        {
            Events = events ?? new List<string>();
        }

        public List<string> Events { get; }
        public bool IsRecording { get; init; }
        public bool ThrowOnStart { get; init; }
        public int StopCallCount { get; private set; }
        public int DisposeCallCount { get; private set; }

        public Task ConnectAsync(CancellationToken token)
        {
            Events.Add("obs:connect");
            return Task.CompletedTask;
        }

        public Task<ObsRecordStatus> GetRecordStatusAsync(CancellationToken token)
        {
            Events.Add("obs:status");
            return Task.FromResult(new ObsRecordStatus(IsRecording));
        }

        public Task StartRecordAsync(CancellationToken token)
        {
            Events.Add("obs:start");
            if (ThrowOnStart)
                throw new InvalidOperationException("start failed");
            return Task.CompletedTask;
        }

        public Task<string> StopRecordAsync(CancellationToken token)
        {
            StopCallCount++;
            Events.Add("obs:stop");
            return Task.FromResult("raw-output.mp4");
        }

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            Events.Add("obs:dispose");
            return ValueTask.CompletedTask;
        }
    }
}
