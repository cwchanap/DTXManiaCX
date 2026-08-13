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
                new FinalizationCallbacks
                {
                    VerifyAndPublish = _ =>
                    {
                        cancellation.Cancel();
                        return Task.FromResult(artifact);
                    },
                    RecordArtifact = _ => recorded = true,
                    MarkCompleted = () => completed = true,
                    WriteDiagnostics = () =>
                    {
                        diagnosticsWritten = true;
                        return Task.CompletedTask;
                    },
                    DeleteSandbox = () =>
                    {
                        sandboxDeleted = true;
                        return Task.CompletedTask;
                    }
                },
                cancellation.Token));

        Assert.False(recorded);
        Assert.False(completed);
        Assert.False(diagnosticsWritten);
        Assert.False(sandboxDeleted);
    }

    [Fact]
    public async Task CompleteAsync_WhenNotCanceled_ShouldRunCallbacksInOrder()
    {
        var order = new List<string>();
        var artifact = new RecordingArtifactVerification(
            "raw.mp4",
            "published.mp4",
            Warning: null);

        var result = await RecordFinalization.CompleteAsync(
            new FinalizationCallbacks
            {
                VerifyAndPublish = _ =>
                {
                    order.Add("verifyAndPublish");
                    return Task.FromResult(artifact);
                },
                RecordArtifact = _ => order.Add("recordArtifact"),
                MarkCompleted = () => order.Add("markCompleted"),
                WriteDiagnostics = () =>
                {
                    order.Add("writeDiagnostics");
                    return Task.CompletedTask;
                },
                DeleteSandbox = () =>
                {
                    order.Add("deleteSandbox");
                    return Task.CompletedTask;
                }
            },
            CancellationToken.None);

        Assert.Same(artifact, result);
        Assert.Equal(
            new[]
            {
                "verifyAndPublish",
                "recordArtifact",
                "markCompleted",
                "writeDiagnostics",
                "deleteSandbox"
            },
            order);
    }
}
