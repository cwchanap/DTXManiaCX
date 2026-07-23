using System.Text.Json;
using DTXMania.E2E.Telemetry;
using DTXMania.Game.Lib;

namespace DTXMania.E2E.Telemetry;

[Trait("Category", "E2E-Support")]
public sealed class E2EGameStateTests
{
    /// <summary>
    /// Serializer options matching the JsonRpcServer configuration:
    /// PropertyNamingPolicy = CamelCase. This test ensures that every field
    /// consumed by E2EGameState is emitted by GameTelemetrySnapshot under
    /// the expected camelCase key. A rename on either side would cause this
    /// test to fail instead of silently returning defaults.
    /// </summary>
    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void RoundTrip_GameTelemetrySnapshot_To_E2EGameState_ShouldMapAllConsumedFields()
    {
        // Arrange: fully populate the producer-side snapshot
        var snapshot = new GameTelemetrySnapshot
        {
            StageName = "PerformanceStage",
            StageType = "Performance",
            StagePhase = "Normal",
            IsTransitioning = false,
            SelectedSongTitle = "Test Song",
            PlaySpeedPercent = 75,
            PitchSemitones = 3,
            PlaybackProfileFrozen = true,
            AudioPreparationCompleted = 2,
            AudioPreparationTotal = 2,
            AudioPreparationCacheHits = 1,
            PreparedAudioBytes = 4096,
            Score = 95000,
            TotalNotes = 200,
            ClearFlag = true,
            StageCompleted = true,
            CompletionReason = "Cleared",
            ScoreSaveStatus = "Saved",
            ScoreSaveError = null,
            PerfectCount = 180,
            GreatCount = 15,
            GoodCount = 3,
            PoorCount = 1,
            MissCount = 1,
        };

        // Serialize with CamelCase (matching JsonRpcServer config)
        var telemetryJson = JsonSerializer.Serialize(snapshot, CamelCaseOptions);
        var telemetryElement = JsonDocument.Parse(telemetryJson).RootElement;

        // Wrap in the GameState envelope that E2EGameState expects
        var gameStateJson = JsonSerializer.Serialize(new
        {
            currentStage = "PerformanceStage",
            customData = new Dictionary<string, object>
            {
                ["telemetry"] = telemetryElement
            }
        }, CamelCaseOptions);

        // Act: deserialize through E2EGameState
        var gameState = JsonSerializer.Deserialize<E2EGameState>(gameStateJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

        // Assert: every field consumed by E2EGameState maps correctly
        Assert.Equal("Performance", gameState.StageType);
        Assert.Equal("Test Song", gameState.SelectedSongTitle);
        Assert.Equal(75, gameState.PlaySpeedPercent);
        Assert.Equal(3, gameState.PitchSemitones);
        Assert.True(gameState.PlaybackProfileFrozen);
        Assert.Equal(2, gameState.AudioPreparationCompleted);
        Assert.Equal(2, gameState.AudioPreparationTotal);
        Assert.Equal(1, gameState.AudioPreparationCacheHits);
        Assert.Equal(4096L, gameState.PreparedAudioBytes);
        Assert.Equal(95000, gameState.Score);
        Assert.Equal(200, gameState.TotalNotes);
        Assert.Equal(180 + 15 + 3 + 1 + 1, gameState.TotalJudgements); // TotalJudgements is computed
        Assert.True(gameState.ClearFlag);
        Assert.True(gameState.StageCompleted);
        Assert.Equal("Cleared", gameState.CompletionReason);
        Assert.Equal("Saved", gameState.ScoreSaveStatus);
        Assert.Null(gameState.ScoreSaveError);
    }

    [Fact]
    public void Telemetry_WhenCustomDataContainsTelemetry_ReturnsValue()
    {
        var state = new E2EGameState
        {
            CustomData = new Dictionary<string, JsonElement>
            {
                ["telemetry"] = JsonDocument.Parse("{\"stageType\":\"Result\",\"score\":500}").RootElement
            }
        };

        Assert.Equal("Result", state.StageType);
        Assert.Equal(500, state.Score);
    }

    [Fact]
    public void Telemetry_WhenCustomDataIsEmpty_ReturnsDefaultInsteadOfThrowing()
    {
        var state = new E2EGameState
        {
            CustomData = new Dictionary<string, JsonElement>()
        };

        // Should not throw — returns empty JSON object
        var ex = Record.Exception(() => _ = state.Telemetry);
        Assert.Null(ex);

        // Downstream accessors degrade gracefully to defaults
        Assert.Equal(string.Empty, state.StageType);
        Assert.Null(state.SelectedSongTitle);
        Assert.Equal(0, state.Score);
        Assert.Equal(100, state.PlaySpeedPercent);
        Assert.Equal(0, state.PitchSemitones);
        Assert.False(state.PlaybackProfileFrozen);
        Assert.Equal(0, state.AudioPreparationCompleted);
        Assert.Equal(0, state.AudioPreparationTotal);
        Assert.Equal(0, state.AudioPreparationCacheHits);
        Assert.Equal(0L, state.PreparedAudioBytes);
        Assert.Null(state.ScoreSaveStatus);
        Assert.Null(state.ScoreSaveError);
        Assert.False(state.ClearFlag);
        Assert.False(state.StageCompleted);
    }

    [Fact]
    public void Telemetry_WhenCustomDataHasNoTelemetryKey_ReturnsDefaultInsteadOfThrowing()
    {
        var state = new E2EGameState
        {
            CustomData = new Dictionary<string, JsonElement>
            {
                ["other"] = JsonDocument.Parse("\"value\"").RootElement
            }
        };

        var ex = Record.Exception(() => _ = state.Telemetry);
        Assert.Null(ex);
        Assert.Equal(string.Empty, state.StageType);
    }

    [Fact]
    public void Telemetry_WhenNumericFieldsAreNull_ReturnsDefaultsInsteadOfThrowing()
    {
        var state = new E2EGameState
        {
            CustomData = new Dictionary<string, JsonElement>
            {
                ["telemetry"] = JsonDocument.Parse("{\"stageType\":\"SongSelect\",\"score\":null,\"totalNotes\":null,\"totalJudgements\":null,\"playSpeedPercent\":null,\"pitchSemitones\":null,\"playbackProfileFrozen\":null,\"audioPreparationCompleted\":null,\"audioPreparationTotal\":null,\"audioPreparationCacheHits\":null,\"preparedAudioBytes\":null,\"clearFlag\":null,\"stageCompleted\":null}").RootElement
            }
        };

        var ex = Record.Exception(() =>
        {
            _ = state.Score;
            _ = state.TotalNotes;
            _ = state.TotalJudgements;
            _ = state.PlaySpeedPercent;
            _ = state.PitchSemitones;
            _ = state.PlaybackProfileFrozen;
            _ = state.AudioPreparationCompleted;
            _ = state.AudioPreparationTotal;
            _ = state.AudioPreparationCacheHits;
            _ = state.PreparedAudioBytes;
            _ = state.ClearFlag;
            _ = state.StageCompleted;
        });

        Assert.Null(ex);
        Assert.Equal(0, state.Score);
        Assert.Equal(0, state.TotalNotes);
        Assert.Equal(0, state.TotalJudgements);
        Assert.Equal(100, state.PlaySpeedPercent);
        Assert.Equal(0, state.PitchSemitones);
        Assert.False(state.PlaybackProfileFrozen);
        Assert.Equal(0, state.AudioPreparationCompleted);
        Assert.Equal(0, state.AudioPreparationTotal);
        Assert.Equal(0, state.AudioPreparationCacheHits);
        Assert.Equal(0L, state.PreparedAudioBytes);
        Assert.False(state.ClearFlag);
        Assert.False(state.StageCompleted);
    }
}
