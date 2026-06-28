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

        private async Task AssertSendInputValidationAsync(
            string requestJson,
            string expectedCode,
            string expectedText)
        {
            using var client = await StartServerAsync(NextPort());
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains(expectedCode, body);
            Assert.Contains(expectedText, body);
            _mockGameApi.Verify(api => api.SendInputAsync(It.IsAny<GameInput>()), Times.Never);
        }

        private async Task AssertSendInputSuccessAsync(string requestJson)
        {
            _mockGameApi.Setup(api => api.SendInputAsync(It.IsAny<GameInput>())).ReturnsAsync(true);

            using var client = await StartServerAsync(NextPort());
            using var response = await client.PostAsync("/jsonrpc", RawRpcBody(requestJson));
            _ = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode);
            _mockGameApi.Verify(api => api.SendInputAsync(It.IsAny<GameInput>()), Times.Once);
        }

        #region ValidateGameInput Tests (via sendInput)

        [Theory]
        [InlineData("SendInput_WithInvalidInputType", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":999,\"data\":\"test\"}}", "-32002", "Invalid input type")]
        [InlineData("SendInput_MouseClick_WithNullData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":0,\"data\":null}}", "-32002", "Mouse input requires position data")]
        [InlineData("SendInput_MouseClick_WithNonObjectData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":0,\"data\":\"not-an-object\"}}", "-32002", "Mouse input data must be an object")]
        [InlineData("SendInput_MouseMove_WithNullData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":1,\"data\":null}}", "-32002", "Mouse input requires position data")]
        [InlineData("SendInput_MouseMove_WithNonObjectData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":1,\"data\":42}}", "-32002", "Mouse input data must be an object")]
        [InlineData("SendInput_KeyPress_WithNullData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":null}}", "-32002", "Key input requires key data")]
        [InlineData("SendInput_KeyPress_WithWhitespaceStringData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":\"   \"}}", "-32002", "Invalid key data format")]
        [InlineData("SendInput_KeyPress_WithOverlongStringData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}}", "-32002", "Invalid key data format")]
        [InlineData("SendInput_KeyPress_WithNegativeKeyCode", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":-1}}", "-32002", "Invalid key data format")]
        [InlineData("SendInput_KeyPress_WithKeyCodeAbove255", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":256}}", "-32002", "Invalid key data format")]
        [InlineData("SendInput_KeyPress_WithObjectMissingKeyProperty", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":{\"holdDurationMs\":50}}}", "-32002", "Invalid key data format")]
        [InlineData("SendInput_KeyPress_WithObjectKeyNotString", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":{\"key\":123,\"holdDurationMs\":50}}}", "-32002", "Invalid key data format")]
        [InlineData("SendInput_KeyPress_WithObjectKeyWhitespace", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":{\"key\":\"   \",\"holdDurationMs\":50}}}", "-32002", "Invalid key data format")]
        [InlineData("SendInput_KeyPress_WithObjectKeyOverlong", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":{\"key\":\"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB\",\"holdDurationMs\":50}}}", "-32002", "Invalid key data format")]
        [InlineData("SendInput_KeyPress_WithBooleanData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":true}}", "-32002", "Invalid key data format")]
        [InlineData("SendInput_KeyPress_WithArrayData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":[\"Enter\"]}}", "-32002", "Invalid key data format")]
        [InlineData("SendInput_KeyRelease_WithNullData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":3,\"data\":null}}", "-32002", "Key input requires key data")]
        [InlineData("SendInput_KeyRelease_WithWhitespaceStringData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":3,\"data\":\"\\t\\n\"}}", "-32002", "Invalid key data format")]
        [InlineData("SendInput_MidiNoteOn_WithNullData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":4,\"data\":null}}", "-32002", "MIDI input requires note data")]
        [InlineData("SendInput_MidiNoteOn_WithNonObjectData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":4,\"data\":\"not-an-object\"}}", "-32002", "MIDI input data must be an object")]
        [InlineData("SendInput_MidiNoteOn_WithMissingNoteNumber", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":4,\"data\":{\"velocity\":100}}}", "-32002", "Invalid MIDI note data format")]
        [InlineData("SendInput_MidiNoteOn_WithMissingVelocity", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":4,\"data\":{\"noteNumber\":36}}}", "-32002", "Invalid MIDI note data format")]
        [InlineData("SendInput_MidiNoteOn_WithNoteNumberOutOfRange", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":4,\"data\":{\"noteNumber\":128,\"velocity\":100}}}", "-32002", "Invalid MIDI note data format")]
        [InlineData("SendInput_MidiNoteOn_WithVelocityOutOfRange", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":4,\"data\":{\"noteNumber\":36,\"velocity\":128}}}", "-32002", "Invalid MIDI note data format")]
        [InlineData("SendInput_MidiNoteOff_WithNullData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":5,\"data\":null}}", "-32002", "MIDI input requires note data")]
        [InlineData("SendInput_MidiNoteOff_WithNonObjectData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":5,\"data\":42}}", "-32002", "MIDI input data must be an object")]
        [InlineData("SendInput_MidiNoteOff_WithInvalidNoteData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":5,\"data\":{\"noteNumber\":36}}}", "-32002", "Invalid MIDI note data format")]
        public Task SendInput_InvalidPayloads_ShouldReturnExpectedError(
            string caseName,
            string requestJson,
            string expectedCode,
            string expectedText)
        {
            _ = caseName;
            return AssertSendInputValidationAsync(requestJson, expectedCode, expectedText);
        }

        [Theory]
        [InlineData("SendInput_KeyPress_WithValidKeyCodeZero", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":0}}")]
        [InlineData("SendInput_KeyPress_WithValidKeyCode255", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":255}}")]
        [InlineData("SendInput_MouseClick_WithValidObjectData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":0,\"data\":{\"x\":100,\"y\":200}}}")]
        [InlineData("SendInput_MouseMove_WithValidObjectData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":1,\"data\":{\"x\":150,\"y\":250}}}")]
        [InlineData("SendInput_KeyPress_WithValidStringData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":\"Enter\"}}")]
        [InlineData("SendInput_KeyRelease_WithValidStringData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":3,\"data\":\"Escape\"}}")]
        [InlineData("SendInput_MidiNoteOn_WithValidObjectData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":4,\"data\":{\"noteNumber\":36,\"velocity\":100}}}")]
        [InlineData("SendInput_MidiNoteOff_WithValidObjectData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":5,\"data\":{\"noteNumber\":36,\"velocity\":0}}}")]
        [InlineData("SendInput_KeyPress_WithValidObjectData", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendInput\",\"params\":{\"type\":2,\"data\":{\"key\":\"Down\",\"holdDurationMs\":50,\"clientId\":\"default\"}}}")]
        public Task SendInput_ValidPayloads_ShouldSucceed(string caseName, string requestJson)
        {
            _ = caseName;
            return AssertSendInputSuccessAsync(requestJson);
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
