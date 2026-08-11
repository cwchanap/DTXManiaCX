using System.Text.Json;
using DTXMania.Automation.JsonRpc;
using DTXMania.Automation.Process;
using DTXMania.Automation.Telemetry;
using DTXMania.E2E.Fixtures;
using DTXMania.Game.Lib;

namespace DTXMania.E2E;

[Trait("Category", "E2E-Support")]
[Collection("E2EFixture")]
public sealed class AutomationContractTests
{
    private const string ApiPortEnvironmentVariable = "DTXMANIA_E2E_API_PORT";

    [Fact]
    public void ResolveApiPort_ValidEnvironmentPort_ShouldUseIt()
    {
        var previous = Environment.GetEnvironmentVariable(ApiPortEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(ApiPortEnvironmentVariable, "18080");

            Assert.Equal(18080, E2EGameLaunch.ResolveApiPort());
        }
        finally
        {
            Environment.SetEnvironmentVariable(ApiPortEnvironmentVariable, previous);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("65536")]
    [InlineData("not-a-port")]
    public void ResolveApiPort_InvalidEnvironmentPort_ShouldUseValidEphemeralPort(string raw)
    {
        var previous = Environment.GetEnvironmentVariable(ApiPortEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(ApiPortEnvironmentVariable, raw);

            var port = E2EGameLaunch.ResolveApiPort();

            Assert.InRange(port, 1, 65535);
            Assert.NotEqual(raw, port.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(ApiPortEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void CreateOptions_Default_ShouldExplicitlyRemoveSimulatedMidi()
    {
        var fixture = CreateFixture(out var runRoot);

        try
        {
            var options = E2EGameLaunch.CreateOptions(fixture);

            Assert.Null(options.EnvironmentOverrides![E2EGameLaunch.SimulatedMidiEnvironmentVariable]);
            Assert.False(string.IsNullOrWhiteSpace(options.LaunchToken));
            Assert.Equal(E2EGameLaunch.ResolveRepoRoot(), options.WorkingDirectory);
            Assert.Equal(fixture.AppDataRoot, options.AppDataRoot);
            Assert.Equal(GameLaunchKind.Project, options.Target.Kind);
        }
        finally
        {
            DeleteFixtureRoot(runRoot);
        }
    }

    [Fact]
    public void CreateOptions_EnableMidi_ShouldSetSimulatedMidi()
    {
        var fixture = CreateFixture(out var runRoot);

        try
        {
            var options = E2EGameLaunch.CreateOptions(fixture, enableSimulatedMidi: true);

            Assert.Equal("1", options.EnvironmentOverrides![E2EGameLaunch.SimulatedMidiEnvironmentVariable]);
        }
        finally
        {
            DeleteFixtureRoot(runRoot);
        }
    }

    [Fact]
    public void CreateOptions_ExtraEnvironment_ShouldMergeScenarioValues()
    {
        var fixture = CreateFixture(out var runRoot);

        try
        {
            var options = E2EGameLaunch.CreateOptions(
                fixture,
                extraEnvironment: new Dictionary<string, string?>
                {
                    ["DTXMANIA_E2E_CRASH_INJECTION"] = "update"
                });

            Assert.Equal("update", options.EnvironmentOverrides!["DTXMANIA_E2E_CRASH_INJECTION"]);
            Assert.Null(options.EnvironmentOverrides[E2EGameLaunch.SimulatedMidiEnvironmentVariable]);
        }
        finally
        {
            DeleteFixtureRoot(runRoot);
        }
    }

    [Theory]
    [InlineData(E2EGameLaunch.SimulatedMidiEnvironmentVariable)]
    [InlineData("dtxmania_enable_simulated_midi")]
    public void CreateOptions_ExtraEnvironment_CannotOverrideMidiPolicyCaseInsensitively(
        string environmentVariableName)
    {
        var fixture = CreateFixture(out var runRoot);

        try
        {
            var exception = Assert.Throws<ArgumentException>(() => E2EGameLaunch.CreateOptions(
                fixture,
                extraEnvironment: new Dictionary<string, string?>
                {
                    [environmentVariableName] = "0"
                }));

            Assert.Contains(environmentVariableName, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixtureRoot(runRoot);
        }
    }

    [Fact]
    public void InputTypeWireValues_ShouldMatchGameProducerValues()
    {
        Assert.Equal((int)InputType.MouseClick, (int)GameApiInputType.MouseClick);
        Assert.Equal((int)InputType.MouseMove, (int)GameApiInputType.MouseMove);
        Assert.Equal((int)InputType.KeyPress, (int)GameApiInputType.KeyPress);
        Assert.Equal((int)InputType.KeyRelease, (int)GameApiInputType.KeyRelease);
        Assert.Equal((int)InputType.MidiNoteOn, (int)GameApiInputType.MidiNoteOn);
        Assert.Equal((int)InputType.MidiNoteOff, (int)GameApiInputType.MidiNoteOff);
    }

    [Fact]
    public void GameTelemetrySnapshot_CamelCaseRoundTrip_ShouldExposeAllConsumedFields()
    {
        var snapshot = new GameTelemetrySnapshot
        {
            StageName = "PerformanceStage",
            StageType = "Performance",
            StagePhase = "Normal",
            IsTransitioning = false,
            SelectedSongTitle = "Test Song",
            PerformanceReady = true,
            PlaySpeedPercent = 75,
            PitchSemitones = 3,
            PlaybackProfileFrozen = true,
            AudioPreparationCompleted = 2,
            AudioPreparationTotal = 2,
            AudioPreparationCacheHits = 1,
            PreparedAudioBytes = 4096,
            AutoPlayEnabled = true,
            CurrentSongTimeMs = 1234.5,
            Score = 95000,
            MaxCombo = 88,
            MissCount = 1,
            TotalNotes = 200,
            LastLaneHitLane = 5,
            LastLaneHitButtonId = "MIDI.36",
            LastLaneHitSongTimeMs = 1230.5,
            ClearFlag = true,
            StageCompleted = true,
            CompletionReason = "Cleared",
            ScoreSaveStatus = "Saved",
            ScoreSaveError = "synthetic-save-error",
            PerfectCount = 180,
            GreatCount = 15,
            GoodCount = 3,
            PoorCount = 1,
        };

        var camelCase = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var telemetryElement = JsonDocument.Parse(JsonSerializer.Serialize(snapshot, camelCase)).RootElement;
        var gameStateJson = JsonSerializer.Serialize(new
        {
            currentStage = "PerformanceStage",
            customData = new Dictionary<string, object>
            {
                ["telemetry"] = telemetryElement
            }
        }, camelCase);

        var state = JsonSerializer.Deserialize<GameStateSnapshot>(
            gameStateJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal("PerformanceStage", state.CurrentStage);
        Assert.Equal("Performance", state.StageType);
        Assert.Equal("Test Song", state.SelectedSongTitle);
        Assert.True(state.PerformanceReady);
        Assert.Equal(75, state.PlaySpeedPercent);
        Assert.Equal(3, state.PitchSemitones);
        Assert.True(state.PlaybackProfileFrozen);
        Assert.Equal(2, state.AudioPreparationCompleted);
        Assert.Equal(2, state.AudioPreparationTotal);
        Assert.Equal(1, state.AudioPreparationCacheHits);
        Assert.Equal(4096L, state.PreparedAudioBytes);
        Assert.True(state.AutoPlayEnabled);
        Assert.Equal(1234.5, state.CurrentSongTimeMs);
        Assert.Equal(95000, state.Score);
        Assert.Equal(88, state.MaxCombo);
        Assert.Equal(1, state.MissCount);
        Assert.Equal(200, state.TotalNotes);
        Assert.Equal(5, state.LastLaneHitLane);
        Assert.Equal("MIDI.36", state.LastLaneHitButtonId);
        Assert.Equal(1230.5, state.LastLaneHitSongTimeMs);
        Assert.True(state.ClearFlag);
        Assert.True(state.StageCompleted);
        Assert.Equal("Cleared", state.CompletionReason);
        Assert.Equal("Saved", state.ScoreSaveStatus);
        Assert.Equal("synthetic-save-error", state.ScoreSaveError);
        Assert.Equal(200, state.TotalJudgements);
    }

    private static E2EFixture CreateFixture(out string runRoot)
    {
        runRoot = Path.Combine(
            Path.GetTempPath(),
            "dtxmaniacx-e2e-contract-" + Guid.NewGuid().ToString("N"));
        return E2EFixtureBuilder.Build(
            runRoot,
            E2EGameLaunch.ResolveRepoRoot(),
            apiPort: 18080);
    }

    private static void DeleteFixtureRoot(string runRoot)
    {
        if (Directory.Exists(runRoot))
            Directory.Delete(runRoot, recursive: true);
    }
}
