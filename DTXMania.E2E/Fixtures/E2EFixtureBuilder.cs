using System.Text;

namespace DTXMania.E2E.Fixtures;

public static class E2EFixtureBuilder
{
    public const string ApiKey = "e2e-autoplay-smoke-key";
    public const string SongTitle = "E2E AutoPlay Smoke";
    public const string ArtifactRootEnvironmentVariable = "DTXMANIA_E2E_ARTIFACT_ROOT";

    // Minimal valid 8x32 white PNG = 1 sprite at the EffectsManager's 8x32 frame size.
    // The Performance stage's EffectsManager requires a loadable hit-effect sprite sheet
    // (TotalSprites > 0) and throws in Debug builds when it is missing, so the sandbox skin
    // must ship one. Mirrors TexturePath.HitFx ("Graphics/hit_fx.png").
    private const string HitEffectPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAgCAYAAAAv8DnQAAAAFklEQVR42mP4TwAwjCoYVTCqYKQqAAA/aPwuqUTQyAAAAABJRU5ErkJggg==";

    public static E2EFixture Build(string runRoot, string repoRoot, int apiPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        var paths = CreateRunPaths(runRoot, repoRoot);

        Directory.CreateDirectory(paths.AppDataRoot);
        Directory.CreateDirectory(paths.SkinRoot);
        Directory.CreateDirectory(Path.Combine(paths.SkinRoot, "Graphics"));
        Directory.CreateDirectory(Path.Combine(paths.SkinRoot, "Sounds"));
        Directory.CreateDirectory(Path.Combine(paths.SkinRoot, "Script"));
        Directory.CreateDirectory(paths.SongDirectory);
        Directory.CreateDirectory(paths.ArtifactRoot);

        File.WriteAllText(paths.ConfigPath, BuildConfig(paths.DtxRoot, paths.SkinRoot, apiPort), Encoding.UTF8);
        File.WriteAllText(paths.ChartPath, BuildChart(), Encoding.UTF8);
        File.WriteAllBytes(
            Path.Combine(paths.SkinRoot, "Graphics", "hit_fx.png"),
            Convert.FromBase64String(HitEffectPngBase64));

        return new E2EFixture(
            paths.RunRoot,
            paths.AppDataRoot,
            paths.SkinRoot,
            paths.DtxRoot,
            paths.SongDirectory,
            paths.ConfigPath,
            paths.ChartPath,
            paths.ArtifactRoot,
            apiPort,
            ApiKey);
    }

    private static E2ERunPaths CreateRunPaths(string runRoot, string repoRoot)
    {
        var normalizedRunRoot = Path.GetFullPath(runRoot);
        var normalizedRepoRoot = Path.GetFullPath(repoRoot);
        var appDataRoot = Path.Combine(normalizedRunRoot, "appdata");
        var skinRoot = Path.Combine(normalizedRunRoot, "System");
        var dtxRoot = Path.Combine(normalizedRunRoot, "DTXFiles");
        var songDirectory = Path.Combine(dtxRoot, "AutoPlaySmoke");
        var artifactRoot = Environment.GetEnvironmentVariable(ArtifactRootEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(artifactRoot))
        {
            artifactRoot = Path.Combine(normalizedRunRoot, "TestResults", "e2e");
        }
        else if (!Path.IsPathRooted(artifactRoot))
        {
            artifactRoot = Path.Combine(normalizedRepoRoot, artifactRoot);
        }

        return new E2ERunPaths(
            normalizedRunRoot,
            appDataRoot,
            skinRoot,
            dtxRoot,
            songDirectory,
            Path.Combine(appDataRoot, "Config.ini"),
            Path.Combine(songDirectory, "autoplay-smoke.dtx"),
            Path.GetFullPath(artifactRoot));
    }

    private static string BuildConfig(string dtxRoot, string systemRoot, int apiPort)
    {
        return string.Join('\n', new[]
        {
            "[System]",
            $"SkinPath={systemRoot}",
            $"DTXPath={dtxRoot}",
            string.Empty,
            "[Skin]",
            "UseBoxDefSkin=False",
            $"SystemSkinRoot={systemRoot}",
            string.Empty,
            "[Display]",
            "ScreenWidth=1280",
            "ScreenHeight=720",
            "FullScreen=False",
            "VSyncWait=False",
            string.Empty,
            "[Game]",
            "ScrollSpeed=100",
            "AutoPlay=True",
            "NoFail=True",
            "AudioLatencyOffsetMs=0",
            string.Empty,
            "[Api]",
            "EnableGameApi=True",
            $"GameApiPort={apiPort}",
            $"GameApiKey={ApiKey}",
            string.Empty
        });
    }

    private static string BuildChart()
    {
        return string.Join('\n', new[]
        {
            $"#TITLE: {SongTitle}",
            "#ARTIST: CI",
            "#BPM: 120",
            "#DLEVEL: 10",
            string.Empty,
            "; Short deterministic AutoPlay pattern with no external audio dependencies.",
            "#00011: 0100000000000000",
            "#00012: 0001000000000000",
            "#00013: 0000010000000000",
            "#00111: 0100000000000000",
            "#00112: 0001000000000000",
            "#00113: 0000010000000000",
            string.Empty
        });
    }
}
