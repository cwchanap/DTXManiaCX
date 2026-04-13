using DTXMania.Game.Lib;
using DTXMania.Game.Lib.JsonRpc;
using DTXMania.Test.TestData;
using Moq;
using System;
using System.Net;
using System.Net.Sockets;
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

        [Fact]
        public void Constructor_WithPortZero_ShouldThrowArgumentOutOfRangeException()
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new JsonRpcServer(_mockGameApi.Object, port: 0));

            Assert.Equal("port", ex.ParamName);
            Assert.Equal(0, ex.ActualValue);
        }

        [Fact]
        public void Constructor_WithPortAboveRange_ShouldThrowArgumentOutOfRangeException()
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new JsonRpcServer(_mockGameApi.Object, port: 65536));

            Assert.Equal("port", ex.ParamName);
            Assert.Equal(65536, ex.ActualValue);
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

        [Fact]
        public async Task StartAsync_WhenPortAlreadyInUse_ShouldThrowAndCleanupState()
        {
            var port = GetAvailablePort();
            await using var firstServer = new JsonRpcServer(_mockGameApi.Object, port);
            await firstServer.StartAsync();
            var secondServer = new JsonRpcServer(_mockGameApi.Object, port);

            try
            {
                await Assert.ThrowsAnyAsync<Exception>(() => secondServer.StartAsync());
                Assert.False(secondServer.IsRunning);
                Assert.Null(ReflectionHelpers.GetPrivateField<object>(secondServer, "_host"));
                Assert.Null(ReflectionHelpers.GetPrivateField<object>(secondServer, "_cancellationTokenSource"));
            }
            finally
            {
                await secondServer.DisposeAsync();
            }
        }

        [Fact]
        public async Task StopAsync_WhenRunning_ShouldStopAndClearHostedResources()
        {
            var port = GetAvailablePort();
            _server = new JsonRpcServer(_mockGameApi.Object, port);
            await _server.StartAsync();

            await _server.StopAsync();

            Assert.False(_server.IsRunning);
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(_server, "_host"));
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(_server, "_cancellationTokenSource"));
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

        [Fact]
        public async Task Dispose_WhenRunningServer_ShouldStopAndDisposeResources()
        {
            var port = GetAvailablePort();
            var server = new JsonRpcServer(_mockGameApi.Object, port);
            await server.StartAsync();

            server.Dispose();

            Assert.False(server.IsRunning);
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(server, "_host"));
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(server, "_cancellationTokenSource"));
        }

        [Fact]
        public async Task StopAsync_AfterDisposeAsync_ShouldThrowObjectDisposedException()
        {
            var server = new JsonRpcServer(_mockGameApi.Object);
            await server.DisposeAsync();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => server.StopAsync());
        }

        [Fact]
        public void Dispose_CalledTwice_ShouldNotThrow()
        {
            var server = new JsonRpcServer(_mockGameApi.Object);

            var exception = Record.Exception(() =>
            {
                server.Dispose();
                server.Dispose();
            });

            Assert.Null(exception);
        }

        #endregion

        private static int GetAvailablePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

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
