using DTXMania.Game.Lib.Config;
using DTXMania.VideoRecorder.Sandbox;
using Microsoft.Data.Sqlite;

namespace DTXMania.E2E;

/// <summary>
/// Cross-project compatibility proof (HPA-190): the recorder's read-only
/// source-database contract must stay compatible with the production
/// ConfigManager that writes it, and the sandbox bootstrap Config.ini the
/// recorder serializes must be importable by the production ConfigManager
/// that runs inside the sandbox game. This is the test that catches future
/// schema/reader drift on either side.
/// </summary>
[Trait("Category", "E2E-Support")]
public sealed class RecorderConfigCompatibilityTests
{
    [Fact]
    public async Task SandboxRoundTrip_FromRealConfigManagerSource_ShouldImportOverridesAndPreserveSource()
    {
        var root = CreateTempDirectory();
        RecordingSandbox? sandbox = null;
        try
        {
            // 1. Create the source config.db through the real explicit-path
            //    ConfigManager, with distinct persisted values.
            var sourceAppData = Path.Combine(root, "source-appdata");
            Directory.CreateDirectory(sourceAppData);
            var sourceDatabasePath = Path.Combine(sourceAppData, "config.db");
            var songsRoot = Path.Combine(root, "Songs");
            var moreSongsRoot = Path.Combine(root, "MoreSongs");
            var systemSkinRoot = Path.Combine(root, "System");
            Directory.CreateDirectory(songsRoot);
            Directory.CreateDirectory(moreSongsRoot);
            Directory.CreateDirectory(systemSkinRoot);

            var sourceManager = new ConfigManager(
                sourceDatabasePath,
                legacyIniPath: Path.Combine(sourceAppData, "Config.ini"));
            sourceManager.LoadConfig();
            sourceManager.Config.SystemSkinRoot = systemSkinRoot;
            // Persist a DISTINCT absolute custom skin path (created on disk
            // so both sides' normalization keep it verbatim) — this pins the
            // recorder's SkinPath contract (nonblank: Default or absolute)
            // through the whole round trip.
            var customSkinPath = Path.Combine(root, "CustomSkin");
            Directory.CreateDirectory(customSkinPath);
            sourceManager.SetSkinPath(customSkinPath);
            var songRootsResult = sourceManager.SetSongRoots(new[] { songsRoot, moreSongsRoot });
            Assert.Equal(SongRootUpdateStatus.Updated, songRootsResult.Status);
            sourceManager.SetScrollSpeed(150);
            sourceManager.FlushPendingSave();
            var expectedSongRoots = songRootsResult.CanonicalRoots.ToArray();

            SqliteConnection.ClearAllPools();
            var sourceDatabaseBefore = File.ReadAllBytes(sourceDatabasePath);

            // 2. Run the real RecordingSandbox.Create against the source root.
            sandbox = RecordingSandbox.Create(sourceAppData);

            // 3. The sandbox bootstrap INI exists; the recorder never wrote
            //    a sandbox database.
            Assert.True(File.Exists(sandbox.ConfigPath));
            var sandboxDatabasePath = Path.Combine(sandbox.AppDataRoot, "config.db");
            Assert.False(File.Exists(sandboxDatabasePath));
            Assert.False(File.Exists(sandboxDatabasePath + "-wal"));
            Assert.False(File.Exists(sandboxDatabasePath + "-shm"));

            // 4. Load the sandbox through the real ConfigManager: the
            //    bootstrap INI is imported and the sandbox database is
            //    created by production code.
            var sandboxManager = new ConfigManager(sandboxDatabasePath, sandbox.ConfigPath);
            sandboxManager.LoadConfig();

            // 5. Source values survived; recorder overrides were applied.
            Assert.True(File.Exists(sandboxDatabasePath));
            Assert.Equal(expectedSongRoots, sandboxManager.Config.SongRoots);
            Assert.Equal(systemSkinRoot, sandboxManager.Config.SystemSkinRoot);
            Assert.Equal(customSkinPath, sandboxManager.Config.SkinPath);
            Assert.Equal(150, sandboxManager.Config.ScrollSpeed);
            Assert.True(sandboxManager.Config.EnableGameApi);
            // The recorder's full-lane AutoPlay patch lands as exactly lanes 0..9.
            Assert.True(
                sandboxManager.Config.AutoPlayLanes.SetEquals(Enumerable.Range(0, 10)));
            Assert.True(sandboxManager.Config.NoFail);
            Assert.Equal(1280, sandboxManager.Config.ScreenWidth);
            Assert.Equal(720, sandboxManager.Config.ScreenHeight);
            Assert.False(sandboxManager.Config.FullScreen);
            Assert.Equal(sandbox.ApiPort, sandboxManager.Config.GameApiPort);
            Assert.Equal(sandbox.ApiKey, sandboxManager.Config.GameApiKey);

            // 6. The source database remains unchanged (read-only recorder).
            SqliteConnection.ClearAllPools();
            Assert.Equal(sourceDatabaseBefore, File.ReadAllBytes(sourceDatabasePath));
            var verifyManager = new ConfigManager(
                sourceDatabasePath,
                legacyIniPath: Path.Combine(sourceAppData, "missing.ini"));
            verifyManager.LoadConfig();
            Assert.Equal(expectedSongRoots, verifyManager.Config.SongRoots);
            Assert.Equal(systemSkinRoot, verifyManager.Config.SystemSkinRoot);
            Assert.Equal(customSkinPath, verifyManager.Config.SkinPath);
            Assert.Equal(150, verifyManager.Config.ScrollSpeed);

            await sandbox.DeleteOnSuccessAsync();
            sandbox = null;
        }
        finally
        {
            // Release pooled source-database handles (the verification load
            // above) before deleting the temp roots — Windows keeps files
            // locked while pooled handles are open.
            SqliteConnection.ClearAllPools();
            if (sandbox is not null)
                TryDelete(sandbox.RunRoot);
            TryDelete(root);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "dtxmaniacx-recorder-compat", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of temp artifacts.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup of temp artifacts.
        }
    }
}
