namespace DTXMania.E2E.Support;

public static class Eventually
{
    public static async Task<T> UntilAsync<T>(
        Func<CancellationToken, Task<T>> probe,
        Func<T, bool> predicate,
        TimeSpan timeout,
        TimeSpan interval,
        string description,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(predicate);

        var deadline = DateTimeOffset.UtcNow + timeout;
        T last = default!;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            last = await probe(cancellationToken);
            if (predicate(last))
                return last;

            await Task.Delay(interval, cancellationToken);
        }

        throw new TimeoutException($"Timed out waiting for {description}. Last value: {last}");
    }
}
