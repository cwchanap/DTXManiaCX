using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;

namespace DTXMania.Test.Helpers;

// Tracks existing stage workers, not their scheduling; failed/canceled cache operations
// can outlive their waiter, so their roots must be left intact.
internal sealed class IsolatedStageAudioCache
{
    private readonly List<Task<PreparedGameplayAudioSet?>> _workers = new();
    private readonly TaskCompletionSource<PlaybackAudioVariantCache> _created =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lazy<string> _root = new(() =>
        Directory.CreateTempSubdirectory("DTXMania_StageAudioCache_").FullName);

    public string Root => _root.Value;

    public Task? Cleanup { get; private set; }
    public Task<PlaybackAudioVariantCache> Created => _created.Task;

    public Func<PlaybackAudioVariantCache> Factory()
    {
        if (Cleanup != null)
            throw new InvalidOperationException("A disposed fixture cannot create another cache.");
        var root = Root;
        var created = _created;
        return () =>
        {
            var cache = new PlaybackAudioVariantCache(root);
            created.TrySetResult(cache);
            return cache;
        };
    }

    public Task<PreparedGameplayAudioSet?> Track(Task<PreparedGameplayAudioSet?> worker)
    {
        _workers.Add(worker);
        return worker;
    }

    public Task<PreparedGameplayAudioSet?[]> Workers => Task.WhenAll(_workers);

    public void Release()
    {
        Cleanup ??= _root.IsValueCreated
            ? ReleaseAfterWorkersAsync(Workers, Root)
            : Task.CompletedTask;
    }

    private static async Task ReleaseAfterWorkersAsync(
        Task<PreparedGameplayAudioSet?[]> workers, string root)
    {
        var results = await workers.ConfigureAwait(false);
        if (results.Any(result => result == null))
            return;

        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
