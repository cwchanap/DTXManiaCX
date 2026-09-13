#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Update;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DTXMania.Test.Update;

public class GameUpdateServiceTests
{
    private const string Digest64Hex = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static Version NextNewerVersion() =>
        new(ApplicationVersion.Current.Major + 10, ApplicationVersion.Current.Minor, ApplicationVersion.Current.Build);

    private static string ReleaseJson(string tag, bool prerelease, string? assetName = null, string? digest = null)
    {
        var assets = assetName is null
            ? "[]"
            : $"[{{\"name\":\"{assetName}\",\"browser_download_url\":\"https://github.com/example/download\",\"digest\":{(digest is null ? "null" : $"\"{digest}\"")}}}]";
        return $"{{\"tag_name\":\"{tag}\",\"prerelease\":{(prerelease ? "true" : "false")},\"assets\":{assets}}}";
    }

    private static string ReleaseJsonWithAssets(string tag, string assetsJson) =>
        $"{{\"tag_name\":\"{tag}\",\"prerelease\":false,\"assets\":{assetsJson}}}";

    private static (GameUpdateService Service, FakeGitHubHandler Handler) CreateService(
        HttpResponseMessage? response = null,
        Exception? throwOnSend = null,
        ILogger<GameUpdateService>? logger = null)
    {
        var handler = new FakeGitHubHandler(response, throwOnSend);
        var service = new GameUpdateService(new HttpClient(handler), logger);
        return (service, handler);
    }

    private static GameUpdateSnapshot Check(HttpResponseMessage? response)
    {
        var (service, _) = CreateService(response);
        service.CheckOnce().GetAwaiter().GetResult();
        return service.GetSnapshot();
    }

    [Fact]
    public void GetSnapshot_WhenNeverChecked_ShouldReturnNotChecked()
    {
        var (service, _) = CreateService();

        var snapshot = service.GetSnapshot();

        Assert.Equal(GameUpdateState.NotChecked, snapshot.State);
        Assert.Null(snapshot.ReasonCode);
    }

    [Fact]
    public void CheckOnce_WhenNewerStableReleaseIsValid_ShouldPublishAvailable()
    {
        var version = NextNewerVersion();
        var display = $"{version.Major}.{version.Minor}.{version.Build}";
        var json = ReleaseJson("v" + display, prerelease: false, $"DTXMania-Setup-{display}.exe", "sha256:" + Digest64Hex);

        var (service, handler) = CreateService(JsonResponse(json));
        service.CheckOnce().GetAwaiter().GetResult();

        var snapshot = service.GetSnapshot();
        Assert.Equal(GameUpdateState.Available, snapshot.State);
        Assert.Equal(display, snapshot.AvailableVersion);
        Assert.Equal("https://github.com/example/download", snapshot.InstallerUrl);
        Assert.Equal("sha256:" + Digest64Hex, snapshot.Sha256Digest);
        Assert.Null(snapshot.ReasonCode);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            $"https://api.github.com/repos/{GitHubCrashIssueBuilder.TargetOwner}/{GitHubCrashIssueBuilder.TargetRepository}/releases/latest",
            request.RequestUri?.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256:short")]
    [InlineData("md5:" + Digest64Hex)]
    [InlineData("sha256:zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void CheckOnce_WhenDigestMissingOrMalformed_ShouldFailWithDigestMissing(string? digest)
    {
        var version = NextNewerVersion();
        var display = $"{version.Major}.{version.Minor}.{version.Build}";
        var json = ReleaseJson("v" + display, prerelease: false, $"DTXMania-Setup-{display}.exe", digest);

        var snapshot = Check(JsonResponse(json));

        Assert.Equal(GameUpdateState.DiscoveryFailed, snapshot.State);
        Assert.Equal("digest_missing", snapshot.ReasonCode);
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("1.2")]
    [InlineData("1.2.3")]
    [InlineData("V1.2.3")]
    [InlineData("v1.2.3-rc.1")]
    [InlineData("1.2.3.4")]
    [InlineData("")]
    public void CheckOnce_WhenTagIsNotStableVXYZ_ShouldFailWithInvalidTag(string tag)
    {
        var json = ReleaseJson(tag, prerelease: false);

        var snapshot = Check(JsonResponse(json));

        Assert.Equal(GameUpdateState.DiscoveryFailed, snapshot.State);
        Assert.Equal("invalid_tag", snapshot.ReasonCode);
    }

    [Fact]
    public void CheckOnce_WhenReleaseIsPrerelease_ShouldFailWithInvalidTag()
    {
        var version = NextNewerVersion();
        var display = $"{version.Major}.{version.Minor}.{version.Build}";
        var json = ReleaseJson("v" + display, prerelease: true, $"DTXMania-Setup-{display}.exe", "sha256:" + Digest64Hex);

        var snapshot = Check(JsonResponse(json));

        Assert.Equal(GameUpdateState.DiscoveryFailed, snapshot.State);
        Assert.Equal("invalid_tag", snapshot.ReasonCode);
    }

    [Fact]
    public void CheckOnce_WhenReleaseVersionEqualsCurrent_ShouldReportUpToDateWithoutOffer()
    {
        var json = ReleaseJson("v" + ApplicationVersion.Display, prerelease: false);

        var snapshot = Check(JsonResponse(json));

        Assert.Equal(GameUpdateState.UpToDate, snapshot.State);
        Assert.Null(snapshot.AvailableVersion);
        Assert.Equal("not_newer", snapshot.ReasonCode);
    }

    [Fact]
    public void CheckOnce_WhenReleaseVersionIsOlder_ShouldReportUpToDateWithoutOffer()
    {
        var json = ReleaseJson("v0.0.1", prerelease: false);

        var snapshot = Check(JsonResponse(json));

        Assert.Equal(GameUpdateState.UpToDate, snapshot.State);
        Assert.Equal("not_newer", snapshot.ReasonCode);
    }

    [Fact]
    public void CheckOnce_WhenInstallerAssetMissing_ShouldFailWithAssetMissing()
    {
        var version = NextNewerVersion();
        var json = ReleaseJson("v" + $"{version.Major}.{version.Minor}.{version.Build}", prerelease: false, "SomeOther-File.zip");

        var snapshot = Check(JsonResponse(json));

        Assert.Equal(GameUpdateState.DiscoveryFailed, snapshot.State);
        Assert.Equal("asset_missing", snapshot.ReasonCode);
    }

    [Fact]
    public void CheckOnce_WhenMultipleAssetsExist_ShouldSelectExactMatchingSetupAsset()
    {
        var version = NextNewerVersion();
        var display = $"{version.Major}.{version.Minor}.{version.Build}";
        var assetsJson = "[" +
            "{\"name\":\"DTXMania-Setup-9.9.9.exe\",\"browser_download_url\":\"https://github.com/example/wrong-exe\",\"digest\":\"sha256:" + new string('b', 64) + "\"}," +
            "{\"name\":\"DTXMania-Setup-" + display + ".zip\",\"browser_download_url\":\"https://github.com/example/zip\",\"digest\":null}," +
            "{\"name\":\"checksums.txt\",\"browser_download_url\":\"https://github.com/example/checksums\",\"digest\":null}," +
            "{\"name\":\"DTXMania-Setup-" + display + ".exe\",\"browser_download_url\":\"https://github.com/example/correct-exe\",\"digest\":\"sha256:" + Digest64Hex + "\"}" +
        "]";
        var json = ReleaseJsonWithAssets("v" + display, assetsJson);

        var snapshot = Check(JsonResponse(json));

        Assert.Equal(GameUpdateState.Available, snapshot.State);
        Assert.Equal("https://github.com/example/correct-exe", snapshot.InstallerUrl);
        Assert.Equal("sha256:" + Digest64Hex, snapshot.Sha256Digest);
        Assert.Null(snapshot.ReasonCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "http_403")]
    [InlineData(HttpStatusCode.NotFound, "http_404")]
    [InlineData(HttpStatusCode.InternalServerError, "http_500")]
    public void CheckOnce_WhenHttpError_ShouldFailWithStatusReasonCode(HttpStatusCode status, string expectedReason)
    {
        var snapshot = Check(new HttpResponseMessage(status));

        Assert.Equal(GameUpdateState.DiscoveryFailed, snapshot.State);
        Assert.Equal(expectedReason, snapshot.ReasonCode);
    }

    [Fact]
    public void CheckOnce_WhenRequestFails_ShouldFailWithNetworkFailure()
    {
        var (service, _) = CreateService(throwOnSend: new HttpRequestException("socket error"));
        service.CheckOnce().GetAwaiter().GetResult();

        var snapshot = service.GetSnapshot();
        Assert.Equal(GameUpdateState.DiscoveryFailed, snapshot.State);
        Assert.Equal("network_failure", snapshot.ReasonCode);
    }

    [Fact]
    public void CheckOnce_WhenBodyIsMalformed_ShouldFailWithInvalidResponseWithoutLoggingBody()
    {
        const string secretBody = "SECRET-RESPONSE-CONTENT-42";
        var logger = new CollectingLogger();

        var (service, _) = CreateService(
            JsonResponse("{ this is not json " + secretBody), logger: logger);
        service.CheckOnce().GetAwaiter().GetResult();

        var snapshot = service.GetSnapshot();
        Assert.Equal(GameUpdateState.DiscoveryFailed, snapshot.State);
        Assert.Equal("invalid_response", snapshot.ReasonCode);
        Assert.DoesNotContain(logger.Messages, message => message.Contains(secretBody));
        Assert.Contains(logger.Messages, message => message.Contains("invalid_response"));
    }

    [Fact]
    public async Task CheckOnce_WhenCalledTwice_ShouldRequestOnlyOnce()
    {
        var (service, handler) = CreateService(JsonResponse(ReleaseJson("v0.0.1", prerelease: false)));

        await service.CheckOnce();
        await service.CheckOnce();

        Assert.Single(handler.Requests);
        Assert.Equal(GameUpdateState.UpToDate, service.GetSnapshot().State);
    }

    [Fact]
    public void DismissForProcess_WhenUpdateAvailable_ShouldStopOfferingAndClearDownloadFields()
    {
        var version = NextNewerVersion();
        var display = $"{version.Major}.{version.Minor}.{version.Build}";
        var json = ReleaseJson("v" + display, prerelease: false, $"DTXMania-Setup-{display}.exe", "sha256:" + Digest64Hex);
        var (service, _) = CreateService(JsonResponse(json));
        service.CheckOnce().GetAwaiter().GetResult();

        service.DismissForProcess();

        var snapshot = service.GetSnapshot();
        Assert.Equal(GameUpdateState.UpToDate, snapshot.State);
        Assert.Null(snapshot.InstallerUrl);
        Assert.Null(snapshot.Sha256Digest);
        Assert.Equal("dismissed", snapshot.ReasonCode);
    }

    [Fact]
    public void DismissForProcess_WhenNothingOffered_ShouldNotChangeSnapshot()
    {
        var (service, _) = CreateService();

        service.DismissForProcess();

        Assert.Equal(GameUpdateState.NotChecked, service.GetSnapshot().State);
    }

    [Fact]
    public void BeginUpdate_WhenUpdateAvailable_ShouldDeferWithoutChangingSnapshot()
    {
        var version = NextNewerVersion();
        var display = $"{version.Major}.{version.Minor}.{version.Build}";
        var json = ReleaseJson("v" + display, prerelease: false, $"DTXMania-Setup-{display}.exe", "sha256:" + Digest64Hex);
        var (service, handler) = CreateService(JsonResponse(json));
        service.CheckOnce().GetAwaiter().GetResult();
        var before = service.GetSnapshot();

        service.BeginUpdate();

        Assert.Equal(before, service.GetSnapshot());
        Assert.Single(handler.Requests);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class FakeGitHubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? _response;
        private readonly Exception? _throwOnSend;

        public List<HttpRequestMessage> Requests { get; } = new();

        public FakeGitHubHandler(HttpResponseMessage? response = null, Exception? throwOnSend = null)
        {
            _response = response;
            _throwOnSend = throwOnSend;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (_throwOnSend is not null)
            {
                throw _throwOnSend;
            }

            return Task.FromResult(_response ?? new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class CollectingLogger : ILogger<GameUpdateService>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
