using DTXMania.Game.Lib;
using DTXMania.Test.Helpers;
using Moq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace DTXMania.Test.GameApi
{
    [Trait("Category", "Unit")]
    public class GameApiImplementationInputRoutingTests
    {
        private static (GameApiImplementation Api, MockInputManagerCompat InputManager) CreateSut()
        {
            var inputManager = new MockInputManagerCompat();
            var gameContext = new Mock<IGameContext>();
            gameContext.SetupGet(g => g.InputManager).Returns(inputManager);

            return (new GameApiImplementation(gameContext.Object), inputManager);
        }

        [Theory]
        [InlineData("\"Down\"", InputType.KeyPress)]
        [InlineData("\"Key.Escape\"", InputType.KeyRelease)]
        [InlineData("40", InputType.KeyPress)]
        public async Task SendInputAsync_WithParsableKeyPayload_ReturnsTrue(string json, InputType inputType)
        {
            var (api, inputManager) = CreateSut();
            using var document = JsonDocument.Parse(json);

            var result = await api.SendInputAsync(new GameInput
            {
                Type = inputType,
                Data = document.RootElement.Clone()
            });

            Assert.True(result);
        }

        [Theory]
        [InlineData("{\"key\":\"Down\"}", InputType.KeyPress)]
        [InlineData("{\"key\":\"Key.Escape\"}", InputType.KeyRelease)]
        public async Task SendInputAsync_WithObjectKeyPayload_ReturnsTrue(string json, InputType inputType)
        {
            var (api, inputManager) = CreateSut();
            using var document = JsonDocument.Parse(json);

            var result = await api.SendInputAsync(new GameInput
            {
                Type = inputType,
                Data = document.RootElement.Clone()
            });

            Assert.True(result);
        }

        [Theory]
        [InlineData("\"\"")]
        [InlineData("\"   \"")]
        [InlineData("null")]
        public async Task SendInputAsync_WithNullOrWhitespaceStringPayload_ReturnsFalse(string json)
        {
            var (api, inputManager) = CreateSut();
            using var document = JsonDocument.Parse(json);

            var result = await api.SendInputAsync(new GameInput
            {
                Type = InputType.KeyPress,
                Data = document.RootElement.Clone()
            });

            Assert.False(result);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"holdDurationMs\":50}")]
        public async Task SendInputAsync_WithObjectPayloadMissingKeyProperty_ReturnsFalse(string json)
        {
            var (api, inputManager) = CreateSut();
            using var document = JsonDocument.Parse(json);

            var result = await api.SendInputAsync(new GameInput
            {
                Type = InputType.KeyPress,
                Data = document.RootElement.Clone()
            });

            Assert.False(result);
        }

        [Fact]
        public async Task SendInputAsync_WithMissingPayload_ReturnsFalse()
        {
            var (api, inputManager) = CreateSut();

            var result = await api.SendInputAsync(new GameInput
            {
                Type = InputType.KeyPress
            });

            Assert.False(result);
        }

        [Theory]
        [InlineData(InputType.MouseClick)]
        [InlineData(InputType.MouseMove)]
        public async Task SendInputAsync_WithUnsupportedMouseInputType_ReturnsFalse(InputType inputType)
        {
            var (api, inputManager) = CreateSut();

            var result = await api.SendInputAsync(new GameInput
            {
                Type = inputType,
                Data = JsonSerializer.SerializeToElement(new { x = 100, y = 200 })
            });

            Assert.False(result);
        }
    }
}
