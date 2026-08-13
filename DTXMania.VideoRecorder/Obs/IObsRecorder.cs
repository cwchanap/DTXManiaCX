namespace DTXMania.VideoRecorder.Obs;

internal sealed record ObsRecordStatus(bool IsRecording);

internal interface IObsRecorder : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken token);

    Task<ObsRecordStatus> GetRecordStatusAsync(CancellationToken token);

    Task StartRecordAsync(CancellationToken token);

    Task<string> StopRecordAsync(CancellationToken token);
}
