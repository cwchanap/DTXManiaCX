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
        Func<CancellationToken, Task<RecordingArtifactVerification>> verifyAndPublish,
        CancellationToken cancellationToken,
        Action<RecordingArtifactVerification> recordArtifact,
        Action markCompleted,
        Func<Task> writeDiagnostics,
        Func<Task> deleteSandbox)
    {
        ArgumentNullException.ThrowIfNull(verifyAndPublish);
        ArgumentNullException.ThrowIfNull(recordArtifact);
        ArgumentNullException.ThrowIfNull(markCompleted);
        ArgumentNullException.ThrowIfNull(writeDiagnostics);
        ArgumentNullException.ThrowIfNull(deleteSandbox);

        var artifact = await verifyAndPublish(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        recordArtifact(artifact);
        cancellationToken.ThrowIfCancellationRequested();
        markCompleted();
        await writeDiagnostics().ConfigureAwait(false);
        await deleteSandbox().ConfigureAwait(false);
        return artifact;
    }
}
