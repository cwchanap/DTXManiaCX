using System;
using System.Collections.Generic;
using System.IO;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Utilities;

namespace DTXMania.Test.CrashReporting;

[Trait("Category", "Unit")]
public sealed class CrashContextSnapshotStoreTests
{
    [Fact]
    public void ContextStore_ShouldReplaceOneSectionAtomically()
    {
        var store = new CrashContextSnapshotStore();

        store.SetSnapshot(new CrashContextSnapshot(
            CrashContextKind.Stage,
            CrashContextStatus.Available,
            new Dictionary<string, object?> { ["Stage"] = StageType.Startup }));
        store.SetSnapshot(new CrashContextSnapshot(
            CrashContextKind.Stage,
            CrashContextStatus.Available,
            new Dictionary<string, object?> { ["Stage"] = StageType.Title }));

        var stage = Assert.Single(store.Snapshot(), item => item.Kind == CrashContextKind.Stage);
        Assert.Equal(StageType.Title, stage.Fields["Stage"]);
    }

    [Fact]
    public void Snapshot_ShouldCopyInputFieldsAndReturnedSectionArray()
    {
        var store = new CrashContextSnapshotStore();
        var fields = new Dictionary<string, object?>
        {
            ["Stage"] = StageType.Startup,
            ["Status"] = "Secret Song",
            ["SongTitle"] = "Secret Song Name"
        };

        store.SetSnapshot(new CrashContextSnapshot(
            CrashContextKind.Stage,
            CrashContextStatus.Available,
            fields,
            FailureCode: "Secret failure detail"));
        var snapshot = store.Snapshot();
        fields["Stage"] = StageType.Title;
        store.SetSnapshot(new CrashContextSnapshot(
            CrashContextKind.Input,
            CrashContextStatus.NotInitialized,
            new Dictionary<string, object?>()));

        var stage = Assert.Single(snapshot);
        Assert.Equal(StageType.Startup, stage.Fields["Stage"]);
        Assert.Equal("[REDACTED]", stage.Fields["Status"]);
        Assert.False(stage.Fields.ContainsKey("SongTitle"));
        Assert.Equal("[REDACTED]", stage.FailureCode);
    }

    [Fact]
    public void SensitivePathSnapshot_ShouldNormalizeAndDeduplicatePaths()
    {
        var store = new CrashContextSnapshotStore();
        var root = Path.Combine(Path.GetTempPath(), "dtx-crash-path-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "skins");

        store.RegisterPath(path);
        store.RegisterPath(Path.Combine(root, "skins", "..", "skins"));
        store.RegisterPath(null);

        var paths = store.SensitivePathSnapshot();

        Assert.Single(paths);
        Assert.Equal(Path.GetFullPath(path), paths[0]);
        Assert.True(AppPaths.SkinPathComparer.Equals(paths[0], Path.GetFullPath(path)));
    }

    [Fact]
    public void AudioUnavailable_ShouldPreserveOnlyTheFixedFailureCode()
    {
        var store = new CrashContextSnapshotStore();

        store.SetSnapshot(new CrashContextSnapshot(
            CrashContextKind.Audio,
            CrashContextStatus.Unavailable,
            new Dictionary<string, object?> { ["DeviceName"] = "Secret audio device" },
            CrashContextPublisher.AudioDeviceSummaryUnavailable));

        var audio = Assert.Single(store.Snapshot());

        Assert.Empty(audio.Fields);
        Assert.Equal(CrashContextPublisher.AudioDeviceSummaryUnavailable, audio.FailureCode);
    }

    [Fact]
    public void SetSnapshot_WithNull_ShouldThrow()
    {
        var store = new CrashContextSnapshotStore();

        Assert.Throws<ArgumentNullException>(() => store.SetSnapshot(null!));
    }

    [Fact]
    public void Snapshot_WithEmptyStore_ShouldReturnEmptyArray()
    {
        var store = new CrashContextSnapshotStore();

        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void SensitivePathSnapshot_WithEmptyStore_ShouldReturnEmptyArray()
    {
        var store = new CrashContextSnapshotStore();

        Assert.Empty(store.SensitivePathSnapshot());
    }

    [Fact]
    public void RegisterPath_WithNullOrWhitespace_ShouldNotRegister()
    {
        var store = new CrashContextSnapshotStore();

        store.RegisterPath(null);
        store.RegisterPath("");
        store.RegisterPath("   ");

        Assert.Empty(store.SensitivePathSnapshot());
    }

    [Fact]
    public void RegisterPath_WithInvalidPath_ShouldNotThrow()
    {
        var store = new CrashContextSnapshotStore();

        var exception = Record.Exception(() => store.RegisterPath("invalid\0path"));

        Assert.Null(exception);
        Assert.Empty(store.SensitivePathSnapshot());
    }

    [Fact]
    public void SetSnapshot_WithProcessContext_ShouldNormalizeProcessFields()
    {
        var store = new CrashContextSnapshotStore();

        store.SetSnapshot(new CrashContextSnapshot(
            CrashContextKind.Process,
            CrashContextStatus.Available,
            new Dictionary<string, object?>
            {
                ["RuntimeFramework"] = ".NET 8.0",
                ["OperatingSystem"] = "macOS",
                ["CommandLine"] = "secret command line"
            }));

        var process = Assert.Single(store.Snapshot());
        Assert.Equal(".NET 8.0", process.Fields["RuntimeFramework"]);
        Assert.Equal("macOS", process.Fields["OperatingSystem"]);
        Assert.False(process.Fields.ContainsKey("CommandLine"));
    }

    [Fact]
    public void SetSnapshot_WithGraphicsContext_ShouldNormalizeGraphicsFields()
    {
        var store = new CrashContextSnapshotStore();

        store.SetSnapshot(new CrashContextSnapshot(
            CrashContextKind.Graphics,
            CrashContextStatus.Available,
            new Dictionary<string, object?>
            {
                ["Width"] = 1920,
                ["Height"] = 1080,
                ["Fullscreen"] = true,
                ["GraphicsSettings"] = "secret"
            }));

        var graphics = Assert.Single(store.Snapshot());
        Assert.Equal(1920, graphics.Fields["Width"]);
        Assert.Equal(1080, graphics.Fields["Height"]);
        Assert.Equal(true, graphics.Fields["Fullscreen"]);
        Assert.False(graphics.Fields.ContainsKey("GraphicsSettings"));
    }

    [Fact]
    public void SetSnapshot_WithConfigurationContext_ShouldNormalizeConfigurationFields()
    {
        var store = new CrashContextSnapshotStore();

        store.SetSnapshot(new CrashContextSnapshot(
            CrashContextKind.Configuration,
            CrashContextStatus.Available,
            new Dictionary<string, object?>
            {
                ["ScreenWidth"] = 1920,
                ["FullScreen"] = true,
                ["GameApiKey"] = "secret-key"
            }));

        var configuration = Assert.Single(store.Snapshot());
        Assert.Equal(1920, configuration.Fields["ScreenWidth"]);
        Assert.Equal(true, configuration.Fields["FullScreen"]);
        Assert.False(configuration.Fields.ContainsKey("GameApiKey"));
    }

    [Fact]
    public void SetSnapshot_WithNonAudioFailureCode_ShouldRedact()
    {
        var store = new CrashContextSnapshotStore();

        store.SetSnapshot(new CrashContextSnapshot(
            CrashContextKind.Graphics,
            CrashContextStatus.CollectionFailed,
            new Dictionary<string, object?>(),
            FailureCode: "graphics_secret_failure"));

        var graphics = Assert.Single(store.Snapshot());
        Assert.Equal("[REDACTED]", graphics.FailureCode);
    }

    [Fact]
    public void SetSnapshot_WithAudioNonCanonicalFailureCode_ShouldRedact()
    {
        var store = new CrashContextSnapshotStore();

        store.SetSnapshot(new CrashContextSnapshot(
            CrashContextKind.Audio,
            CrashContextStatus.Unavailable,
            new Dictionary<string, object?>(),
            FailureCode: "some_other_failure_code"));

        var audio = Assert.Single(store.Snapshot());
        Assert.Equal("[REDACTED]", audio.FailureCode);
    }
}
