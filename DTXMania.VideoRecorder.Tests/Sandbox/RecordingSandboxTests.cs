using DTXMania.VideoRecorder.Sandbox;

namespace DTXMania.VideoRecorder.Tests.Sandbox;

public sealed class RecordingSandboxTests
{
    [Theory]
    [InlineData("Default")]
    [InlineData("default")]
    [InlineData(" DEFAULT ")]
    public void Create_DefaultSkinToken_ShouldSurviveUnchanged(string skinPath)
    {
        var sourceRoot = CreateSourceRoot(BuildConfig($"SkinPath={skinPath}"));

        try
        {
            var sandbox = RecordingSandbox.Create(sourceRoot);

            Assert.Contains($"SkinPath={skinPath}", File.ReadAllText(sandbox.ConfigPath));
            Delete(sandbox.RunRoot);
        }
        finally
        {
            Delete(sourceRoot);
        }
    }

    [Fact]
    public void Create_NormalizedAbsolutePaths_ShouldSurviveUnchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), "dtx-video-source", Guid.NewGuid().ToString("N"));
        var dtxPath = Path.Combine(root, "Songs");
        var songRoot1 = Path.Combine(root, "MoreSongs");
        var systemSkinRoot = Path.Combine(root, "System");
        var customSkinPath = Path.Combine(root, "Skins", "X");
        var sourceRoot = CreateSourceRoot(BuildConfig(
            $"DTXPath={dtxPath}",
            $"SongRoot.0={dtxPath}",
            $"SongRoot.1={songRoot1}",
            $"SystemSkinRoot={systemSkinRoot}",
            $"SkinPath={customSkinPath}"), root);

        try
        {
            var sandbox = RecordingSandbox.Create(sourceRoot);
            var config = File.ReadAllText(sandbox.ConfigPath);

            Assert.Contains($"DTXPath={dtxPath}", config);
            Assert.Contains($"SongRoot.0={dtxPath}", config);
            Assert.Contains($"SongRoot.1={songRoot1}", config);
            Assert.Contains($"SystemSkinRoot={systemSkinRoot}", config);
            Assert.Contains($"SkinPath={customSkinPath}", config);
            Delete(sandbox.RunRoot);
        }
        finally
        {
            Delete(sourceRoot);
        }
    }

    [Theory]
    [InlineData("DTXPath", "Songs")]
    [InlineData("SongRoot.0", "~/charts")]
    [InlineData("SkinPath", "Skins/X")]
    [InlineData("SystemSkinRoot", "System")]
    public void Create_RelativePath_ShouldRejectWithNormalizationGuidance(string key, string value)
    {
        var sourceRoot = CreateSourceRoot(BuildConfig($"{key}={value}"));

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => RecordingSandbox.Create(sourceRoot));

            Assert.Contains($"Source Config.ini key '{key}' is not normalized.", exception.Message);
            Assert.Contains("Open CX once and exit normally, then retry dtx-video.", exception.Message);
        }
        finally
        {
            Delete(sourceRoot);
        }
    }

    [Fact]
    public void Create_WithoutIndexedSongRoot_ShouldReject()
    {
        var sourceRoot = CreateSourceRoot(
            BuildConfig().Replace("SongRoot.0=/absolute/Songs\n", string.Empty, StringComparison.Ordinal));

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => RecordingSandbox.Create(sourceRoot));

            Assert.Contains("SongRoot", exception.Message);
            Assert.Contains("Open CX once and exit normally, then retry dtx-video.", exception.Message);
        }
        finally
        {
            Delete(sourceRoot);
        }
    }

    [Fact]
    public void Create_ShouldPreserveLastUsedSkinGamePresentationAndUnrelatedLines()
    {
        var sourceRoot = CreateSourceRoot(BuildConfig(
            "LastUsedSkin=My Custom Skin",
            "ScrollSpeed=73",
            "PlaySpeedPercent=75",
            "PitchSemitones=-2",
            "MasterVolume=42",
            "BGMVolume=43",
            "SEVolume=44",
            "VSyncWait=True",
            "UnrelatedSetting=keep-this"));

        try
        {
            var sandbox = RecordingSandbox.Create(sourceRoot);
            var config = File.ReadAllText(sandbox.ConfigPath);

            Assert.Contains("LastUsedSkin=My Custom Skin", config);
            Assert.Contains("ScrollSpeed=73", config);
            Assert.Contains("PlaySpeedPercent=75", config);
            Assert.Contains("PitchSemitones=-2", config);
            Assert.Contains("MasterVolume=42", config);
            Assert.Contains("BGMVolume=43", config);
            Assert.Contains("SEVolume=44", config);
            Assert.Contains("VSyncWait=True", config);
            Assert.Contains("UnrelatedSetting=keep-this", config);
            Delete(sandbox.RunRoot);
        }
        finally
        {
            Delete(sourceRoot);
        }
    }

    [Fact]
    public void Create_ShouldOverrideOnlyRecorderOwnedKeys()
    {
        var sourceRoot = CreateSourceRoot(BuildConfig(
            "EnableGameApi=False",
            "GameApiPort=1",
            "GameApiKey=source-key",
            "AutoPlay=False",
            "NoFail=False",
            "ScreenWidth=640",
            "ScreenHeight=480",
            "FullScreen=True"));

        try
        {
            var sandbox = RecordingSandbox.Create(sourceRoot);
            var config = File.ReadAllText(sandbox.ConfigPath);

            Assert.Contains("EnableGameApi=True", config);
            Assert.Contains($"GameApiPort={sandbox.ApiPort}", config);
            Assert.Contains($"GameApiKey={sandbox.ApiKey}", config);
            Assert.Contains("AutoPlay=True", config);
            Assert.Contains("NoFail=True", config);
            Assert.Contains("ScreenWidth=1280", config);
            Assert.Contains("ScreenHeight=720", config);
            Assert.Contains("FullScreen=False", config);
            Assert.DoesNotContain("GameApiPort=1", config);
            Assert.DoesNotContain("GameApiKey=source-key", config);
            Delete(sandbox.RunRoot);
        }
        finally
        {
            Delete(sourceRoot);
        }
    }

    [Fact]
    public void Create_ShouldCopyConfigOnlyAndNotLiveState()
    {
        var sourceRoot = CreateSourceRoot(BuildConfig());
        File.WriteAllText(Path.Combine(sourceRoot, "songs.db"), "database");
        File.WriteAllText(Path.Combine(sourceRoot, "songs.db-wal"), "wal");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "Cache"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "CrashReports"));

        try
        {
            var sandbox = RecordingSandbox.Create(sourceRoot);
            var files = Directory.GetFiles(sandbox.AppDataRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(sandbox.AppDataRoot, path))
                .ToArray();

            Assert.Equal(new[] { "Config.ini" }, files);
            Assert.True(File.Exists(Path.Combine(sourceRoot, "songs.db")));
            Assert.True(Directory.Exists(Path.Combine(sourceRoot, "Cache")));
            Delete(sandbox.RunRoot);
        }
        finally
        {
            Delete(sourceRoot);
        }
    }

    [Fact]
    public async Task DeleteOnSuccessAsync_ShouldDeleteRunRoot()
    {
        var sourceRoot = CreateSourceRoot(BuildConfig());
        try
        {
            var sandbox = RecordingSandbox.Create(sourceRoot);
            Assert.True(Directory.Exists(sandbox.RunRoot));

            await sandbox.DeleteOnSuccessAsync();

            Assert.False(Directory.Exists(sandbox.RunRoot));
        }
        finally
        {
            Delete(sourceRoot);
        }
    }

    [Fact]
    public void UnhandledRunFailure_ShouldLeaveRunRootForDiagnostics()
    {
        var sourceRoot = CreateSourceRoot(BuildConfig());
        string? runRoot = null;

        try
        {
            var sandbox = RecordingSandbox.Create(sourceRoot);
            runRoot = sandbox.RunRoot;

            Assert.True(Directory.Exists(sandbox.RunRoot));
            Assert.True(File.Exists(sandbox.ConfigPath));
        }
        finally
        {
            if (runRoot is not null)
                Delete(runRoot);
            Delete(sourceRoot);
        }
    }

    private static string CreateSourceRoot(string config, string? root = null)
    {
        root ??= Path.Combine(Path.GetTempPath(), "dtx-video-source", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Config.ini"), config);
        return root;
    }

    private static string BuildConfig(params string[] overrides)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SkinPath"] = "Default",
            ["DTXPath"] = "/absolute/Songs",
            ["SongRoot.0"] = "/absolute/Songs",
            ["SystemSkinRoot"] = "/absolute/System",
            ["LastUsedSkin"] = "Default",
            ["ScreenWidth"] = "1920",
            ["ScreenHeight"] = "1080",
            ["FullScreen"] = "True",
            ["ScrollSpeed"] = "50",
            ["PlaySpeedPercent"] = "100",
            ["PitchSemitones"] = "0",
            ["MasterVolume"] = "100",
            ["BGMVolume"] = "100",
            ["SEVolume"] = "100",
            ["EnableGameApi"] = "False",
            ["GameApiPort"] = "8080",
            ["GameApiKey"] = "source-key",
            ["AutoPlay"] = "False",
            ["NoFail"] = "False"
        };

        foreach (var item in overrides)
        {
            var parts = item.Split('=', 2);
            values[parts[0]] = parts[1];
        }

        return string.Join('\n', values.Select(pair => $"{pair.Key}={pair.Value}")) + "\n";
    }

    private static void Delete(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
