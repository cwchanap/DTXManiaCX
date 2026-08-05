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
    public void UnclassifiedRecord_ShouldRetainBoundedLoggerCategory()
    {
        using var provider = new CrashLogBufferProvider(
            CrashLogFieldPolicy.Default,
            TimeProvider.System,
            capacity: 8);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var logger = factory.CreateLogger("DTXMania.Game.Lib.Graphics.GraphicsManager");

        // Non-structured (interpolated) messages are unclassified, so the category is the
        // only signal identifying the originating subsystem.
        logger.LogWarning($"rendered graphics warning");

        var record = Assert.Single(provider.Snapshot());
        Assert.Equal("[UNCLASSIFIED MESSAGE OMITTED]", record.MessageTemplate);
        Assert.Equal("DTXMania.Game.Lib.Graphics.GraphicsManager", record.Category);
    }

    [Fact]
    public void UnclassifiedRecord_WithOverlongCategory_ShouldBoundToLimit()
    {
        using var provider = new CrashLogBufferProvider(
            CrashLogFieldPolicy.Default,
            TimeProvider.System,
            capacity: 8);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var overlongCategory = new string('x', 200);
        var logger = factory.CreateLogger(overlongCategory);

        logger.LogInformation($"rendered message");

        var record = Assert.Single(provider.Snapshot());
        Assert.True(record.Category.Length <= 64);
        Assert.Equal(new string('x', 64), record.Category);
    }

    [Fact]
    public void UnclassifiedRecord_WithNewlinesInCategory_ShouldCollapseToSingleLine()
    {
        using var provider = new CrashLogBufferProvider(
            CrashLogFieldPolicy.Default,
            TimeProvider.System,
            capacity: 8);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var logger = factory.CreateLogger("DTXMania.Game\nLib.Input");

        logger.LogInformation($"rendered message");

        var record = Assert.Single(provider.Snapshot());
        Assert.DoesNotContain('\n', record.Category);
        Assert.Contains("DTXMania.Game Lib.Input", record.Category);
    }

    /// <summary>
    /// Collapses <see cref="CrashLogFieldPolicy.TryNormalizeProperty"/> into a single value so the
    /// allowlist and the scalar-normalization rules can be asserted in one expression.
    /// </summary>
    private static object? Normalize(string propertyName, object? value)
    {
        return CrashLogFieldPolicy.Default.TryNormalizeProperty(propertyName, value, out var normalized)
            ? normalized
            : CrashLogFieldPolicy.RedactedValue;
    }

    [Fact]
    public void UnknownStringValue_ShouldBeRedactedByCentralPolicy()
    {
        var normalized = Normalize(
            propertyName: "Milestone",
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

        LogStageTransition(logger, StageType.Startup);
        LogStageTransition(logger, StageType.Title);
        LogStageTransition(logger, StageType.Config);

        var records = provider.Snapshot();

        Assert.Collection(
            records,
            record => Assert.Equal(StageType.Title, record.Properties["TargetStage"]),
            record => Assert.Equal(StageType.Config, record.Properties["TargetStage"]));
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

        LogStageTransition(logger, StageType.Startup);
        var snapshot = provider.Snapshot();
        LogStageTransition(logger, StageType.Title);

        var record = Assert.Single(snapshot);
        Assert.Equal(StageType.Startup, record.Properties["TargetStage"]);
    }

    [Fact]
    public void ScalarValues_ShouldNormalizeDeterministically()
    {
        var policy = CrashLogFieldPolicy.Default;
        var timestamp = new DateTimeOffset(2026, 8, 2, 15, 30, 0, TimeSpan.FromHours(2));

        Assert.Equal(42, Normalize("MidiDeviceCount", 42));
        Assert.Equal(true, Normalize("Fullscreen", true));
        Assert.Equal(StageType.Title, Normalize("TargetStage", StageType.Title));

        var normalizedTimestamp = Assert.IsType<DateTimeOffset>(
            Normalize("Milestone", timestamp));
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
            CrashLogEvents.StageTransitionCompleted.EventId,
            "Unsafe replacement template {TargetStage}",
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
            new EventId(CrashLogEvents.StageTransitionCompleted.Id, "secret_event_name"),
            CrashLogEvents.StageTransitionCompleted.MessageTemplate,
            StageType.Startup,
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
            CrashLogEvents.StageTransitionCompleted.EventId,
            exception,
            CrashLogEvents.StageTransitionCompleted.MessageTemplate,
            StageType.Startup,
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
            CrashLogEvents.StageTransitionCompleted.EventId,
            new Dictionary<string, object?>
            {
                ["TargetStage"] = StageType.Title,
                ["Milestone"] = "Secret Song",
                ["SongTitle"] = "Secret Song Name",
                ["{OriginalFormat}"] = CrashLogEvents.StageTransitionCompleted.MessageTemplate
            },
            exception: null,
            static (_, _) => throw new InvalidOperationException("Formatter must not be rendered."));

        var record = Assert.Single(provider.Snapshot());
        Assert.Equal(CrashLogEvents.StageTransitionCompleted.MessageTemplate, record.MessageTemplate);
        Assert.Equal(StageType.Title, record.Properties["TargetStage"]);
        Assert.Equal("[REDACTED]", record.Properties["Milestone"]);
        Assert.False(record.Properties.ContainsKey("SongTitle"));
    }

    [Fact]
    public void Constructor_WithNullPolicy_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(
            () => new CrashLogBufferProvider(null!, TimeProvider.System, capacity: 8));
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(
            () => new CrashLogBufferProvider(CrashLogFieldPolicy.Default, null!, capacity: 8));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithNonPositiveCapacity_ShouldThrow(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CrashLogBufferProvider(CrashLogFieldPolicy.Default, TimeProvider.System, capacity));
    }

    [Fact]
    public void Log_WithLogLevelNone_ShouldBeSkipped()
    {
        using var provider = new CrashLogBufferProvider(
            CrashLogFieldPolicy.Default,
            TimeProvider.System,
            capacity: 8);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var logger = factory.CreateLogger("test");

        logger.Log(
            LogLevel.None,
            new EventId(5100, "crash_safe_stage"),
            new Dictionary<string, object?>
            {
                ["Stage"] = StageType.Title,
                ["{OriginalFormat}"] = "Crash-safe stage changed to {Stage}"
            },
            exception: null,
            static (_, _) => "unused");

        Assert.Empty(provider.Snapshot());
    }

    [Fact]
    public void Log_AfterDispose_ShouldNotRecord()
    {
        var provider = new CrashLogBufferProvider(
            CrashLogFieldPolicy.Default,
            TimeProvider.System,
            capacity: 8);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var logger = factory.CreateLogger("test");

        provider.Dispose();
        LogStageTransition(logger, StageType.Title);

        Assert.Empty(provider.Snapshot());
    }

    [Fact]
    public void Log_WithNonStructuredState_ShouldBeOmitted()
    {
        using var provider = new CrashLogBufferProvider(
            CrashLogFieldPolicy.Default,
            TimeProvider.System,
            capacity: 8);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var logger = factory.CreateLogger("test");

        // A non-IEnumerable<KeyValuePair<string,object?>> state triggers the unclassified path.
        logger.Log(
            LogLevel.Information,
            new EventId(5100, "crash_safe_stage"),
            state: 42,
            exception: null,
            static (_, _) => "non-structured state");

        var record = Assert.Single(provider.Snapshot());
        Assert.Equal("[UNCLASSIFIED MESSAGE OMITTED]", record.MessageTemplate);
        Assert.Empty(record.Properties);
    }

    [Fact]
    public void IsEnabled_WithLogLevelNone_ShouldReturnFalse()
    {
        using var provider = new CrashLogBufferProvider(
            CrashLogFieldPolicy.Default,
            TimeProvider.System,
            capacity: 8);
        var logger = provider.CreateLogger("test");

        Assert.False(logger.IsEnabled(LogLevel.None));
        Assert.True(logger.IsEnabled(LogLevel.Information));
    }

    [Fact]
    public void BeginScope_ShouldReturnDisposableWithoutThrowing()
    {
        using var provider = new CrashLogBufferProvider(
            CrashLogFieldPolicy.Default,
            TimeProvider.System,
            capacity: 8);
        var logger = provider.CreateLogger("test");

        using var scope = logger.BeginScope("test-scope");

        Assert.NotNull(scope);
    }

    [Fact]
    public void Log_WithExceptionButNoStructuredFormat_ShouldRecordExceptionType()
    {
        using var provider = new CrashLogBufferProvider(
            CrashLogFieldPolicy.Default,
            TimeProvider.System,
            capacity: 8);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var logger = factory.CreateLogger("test");

        logger.LogError(
            new EventId(9999, "unknown"),
            new InvalidOperationException("secret"),
            "Unknown event {Stage}",
            StageType.Title);

        var record = Assert.Single(provider.Snapshot());
        Assert.Equal(typeof(InvalidOperationException).FullName, record.ExceptionType);
        Assert.Equal("[UNCLASSIFIED MESSAGE OMITTED]", record.MessageTemplate);
    }

    private static void LogStageTransition(ILogger logger, StageType stage)
    {
        logger.LogCrashEvent(
            LogLevel.Information,
            CrashLogEvents.StageTransitionCompleted,
            StageType.Startup,
            stage);
    }
}
