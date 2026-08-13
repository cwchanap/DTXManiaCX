using System.Buffers;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;

namespace DTXMania.VideoRecorder.Obs;

internal sealed class ObsWebSocketRecorder : IObsRecorder
{
    private const int EventOpCode = 5;
    private static readonly TimeSpan StartConfirmationCompensationTimeout =
        TimeSpan.FromSeconds(15);

    private readonly Uri _url;
    private readonly string _password;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private ClientWebSocket? _socket;
    private int _nextRequestId;
    private bool _disposed;

    internal ObsWebSocketRecorder(Uri url, string password)
    {
        _url = url ?? throw new ArgumentNullException(nameof(url));
        _password = password ?? throw new ArgumentNullException(nameof(password));
    }

    public async Task ConnectAsync(CancellationToken token)
    {
        ThrowIfDisposed();
        await _connectGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_socket?.State == WebSocketState.Open)
                return;

            _socket?.Dispose();
            _socket = null;

            var socket = new ClientWebSocket();
            try
            {
                await socket.ConnectAsync(_url, token).ConfigureAwait(false);

                var hello = await ReceiveUntilAsync(
                    socket,
                    expectedOpCode: 0,
                    expectedKind: "Hello",
                    token).ConfigureAwait(false);
                var helloDetails = ObsProtocol.ParseHello(hello);
                var identify = ObsProtocol.BuildIdentifyRequest(
                    _password,
                    helloDetails.Salt,
                    helloDetails.Challenge,
                    helloDetails.RpcVersion);
                await SendTextAsync(socket, identify, token).ConfigureAwait(false);
                var identified = await ReceiveUntilAsync(
                    socket,
                    expectedOpCode: 2,
                    expectedKind: "Identified",
                    token).ConfigureAwait(false);
                ObsProtocol.EnsureIdentified(identified);
                _socket = socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        finally
        {
            _connectGate.Release();
        }
    }

    public async Task<ObsRecordStatus> GetRecordStatusAsync(CancellationToken token)
    {
        var response = await SendRequestAsync("GetRecordStatus", requestData: null, token)
            .ConfigureAwait(false);
        return ObsProtocol.ParseRecordStatus(response);
    }

    public async Task StartRecordAsync(CancellationToken token)
    {
        var response = await SendRequestAsync("StartRecord", requestData: null, token)
            .ConfigureAwait(false);
        ObsProtocol.EnsureRequestSucceeded(response, "StartRecord");

        try
        {
            var status = await GetRecordStatusAsync(token).ConfigureAwait(false);
            if (!status.IsRecording)
            {
                throw new InvalidOperationException(
                    "OBS StartRecord succeeded but GetRecordStatus reported recording inactive.");
            }
        }
        catch (Exception confirmationFailure)
        {
            try
            {
                // StartRecord has already been acknowledged at this point. Do
                // not reuse the caller token: a timeout/cancellation which
                // caused confirmation to fail must not skip compensation.
                using var compensationTimeout = new CancellationTokenSource(
                    StartConfirmationCompensationTimeout);
                await StopRecordForCompensationAsync(compensationTimeout.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception compensationFailure)
            {
                throw new InvalidOperationException(
                    "OBS StartRecord succeeded, but status confirmation failed " +
                    "and StopRecord compensation also failed.",
                    new AggregateException(confirmationFailure, compensationFailure));
            }

            throw;
        }
    }

    public async Task<string> StopRecordAsync(CancellationToken token)
    {
        var response = await SendRequestAsync("StopRecord", requestData: null, token)
            .ConfigureAwait(false);
        return ObsProtocol.ParseStopRecordOutputPath(response);
    }

    private async Task StopRecordForCompensationAsync(CancellationToken token)
    {
        var response = await SendRequestAsync("StopRecord", requestData: null, token)
            .ConfigureAwait(false);
        ObsProtocol.EnsureRequestSucceeded(response, "StopRecord");
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _socket?.Dispose();
            _socket = null;
            _requestGate.Dispose();
            _connectGate.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private async Task<string> SendRequestAsync(
        string requestType,
        object? requestData,
        CancellationToken token)
    {
        ThrowIfDisposed();
        await _requestGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var socket = GetConnectedSocket();
            var requestId = Interlocked.Increment(ref _nextRequestId)
                .ToString(CultureInfo.InvariantCulture);
            var request = ObsProtocol.BuildRequest(requestType, requestId, requestData);
            await SendTextAsync(socket, request, token).ConfigureAwait(false);

            while (true)
            {
                var message = await ReceiveTextAsync(socket, token).ConfigureAwait(false);
                if (!ObsProtocol.TryGetOpCode(message, out var opCode))
                {
                    throw new InvalidOperationException(
                        "OBS sent a malformed message while waiting for a request response.");
                }

                if (opCode == EventOpCode)
                    continue;
                if (opCode != 7)
                {
                    throw new InvalidOperationException(
                        $"OBS sent unexpected message op {opCode} while waiting for {requestType}.");
                }

                var response = ObsProtocol.ParseRequestResponse(message);
                if (!response.RequestId.Equals(requestId, StringComparison.Ordinal))
                    continue;
                if (!response.RequestType.Equals(requestType, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"OBS response requestType was '{response.RequestType}', expected '{requestType}'.");
                }

                return message;
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private ClientWebSocket GetConnectedSocket()
    {
        var socket = _socket;
        if (socket is null || socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException(
                "OBS recorder is not connected. Call ConnectAsync before recording operations.");
        }

        return socket;
    }

    private static async Task<string> ReceiveUntilAsync(
        ClientWebSocket socket,
        int expectedOpCode,
        string expectedKind,
        CancellationToken token)
    {
        while (true)
        {
            var message = await ReceiveTextAsync(socket, token).ConfigureAwait(false);
            if (!ObsProtocol.TryGetOpCode(message, out var opCode))
            {
                throw new InvalidOperationException(
                    $"OBS sent a malformed message while waiting for {expectedKind}.");
            }

            if (opCode == EventOpCode)
                continue;
            if (opCode != expectedOpCode)
            {
                throw new InvalidOperationException(
                    $"OBS sent unexpected message op {opCode} while waiting for {expectedKind}.");
            }

            return message;
        }
    }

    private static async Task SendTextAsync(
        ClientWebSocket socket,
        string message,
        CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await socket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken: token).ConfigureAwait(false);
    }

    private static async Task<string> ReceiveTextAsync(
        ClientWebSocket socket,
        CancellationToken token)
    {
        var rented = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            using var stream = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(
                    rented.AsMemory(),
                    token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new InvalidOperationException(
                        "OBS closed the WebSocket while the recorder was waiting for a response.");
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    throw new InvalidOperationException(
                        "OBS sent a non-text WebSocket message to the recorder.");
                }

                stream.Write(rented, 0, result.Count);
                if (result.EndOfMessage)
                    return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
