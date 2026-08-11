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
}
