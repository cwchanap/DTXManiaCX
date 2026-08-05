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

    [Fact]
    public void MidiDeviceCountChanged_ShouldRetainOnlyTheSafeCount()
    {
        var buffer = new CrashBreadcrumbBuffer(TimeProvider.System, capacity: 2);

        buffer.Record(
            "midi_device_count_changed",
            new Dictionary<string, object?>
            {
                ["MidiDeviceCount"] = 2,
                ["MidiDeviceName"] = "Secret MIDI Device"
            });

        var breadcrumb = Assert.Single(buffer.Snapshot());

        Assert.Equal("midi_device_count_changed", breadcrumb.EventName);
        Assert.Equal(2, breadcrumb.Properties["MidiDeviceCount"]);
        Assert.False(breadcrumb.Properties.ContainsKey("MidiDeviceName"));
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(
            () => new CrashBreadcrumbBuffer(null!, capacity: 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithNonPositiveCapacity_ShouldThrow(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CrashBreadcrumbBuffer(TimeProvider.System, capacity));
    }

    [Fact]
    public void Record_WithNullProperties_ShouldRetainEventName()
    {
        var buffer = new CrashBreadcrumbBuffer(TimeProvider.System, capacity: 2);

        buffer.Record("stage_transition_completed", null);

        var breadcrumb = Assert.Single(buffer.Snapshot());
        Assert.Equal("stage_transition_completed", breadcrumb.EventName);
        Assert.Empty(breadcrumb.Properties);
    }

    [Fact]
    public void Record_WithEmptyProperties_ShouldRetainEventName()
    {
        var buffer = new CrashBreadcrumbBuffer(TimeProvider.System, capacity: 2);

        buffer.Record("exit_requested", new Dictionary<string, object?>());

        var breadcrumb = Assert.Single(buffer.Snapshot());
        Assert.Equal("exit_requested", breadcrumb.EventName);
        Assert.Empty(breadcrumb.Properties);
    }

    [Fact]
    public void Capacity_ShouldDropOldestBreadcrumbs()
    {
        var buffer = new CrashBreadcrumbBuffer(TimeProvider.System, capacity: 2);

        buffer.Record("process_started");
        buffer.Record("exit_requested");
        buffer.Record("graphics_device_lost");

        var breadcrumbs = buffer.Snapshot();
        Assert.Equal(2, breadcrumbs.Count);
        Assert.Equal("exit_requested", breadcrumbs[0].EventName);
        Assert.Equal("graphics_device_lost", breadcrumbs[1].EventName);
    }

    [Fact]
    public void Record_WithKnownEventButAllUnsafeProperties_ShouldRetainEventWithEmptyProperties()
    {
        var buffer = new CrashBreadcrumbBuffer(TimeProvider.System, capacity: 2);

        buffer.Record(
            "graphics_settings_changed",
            new Dictionary<string, object?>
            {
                ["SongTitle"] = "Secret Song",
                ["DeviceName"] = "Secret Device"
            });

        var breadcrumb = Assert.Single(buffer.Snapshot());
        Assert.Equal("graphics_settings_changed", breadcrumb.EventName);
        Assert.Empty(breadcrumb.Properties);
    }
}
