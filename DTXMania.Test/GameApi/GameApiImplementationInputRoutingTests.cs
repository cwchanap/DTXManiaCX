using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Input.Midi;
using DTXMania.Test.Helpers;
using DTXMania.Test.TestData;
using Moq;
using System.Collections.Concurrent;
using System.Linq;
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

        [Fact]
        public async Task SendInputAsync_WithMidiNoteOnPayload_RoutesThroughSimulatedMidiSource()
        {
            var configManager = new ConfigManager();
            configManager.SetMidiVelocityThreshold(36, 20);
            configManager.Config.KeyBindings["MIDI.36"] = 5;
            var midiBackend = new SimulatedMidiDeviceBackend();
            using var inputManager = new InputManagerCompat(configManager, midiBackend);
            var gameContext = new Mock<IGameContext>();
            gameContext.SetupGet(g => g.InputManager).Returns(inputManager);
            var api = new GameApiImplementation(gameContext.Object);
            LaneHitEventArgs? capturedLaneHit = null;
            inputManager.ModularInputManager.OnLaneHit += (_, args) => capturedLaneHit = args;

            var result = await api.SendInputAsync(new GameInput
            {
                Type = InputType.MidiNoteOn,
                Data = JsonSerializer.SerializeToElement(new { noteNumber = 36, velocity = 100 })
            });

            Assert.True(result);
            inputManager.ModularInputManager.Update();

            var pressed = inputManager.ModularInputManager.ConsumePressedButtons();
            var button = Assert.Single(pressed.Where(state => state.Id == "MIDI.36"));
            Assert.True(button.IsPressed);
            Assert.Equal(100f / 127f, button.Velocity, precision: 4);
            Assert.NotNull(capturedLaneHit);
            Assert.Equal(5, capturedLaneHit!.Lane);
            Assert.Equal("MIDI.36", capturedLaneHit.Button.Id);
        }

        [Fact]
        public async Task SendInputAsync_WithMidiNoteOnPayload_NonInjectorBackend_ReturnsFalseAndDoesNotLogSuccess()
        {
            // Documents production behavior: the DryWetMidi backend does not implement
            // IMidiNoteInjector, so MIDI injection via the API is a no-op that returns false.
            // A warning is logged (verified here by capturing the ILogger).
            var configManager = new ConfigManager();
            var midiBackend = new Mock<IMidiDeviceBackend>();
            midiBackend.Setup(b => b.GetInputDevices())
                .Returns(System.Array.Empty<IMidiInputDevice>());
            using var inputManager = new InputManagerCompat(configManager, midiBackend.Object);

            // Direct manager-level assertion: no injector configured.
            Assert.False(inputManager.ModularInputManager.InjectMidiNote(36, 100, true));

            var logger = new Mock<Microsoft.Extensions.Logging.ILogger<GameApiImplementation>>();
            var gameContext = new Mock<IGameContext>();
            gameContext.SetupGet(g => g.InputManager).Returns(inputManager);
            var api = new GameApiImplementation(gameContext.Object, logger.Object);

            var result = await api.SendInputAsync(new GameInput
            {
                Type = InputType.MidiNoteOn,
                Data = JsonSerializer.SerializeToElement(new { noteNumber = 36, velocity = 100 })
            });

            Assert.False(result);
            logger.Verify(
                l => l.Log(
                    Microsoft.Extensions.Logging.LogLevel.Warning,
                    It.IsAny<Microsoft.Extensions.Logging.EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("MIDI note injection rejected")),
                    It.IsAny<System.Exception?>(),
                    It.IsAny<Func<It.IsAnyType, System.Exception?, string>>()),
                Times.Once);
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

        [Fact]
        public async Task SendInputAsync_WithMidiNoteOffPayload_RoutesThroughSimulatedMidiSource()
        {
            var configManager = new ConfigManager();
            configManager.Config.KeyBindings["MIDI.36"] = 5;
            var midiBackend = new SimulatedMidiDeviceBackend();
            using var inputManager = new InputManagerCompat(configManager, midiBackend);
            var gameContext = new Mock<IGameContext>();
            gameContext.SetupGet(g => g.InputManager).Returns(inputManager);
            var api = new GameApiImplementation(gameContext.Object);

            // First press the note so a subsequent release has state to clear.
            var pressResult = await api.SendInputAsync(new GameInput
            {
                Type = InputType.MidiNoteOn,
                Data = JsonSerializer.SerializeToElement(new { noteNumber = 36, velocity = 100 })
            });
            Assert.True(pressResult);
            inputManager.ModularInputManager.Update();

            // Now send the note-off — should also return true (simulated backend accepts it).
            var result = await api.SendInputAsync(new GameInput
            {
                Type = InputType.MidiNoteOff,
                Data = JsonSerializer.SerializeToElement(new { noteNumber = 36, velocity = 0 })
            });

            Assert.True(result);
        }

        [Theory]
        [InlineData("{\"noteNumber\":128,\"velocity\":100}", "noteNumber out of range")]
        [InlineData("{\"noteNumber\":-1,\"velocity\":100}", "noteNumber negative")]
        [InlineData("{\"noteNumber\":36,\"velocity\":128}", "velocity out of range")]
        [InlineData("{\"noteNumber\":36,\"velocity\":-1}", "velocity negative")]
        [InlineData("{\"velocity\":100}", "missing noteNumber")]
        [InlineData("{\"noteNumber\":36}", "missing velocity")]
        [InlineData("{}", "empty object")]
        public async Task SendInputAsync_WithInvalidMidiData_ReturnsFalse(string json, string scenario)
        {
            var (api, _) = CreateSut();

            var data = JsonDocument.Parse(json).RootElement.Clone();

            var result = await api.SendInputAsync(new GameInput
            {
                Type = InputType.MidiNoteOn,
                Data = data
            });

            Assert.False(result);
        }

        [Fact]
        public async Task SendInputAsync_WithMidiNoteDataNotObject_ReturnsFalse()
        {
            var (api, _) = CreateSut();

            var result = await api.SendInputAsync(new GameInput
            {
                Type = InputType.MidiNoteOn,
                Data = JsonSerializer.SerializeToElement("not-an-object")
            });

            Assert.False(result);
        }

        [Fact]
        public async Task SendInputAsync_WithMidiNoteMissingData_ReturnsFalse()
        {
            var (api, _) = CreateSut();

            var result = await api.SendInputAsync(new GameInput
            {
                Type = InputType.MidiNoteOn
            });

            Assert.False(result);
        }

        [Fact]
        public async Task SendInputAsync_WithNullInput_ReturnsFalse()
        {
            var (api, _) = CreateSut();

            var result = await api.SendInputAsync(null!);

            Assert.False(result);
        }

        [Fact]
        public async Task SendInputAsync_WithUnknownInputType_ReturnsFalse()
        {
            var (api, _) = CreateSut();

            var result = await api.SendInputAsync(new GameInput
            {
                Type = (InputType)999,
                Data = JsonSerializer.SerializeToElement(new { })
            });

            Assert.False(result);
        }

        [Fact]
        public async Task SendInputAsync_WhenModularInputManagerIsNull_ReturnsFalse()
        {
            // Simulate a game context where InputManager is null.
            var gameContext = new Mock<IGameContext>();
            gameContext.SetupGet(g => g.InputManager).Returns((InputManagerCompat?)null);
            var api = new GameApiImplementation(gameContext.Object);

            var result = await api.SendInputAsync(new GameInput
            {
                Type = InputType.KeyPress,
                Data = JsonSerializer.SerializeToElement("Down")
            });

            Assert.False(result);
        }
    }
}
