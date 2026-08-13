using DTXMania.VideoRecorder.Media;

namespace DTXMania.VideoRecorder.Workflow;

/// <summary>
/// Owns the narrow post-verification success boundary. Cancellation is checked
/// after verification returns and before any completion or sandbox deletion so
/// a late Ctrl+C cannot turn a copy in progress into a successful run.
/// </summary>
internal static class RecordFinalization
{
    internal static async Task<RecordingArtifactVerification> CompleteAsync(
        FinalizationCallbacks callbacks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callbacks);
        ArgumentNullException.ThrowIfNull(callbacks.VerifyAndPublish);
        ArgumentNullException.ThrowIfNull(callbacks.RecordArtifact);
        ArgumentNullException.ThrowIfNull(callbacks.MarkCompleted);
        ArgumentNullException.ThrowIfNull(callbacks.WriteDiagnostics);
        ArgumentNullException.ThrowIfNull(callbacks.DeleteSandbox);

        var artifact = await callbacks.VerifyAndPublish(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        callbacks.RecordArtifact(artifact);
        cancellationToken.ThrowIfCancellationRequested();
        callbacks.MarkCompleted();
        await callbacks.WriteDiagnostics().ConfigureAwait(false);
        await callbacks.DeleteSandbox().ConfigureAwait(false);
        return artifact;
    }
}

/// <summary>
/// The finalization callbacks invoked by <see cref="RecordFinalization.CompleteAsync"/>
/// in a fixed order: verifyAndPublish -> recordArtifact -> markCompleted ->
/// writeDiagnostics -> deleteSandbox.
/// </summary>
internal sealed record FinalizationCallbacks
{
    public required Func<CancellationToken, Task<RecordingArtifactVerification>> VerifyAndPublish { get; init; }
    public required Action<RecordingArtifactVerification> RecordArtifact { get; init; }
    public required Action MarkCompleted { get; init; }
    public required Func<Task> WriteDiagnostics { get; init; }
    public required Func<Task> DeleteSandbox { get; init; }
}
