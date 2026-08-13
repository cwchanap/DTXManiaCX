using DTXMania.VideoRecorder.Media;
using DTXMania.VideoRecorder.Workflow;

namespace DTXMania.VideoRecorder.Tests.Workflow;

public sealed class RecordFinalizationTests
{
    [Fact]
    public async Task CompleteAsync_WhenCanceledAfterVerification_ShouldNotMarkCompleteOrDeleteSandbox()
    {
        using var cancellation = new CancellationTokenSource();
        var artifact = new RecordingArtifactVerification(
            "raw.mp4",
            "published.mp4",
            Warning: null);
        var recorded = false;
        var completed = false;
        var diagnosticsWritten = false;
        var sandboxDeleted = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RecordFinalization.CompleteAsync(
                _ =>
                {
                    cancellation.Cancel();
                    return Task.FromResult(artifact);
                },
                cancellation.Token,
                _ => recorded = true,
                () => completed = true,
                () =>
                {
                    diagnosticsWritten = true;
                    return Task.CompletedTask;
                },
                () =>
                {
                    sandboxDeleted = true;
                    return Task.CompletedTask;
                }));

        Assert.False(recorded);
        Assert.False(completed);
        Assert.False(diagnosticsWritten);
        Assert.False(sandboxDeleted);
    }
}
