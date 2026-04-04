using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Input;
using DTXMania.Test.Helpers;
using DTXMania.Test.TestData;
using Moq;
using System.Collections.Concurrent;
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

        private static ButtonState AssertSingleInjectedButton(MockInputManagerCompat inputManager)
        {
            var queue = ReflectionHelpers.GetPrivateField<ConcurrentQueue<ButtonState>>(
                inputManager.ModularInputManager,
                "_injectedButtonQueue");

            Assert.NotNull(queue);
            Assert.True(queue!.TryDequeue(out var buttonState));
            Assert.NotNull(buttonState);
            Assert.False(queue.TryDequeue(out _));
            return buttonState!;
        }

        [Theory]
        [InlineData("\"Down\"", InputType.KeyPress, "Key.Down", true)]
        [InlineData("\"Key.Escape\"", InputType.KeyRelease, "Key.Escape", false)]
        [InlineData("40", InputType.KeyPress, "Key.Down", true)]
        public async Task SendInputAsync_WithParsableKeyPayload_InjectsExpectedButton(
            string json,
            InputType inputType,
            string expectedButtonId,
            bool expectedPressed)
        {
            var (api, inputManager) = CreateSut();
            using var document = JsonDocument.Parse(json);

            var result = await api.SendInputAsync(new GameInput
            {
                Type = inputType,
                Data = document.RootElement.Clone()
            });

            Assert.True(result);
            var buttonState = AssertSingleInjectedButton(inputManager);
            Assert.Equal(expectedButtonId, buttonState.Id);
            Assert.Equal(expectedPressed, buttonState.IsPressed);
        }

        [Theory]
        [InlineData("{\"key\":\"Down\"}", InputType.KeyPress, "Key.Down", true)]
        [InlineData("{\"key\":\"Key.Escape\"}", InputType.KeyRelease, "Key.Escape", false)]
        public async Task SendInputAsync_WithObjectKeyPayload_InjectsExpectedButton(
            string json,
            InputType inputType,
            string expectedButtonId,
            bool expectedPressed)
        {
            var (api, inputManager) = CreateSut();
            using var document = JsonDocument.Parse(json);

            var result = await api.SendInputAsync(new GameInput
            {
                Type = inputType,
                Data = document.RootElement.Clone()
            });

            Assert.True(result);
            var buttonState = AssertSingleInjectedButton(inputManager);
            Assert.Equal(expectedButtonId, buttonState.Id);
            Assert.Equal(expectedPressed, buttonState.IsPressed);
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
