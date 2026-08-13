using System.Text.Json;
using System.Text.Json.Serialization;
using DTXMania.Automation.Telemetry;

namespace DTXMania.VideoRecorder.Diagnostics;

/// <summary>
/// Writes the recorder's deliberately small evidence bundle. This is a
/// recorder-local writer rather than a reusable redaction service: only the
/// known API key and OBS password are removed from the three output files.
/// </summary>
internal sealed class RecorderDiagnostics
{
    private readonly string _apiKey;
    private readonly string _obsPassword;
    private readonly List<RecorderStepEvidence> _steps = new();
    private readonly List<RecorderObsOutcome> _obsOutcomes = new();
    private RecorderRunStatus _status = RecorderRunStatus.Running;
    private string? _lastCompletedStep;
    private string? _failure;
    private string? _failureType;
    private string? _retainedSandboxPath;
    private string? _rawOutputPath;
    private string? _publishedPath;
    private string? _verifierWarning;

    public RecorderDiagnostics(
        string outputDirectory,
        string runId,
        string? apiKey = null,
        string? obsPassword = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var normalizedRunId = Path.GetFileName(runId);
        if (!string.Equals(normalizedRunId, runId, StringComparison.Ordinal) ||
            normalizedRunId is "." or "..")
        {
            throw new ArgumentException("runId must be a single path component.", nameof(runId));
        }

        OutputDirectory = Path.GetFullPath(outputDirectory);
        RunId = runId;
        DiagnosticsDirectory = Path.Combine(OutputDirectory, "diagnostics", RunId);
        _apiKey = apiKey ?? string.Empty;
        _obsPassword = obsPassword ?? string.Empty;
    }

    public string OutputDirectory { get; }

    public string RunId { get; }

    public string DiagnosticsDirectory { get; }

    public void RecordStep(string name, GameStateSnapshot? snapshot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var evidence = new RecorderStepEvidence(
            Sanitize(name)!,
            DateTimeOffset.UtcNow,
            snapshot is null ? null : SelectTelemetry(snapshot));
        _steps.Add(evidence);
        if (_status == RecorderRunStatus.Running)
            _lastCompletedStep = evidence.Name;
    }

    public void RecordObsOutcome(string operation, bool succeeded, string? detail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        _obsOutcomes.Add(new RecorderObsOutcome(
            Sanitize(operation)!,
            succeeded,
            Sanitize(detail)));
    }

    public void SetRawOutputPath(string? path) => _rawOutputPath = Sanitize(path);

    public void SetPublishedPath(string? path) => _publishedPath = Sanitize(path);

    public void SetVerifierWarning(string? warning) => _verifierWarning = Sanitize(warning);

    public void MarkCompleted()
    {
        _status = RecorderRunStatus.Completed;
        _failure = null;
        _failureType = null;
        _retainedSandboxPath = null;
    }

    public void MarkFailure(Exception exception, string? retainedSandboxPath = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _status = RecorderRunStatus.Failed;
        _failure = Sanitize(exception.Message);
        _failureType = Sanitize(exception.GetType().FullName);
        _retainedSandboxPath = Sanitize(retainedSandboxPath);
    }

    public async Task WriteAsync(
        string? cxStandardOutput,
        string? cxStandardError,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(DiagnosticsDirectory);

        var run = new RecorderRunEvidence(
            RunId,
            _status.ToString(),
            _steps,
            _obsOutcomes,
            _rawOutputPath,
            _publishedPath,
            _verifierWarning,
            _failure,
            _failureType,
            _lastCompletedStep,
            _retainedSandboxPath,
            DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(
            run,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

        await File.WriteAllTextAsync(
                Path.Combine(DiagnosticsDirectory, "run.json"),
                json,
                cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(
                Path.Combine(DiagnosticsDirectory, "cx-stdout.log"),
                Sanitize(cxStandardOutput),
                cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(
                Path.Combine(DiagnosticsDirectory, "cx-stderr.log"),
                Sanitize(cxStandardError),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private RecorderTelemetryEvidence SelectTelemetry(GameStateSnapshot snapshot) =>
        new(
            Sanitize(snapshot.StageType)!,
            Sanitize(snapshot.SelectedSongTitle),
            Sanitize(snapshot.PreparedPreviewState),
            snapshot.PreparedPreviewElapsedMs,
            snapshot.PerformanceReady,
            snapshot.AutoPlayEnabled,
            snapshot.TotalNotes,
            snapshot.TotalJudgements,
            snapshot.ClearFlag,
            snapshot.StageCompleted,
            Sanitize(snapshot.CompletionReason),
            snapshot.Score,
            snapshot.MaxCombo,
            snapshot.MissCount,
            snapshot.PlaySpeedPercent,
            snapshot.PitchSemitones,
            snapshot.PlaybackProfileFrozen,
            snapshot.AudioPreparationCompleted,
            snapshot.AudioPreparationTotal,
            snapshot.AudioPreparationCacheHits,
            snapshot.PreparedAudioBytes,
            Sanitize(snapshot.PreparedChartIdentity));

    private string? Sanitize(string? value)
    {
        if (value is null)
            return null;

        var sanitized = value;
        if (!string.IsNullOrEmpty(_apiKey))
            sanitized = sanitized.Replace(_apiKey, "<redacted>", StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(_obsPassword))
            sanitized = sanitized.Replace(_obsPassword, "<redacted>", StringComparison.Ordinal);
        return sanitized;
    }

    private enum RecorderRunStatus
    {
        Running,
        Completed,
        Failed
    }

    private sealed record RecorderStepEvidence(
        string Name,
        DateTimeOffset CompletedAtUtc,
        RecorderTelemetryEvidence? Telemetry);

    private sealed record RecorderObsOutcome(
        string Operation,
        bool Succeeded,
        string? Detail);

    private sealed record RecorderTelemetryEvidence(
        string StageType,
        string? SelectedSongTitle,
        string? PreparedPreviewState,
        double PreparedPreviewElapsedMs,
        bool PerformanceReady,
        bool AutoPlayEnabled,
        int TotalNotes,
        int TotalJudgements,
        bool ClearFlag,
        bool StageCompleted,
        string? CompletionReason,
        int Score,
        int MaxCombo,
        int MissCount,
        int PlaySpeedPercent,
        int PitchSemitones,
        bool PlaybackProfileFrozen,
        int AudioPreparationCompleted,
        int AudioPreparationTotal,
        int AudioPreparationCacheHits,
        long PreparedAudioBytes,
        string? PreparedChartIdentity);

    private sealed record RecorderRunEvidence(
        string RunId,
        string Status,
        IReadOnlyList<RecorderStepEvidence> Steps,
        IReadOnlyList<RecorderObsOutcome> ObsOutcomes,
        string? RawOutputPath,
        string? PublishedPath,
        string? VerifierWarning,
        string? Failure,
        string? FailureType,
        string? LastCompletedStep,
        string? RetainedSandboxPath,
        DateTimeOffset WrittenAtUtc);
}
