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
}
