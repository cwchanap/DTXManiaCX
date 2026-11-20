using DTXMania.Game.Lib.Stage.Performance;

namespace DTXMania.Test.Helpers;

public class IsolatedStageAudioCacheTests
{
    [Fact]
    public async Task Release_WaitsForEveryWorkerBeforeDeletingOwnedRoot()
    {
        var cache = new IsolatedStageAudioCache();
        _ = cache.Factory()();
        using var empty = await PrepareEmptySet();
        var first = new TaskCompletionSource<PreparedGameplayAudioSet?>();
        var second = new TaskCompletionSource<PreparedGameplayAudioSet?>();
        _ = cache.Track(first.Task);
        _ = cache.Track(second.Task);
        try
        {
            cache.Release();
            Assert.True(Directory.Exists(cache.Root));
            Assert.Throws<InvalidOperationException>(() => cache.Factory());
            first.SetResult(empty);
            Assert.False(cache.Cleanup!.IsCompleted);
            Assert.True(Directory.Exists(cache.Root));
            second.SetResult(empty);
        }
        finally
        {
            first.TrySetResult(empty);
            second.TrySetResult(empty);
            cache.Release();
            await cache.Cleanup!.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.False(Directory.Exists(cache.Root));
    }

    [Fact]
    public async Task Release_WhenWorkerReturnsNull_LeavesRootForPossibleDetachedCacheOperation()
    {
        var cache = new IsolatedStageAudioCache();
        _ = cache.Factory()();
        var worker = new TaskCompletionSource<PreparedGameplayAudioSet?>();
        _ = cache.Track(worker.Task);
        try
        {
            cache.Release();
            Assert.True(Directory.Exists(cache.Root));
            worker.SetResult(null);
            await cache.Cleanup!.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(Directory.Exists(cache.Root));
        }
        finally
        {
            worker.TrySetResult(null);
            cache.Release();
            await cache.Cleanup!.WaitAsync(TimeSpan.FromSeconds(5));
            // The controlled worker above never started a detached cache operation.
            Directory.Delete(cache.Root, recursive: true);
        }
    }

    private static Task<PreparedGameplayAudioSet> PrepareEmptySet() =>
        PreparedGameplayAudioSet.PrepareAsync(
            null, Array.Empty<string>(), new Dictionary<string, string>(),
            new PlaybackModifiers(100, 0), null, null, null, CancellationToken.None);
}
