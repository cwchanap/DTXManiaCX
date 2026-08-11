using System.Net;
using System.Text;
using System.Text.Json;
using DTXMania.Automation.JsonRpc;
using DTXMania.Automation.Process;

namespace DTXMania.Automation.Tests.JsonRpc;

public sealed class JsonRpcGameClientTests
{
    [Theory]
    [InlineData("{\"processId\":1234,\"launchToken\":\"abc\"}", 1234, "abc")]
    [InlineData("{\"processId\":\"1234\",\"launchToken\":\"abc\"}", 1234, "abc")]
    public async Task GetHealthAsync_ShouldParseNumericOrStringProcessId(
        string body,
        int expectedProcessId,
        string expectedLaunchToken)
    {
        using var handler = new FakeHandler(_ => Task.FromResult(JsonResponse(body)));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var snapshot = await client.GetHealthAsync(CancellationToken.None);

        Assert.Equal(new GameHealthSnapshot(expectedProcessId, expectedLaunchToken), snapshot);
        Assert.Equal("http://127.0.0.1:18080/root/health", handler.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task GetHealthAsync_SuccessWithoutIdentity_ShouldReturnEmptyObservation()
    {
        using var handler = new FakeHandler(_ => Task.FromResult(JsonResponse("{\"status\":\"ok\"}")));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var snapshot = await client.GetHealthAsync(CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Null(snapshot!.ProcessId);
        Assert.Null(snapshot.LaunchToken);
    }

    [Fact]
    public async Task GetHealthAsync_MalformedJson_ShouldReturnNull()
    {
        using var handler = new FakeHandler(_ => Task.FromResult(JsonResponse("not-json")));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        Assert.Null(await client.GetHealthAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetHealthAsync_NonSuccessResponse_ShouldReturnNull()
    {
        using var handler = new FakeHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("offline", Encoding.UTF8, "text/plain")
            }));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        Assert.Null(await client.GetHealthAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetHealthAsync_TransientHttpRequestException_ShouldReturnNull()
    {
        using var handler = new FakeHandler(_ => throw new HttpRequestException("connection refused"));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        Assert.Null(await client.GetHealthAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetHealthAsync_PerRequestTimeout_ShouldReturnNull()
    {
        using var handler = new FakeHandler(_ => throw new TaskCanceledException("request timeout"));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        Assert.Null(await client.GetHealthAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetHealthAsync_CallerCancellation_ShouldPropagateCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var handler = new FakeHandler(_ => throw new OperationCanceledException(cancellation.Token));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetHealthAsync(cancellation.Token));
    }

    [Fact]
    public async Task SendKeyAsync_ShouldSendPressAndReleaseWireValues()
    {
        using var handler = new FakeHandler(req => EchoJsonRpcResponseAsync(req, "{\"success\":true}"));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        await client.SendKeyAsync("Enter", TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(2, ReadInputType(handler.RequestBodies[0]));
        Assert.Equal(3, ReadInputType(handler.RequestBodies[1]));
        Assert.All(handler.Requests, request => Assert.Equal("secret", request.Headers.GetValues("X-Api-Key").Single()));
    }

    [Fact]
    public async Task SendKeyAsync_WhenHoldDelayIsCanceled_ShouldStillSendReleaseAndRethrowCancellation()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        using var handler = new FakeHandler(req => Task.FromResult(EchoJsonRpcResponse(req)));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.SendKeyAsync("Enter", TimeSpan.FromSeconds(30), cancellation.Token));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(2, ReadInputType(handler.RequestBodies[0]));
        Assert.Equal(3, ReadInputType(handler.RequestBodies[1]));
    }

    [Fact]
    public async Task SendMidiNoteAsync_ShouldSendNoteOnAndNoteOffWireValues()
    {
        using var handler = new FakeHandler(req => EchoJsonRpcResponseAsync(req, "{\"success\":true}"));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        await client.SendMidiNoteAsync(36, 100, TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(4, ReadInputType(handler.RequestBodies[0]));
        Assert.Equal(5, ReadInputType(handler.RequestBodies[1]));
        Assert.Contains("\"noteNumber\":36", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"velocity\":100", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendMidiNoteAsync_WhenHoldDelayIsCanceled_ShouldStillSendReleaseAndRethrowCancellation()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        using var handler = new FakeHandler(req => Task.FromResult(EchoJsonRpcResponse(req)));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.SendMidiNoteAsync(36, 100, TimeSpan.FromSeconds(30), cancellation.Token));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(4, ReadInputType(handler.RequestBodies[0]));
        Assert.Equal(5, ReadInputType(handler.RequestBodies[1]));
    }

    [Fact]
    public async Task GetGameStateAsync_ShouldDeserializeTelemetrySnapshot()
    {
        const string result = """
            {
              "currentStage":"DTXMania.Game.Lib.Stage.ResultStage",
              "customData":{"telemetry":{"stageType":"Result","selectedSongTitle":"Smoke","score":1000000,"clearFlag":true,"totalNotes":6,"totalJudgements":6,"completionReason":"SongComplete"}}
            }
            """;
        using var handler = new FakeHandler(_ => Task.FromResult(JsonRpcResponse(result)));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var state = await client.GetGameStateAsync(CancellationToken.None);

        Assert.Equal("Result", state.StageType);
        Assert.Equal("Smoke", state.SelectedSongTitle);
        Assert.Equal(1000000, state.Score);
        Assert.True(state.ClearFlag);
        Assert.Equal(6, state.TotalNotes);
        Assert.Equal(6, state.TotalJudgements);
        Assert.Equal("SongComplete", state.CompletionReason);
    }

    [Fact]
    public async Task ChangeStageAsync_HttpFailure_ShouldIncludeMethodStatusAndBody()
    {
        using var handler = new FakeHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("maintenance", Encoding.UTF8, "text/plain")
            }));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ChangeStageAsync("Title", CancellationToken.None));

        Assert.Contains("changeStage", exception.Message, StringComparison.Ordinal);
        Assert.Contains("503", exception.Message, StringComparison.Ordinal);
        Assert.Contains("maintenance", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangeStageAsync_JsonRpcError_ShouldIncludeMethodAndBody()
    {
        const string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32602,\"message\":\"bad stage\"}}";
        using var handler = new FakeHandler(_ => Task.FromResult(JsonResponse(body)));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ChangeStageAsync("Title", CancellationToken.None));

        Assert.Contains("changeStage", exception.Message, StringComparison.Ordinal);
        Assert.Contains("bad stage", exception.Message, StringComparison.Ordinal);
        Assert.Contains(body, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_WhenResponseIdMismatchesRequestId_ShouldReject()
    {
        const string body = "{\"jsonrpc\":\"2.0\",\"id\":999,\"result\":{\"ok\":true}}";
        using var handler = new FakeHandler(_ => Task.FromResult(JsonResponse(body)));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ChangeStageAsync("Title", CancellationToken.None));

        Assert.Contains("does not match", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("999", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_WhenResponseOmitsId_ShouldReject()
    {
        const string body = "{\"jsonrpc\":\"2.0\",\"result\":{\"ok\":true}}";
        using var handler = new FakeHandler(_ => Task.FromResult(JsonResponse(body)));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ChangeStageAsync("Title", CancellationToken.None));

        Assert.Contains("did not include an id", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetGameStateAsync_MalformedJsonRpc_ShouldIncludeMethodAndBody()
    {
        const string body = "not-json";
        using var handler = new FakeHandler(_ => Task.FromResult(JsonResponse(body)));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetGameStateAsync(CancellationToken.None));

        Assert.Contains("getGameState", exception.Message, StringComparison.Ordinal);
        Assert.Contains(body, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TakeScreenshotBase64Async_ShouldReturnImageData()
    {
        using var handler = new FakeHandler(_ => Task.FromResult(JsonRpcResponse("{\"imageData\":\"aGVsbG8=\"}")));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var image = await client.TakeScreenshotBase64Async(CancellationToken.None);

        Assert.Equal("aGVsbG8=", image);
    }

    [Fact]
    public async Task Client_WithBlankApiKey_ShouldOmitApiKeyHeader()
    {
        using var handler = new FakeHandler(_ => Task.FromResult(JsonRpcResponse("{\"success\":true}")));
        using var httpClient = CreateHttpClient(handler);
        var client = new JsonRpcGameClient(
            httpClient,
            new GameApiConnectionOptions(new Uri("http://127.0.0.1:18080/root/"), " "));

        await client.ChangeStageAsync("Title", CancellationToken.None);

        Assert.False(handler.Requests.Single().Headers.Contains("X-Api-Key"));
    }

    private static JsonRpcGameClient CreateClient(HttpClient httpClient) =>
        new(httpClient, new GameApiConnectionOptions(new Uri("http://127.0.0.1:18080/root/"), "secret"));

    private static HttpClient CreateHttpClient(FakeHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://127.0.0.1:18080/root/") };

    private static int ReadInputType(string requestBody)
    {
        using var document = JsonDocument.Parse(requestBody);
        return document.RootElement
            .GetProperty("params")
            .GetProperty("type")
            .GetInt32();
    }

    private static HttpResponseMessage JsonRpcResponse(string resultJson) =>
        JsonResponse($"{{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{resultJson}}}");

    private static HttpResponseMessage EchoJsonRpcResponse(HttpRequestMessage request)
    {
        var id = ReadRequestId(request);
        return JsonResponse($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"success\":true}}}}");
    }

    private static async Task<HttpResponseMessage> EchoJsonRpcResponseAsync(
        HttpRequestMessage request,
        string resultJson)
    {
        var id = await ReadRequestIdAsync(request).ConfigureAwait(false);
        return JsonResponse($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{resultJson}}}");
    }

    private static int ReadRequestId(HttpRequestMessage request) =>
        ReadRequestIdAsync(request).GetAwaiter().GetResult();

    private static async Task<int> ReadRequestIdAsync(HttpRequestMessage request)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content!.ReadAsStringAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("id").GetInt32();
    }

    private static HttpResponseMessage JsonResponse(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class FakeHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return await responseFactory(request);
        }
    }
}
