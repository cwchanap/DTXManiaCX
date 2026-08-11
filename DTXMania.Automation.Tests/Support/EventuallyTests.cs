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

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => Eventually.UntilAsync(
            _ =>
            {
                attempt++;
                if (attempt == 1)
                    return Task.FromResult("last-value");

                throw new InvalidOperationException("transient boom");
            },
            _ => false,
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(1),
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

        // The deadline must actually bound the probe — it should fire well under
        // the safety WaitAsync window and not dramatically exceed the configured
        // timeout.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Expected the deadline to bound the never-completing probe near {timeout}, but waited {stopwatch.Elapsed}.");

        // The probe must be canceled (not left running unbounded) once the
        // deadline fires.
        Assert.True(neverCompletingSource.Task.IsCanceled);
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

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => Eventually.UntilAsync<int>(
            _ =>
            {
                attempt++;
                throw new TimeoutException("persistent probe-side timeout");
            },
            _ => false,
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(1),
            "probe always times out",
            CancellationToken.None));

        Assert.Contains("persistent probe-side timeout", exception.Message, StringComparison.Ordinal);
        Assert.IsType<TimeoutException>(exception.InnerException);
        Assert.True(attempt > 1, $"Expected multiple retries, got {attempt}.");
    }
}
