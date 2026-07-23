namespace DTXMania.E2E.Fixtures;

public sealed record E2EFixture(
    string RunRoot,
    string AppDataRoot,
    string SkinRoot,
    string DtxRoot,
    string SongDirectory,
    string ConfigPath,
    string ChartPath,
    string AudioPath,
    string ArtifactRoot,
    int ApiPort,
    string ApiKey)
{
    public Uri ApiBaseUri => new($"http://127.0.0.1:{ApiPort}/");
    public Uri JsonRpcUri => new($"http://127.0.0.1:{ApiPort}/jsonrpc");
}
