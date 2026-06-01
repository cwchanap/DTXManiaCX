using System.Net;
using System.Text.Json;

namespace DTXMania.E2E.JsonRpc;

[Trait("Category", "E2E-Support")]
public sealed class JsonRpcGameClientTests
{
    [Fact]
    public async Task SendKeyAsync_ShouldSendPressAndReleaseRequests()
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:18080/") };
        var client = new JsonRpcGameClient(httpClient, "secret");

        await client.SendKeyAsync("Enter", TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Contains("\"method\":\"sendInput\"", handler.RequestBodies[0]);
        Assert.Contains("\"type\":2", handler.RequestBodies[0]);
        Assert.Contains("\"Enter\"", handler.RequestBodies[0]);
        Assert.Contains("\"type\":3", handler.RequestBodies[1]);
        Assert.All(handler.ApiKeys, apiKey => Assert.Equal("secret", apiKey));
    }

    [Fact]
    public async Task GetGameStateAsync_ShouldDeserializeTelemetry()
    {
        using var handler = new RecordingHandler(
            new
            {
                currentStage = "DTXMania.Game.Lib.Stage.ResultStage",
                customData = new
                {
                    telemetry = new
                    {
                        stageType = "Result",
                        selectedSongTitle = "E2E AutoPlay Smoke",
                        score = 1000000,
                        clearFlag = true,
                        totalNotes = 6,
                        totalJudgements = 6,
                        completionReason = "SongComplete"
                    }
                }
            });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:18080/") };
        var client = new JsonRpcGameClient(httpClient, "secret");

        var state = await client.GetGameStateAsync(CancellationToken.None);

        Assert.Equal("Result", state.StageType);
        Assert.Equal("E2E AutoPlay Smoke", state.SelectedSongTitle);
        Assert.Equal(1000000, state.Score);
        Assert.True(state.ClearFlag);
        Assert.Equal(6, state.TotalNotes);
        Assert.Equal(6, state.TotalJudgements);
        Assert.Equal("SongComplete", state.CompletionReason);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly object _result;

        public RecordingHandler()
            : this(new { success = true })
        {
        }

        public RecordingHandler(object result)
        {
            _result = result;
        }

        public List<string> RequestBodies { get; } = new();

        public List<string?> ApiKeys { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            ApiKeys.Add(request.Headers.TryGetValues("X-Api-Key", out var values) ? values.SingleOrDefault() : null);

            var responseJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                result = _result
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            };
        }
    }
}
