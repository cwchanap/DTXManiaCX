#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Graphics;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Utilities;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

/// <summary>
/// Publishes the small, deterministic allowlist of context values that may be retained for a
/// managed crash report. This helper intentionally has no discovery APIs: callers supply values
/// collected during normal lifecycle work, and fatal capture only reads the resulting cache.
/// </summary>
internal static class CrashContextPublisher
{
    internal const string AudioDeviceSummaryUnavailable = "audio_device_summary_unavailable";

    internal static void PublishProcessAndApplication(
        IGameCrashDiagnostics diagnostics,
        DateTimeOffset processStartedUtc)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var assembly = typeof(CrashContextPublisher).Assembly;
        var applicationVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(applicationVersion))
        {
            applicationVersion = assembly.GetName().Version?.ToString() ?? "Unknown";
        }

        diagnostics.Contexts.SetSnapshot(new CrashContextSnapshot(
            CrashContextKind.Process,
            CrashContextStatus.Available,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["RuntimeFramework"] = RuntimeInformation.FrameworkDescription,
                ["OperatingSystem"] = RuntimeInformation.OSDescription,
                ["ProcessArchitecture"] = RuntimeInformation.ProcessArchitecture,
                ["ProcessStartUtc"] = processStartedUtc.ToUniversalTime()
            }));
        diagnostics.Contexts.SetSnapshot(new CrashContextSnapshot(
            CrashContextKind.Application,
            CrashContextStatus.Available,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["ApplicationVersion"] = applicationVersion
            }));
    }

    internal static void PublishConfiguration(IGameCrashDiagnostics diagnostics, ConfigData config)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(config);

        diagnostics.Contexts.SetSnapshot(new CrashContextSnapshot(
            CrashContextKind.Configuration,
            CrashContextStatus.Available,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["ScreenWidth"] = config.ScreenWidth,
                ["ScreenHeight"] = config.ScreenHeight,
                ["FullScreen"] = config.FullScreen,
                ["VSyncWait"] = config.VSyncWait,
                ["BufferSizeMs"] = config.BufferSizeMs,
                ["NoFail"] = config.NoFail,
                ["EnableGameApi"] = config.EnableGameApi,
                ["KeyBindingCount"] = config.KeyBindings?.Count ?? 0,
                ["SystemKeyBindingCount"] = config.SystemKeyBindings?.Count ?? 0,
                ["AutoPlayLaneCount"] = config.AutoPlayLanes.Count,
                ["UnboundDrumLaneCount"] = config.UnboundDrumLanes?.Count ?? 0,
                ["UnboundDrumButtonCount"] = config.UnboundDrumButtons?.Count ?? 0,
                ["MidiVelocityThresholdCount"] = config.MidiVelocityThresholds.Count
            }));
    }

    internal static void RegisterSensitivePrefixes(IGameCrashDiagnostics diagnostics, ConfigData config)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(config);

        var sensitiveData = diagnostics.SensitiveData;
        foreach (var songRoot in config.SongRoots)
        {
            RegisterPath(sensitiveData, songRoot);
        }

        RegisterPath(sensitiveData, config.SkinPath);
        RegisterComputedPath(sensitiveData, AppPaths.GetAppDataRoot);
        RegisterComputedPath(sensitiveData, AppPaths.GetConfigDatabasePath);
        RegisterComputedPath(sensitiveData, AppPaths.GetLegacyConfigFilePath);
        RegisterComputedPath(sensitiveData, AppPaths.GetSongsDatabasePath);
        RegisterComputedPath(sensitiveData, AppPaths.GetPlaybackAudioCacheRoot);
        RegisterComputedPath(sensitiveData, AppPaths.GetCrashReportsRoot);

        // The API key is a secret. Register its value so the sanitizer redacts it anywhere it
        // appears (exception messages, stack traces, metadata) before a report reaches disk.
        if (!string.IsNullOrEmpty(config.GameApiKey))
        {
            sensitiveData.RegisterSecret(config.GameApiKey);
        }
    }

    internal static void PublishStartupMilestone(
        IGameCrashDiagnostics? diagnostics,
        StartupCriticalPathMilestone milestone)
    {
        // Headless lifecycle tests can exercise startup timing on an uninitialized BaseGame
        // test double. Publishing diagnostics must never change that pre-existing behavior.
        if (diagnostics == null)
        {
            return;
        }

        var fields = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Milestone"] = milestone
        };
        diagnostics.Contexts.SetSnapshot(new CrashContextSnapshot(
            CrashContextKind.Startup,
            CrashContextStatus.Available,
            fields));
        diagnostics.Breadcrumbs.Record(CrashBreadcrumbEvents.InitializationMilestoneReached, fields);
    }

    internal static void PublishGraphics(
        IGameCrashDiagnostics diagnostics,
        GraphicsSettings settings,
        bool isDeviceAvailable)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(settings);

        diagnostics.Contexts.SetSnapshot(new CrashContextSnapshot(
            CrashContextKind.Graphics,
            CrashContextStatus.Available,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Width"] = settings.Width,
                ["Height"] = settings.Height,
                ["Fullscreen"] = settings.IsFullscreen,
                ["VSync"] = settings.VSync,
                ["BackBufferFormat"] = settings.BackBufferFormat,
                ["DepthStencilFormat"] = settings.DepthStencilFormat,
                ["MultiSampleCount"] = settings.MultiSampleCount,
                ["DeviceAvailable"] = isDeviceAvailable
            }));
    }

    internal static void PublishAudioUnavailable(IGameCrashDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        diagnostics.Contexts.SetSnapshot(new CrashContextSnapshot(
            CrashContextKind.Audio,
            CrashContextStatus.Unavailable,
            new Dictionary<string, object?>(StringComparer.Ordinal),
            AudioDeviceSummaryUnavailable));
    }

    internal static void PublishInput(IGameCrashDiagnostics diagnostics, int midiDeviceCount)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        diagnostics.Contexts.SetSnapshot(new CrashContextSnapshot(
            CrashContextKind.Input,
            CrashContextStatus.Available,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["MidiDeviceCount"] = Math.Max(0, midiDeviceCount)
            }));
    }

    internal static void PublishStage(
        ICrashContextSink contexts,
        StageType stage,
        int stageCount)
    {
        ArgumentNullException.ThrowIfNull(contexts);

        contexts.SetSnapshot(new CrashContextSnapshot(
            CrashContextKind.Stage,
            CrashContextStatus.Available,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Stage"] = stage,
                ["StageCount"] = Math.Max(0, stageCount)
            }));
    }

    private static void RegisterComputedPath(ICrashSensitiveDataSink sensitiveData, Func<string> resolvePath)
    {
        try
        {
            RegisterPath(sensitiveData, resolvePath());
        }
        catch (Exception exception) when (IsExpectedPathException(exception))
        {
        }
    }

    private static void RegisterPath(ICrashSensitiveDataSink sensitiveData, string? path)
    {
        try
        {
            sensitiveData.RegisterPath(path);
        }
        catch (Exception exception) when (IsExpectedPathException(exception))
        {
        }
    }

    private static bool IsExpectedPathException(Exception exception)
    {
        return exception is ArgumentException
            or IOException
            or NotSupportedException
            or PathTooLongException
            or UnauthorizedAccessException
            or SecurityException
            or InvalidOperationException;
    }
}
