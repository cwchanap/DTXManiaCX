using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Stage;

namespace DTXMania.Test.CrashReporting;

[Trait("Category", "Unit")]
public sealed class CrashBreadcrumbBufferTests
{
    [Fact]
    public void BreadcrumbSnapshot_ShouldCopyMutableBuffer()
    {
        var buffer = new CrashBreadcrumbBuffer(TimeProvider.System, capacity: 2);

        buffer.Record(
            "stage_transition_requested",
            new Dictionary<string, object?> { ["TargetStage"] = StageType.Title });
        var snapshot = buffer.Snapshot();
        buffer.Record(
            "stage_transition_completed",
            new Dictionary<string, object?> { ["TargetStage"] = StageType.Title });

        Assert.Single(snapshot);
        Assert.Equal("stage_transition_requested", snapshot[0].EventName);
    }

    [Fact]
    public void UnknownEvent_ShouldUseSafeNameAndDiscardCallerProperties()
    {
        var buffer = new CrashBreadcrumbBuffer(TimeProvider.System, capacity: 2);

        buffer.Record(
            "song_selected",
            new Dictionary<string, object?>
            {
                ["TargetStage"] = StageType.SongSelect,
                ["SongTitle"] = "Secret Song Name"
            });

        var breadcrumb = Assert.Single(buffer.Snapshot());
        Assert.Equal("unknown_event", breadcrumb.EventName);
        Assert.Empty(breadcrumb.Properties);
    }

    [Fact]
    public void KnownEvent_ShouldRetainOnlyAllowlistedNormalizedProperties()
    {
        var buffer = new CrashBreadcrumbBuffer(TimeProvider.System, capacity: 2);

        buffer.Record(
            "stage_transition_requested",
            new Dictionary<string, object?>
            {
                ["TargetStage"] = StageType.Title,
                ["Status"] = "Secret Song",
                ["SongTitle"] = "Secret Song Name"
            });

        var breadcrumb = Assert.Single(buffer.Snapshot());
        Assert.Equal(StageType.Title, breadcrumb.Properties["TargetStage"]);
        Assert.Equal("[REDACTED]", breadcrumb.Properties["Status"]);
        Assert.False(breadcrumb.Properties.ContainsKey("SongTitle"));
    }
}
