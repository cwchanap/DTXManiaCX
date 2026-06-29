using System.Text.Json;
using DTXMania.Game.Lib;
using Xunit;

namespace DTXMania.Test.GameApi;

/// <summary>
/// Unit tests for the shared <see cref="GameInputValidator"/> used by both
/// <see cref="GameApiServer"/> (REST) and <see cref="JsonRpc.JsonRpcServer"/> (JSON-RPC).
/// These tests verify the single source of truth so the two endpoints can never drift.
/// </summary>
[Trait("Category", "Unit")]
public class GameInputValidatorTests
{
    private static JsonElement Data(object obj) => JsonSerializer.SerializeToElement(obj);

    private static JsonElement ParseData(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    #region Invalid input type

    [Fact]
    public void Validate_UndefinedInputType_ShouldReturnFalse()
    {
        var input = new GameInput { Type = (InputType)(-1), Data = Data(new { x = 0, y = 0 }) };
        var (isValid, errorMessage) = GameInputValidator.ValidateGameInput(input);

        Assert.False(isValid);
        Assert.Equal("Invalid input type", errorMessage);
    }

    #endregion

    #region Mouse input

    [Fact]
    public void Validate_MouseClick_NullData_ShouldReturnFalse()
    {
        var input = new GameInput { Type = InputType.MouseClick, Data = null };
        var (isValid, _) = GameInputValidator.ValidateGameInput(input);
        Assert.False(isValid);
    }

    [Fact]
    public void Validate_MouseClick_ValidObjectData_ShouldReturnTrue()
    {
        var input = new GameInput { Type = InputType.MouseClick, Data = Data(new { x = 100, y = 200 }) };
        var (isValid, _) = GameInputValidator.ValidateGameInput(input);
        Assert.True(isValid);
    }

    #endregion

    #region Key input — whitespace handling (the drift fix)

    [Theory]
    [InlineData("\"  \"",  true,  "String key data that is whitespace-only must be rejected.")]
    [InlineData("\"Down\"", false, "Non-empty string key data must be accepted.")]
    public void Validate_KeyPress_StringData_WhitespaceHandling(
        string json, bool expectInvalid, string reason)
    {
        var input = new GameInput { Type = InputType.KeyPress, Data = ParseData(json) };
        var (isValid, _) = GameInputValidator.ValidateGameInput(input);

        // This test documents the fix for the previous drift where GameApiServer used
        // IsNullOrEmpty (accepting whitespace) while JsonRpcServer used IsNullOrWhiteSpace
        // (rejecting it). The shared validator now uses IsNullOrWhiteSpace consistently.
        Assert.Equal(!expectInvalid, isValid);
    }

    [Fact]
    public void Validate_KeyPress_ObjectData_WhitespaceKey_ShouldReturnFalse()
    {
        // Object format: {"key":"  ","holdDurationMs":50} — whitespace-only key must be rejected.
        var input = new GameInput
        {
            Type = InputType.KeyPress,
            Data = ParseData("{\"key\":\"  \",\"holdDurationMs\":50}")
        };
        var (isValid, _) = GameInputValidator.ValidateGameInput(input);
        Assert.False(isValid);
    }

    [Fact]
    public void Validate_KeyPress_ObjectData_ValidKey_ShouldReturnTrue()
    {
        var input = new GameInput
        {
            Type = InputType.KeyPress,
            Data = ParseData("{\"key\":\"Down\",\"holdDurationMs\":50,\"clientId\":\"default\"}")
        };
        var (isValid, _) = GameInputValidator.ValidateGameInput(input);
        Assert.True(isValid);
    }

    [Fact]
    public void Validate_KeyPress_NumberKeyCode_InRange_ShouldReturnTrue()
    {
        var input = new GameInput { Type = InputType.KeyPress, Data = ParseData("13") };
        var (isValid, _) = GameInputValidator.ValidateGameInput(input);
        Assert.True(isValid);
    }

    [Fact]
    public void Validate_KeyPress_NumberKeyCode_OutOfRange_ShouldReturnFalse()
    {
        var input = new GameInput { Type = InputType.KeyPress, Data = ParseData("300") };
        var (isValid, _) = GameInputValidator.ValidateGameInput(input);
        Assert.False(isValid);
    }

    #endregion

    #region MIDI input

    [Theory]
    [InlineData(InputType.MidiNoteOn)]
    [InlineData(InputType.MidiNoteOff)]
    public void Validate_Midi_ValidData_ShouldReturnTrue(InputType type)
    {
        var input = new GameInput
        {
            Type = type,
            Data = Data(new { noteNumber = 36, velocity = 100 })
        };
        var (isValid, _) = GameInputValidator.ValidateGameInput(input);
        Assert.True(isValid);
    }

    [Fact]
    public void Validate_Midi_NullData_ShouldReturnFalse()
    {
        var input = new GameInput { Type = InputType.MidiNoteOn, Data = null };
        var (isValid, errorMessage) = GameInputValidator.ValidateGameInput(input);
        Assert.False(isValid);
        Assert.Equal("MIDI input requires note data", errorMessage);
    }

    [Fact]
    public void Validate_Midi_NonObjectData_ShouldReturnFalse()
    {
        var input = new GameInput { Type = InputType.MidiNoteOn, Data = ParseData("\"not-an-object\"") };
        var (isValid, errorMessage) = GameInputValidator.ValidateGameInput(input);
        Assert.False(isValid);
        Assert.Equal("MIDI input data must be an object", errorMessage);
    }

    [Theory]
    [InlineData("{\"noteNumber\":128,\"velocity\":100}")]
    [InlineData("{\"noteNumber\":-1,\"velocity\":100}")]
    [InlineData("{\"noteNumber\":36,\"velocity\":128}")]
    [InlineData("{\"noteNumber\":36,\"velocity\":-1}")]
    [InlineData("{\"velocity\":100}")]
    [InlineData("{\"noteNumber\":36}")]
    [InlineData("{}")]
    [InlineData("{\"noteNumber\":\"36\",\"velocity\":100}")]
    [InlineData("{\"noteNumber\":36,\"velocity\":\"100\"}")]
    public void Validate_Midi_InvalidNoteData_ShouldReturnFalse(string json)
    {
        var input = new GameInput { Type = InputType.MidiNoteOn, Data = ParseData(json) };
        var (isValid, errorMessage) = GameInputValidator.ValidateGameInput(input);
        Assert.False(isValid);
        Assert.Equal("Invalid MIDI note data format", errorMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(127)]
    public void Validate_Midi_BoundaryNoteNumbers_ShouldReturnTrue(int noteNumber)
    {
        var input = new GameInput
        {
            Type = InputType.MidiNoteOn,
            Data = Data(new { noteNumber, velocity = 100 })
        };
        var (isValid, _) = GameInputValidator.ValidateGameInput(input);
        Assert.True(isValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(127)]
    public void Validate_Midi_BoundaryVelocities_ShouldReturnTrue(int velocity)
    {
        var input = new GameInput
        {
            Type = InputType.MidiNoteOn,
            Data = Data(new { noteNumber = 36, velocity })
        };
        var (isValid, _) = GameInputValidator.ValidateGameInput(input);
        Assert.True(isValid);
    }

    #endregion

    #region TryValidateMidiNoteData

    [Fact]
    public void TryValidateMidiNoteData_ValidPayload_ShouldReturnTrue()
    {
        Assert.True(GameInputValidator.TryValidateMidiNoteData(
            Data(new { noteNumber = 60, velocity = 80 })));
    }

    [Fact]
    public void TryValidateMidiNoteData_MissingNoteNumber_ShouldReturnFalse()
    {
        Assert.False(GameInputValidator.TryValidateMidiNoteData(
            Data(new { velocity = 80 })));
    }

    [Fact]
    public void TryValidateMidiNoteData_MissingVelocity_ShouldReturnFalse()
    {
        Assert.False(GameInputValidator.TryValidateMidiNoteData(
            Data(new { noteNumber = 60 })));
    }

    [Theory]
    // Non-object payloads must return false rather than throwing. TryGetProperty raises
    // InvalidOperationException when the element is not a JSON object, so the guard must short-circuit.
    [InlineData("\"not-an-object\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void TryValidateMidiNoteData_NonObjectPayload_ShouldReturnFalseNotThrow(string json)
    {
        Assert.False(GameInputValidator.TryValidateMidiNoteData(ParseData(json)));
    }

    #endregion

    #region TryParseMidiNoteData

    [Fact]
    public void TryParseMidiNoteData_ValidPayload_ShouldReturnParsedValues()
    {
        var ok = GameInputValidator.TryParseMidiNoteData(
            Data(new { noteNumber = 60, velocity = 80 }),
            out var noteNumber, out var velocity);

        Assert.True(ok);
        Assert.Equal(60, noteNumber);
        Assert.Equal(80, velocity);
    }

    [Fact]
    public void TryParseMidiNoteData_ValidBoundaryValues_ShouldReturnParsedValues()
    {
        var ok = GameInputValidator.TryParseMidiNoteData(
            Data(new { noteNumber = 0, velocity = 127 }),
            out var noteNumber, out var velocity);

        Assert.True(ok);
        Assert.Equal(0, noteNumber);
        Assert.Equal(127, velocity);
    }

    [Theory]
    // The ValueKind == Number guard is load-bearing: JsonElement.TryGetInt32 throws
    // InvalidOperationException for non-Number kinds. These cases must return false,
    // not throw. Previously GameApiImplementation.ParseMidiNoteInput inlined the parse
    // without the guard, so a JSON string like "36" would have thrown — masked only
    // because ValidateGameInput ran first at both call sites.
    [InlineData("{\"noteNumber\":\"36\",\"velocity\":100}", "string-valued noteNumber")]
    [InlineData("{\"noteNumber\":36,\"velocity\":\"100\"}", "string-valued velocity")]
    public void TryParseMidiNoteData_StringValuedField_ShouldReturnFalseNotThrow(string json, string reason)
    {
        var ok = GameInputValidator.TryParseMidiNoteData(ParseData(json), out var noteNumber, out var velocity);

        Assert.False(ok);
        // Out params must be zeroed on failure so callers can't observe stale data.
        Assert.Equal(0, noteNumber);
        Assert.Equal(0, velocity);
    }

    [Theory]
    [InlineData("{\"noteNumber\":128,\"velocity\":100}")]
    [InlineData("{\"noteNumber\":-1,\"velocity\":100}")]
    [InlineData("{\"noteNumber\":36,\"velocity\":128}")]
    [InlineData("{\"noteNumber\":36,\"velocity\":-1}")]
    [InlineData("{\"velocity\":100}")]
    [InlineData("{\"noteNumber\":36}")]
    public void TryParseMidiNoteData_InvalidPayload_ShouldReturnFalseAndZeroOutParams(string json)
    {
        var ok = GameInputValidator.TryParseMidiNoteData(ParseData(json), out var noteNumber, out var velocity);

        Assert.False(ok);
        Assert.Equal(0, noteNumber);
        Assert.Equal(0, velocity);
    }

    #endregion
}
