using DTXMania.Game.Lib;
using DTXMania.Game.Lib.JsonRpc;
using Moq;
using System;
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

        [Fact]
        public async Task StopAsync_WhenNotRunning_ShouldNotThrow()
        {
            // Arrange
            _server = new JsonRpcServer(_mockGameApi.Object);

            // Act & Assert - should not throw
            await _server.StopAsync();
            Assert.False(_server.IsRunning);
        }

        #endregion

        #region Dispose Tests

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
