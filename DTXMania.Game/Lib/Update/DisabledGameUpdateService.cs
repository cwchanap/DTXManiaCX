#nullable enable

using System.Threading.Tasks;

namespace DTXMania.Game.Lib.Update;

/// <summary>
/// Null-object <see cref="IGameUpdateService"/> used as the <see
/// cref="Stage.IStageGame.GameUpdateService"/> default: the snapshot stays
/// <see cref="GameUpdateState.NotChecked"/> forever and every action is a no-op.
/// </summary>
public sealed class DisabledGameUpdateService : IGameUpdateService
{
    public static DisabledGameUpdateService Instance { get; } = new();

    private DisabledGameUpdateService()
    {
    }

    public GameUpdateSnapshot GetSnapshot() => GameUpdateSnapshot.NotChecked;

    public Task CheckOnce() => Task.CompletedTask;

    public void BeginUpdate()
    {
    }

    public void DismissForProcess()
    {
    }
}
