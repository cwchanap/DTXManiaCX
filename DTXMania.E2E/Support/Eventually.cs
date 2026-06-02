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
        Exception? lastException = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                last = await probe(cancellationToken);
                lastException = null;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Transient probe failure (HTTP error, timeout, JSON-RPC error) —
                // treat as "predicate not yet satisfied" and retry.
                lastException = ex;
                await Task.Delay(interval, cancellationToken);
                continue;
            }

            if (predicate(last))
                return last;

            await Task.Delay(interval, cancellationToken);
        }

        var baseMessage = $"Timed out waiting for {description}. Last value: {last}";
        throw lastException is not null
            ? new TimeoutException($"{baseMessage}. Last error: {lastException.Message}", lastException)
            : new TimeoutException(baseMessage);
    }
}
