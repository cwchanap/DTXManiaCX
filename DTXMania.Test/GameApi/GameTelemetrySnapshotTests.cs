using System.Text.Json;
using DTXMania.Game.Lib;
using Xunit;

namespace DTXMania.Test.GameApi
{
    [Trait("Category", "Unit")]
    public sealed class GameTelemetrySnapshotTests
    {
        [Fact]
        public void Serialization_ShouldExposeFrozenPlaybackAndAudioPreparationContract()
        {
            var snapshot = new GameTelemetrySnapshot
            {
                PlaySpeedPercent = 75,
                PitchSemitones = -3,
                PlaybackProfileFrozen = true,
                AudioPreparationCompleted = 5,
                AudioPreparationTotal = 5,
                AudioPreparationCacheHits = 2,
                PreparedAudioBytes = 4096,
                CurrentSongTimeMs = 1500.0,
                LastLaneHitLane = 3,
                LastLaneHitButtonId = "MIDI.38",
                LastLaneHitSongTimeMs = 1490.0,
                ScoreSaveStatus = "Failed",
                ScoreSaveError = "database busy"
            };

            var json = JsonSerializer.Serialize(
                snapshot,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.Equal(75, root.GetProperty("playSpeedPercent").GetInt32());
            Assert.Equal(-3, root.GetProperty("pitchSemitones").GetInt32());
            Assert.True(root.GetProperty("playbackProfileFrozen").GetBoolean());
            Assert.Equal(5, root.GetProperty("audioPreparationCompleted").GetInt32());
            Assert.Equal(5, root.GetProperty("audioPreparationTotal").GetInt32());
            Assert.Equal(2, root.GetProperty("audioPreparationCacheHits").GetInt32());
            Assert.Equal(4096L, root.GetProperty("preparedAudioBytes").GetInt64());
            Assert.Equal(1500.0, root.GetProperty("currentSongTimeMs").GetDouble());
            Assert.Equal(3, root.GetProperty("lastLaneHitLane").GetInt32());
            Assert.Equal(
                "MIDI.38",
                root.GetProperty("lastLaneHitButtonId").GetString());
            Assert.Equal(
                1490.0,
                root.GetProperty("lastLaneHitSongTimeMs").GetDouble());
            Assert.Equal("Failed", root.GetProperty("scoreSaveStatus").GetString());
            Assert.Equal("database busy", root.GetProperty("scoreSaveError").GetString());
        }
    }
}