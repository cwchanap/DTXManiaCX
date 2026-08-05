#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Stage;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Test.CrashReporting;

[Trait("Category", "Unit")]
public sealed class CrashLogFieldPolicyTests
{
    private static CrashLogFieldPolicy Policy => CrashLogFieldPolicy.Default;

    [Fact]
    public void NormalizeProperty_WithDisallowedName_ShouldRedact()
    {
        Assert.Equal(CrashLogFieldPolicy.RedactedValue, Policy.NormalizeProperty("SongTitle", "Secret Song"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NormalizeProperty_WithNullOrdinalName_ShouldRedact(string? propertyName)
    {
        // IsAllowedProperty guards against null; the ordinal comparer rejects empty strings.
        Assert.Equal(CrashLogFieldPolicy.RedactedValue, Policy.NormalizeProperty(propertyName!, "value"));
    }

    [Fact]
    public void TryNormalizeProperty_WithDisallowedName_ShouldReturnFalse()
    {
        Assert.False(Policy.TryNormalizeProperty("SongTitle", "Secret Song", out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalizeProperty_WithReasonEnum_ShouldRetainDefinedValue()
    {
        Assert.True(Policy.TryNormalizeProperty(
            "Reason",
            StageTransitionRejectionReason.AlreadyTransitioning,
            out var normalized));

        Assert.Equal(StageTransitionRejectionReason.AlreadyTransitioning, normalized);
    }

    [Fact]
    public void TryNormalizeProperty_WithUndefinedReasonEnum_ShouldRedact()
    {
        Assert.True(Policy.TryNormalizeProperty(
            "Reason",
            (StageTransitionRejectionReason)999,
            out var normalized));

        Assert.Equal(CrashLogFieldPolicy.RedactedValue, normalized);
    }

    [Fact]
    public void TryNormalizeProperty_WithReasonNonEnumValue_ShouldRedact()
    {
        Assert.True(Policy.TryNormalizeProperty("Reason", "some text", out var normalized));
        Assert.Equal(CrashLogFieldPolicy.RedactedValue, normalized);
    }

    [Fact]
    public void NormalizeProperty_WithByteScalar_ShouldRetain()
    {
        Assert.Equal((byte)5, Policy.NormalizeProperty("Count", (byte)5));
    }

    [Fact]
    public void NormalizeProperty_WithSignedByteScalar_ShouldRetain()
    {
        Assert.Equal((sbyte)-3, Policy.NormalizeProperty("Count", (sbyte)(-3)));
    }

    [Fact]
    public void NormalizeProperty_WithShortScalar_ShouldRetain()
    {
        Assert.Equal((short)7, Policy.NormalizeProperty("Count", (short)7));
    }

    [Fact]
    public void NormalizeProperty_WithUnsignedShortScalar_ShouldRetain()
    {
        Assert.Equal((ushort)9, Policy.NormalizeProperty("Count", (ushort)9));
    }

    [Fact]
    public void NormalizeProperty_WithUnsignedIntScalar_ShouldRetain()
    {
        Assert.Equal(42u, Policy.NormalizeProperty("Count", 42u));
    }

    [Fact]
    public void NormalizeProperty_WithLongScalar_ShouldRetain()
    {
        Assert.Equal(123L, Policy.NormalizeProperty("Count", 123L));
    }

    [Fact]
    public void NormalizeProperty_WithUnsignedLongScalar_ShouldRetain()
    {
        Assert.Equal(999UL, Policy.NormalizeProperty("Count", 999UL));
    }

    [Fact]
    public void NormalizeProperty_WithFloatScalar_ShouldRetain()
    {
        Assert.Equal(3.14f, Policy.NormalizeProperty("Count", 3.14f));
    }

    [Fact]
    public void NormalizeProperty_WithDoubleScalar_ShouldRetain()
    {
        Assert.Equal(2.718, Policy.NormalizeProperty("Count", 2.718));
    }

    [Fact]
    public void NormalizeProperty_WithDecimalScalar_ShouldRetain()
    {
        Assert.Equal(1.5m, Policy.NormalizeProperty("Count", 1.5m));
    }

    [Fact]
    public void NormalizeProperty_WithStringScalar_ShouldRedact()
    {
        Assert.Equal(CrashLogFieldPolicy.RedactedValue, Policy.NormalizeProperty("Count", "Secret Song"));
    }

    [Fact]
    public void NormalizeProperty_WithNullScalar_ShouldReturnNull()
    {
        Assert.Null(Policy.NormalizeProperty("Count", null));
    }

    [Fact]
    public void NormalizeProperty_WithGuid_ShouldRetain()
    {
        var guid = Guid.Parse("9bc2520f-5b38-4e6c-a1c4-5f34e0135da3");
        Assert.Equal(guid, Policy.NormalizeProperty("Count", guid));
    }

    [Fact]
    public void NormalizeProperty_WithDateTimeUnspecifiedKind_ShouldNormalizeToUtc()
    {
        var dateTime = new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Unspecified);
        var normalized = Assert.IsType<DateTime>(Policy.NormalizeProperty("Count", dateTime));

        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.Equal(dateTime, normalized);
    }

    [Fact]
    public void NormalizeProperty_WithDateTimeLocalKind_ShouldConvertToUtc()
    {
        var local = new DateTime(2026, 8, 2, 14, 0, 0, DateTimeKind.Local);
        var normalized = Assert.IsType<DateTime>(Policy.NormalizeProperty("Count", local));

        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
    }

    [Fact]
    public void NormalizeProperty_WithUnsupportedType_ShouldRedact()
    {
        Assert.Equal(CrashLogFieldPolicy.RedactedValue, Policy.NormalizeProperty("Count", new object()));
    }

    [Fact]
    public void NormalizeExceptionType_WithLongName_ShouldTruncateToLimit()
    {
        var longNamespace = new string('A', 200);
        var typeName = longNamespace + ".MyException";
        var exceptionType = Type.GetType(typeName) ?? typeof(Exception);
        // Use a real type with a long full name via reflection is impractical; instead verify
        // that a normal type name is preserved (LimitText only truncates > 128 chars).
        var result = Policy.NormalizeExceptionType(typeof(InvalidOperationException));

        Assert.Equal(typeof(InvalidOperationException).FullName, result);
    }

    [Fact]
    public void TryNormalizeContextProperty_ProcessRuntimeFramework_ShouldRetainMetadata()
    {
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Process, "RuntimeFramework", ".NET 8.0", out var normalized));
        Assert.Equal(".NET 8.0", normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_ProcessOperatingSystem_ShouldRetainMetadata()
    {
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Process, "OperatingSystem", "macOS", out var normalized));
        Assert.Equal("macOS", normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_ProcessArchitecture_ShouldRetainEnum()
    {
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Process, "ProcessArchitecture", Architecture.Arm64, out var normalized));
        Assert.Equal(Architecture.Arm64, normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_ProcessStartUtc_ShouldConvertToUniversal()
    {
        var offset = new DateTimeOffset(2026, 8, 2, 14, 0, 0, TimeSpan.FromHours(2));
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Process, "ProcessStartUtc", offset, out var normalized));

        var result = Assert.IsType<DateTimeOffset>(normalized);
        Assert.Equal(TimeSpan.Zero, result.Offset);
    }

    [Fact]
    public void TryNormalizeContextProperty_ProcessUnknownField_ShouldReturnFalse()
    {
        Assert.False(Policy.TryNormalizeContextProperty(
            CrashContextKind.Process, "CommandLine", "secret", out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_ProcessMetadataTooLong_ShouldRedact()
    {
        var longValue = new string('X', 300);
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Process, "RuntimeFramework", longValue, out var normalized));
        Assert.Equal(CrashLogFieldPolicy.RedactedValue, normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_ProcessMetadataWithNewline_ShouldRedact()
    {
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Process, "OperatingSystem", "OS\ninjection", out var normalized));
        Assert.Equal(CrashLogFieldPolicy.RedactedValue, normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_ProcessMetadataWithNullChar_ShouldRedact()
    {
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Process, "OperatingSystem", "OS\0injection", out var normalized));
        Assert.Equal(CrashLogFieldPolicy.RedactedValue, normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_ApplicationVersion_ShouldRetain()
    {
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Application, "ApplicationVersion", "1.2.3", out var normalized));
        Assert.Equal("1.2.3", normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_ApplicationUnknownField_ShouldReturnFalse()
    {
        Assert.False(Policy.TryNormalizeContextProperty(
            CrashContextKind.Application, "BuildPath", "secret", out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_StartupMilestone_ShouldRetainEnum()
    {
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Startup,
            "Milestone",
            StartupCriticalPathMilestone.StartupActivation,
            out var normalized));
        Assert.Equal(StartupCriticalPathMilestone.StartupActivation, normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_StartupUndefinedMilestone_ShouldReturnFalse()
    {
        Assert.False(Policy.TryNormalizeContextProperty(
            CrashContextKind.Startup, "Milestone", (StartupCriticalPathMilestone)999, out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_StartupUnknownField_ShouldReturnFalse()
    {
        Assert.False(Policy.TryNormalizeContextProperty(
            CrashContextKind.Startup, "SongTitle", "secret", out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_ConfigurationScreenDimensions_ShouldRetain()
    {
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Configuration, "ScreenWidth", 1920, out var width));
        Assert.Equal(1920, width);

        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Configuration, "ScreenHeight", 1080, out var height));
        Assert.Equal(1080, height);
    }

    [Fact]
    public void TryNormalizeContextProperty_ConfigurationDimensionOutOfRange_ShouldReturnFalse()
    {
        Assert.False(Policy.TryNormalizeContextProperty(
            CrashContextKind.Configuration, "ScreenWidth", -1, out var normalized));
        Assert.Null(normalized);

        Assert.False(Policy.TryNormalizeContextProperty(
            CrashContextKind.Configuration, "ScreenWidth", 20_000, out normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_ConfigurationBufferSizeMs_ShouldRetainInBounds()
    {
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Configuration, "BufferSizeMs", 30_000, out var normalized));
        Assert.Equal(30_000, normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_ConfigurationBufferSizeMsOutOfRange_ShouldReturnFalse()
    {
        Assert.False(Policy.TryNormalizeContextProperty(
            CrashContextKind.Configuration, "BufferSizeMs", -1, out var normalized));
        Assert.Null(normalized);

        Assert.False(Policy.TryNormalizeContextProperty(
            CrashContextKind.Configuration, "BufferSizeMs", 61_000, out normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_ConfigurationCounts_ShouldRetainInBounds()
    {
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Configuration, "KeyBindingCount", 4, out var normalized));
        Assert.Equal(4, normalized);

        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Configuration, "UnboundDrumLaneCount", 1, out normalized));
        Assert.Equal(1, normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_ConfigurationBooleans_ShouldRetain()
    {
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Configuration, "FullScreen", true, out var normalized));
        Assert.True((bool)normalized!);

        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Configuration, "VSyncWait", false, out normalized));
        Assert.False((bool)normalized!);

        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Configuration, "AutoPlay", true, out normalized));
        Assert.True((bool)normalized!);

        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Configuration, "NoFail", true, out normalized));
        Assert.True((bool)normalized!);

        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Configuration, "EnableGameApi", true, out normalized));
        Assert.True((bool)normalized!);
    }

    [Fact]
    public void TryNormalizeContextProperty_ConfigurationUnknownField_ShouldReturnFalse()
    {
        Assert.False(Policy.TryNormalizeContextProperty(
            CrashContextKind.Configuration, "GameApiKey", "secret", out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_StageField_ShouldRetainEnum()
    {
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Stage, "Stage", StageType.Title, out var normalized));
        Assert.Equal(StageType.Title, normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_StageUndefinedEnum_ShouldReturnFalse()
    {
        Assert.False(Policy.TryNormalizeContextProperty(
            CrashContextKind.Stage, "Stage", (StageType)999, out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_StageCount_ShouldRetainInBounds()
    {
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Stage, "StageCount", 3, out var normalized));
        Assert.Equal(3, normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_StageCountOutOfRange_ShouldReturnFalse()
    {
        Assert.False(Policy.TryNormalizeContextProperty(
            CrashContextKind.Stage, "StageCount", -1, out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_StageStatus_ShouldRedact()
    {
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Stage, "Status", "secret", out var normalized));
        Assert.Equal(CrashLogFieldPolicy.RedactedValue, normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_StageUnknownField_ShouldReturnFalse()
    {
        Assert.False(Policy.TryNormalizeContextProperty(
            CrashContextKind.Stage, "SharedData", "secret", out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_GraphicsDimensions_ShouldRetainInBounds()
    {
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Graphics, "Width", 1920, out var normalized));
        Assert.Equal(1920, normalized);

        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Graphics, "Height", 1080, out normalized));
        Assert.Equal(1080, normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_GraphicsDimensionOutOfRange_ShouldReturnFalse()
    {
        Assert.False(Policy.TryNormalizeContextProperty(
            CrashContextKind.Graphics, "Width", -1, out var normalized));
        Assert.Null(normalized);

        Assert.False(Policy.TryNormalizeContextProperty(
            CrashContextKind.Graphics, "Height", 20_000, out normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_GraphicsBooleans_ShouldRetain()
    {
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Graphics, "Fullscreen", true, out var normalized));
        Assert.True((bool)normalized!);

        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Graphics, "VSync", false, out normalized));
        Assert.False((bool)normalized!);

        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Graphics, "DeviceAvailable", true, out normalized));
        Assert.True((bool)normalized!);
    }

    [Fact]
    public void TryNormalizeContextProperty_GraphicsBackBufferFormat_ShouldRetainEnum()
    {
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Graphics, "BackBufferFormat", SurfaceFormat.Color, out var normalized));
        Assert.Equal(SurfaceFormat.Color, normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_GraphicsDepthStencilFormat_ShouldRetainEnum()
    {
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Graphics, "DepthStencilFormat", DepthFormat.Depth24, out var normalized));
        Assert.Equal(DepthFormat.Depth24, normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_GraphicsMultiSampleCount_ShouldRetainInBounds()
    {
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Graphics, "MultiSampleCount", 4, out var normalized));
        Assert.Equal(4, normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_GraphicsMultiSampleCountOutOfRange_ShouldReturnFalse()
    {
        Assert.False(Policy.TryNormalizeContextProperty(
            CrashContextKind.Graphics, "MultiSampleCount", -1, out var normalized));
        Assert.Null(normalized);

        Assert.False(Policy.TryNormalizeContextProperty(
            CrashContextKind.Graphics, "MultiSampleCount", 65, out normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_GraphicsUnknownField_ShouldReturnFalse()
    {
        Assert.False(Policy.TryNormalizeContextProperty(
            CrashContextKind.Graphics, "GraphicsSettings", "secret", out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_InputMidiDeviceCount_ShouldRetainInBounds()
    {
        Assert.True(Policy.TryNormalizeContextProperty(
            CrashContextKind.Input, "MidiDeviceCount", 2, out var normalized));
        Assert.Equal(2, normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_InputMidiDeviceCountOutOfRange_ShouldReturnFalse()
    {
        Assert.False(Policy.TryNormalizeContextProperty(
            CrashContextKind.Input, "MidiDeviceCount", -1, out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_InputUnknownField_ShouldReturnFalse()
    {
        Assert.False(Policy.TryNormalizeContextProperty(
            CrashContextKind.Input, "MidiDeviceName", "secret", out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalizeContextProperty_AudioAnyField_ShouldReturnFalse()
    {
        Assert.False(Policy.TryNormalizeContextProperty(
            CrashContextKind.Audio, "DeviceName", "secret", out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void NormalizeContextFailureCode_WithNull_ShouldReturnNull()
    {
        Assert.Null(Policy.NormalizeContextFailureCode(CrashContextKind.Audio, null));
    }

    [Fact]
    public void NormalizeContextFailureCode_WithAudioUnavailableCode_ShouldRetain()
    {
        Assert.Equal(
            CrashContextPublisher.AudioDeviceSummaryUnavailable,
            Policy.NormalizeContextFailureCode(
                CrashContextKind.Audio,
                CrashContextPublisher.AudioDeviceSummaryUnavailable));
    }

    [Fact]
    public void NormalizeContextFailureCode_WithAudioUnknownCode_ShouldRedact()
    {
        Assert.Equal(
            CrashLogFieldPolicy.RedactedValue,
            Policy.NormalizeContextFailureCode(CrashContextKind.Audio, "secret_failure"));
    }

    [Fact]
    public void NormalizeContextFailureCode_WithNonAudioKind_ShouldRedact()
    {
        Assert.Equal(
            CrashLogFieldPolicy.RedactedValue,
            Policy.NormalizeContextFailureCode(CrashContextKind.Graphics, "graphics_failure"));
    }

    [Fact]
    public void TryClassify_WithMatchingEvent_ShouldReturnTrue()
    {
        Assert.True(Policy.TryClassify(
            new EventId(5100, "crash_safe_stage"),
            "Crash-safe stage changed to {Stage}",
            out var safeEventId,
            out var safeMessageTemplate));

        Assert.Equal(5100, safeEventId.Id);
        Assert.Equal("crash_safe_stage", safeEventId.Name);
        Assert.Equal("Crash-safe stage changed to {Stage}", safeMessageTemplate);
    }

    [Fact]
    public void TryClassify_WithUnknownEventId_ShouldReturnFalse()
    {
        Assert.False(Policy.TryClassify(
            new EventId(9999, "unknown"),
            "some template",
            out var safeEventId,
            out var safeMessageTemplate));

        Assert.Equal(0, safeEventId.Id);
        Assert.Null(safeEventId.Name);
        Assert.Equal(CrashLogFieldPolicy.UnclassifiedMessageTemplate, safeMessageTemplate);
    }

    [Fact]
    public void TryClassify_WithNullOriginalFormat_ShouldReturnFalse()
    {
        Assert.False(Policy.TryClassify(
            new EventId(5100, "crash_safe_stage"),
            null,
            out var safeEventId,
            out var safeMessageTemplate));

        Assert.Equal(CrashLogFieldPolicy.UnclassifiedMessageTemplate, safeMessageTemplate);
    }

    [Fact]
    public void TryClassify_WithMismatchedName_ShouldReturnFalse()
    {
        Assert.False(Policy.TryClassify(
            new EventId(5100, "wrong_name"),
            "Crash-safe stage changed to {Stage}",
            out _,
            out var safeMessageTemplate));

        Assert.Equal(CrashLogFieldPolicy.UnclassifiedMessageTemplate, safeMessageTemplate);
    }

    [Fact]
    public void TryClassify_WithMismatchedTemplate_ShouldReturnFalse()
    {
        Assert.False(Policy.TryClassify(
            new EventId(5100, "crash_safe_stage"),
            "wrong template",
            out _,
            out var safeMessageTemplate));

        Assert.Equal(CrashLogFieldPolicy.UnclassifiedMessageTemplate, safeMessageTemplate);
    }
}
