#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Update;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DTXMania.Test.Update;

[Trait("Category", "Unit")]
public class GameUpdateServiceDownloadTests
{
    private static string NewerDisplay { get; } =
        $"{ApplicationVersion.Current.Major + 10}.{ApplicationVersion.Current.Minor}.{ApplicationVersion.Current.Build}";

    private static string InstallerUrl => "http://update.test/installer";

    private static byte[] InstallerBytes(int size = 1024)
    {
        var data = new byte[size];
        new Random(1234).NextBytes(data);
        return data;
    }

    private static string DigestOf(byte[] data) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static string ReleaseJson(string downloadUrl, string digest) =>
        "{\"tag_name\":\"v" + NewerDisplay + "\",\"prerelease\":false,\"assets\":[{" +
        "\"name\":\"DTXMania-Setup-" + NewerDisplay + ".exe\"," +
        "\"browser_download_url\":\"" + downloadUrl + "\"," +
        "\"digest\":\"" + digest + "\"}]}";

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; }
        public List<HttpRequestMessage> Requests { get; } = new();

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => Respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Respond(request));
        }
    }

    /// <summary>
    /// The attempt the fake starter returns when no failure is being exercised: the
    /// installer process survived the elevation-decision window, so the launch is
    /// committed and the service may publish InstallerLaunched.
    /// </summary>
    private static InstallerLaunchAttempt CommittedAttempt => new(Started: true, ExitCode: 0, StillRunning: true);

    private static (GameUpdateService Service, FakeHandler Handler, List<ProcessStartInfo> Starts) CreateOfferedService(
        byte[] installerBytes,
        string? advertisedDigest = null,
        Exception? starterThrows = null,
        InstallerLaunchAttempt? starterAttempt = null,
        Action? onLaunchAttempt = null)
    {
        var handler = new FakeHandler(request =>
        {
            if (request.RequestUri?.Host == "api.github.com")
            {
                return JsonResponse(ReleaseJson(InstallerUrl, advertisedDigest ?? DigestOf(installerBytes)));
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(installerBytes) };
        });

        var starts = new List<ProcessStartInfo>();
        var launcher = new WindowsUpdateInstallerLauncher(info =>
        {
            onLaunchAttempt?.Invoke();
            starts.Add(info);
            if (starterThrows is not null)
            {
                throw starterThrows;
            }

            return starterAttempt ?? CommittedAttempt;
        });

        var service = new GameUpdateService(new HttpClient(handler), logger: null, launcher);
        service.CheckOnce().GetAwaiter().GetResult();
        Assert.Equal(GameUpdateState.Available, service.GetSnapshot().State);
        return (service, handler, starts);
    }

    private static string TempPath => GameUpdateService.TempInstallerPathForVersion(NewerDisplay);

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "condition was not met within the timeout");
    }

    [Fact]
    public void BeginUpdate_WhileVerifiedFileIsHeldForLaunch_ShouldDenyOtherOpenersWriteAccess()
    {
        // Runs inside the fake launcher, i.e. exactly inside the read-hold (verify→launch):
        // the service holds a FileAccess.Read/FileShare.Read handle over the verified file,
        // so a write-open must hit a sharing violation until the launcher has returned.
        Exception? observed = null;
        var bytes = InstallerBytes();
        var (service, _, _) = CreateOfferedService(bytes, onLaunchAttempt: () =>
        {
            try
            {
                using var probe = File.Open(TempPath, FileMode.Open, FileAccess.Write);
            }
            catch (Exception exception)
            {
                observed = exception;
            }
        });

        try
        {
            service.BeginUpdate();
            service.UpdateTask.GetAwaiter().GetResult();

            Assert.Equal(GameUpdateState.InstallerLaunched, service.GetSnapshot().State);
            Assert.IsType<IOException>(observed); // no write sharing between verify and launch
        }
        finally
        {
            File.Delete(TempPath); // also proves the handle was released once the flow completed
        }
    }

    [Fact]
    public void BeginUpdate_WhenStaleTempFileExistsAtTempPath_ShouldReplaceItWithFreshDownload()
    {
        var bytes = InstallerBytes();
        var (service, _, starts) = CreateOfferedService(bytes);
        File.WriteAllBytes(TempPath, new byte[bytes.Length + 4096]); // stale garbage at the one path

        try
        {
            service.BeginUpdate();
            service.UpdateTask.GetAwaiter().GetResult();

            Assert.Equal(GameUpdateState.InstallerLaunched, service.GetSnapshot().State);
            Assert.Equal(bytes, File.ReadAllBytes(TempPath));
            Assert.Single(starts);
        }
        finally
        {
            File.Delete(TempPath);
        }
    }

    [Fact]
    public void BeginUpdate_WhenDigestMatches_ShouldLaunchInstallerOnceWithBaselineArguments()
    {
        var bytes = InstallerBytes();
        var (service, _, starts) = CreateOfferedService(bytes);

        try
        {
            service.BeginUpdate();
            service.UpdateTask.GetAwaiter().GetResult();

            var snapshot = service.GetSnapshot();
            Assert.Equal(GameUpdateState.InstallerLaunched, snapshot.State);
            Assert.Equal(NewerDisplay, snapshot.AvailableVersion);
            Assert.Equal(100, snapshot.DownloadPercent); // ByteArrayContent always carries Content-Length

            var start = Assert.Single(starts);
            Assert.Equal(TempPath, start.FileName);
            Assert.False(start.UseShellExecute); // never a shell
            Assert.Equal(
                new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", "/AUTOUPDATE" },
                start.ArgumentList.ToArray());
        }
        finally
        {
            File.Delete(TempPath);
        }
    }

    [Fact]
    public void BeginUpdate_WhenDigestMismatch_ShouldFailWithoutLaunchingAndRetryFromFreshFile()
    {
        var goodBytes = InstallerBytes();
        var badBytes = InstallerBytes();
        badBytes[0] ^= 0xFF;
        var (service, handler, starts) = CreateOfferedService(goodBytes, advertisedDigest: DigestOf(goodBytes));
        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(badBytes) };

        try
        {
            service.BeginUpdate();
            service.UpdateTask.GetAwaiter().GetResult();

            var failed = service.GetSnapshot();
            Assert.Equal(GameUpdateState.Failed, failed.State);
            Assert.Equal("digest_mismatch", failed.ReasonCode);
            Assert.Equal(InstallerUrl, failed.InstallerUrl); // offer preserved for retry
            Assert.Null(failed.DownloadPercent);
            Assert.Empty(starts); // launcher never sees unverified bytes

            // Retry must start from a fresh temp file, never the stale download.
            File.WriteAllBytes(TempPath, new byte[64]);
            handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(goodBytes) };

            service.BeginUpdate();
            service.UpdateTask.GetAwaiter().GetResult();

            Assert.Equal(GameUpdateState.InstallerLaunched, service.GetSnapshot().State);
            Assert.Equal(goodBytes, File.ReadAllBytes(TempPath));
            Assert.Single(starts);
        }
        finally
        {
            File.Delete(TempPath);
        }
    }

    [Fact]
    public async Task BeginUpdate_WhenContentLengthPresent_ShouldPublishIntermediatePercent()
    {
        var bytes = InstallerBytes(200);
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = new SignalingStream(bytes, chunkSize: 100, releaseGate);

        var handler = new FakeHandler(request =>
        {
            if (request.RequestUri?.Host == "api.github.com")
            {
                return JsonResponse(ReleaseJson(InstallerUrl, DigestOf(bytes)));
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = RawStreamContent.WithLength(stream, bytes.Length) };
        });
        var service = new GameUpdateService(
            new HttpClient(handler), null, new WindowsUpdateInstallerLauncher(_ => CommittedAttempt));
        service.CheckOnce().GetAwaiter().GetResult();

        try
        {
            service.BeginUpdate();
            await stream.FirstChunkRead; // first 100 bytes are through the copy loop
            await WaitUntilAsync(() => service.GetSnapshot().DownloadPercent == 50);

            releaseGate.TrySetResult();
            await service.UpdateTask;

            Assert.Equal(GameUpdateState.InstallerLaunched, service.GetSnapshot().State);
            Assert.Equal(100, service.GetSnapshot().DownloadPercent);
        }
        finally
        {
            releaseGate.TrySetResult();
            File.Delete(TempPath);
        }
    }

    [Fact]
    public void BeginUpdate_WhenContentLengthAbsent_ShouldLeavePercentNull()
    {
        var bytes = InstallerBytes();
        var handler = new FakeHandler(request =>
        {
            if (request.RequestUri?.Host == "api.github.com")
            {
                return JsonResponse(ReleaseJson(InstallerUrl, DigestOf(bytes)));
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = RawStreamContent.WithoutLength(new NonSeekableMemoryStream(bytes))
            };
        });
        var logger = new CollectingLogger();
        var service = new GameUpdateService(
            new HttpClient(handler), logger, new WindowsUpdateInstallerLauncher(_ => CommittedAttempt));
        service.CheckOnce().GetAwaiter().GetResult();

        try
        {
            service.BeginUpdate();
            service.UpdateTask.GetAwaiter().GetResult();

            var snapshot = service.GetSnapshot();
            Assert.True(snapshot.State == GameUpdateState.InstallerLaunched,
                $"state={snapshot.State} reason={snapshot.ReasonCode} log=[{string.Join(" | ", logger.Messages)}]");
            Assert.Null(snapshot.DownloadPercent);
        }
        finally
        {
            File.Delete(TempPath);
        }
    }

    [Theory]
    [InlineData(740)]  // ERROR_ELEVATION_REQUIRED
    [InlineData(1223)] // ERROR_CANCELLED — UAC declined
    public void BeginUpdate_WhenLauncherReportsElevationFailure_ShouldFailRetryableWithoutInstallerLaunched(int win32Error)
    {
        var bytes = InstallerBytes();
        var (service, _, starts) = CreateOfferedService(bytes, starterThrows: new Win32Exception(win32Error));

        try
        {
            service.BeginUpdate();
            service.UpdateTask.GetAwaiter().GetResult();

            var snapshot = service.GetSnapshot();
            Assert.Equal(GameUpdateState.Failed, snapshot.State);
            Assert.Equal("launch_failed", snapshot.ReasonCode);
            Assert.Equal(InstallerUrl, snapshot.InstallerUrl); // retryable: offer survives
            // The start was ATTEMPTED once (and only once) with the verified installer.
            var start = Assert.Single(starts);
            Assert.Equal(TempPath, start.FileName);
            // The test process (standing in for the game) is still alive.
        }
        finally
        {
            File.Delete(TempPath);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1223)]
    public void BeginUpdate_WhenBootstrapperExitsNonzeroInsideElevationWindow_ShouldFailRetryableWithoutInstallerLaunched(int exitCode)
    {
        // The Task-0 all-users cancel path: Process.Start succeeded, but the Inno
        // bootstrapper's internal UAC handoff was refused and the process exited
        // nonzero inside the decision window. The service must publish a retryable
        // Failed — never InstallerLaunched — so the running game stays alive.
        var bytes = InstallerBytes();
        var (service, _, starts) = CreateOfferedService(
            bytes, starterAttempt: new InstallerLaunchAttempt(Started: true, ExitCode: exitCode));

        try
        {
            service.BeginUpdate();
            service.UpdateTask.GetAwaiter().GetResult();

            var snapshot = service.GetSnapshot();
            Assert.Equal(GameUpdateState.Failed, snapshot.State);
            Assert.Equal("launch_failed", snapshot.ReasonCode);
            Assert.Equal(InstallerUrl, snapshot.InstallerUrl); // retryable: offer survives
            Assert.Single(starts);
        }
        finally
        {
            File.Delete(TempPath);
        }
    }

    [Fact]
    public void BeginUpdate_WhenInstallerDownloadReturnsHttpError_ShouldFailRetryableWithoutLaunching()
    {
        var bytes = InstallerBytes();
        var (service, handler, starts) = CreateOfferedService(bytes);
        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        try
        {
            service.BeginUpdate();
            service.UpdateTask.GetAwaiter().GetResult();

            var snapshot = service.GetSnapshot();
            Assert.Equal(GameUpdateState.Failed, snapshot.State);
            Assert.Equal("http_404", snapshot.ReasonCode);
            Assert.Equal(InstallerUrl, snapshot.InstallerUrl);
            Assert.Empty(starts);
        }
        finally
        {
            File.Delete(TempPath);
        }
    }

    [Fact]
    public void BeginUpdate_WhenInstallerAlreadyLaunched_ShouldIgnoreRepeatCalls()
    {
        var bytes = InstallerBytes();
        var (service, handler, starts) = CreateOfferedService(bytes);

        try
        {
            service.BeginUpdate();
            service.UpdateTask.GetAwaiter().GetResult();
            var launched = service.GetSnapshot();
            var requests = handler.Requests.Count;

            service.BeginUpdate();

            Assert.Equal(launched, service.GetSnapshot());
            Assert.Equal(requests, handler.Requests.Count);
            Assert.Single(starts);
        }
        finally
        {
            File.Delete(TempPath);
        }
    }

    [Fact]
    public async Task BeginUpdate_WhenLoopbackServerRedirects_ShouldFollowRedirectAndDownloadVerifiedBody()
    {
        // 300KB deterministic body — big enough that a buffered fake could not fake its way through.
        var body = new byte[300 * 1024];
        for (var i = 0; i < body.Length; i++)
        {
            body[i] = (byte)(i * 7);
        }

        using var server = new LoopbackServer(body);
        var discovery = JsonResponse(
            ReleaseJson($"{server.BaseUrl}/asset", DigestOf(body)));

        // Production HttpClient stack (SocketsHttpHandler) everywhere except the
        // GitHub discovery call, which cannot leave localhost. The 302 from
        // /asset to /real is followed by the REAL redirect pipeline.
        var httpClient = new HttpClient(new GitHubStubbingHandler(new SocketsHttpHandler(), discovery));
        var starts = new List<ProcessStartInfo>();
        var service = new GameUpdateService(
            httpClient,
            logger: null,
            new WindowsUpdateInstallerLauncher(info => { starts.Add(info); return CommittedAttempt; }));

        try
        {
            service.CheckOnce().GetAwaiter().GetResult();
            Assert.Equal(GameUpdateState.Available, service.GetSnapshot().State);
            Assert.Equal($"{server.BaseUrl}/asset", service.GetSnapshot().InstallerUrl);

            service.BeginUpdate();
            await service.UpdateTask;

            Assert.Equal(GameUpdateState.InstallerLaunched, service.GetSnapshot().State);
            Assert.Equal(100, service.GetSnapshot().DownloadPercent);
            Assert.Equal(2, server.ServedRequests); // /asset 302, then /real — redirect really followed
            Assert.Single(starts);
            Assert.Equal(TempPath, starts[0].FileName);
            // Hash the final temp FILE bytes: they must be /real's body.
            Assert.Equal(DigestOf(body), DigestOf(File.ReadAllBytes(TempPath)));
        }
        finally
        {
            File.Delete(TempPath);
        }
    }

    /// <summary>
    /// Fakes only the api.github.com discovery call (which cannot point at a local
    /// server); every other request flows through the wrapped production handler.
    /// </summary>
    private sealed class GitHubStubbingHandler : DelegatingHandler
    {
        private readonly HttpResponseMessage _discoveryResponse;

        public GitHubStubbingHandler(HttpMessageHandler innerHandler, HttpResponseMessage discoveryResponse)
            : base(innerHandler)
        {
            _discoveryResponse = discoveryResponse;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host == "api.github.com")
            {
                return Task.FromResult(_discoveryResponse);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>Minimal HTTP/1.1 loopback: /asset 302 -> /real serves the body.</summary>
    private sealed class LoopbackServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly byte[] _body;
        private readonly CancellationTokenSource _cancellation = new();

        public string BaseUrl { get; }
        public int ServedRequests;

        public LoopbackServer(byte[] body)
        {
            _body = body;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            BaseUrl = $"http://127.0.0.1:{port}";
            _ = Task.Run(AcceptLoopAsync);
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_cancellation.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_cancellation.Token);
                    await HandleAsync(client);
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException or IOException)
            {
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using var stream = client.GetStream();
            var head = await ReadHeadAsync(stream);
            if (head is null)
            {
                return;
            }

            var path = head.Split(' ')[1];
            Interlocked.Increment(ref ServedRequests);
            if (path == "/asset")
            {
                await WriteAsync(stream, "302 Found", new[] { "Location: /real" }, Array.Empty<byte>());
            }
            else if (path == "/real")
            {
                await WriteAsync(stream, "200 OK", Array.Empty<string>(), _body);
            }
        }

        private static async Task<string?> ReadHeadAsync(NetworkStream stream)
        {
            var buffer = new byte[8192];
            var total = 0;
            while (total < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(total));
                if (read == 0)
                {
                    return null;
                }

                total += read;
                var head = Encoding.ASCII.GetString(buffer, 0, total);
                var end = head.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (end >= 0)
                {
                    return head[..end];
                }
            }

            return null;
        }

        private static async Task WriteAsync(NetworkStream stream, string status, string[] headers, byte[] body)
        {
            var builder = new StringBuilder();
            builder.Append("HTTP/1.1 ").Append(status).Append("\r\n");
            foreach (var header in headers)
            {
                builder.Append(header).Append("\r\n");
            }

            builder.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            builder.Append("Connection: close\r\n\r\n");
            await stream.WriteAsync(Encoding.ASCII.GetBytes(builder.ToString()));
            await stream.WriteAsync(body);
        }

        public void Dispose()
        {
            _cancellation.Cancel();
            _listener.Stop();
            _cancellation.Dispose();
        }
    }

    private sealed class CollectingLogger : ILogger<GameUpdateService>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception) + (exception is null ? string.Empty : $" :: {exception.GetType().Name}: {exception.Message}"));
        }
    }

    /// <summary>Serves one live stream with (or without) an explicit Content-Length header.</summary>
    private sealed class RawStreamContent : HttpContent
    {
        private readonly Stream _stream;

        private RawStreamContent(Stream stream) => _stream = stream;

        public static RawStreamContent WithLength(Stream stream, int length)
        {
            var content = new RawStreamContent(stream);
            content.Headers.ContentLength = length;
            return content;
        }

        public static RawStreamContent WithoutLength(Stream stream) => new(stream);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            _stream.CopyToAsync(stream);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        // Hand the live stream over so the service copy loop streams (never buffers).
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(_stream);

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_stream);
    }

    private sealed class NonSeekableMemoryStream : MemoryStream
    {
        public NonSeekableMemoryStream(byte[] buffer)
            : base(buffer)
        {
        }

        public override bool CanSeek => false;
    }

    /// <summary>
    /// Yields <paramref name="chunkSize"/> bytes per read and, after the first
    /// chunk, signals <see cref="FirstChunkRead"/> and blocks on the release gate
    /// so a test can observe the intermediate snapshot state deterministically.
    /// </summary>
    private sealed class SignalingStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _chunkSize;
        private readonly TaskCompletionSource _releaseGate;
        private readonly TaskCompletionSource _firstChunkRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _signaled;
        private int _position;

        public SignalingStream(byte[] data, int chunkSize, TaskCompletionSource releaseGate)
        {
            _data = data;
            _chunkSize = chunkSize;
            _releaseGate = releaseGate;
        }

        public Task FirstChunkRead => _firstChunkRead.Task;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // Gate at the START of the follow-up read: by then the caller has fully
            // processed chunk one (written, hashed, published its percent).
            if (_position >= _chunkSize && !_signaled)
            {
                _signaled = true;
                _firstChunkRead.TrySetResult();
                await _releaseGate.Task.ConfigureAwait(false);
            }

            if (_position >= _data.Length)
            {
                return 0;
            }

            var count = Math.Min(buffer.Length, Math.Min(_chunkSize, _data.Length - _position));
            _data.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
