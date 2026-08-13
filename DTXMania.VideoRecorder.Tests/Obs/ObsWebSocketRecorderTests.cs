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

        try
        {
            await recorder.ConnectAsync(timeout.Token);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => recorder.StartRecordAsync(timeout.Token));

            await server.StopReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            await recorder.DisposeAsync();
        }
    }

    private sealed class ObsTestServer : IAsyncDisposable
    {
        private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private readonly TcpListener _listener;
        private readonly Task _runTask;
        private readonly TaskCompletionSource<object?> _stopReceived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private ObsTestServer(TcpListener listener)
        {
            _listener = listener;
            Url = new Uri($"ws://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
            _runTask = RunAsync();
        }

        public Uri Url { get; }

        public TaskCompletionSource<object?> StopReceived => _stopReceived;

        public static async Task<ObsTestServer> StartAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, port: 0);
            listener.Start();
            var server = new ObsTestServer(listener);
            await Task.Yield();
            return server;
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try
            {
                await _runTask.WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (Exception) when (_runTask.IsCompletedSuccessfully || _runTask.IsCanceled)
            {
            }
        }

        private async Task RunAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                var headers = await ReadHttpHeadersAsync(stream);
                var key = headers["Sec-WebSocket-Key"];
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
                            await SendTextAsync(
                                stream,
                                $"{{\"op\":7,\"d\":{{\"requestType\":\"StartRecord\",\"requestId\":\"{requestId}\",\"requestStatus\":{{\"result\":true,\"code\":100}}}}}}");
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
            catch (Exception exception)
            {
                _stopReceived.TrySetException(exception);
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
                if (bytes.Count >= 4 && bytes[^4..].SequenceEqual("\r\n\r\n"u8.ToArray()))
                    break;
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
            if (payload.Length > byte.MaxValue)
                throw new InvalidOperationException("Test payload unexpectedly exceeds one-byte frame size.");

            await stream.WriteAsync(new[] { (byte)0x81, (byte)payload.Length });
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
