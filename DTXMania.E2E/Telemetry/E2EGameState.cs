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

    public int TotalNotes => GetTelemetryInt("totalNotes") ?? 0;

    public int TotalJudgements => GetTelemetryInt("totalJudgements") ?? 0;

    public int Score => GetTelemetryInt("score") ?? 0;

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

    private bool? GetTelemetryBool(string propertyName) =>
        Telemetry.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
