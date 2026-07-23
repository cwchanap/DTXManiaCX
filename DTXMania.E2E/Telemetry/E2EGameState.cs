using System.Text.Json;

namespace DTXMania.E2E.Telemetry;

public sealed class E2EGameState
{
    public string CurrentStage { get; set; } = string.Empty;

    public Dictionary<string, JsonElement> CustomData { get; set; } = new();

    public JsonElement Telemetry
    {
        get
        {
            if (CustomData.TryGetValue("telemetry", out var telemetry))
                return telemetry;

            // Return a default empty object so downstream accessors degrade gracefully
            // instead of throwing when telemetry is absent (e.g. game not fully started).
            return _emptyTelemetry;
        }
    }

    private static readonly JsonElement _emptyTelemetry =
        JsonDocument.Parse("{}").RootElement;

    public string StageType => GetTelemetryString("stageType") ?? string.Empty;

    public string? SelectedSongTitle => GetTelemetryString("selectedSongTitle");

    public string? CompletionReason => GetTelemetryString("completionReason");

    public string? ScoreSaveStatus => GetTelemetryString("scoreSaveStatus");

    public string? ScoreSaveError => GetTelemetryString("scoreSaveError");

    public int TotalNotes => GetTelemetryInt("totalNotes") ?? 0;

    public int TotalJudgements => GetTelemetryInt("totalJudgements") ?? 0;

    public int Score => GetTelemetryInt("score") ?? 0;

    public int MaxCombo => GetTelemetryInt("maxCombo") ?? 0;

    public int MissCount => GetTelemetryInt("missCount") ?? 0;

    public bool PerformanceReady => GetTelemetryBool("performanceReady") ?? false;

    public int PlaySpeedPercent => GetTelemetryInt("playSpeedPercent") ?? 100;

    public int PitchSemitones => GetTelemetryInt("pitchSemitones") ?? 0;

    public bool PlaybackProfileFrozen => GetTelemetryBool("playbackProfileFrozen") ?? false;

    public int AudioPreparationCompleted => GetTelemetryInt("audioPreparationCompleted") ?? 0;

    public int AudioPreparationTotal => GetTelemetryInt("audioPreparationTotal") ?? 0;

    public int AudioPreparationCacheHits => GetTelemetryInt("audioPreparationCacheHits") ?? 0;

    public long PreparedAudioBytes => GetTelemetryLong("preparedAudioBytes") ?? 0L;

    public bool AutoPlayEnabled => GetTelemetryBool("autoPlayEnabled") ?? false;

    public double CurrentSongTimeMs => GetTelemetryDouble("currentSongTimeMs") ?? 0.0;

    public int? LastLaneHitLane => GetTelemetryInt("lastLaneHitLane");

    public string? LastLaneHitButtonId => GetTelemetryString("lastLaneHitButtonId");

    public double? LastLaneHitSongTimeMs => GetTelemetryDouble("lastLaneHitSongTimeMs");

    public bool ClearFlag => GetTelemetryBool("clearFlag") ?? false;

    public bool StageCompleted => GetTelemetryBool("stageCompleted") ?? false;

    private string? GetTelemetryString(string propertyName) =>
        Telemetry.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private int? GetTelemetryInt(string propertyName) =>
        Telemetry.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private double? GetTelemetryDouble(string propertyName) =>
        Telemetry.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
            : null;

    private long? GetTelemetryLong(string propertyName) =>
        Telemetry.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;

    private bool? GetTelemetryBool(string propertyName) =>
        Telemetry.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
