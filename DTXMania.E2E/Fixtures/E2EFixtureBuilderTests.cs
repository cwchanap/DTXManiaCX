namespace DTXMania.E2E.Fixtures;

[Trait("Category", "E2E-Support")]
public sealed class E2EFixtureBuilderTests
{
    [Fact]
    public void Build_ShouldWriteConfigAndGeneratedChart()
    {
        var root = Path.Combine(Path.GetTempPath(), "dtx-e2e-fixture-" + Guid.NewGuid().ToString("N"));
        var repoRoot = Directory.GetCurrentDirectory();
        var expectedSystemRoot = Path.Combine(repoRoot, "System");

        try
        {
            var fixture = E2EFixtureBuilder.Build(root, repoRoot, apiPort: 18080);

            Assert.Equal(Path.GetFullPath(root), fixture.RunRoot);
            Assert.Equal(Path.Combine(fixture.RunRoot, "appdata"), fixture.AppDataRoot);
            Assert.Equal(Path.Combine(fixture.RunRoot, "DTXFiles"), fixture.DtxRoot);
            Assert.Equal(Path.Combine(fixture.DtxRoot, "AutoPlaySmoke"), fixture.SongDirectory);
            Assert.Equal(Path.Combine(fixture.SongDirectory, "autoplay-smoke.dtx"), fixture.ChartPath);
            Assert.Equal(Path.Combine(fixture.RunRoot, "TestResults", "e2e"), fixture.ArtifactRoot);
            Assert.Equal(new Uri("http://127.0.0.1:18080/"), fixture.ApiBaseUri);
            Assert.Equal(new Uri("http://127.0.0.1:18080/jsonrpc"), fixture.JsonRpcUri);

            Assert.True(Directory.Exists(fixture.AppDataRoot));
            Assert.True(Directory.Exists(fixture.DtxRoot));
            Assert.True(Directory.Exists(fixture.SongDirectory));
            Assert.True(Directory.Exists(fixture.ArtifactRoot));
            Assert.True(File.Exists(fixture.ConfigPath));
            Assert.True(File.Exists(fixture.ChartPath));

            var config = File.ReadAllText(fixture.ConfigPath);
            Assert.Contains("EnableGameApi=True", config);
            Assert.Contains("GameApiKey=e2e-autoplay-smoke-key", config);
            Assert.Contains("GameApiPort=18080", config);
            Assert.Contains("AutoPlay=True", config);
            Assert.Contains("NoFail=True", config);
            Assert.Contains("ScreenWidth=1280", config);
            Assert.Contains("ScreenHeight=720", config);
            Assert.Contains("FullScreen=False", config);
            Assert.Contains("VSyncWait=False", config);
            Assert.Contains("AudioLatencyOffsetMs=0", config);
            Assert.Contains($"DTXPath={fixture.DtxRoot}", config);
            Assert.Contains($"SkinPath={expectedSystemRoot}", config);
            Assert.Contains($"SystemSkinRoot={expectedSystemRoot}", config);

            var chart = File.ReadAllText(fixture.ChartPath);
            Assert.Contains("#TITLE: E2E AutoPlay Smoke", chart);
            Assert.Contains("#BPM: 120", chart);
            Assert.Contains("#00011:", chart);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
