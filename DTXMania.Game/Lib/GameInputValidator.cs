#nullable enable

using System;
using System.Text.Json;

namespace DTXMania.Game.Lib;

/// <summary>
/// Shared validation logic for <see cref="GameInput"/> payloads received from both the
/// REST API (<see cref="GameApiServer"/>) and the JSON-RPC server (<see cref="JsonRpc.JsonRpcServer"/>).
/// Keeping the rules in one place prevents the two endpoints from silently drifting apart
/// (a previous version already diverged on whitespace handling for key data).
/// </summary>
public static class GameInputValidator
{
    /// <summary>
    /// Validate game input for security and correctness.
    /// </summary>
    /// <returns>A tuple indicating whether the input is valid and, when invalid, a human-readable error message.</returns>
    public static (bool IsValid, string ErrorMessage) ValidateGameInput(GameInput input)
    {
        // Validate input type
        if (!Enum.IsDefined(typeof(InputType), input.Type))
            return (false, "Invalid input type");

        // Validate data based on input type
        switch (input.Type)
        {
            case InputType.MouseClick:
            case InputType.MouseMove:
                // For mouse input, data should contain position info
                if (input.Data is null || input.Data.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    return (false, "Mouse input requires position data");

                if (input.Data.Value.ValueKind != JsonValueKind.Object)
                    return (false, "Mouse input data must be an object");
                break;

            case InputType.KeyPress:
            case InputType.KeyRelease:
                // For key input, data should contain key info
                if (input.Data is null || input.Data.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    return (false, "Key input requires key data");

                if (input.Data.Value.ValueKind == JsonValueKind.String)
                {
                    var keyData = input.Data.Value.GetString();
                    if (string.IsNullOrWhiteSpace(keyData) || keyData.Length > 50)
                        return (false, "Invalid key data format");
                }
                else if (input.Data.Value.ValueKind == JsonValueKind.Number)
                {
                    if (!input.Data.Value.TryGetInt32(out var keyCode) || keyCode < 0 || keyCode > 255)
                        return (false, "Invalid key data format");
                }
                else if (input.Data.Value.ValueKind == JsonValueKind.Object)
                {
                    // Object format from MCP bridge: {"key":"Down","holdDurationMs":50,"clientId":"default"}
                    if (!input.Data.Value.TryGetProperty("key", out var keyProp) ||
                        keyProp.ValueKind != JsonValueKind.String)
                        return (false, "Invalid key data format");
                    var keyString = keyProp.GetString();
                    if (string.IsNullOrWhiteSpace(keyString) || keyString.Length > 50)
                        return (false, "Invalid key data format");
                }
                else
                {
                    return (false, "Invalid key data format");
                }
                break;

            case InputType.MidiNoteOn:
            case InputType.MidiNoteOff:
                if (input.Data is null || input.Data.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    return (false, "MIDI input requires note data");

                if (input.Data.Value.ValueKind != JsonValueKind.Object)
                    return (false, "MIDI input data must be an object");

                if (!TryValidateMidiNoteData(input.Data.Value))
                    return (false, "Invalid MIDI note data format");
                break;

            default:
                return (false, "Unsupported input type");
        }

        return (true, string.Empty);
    }

    /// <summary>
    /// Validates that the given JSON value contains a well-formed MIDI note payload:
    /// an integer <c>noteNumber</c> (0–127) and an integer <c>velocity</c> (0–127).
    /// Returns <c>false</c> (never throws) for non-object input, since <see cref="JsonElement.TryGetProperty"/>
    /// raises <see cref="InvalidOperationException"/> when the element is not a JSON object.
    /// </summary>
    public static bool TryValidateMidiNoteData(JsonElement data)
        => TryParseMidiNoteData(data, out _, out _);

    /// <summary>
    /// Parses and validates a MIDI note payload in one pass, returning the parsed
    /// <paramref name="noteNumber"/> and <paramref name="velocity"/> on success. This is
    /// the single source of truth for MIDI note parsing: both the validator
    /// (<see cref="TryValidateMidiNoteData"/>) and the API input parser
    /// (<c>GameApiImplementation.ParseMidiNoteInput</c>) delegate here so the two
    /// endpoints cannot drift on what counts as a valid note.
    /// </summary>
    /// <remarks>
    /// The explicit <see cref="JsonValueKind.Number"/> guard before each
    /// <see cref="JsonElement.TryGetInt32"/> call is load-bearing:
    /// <see cref="JsonElement.TryGetInt32"/> throws <see cref="InvalidOperationException"/>
    /// for non-Number kinds (e.g. a JSON string <c>"36"</c>), so without this guard a
    /// caller that skips upstream validation would observe a throw instead of
    /// <c>false</c>.
    /// </remarks>
    public static bool TryParseMidiNoteData(JsonElement data, out int noteNumber, out int velocity)
    {
        // Out params default to 0 and are only assigned on full success, matching the
        // standard TryX convention (callers must never observe partial data on failure).
        noteNumber = 0;
        velocity = 0;

        if (data.ValueKind != JsonValueKind.Object)
            return false;

        // ValueKind == Number guard is load-bearing: TryGetInt32 throws
        // InvalidOperationException for non-Number kinds (e.g. a JSON string "36").
        if (!data.TryGetProperty("noteNumber", out var noteNumberProp) ||
            noteNumberProp.ValueKind != JsonValueKind.Number ||
            !noteNumberProp.TryGetInt32(out var parsedNoteNumber) ||
            parsedNoteNumber < 0 ||
            parsedNoteNumber > 127)
        {
            return false;
        }

        if (!data.TryGetProperty("velocity", out var velocityProp) ||
            velocityProp.ValueKind != JsonValueKind.Number ||
            !velocityProp.TryGetInt32(out var parsedVelocity) ||
            parsedVelocity < 0 ||
            parsedVelocity > 127)
        {
            return false;
        }

        noteNumber = parsedNoteNumber;
        velocity = parsedVelocity;
        return true;
    }
}
