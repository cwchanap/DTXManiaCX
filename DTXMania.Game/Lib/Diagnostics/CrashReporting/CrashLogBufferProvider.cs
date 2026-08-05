#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

internal sealed record CrashLogRecord(
    DateTimeOffset TimestampUtc,
    LogLevel LogLevel,
    EventId EventId,
    string MessageTemplate,
    IReadOnlyDictionary<string, object?> Properties,
    string? ExceptionType,
    string Category = "");

internal sealed class CrashLogBufferProvider : ILoggerProvider
{
    private const int MaximumCategoryLength = 64;

    private readonly object _gate = new();
    private readonly CrashLogFieldPolicy _policy;
    private readonly TimeProvider _timeProvider;
    private readonly int _capacity;
    private readonly Queue<CrashLogRecord> _records = new();
    private bool _disposed;

    internal CrashLogBufferProvider(CrashLogFieldPolicy policy, TimeProvider timeProvider, int capacity)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public ILogger CreateLogger(string categoryName)
    {
        // Retain a bounded category so unclassified records still identify their originating
        // subsystem (graphics, input, JSON-RPC, …) instead of all collapsing to the same
        // anonymous [UNCLASSIFIED MESSAGE OMITTED] entry.
        var boundedCategory = BoundCategory(categoryName);
        return new CrashLogBufferLogger(this, boundedCategory);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }
    }

    internal IReadOnlyList<CrashLogRecord> Snapshot()
    {
        lock (_gate)
        {
            return _records.ToArray();
        }
    }

    private void Record<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, string category)
    {
        if (logLevel == LogLevel.None)
        {
            return;
        }

        try
        {
            var hasOriginalFormat = TryReadStructuredState(state, out var originalFormat, out var rawProperties);
            if (!hasOriginalFormat
                || !_policy.TryClassify(eventId, originalFormat, out var safeEventId, out var safeMessageTemplate))
            {
                Append(CreateUnclassifiedRecord(logLevel, exception, category));
                return;
            }

            var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in rawProperties)
            {
                if (_policy.TryNormalizeProperty(property.Key, property.Value, out var normalizedValue))
                {
                    properties[property.Key] = normalizedValue;
                }
            }

            Append(new CrashLogRecord(
                _timeProvider.GetUtcNow(),
                logLevel,
                safeEventId,
                safeMessageTemplate,
                FreezeProperties(properties),
                GetExceptionType(exception),
                category));
        }
        catch (Exception)
        {
            Append(CreateUnclassifiedRecord(logLevel, exception, category));
        }
    }

    private CrashLogRecord CreateUnclassifiedRecord(LogLevel logLevel, Exception? exception, string category)
    {
        return new CrashLogRecord(
            _timeProvider.GetUtcNow(),
            logLevel,
            CrashLogFieldPolicy.UnclassifiedEventId,
            CrashLogFieldPolicy.UnclassifiedMessageTemplate,
            ReadOnlyDictionary<string, object?>.Empty,
            GetExceptionType(exception),
            category);
    }

    private static string BoundCategory(string? categoryName)
    {
        if (string.IsNullOrEmpty(categoryName))
        {
            return string.Empty;
        }

        // Collapse newlines so the category cannot break the line-oriented report format.
        var collapsed = categoryName.ReplaceLineEndings(" ");
        return collapsed.Length <= MaximumCategoryLength
            ? collapsed
            : collapsed[..MaximumCategoryLength];
    }

    private void Append(CrashLogRecord record)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_records.Count == _capacity)
            {
                _records.Dequeue();
            }

            _records.Enqueue(record);
        }
    }

    private bool TryReadStructuredState<TState>(
        TState state,
        out string? originalFormat,
        out List<KeyValuePair<string, object?>> rawProperties)
    {
        originalFormat = null;
        rawProperties = new List<KeyValuePair<string, object?>>();

        if (state is not IEnumerable<KeyValuePair<string, object?>> structuredState)
        {
            return false;
        }

        foreach (var property in structuredState)
        {
            if (string.Equals(property.Key, "{OriginalFormat}", StringComparison.Ordinal))
            {
                originalFormat = property.Value as string;
            }
            else
            {
                rawProperties.Add(property);
            }
        }

        return originalFormat is not null;
    }

    private string? GetExceptionType(Exception? exception)
    {
        return exception is null ? null : _policy.NormalizeExceptionType(exception.GetType());
    }

    private static IReadOnlyDictionary<string, object?> FreezeProperties(Dictionary<string, object?> properties)
    {
        return properties.Count == 0
            ? ReadOnlyDictionary<string, object?>.Empty
            : new ReadOnlyDictionary<string, object?>(properties);
    }

    private sealed class CrashLogBufferLogger : ILogger
    {
        private readonly CrashLogBufferProvider _provider;
        private readonly string _category;

        internal CrashLogBufferLogger(CrashLogBufferProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NoopScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _provider.Record(logLevel, eventId, state, exception, _category);
        }
    }

    private sealed class NoopScope : IDisposable
    {
        internal static NoopScope Instance { get; } = new();

        private NoopScope()
        {
        }

        public void Dispose()
        {
        }
    }
}
