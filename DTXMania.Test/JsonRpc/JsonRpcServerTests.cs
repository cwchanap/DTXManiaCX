using DTXMania.Game.Lib;
using DTXMania.Game.Lib.JsonRpc;
using Moq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace DTXMania.Test.JsonRpc
{
    public class JsonRpcServerTests : IAsyncDisposable
    {
        private readonly Mock<IGameApi> _mockGameApi;
        private JsonRpcServer? _server;

        public JsonRpcServerTests()
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

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullGameApi_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() => new JsonRpcServer(null!));
            Assert.Equal("gameApi", ex.ParamName);
        }

        [Fact]
        public void Constructor_WithValidGameApi_ShouldCreateInstance()
        {
            // Act
            _server = new JsonRpcServer(_mockGameApi.Object);

            // Assert
            Assert.NotNull(_server);
            Assert.False(_server.IsRunning);
        }

        [Fact]
        public void Constructor_WithCustomPort_ShouldUseSpecifiedPort()
        {
            // Act
            _server = new JsonRpcServer(_mockGameApi.Object, port: 9090);

            // Assert
            var uri = new Uri(_server.GetServerUrl());
            Assert.Equal(9090, uri.Port);
        }

        [Fact]
        public void Constructor_WithDefaultPort_ShouldUse8080()
        {
            // Act
            _server = new JsonRpcServer(_mockGameApi.Object);

            // Assert
            var uri = new Uri(_server.GetServerUrl());
            Assert.Equal(8080, uri.Port);
        }

        #endregion

        #region GetServerUrl Tests

        [Fact]
        public void GetServerUrl_ShouldReturnCorrectFormat()
        {
            // Arrange
            _server = new JsonRpcServer(_mockGameApi.Object, port: 8080);

            // Act
            var url = _server.GetServerUrl();

            // Assert
            Assert.Equal("http://localhost:8080/jsonrpc", url);
        }

        [Theory]
        [InlineData(8080)]
        [InlineData(9000)]
        [InlineData(3000)]
        [InlineData(5000)]
        public void GetServerUrl_WithVariousPorts_ShouldReturnCorrectUrl(int port)
        {
            // Arrange
            _server = new JsonRpcServer(_mockGameApi.Object, port: port);

            // Act
            var url = _server.GetServerUrl();

            // Assert
            Assert.Equal($"http://localhost:{port}/jsonrpc", url);
        }

        #endregion

        #region IsRunning Tests

        [Fact]
        public void IsRunning_BeforeStart_ShouldReturnFalse()
        {
            // Arrange
            _server = new JsonRpcServer(_mockGameApi.Object);

            // Act & Assert
            Assert.False(_server.IsRunning);
        }

        #endregion

        #region StartAsync/StopAsync Tests

        [Fact(Skip = "Integration test - requires server infrastructure")]
        public async Task StartAsync_ShouldSetIsRunningToTrue()
        {
            // Arrange
            _server = new JsonRpcServer(_mockGameApi.Object, port: GetRandomPort());

            // Act
            await _server.StartAsync();

            // Assert
            Assert.True(_server.IsRunning);
        }

        [Fact(Skip = "Integration test - requires server infrastructure")]
        public async Task StartAsync_WhenAlreadyRunning_ShouldNotThrow()
        {
            // Arrange
            _server = new JsonRpcServer(_mockGameApi.Object, port: GetRandomPort());
            await _server.StartAsync();

            // Act & Assert - should not throw
            await _server.StartAsync();
            Assert.True(_server.IsRunning);
        }

        [Fact(Skip = "Integration test - requires server infrastructure")]
        public async Task StopAsync_WhenRunning_ShouldSetIsRunningToFalse()
        {
            // Arrange
            _server = new JsonRpcServer(_mockGameApi.Object, port: GetRandomPort());
            await _server.StartAsync();

            // Act
            await _server.StopAsync();

            // Assert
            Assert.False(_server.IsRunning);
        }

        [Fact]
        public async Task StopAsync_WhenNotRunning_ShouldNotThrow()
        {
            // Arrange
            _server = new JsonRpcServer(_mockGameApi.Object);

            // Act & Assert - should not throw
            await _server.StopAsync();
            Assert.False(_server.IsRunning);
        }

        [Fact(Skip = "Integration test - requires server infrastructure")]
        public async Task StartAsync_WithCancellationToken_ShouldRespectCancellation()
        {
            // Arrange
            _server = new JsonRpcServer(_mockGameApi.Object, port: GetRandomPort());
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => _server.StartAsync(cts.Token));
        }

        #endregion

        #region Dispose Tests

        [Fact(Skip = "Integration test - requires server infrastructure")]
        public async Task DisposeAsync_WhenRunning_ShouldStopServer()
        {
            // Arrange
            _server = new JsonRpcServer(_mockGameApi.Object, port: GetRandomPort());
            await _server.StartAsync();

            // Act
            await _server.DisposeAsync();

            // Assert
            Assert.False(_server.IsRunning);
            _server = null; // Prevent double dispose in DisposeAsync
        }

        [Fact]
        public async Task DisposeAsync_WhenNotRunning_ShouldNotThrow()
        {
            // Arrange
            _server = new JsonRpcServer(_mockGameApi.Object);

            // Act & Assert - should not throw
            await _server.DisposeAsync();
            _server = null; // Prevent double dispose
        }

        [Fact]
        public void Dispose_ShouldStopServer()
        {
            // Arrange
            var server = new JsonRpcServer(_mockGameApi.Object);

            // Act & Assert - should not throw
            server.Dispose();
        }

        #endregion

        #region HTTP Request Tests (Integration - require server infrastructure)

        [Fact(Skip = "Integration test - requires server infrastructure")]
        public async Task HealthEndpoint_ShouldReturnOkStatus()
        {
            // Arrange
            var port = GetRandomPort();
            _server = new JsonRpcServer(_mockGameApi.Object, port: port);
            await _server.StartAsync();

            using var client = new HttpClient();

            // Act
            var response = await client.GetAsync($"http://localhost:{port}/health");
            var content = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.True(response.IsSuccessStatusCode);
            Assert.Contains("ok", content);
        }

        [Fact(Skip = "Integration test - requires server infrastructure")]
        public async Task JsonRpcEndpoint_WithPingMethod_ShouldReturnPong()
        {
            // Arrange
            var port = GetRandomPort();
            _server = new JsonRpcServer(_mockGameApi.Object, port: port);
            await _server.StartAsync();

            using var client = new HttpClient();
            var request = new JsonRpcRequest
            {
                Id = 1,
                Method = "ping"
            };
            var json = JsonSerializer.Serialize(request);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Act
            var response = await client.PostAsync($"http://localhost:{port}/jsonrpc", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.True(response.IsSuccessStatusCode);
            Assert.Contains("pong", responseContent);
        }

        [Fact(Skip = "Integration test - requires server infrastructure")]
        public async Task JsonRpcEndpoint_WithInvalidJson_ShouldReturnParseError()
        {
            // Arrange
            var port = GetRandomPort();
            _server = new JsonRpcServer(_mockGameApi.Object, port: port);
            await _server.StartAsync();

            using var client = new HttpClient();
            using var content = new StringContent("invalid json{", Encoding.UTF8, "application/json");

            // Act
            var response = await client.PostAsync($"http://localhost:{port}/jsonrpc", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Contains("-32700", responseContent); // Parse error code
        }

        [Fact(Skip = "Integration test - requires server infrastructure")]
        public async Task JsonRpcEndpoint_WithInvalidMethod_ShouldReturnMethodNotFound()
        {
            // Arrange
            var port = GetRandomPort();
            _server = new JsonRpcServer(_mockGameApi.Object, port: port);
            await _server.StartAsync();

            using var client = new HttpClient();
            var request = new JsonRpcRequest
            {
                Id = 1,
                Method = "nonExistentMethod"
            };
            var json = JsonSerializer.Serialize(request);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Act
            var response = await client.PostAsync($"http://localhost:{port}/jsonrpc", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Contains("-32601", responseContent); // Method not found code
        }

        [Fact(Skip = "Integration test - requires server infrastructure")]
        public async Task JsonRpcEndpoint_GetGameState_ShouldReturnGameState()
        {
            // Arrange
            var port = GetRandomPort();
            var gameState = new GameState
            {
                Score = 100,
                Level = 5,
                CurrentStage = "TestStage"
            };
            _mockGameApi.Setup(api => api.GetGameStateAsync()).ReturnsAsync(gameState);

            _server = new JsonRpcServer(_mockGameApi.Object, port: port);
            await _server.StartAsync();

            using var client = new HttpClient();
            var request = new JsonRpcRequest
            {
                Id = 1,
                Method = "getGameState"
            };
            var json = JsonSerializer.Serialize(request);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Act
            var response = await client.PostAsync($"http://localhost:{port}/jsonrpc", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.True(response.IsSuccessStatusCode);
            Assert.Contains("100", responseContent); // Score
        }

        [Fact(Skip = "Integration test - requires server infrastructure")]
        public async Task JsonRpcEndpoint_GetWindowInfo_ShouldReturnWindowInfo()
        {
            // Arrange
            var port = GetRandomPort();
            var windowInfo = new GameWindowInfo
            {
                Width = 1920,
                Height = 1080,
                Title = "TestWindow"
            };
            _mockGameApi.Setup(api => api.GetWindowInfoAsync()).ReturnsAsync(windowInfo);

            _server = new JsonRpcServer(_mockGameApi.Object, port: port);
            await _server.StartAsync();

            using var client = new HttpClient();
            var request = new JsonRpcRequest
            {
                Id = 1,
                Method = "getWindowInfo"
            };
            var json = JsonSerializer.Serialize(request);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Act
            var response = await client.PostAsync($"http://localhost:{port}/jsonrpc", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.True(response.IsSuccessStatusCode);
            Assert.Contains("1920", responseContent); // Width
        }

        [Fact(Skip = "Integration test - requires server infrastructure")]
        public async Task JsonRpcEndpoint_WhenGameNotRunning_ShouldReturnError()
        {
            // Arrange
            var port = GetRandomPort();
            _mockGameApi.Setup(api => api.IsRunning).Returns(false);

            _server = new JsonRpcServer(_mockGameApi.Object, port: port);
            await _server.StartAsync();

            using var client = new HttpClient();
            var request = new JsonRpcRequest
            {
                Id = 1,
                Method = "getGameState"
            };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Act
            var response = await client.PostAsync($"http://localhost:{port}/jsonrpc", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Contains("-32001", responseContent); // Game not running error code
        }

        [Fact(Skip = "Integration test - requires server infrastructure")]
        public async Task JsonRpcEndpoint_WithApiKey_ShouldRequireValidKey()
        {
            // Arrange
            var port = GetRandomPort();
            _server = new JsonRpcServer(_mockGameApi.Object, port: port, apiKey: "secret-key");
            await _server.StartAsync();

            using var client = new HttpClient();
            var request = new JsonRpcRequest
            {
                Id = 1,
                Method = "ping"
            };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Act - without API key
            var response = await client.PostAsync($"http://localhost:{port}/jsonrpc", content);

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact(Skip = "Integration test - requires server infrastructure")]
        public async Task JsonRpcEndpoint_WithApiKey_ValidKeyShouldWork()
        {
            // Arrange
            var port = GetRandomPort();
            _server = new JsonRpcServer(_mockGameApi.Object, port: port, apiKey: "secret-key");
            await _server.StartAsync();

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", "secret-key");
            var request = new JsonRpcRequest
            {
                Id = 1,
                Method = "ping"
            };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Act
            var response = await client.PostAsync($"http://localhost:{port}/jsonrpc", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.True(response.IsSuccessStatusCode);
            Assert.Contains("pong", responseContent);
        }

        [Fact(Skip = "Integration test - requires server infrastructure")]
        public async Task JsonRpcEndpoint_SendInput_WithValidInput_ShouldCallGameApi()
        {
            // Arrange
            var port = GetRandomPort();
            _mockGameApi.Setup(api => api.SendInputAsync(It.IsAny<GameInput>())).ReturnsAsync(true);

            _server = new JsonRpcServer(_mockGameApi.Object, port: port);
            await _server.StartAsync();

            using var client = new HttpClient();
            var inputParams = new { type = 2, data = "Enter" }; // KeyPress = 2
            var request = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "sendInput",
                @params = inputParams
            };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Act
            var response = await client.PostAsync($"http://localhost:{port}/jsonrpc", content);

            // Assert
            Assert.True(response.IsSuccessStatusCode);
            _mockGameApi.Verify(api => api.SendInputAsync(It.IsAny<GameInput>()), Times.Once);
        }

        [Fact(Skip = "Integration test - requires server infrastructure")]
        public async Task JsonRpcEndpoint_SendInput_WithNullParams_ShouldReturnInvalidParamsError()
        {
            // Arrange
            var port = GetRandomPort();
            _server = new JsonRpcServer(_mockGameApi.Object, port: port);
            await _server.StartAsync();

            using var client = new HttpClient();
            var request = new JsonRpcRequest
            {
                Id = 1,
                Method = "sendInput",
                Params = null
            };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Act
            var response = await client.PostAsync($"http://localhost:{port}/jsonrpc", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Contains("-32602", responseContent); // Invalid params error code
        }

        #endregion

        #region Helper Methods

        private static int _portCounter = 10000;
        private static readonly object _portLock = new object();

        private static int GetRandomPort()
        {
            lock (_portLock)
            {
                return _portCounter++;
            }
        }

        #endregion
    }

    #region JSON-RPC Message Model Tests

    public class JsonRpcMessageTests
    {
        [Fact]
        public void JsonRpcRequest_DefaultJsonRpc_ShouldBe2_0()
        {
            // Arrange & Act
            var request = new JsonRpcRequest();

            // Assert
            Assert.Equal("2.0", request.JsonRpc);
        }

        [Fact]
        public void JsonRpcRequest_IsNotification_WithNullId_ShouldReturnTrue()
        {
            // Arrange
            var request = new JsonRpcRequest { Id = null };

            // Assert
            Assert.True(request.IsNotification);
        }

        [Fact]
        public void JsonRpcRequest_IsNotification_WithId_ShouldReturnFalse()
        {
            // Arrange
            var request = new JsonRpcRequest { Id = 1 };

            // Assert
            Assert.False(request.IsNotification);
        }

        [Fact]
        public void JsonRpcResponse_IsError_WithError_ShouldReturnTrue()
        {
            // Arrange
            var response = new JsonRpcResponse
            {
                Error = new JsonRpcError { Code = -32600, Message = "Invalid Request" }
            };

            // Assert
            Assert.True(response.IsError);
        }

        [Fact]
        public void JsonRpcResponse_IsError_WithoutError_ShouldReturnFalse()
        {
            // Arrange
            var response = new JsonRpcResponse { Result = new { data = "test" } };

            // Assert
            Assert.False(response.IsError);
        }

        [Fact]
        public void JsonRpcErrorCodes_ShouldHaveCorrectValues()
        {
            // Assert standard JSON-RPC error codes
            Assert.Equal(-32700, JsonRpcErrorCodes.ParseError);
            Assert.Equal(-32600, JsonRpcErrorCodes.InvalidRequest);
            Assert.Equal(-32601, JsonRpcErrorCodes.MethodNotFound);
            Assert.Equal(-32602, JsonRpcErrorCodes.InvalidParams);
            Assert.Equal(-32603, JsonRpcErrorCodes.InternalError);

            // Assert application-specific error codes
            Assert.Equal(-32001, JsonRpcErrorCodes.GameNotRunning);
            Assert.Equal(-32002, JsonRpcErrorCodes.InvalidInput);
            Assert.Equal(-32003, JsonRpcErrorCodes.WindowNotFound);
        }
    }

    #endregion
}
