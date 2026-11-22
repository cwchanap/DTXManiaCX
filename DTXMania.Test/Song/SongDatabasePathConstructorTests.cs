using System;
using System.IO;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song;

/// <summary>
/// HPA-801 Decision A: the game constructs <see cref="SongDatabase"/> from
/// its path constructor during ordinary <c>BaseGame</c> load, so the constructor
/// must not touch the filesystem. The database directory is created by
/// <see cref="SongDatabase.InitializeDatabaseAsync"/> instead — Startup's
/// initialization phase — so sandboxed tests and non-running games never write
/// into host AppData.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SongDatabasePathConstructorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "HPA-801-DbCtor", Guid.NewGuid().ToString("N"));

    [Fact]
    public void PathConstructor_ShouldNotCreateDatabaseDirectory()
    {
        var databasePath = Path.Combine(_root, "nested", "songs.db");

        var service = new SongDatabase(databasePath);

        Assert.Equal(databasePath, service.DatabasePath);
        Assert.False(Directory.Exists(_root),
            "constructing the game-owned database must not write directories; " +
            "ordinary BaseGame construction runs in sandboxed and test contexts");
    }

    [Fact]
    public async Task InitializeDatabaseAsync_ShouldCreateDatabaseDirectoryAndFile()
    {
        var databasePath = Path.Combine(_root, "nested", "songs.db");
        var service = new SongDatabase(databasePath);

        await service.InitializeDatabaseAsync();

        Assert.True(Directory.Exists(Path.GetDirectoryName(databasePath)),
            "initialization must create the database directory (moved from the constructor)");
        Assert.True(File.Exists(databasePath),
            "initialization must create the database file");
        using var context = service.CreateContext();
        Assert.True(await context.Database.CanConnectAsync());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp test assets.
        }
    }
}
