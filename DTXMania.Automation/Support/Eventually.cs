namespace DTXMania.Automation.Support;

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

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            // Bound each probe with its share of the overall deadline. Without
            // this, a probe that never completes (and a caller token that is not
            // canceled) would hang the method indefinitely, ignoring the
            // advertised timeout. A linked CTS lets us cancel a probe that
            // exceeds its share rather than orphaning it.
            using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task<T> probeTask = Task.FromResult<T>(default!);
            try
            {
                probeTask = probe(probeCancellation.Token);
                last = await probeTask.WaitAsync(remaining, cancellationToken).ConfigureAwait(false);
                lastException = null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TimeoutException)
            {
                // The probe exceeded its share of the deadline. Cancel it so it
                // does not run unbounded, observe it best-effort, then fall
                // through to the overall timeout below.
                probeCancellation.Cancel();
                await ObserveProbeCompletionAsync(probeTask).ConfigureAwait(false);
                break;
            }
            catch (Exception ex)
            {
                // Transient probe failure (HTTP error, timeout, JSON-RPC error) —
                // treat as "predicate not yet satisfied" and retry.
                lastException = ex;
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (predicate(last))
                return last;

            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }

        var baseMessage = $"Timed out waiting for {description}. Last value: {last}";
        throw lastException is not null
            ? new TimeoutException($"{baseMessage}. Last error: {lastException.Message}", lastException)
            : new TimeoutException(baseMessage);
    }

    private static async Task ObserveProbeCompletionAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort observation of a probe that was canceled/timed out
            // after the overall deadline fired. If it still hasn't completed
            // within the observation window it is left to the GC — acceptable
            // since the caller has already given up waiting.
        }
    }
}
