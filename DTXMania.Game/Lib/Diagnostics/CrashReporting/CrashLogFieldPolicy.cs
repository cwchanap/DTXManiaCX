#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// Every property name a breadcrumb or log call may carry into a crash report. A name that is
    /// not here is dropped, so adding a field to a call site is a deliberate two-step act.
    /// </summary>
    private static readonly HashSet<string> AllowedPropertyNames = new(StringComparer.Ordinal)
    {
        "PreviousStage",
        "TargetStage",
        "Milestone",
        "Width",
        "Height",
        "Fullscreen",
        "VSync",
        "MidiDeviceCount",
        "Reason"
    };

    private readonly IReadOnlyDictionary<int, CrashLogEvent> _events;

    internal static EventId UnclassifiedEventId { get; } = default;

    internal static CrashLogFieldPolicy Default { get; } =
        new(CrashLogEvents.All.ToDictionary(static crashEvent => crashEvent.Id));

    private CrashLogFieldPolicy(IReadOnlyDictionary<int, CrashLogEvent> events)
    {
        _events = events;
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

            case "KeyBindingCount" when TryNormalizeCount(value, out normalizedValue):
            case "SystemKeyBindingCount" when TryNormalizeCount(value, out normalizedValue):
            case "AutoPlayLaneCount" when TryNormalizeCount(value, out normalizedValue):
            case "UnboundDrumLaneCount" when TryNormalizeCount(value, out normalizedValue):
            case "UnboundDrumButtonCount" when TryNormalizeCount(value, out normalizedValue):
            case "MidiVelocityThresholdCount" when TryNormalizeCount(value, out normalizedValue):
                return true;

            case "FullScreen" or "VSyncWait" or "NoFail" or "EnableGameApi"
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
}
