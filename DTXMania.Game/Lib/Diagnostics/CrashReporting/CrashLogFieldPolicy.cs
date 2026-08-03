#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DTXMania.Game.Lib.Stage;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

internal sealed class CrashLogFieldPolicy
{
    internal const string RedactedValue = "[REDACTED]";
    internal const string UnclassifiedMessageTemplate = "[UNCLASSIFIED MESSAGE OMITTED]";

    private const int MaximumNormalizedTextLength = 128;
    private const int MaximumContextMetadataLength = 256;
    private const int MaximumReportedDimension = 16_384;
    private const int MaximumReportedCount = 100_000;
    private const int MaximumBufferMilliseconds = 60_000;

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
        "Status",
        "Reason"
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
        [5110] = new(5110, "exit_requested", "Orderly exit requested"),
        [5111] = new(5111, "stage_transition_rejected", "Stage transition rejected: {Reason}"),
        [5112] = new(5112, "graphics_device_lost", "Graphics device lost"),
        [5113] = new(5113, "graphics_device_reset", "Graphics device reset"),
        [5114] = new(5114, "graphics_initialized", "Graphics initialized: {Width}x{Height}, fullscreen={Fullscreen}, vsync={VSync}")
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

        if (propertyName == "Reason")
        {
            if (value is StageTransitionRejectionReason reason && Enum.IsDefined(reason))
            {
                normalizedValue = reason;
                return true;
            }

            normalizedValue = RedactedValue;
            return true;
        }

        normalizedValue = NormalizeScalar(value);
        return true;
    }

    internal bool TryNormalizeContextProperty(
        CrashContextKind kind,
        string propertyName,
        object? value,
        out object? normalizedValue)
    {
        normalizedValue = null;

        switch (kind)
        {
            case CrashContextKind.Process:
                switch (propertyName)
                {
                    case "RuntimeFramework" or "OperatingSystem" when value is string metadata:
                        normalizedValue = NormalizeContextMetadata(metadata);
                        return true;

                    case "ProcessArchitecture" when value is Architecture architecture
                                                   && Enum.IsDefined(architecture):
                        normalizedValue = architecture;
                        return true;

                    case "ProcessStartUtc" when value is DateTimeOffset processStartUtc:
                        normalizedValue = processStartUtc.ToUniversalTime();
                        return true;
                }
                break;

            case CrashContextKind.Application:
                if (propertyName == "ApplicationVersion" && value is string applicationVersion)
                {
                    normalizedValue = NormalizeContextMetadata(applicationVersion);
                    return true;
                }
                break;

            case CrashContextKind.Startup:
                if (propertyName == "Milestone"
                    && value is StartupCriticalPathMilestone milestone
                    && Enum.IsDefined(milestone))
                {
                    normalizedValue = milestone;
                    return true;
                }
                break;

            case CrashContextKind.Configuration:
                if (TryNormalizeConfigurationProperty(propertyName, value, out normalizedValue))
                {
                    return true;
                }
                break;

            case CrashContextKind.Stage:
                if (propertyName == "Stage"
                    && value is StageType stage
                    && Enum.IsDefined(stage))
                {
                    normalizedValue = stage;
                    return true;
                }

                if (propertyName == "StageCount" && TryNormalizeCount(value, out normalizedValue))
                {
                    return true;
                }

                // Preserve the prior in-memory redaction behavior for a legacy diagnostic
                // field without allowing its original value through the context cache.
                if (propertyName == "Status")
                {
                    normalizedValue = RedactedValue;
                    return true;
                }
                break;

            case CrashContextKind.Graphics:
                if (TryNormalizeGraphicsProperty(propertyName, value, out normalizedValue))
                {
                    return true;
                }
                break;

            case CrashContextKind.Input:
                if (propertyName == "MidiDeviceCount" && TryNormalizeCount(value, out normalizedValue))
                {
                    return true;
                }
                break;

            case CrashContextKind.Audio:
                break;
        }

        return false;
    }

    internal string? NormalizeContextFailureCode(CrashContextKind kind, string? failureCode)
    {
        if (failureCode is null)
        {
            return null;
        }

        return kind == CrashContextKind.Audio
               && string.Equals(
                   failureCode,
                   CrashContextPublisher.AudioDeviceSummaryUnavailable,
                   StringComparison.Ordinal)
            ? CrashContextPublisher.AudioDeviceSummaryUnavailable
            : RedactedValue;
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

    private static bool TryNormalizeConfigurationProperty(
        string propertyName,
        object? value,
        out object? normalizedValue)
    {
        normalizedValue = null;

        switch (propertyName)
        {
            case "ScreenWidth" when TryNormalizeDimension(value, out normalizedValue):
            case "ScreenHeight" when TryNormalizeDimension(value, out normalizedValue):
                return true;

            case "BufferSizeMs" when value is int bufferSizeMs
                                     && bufferSizeMs >= 0
                                     && bufferSizeMs <= MaximumBufferMilliseconds:
                normalizedValue = bufferSizeMs;
                return true;

            case "KeyBindingCount" when TryNormalizeCount(value, out normalizedValue):
            case "SystemKeyBindingCount" when TryNormalizeCount(value, out normalizedValue):
            case "UnboundDrumLaneCount" when TryNormalizeCount(value, out normalizedValue):
            case "UnboundDrumButtonCount" when TryNormalizeCount(value, out normalizedValue):
            case "MidiVelocityThresholdCount" when TryNormalizeCount(value, out normalizedValue):
                return true;

            case "FullScreen" or "VSyncWait" or "AutoPlay" or "NoFail" or "EnableGameApi"
                when value is bool booleanValue:
                normalizedValue = booleanValue;
                return true;

            default:
                return false;
        }
    }

    private static bool TryNormalizeGraphicsProperty(
        string propertyName,
        object? value,
        out object? normalizedValue)
    {
        normalizedValue = null;

        switch (propertyName)
        {
            case "Width" when TryNormalizeDimension(value, out normalizedValue):
            case "Height" when TryNormalizeDimension(value, out normalizedValue):
                return true;

            case "Fullscreen" or "VSync" or "DeviceAvailable" when value is bool booleanValue:
                normalizedValue = booleanValue;
                return true;

            case "BackBufferFormat" when value is SurfaceFormat backBufferFormat
                                         && Enum.IsDefined(backBufferFormat):
                normalizedValue = backBufferFormat;
                return true;

            case "DepthStencilFormat" when value is DepthFormat depthStencilFormat
                                           && Enum.IsDefined(depthStencilFormat):
                normalizedValue = depthStencilFormat;
                return true;

            case "MultiSampleCount" when value is int multiSampleCount
                                         && multiSampleCount >= 0
                                         && multiSampleCount <= 64:
                normalizedValue = multiSampleCount;
                return true;

            default:
                return false;
        }
    }

    private static bool TryNormalizeDimension(object? value, out object? normalizedValue)
    {
        if (value is int dimension && dimension >= 0 && dimension <= MaximumReportedDimension)
        {
            normalizedValue = dimension;
            return true;
        }

        normalizedValue = null;
        return false;
    }

    private static bool TryNormalizeCount(object? value, out object? normalizedValue)
    {
        if (value is int count && count >= 0 && count <= MaximumReportedCount)
        {
            normalizedValue = count;
            return true;
        }

        normalizedValue = null;
        return false;
    }

    private static string NormalizeContextMetadata(string value)
    {
        if (value.Length > MaximumContextMetadataLength
            || value.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            return RedactedValue;
        }

        return value;
    }

    private sealed record CrashLogEventDefinition(int Id, string Name, string MessageTemplate);
}
