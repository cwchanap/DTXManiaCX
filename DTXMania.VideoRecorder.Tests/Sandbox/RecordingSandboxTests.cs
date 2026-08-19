using DTXMania.VideoRecorder.Sandbox;
using Microsoft.Data.Sqlite;

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
        var sourceRoot = CreateSourceRoot(BuildRows($"SkinPath={skinPath}"));

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
        var songRoot1 = Path.Combine(root, "MoreSongs");
        var systemSkinRoot = Path.Combine(root, "System");
        var customSkinPath = Path.Combine(root, "Skins", "X");
        var sourceRoot = CreateSourceRoot(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SkinPath"] = customSkinPath,
            ["SongRoot.0"] = Path.Combine(root, "Songs"),
            ["SongRoot.1"] = songRoot1,
            ["SystemSkinRoot"] = systemSkinRoot
        });

        RecordingSandbox? sandbox = null;
        try
        {
            sandbox = RecordingSandbox.Create(sourceRoot);
            var config = File.ReadAllText(sandbox.ConfigPath);

            Assert.Contains($"SongRoot.0={Path.Combine(root, "Songs")}", config);
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
    [InlineData("SongRoot.0", "~/charts")]
    [InlineData("SkinPath", "Skins/X")]
    [InlineData("SystemSkinRoot", "System")]
    public void Create_RelativePath_ShouldRejectWithNormalizationGuidance(string key, string value)
    {
        var sourceRoot = CreateSourceRoot(BuildRows($"{key}={value}"));

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => RecordingSandbox.Create(sourceRoot));

            Assert.Contains($"Source config database key '{key}' is not normalized.", exception.Message);
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
        var rows = BuildRows();
        rows.Remove("SongRoot.0");
        var sourceRoot = CreateSourceRoot(rows);

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
    public void Create_WhenSourceDatabaseMissing_ShouldReject()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), "dtx-video-source", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceRoot);

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => RecordingSandbox.Create(sourceRoot));

            Assert.Contains("Source config database was not found", exception.Message);
            Assert.Contains("config.db", exception.Message);
            Assert.Contains("Open CX once and exit normally, then retry dtx-video.", exception.Message);
        }
        finally
        {
            Delete(sourceRoot);
        }
    }

    [Fact]
    public void Create_WhenSchemaVersionUnsupported_ShouldReject()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), "dtx-video-source", Guid.NewGuid().ToString("N"));
        TestSourceConfigDatabase.Create(sourceRoot, BuildRows(), userVersion: 2);

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => RecordingSandbox.Create(sourceRoot));

            Assert.Contains("schema version", exception.Message);
        }
        finally
        {
            Delete(sourceRoot);
        }
    }

    [Fact]
    public void Create_WhenConfigEntriesTableMissing_ShouldReject()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), "dtx-video-source", Guid.NewGuid().ToString("N"));
        TestSourceConfigDatabase.Create(
            sourceRoot,
            BuildRows(),
            createConfigEntriesTable: false);

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => RecordingSandbox.Create(sourceRoot));

            Assert.Contains("ConfigEntries", exception.Message);
        }
        finally
        {
            Delete(sourceRoot);
        }
    }

    [Fact]
    public void Create_ShouldPreserveLastUsedSkinGamePresentationAndUnrelatedRows()
    {
        var sourceRoot = CreateSourceRoot(BuildRows(
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
        var sourceRoot = CreateSourceRoot(BuildRows(
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
            Assert.DoesNotContain("GameApiPort=1\n", config);
            Assert.DoesNotContain("GameApiPort=1\r", config);
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
    public void Create_ShouldSerializeBootstrapIniOnlyAndNeverTouchSourceDatabase()
    {
        var sourceRoot = CreateSourceRoot(BuildRows());
        var sourceDatabasePath = Path.Combine(sourceRoot, TestSourceConfigDatabase.DatabaseFileName);
        File.WriteAllText(Path.Combine(sourceRoot, "songs.db"), "database");
        File.WriteAllText(Path.Combine(sourceRoot, "songs.db-wal"), "wal");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "Cache"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "CrashReports"));

        SqliteConnection.ClearAllPools();
        var sourceDatabaseBefore = File.ReadAllBytes(sourceDatabasePath);

        RecordingSandbox? sandbox = null;
        try
        {
            sandbox = RecordingSandbox.Create(sourceRoot);
            var files = Directory.GetFiles(sandbox.AppDataRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(sandbox.AppDataRoot, path))
                .ToArray();

            Assert.Equal(new[] { "Config.ini" }, files);
            Assert.True(File.Exists(sourceDatabasePath));
            Assert.True(Directory.Exists(Path.Combine(sourceRoot, "Cache")));
        }
        finally
        {
            if (sandbox is not null)
                Delete(sandbox.RunRoot);
            SqliteConnection.ClearAllPools();
            Assert.Equal(sourceDatabaseBefore, File.ReadAllBytes(sourceDatabasePath));
            Assert.False(File.Exists(sourceDatabasePath + "-wal"));
            Assert.False(File.Exists(sourceDatabasePath + "-shm"));
            Delete(sourceRoot);
        }
    }

    [Fact]
    public async Task DeleteOnSuccessAsync_ShouldDeleteRunRoot()
    {
        var sourceRoot = CreateSourceRoot(BuildRows());
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
        var sourceRoot = CreateSourceRoot(BuildRows());
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

    private static string CreateSourceRoot(Dictionary<string, string>? rows = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "dtx-video-source", Guid.NewGuid().ToString("N"));
        TestSourceConfigDatabase.Create(root, rows ?? BuildRows());
        return root;
    }

    private static Dictionary<string, string> BuildRows(params string[] overrides)
    {
        var fixtureRoot = Path.Combine(Path.GetTempPath(), "dtx-video-config-fixture");
        return TestSourceConfigDatabase.BuildValidRows(fixtureRoot, overrides);
    }

    private static void Delete(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
