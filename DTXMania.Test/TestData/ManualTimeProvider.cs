using System;

namespace DTXMania.Test.TestData;

/// <summary>
/// Deterministic <see cref="TimeProvider"/> for tests that need to advance the
/// clock manually. Shared between CrashReportStoreTests and
/// CrashReportIntegrationTests to eliminate duplicate nested definitions.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    internal ManualTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow.ToUniversalTime();
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    internal void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
}
