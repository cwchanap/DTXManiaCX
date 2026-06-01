namespace DTXMania.E2E.Fixtures;

public sealed record E2ERunPaths(
    string RunRoot,
    string AppDataRoot,
    string SkinRoot,
    string DtxRoot,
    string SongDirectory,
    string ConfigPath,
    string ChartPath,
    string ArtifactRoot);
