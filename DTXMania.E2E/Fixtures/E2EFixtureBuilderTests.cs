using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song;

namespace DTXMania.E2E.Fixtures;

[Trait("Category", "E2E-Support")]
[Collection("E2EFixture")]
public sealed class E2EFixtureBuilderTests
{
    [Fact]
    public async Task Build_ShouldWriteConfigAndGeneratedChart()
    {
        var root = Path.Combine(Path.GetTempPath(), "dtx-e2e-fixture-" + Guid.NewGuid().ToString("N"));
        var repoRoot = Directory.GetCurrentDirectory();

        try
        {
            var fixture = E2EFixtureBuilder.Build(root, repoRoot, apiPort: 18080);

            Assert.Equal(Path.GetFullPath(root), fixture.RunRoot);
            Assert.Equal(Path.Combine(fixture.RunRoot, "appdata"), fixture.AppDataRoot);
            Assert.Equal(Path.Combine(fixture.RunRoot, "System"), fixture.SkinRoot);
            Assert.Equal(Path.Combine(fixture.RunRoot, "DTXFiles"), fixture.DtxRoot);
            Assert.Equal(Path.Combine(fixture.DtxRoot, "AutoPlaySmoke"), fixture.SongDirectory);
            Assert.Equal(Path.Combine(fixture.SongDirectory, "autoplay-smoke.dtx"), fixture.ChartPath);
            Assert.Equal(Path.Combine(fixture.RunRoot, "TestResults", "e2e"), fixture.ArtifactRoot);
            Assert.Equal(new Uri("http://127.0.0.1:18080/"), fixture.ApiBaseUri);
            Assert.Equal(new Uri("http://127.0.0.1:18080/jsonrpc"), fixture.JsonRpcUri);

            Assert.True(Directory.Exists(fixture.AppDataRoot));
            Assert.True(Directory.Exists(fixture.SkinRoot));
            Assert.True(Directory.Exists(Path.Combine(fixture.SkinRoot, "Graphics")));
            Assert.True(Directory.Exists(Path.Combine(fixture.SkinRoot, "Sounds")));
            Assert.True(Directory.Exists(Path.Combine(fixture.SkinRoot, "Script")));
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
            Assert.Contains($"SkinPath={fixture.SkinRoot}", config);
            Assert.Contains($"SystemSkinRoot={fixture.SkinRoot}", config);

            var chart = File.ReadAllText(fixture.ChartPath);
            Assert.Contains("#TITLE: E2E AutoPlay Smoke", chart);
            Assert.Contains("#BPM: 120", chart);
            Assert.Contains("#00011:", chart);

            var configManager = new ConfigManager();
            configManager.LoadConfig(fixture.ConfigPath);

            Assert.True(configManager.Config.EnableGameApi);
            Assert.Equal("e2e-autoplay-smoke-key", configManager.Config.GameApiKey);
            Assert.Equal(18080, configManager.Config.GameApiPort);
            Assert.True(configManager.Config.AutoPlay);
            Assert.True(configManager.Config.NoFail);
            Assert.Equal(fixture.DtxRoot, configManager.Config.DTXPath);
            Assert.Equal(fixture.SkinRoot, configManager.Config.SkinPath);
            Assert.Equal(fixture.SkinRoot, configManager.Config.SystemSkinRoot);

            var parsedChart = await DTXChartParser.ParseAsync(fixture.ChartPath);
            var (song, songChart) = await DTXChartParser.ParseSongEntitiesAsync(fixture.ChartPath);

            Assert.Equal("E2E AutoPlay Smoke", song.Title);
            Assert.Equal(10, songChart.DrumLevel);
            Assert.Equal(120.0, parsedChart.Bpm);
            Assert.Equal(6, parsedChart.TotalNotes);
            Assert.True(parsedChart.DurationMs > 0);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_WhenArtifactRootEnvironmentOverrideIsSet_ShouldUseOverride()
    {
        var root = Path.Combine(Path.GetTempPath(), "dtx-e2e-fixture-" + Guid.NewGuid().ToString("N"));
        var artifactRoot = Path.Combine(Path.GetTempPath(), "dtx-e2e-artifacts-" + Guid.NewGuid().ToString("N"));
        var previousArtifactRoot = Environment.GetEnvironmentVariable(E2EFixtureBuilder.ArtifactRootEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(E2EFixtureBuilder.ArtifactRootEnvironmentVariable, artifactRoot);

            var fixture = E2EFixtureBuilder.Build(root, Directory.GetCurrentDirectory(), apiPort: 18080);

            Assert.Equal(Path.GetFullPath(artifactRoot), fixture.ArtifactRoot);
            Assert.True(Directory.Exists(fixture.ArtifactRoot));
        }
        finally
        {
            Environment.SetEnvironmentVariable(E2EFixtureBuilder.ArtifactRootEnvironmentVariable, previousArtifactRoot);

            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
            if (Directory.Exists(artifactRoot))
                Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Fact]
    public void Build_WhenArtifactRootEnvironmentOverrideIsRelative_ShouldResolveFromRepoRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "dtx-e2e-fixture-" + Guid.NewGuid().ToString("N"));
        var repoRoot = Path.Combine(Path.GetTempPath(), "dtx-e2e-repo-" + Guid.NewGuid().ToString("N"));
        var artifactRoot = Path.Combine(repoRoot, "TestResults", "e2e");
        var previousArtifactRoot = Environment.GetEnvironmentVariable(E2EFixtureBuilder.ArtifactRootEnvironmentVariable);

        try
        {
            Directory.CreateDirectory(repoRoot);
            Environment.SetEnvironmentVariable(E2EFixtureBuilder.ArtifactRootEnvironmentVariable, "TestResults/e2e");

            var fixture = E2EFixtureBuilder.Build(root, repoRoot, apiPort: 18080);

            Assert.Equal(Path.GetFullPath(artifactRoot), fixture.ArtifactRoot);
            Assert.True(Directory.Exists(fixture.ArtifactRoot));
        }
        finally
        {
            Environment.SetEnvironmentVariable(E2EFixtureBuilder.ArtifactRootEnvironmentVariable, previousArtifactRoot);

            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
            if (Directory.Exists(repoRoot))
                Directory.Delete(repoRoot, recursive: true);
        }
    }
}
