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

    private const int DownloadTimeoutMinutes = 10;

    private const string DigestPrefix = "sha256:";

    private readonly HttpClient _httpClient;
    private readonly GitHubReleaseClient _client;
    private readonly WindowsUpdateInstallerLauncher _launcher;
    private readonly ILogger<GameUpdateService>? _logger;
    private readonly object _checkGate = new();
    private GameUpdateSnapshot _snapshot = GameUpdateSnapshot.NotChecked;
    private Task? _checkTask;
    private Task _updateTask = Task.CompletedTask;

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
        lock (_checkGate)
        {
            if (_checkTask is not null)
            {
                return _checkTask;
            }

            Publish(new GameUpdateSnapshot(GameUpdateState.Checking, null, null, null, null));
            _checkTask = RunCheckAsync();
            return _checkTask;
        }
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
        // Declines the offer in either reviewable state — a fresh offer AND a
        // retryable failure — so Later genuinely suppresses the banner/panel for
        // the rest of the process instead of leaving Failed re-surfacing.
        if (_snapshot.State is not (GameUpdateState.Available or GameUpdateState.Failed))
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
    /// After a successful start the installer process's first exit is the only
    /// terminal signal: nonzero (a refused/cancelled internal elevation, whenever
    /// it happens) publishes retryable Failed and keeps the game alive; zero
    /// publishes InstallerCommitted. While the handoff is undecided the game must
    /// NOT exit — Inno's /CLOSEAPPLICATIONS closes it once an install proceeds.
    /// </summary>
    private async Task RunUpdateAsync(GameUpdateSnapshot offered)
    {
        var tempPath = TempInstallerPathForVersion(offered.AvailableVersion!);
        try
        {
            TryDeleteStaleInstaller(tempPath);

            using var download = new CancellationTokenSource(TimeSpan.FromMinutes(DownloadTimeoutMinutes));
            using var response = await _httpClient
                .GetAsync(offered.InstallerUrl!, HttpCompletionOption.ResponseHeadersRead, download.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Publish(UpdateFailed(offered, $"http_{(int)response.StatusCode}"));
                return;
            }

            var (verifiedFile, percent) = await DownloadAndVerifyAsync(response, offered, tempPath, download.Token).ConfigureAwait(false);
            if (verifiedFile is null)
            {
                return; // digest mismatch already published
            }

            // TOCTOU hold: the verified bytes' read-only handle (FileShare.Read) stays open
            // until the launcher has started (or refused) the process — denying every other
            // process write/delete access between verification and launch, while CreateProcess
            // can still map the image (its FILE_SHARE_READ|FILE_SHARE_DELETE share permits our
            // read access; FILE_SHARE_READ permits its read+execute access).
            Task<int>? exitObservation;
            try
            {
                exitObservation = _launcher.Launch(tempPath);
            }
            finally
            {
                await verifiedFile.DisposeAsync().ConfigureAwait(false);
            }

            if (exitObservation is null)
            {
                Publish(UpdateFailed(offered, "launch_failed"));
                return;
            }

            // Handoff in flight — the game stays alive here. The bootstrapper may
            // still be parked on an unanswered internal UAC prompt, so neither
            // process creation nor continued liveness is commitment; the title
            // stage must not exit on this state.
            Publish(offered with { State = GameUpdateState.InstallerLaunched, DownloadPercent = percent });

            // The started process's first exit resolves the handoff whenever the
            // player answers the elevation prompt: nonzero = refused/cancelled
            // (retryable, game alive), zero = committed elevated respawn or a
            // finished no-elevation install.
            var exitCode = await exitObservation.ConfigureAwait(false);
            Publish(exitCode == 0
                ? offered with { State = GameUpdateState.InstallerCommitted, DownloadPercent = percent }
                : UpdateFailed(offered, "launch_failed"));
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
    /// Streams the installer to the version-scoped temp path (write handle, no
    /// incremental hash), flushes and closes it, then SHA-256-verifies the bytes
    /// on disk through a second, read-only pass. On a digest match the read
    /// handle is returned still open — FileAccess.Read + FileShare.Read denies
    /// every other process write/delete access after verification, while
    /// CreateProcess can still open the image, so the launcher runs against the
    /// verified bytes with no substitution window; the caller must dispose it
    /// only after the launcher has run. Returns (null, null) on a digest
    /// mismatch (handle already disposed, failed snapshot published). Percent
    /// stays null when no Content-Length was available.
    /// </summary>
    private async Task<(FileStream? VerifiedFile, int? Percent)> DownloadAndVerifyAsync(HttpResponseMessage response, GameUpdateSnapshot offered, string tempPath, CancellationToken cancellationToken)
    {
        long? totalBytes = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var digest = offered.Sha256Digest!.Substring(DigestPrefix.Length);

        // Pass 1 — write the download. The read pass below is the authoritative
        // digest of the bytes actually on disk, so no incremental hash here.
        int? percent = null;
        using (var file = new FileStream(
            tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
            bufferSize: 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                written += read;

                if (totalBytes is > 0)
                {
                    var current = (int)Math.Clamp(written * 100 / totalBytes.Value, 0, 100);
                    if (current != percent)
                    {
                        percent = current;
                        Publish(offered with { State = GameUpdateState.Downloading, DownloadPercent = current });
                    }
                }
            }

            // Flush so the read pass hashes (and the launcher reads) complete bytes.
            await file.FlushAsync(cancellationToken).ConfigureAwait(false);
        } // write handle closed before verify/launch; a retry always sees a fresh path

        // Pass 2 — verify from a read hold we keep open across the launch.
        var readHandle = new FileStream(
            tempPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        FileStream? held = null;
        try
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            int read;
            while ((read = await readHandle.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                hasher.AppendData(buffer, 0, read);
            }

            var actual = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actual, digest, StringComparison.OrdinalIgnoreCase))
            {
                Publish(UpdateFailed(offered, "digest_mismatch"));
                return (null, null);
            }

            held = readHandle;
            return (readHandle, percent);
        }
        finally
        {
            if (held is null)
            {
                await readHandle.DisposeAsync().ConfigureAwait(false);
            }
        }
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
            ?? (snapshot.State switch
            {
                GameUpdateState.InstallerLaunched => "installer_launched",
                GameUpdateState.InstallerCommitted => "installer_committed",
                _ => $"available_v{snapshot.AvailableVersion}"
            });
        _logger?.LogInformation("Game update: {ReasonCode}", detail);
    }
}
