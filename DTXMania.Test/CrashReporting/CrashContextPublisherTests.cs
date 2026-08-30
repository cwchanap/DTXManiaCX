#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Graphics;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Utilities;
using DTXMania.Test.TestData;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Test.CrashReporting;

[Trait("Category", "Unit")]
public sealed class CrashContextPublisherTests
{
    [Fact]
    public void PublishProcessAndApplication_ShouldSetProcessAndApplicationContexts()
    {
        var diagnostics = new RecordingGameCrashDiagnostics();
        var processStart = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

        CrashContextPublisher.PublishProcessAndApplication(diagnostics, processStart);

        var process = Assert.Single(diagnostics.Contexts.Snapshots, s => s.Kind == CrashContextKind.Process);
        Assert.Equal(CrashContextStatus.Available, process.Status);
        Assert.True(process.Fields.ContainsKey("RuntimeFramework"));
        Assert.True(process.Fields.ContainsKey("OperatingSystem"));
        Assert.True(process.Fields.ContainsKey("ProcessArchitecture"));
        Assert.True(process.Fields.ContainsKey("ProcessStartUtc"));

        var application = Assert.Single(diagnostics.Contexts.Snapshots, s => s.Kind == CrashContextKind.Application);
        Assert.True(application.Fields.ContainsKey("ApplicationVersion"));
    }

    [Fact]
    public void PublishProcessAndApplication_WithNullDiagnostics_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(
            () => CrashContextPublisher.PublishProcessAndApplication(
                null!,
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void PublishConfiguration_ShouldSetConfigurationContext()
    {
        var diagnostics = new RecordingGameCrashDiagnostics();
        var config = new ConfigData
        {
            ScreenWidth = 1920,
            ScreenHeight = 1080,
            FullScreen = true,
            VSyncWait = false,
            NoFail = true,
            EnableGameApi = true
        };
        config.KeyBindings["Snare"] = 1;
        config.SystemKeyBindings["Exit"] = "Escape";
        config.UnboundDrumLanes.Add(2);
        config.UnboundDrumButtons.Add("ButtonA");
        config.MidiVelocityThresholds[36] = 10;
        config.AutoPlayLanes.Add(0);
        config.AutoPlayLanes.Add(3);

        CrashContextPublisher.PublishConfiguration(diagnostics, config);

        var configuration = Assert.Single(diagnostics.Contexts.Snapshots, s => s.Kind == CrashContextKind.Configuration);
        Assert.Equal(1920, configuration.Fields["ScreenWidth"]);
        Assert.Equal(1080, configuration.Fields["ScreenHeight"]);
        Assert.Equal(true, configuration.Fields["FullScreen"]);
        Assert.Equal(false, configuration.Fields["VSyncWait"]);
        Assert.Equal(2, configuration.Fields["AutoPlayLaneCount"]);
        Assert.Equal(true, configuration.Fields["NoFail"]);
        Assert.Equal(true, configuration.Fields["EnableGameApi"]);
        Assert.Equal(1, configuration.Fields["KeyBindingCount"]);
        Assert.Equal(1, configuration.Fields["SystemKeyBindingCount"]);
        Assert.Equal(1, configuration.Fields["UnboundDrumLaneCount"]);
        Assert.Equal(1, configuration.Fields["UnboundDrumButtonCount"]);
        Assert.Equal(1, configuration.Fields["MidiVelocityThresholdCount"]);
    }

    [Fact]
    public void PublishConfiguration_WithNullDiagnostics_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(
            () => CrashContextPublisher.PublishConfiguration(null!, new ConfigData()));
    }

    [Fact]
    public void PublishConfiguration_WithNullConfig_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(
            () => CrashContextPublisher.PublishConfiguration(new RecordingGameCrashDiagnostics(), null!));
    }

    [Fact]
    public void PublishGraphics_ShouldSetGraphicsContext()
    {
        var diagnostics = new RecordingGameCrashDiagnostics();
        var settings = new GraphicsSettings
        {
            Width = 1920,
            Height = 1080,
            IsFullscreen = true,
            VSync = false,
            BackBufferFormat = SurfaceFormat.Color,
            DepthStencilFormat = DepthFormat.Depth24,
            MultiSampleCount = 4
        };

        CrashContextPublisher.PublishGraphics(diagnostics, settings, isDeviceAvailable: true);

        var graphics = Assert.Single(diagnostics.Contexts.Snapshots, s => s.Kind == CrashContextKind.Graphics);
        Assert.Equal(CrashContextStatus.Available, graphics.Status);
        Assert.Equal(1920, graphics.Fields["Width"]);
        Assert.Equal(1080, graphics.Fields["Height"]);
        Assert.Equal(true, graphics.Fields["Fullscreen"]);
        Assert.Equal(false, graphics.Fields["VSync"]);
        Assert.Equal(SurfaceFormat.Color, graphics.Fields["BackBufferFormat"]);
        Assert.Equal(DepthFormat.Depth24, graphics.Fields["DepthStencilFormat"]);
        Assert.Equal(4, graphics.Fields["MultiSampleCount"]);
        Assert.Equal(true, graphics.Fields["DeviceAvailable"]);
    }

    [Fact]
    public void PublishGraphics_WithNullDiagnostics_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(
            () => CrashContextPublisher.PublishGraphics(null!, new GraphicsSettings(), true));
    }

    [Fact]
    public void PublishGraphics_WithNullSettings_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(
            () => CrashContextPublisher.PublishGraphics(new RecordingGameCrashDiagnostics(), null!, true));
    }

    [Fact]
    public void PublishAudioUnavailable_ShouldSetAudioUnavailableContext()
    {
        var diagnostics = new RecordingGameCrashDiagnostics();

        CrashContextPublisher.PublishAudioUnavailable(diagnostics);

        var audio = Assert.Single(diagnostics.Contexts.Snapshots, s => s.Kind == CrashContextKind.Audio);
        Assert.Equal(CrashContextStatus.Unavailable, audio.Status);
        Assert.Equal(CrashContextPublisher.AudioDeviceSummaryUnavailable, audio.FailureCode);
    }

    [Fact]
    public void PublishAudioUnavailable_WithNullDiagnostics_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => CrashContextPublisher.PublishAudioUnavailable(null!));
    }

    [Fact]
    public void PublishInput_WithPositiveCount_ShouldSetInputContext()
    {
        var diagnostics = new RecordingGameCrashDiagnostics();

        CrashContextPublisher.PublishInput(diagnostics, midiDeviceCount: 3);

        var input = Assert.Single(diagnostics.Contexts.Snapshots, s => s.Kind == CrashContextKind.Input);
        Assert.Equal(3, input.Fields["MidiDeviceCount"]);
    }

    [Fact]
    public void PublishInput_WithNegativeCount_ShouldClampToZero()
    {
        var diagnostics = new RecordingGameCrashDiagnostics();

        CrashContextPublisher.PublishInput(diagnostics, midiDeviceCount: -5);

        var input = Assert.Single(diagnostics.Contexts.Snapshots, s => s.Kind == CrashContextKind.Input);
        Assert.Equal(0, input.Fields["MidiDeviceCount"]);
    }

    [Fact]
    public void PublishInput_WithNullDiagnostics_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => CrashContextPublisher.PublishInput(null!, 0));
    }

    [Fact]
    public void PublishStage_ShouldSetStageContext()
    {
        var contexts = new RecordingContextSink();

        CrashContextPublisher.PublishStage(contexts, StageType.Title, stageCount: 3);

        var stage = Assert.Single(contexts.Snapshots);
        Assert.Equal(CrashContextKind.Stage, stage.Kind);
        Assert.Equal(StageType.Title, stage.Fields["Stage"]);
        Assert.Equal(3, stage.Fields["StageCount"]);
    }

    [Fact]
    public void PublishStage_WithNegativeCount_ShouldClampToZero()
    {
        var contexts = new RecordingContextSink();

        CrashContextPublisher.PublishStage(contexts, StageType.Title, stageCount: -1);

        Assert.Equal(0, contexts.Snapshots[0].Fields["StageCount"]);
    }

    [Fact]
    public void PublishStage_WithNullContexts_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(
            () => CrashContextPublisher.PublishStage(null!, StageType.Title, 1));
    }

    [Fact]
    public void PublishStartupMilestone_WithNullDiagnostics_ShouldReturnWithoutThrowing()
    {
        var exception = Record.Exception(
            () => CrashContextPublisher.PublishStartupMilestone(
                null,
                StartupCriticalPathMilestone.StartupActivation));

        Assert.Null(exception);
    }

    [Fact]
    public void PublishStartupMilestone_WithDiagnostics_ShouldSetStartupContextAndBreadcrumb()
    {
        var diagnostics = new RecordingGameCrashDiagnostics();

        CrashContextPublisher.PublishStartupMilestone(
            diagnostics,
            StartupCriticalPathMilestone.StartupActivation);

        var startup = Assert.Single(diagnostics.Contexts.Snapshots, s => s.Kind == CrashContextKind.Startup);
        Assert.Equal(StartupCriticalPathMilestone.StartupActivation, startup.Fields["Milestone"]);

        var breadcrumb = Assert.Single(diagnostics.Breadcrumbs.Events);
        Assert.Equal("initialization_milestone_reached", breadcrumb.EventName);
    }

    [Fact]
    public void RegisterSensitivePrefixes_ShouldRegisterSongRootsSkinAndAppPaths()
    {
        var diagnostics = new RecordingGameCrashDiagnostics();
        var tempRoot = Path.Combine(Path.GetTempPath(), "dtx-publisher-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var songsPath = Path.Combine(tempRoot, "songs");
            var config = new ConfigData
            {
                SkinPath = Path.Combine(tempRoot, "skins", "MySkin")
            };
            config.SongRoots.Add(songsPath);

            CrashContextPublisher.RegisterSensitivePrefixes(diagnostics, config);

            Assert.Contains(songsPath, diagnostics.SensitiveData.Paths);
            Assert.Contains(diagnostics.SensitiveData.Paths, p => p == config.SkinPath);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void RegisterSensitivePrefixes_ShouldRegisterBothConfigDatabaseAndLegacyIniPaths()
    {
        // HPA-190: the live store is config.db and Config.ini remains on disk
        // as the legacy import input — both paths can contain identifying
        // directory names and must be redacted from crash reports.
        var diagnostics = new RecordingGameCrashDiagnostics();

        CrashContextPublisher.RegisterSensitivePrefixes(diagnostics, new ConfigData());

        Assert.Contains(AppPaths.GetConfigDatabasePath(), diagnostics.SensitiveData.Paths);
        Assert.Contains(AppPaths.GetLegacyConfigFilePath(), diagnostics.SensitiveData.Paths);
    }

    [Fact]
    public void RegisterSensitivePrefixes_WithNullDiagnostics_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(
            () => CrashContextPublisher.RegisterSensitivePrefixes(null!, new ConfigData()));
    }

    [Fact]
    public void RegisterSensitivePrefixes_WithNullConfig_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(
            () => CrashContextPublisher.RegisterSensitivePrefixes(new RecordingGameCrashDiagnostics(), null!));
    }
}
