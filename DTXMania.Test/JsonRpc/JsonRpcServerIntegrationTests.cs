using DTXMania.Game.Lib;
using DTXMania.Game.Lib.JsonRpc;
using Moq;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace DTXMania.Test.JsonRpc
{
    /// <summary>
    /// Integration tests that start a real Kestrel-based JsonRpcServer and exercise
    /// the HTTP handling, routing, and all method handlers end-to-end.
    /// These tests use a mocked IGameApi so no game process is required.
    /// </summary>
    public class JsonRpcServerIntegrationTests : IAsyncDisposable
    {
        private JsonRpcServer? _server;
        private readonly Mock<IGameApi> _mockGameApi;

        public JsonRpcServerIntegrationTests()
        {
            _mockGameApi = new Mock<IGameApi>();
            _mockGameApi.Setup(api => api.IsRunning).Returns(true);
        }

        public async ValueTask DisposeAsync()
        {
            if (_server != null)
            {
                await _server.DisposeAsync();
                _server = null;
            }
        }

        private async Task<HttpClient> StartServerAsync(int port, string apiKey = "")
        {
            _server = new JsonRpcServer(_mockGameApi.Object, port: port, apiKey: apiKey);
            await _server.StartAsync();
            return new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        }

        private static StringContent RpcBody(object request) =>
            new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        private static int _portBase = 11000;
        private static readonly object _portLock = new();
        private static int NextPort() { lock (_portLock) return _portBase++; }

        #region Health endpoint

        [Fact]
        public async Task HealthEndpoint_ShouldReturnOkAndGameRunningFlag()
        {
            using var client = await StartServerAsync(NextPort());

            var response = await client.GetAsync("/health");
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode);
            Assert.Contains("\"status\"", body);
            Assert.Contains("ok", body);
            Assert.Contains("gameRunning", body);
        }

        [Fact]
        public async Task HealthEndpoint_WhenGameNotRunning_StillReturnsOkWithFalseFlag()
        {
            _mockGameApi.Setup(api => api.IsRunning).Returns(false);
            using var client = await StartServerAsync(NextPort());

            var response = await client.GetAsync("/health");
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode);
            Assert.Contains("false", body);
        }

        #endregion

        #region Request parsing / format validation

        [Fact]
        public async Task JsonRpcEndpoint_WithInvalidJson_ShouldReturnParseError()
        {
            using var client = await StartServerAsync(NextPort());
            using var content = new StringContent("not-valid-json{", Encoding.UTF8, "application/json");

            var response = await client.PostAsync("/jsonrpc", content);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32700", body);
        }

        [Fact]
        public async Task JsonRpcEndpoint_WithWrongVersionField_ShouldReturnInvalidRequest()
        {
            using var client = await StartServerAsync(NextPort());
            var request = new { jsonrpc = "1.0", id = 1, method = "ping" }; // wrong version
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32600", body);
        }

        [Fact]
        public async Task JsonRpcEndpoint_WithUnknownMethod_ShouldReturnMethodNotFound()
        {
            using var client = await StartServerAsync(NextPort());
            var request = new { jsonrpc = "2.0", id = 1, method = "doesNotExist" };
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32601", body);
        }

        #endregion

        #region API key authentication

        [Fact]
        public async Task ApiKey_WithMissingKey_ShouldReturnUnauthorized()
        {
            using var client = await StartServerAsync(NextPort(), apiKey: "supersecret");
            var request = new { jsonrpc = "2.0", id = 1, method = "ping" };
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));

            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ApiKey_WithWrongKey_ShouldReturnUnauthorized()
        {
            using var client = await StartServerAsync(NextPort(), apiKey: "supersecret");
            client.DefaultRequestHeaders.Add("X-Api-Key", "wrongkey");
            var request = new { jsonrpc = "2.0", id = 1, method = "ping" };
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));

            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ApiKey_WithCorrectKey_ShouldReturnSuccess()
        {
            using var client = await StartServerAsync(NextPort(), apiKey: "supersecret");
            client.DefaultRequestHeaders.Add("X-Api-Key", "supersecret");
            var request = new { jsonrpc = "2.0", id = 1, method = "ping" };
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode);
            Assert.Contains("pong", body);
        }

        #endregion

        #region ping

        [Fact]
        public async Task Ping_ShouldReturnPong()
        {
            using var client = await StartServerAsync(NextPort());
            var request = new { jsonrpc = "2.0", id = 1, method = "ping" };
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode);
            Assert.Contains("pong", body);
        }

        #endregion

        #region getGameState

        [Fact]
        public async Task GetGameState_ShouldReturnCurrentState()
        {
            var gameState = new GameState { CurrentStage = "TestStage", Score = 42 };
            _mockGameApi.Setup(api => api.GetGameStateAsync()).ReturnsAsync(gameState);
            using var client = await StartServerAsync(NextPort());

            var request = new { jsonrpc = "2.0", id = 1, method = "getGameState" };
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode);
            Assert.Contains("TestStage", body);
        }

        [Fact]
        public async Task GetGameState_WhenGameNotRunning_ShouldReturnGameNotRunningError()
        {
            _mockGameApi.Setup(api => api.IsRunning).Returns(false);
            using var client = await StartServerAsync(NextPort());

            var request = new { jsonrpc = "2.0", id = 1, method = "getGameState" };
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32001", body);
        }

        #endregion

        #region getWindowInfo

        [Fact]
        public async Task GetWindowInfo_ShouldReturnWindowDimensions()
        {
            var info = new GameWindowInfo { Width = 1920, Height = 1080, Title = "DTXManiaCX" };
            _mockGameApi.Setup(api => api.GetWindowInfoAsync()).ReturnsAsync(info);
            using var client = await StartServerAsync(NextPort());

            var request = new { jsonrpc = "2.0", id = 1, method = "getWindowInfo" };
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode);
            Assert.Contains("1920", body);
        }

        #endregion

        #region sendInput

        [Fact]
        public async Task SendInput_WithNullParams_ShouldReturnInvalidParams()
        {
            using var client = await StartServerAsync(NextPort());
            var request = new { jsonrpc = "2.0", id = 1, method = "sendInput" };
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32602", body);
        }

        [Fact]
        public async Task SendInput_WithValidKeyPress_ShouldCallGameApi()
        {
            _mockGameApi.Setup(api => api.SendInputAsync(It.IsAny<GameInput>())).ReturnsAsync(true);
            using var client = await StartServerAsync(NextPort());

            var request = new
            {
                jsonrpc = "2.0", id = 1, method = "sendInput",
                @params = new { type = 2, data = "Enter" } // KeyPress + string key
            };
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode);
            _mockGameApi.Verify(api => api.SendInputAsync(It.IsAny<GameInput>()), Times.Once);
        }

        [Fact]
        public async Task SendInput_MouseClick_WithoutPositionData_ShouldReturnInvalidInput()
        {
            using var client = await StartServerAsync(NextPort());
            var request = new
            {
                jsonrpc = "2.0", id = 1, method = "sendInput",
                @params = new { type = 0 } // MouseClick without data
            };
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32002", body); // InvalidInput
        }

        [Fact]
        public async Task SendInput_KeyPress_WithoutKeyData_ShouldReturnInvalidInput()
        {
            using var client = await StartServerAsync(NextPort());
            var request = new
            {
                jsonrpc = "2.0", id = 1, method = "sendInput",
                @params = new { type = 2 } // KeyPress without data
            };
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32002", body); // InvalidInput
        }

        #endregion

        #region takeScreenshot

        [Fact]
        public async Task TakeScreenshot_WhenGameRunning_ShouldReturnBase64ImageData()
        {
            var pngBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            _mockGameApi.Setup(api => api.TakeScreenshotAsync()).ReturnsAsync(pngBytes);
            using var client = await StartServerAsync(NextPort());

            var request = new { jsonrpc = "2.0", id = 1, method = "takeScreenshot" };
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode);
            Assert.Contains("imageData", body);
            Assert.Contains("image/png", body);
            Assert.Contains(Convert.ToBase64String(pngBytes), body);
        }

        [Fact]
        public async Task TakeScreenshot_WhenGameNotRunning_ShouldReturnGameNotRunningError()
        {
            _mockGameApi.Setup(api => api.IsRunning).Returns(false);
            using var client = await StartServerAsync(NextPort());

            var request = new { jsonrpc = "2.0", id = 1, method = "takeScreenshot" };
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32001", body);
        }

        [Fact]
        public async Task TakeScreenshot_WhenCaptureReturnsNull_ShouldReturnInternalError()
        {
            _mockGameApi.Setup(api => api.TakeScreenshotAsync()).ReturnsAsync((byte[]?)null);
            using var client = await StartServerAsync(NextPort());

            var request = new { jsonrpc = "2.0", id = 1, method = "takeScreenshot" };
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32603", body);
        }

        #endregion

        #region changeStage

        [Fact]
        public async Task ChangeStage_WithValidStage_ShouldReturnSuccess()
        {
            _mockGameApi.Setup(api => api.ChangeStageAsync("SongSelect")).ReturnsAsync(true);
            using var client = await StartServerAsync(NextPort());

            var request = new
            {
                jsonrpc = "2.0", id = 1, method = "changeStage",
                @params = new { stageName = "SongSelect" }
            };
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode);
            Assert.Contains("true", body);
            Assert.Contains("SongSelect", body);
        }

        [Fact]
        public async Task ChangeStage_WithInvalidStage_ShouldReturnInvalidParams()
        {
            _mockGameApi.Setup(api => api.ChangeStageAsync("NonExistent")).ReturnsAsync(false);
            using var client = await StartServerAsync(NextPort());

            var request = new
            {
                jsonrpc = "2.0", id = 1, method = "changeStage",
                @params = new { stageName = "NonExistent" }
            };
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32602", body);
        }

        [Fact]
        public async Task ChangeStage_WithMissingParams_ShouldReturnInvalidParams()
        {
            using var client = await StartServerAsync(NextPort());

            var request = new { jsonrpc = "2.0", id = 1, method = "changeStage" };
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32602", body);
        }

        [Fact]
        public async Task ChangeStage_WithNonStringStageNameValue_ShouldReturnInvalidParams()
        {
            // {"stageName": 123} — value is a number, not a string
            using var client = await StartServerAsync(NextPort());
            var request = new
            {
                jsonrpc = "2.0", id = 1, method = "changeStage",
                @params = new { stageName = 123 }
            };
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32602", body);
        }

        [Fact]
        public async Task ChangeStage_WhenGameNotRunning_ShouldReturnGameNotRunningError()
        {
            _mockGameApi.Setup(api => api.IsRunning).Returns(false);
            using var client = await StartServerAsync(NextPort());

            var request = new
            {
                jsonrpc = "2.0", id = 1, method = "changeStage",
                @params = new { stageName = "Title" }
            };
            using var response = await client.PostAsync("/jsonrpc", RpcBody(request));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32001", body);
        }

        #endregion
    }
}
