using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Stage;
using Microsoft.Extensions.Logging;

namespace DTXMania.Test.CrashReporting;

[Trait("Category", "Unit")]
public sealed class CrashLogBufferProviderTests
{
    [Fact]
    public void UnknownRenderedMessage_ShouldBeOmitted()
    {
        using var provider = new CrashLogBufferProvider(
            CrashLogFieldPolicy.Default,
            TimeProvider.System,
            capacity: 8);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var logger = factory.CreateLogger("test");

        logger.LogInformation($"Loaded song Secret Song Name");

        var record = Assert.Single(provider.Snapshot());
        Assert.Equal("[UNCLASSIFIED MESSAGE OMITTED]", record.MessageTemplate);
        Assert.Empty(record.Properties);
    }

    [Fact]
    public void UnknownStringValue_ShouldBeRedactedByCentralPolicy()
    {
        var normalized = CrashLogFieldPolicy.Default.NormalizeProperty(
            propertyName: "Status",
            value: "Secret Song");

        Assert.Equal("[REDACTED]", normalized);
    }

    [Fact]
    public void Capacity_ShouldDropOldestRecords()
    {
        using var provider = new CrashLogBufferProvider(
            CrashLogFieldPolicy.Default,
            TimeProvider.System,
            capacity: 2);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var logger = factory.CreateLogger("test");

        LogSafeStage(logger, StageType.Startup);
        LogSafeStage(logger, StageType.Title);
        LogSafeStage(logger, StageType.Config);

        var records = provider.Snapshot();

        Assert.Collection(
            records,
            record => Assert.Equal(StageType.Title, record.Properties["Stage"]),
            record => Assert.Equal(StageType.Config, record.Properties["Stage"]));
    }

    [Fact]
    public void Snapshot_ShouldReturnAnIndependentCopy()
    {
        using var provider = new CrashLogBufferProvider(
            CrashLogFieldPolicy.Default,
            TimeProvider.System,
            capacity: 2);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var logger = factory.CreateLogger("test");

        LogSafeStage(logger, StageType.Startup);
        var snapshot = provider.Snapshot();
        LogSafeStage(logger, StageType.Title);

        var record = Assert.Single(snapshot);
        Assert.Equal(StageType.Startup, record.Properties["Stage"]);
    }

    [Fact]
    public void ScalarValues_ShouldNormalizeDeterministically()
    {
        var policy = CrashLogFieldPolicy.Default;
        var timestamp = new DateTimeOffset(2026, 8, 2, 15, 30, 0, TimeSpan.FromHours(2));

        Assert.Equal(42, policy.NormalizeProperty("Count", 42));
        Assert.Equal(true, policy.NormalizeProperty("Enabled", true));
        Assert.Equal(StageType.Title, policy.NormalizeProperty("Stage", StageType.Title));

        var normalizedTimestamp = Assert.IsType<DateTimeOffset>(
            policy.NormalizeProperty("Milestone", timestamp));
        Assert.Equal(timestamp.ToUniversalTime(), normalizedTimestamp);
        Assert.Equal(TimeSpan.Zero, normalizedTimestamp.Offset);
    }

    [Fact]
    public void EventTemplateMismatch_ShouldBeOmitted()
    {
        using var provider = new CrashLogBufferProvider(
            CrashLogFieldPolicy.Default,
            TimeProvider.System,
            capacity: 8);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var logger = factory.CreateLogger("test");

        logger.LogInformation(
            new EventId(5100, "crash_safe_stage"),
            "Unsafe replacement template {Stage}",
            StageType.Title);

        var record = Assert.Single(provider.Snapshot());
        Assert.Equal("[UNCLASSIFIED MESSAGE OMITTED]", record.MessageTemplate);
        Assert.Empty(record.Properties);
    }

    [Fact]
    public void EventNameMismatch_ShouldBeOmitted()
    {
        using var provider = new CrashLogBufferProvider(
            CrashLogFieldPolicy.Default,
            TimeProvider.System,
            capacity: 8);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var logger = factory.CreateLogger("test");

        logger.LogInformation(
            new EventId(5100, "secret_event_name"),
            "Crash-safe stage changed to {Stage}",
            StageType.Title);

        var record = Assert.Single(provider.Snapshot());
        Assert.Equal("[UNCLASSIFIED MESSAGE OMITTED]", record.MessageTemplate);
        Assert.Empty(record.Properties);
    }

    [Fact]
    public void UnknownEventId_ShouldNotRetainCallerControlledNumericId()
    {
        using var provider = new CrashLogBufferProvider(
            CrashLogFieldPolicy.Default,
            TimeProvider.System,
            capacity: 8);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var logger = factory.CreateLogger("test");

        logger.LogInformation(
            new EventId(36, "midi_note_36"),
            "Unknown lifecycle event {Stage}",
            StageType.Title);

        var record = Assert.Single(provider.Snapshot());
        Assert.Equal(0, record.EventId.Id);
        Assert.Null(record.EventId.Name);
        Assert.Equal("[UNCLASSIFIED MESSAGE OMITTED]", record.MessageTemplate);
        Assert.Empty(record.Properties);
    }

    [Fact]
    public void ExceptionType_ShouldBeRetainedWithoutItsMessage()
    {
        using var provider = new CrashLogBufferProvider(
            CrashLogFieldPolicy.Default,
            TimeProvider.System,
            capacity: 8);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var logger = factory.CreateLogger("test");
        var exception = new InvalidOperationException("Secret exception message");

        logger.LogError(
            new EventId(5100, "crash_safe_stage"),
            exception,
            "Crash-safe stage changed to {Stage}",
            StageType.Title);

        var record = Assert.Single(provider.Snapshot());
        Assert.Equal(typeof(InvalidOperationException).FullName, record.ExceptionType);
        Assert.DoesNotContain("Secret exception message", record.MessageTemplate);
        Assert.DoesNotContain(
            record.Properties.Values,
            static value => value is string text && text.Contains("Secret exception message", StringComparison.Ordinal));
    }

    [Fact]
    public void KnownEvent_ShouldCopyOnlyAllowlistedNormalizedPropertiesWithoutRendering()
    {
        using var provider = new CrashLogBufferProvider(
            CrashLogFieldPolicy.Default,
            TimeProvider.System,
            capacity: 8);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var logger = factory.CreateLogger("test");

        logger.Log(
            LogLevel.Information,
            new EventId(5100, "crash_safe_stage"),
            new Dictionary<string, object?>
            {
                ["Stage"] = StageType.Title,
                ["Status"] = "Secret Song",
                ["SongTitle"] = "Secret Song Name",
                ["{OriginalFormat}"] = "Crash-safe stage changed to {Stage}"
            },
            exception: null,
            static (_, _) => throw new InvalidOperationException("Formatter must not be rendered."));

        var record = Assert.Single(provider.Snapshot());
        Assert.Equal("Crash-safe stage changed to {Stage}", record.MessageTemplate);
        Assert.Equal(StageType.Title, record.Properties["Stage"]);
        Assert.Equal("[REDACTED]", record.Properties["Status"]);
        Assert.False(record.Properties.ContainsKey("SongTitle"));
    }

    private static void LogSafeStage(ILogger logger, StageType stage)
    {
        logger.LogInformation(
            new EventId(5100, "crash_safe_stage"),
            "Crash-safe stage changed to {Stage}",
            stage);
    }
}
