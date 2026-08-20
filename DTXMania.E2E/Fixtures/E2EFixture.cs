namespace DTXMania.E2E.Fixtures;

/// <param name="LegacyConfigPath">
/// Bootstrap-input Config.ini. The builder writes it before launch; the game
/// imports it once when <paramref name="ConfigDatabasePath"/> is absent
/// (first launch) and never modifies it afterwards.
/// </param>
/// <param name="ConfigDatabasePath">
/// Authoritative SQLite config database (HPA-190) created by the launched
/// game; post-run persistence assertions must load this, not the INI.
/// </param>
public sealed record E2EFixture(
    string RunRoot,
    string AppDataRoot,
    string SkinRoot,
    string DtxRoot,
    string SongDirectory,
    string LegacyConfigPath,
    string ConfigDatabasePath,
    string ChartPath,
    string AudioPath,
    string ArtifactRoot,
    int ApiPort,
    string ApiKey)
{
    public Uri ApiBaseUri => new($"http://127.0.0.1:{ApiPort}/");
    public Uri JsonRpcUri => new($"http://127.0.0.1:{ApiPort}/jsonrpc");
}
