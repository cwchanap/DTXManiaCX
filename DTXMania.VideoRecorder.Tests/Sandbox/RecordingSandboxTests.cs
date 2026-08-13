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
        RecordingSandbox? sandbox = null;
        var sourceRoot = CreateSourceRoot(BuildConfig($"SkinPath={skinPath}"));

        try
        {
            sandbox = RecordingSandbox.Create(sourceRoot);

            Assert.Contains($"SkinPath={skinPath}", File.ReadAllText(sandbox.ConfigPath));
        }
        finally
        {
            if (sandbox is not null)
                Delete(sandbox.RunRoot);
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

        RecordingSandbox? sandbox = null;
        try
        {
            sandbox = RecordingSandbox.Create(sourceRoot);
            var config = File.ReadAllText(sandbox.ConfigPath);

            Assert.Contains($"DTXPath={dtxPath}", config);
            Assert.Contains($"SongRoot.0={dtxPath}", config);
            Assert.Contains($"SongRoot.1={songRoot1}", config);
            Assert.Contains($"SystemSkinRoot={systemSkinRoot}", config);
            Assert.Contains($"SkinPath={customSkinPath}", config);
        }
        finally
        {
            if (sandbox is not null)
                Delete(sandbox.RunRoot);
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
            string.Join(
                    '\n',
                    BuildConfig()
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Where(line => !line.StartsWith("SongRoot.0=", StringComparison.Ordinal))) +
                "\n");

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

        RecordingSandbox? sandbox = null;
        try
        {
            sandbox = RecordingSandbox.Create(sourceRoot);
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
        }
        finally
        {
            if (sandbox is not null)
                Delete(sandbox.RunRoot);
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

        RecordingSandbox? sandbox = null;
        try
        {
            sandbox = RecordingSandbox.Create(sourceRoot);
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
        }
        finally
        {
            if (sandbox is not null)
                Delete(sandbox.RunRoot);
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

        RecordingSandbox? sandbox = null;
        try
        {
            sandbox = RecordingSandbox.Create(sourceRoot);
            var files = Directory.GetFiles(sandbox.AppDataRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(sandbox.AppDataRoot, path))
                .ToArray();

            Assert.Equal(new[] { "Config.ini" }, files);
            Assert.True(File.Exists(Path.Combine(sourceRoot, "songs.db")));
            Assert.True(Directory.Exists(Path.Combine(sourceRoot, "Cache")));
        }
        finally
        {
            if (sandbox is not null)
                Delete(sandbox.RunRoot);
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
    public void Create_WhenFailureOccursAfterRunRootCreation_ShouldLeaveRunRootForDiagnostics()
    {
        var sourceRoot = CreateSourceRoot(BuildConfig());
        string? runRoot = null;

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                RecordingSandbox.CreateForTests(
                    sourceRoot,
                    createdRunRoot =>
                    {
                        runRoot = createdRunRoot;
                        throw new InvalidOperationException("injected post-create failure");
                    }));

            Assert.Equal("injected post-create failure", exception.Message);
            Assert.NotNull(runRoot);
            Assert.True(Directory.Exists(runRoot));
            Assert.True(Directory.Exists(Path.Combine(runRoot!, "appdata")));
        }
        finally
        {
            if (runRoot is not null)
                Delete(runRoot);
            Delete(sourceRoot);
        }
    }

    [Theory]
    [InlineData("DTXPath=relative/duplicate")]
    [InlineData("SystemSkinRoot=relative/duplicate")]
    [InlineData("SkinPath=relative/duplicate")]
    public void Create_WhenScalarPathHasRelativeDuplicate_ShouldReject(string duplicateLine)
    {
        var sourceRoot = CreateSourceRoot(BuildConfig() + duplicateLine + "\n");

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => RecordingSandbox.Create(sourceRoot));

            Assert.Contains("not normalized", exception.Message);
            Assert.Contains("Open CX once and exit normally, then retry dtx-video.", exception.Message);
        }
        finally
        {
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
        var fixtureRoot = Path.Combine(Path.GetTempPath(), "dtx-video-config-fixture");
        var fixtureSongs = Path.Combine(fixtureRoot, "Songs");
        var fixtureSystem = Path.Combine(fixtureRoot, "System");
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SkinPath"] = "Default",
            ["DTXPath"] = fixtureSongs,
            ["SongRoot.0"] = fixtureSongs,
            ["SystemSkinRoot"] = fixtureSystem,
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
