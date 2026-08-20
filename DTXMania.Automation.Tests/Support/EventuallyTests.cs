using System.Diagnostics;
using DTXMania.Automation.Support;

namespace DTXMania.Automation.Tests.Support;

public sealed class EventuallyTests
{
    [Fact]
    public async Task UntilAsync_ShouldReturnWhenPredicateEventuallySucceeds()
    {
        var attempt = 0;

        var result = await Eventually.UntilAsync(
            _ => Task.FromResult(++attempt),
            value => value >= 3,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(1),
            "value reaches three",
            CancellationToken.None);

        Assert.Equal(3, result);
    }

    [Fact]
    public async Task UntilAsync_Timeout_ShouldIncludeLastValue()
    {
        var attempt = 0;

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => Eventually.UntilAsync(
            _ => Task.FromResult(++attempt),
            _ => false,
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(1),
            "impossible value",
            CancellationToken.None));

        Assert.Contains("impossible value", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Last value:", exception.Message, StringComparison.Ordinal);
        Assert.True(attempt > 0);
    }

    [Fact]
    public async Task UntilAsync_TransientFailure_ShouldRetainLastException()
    {
        var attempt = 0;

        // Generous timeout relative to the interval so the failure attempt is
        // reached even on loaded CI runners, where Task.Delay can inflate far
        // beyond the requested interval. With 20ms the delay after the first
        // (successful) attempt could exhaust the budget before the throwing
        // attempt runs, leaving no last exception to retain; 500ms guarantees
        // both attempts land.
        var exception = await Assert.ThrowsAsync<TimeoutException>(() => Eventually.UntilAsync(
            _ =>
            {
                attempt++;
                if (attempt == 1)
                    return Task.FromResult("last-value");

                throw new InvalidOperationException("transient boom");
            },
            _ => false,
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(5),
            "transient operation",
            CancellationToken.None));

        Assert.Contains("Last value: last-value", exception.Message, StringComparison.Ordinal);
        Assert.Contains("transient boom", exception.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task UntilAsync_CallerCancellation_ShouldPropagateOperationCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Eventually.UntilAsync(
            _ => Task.FromResult(false),
            value => value,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(1),
            "cancelled operation",
            cancellation.Token));
    }

    [Fact]
    public async Task UntilAsync_WhenProbeNeverCompletes_ShouldHonorDeadlineInsteadOfHanging()
    {
        // A probe that never completes and ignores its token would, without a
        // per-probe deadline, hang the method indefinitely and ignore the
        // advertised timeout. The deadline must bound the probe.
        var neverCompletingSource = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var timeout = TimeSpan.FromMilliseconds(150);
        var interval = TimeSpan.FromMilliseconds(10);
        // Matches the observation window in ObserveProbeCompletionAsync.
        var observationWindow = TimeSpan.FromSeconds(1);

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(() =>
            Eventually.UntilAsync(
                token =>
                {
                    // Make the probe token-aware so we can assert it was canceled
                    // when the deadline fired, proving the probe was not left
                    // running unbounded.
                    token.Register(() => neverCompletingSource.TrySetCanceled(token));
                    return neverCompletingSource.Task;
                },
                _ => false,
                timeout,
                interval,
                "never-completing probe",
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));
        stopwatch.Stop();

        // UntilAsync must NOT await the probe observation — it should complete
        // before the additional observation window would have elapsed, proving
        // the observation is fire-and-forget.
        Assert.True(
            stopwatch.Elapsed < observationWindow,
            $"Expected UntilAsync to complete before the {observationWindow} observation window, but waited {stopwatch.Elapsed}.");

        // The probe cancellation is recorded separately — it happens in the
        // background after UntilAsync has already returned. Verify it propagates
        // without affecting the timing assertion above.
        try
        {
            await neverCompletingSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
            // Expected — the probe was canceled by the linked token source.
        }
        Assert.True(neverCompletingSource.Task.IsCanceled);
    }

    [Fact]
    public async Task UntilAsync_ProbeCanceledWithForeignToken_ShouldRetryUntilDeadline()
    {
        // A probe that throws OperationCanceledException using a token that is
        // NOT the caller's cancellationToken must be treated as a transient
        // failure and retried, not propagated as caller cancellation. This
        // proves the OCE filter (when cancellationToken.IsCancellationRequested)
        // lets foreign-token cancellations continue through the retry path.
        using var foreignTokenSource = new CancellationTokenSource();
        var attempt = 0;

        // The timeout is intentionally generous relative to the interval so the
        // retry count stays robust on loaded CI runners, where Task.Delay can
        // inflate far beyond the requested interval (timer coalescing plus
        // scheduling latency). With 50ms the first delay alone could exhaust
        // the budget and yield a single attempt; 500ms guarantees multiple
        // retries even under heavy inflation while still completing quickly.
        var exception = await Assert.ThrowsAsync<TimeoutException>(() => Eventually.UntilAsync<int>(
            _ =>
            {
                attempt++;
                throw new OperationCanceledException(foreignTokenSource.Token);
            },
            _ => false,
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(5),
            "probe canceled with foreign token",
            CancellationToken.None));

        Assert.Contains("probe canceled with foreign token", exception.Message, StringComparison.Ordinal);
        Assert.True(attempt > 1, $"Expected multiple retries despite foreign cancellation, got {attempt}.");
    }

    [Fact]
    public async Task UntilAsync_NegativeTimeout_ShouldThrowArgumentOutOfRangeException()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Eventually.UntilAsync(
            _ => Task.FromResult(0),
            _ => true,
            TimeSpan.FromMilliseconds(-1),
            TimeSpan.FromMilliseconds(1),
            "negative timeout",
            CancellationToken.None));
    }

    [Fact]
    public async Task UntilAsync_NonPositiveInterval_ShouldThrowArgumentOutOfRangeException()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Eventually.UntilAsync(
            _ => Task.FromResult(0),
            _ => true,
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero,
            "zero interval",
            CancellationToken.None));
    }

    [Fact]
    public async Task UntilAsync_ProbeThrowsTimeoutException_ShouldRetryUntilSucceeding()
    {
        // A probe that faults with its own TimeoutException (e.g. an HTTP client
        // timeout) must be treated as a transient failure and retried, not
        // confused with the wrapper deadline expiring. This probe throws once
        // then succeeds on the next attempt, proving recovery.
        var attempt = 0;

        var result = await Eventually.UntilAsync(
            _ =>
            {
                attempt++;
                if (attempt == 1)
                    throw new TimeoutException("probe-side timeout");
                return Task.FromResult(42);
            },
            value => value == 42,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(1),
            "probe recovers from its own timeout",
            CancellationToken.None);

        Assert.Equal(42, result);
        Assert.True(attempt >= 2);
    }

    [Fact]
    public async Task UntilAsync_ProbeThrowsTimeoutException_ShouldRetryUntilDeadline()
    {
        // A probe that keeps faulting with TimeoutException must keep retrying
        // until the overall deadline elapses, then surface a TimeoutException
        // that carries the probe's last error — the same behavior as any other
        // transient probe failure.
        var attempt = 0;

        // Generous timeout relative to the interval so the retry count stays
        // robust on loaded CI runners, where Task.Delay can inflate far beyond
        // the requested interval. With 20ms the first delay alone could
        // exhaust the budget and yield a single attempt; 500ms guarantees
        // multiple retries even under heavy inflation while still completing
        // quickly.
        var exception = await Assert.ThrowsAsync<TimeoutException>(() => Eventually.UntilAsync<int>(
            _ =>
            {
                attempt++;
                throw new TimeoutException("persistent probe-side timeout");
            },
            _ => false,
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(5),
            "probe always times out",
            CancellationToken.None));

        Assert.Contains("persistent probe-side timeout", exception.Message, StringComparison.Ordinal);
        Assert.IsType<TimeoutException>(exception.InnerException);
        Assert.True(attempt > 1, $"Expected multiple retries, got {attempt}.");
    }
}
