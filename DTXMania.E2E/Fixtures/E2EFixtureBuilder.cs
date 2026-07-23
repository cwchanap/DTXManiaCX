using System.Text;
using DTXMania.Game.Lib.Config;

namespace DTXMania.E2E.Fixtures;

public static class E2EFixtureBuilder
{
    public const string ApiKey = "e2e-autoplay-smoke-key";
    public const string SongTitle = "E2E AutoPlay Smoke";
    public const string AudioFileName = "autoplay-tone.wav";
    public const string ArtifactRootEnvironmentVariable = "DTXMANIA_E2E_ARTIFACT_ROOT";

    // Minimal valid 8x32 white PNG shipped into the sandbox skin so it mirrors the
    // bundled System skin, which ships this same file at System/Graphics/hit_fx.png
    // (kept in sync by DTXMania.Test/Resources/DefaultSkinAssetsTests). Mirrors
    // TexturePath.HitFx ("Graphics/hit_fx.png").
    private const string HitEffectPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAgCAYAAAAv8DnQAAAAFklEQVR42mP4TwAwjCoYVTCqYKQqAAA/aPwuqUTQyAAAAABJRU5ErkJggg==";

    public static E2EFixture Build(
        string runRoot,
        string repoRoot,
        int apiPort,
        int playSpeedPercent = PlaySpeedRange.Default,
        int pitchSemitones = PitchRange.Default)
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

        File.WriteAllText(
            paths.ConfigPath,
            BuildConfig(
                paths.DtxRoot,
                paths.SkinRoot,
                apiPort,
                PlaySpeedRange.SnapAndClamp(playSpeedPercent),
                PitchRange.SnapAndClamp(pitchSemitones)),
            Encoding.UTF8);
        File.WriteAllText(paths.ChartPath, BuildChart(), Encoding.UTF8);
        var audioPath = Path.Combine(paths.SongDirectory, AudioFileName);
        WriteDeterministicWave(audioPath);
        // Ship hit_fx.png so the sandbox skin matches the bundled System skin.
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
            audioPath,
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

    private static string BuildConfig(
        string dtxRoot,
        string systemRoot,
        int apiPort,
        int playSpeedPercent,
        int pitchSemitones)
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
            $"PlaySpeedPercent={playSpeedPercent}",
            $"PitchSemitones={pitchSemitones}",
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
            $"#WAV01: {AudioFileName}",
            string.Empty,
            "; Short deterministic AutoPlay pattern with generated local audio.",
            "#00001: 0100000000000000",
            "#00011: 0100000000000000",
            "#00012: 0001000000000000",
            "#00013: 0000010000000000",
            "#00111: 0100000000000000",
            "#00112: 0001000000000000",
            "#00113: 0000010000000000",
            string.Empty
        });
    }

    private static void WriteDeterministicWave(string path)
    {
        const int sampleRate = 44_100;
        const short channelCount = 1;
        const short bitsPerSample = 16;
        const double frequencyHz = 440.0;
        const double durationSeconds = 1.0;
        var sampleCount = (int)(sampleRate * durationSeconds);
        var bytesPerSample = bitsPerSample / 8;
        var dataLength = sampleCount * channelCount * bytesPerSample;

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channelCount);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channelCount * bytesPerSample);
        writer.Write((short)(channelCount * bytesPerSample));
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);

        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            var phase = 2.0 * Math.PI * frequencyHz * sampleIndex / sampleRate;
            var sample = (short)Math.Round(Math.Sin(phase) * short.MaxValue * 0.20);
            writer.Write(sample);
        }
    }
}
