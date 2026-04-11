using DTXMania.Game.Lib;
using DTXMania.Game.Lib.JsonRpc;
using Moq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace DTXMania.Test.JsonRpc
{
    /// <summary>
    /// Focused validation tests for JsonRpcServer covering edge cases in
    /// ValidateGameInput, HandleSendInput, HandleChangeStage, and HTTP handling.
    /// </summary>
    [Trait("Category", "Integration")]
    public class JsonRpcServerValidationTests : IAsyncDisposable
    {
        private JsonRpcServer? _server;
        private readonly Mock<IGameApi> _mockGameApi;

        public JsonRpcServerValidationTests()
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
            return new HttpClient(new SocketsHttpHandler { UseCookies = false }) { BaseAddress = new Uri($"http://localhost:{port}") };
        }

        private static StringContent RawRpcBody(string request) =>
            new StringContent(request, Encoding.UTF8, "application/json");

        private static int _portBase = 12000;
        private static readonly object _portLock = new();
        private static int NextPort() { lock (_portLock) return _portBase++; }

        #region ValidateGameInput Tests (via sendInput)

        [Fact]
        public async Task SendInput_WithInvalidInputType_ShouldReturnInvalidInput()
        {
            // Type = 999 is not in the InputType enum
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":999,\"data\":\"test\"}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32002", body); // InvalidInput
            Assert.Contains("Invalid input type", body);
        }

        [Fact]
        public async Task SendInput_MouseClick_WithNullData_ShouldReturnInvalidInput()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":0,\"data\":null}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32002", body);
            Assert.Contains("Mouse input requires position data", body);
        }

        [Fact]
        public async Task SendInput_MouseClick_WithNonObjectData_ShouldReturnInvalidInput()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":0,\"data\":\"not-an-object\"}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32002", body);
            Assert.Contains("Mouse input data must be an object", body);
        }

        [Fact]
        public async Task SendInput_MouseMove_WithNullData_ShouldReturnInvalidInput()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":1,\"data\":null}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32002", body);
            Assert.Contains("Mouse input requires position data", body);
        }

        [Fact]
        public async Task SendInput_MouseMove_WithNonObjectData_ShouldReturnInvalidInput()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":1,\"data\":42}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32002", body);
            Assert.Contains("Mouse input data must be an object", body);
        }

        [Fact]
        public async Task SendInput_KeyPress_WithNullData_ShouldReturnInvalidInput()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":null}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32002", body);
            Assert.Contains("Key input requires key data", body);
        }

        [Fact]
        public async Task SendInput_KeyPress_WithWhitespaceStringData_ShouldReturnInvalidInput()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":\"   \"}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32002", body);
            Assert.Contains("Invalid key data format", body);
        }

        [Fact]
        public async Task SendInput_KeyPress_WithOverlongStringData_ShouldReturnInvalidInput()
        {
            using var client = await StartServerAsync(NextPort());
            var longKey = new string('A', 51); // exceeds 50 char limit
            var requestJson = $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{{\"type\":2,\"data\":\"{longKey}\"}}}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32002", body);
            Assert.Contains("Invalid key data format", body);
        }

        [Fact]
        public async Task SendInput_KeyPress_WithNegativeKeyCode_ShouldReturnInvalidInput()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":-1}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32002", body);
            Assert.Contains("Invalid key data format", body);
        }

        [Fact]
        public async Task SendInput_KeyPress_WithKeyCodeAbove255_ShouldReturnInvalidInput()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":256}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32002", body);
            Assert.Contains("Invalid key data format", body);
        }

        [Fact]
        public async Task SendInput_KeyPress_WithValidKeyCodeZero_ShouldSucceed()
        {
            _mockGameApi.Setup(api => api.SendInputAsync(It.IsAny<GameInput>())).ReturnsAsync(true);
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":0}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode);
            _mockGameApi.Verify(api => api.SendInputAsync(It.IsAny<GameInput>()), Times.Once);
        }

        [Fact]
        public async Task SendInput_KeyPress_WithValidKeyCode255_ShouldSucceed()
        {
            _mockGameApi.Setup(api => api.SendInputAsync(It.IsAny<GameInput>())).ReturnsAsync(true);
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":255}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode);
            _mockGameApi.Verify(api => api.SendInputAsync(It.IsAny<GameInput>()), Times.Once);
        }

        [Fact]
        public async Task SendInput_KeyPress_WithObjectMissingKeyProperty_ShouldReturnInvalidInput()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":{\"holdDurationMs\":50}}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32002", body);
            Assert.Contains("Invalid key data format", body);
        }

        [Fact]
        public async Task SendInput_KeyPress_WithObjectKeyNotString_ShouldReturnInvalidInput()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":{\"key\":123,\"holdDurationMs\":50}}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32002", body);
            Assert.Contains("Invalid key data format", body);
        }

        [Fact]
        public async Task SendInput_KeyPress_WithObjectKeyWhitespace_ShouldReturnInvalidInput()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":{\"key\":\"   \",\"holdDurationMs\":50}}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32002", body);
            Assert.Contains("Invalid key data format", body);
        }

        [Fact]
        public async Task SendInput_KeyPress_WithObjectKeyOverlong_ShouldReturnInvalidInput()
        {
            using var client = await StartServerAsync(NextPort());
            var longKey = new string('B', 51);
            var requestJson = $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{{\"type\":2,\"data\":{{\"key\":\"{longKey}\",\"holdDurationMs\":50}}}}}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32002", body);
            Assert.Contains("Invalid key data format", body);
        }

        [Fact]
        public async Task SendInput_KeyPress_WithBooleanData_ShouldReturnInvalidInput()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":true}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32002", body);
            Assert.Contains("Invalid key data format", body);
        }

        [Fact]
        public async Task SendInput_KeyPress_WithArrayData_ShouldReturnInvalidInput()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":[\"Enter\"]}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32002", body);
            Assert.Contains("Invalid key data format", body);
        }

        [Fact]
        public async Task SendInput_KeyRelease_WithNullData_ShouldReturnInvalidInput()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":3,\"data\":null}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32002", body);
            Assert.Contains("Key input requires key data", body);
        }

        [Fact]
        public async Task SendInput_KeyRelease_WithWhitespaceStringData_ShouldReturnInvalidInput()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":3,\"data\":\"\\t\\n\"}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32002", body);
            Assert.Contains("Invalid key data format", body);
        }

        [Fact]
        public async Task SendInput_MouseClick_WithValidObjectData_ShouldSucceed()
        {
            _mockGameApi.Setup(api => api.SendInputAsync(It.IsAny<GameInput>())).ReturnsAsync(true);
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":0,\"data\":{\"x\":100,\"y\":200}}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode);
            _mockGameApi.Verify(api => api.SendInputAsync(It.IsAny<GameInput>()), Times.Once);
        }

        [Fact]
        public async Task SendInput_MouseMove_WithValidObjectData_ShouldSucceed()
        {
            _mockGameApi.Setup(api => api.SendInputAsync(It.IsAny<GameInput>())).ReturnsAsync(true);
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":1,\"data\":{\"x\":150,\"y\":250}}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode);
            _mockGameApi.Verify(api => api.SendInputAsync(It.IsAny<GameInput>()), Times.Once);
        }

        [Fact]
        public async Task SendInput_KeyPress_WithValidStringData_ShouldSucceed()
        {
            _mockGameApi.Setup(api => api.SendInputAsync(It.IsAny<GameInput>())).ReturnsAsync(true);
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":\"Enter\"}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode);
            _mockGameApi.Verify(api => api.SendInputAsync(It.IsAny<GameInput>()), Times.Once);
        }

        [Fact]
        public async Task SendInput_KeyRelease_WithValidStringData_ShouldSucceed()
        {
            _mockGameApi.Setup(api => api.SendInputAsync(It.IsAny<GameInput>())).ReturnsAsync(true);
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":3,\"data\":\"Escape\"}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode);
            _mockGameApi.Verify(api => api.SendInputAsync(It.IsAny<GameInput>()), Times.Once);
        }

        [Fact]
        public async Task SendInput_KeyPress_WithValidObjectData_ShouldSucceed()
        {
            _mockGameApi.Setup(api => api.SendInputAsync(It.IsAny<GameInput>())).ReturnsAsync(true);
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":{\"key\":\"Down\",\"holdDurationMs\":50,\"clientId\":\"default\"}}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode);
            _mockGameApi.Verify(api => api.SendInputAsync(It.IsAny<GameInput>()), Times.Once);
        }

        #endregion

        #region HandleChangeStage Tests

        [Fact]
        public async Task ChangeStage_WithParamsNotObject_ShouldReturnInvalidParams()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"changeStage\",\"params\":\"Title\"}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32602", body);
            Assert.Contains("params must be an object", body);
        }

        [Fact]
        public async Task ChangeStage_WithParamsArray_ShouldReturnInvalidParams()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"changeStage\",\"params\":[\"Title\"]}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32602", body);
            Assert.Contains("params must be an object", body);
        }

        [Fact]
        public async Task ChangeStage_WithMissingStageName_ShouldReturnInvalidParams()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"changeStage\",\"params\":{\"otherField\":\"value\"}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32602", body);
            Assert.Contains("stageName must be a non-empty string", body);
        }

        [Fact]
        public async Task ChangeStage_WithWhitespaceStageName_ShouldReturnInvalidParams()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"changeStage\",\"params\":{\"stageName\":\"   \"}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32602", body);
            Assert.Contains("stageName must be a non-empty string", body);
        }

        [Fact]
        public async Task ChangeStage_WithEmptyStageName_ShouldReturnInvalidParams()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"changeStage\",\"params\":{\"stageName\":\"\"}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32602", body);
            Assert.Contains("stageName must be a non-empty string", body);
        }

        [Fact]
        public async Task ChangeStage_WithNonStringStageName_ShouldReturnInvalidParams()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"changeStage\",\"params\":{\"stageName\":123}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32602", body);
            Assert.Contains("stageName must be a string", body);
        }

        [Fact]
        public async Task ChangeStage_WithBooleanStageName_ShouldReturnInvalidParams()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"changeStage\",\"params\":{\"stageName\":true}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32602", body);
            Assert.Contains("stageName must be a string", body);
        }

        [Fact]
        public async Task ChangeStage_WithNullStageName_ShouldReturnInvalidParams()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"changeStage\",\"params\":{\"stageName\":null}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32602", body);
            Assert.Contains("stageName must be a string", body);
        }

        [Fact]
        public async Task ChangeStage_WhenGameApiReturnsFalse_ShouldReturnInvalidParams()
        {
            _mockGameApi.Setup(api => api.ChangeStageAsync(It.IsAny<string>())).ReturnsAsync(false);
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"changeStage\",\"params\":{\"stageName\":\"InvalidStage\"}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32602", body);
            Assert.Contains("Unknown stage name", body);
            Assert.Contains("InvalidStage", body);
        }

        [Fact]
        public async Task ChangeStage_WithValidStageName_ShouldSucceed()
        {
            _mockGameApi.Setup(api => api.ChangeStageAsync("SongSelect")).ReturnsAsync(true);
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"changeStage\",\"params\":{\"stageName\":\"SongSelect\"}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode);
            Assert.Contains("\"success\":true", body);
            Assert.Contains("SongSelect", body);
        }

        #endregion

        #region HTTP/Request Validation Edge Cases

        [Fact]
        public async Task HandleJsonRpcRequest_WithEmptyMethod_ShouldReturnInvalidRequest()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"\"}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32600", body);
            Assert.Contains("Invalid JSON-RPC 2.0 request format", body);
        }

        [Fact]
        public async Task HandleJsonRpcRequest_WithNullMethod_ShouldReturnInvalidRequest()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":null}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32600", body);
        }

        [Fact]
        public async Task HandleJsonRpcRequest_DeserializesToNull_ShouldReturnInvalidRequest()
        {
            using var client = await StartServerAsync(NextPort());
            var requestJson = "null";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32600", body);
            Assert.Contains("Request is null", body);
        }

        #endregion

        #region GetWindowInfo Game Not Running

        [Fact]
        public async Task GetWindowInfo_WhenGameNotRunning_ShouldReturnGameNotRunningError()
        {
            _mockGameApi.Setup(api => api.IsRunning).Returns(false);
            using var client = await StartServerAsync(NextPort());

            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"getWindowInfo\"}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32001", body);
            Assert.Contains("Game is not running", body);
        }

        #endregion

        #region SendInput Game Not Running

        [Fact]
        public async Task SendInput_WhenGameNotRunning_ShouldReturnGameNotRunningError()
        {
            _mockGameApi.Setup(api => api.IsRunning).Returns(false);
            using var client = await StartServerAsync(NextPort());

            var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":\"Enter\"}}";
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("-32001", body);
        }

        #endregion
    }
}
