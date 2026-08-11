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

            // Invoke the probe separately from awaiting it, so a synchronous
            // throw from probe(...) is handled as a transient probe failure
            // rather than being misattributed to the wrapper deadline below.
            Task<T> probeTask;
            try
            {
                probeTask = probe(probeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Synchronous probe failure (HTTP error, timeout, JSON-RPC
                // error) — treat as "predicate not yet satisfied" and retry.
                lastException = ex;
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                last = await probeTask.WaitAsync(remaining, cancellationToken).ConfigureAwait(false);
                lastException = null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TimeoutException ex)
            {
                // WaitAsync(remaining) throws TimeoutException in two cases:
                //   (a) the wrapper deadline elapsed while the probe was still
                //       running, or
                //   (b) the probe itself faulted with TimeoutException (e.g. an
                //       HTTP client timeout) before the wrapper fired.
                // Distinguish them by inspecting the probe task: a faulted probe
                // threw transiently and must be retried (matching the transient
                // handling below); a still-running probe means the wrapper
                // deadline fired, so cancel the probe and stop polling.
                if (probeTask.IsFaulted)
                {
                    lastException = ex;
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

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
