#nullable enable

using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DTXMania.Game.Lib.Update;

/// <summary>
/// Process-owned update discovery facade. One-shot: <see cref="CheckOnce"/>
/// performs at most one network attempt per process. All rejections are
/// player-silent and surface only as a bounded reason code on the snapshot and
/// in the diagnostic log — never a response body or exception text.
/// </summary>
public sealed class GameUpdateService : IGameUpdateService
{
    private static readonly Regex StableTagPattern =
        new(@"^v\d+\.\d+\.\d+$", RegexOptions.Compiled);

    private static readonly Regex DigestPattern =
        new(@"^sha256:[0-9a-f]{64}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int DiscoveryTimeoutSeconds = 30;

    private readonly GitHubReleaseClient _client;
    private readonly ILogger<GameUpdateService>? _logger;
    private GameUpdateSnapshot _snapshot = GameUpdateSnapshot.NotChecked;
    private Task _checkTask = Task.CompletedTask;
    private int _checkStarted;

    public GameUpdateService(HttpClient httpClient, ILogger<GameUpdateService>? logger = null)
    {
        _client = new GitHubReleaseClient(httpClient);
        _logger = logger;
    }

    public GameUpdateSnapshot GetSnapshot() => _snapshot;

    public Task CheckOnce()
    {
        if (Interlocked.CompareExchange(ref _checkStarted, 1, 0) != 0)
        {
            return _checkTask;
        }

        Publish(new GameUpdateSnapshot(GameUpdateState.Checking, null, null, null, null));
        _checkTask = RunCheckAsync();
        return _checkTask;
    }

    public void BeginUpdate()
    {
        // Download/verify/launch arrives in Task 3; stay an honest no-op until then
        // (the available snapshot is left untouched, so the UI keeps offering Later).
        if (_snapshot.State == GameUpdateState.Available)
        {
            _logger?.LogInformation("Game update: {ReasonCode}", "not_implemented");
        }
    }

    public void DismissForProcess()
    {
        if (_snapshot.State != GameUpdateState.Available)
        {
            return;
        }

        Publish(new GameUpdateSnapshot(GameUpdateState.UpToDate, _snapshot.AvailableVersion, null, null, "dismissed"));
    }

    private async Task RunCheckAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(DiscoveryTimeoutSeconds));
            var release = await _client.GetLatestReleaseAsync(timeout.Token).ConfigureAwait(false);
            Publish(Evaluate(release));
        }
        catch (Exception ex)
        {
            var reason = ex switch
            {
                GameUpdateHttpException http => $"http_{http.StatusCode}",
                JsonException => "invalid_response",
                HttpRequestException or OperationCanceledException => "network_failure",
                _ => "unexpected_failure"
            };
            Publish(new GameUpdateSnapshot(GameUpdateState.DiscoveryFailed, null, null, null, reason));
        }
    }

    private GameUpdateSnapshot Evaluate(GitHubReleaseInfo release)
    {
        var version = ApplicationVersion.Parse(release.TagName);
        if (release.Prerelease || version is null || !StableTagPattern.IsMatch(release.TagName ?? string.Empty))
        {
            return Failed("invalid_tag");
        }

        if (version.CompareTo(ApplicationVersion.Current) <= 0)
        {
            return new GameUpdateSnapshot(GameUpdateState.UpToDate, null, null, null, "not_newer");
        }

        var display = $"{version.Major}.{version.Minor}.{version.Build}";
        var assetName = $"DTXMania-Setup-{display}.exe";
        var asset = release.Assets?.Find(a => a.Name == assetName);
        if (asset is null)
        {
            return Failed("asset_missing");
        }

        if (string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
        {
            return Failed("invalid_response");
        }

        if (asset.Digest is null || !DigestPattern.IsMatch(asset.Digest))
        {
            return Failed("digest_missing");
        }

        return new GameUpdateSnapshot(
            GameUpdateState.Available,
            display,
            asset.BrowserDownloadUrl,
            asset.Digest.ToLowerInvariant(),
            null);
    }

    private static GameUpdateSnapshot Failed(string reason) =>
        new(GameUpdateState.DiscoveryFailed, null, null, null, reason);

    private void Publish(GameUpdateSnapshot snapshot)
    {
        _snapshot = snapshot;
        if (snapshot.State == GameUpdateState.Checking)
        {
            return;
        }

        var detail = snapshot.ReasonCode ?? $"available_v{snapshot.AvailableVersion}";
        _logger?.LogInformation("Game update: {ReasonCode}", detail);
    }
}
