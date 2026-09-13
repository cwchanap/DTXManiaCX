#nullable enable

namespace DTXMania.Game.Lib.Update;

/// <summary>
/// Pure composition predicate deciding whether the process-owned Windows
/// auto-update runs at all. Enabled only on Windows outside the automation/E2E
/// harness (whose launch token must never trigger a real update check).
/// </summary>
public static class GameUpdateComposition
{
    public static bool ShouldEnable(bool isWindows, string? launchToken) =>
        isWindows && string.IsNullOrEmpty(launchToken);
}
