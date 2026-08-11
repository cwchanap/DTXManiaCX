using System.Text.Json;
using DTXMania.Automation.Telemetry;

namespace DTXMania.Automation.Tests.Telemetry;

public sealed class GameStateSnapshotTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Deserialize_CamelCaseTelemetry_ShouldExposeAllConsumedFields()
    {
        const string json = """
            {
              "currentStage":"PerformanceStage",
              "customData":{
                "telemetry":{
                  "stageType":"Performance",
                  "selectedSongTitle":"Test Song",
                  "completionReason":"Cleared",
                  "scoreSaveStatus":"Saved",
                  "scoreSaveError":null,
                  "totalNotes":200,
                  "totalJudgements":199,
                  "score":95000,
                  "maxCombo":88,
                  "missCount":1,
                  "performanceReady":true,
                  "playSpeedPercent":75,
                  "pitchSemitones":3,
                  "playbackProfileFrozen":true,
                  "audioPreparationCompleted":2,
                  "audioPreparationTotal":2,
                  "audioPreparationCacheHits":1,
                  "preparedAudioBytes":4096,
                  "autoPlayEnabled":false,
                  "currentSongTimeMs":1234.5,
                  "lastLaneHitLane":5,
                  "lastLaneHitButtonId":"MIDI.36",
                  "lastLaneHitSongTimeMs":1230.5,
                  "clearFlag":true,
                  "stageCompleted":true
                }
              }
            }
            """;

        var state = JsonSerializer.Deserialize<GameStateSnapshot>(json, JsonOptions)!;

        Assert.Equal("PerformanceStage", state.CurrentStage);
        Assert.Equal("Performance", state.StageType);
        Assert.Equal("Test Song", state.SelectedSongTitle);
        Assert.Equal("Cleared", state.CompletionReason);
        Assert.Equal("Saved", state.ScoreSaveStatus);
        Assert.Null(state.ScoreSaveError);
        Assert.Equal(200, state.TotalNotes);
        Assert.Equal(199, state.TotalJudgements);
        Assert.Equal(95000, state.Score);
        Assert.Equal(88, state.MaxCombo);
        Assert.Equal(1, state.MissCount);
        Assert.True(state.PerformanceReady);
        Assert.Equal(75, state.PlaySpeedPercent);
        Assert.Equal(3, state.PitchSemitones);
        Assert.True(state.PlaybackProfileFrozen);
        Assert.Equal(2, state.AudioPreparationCompleted);
        Assert.Equal(2, state.AudioPreparationTotal);
        Assert.Equal(1, state.AudioPreparationCacheHits);
        Assert.Equal(4096L, state.PreparedAudioBytes);
        Assert.False(state.AutoPlayEnabled);
        Assert.Equal(1234.5, state.CurrentSongTimeMs);
        Assert.Equal(5, state.LastLaneHitLane);
        Assert.Equal("MIDI.36", state.LastLaneHitButtonId);
        Assert.Equal(1230.5, state.LastLaneHitSongTimeMs);
        Assert.True(state.ClearFlag);
        Assert.True(state.StageCompleted);
    }

    [Fact]
    public void MissingTelemetry_ShouldReturnDefaultsWithoutThrowing()
    {
        var state = JsonSerializer.Deserialize<GameStateSnapshot>("{\"customData\":{}}", JsonOptions)!;

        Assert.Equal(JsonValueKind.Object, state.Telemetry.ValueKind);
        Assert.Equal(string.Empty, state.StageType);
        Assert.Null(state.SelectedSongTitle);
        Assert.Null(state.CompletionReason);
        Assert.Null(state.ScoreSaveStatus);
        Assert.Null(state.ScoreSaveError);
        Assert.Equal(0, state.TotalNotes);
        Assert.Equal(0, state.TotalJudgements);
        Assert.Equal(0, state.Score);
        Assert.Equal(0, state.MaxCombo);
        Assert.Equal(0, state.MissCount);
        Assert.False(state.PerformanceReady);
        Assert.Equal(100, state.PlaySpeedPercent);
        Assert.Equal(0, state.PitchSemitones);
        Assert.False(state.PlaybackProfileFrozen);
        Assert.Equal(0, state.AudioPreparationCompleted);
        Assert.Equal(0, state.AudioPreparationTotal);
        Assert.Equal(0, state.AudioPreparationCacheHits);
        Assert.Equal(0L, state.PreparedAudioBytes);
        Assert.False(state.AutoPlayEnabled);
        Assert.Equal(0.0, state.CurrentSongTimeMs);
        Assert.Null(state.LastLaneHitLane);
        Assert.Null(state.LastLaneHitButtonId);
        Assert.Null(state.LastLaneHitSongTimeMs);
        Assert.False(state.ClearFlag);
        Assert.False(state.StageCompleted);
    }

    [Fact]
    public void TelemetryWithNullOrWrongValueKinds_ShouldUseAccessorDefaults()
    {
        const string json = """
            {
              "customData":{
                "telemetry":{
                  "stageType":17,
                  "score":null,
                  "totalNotes":"200",
                  "totalJudgements":null,
                  "playSpeedPercent":null,
                  "pitchSemitones":null,
                  "playbackProfileFrozen":null,
                  "audioPreparationCompleted":null,
                  "audioPreparationTotal":null,
                  "audioPreparationCacheHits":null,
                  "preparedAudioBytes":null,
                  "autoPlayEnabled":null,
                  "currentSongTimeMs":null,
                  "clearFlag":null,
                  "stageCompleted":null
                }
              }
            }
            """;

        var state = JsonSerializer.Deserialize<GameStateSnapshot>(json, JsonOptions)!;

        Assert.Equal(string.Empty, state.StageType);
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
        Assert.False(state.AutoPlayEnabled);
        Assert.Equal(0.0, state.CurrentSongTimeMs);
        Assert.False(state.ClearFlag);
        Assert.False(state.StageCompleted);
    }
}
