using System.Runtime.ExceptionServices;
using DTXMania.Automation.Process;
using DTXMania.Automation.Support;
using DTXMania.Automation.Telemetry;
using DTXMania.VideoRecorder.Obs;

namespace DTXMania.VideoRecorder.Workflow;

/// <summary>
/// Runs one exact chart through the normal CX journey while owning only the
/// process and OBS recording started by this run.
/// </summary>
internal sealed class RecordWorkflow
{
    private static readonly TimeSpan EnterHold = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan ResultHold = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PreviewMinimum = TimeSpan.FromSeconds(10);

    private readonly IGameRecordingControl _game;
    private readonly IObsRecorder _obs;
    private readonly string _chartPath;
    private readonly GameProcessStartOptions _startOptions;
    private readonly RecordWorkflowOptions _options;

    public RecordWorkflow(
        IGameRecordingControl game,
        IObsRecorder obs,
        string chartPath,
        GameProcessStartOptions startOptions,
        RecordWorkflowOptions? options = null)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _obs = obs ?? throw new ArgumentNullException(nameof(obs));
        ArgumentException.ThrowIfNullOrWhiteSpace(chartPath);
        _chartPath = chartPath;
        _startOptions = startOptions ?? throw new ArgumentNullException(nameof(startOptions));
        _options = options ?? new RecordWorkflowOptions();
        _options.Validate();
    }

    /// <summary>
    /// Runs the journey and returns the raw OBS output path when StopRecord
    /// succeeds. Artifact verification and publication are deliberately left to
    /// the next recorder task.
    /// </summary>
    public async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var obsOwned = false;
        var obsStopped = false;
        string? rawOutputPath = null;
        Exception? primaryFailure = null;

        try
        {
            _game.Start(_startOptions);

            await _game.WaitForStartupAsync(_options.SetupTimeout, cancellationToken)
                .ConfigureAwait(false);
            await WaitForStateAsync(
                    state => string.Equals(state.StageType, "Title", StringComparison.Ordinal),
                    _options.SetupTimeout,
                    "Title",
                    cancellationToken)
                .ConfigureAwait(false);

            await _game.SendKeyAsync("Enter", EnterHold, cancellationToken).ConfigureAwait(false);
            await WaitForStateAsync(
                    state => string.Equals(state.StageType, "SongSelect", StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(state.SelectedSongTitle),
                    _options.SetupTimeout,
                    "populated Song Select",
                    cancellationToken)
                .ConfigureAwait(false);

            // Preparation is intentionally one-shot. Retrying a permanent
            // chart/library failure for the full setup timeout obscures the
            // actionable error returned by CX.
            using (var prepareTimeout = CreateTimeoutSource(
                       _options.SetupTimeout,
                       cancellationToken))
            {
                await _game.PrepareVideoChartAsync(_chartPath, prepareTimeout.Token)
                    .ConfigureAwait(false);
            }
            await EnsureScreenshotAsync(cancellationToken).ConfigureAwait(false);

            await RunExternalAsync(
                    token => _obs.ConnectAsync(token),
                    cancellationToken)
                .ConfigureAwait(false);
            var status = await RunExternalAsync(
                    token => _obs.GetRecordStatusAsync(token),
                    cancellationToken)
                .ConfigureAwait(false);
            if (status.IsRecording)
            {
                throw new InvalidOperationException(
                    "OBS is already recording; stop the existing recording before starting dtx-video.");
            }

            await RunExternalAsync(
                    token => _obs.StartRecordAsync(token),
                    cancellationToken)
                .ConfigureAwait(false);
            obsOwned = true;

            await RunStageOperationAsync(
                    token => _game.StartPreparedPreviewAsync(token),
                    cancellationToken)
                .ConfigureAwait(false);
            await WaitForStateAsync(
                    state => string.Equals(state.StageType, "SongSelect", StringComparison.Ordinal)
                        && string.Equals(state.PreparedPreviewState, "Playing", StringComparison.Ordinal)
                        && state.PreparedPreviewElapsedMs >= PreviewMinimum.TotalMilliseconds,
                    _options.StageTimeout,
                    "prepared preview playing for ten seconds",
                    cancellationToken)
                .ConfigureAwait(false);

            await RunStageOperationAsync(
                    token => _game.ActivatePreparedChartAsync(token),
                    cancellationToken)
                .ConfigureAwait(false);
            await WaitForStateAsync(
                    state => string.Equals(state.StageType, "SongTransition", StringComparison.Ordinal),
                    _options.StageTimeout,
                    "SongTransition",
                    cancellationToken)
                .ConfigureAwait(false);
            await WaitForStateAsync(
                    state => string.Equals(state.StageType, "Performance", StringComparison.Ordinal)
                        && state.PerformanceReady
                        && state.AutoPlayEnabled
                        && state.TotalNotes > 0,
                    _options.StageTimeout,
                    "ready AutoPlay Performance",
                    cancellationToken)
                .ConfigureAwait(false);

            await WaitForStateAsync(
                    state => string.Equals(state.StageType, "Result", StringComparison.Ordinal)
                        && state.StageCompleted
                        && state.ClearFlag
                        && string.Equals(
                            state.CompletionReason,
                            "SongComplete",
                            StringComparison.Ordinal)
                        && state.TotalNotes > 0
                        && state.TotalJudgements == state.TotalNotes,
                    _options.PerformanceTimeout,
                    "completed cleared Result",
                    cancellationToken)
                .ConfigureAwait(false);

            await EnsureScreenshotAsync(cancellationToken).ConfigureAwait(false);
            // This is a deliberate no-input hold. Do not send a key to advance
            // Result; the captured frame must remain visible for five seconds.
            await _options.DelayAsync(ResultHold, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            Exception? cleanupFailure = null;
            if (obsOwned && !obsStopped)
            {
                try
                {
                    rawOutputPath = await StopOwnedObsAsync().ConfigureAwait(false);
                    obsStopped = true;
                }
                catch (Exception exception)
                {
                    cleanupFailure = exception;
                }
            }

            try
            {
                await _game.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                cleanupFailure ??= exception;
            }

            try
            {
                await _obs.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                cleanupFailure ??= exception;
            }

            // Never replace the actionable journey/cancellation failure with a
            // secondary cleanup failure. If the journey itself succeeded, a
            // cleanup failure is still a failed recording run.
            if (primaryFailure is null && cleanupFailure is not null)
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }

        return rawOutputPath;
    }

    private async Task<GameStateSnapshot> WaitForStateAsync(
        Func<GameStateSnapshot, bool> predicate,
        TimeSpan timeout,
        string description,
        CancellationToken cancellationToken)
    {
        return await Eventually.UntilAsync(
                token => _game.GetGameStateAsync(token),
                predicate,
                timeout,
                _options.PollInterval,
                description,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnsureScreenshotAsync(CancellationToken cancellationToken)
    {
        var screenshot = await RunExternalAsync(
                token => _game.TakeScreenshotBase64Async(token),
                cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(screenshot))
        {
            throw new InvalidOperationException(
                "CX returned an empty screenshot at a recording barrier.");
        }
    }

    private async Task<string> StopOwnedObsAsync()
    {
        using var timeout = new CancellationTokenSource(_options.ExternalIoTimeout);
        return await _obs.StopRecordAsync(timeout.Token).ConfigureAwait(false);
    }

    private async Task RunStageOperationAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        using var timeout = CreateTimeoutSource(
            _options.StageTimeout,
            cancellationToken);
        await operation(timeout.Token).ConfigureAwait(false);
    }

    private async Task RunExternalAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        using var timeout = CreateTimeoutSource(
            _options.ExternalIoTimeout,
            cancellationToken);
        await operation(timeout.Token).ConfigureAwait(false);
    }

    private async Task<T> RunExternalAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using var timeout = CreateTimeoutSource(
            _options.ExternalIoTimeout,
            cancellationToken);
        return await operation(timeout.Token).ConfigureAwait(false);
    }

    private static CancellationTokenSource CreateTimeoutSource(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }
}
