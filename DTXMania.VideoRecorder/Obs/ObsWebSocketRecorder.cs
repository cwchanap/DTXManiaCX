using System.Buffers;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;

namespace DTXMania.VideoRecorder.Obs;

internal sealed class ObsWebSocketRecorder : IObsRecorder
{
    private static readonly TimeSpan StartConfirmationCompensationTimeout =
        TimeSpan.FromSeconds(15);

    private readonly Uri _url;
    private readonly string _password;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private ClientWebSocket? _socket;
    private int _nextRequestId;
    private int _disposed;

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
                    expectedOpCode: ObsProtocol.HelloOpCode,
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
                    expectedOpCode: ObsProtocol.IdentifiedOpCode,
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
        // Phase 1: send StartRecord and await its response. Once the bytes
        // leave the socket, OBS may begin recording whether or not we ever see
        // the reply, so a failure here is ambiguous and must compensate.
        var startSent = false;
        string response;
        try
        {
            response = await SendRequestAsync(
                    "StartRecord",
                    requestData: null,
                    onRequestSent: () => startSent = true,
                    token)
                .ConfigureAwait(false);
        }
        catch (Exception sendOrReceiveFailure) when (startSent)
        {
            // StartRecord was sent but no reply was confirmed. Ownership is
            // ambiguous; compensate with an independent token so the
            // cancellation/timeout that caused this failure does not skip
            // cleanup.
            await CompensateStopAsync(sendOrReceiveFailure).ConfigureAwait(false);
            throw;
        }

        // Phase 2: OBS returned a definitive StartRecord response. If it
        // reports failure, OBS is not recording and no compensation is owed.
        ObsProtocol.EnsureRequestSucceeded(response, "StartRecord");

        // Phase 3: confirm OBS is actually recording. A failure here, after a
        // successful StartRecord acknowledgement, must compensate.
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
            await CompensateStopAsync(confirmationFailure).ConfigureAwait(false);
            throw;
        }
    }

    private async Task CompensateStopAsync(Exception primaryFailure)
    {
        // Do not reuse the caller token: a timeout/cancellation which caused
        // the failure being compensated must not skip the StopRecord attempt.
        using var compensationTimeout = new CancellationTokenSource(
            StartConfirmationCompensationTimeout);
        var token = compensationTimeout.Token;
        try
        {
            // The cancellation/timeout that aborted the in-flight receive can
            // leave the WebSocket unusable (ClientWebSocket aborts on a cancelled
            // receive). OBS recording state is server-side and survives a
            // reconnect, so re-establish the session before attempting the
            // compensating StopRecord.
            if (_socket is null || _socket.State != WebSocketState.Open)
            {
                await ConnectAsync(token).ConfigureAwait(false);
            }

            await StopRecordForCompensationAsync(token).ConfigureAwait(false);
        }
        catch (Exception compensationFailure)
        {
            throw new InvalidOperationException(
                "OBS StartRecord was sent but its recording state could not be confirmed, " +
                "and StopRecord compensation also failed.",
                new AggregateException(primaryFailure, compensationFailure));
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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // Serialize teardown with any in-flight request so a receive cannot
        // target a disposed socket or semaphore.
        await _requestGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            var socket = _socket;
            _socket = null;
            if (socket is not null)
            {
                try
                {
                    if (socket.State == WebSocketState.Open)
                    {
                        using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await socket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Recorder shutting down.",
                            closeTimeout.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Best-effort close handshake; teardown must still complete.
                }
                catch (WebSocketException)
                {
                    // The peer may have closed first; ignore and dispose.
                }

                socket.Dispose();
            }
        }
        finally
        {
            _requestGate.Release();
        }

        _requestGate.Dispose();
        _connectGate.Dispose();
    }

    private async Task<string> SendRequestAsync(
        string requestType,
        object? requestData,
        CancellationToken token)
        => await SendRequestAsync(requestType, requestData, onRequestSent: null, token)
            .ConfigureAwait(false);

    private async Task<string> SendRequestAsync(
        string requestType,
        object? requestData,
        Action? onRequestSent,
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
            // Signal that the request bytes have left the socket. Callers such
            // as StartRecord treat this as the point past which OBS may have
            // acted, so any later failure must trigger compensation.
            onRequestSent?.Invoke();

            while (true)
            {
                var message = await ReceiveTextAsync(socket, token).ConfigureAwait(false);
                if (!ObsProtocol.TryGetOpCode(message, out var opCode))
                {
                    throw new InvalidOperationException(
                        "OBS sent a malformed message while waiting for a request response.");
                }

                if (opCode == ObsProtocol.EventOpCode)
                    continue;
                if (opCode != ObsProtocol.RequestResponseOpCode)
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

            if (opCode == ObsProtocol.EventOpCode)
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
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(ObsWebSocketRecorder));
    }
}
