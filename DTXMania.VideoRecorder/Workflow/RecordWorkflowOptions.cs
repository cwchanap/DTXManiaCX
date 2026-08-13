namespace DTXMania.VideoRecorder.Workflow;

internal sealed record RecordWorkflowOptions
{
    public TimeSpan SetupTimeout { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan StageTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan PerformanceTimeout { get; init; } = TimeSpan.FromMinutes(20);

    public TimeSpan ExternalIoTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    public Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } =
        static (delay, token) => Task.Delay(delay, token);

    internal void Validate()
    {
        if (SetupTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SetupTimeout));
        if (StageTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(StageTimeout));
        if (PerformanceTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(PerformanceTimeout));
        if (ExternalIoTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ExternalIoTimeout));
        if (PollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(PollInterval));
        ArgumentNullException.ThrowIfNull(DelayAsync);
    }
}
