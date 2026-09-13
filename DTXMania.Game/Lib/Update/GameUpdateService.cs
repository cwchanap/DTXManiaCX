#nullable enable

using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
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

    private const string DigestPrefix = "sha256:";

    private readonly HttpClient _httpClient;
    private readonly GitHubReleaseClient _client;
    private readonly WindowsUpdateInstallerLauncher _launcher;
    private readonly ILogger<GameUpdateService>? _logger;
    private GameUpdateSnapshot _snapshot = GameUpdateSnapshot.NotChecked;
    private Task _checkTask = Task.CompletedTask;
    private Task _updateTask = Task.CompletedTask;
    private int _checkStarted;

    public GameUpdateService(HttpClient httpClient, ILogger<GameUpdateService>? logger = null)
        : this(httpClient, logger, launcher: null)
    {
    }

    internal GameUpdateService(
        HttpClient httpClient,
        ILogger<GameUpdateService>? logger,
        WindowsUpdateInstallerLauncher? launcher)
    {
        _httpClient = httpClient;
        _client = new GitHubReleaseClient(httpClient);
        _launcher = launcher ?? new WindowsUpdateInstallerLauncher();
        _logger = logger;
    }

    /// <summary>The in-flight (or most recent) BeginUpdate task, for awaiting in tests.</summary>
    internal Task UpdateTask => _updateTask;

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

    /// <summary>One deterministic, version-scoped temp path for the installer download.</summary>
    internal static string TempInstallerPathForVersion(string version) =>
        Path.Combine(Path.GetTempPath(), $"DTXMania-Setup-{version}.tmp.exe");

    public void BeginUpdate()
    {
        var offered = _snapshot;
        if (offered.State is not (GameUpdateState.Available or GameUpdateState.Failed)
            || string.IsNullOrEmpty(offered.InstallerUrl)
            || string.IsNullOrEmpty(offered.Sha256Digest))
        {
            return;
        }

        Publish(offered with { State = GameUpdateState.Downloading, ReasonCode = null });
        _updateTask = RunUpdateAsync(offered);
    }

    public void DismissForProcess()
    {
        if (_snapshot.State != GameUpdateState.Available)
        {
            return;
        }

        Publish(new GameUpdateSnapshot(GameUpdateState.UpToDate, _snapshot.AvailableVersion, null, null, "dismissed"));
    }

    /// <summary>
    /// Streams the installer to the version-scoped temp path, SHA-256-verifies
    /// the final bytes, and launches only on a match. Any download/write/verify
    /// failure — or a refused process start — publishes a retryable Failed; the
    /// launcher is only ever called with verified bytes, and each retry starts
    /// from a fresh temp file (best-effort stale delete before every attempt).
    /// </summary>
    private async Task RunUpdateAsync(GameUpdateSnapshot offered)
    {
        var tempPath = TempInstallerPathForVersion(offered.AvailableVersion!);
        try
        {
            TryDeleteStaleInstaller(tempPath);

            using var response = await _httpClient
                .GetAsync(offered.InstallerUrl!, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Publish(UpdateFailed(offered, $"http_{(int)response.StatusCode}"));
                return;
            }

            if (await DownloadAndVerifyAsync(response, offered, tempPath).ConfigureAwait(false) is not (true, var percent))
            {
                return; // digest mismatch already published
            }

            if (!_launcher.Launch(tempPath))
            {
                Publish(UpdateFailed(offered, "launch_failed"));
                return;
            }

            Publish(offered with { State = GameUpdateState.InstallerLaunched, DownloadPercent = percent });
        }
        catch (Exception exception)
        {
            var reason = exception switch
            {
                HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException => "download_failed",
                _ => "unexpected_failure"
            };
            Publish(UpdateFailed(offered, reason));
        }
    }

    /// <summary>
    /// Returns (true, lastPublishedPercent) on a digest match — percent stays
    /// null when no Content-Length was available — and (false, null) on mismatch.
    /// </summary>
    private async Task<(bool Verified, int? Percent)> DownloadAndVerifyAsync(HttpResponseMessage response, GameUpdateSnapshot offered, string tempPath)
    {
        long? totalBytes = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await using var file = new FileStream(
            tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = new byte[81920];
        var digest = offered.Sha256Digest!.Substring(DigestPrefix.Length);
        long written = 0;
        int? lastPercent = null;
        int read;
        while ((read = await source.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            hasher.AppendData(buffer, 0, read);
            written += read;

            if (totalBytes is > 0)
            {
                var percent = (int)Math.Clamp(written * 100 / totalBytes.Value, 0, 100);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    Publish(offered with { State = GameUpdateState.Downloading, DownloadPercent = percent });
                }
            }
        }

        var actual = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(actual, digest, StringComparison.OrdinalIgnoreCase))
        {
            Publish(UpdateFailed(offered, "digest_mismatch"));
            return (false, null);
        }

        return (true, lastPercent);
    }

    private static void TryDeleteStaleInstaller(string path)
    {
        // Best-effort: if a stale file survives, CreateNew below fails loudly
        // into the retryable Failed path instead of silently overwriting it.
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    // Keeps version/url/digest so BeginUpdate can retry; only the percent resets.
    private static GameUpdateSnapshot UpdateFailed(GameUpdateSnapshot offered, string reason) =>
        offered with { State = GameUpdateState.Failed, ReasonCode = reason, DownloadPercent = null };

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
        if (snapshot.State is GameUpdateState.Checking or GameUpdateState.Downloading)
        {
            return;
        }

        var detail = snapshot.ReasonCode
            ?? (snapshot.State == GameUpdateState.InstallerLaunched ? "installer_launched" : $"available_v{snapshot.AvailableVersion}");
        _logger?.LogInformation("Game update: {ReasonCode}", detail);
    }
}
