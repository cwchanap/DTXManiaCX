#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

internal sealed class CrashLogFieldPolicy
{
    internal const string RedactedValue = "[REDACTED]";
    internal const string UnclassifiedMessageTemplate = "[UNCLASSIFIED MESSAGE OMITTED]";

    private const int MaximumNormalizedTextLength = 128;

    private static readonly HashSet<string> AllowedPropertyNames = new(StringComparer.Ordinal)
    {
        "Stage",
        "PreviousStage",
        "TargetStage",
        "Milestone",
        "Width",
        "Height",
        "Fullscreen",
        "VSync",
        "MidiDeviceCount",
        "Enabled",
        "Count",
        "Status"
    };

    private readonly IReadOnlyDictionary<int, CrashLogEventDefinition> _events;

    internal static EventId UnclassifiedEventId { get; } = default;

    internal static CrashLogFieldPolicy Default { get; } = new(new Dictionary<int, CrashLogEventDefinition>
    {
        [5100] = new(5100, "crash_safe_stage", "Crash-safe stage changed to {Stage}"),
        [5101] = new(5101, "crash_runtime_started", "Crash reporting runtime started"),
        [5102] = new(5102, "startup_milestone_reached", "Startup milestone reached: {Milestone}"),
        [5103] = new(5103, "stage_transition_requested", "Stage transition requested: {PreviousStage} -> {TargetStage}"),
        [5104] = new(5104, "stage_transition_started", "Stage transition started: {PreviousStage} -> {TargetStage}"),
        [5105] = new(5105, "stage_transition_completed", "Stage transition completed: {PreviousStage} -> {TargetStage}"),
        [5106] = new(5106, "configuration_screen_changed", "Configuration screen {Status}"),
        [5107] = new(5107, "graphics_settings_changed", "Graphics settings updated: {Width}x{Height}, fullscreen={Fullscreen}, vsync={VSync}"),
        [5108] = new(5108, "midi_device_count_changed", "MIDI device count: {MidiDeviceCount}"),
        [5109] = new(5109, "crash_runtime_disabled", "Crash reporting runtime disabled"),
        [5110] = new(5110, "exit_requested", "Orderly exit requested")
    });

    private CrashLogFieldPolicy(IReadOnlyDictionary<int, CrashLogEventDefinition> events)
    {
        _events = events;
    }

    internal object? NormalizeProperty(string propertyName, object? value)
    {
        return IsAllowedProperty(propertyName) ? NormalizeScalar(value) : RedactedValue;
    }

    internal bool TryNormalizeProperty(string propertyName, object? value, out object? normalizedValue)
    {
        if (!IsAllowedProperty(propertyName))
        {
            normalizedValue = null;
            return false;
        }

        normalizedValue = NormalizeScalar(value);
        return true;
    }

    internal bool TryClassify(
        EventId eventId,
        string? originalFormat,
        out EventId safeEventId,
        out string safeMessageTemplate)
    {
        if (originalFormat is not null
            && _events.TryGetValue(eventId.Id, out var definition)
            && string.Equals(definition.Name, eventId.Name, StringComparison.Ordinal)
            && string.Equals(definition.MessageTemplate, originalFormat, StringComparison.Ordinal))
        {
            safeEventId = new EventId(definition.Id, definition.Name);
            safeMessageTemplate = definition.MessageTemplate;
            return true;
        }

        safeEventId = default;
        safeMessageTemplate = UnclassifiedMessageTemplate;
        return false;
    }

    internal string NormalizeExceptionType(Type exceptionType)
    {
        var typeName = exceptionType.FullName ?? exceptionType.Name;
        return LimitText(typeName);
    }

    private static bool IsAllowedProperty(string propertyName)
    {
        return propertyName is not null && AllowedPropertyNames.Contains(propertyName);
    }

    private static object? NormalizeScalar(object? value)
    {
        return value switch
        {
            null => null,
            string => RedactedValue,
            bool booleanValue => booleanValue,
            byte byteValue => byteValue,
            sbyte signedByteValue => signedByteValue,
            short shortValue => shortValue,
            ushort unsignedShortValue => unsignedShortValue,
            int integerValue => integerValue,
            uint unsignedIntegerValue => unsignedIntegerValue,
            long longValue => longValue,
            ulong unsignedLongValue => unsignedLongValue,
            float singleValue => singleValue,
            double doubleValue => doubleValue,
            decimal decimalValue => decimalValue,
            Enum enumValue => enumValue,
            DateTime dateTimeValue => NormalizeDateTime(dateTimeValue),
            DateTimeOffset dateTimeOffsetValue => dateTimeOffsetValue.ToUniversalTime(),
            Guid guidValue => guidValue,
            _ => RedactedValue
        };
    }

    private static DateTime NormalizeDateTime(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }

    private static string LimitText(string value)
    {
        return value.Length <= MaximumNormalizedTextLength
            ? value
            : value[..MaximumNormalizedTextLength];
    }

    private sealed record CrashLogEventDefinition(int Id, string Name, string MessageTemplate);
}
