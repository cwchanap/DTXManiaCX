using DTXMania.Game.Lib;
using Moq;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DTXMania.Test.GameApi
{
    [Trait("Category", "Integration")]
    public class GameApiServerIntegrationTests
    {
        private static HttpClient CreateClient(GameApiServer server, string? apiKey = null)
        {
            var client = new HttpClient(new SocketsHttpHandler { UseCookies = false })
            {
                BaseAddress = new Uri(server.GetServerUrl())
            };

            if (!string.IsNullOrEmpty(apiKey))
            {
                client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            }

            return client;
        }

        private static StringContent JsonBody(object payload) =>
            new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        private static StringContent RawJsonBody(string payload) =>
            new(payload, Encoding.UTF8, "application/json");

        private static Mock<IGameApi> CreateGameApiMock()
        {
            var gameApi = new Mock<IGameApi>();
            gameApi.SetupGet(api => api.IsRunning).Returns(true);
            return gameApi;
        }

        [Fact]
        public async Task StartAsync_CalledTwice_ShouldRemainRunning()
        {
            var gameApi = CreateGameApiMock();
            await using var server = new GameApiServer(gameApi.Object, port: 0);

            await server.StartAsync();
            await server.StartAsync();

            using var client = CreateClient(server);
            using var response = await client.GetAsync("/health");

            Assert.True(server.IsRunning);
            Assert.True(response.IsSuccessStatusCode);
        }

        [Fact]
        public async Task StartAsync_WithPortAlreadyInUse_ShouldThrowAndLeaveServerStopped()
        {
            await using var firstServer = new GameApiServer(CreateGameApiMock().Object, port: 0);
            await firstServer.StartAsync();
            var port = new Uri(firstServer.GetServerUrl()).Port;
            await using var secondServer = new GameApiServer(CreateGameApiMock().Object, port: port);

            await Assert.ThrowsAnyAsync<Exception>(() => secondServer.StartAsync());

            Assert.False(secondServer.IsRunning);
        }

        [Fact]
        public async Task HealthEndpoint_WithApiKeyConfigured_ShouldBypassAuthentication()
        {
            var gameApi = CreateGameApiMock();
            await using var server = new GameApiServer(gameApi.Object, port: 0, apiKey: "secret");
            await server.StartAsync();
            using var client = CreateClient(server);

            using var response = await client.GetAsync("/health");
            var body = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);

            Assert.True(response.IsSuccessStatusCode);
            Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
            Assert.True(document.RootElement.GetProperty("game_running").GetBoolean());
        }

        [Fact]
        public async Task GameStateEndpoint_WithMissingApiKey_ShouldReturnUnauthorized()
        {
            var gameApi = CreateGameApiMock();
            await using var server = new GameApiServer(gameApi.Object, port: 0, apiKey: "secret");
            await server.StartAsync();
            using var client = CreateClient(server);

            using var response = await client.GetAsync("/game/state");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains("Unauthorized", body);
            gameApi.Verify(api => api.GetGameStateAsync(), Times.Never);
        }

        [Fact]
        public async Task GameStateEndpoint_WithCorrectApiKey_ShouldReturnGameState()
        {
            var gameApi = CreateGameApiMock();
            gameApi.Setup(api => api.GetGameStateAsync()).ReturnsAsync(new GameState
            {
                CurrentStage = "Title",
                Score = 42
            });

            await using var server = new GameApiServer(gameApi.Object, port: 0, apiKey: "secret");
            await server.StartAsync();
            using var client = CreateClient(server, apiKey: "secret");

            using var response = await client.GetAsync("/game/state");
            var body = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);

            Assert.True(response.IsSuccessStatusCode);
            Assert.Equal("Title", document.RootElement.GetProperty("currentStage").GetString());
            Assert.Equal(42, document.RootElement.GetProperty("score").GetInt32());
        }

        [Fact]
        public async Task GameStateEndpoint_WhenGameApiThrows_ShouldReturnInternalServerError()
        {
            var gameApi = CreateGameApiMock();
            gameApi.Setup(api => api.GetGameStateAsync()).ThrowsAsync(new InvalidOperationException("boom"));

            await using var server = new GameApiServer(gameApi.Object, port: 0);
            await server.StartAsync();
            using var client = CreateClient(server);

            using var response = await client.GetAsync("/game/state");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Contains("Internal server error", body);
        }

        [Fact]
        public async Task GameWindowEndpoint_ShouldReturnWindowInfo()
        {
            var gameApi = CreateGameApiMock();
            gameApi.Setup(api => api.GetWindowInfoAsync()).ReturnsAsync(new GameWindowInfo
            {
                Width = 1280,
                Height = 720,
                Title = "DTXManiaCX"
            });

            await using var server = new GameApiServer(gameApi.Object, port: 0);
            await server.StartAsync();
            using var client = CreateClient(server);

            using var response = await client.GetAsync("/game/window");
            var body = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);

            Assert.True(response.IsSuccessStatusCode);
            Assert.Equal(1280, document.RootElement.GetProperty("width").GetInt32());
            Assert.Equal(720, document.RootElement.GetProperty("height").GetInt32());
            Assert.Equal("DTXManiaCX", document.RootElement.GetProperty("title").GetString());
        }

        [Fact]
        public async Task GameWindowEndpoint_WithMissingApiKey_ShouldReturnUnauthorized()
        {
            var gameApi = CreateGameApiMock();
            await using var server = new GameApiServer(gameApi.Object, port: 0, apiKey: "secret");
            await server.StartAsync();
            using var client = CreateClient(server);

            using var response = await client.GetAsync("/game/window");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains("Unauthorized", body);
            gameApi.Verify(api => api.GetWindowInfoAsync(), Times.Never);
        }

        [Fact]
        public async Task GameWindowEndpoint_WhenGameApiThrows_ShouldReturnInternalServerError()
        {
            var gameApi = CreateGameApiMock();
            gameApi.Setup(api => api.GetWindowInfoAsync()).ThrowsAsync(new InvalidOperationException("boom"));

            await using var server = new GameApiServer(gameApi.Object, port: 0);
            await server.StartAsync();
            using var client = CreateClient(server);

            using var response = await client.GetAsync("/game/window");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Contains("Internal server error", body);
        }

        [Fact]
        public async Task InputEndpoint_WithValidPayload_ShouldReturnSuccessAndForwardInput()
        {
            var gameApi = CreateGameApiMock();
            gameApi.Setup(api => api.SendInputAsync(It.IsAny<GameInput>())).ReturnsAsync(true);

            await using var server = new GameApiServer(gameApi.Object, port: 0);
            await server.StartAsync();
            using var client = CreateClient(server);

            using var response = await client.PostAsync("/game/input", JsonBody(new
            {
                type = InputType.KeyPress,
                data = "Enter"
            }));
            var body = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);

            Assert.True(response.IsSuccessStatusCode);
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            gameApi.Verify(api => api.SendInputAsync(It.Is<GameInput>(input =>
                input.Type == InputType.KeyPress &&
                input.Data.HasValue &&
                input.Data.Value.ValueKind == JsonValueKind.String &&
                input.Data.Value.GetString() == "Enter")), Times.Once);
        }

        [Fact]
        public async Task InputEndpoint_WithJsonNull_ShouldReturnBadRequest()
        {
            var gameApi = CreateGameApiMock();
            await using var server = new GameApiServer(gameApi.Object, port: 0);
            await server.StartAsync();
            using var client = CreateClient(server);

            using var response = await client.PostAsync("/game/input", RawJsonBody("null"));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Input data is null", body);
            gameApi.Verify(api => api.SendInputAsync(It.IsAny<GameInput>()), Times.Never);
        }

        [Fact]
        public async Task InputEndpoint_WithInvalidJson_ShouldReturnBadRequest()
        {
            var gameApi = CreateGameApiMock();
            await using var server = new GameApiServer(gameApi.Object, port: 0);
            await server.StartAsync();
            using var client = CreateClient(server);

            using var response = await client.PostAsync("/game/input", RawJsonBody("{"));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Invalid JSON format", body);
        }

        [Fact]
        public async Task InputEndpoint_WithInvalidInput_ShouldReturnBadRequest()
        {
            var gameApi = CreateGameApiMock();
            await using var server = new GameApiServer(gameApi.Object, port: 0);
            await server.StartAsync();
            using var client = CreateClient(server);

            using var response = await client.PostAsync("/game/input", JsonBody(new
            {
                type = InputType.MouseClick
            }));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Mouse input requires position data", body);
            gameApi.Verify(api => api.SendInputAsync(It.IsAny<GameInput>()), Times.Never);
        }

        [Fact]
        public async Task InputEndpoint_WithOversizedPayload_ShouldReturnPayloadTooLarge()
        {
            var gameApi = CreateGameApiMock();
            await using var server = new GameApiServer(gameApi.Object, port: 0);
            await server.StartAsync();
            using var client = CreateClient(server);

            var oversizedPayload = JsonSerializer.Serialize(new
            {
                type = InputType.KeyPress,
                data = new string('A', 2000)
            });

            using var response = await client.PostAsync("/game/input", RawJsonBody(oversizedPayload));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
            Assert.Contains("Request payload too large", body);
        }

        [Fact]
        public async Task InputEndpoint_WhenGameApiThrows_ShouldReturnInternalServerError()
        {
            var gameApi = CreateGameApiMock();
            gameApi.Setup(api => api.SendInputAsync(It.IsAny<GameInput>())).ThrowsAsync(new InvalidOperationException("boom"));

            await using var server = new GameApiServer(gameApi.Object, port: 0);
            await server.StartAsync();
            using var client = CreateClient(server);

            using var response = await client.PostAsync("/game/input", JsonBody(new
            {
                type = InputType.KeyPress,
                data = "Escape"
            }));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Contains("Internal server error", body);
        }

        [Fact]
        public async Task StopAsync_CalledTwiceAfterStart_ShouldLeaveServerStopped()
        {
            var gameApi = CreateGameApiMock();
            await using var server = new GameApiServer(gameApi.Object, port: 0);

            await server.StartAsync();
            await server.StopAsync();
            await server.StopAsync();

            Assert.False(server.IsRunning);
        }
    }
}
