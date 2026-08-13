using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DTXMania.VideoRecorder.Obs;

namespace DTXMania.VideoRecorder.Tests.Obs;

public sealed class ObsWebSocketRecorderTests
{
    [Fact]
    public async Task StartRecordAsync_WhenStatusConfirmationFails_ShouldStopAfterAcknowledgedStart()
    {
        await using var server = await ObsTestServer.StartAsync();
        await using var recorder = new ObsWebSocketRecorder(server.Url, password: string.Empty);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await recorder.ConnectAsync(timeout.Token);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => recorder.StartRecordAsync(timeout.Token));

        await server.StopReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StartRecordAsync_WhenStartResponseIsDropped_ShouldCompensateWithStop()
    {
        // The server receives StartRecord but never replies. The recorder must
        // treat ownership as ambiguous (StartRecord was sent) and send
        // StopRecord as compensation despite the start failure.
        await using var server = await ObsTestServer.StartAsync(dropStartRecordResponse: true);
        await using var recorder = new ObsWebSocketRecorder(server.Url, password: string.Empty);
        using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await recorder.ConnectAsync(connectTimeout.Token);

        using var startTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => recorder.StartRecordAsync(startTimeout.Token));

        // Proves the server actually saw StartRecord (send happened before drop).
        await server.StartRecordReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        // Proves compensation ran: StopRecord reached OBS after the dropped start.
        await server.StopReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class ObsTestServer : IAsyncDisposable
    {
        private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private readonly TcpListener _listener;
        private readonly Task _runTask;
        private readonly TaskCompletionSource<object?> _startRecordReceived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<object?> _stopReceived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool _dropStartRecordResponse;
        // Explicit disposal signal: set before the listener is stopped so the
        // accept loop can distinguish a disposal-induced listener stop from a
        // mid-session connection drop and exit instead of busy-looping on a
        // stopped listener.
        private readonly CancellationTokenSource _disposeSignal = new();

        private ObsTestServer(TcpListener listener, bool dropStartRecordResponse)
        {
            _listener = listener;
            _dropStartRecordResponse = dropStartRecordResponse;
            Url = new Uri($"ws://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
            _runTask = RunAsync();
        }

        public Uri Url { get; }

        public TaskCompletionSource<object?> StartRecordReceived => _startRecordReceived;

        public TaskCompletionSource<object?> StopReceived => _stopReceived;

        public static async Task<ObsTestServer> StartAsync(bool dropStartRecordResponse = false)
        {
            var listener = new TcpListener(IPAddress.Loopback, port: 0);
            listener.Start();
            var server = new ObsTestServer(listener, dropStartRecordResponse);
            await Task.Yield();
            return server;
        }

        public async ValueTask DisposeAsync()
        {
            // Signal the accept loop to exit before stopping the listener. The
            // loop checks this signal in its catch block so a disposal-induced
            // listener stop is not mistaken for a recoverable connection drop
            // (which would otherwise busy-loop on the stopped listener).
            _disposeSignal.Cancel();
            _listener.Stop();
            try
            {
                await _runTask.WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (Exception)
            {
                // Teardown is best-effort: a slow, faulted, or canceled run task
                // must not escape disposal.
            }
            finally
            {
                _disposeSignal.Dispose();
            }
        }

        private async Task RunAsync()
        {
            try
            {
                // Accept reconnects until StopRecord has been observed or
                // disposal signals shutdown. A cancelled receive aborts the
                // recorder's ClientWebSocket, so compensation reconnects on a
                // fresh connection that this loop must accept rather than
                // treating the dropped connection as fatal.
                while (!_disposeSignal.IsCancellationRequested &&
                       !_stopReceived.Task.IsCompleted)
                {
                    try
                    {
                        using var client = await _listener.AcceptTcpClientAsync();
                        await using var stream = client.GetStream();
                        await HandleConnectionAsync(stream);
                    }
                    catch (Exception)
                    {
                        if (_disposeSignal.IsCancellationRequested)
                        {
                            // Disposal stopped the listener mid-accept; exit
                            // cleanly instead of retrying on a stopped listener.
                            break;
                        }
                        // A connection dropped mid-session (e.g. the recorder
                        // aborted after a cancelled StartRecord receive). Loop
                        // to accept the compensation reconnect unless StopRecord
                        // has already been observed.
                    }
                }
            }
            catch (Exception exception)
            {
                _stopReceived.TrySetException(exception);
            }
        }

        private async Task HandleConnectionAsync(NetworkStream stream)
        {
            var headers = await ReadHttpHeadersAsync(stream);
            var key = headers["Sec-WebSocket-Key"];
            // SHA-1 is mandated by RFC 6455 for the Sec-WebSocket-Accept handshake;
            // do not replace it with a stronger hash algorithm.
            var accept = Convert.ToBase64String(
                SHA1.HashData(Encoding.ASCII.GetBytes(key + WebSocketGuid)));
            await WriteHttpResponseAsync(
                stream,
                $"HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {accept}\r\n\r\n");

            await SendTextAsync(stream, "{\"op\":0,\"d\":{\"rpcVersion\":1,\"authentication\":{\"salt\":\"salt\",\"challenge\":\"challenge\"}}}");
            _ = await ReceiveTextAsync(stream);
            await SendTextAsync(stream, "{\"op\":2,\"d\":{\"negotiatedRpcVersion\":1}}");

            while (true)
            {
                using var document = JsonDocument.Parse(await ReceiveTextAsync(stream));
                var root = document.RootElement;
                var data = root.GetProperty("d");
                var requestType = data.GetProperty("requestType").GetString();
                var requestId = data.GetProperty("requestId").GetString();
                switch (requestType)
                {
                    case "StartRecord":
                        _startRecordReceived.TrySetResult(null);
                        if (!_dropStartRecordResponse)
                        {
                            await SendTextAsync(
                                stream,
                                $"{{\"op\":7,\"d\":{{\"requestType\":\"StartRecord\",\"requestId\":\"{requestId}\",\"requestStatus\":{{\"result\":true,\"code\":100}}}}}}");
                        }
                        // When dropping, the recorder must time out and then
                        // compensate with StopRecord, handled by the case below.
                        break;
                    case "GetRecordStatus":
                        // The StartRecord acknowledgement was successful, but
                        // confirmation is malformed. The client must compensate.
                        await SendTextAsync(
                            stream,
                            $"{{\"op\":7,\"d\":{{\"requestType\":\"GetRecordStatus\",\"requestId\":\"{requestId}\",\"requestStatus\":{{\"result\":true,\"code\":100}}}}}}");
                        break;
                    case "StopRecord":
                        _stopReceived.TrySetResult(null);
                        await SendTextAsync(
                            stream,
                            $"{{\"op\":7,\"d\":{{\"requestType\":\"StopRecord\",\"requestId\":\"{requestId}\",\"requestStatus\":{{\"result\":true,\"code\":100}},\"responseData\":{{\"outputPath\":\"C:\\\\recordings\\\\capture.mp4\"}}}}}}");
                        return;
                    default:
                        throw new InvalidOperationException($"Unexpected request '{requestType}'.");
                }
            }
        }

        private static async Task<Dictionary<string, string>> ReadHttpHeadersAsync(NetworkStream stream)
        {
            var bytes = new List<byte>();
            var buffer = new byte[1024];
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                    throw new EndOfStreamException("WebSocket handshake ended early.");
                bytes.AddRange(buffer.AsSpan(0, read).ToArray());
                if (bytes.Count >= 4 &&
                    bytes[^4] == (byte)'\r' && bytes[^3] == (byte)'\n' &&
                    bytes[^2] == (byte)'\r' && bytes[^1] == (byte)'\n')
                {
                    break;
                }
            }

            var request = Encoding.ASCII.GetString(bytes.ToArray());
            return request
                .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
                .Skip(1)
                .Select(line => line.Split(':', 2))
                .ToDictionary(parts => parts[0], parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
        }

        private static async Task WriteHttpResponseAsync(NetworkStream stream, string response)
            => await stream.WriteAsync(Encoding.ASCII.GetBytes(response));

        private static async Task SendTextAsync(NetworkStream stream, string message)
        {
            var payload = Encoding.UTF8.GetBytes(message);
            // Server-to-client frames are unmasked per RFC 6455. Lengths of 126+
            // must use the extended length form, otherwise the high bit of the
            // length byte collides with the mask bit and the client rejects the
            // frame as "masked" — which previously corrupted the StopRecord
            // response (≈154 bytes) and silently broke compensation.
            var header = new List<byte> { 0x81 }; // FIN + text opcode
            if (payload.Length <= 125)
            {
                header.Add((byte)payload.Length);
            }
            else
            {
                header.Add(126);
                header.Add((byte)(payload.Length >> 8));
                header.Add((byte)(payload.Length & 0xff));
            }

            await stream.WriteAsync(header.ToArray());
            await stream.WriteAsync(payload);
        }

        private static async Task<string> ReceiveTextAsync(NetworkStream stream)
        {
            var header = await ReadExactlyAsync(stream, 2);
            var payloadLength = header[1] & 0x7f;
            if ((header[1] & 0x80) == 0 || payloadLength is 126 or 127)
                throw new InvalidOperationException("Expected a masked short client frame.");

            var mask = await ReadExactlyAsync(stream, 4);
            var payload = await ReadExactlyAsync(stream, payloadLength);
            for (var index = 0; index < payload.Length; index++)
                payload[index] ^= mask[index % 4];
            return Encoding.UTF8.GetString(payload);
        }

        private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count)
        {
            var buffer = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset));
                if (read == 0)
                    throw new EndOfStreamException("WebSocket frame ended early.");
                offset += read;
            }

            return buffer;
        }
    }
}
