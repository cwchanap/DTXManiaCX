using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.EntityFrameworkCore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using SongScoreEntity = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Test.Song;

[Trait("Category", "Unit")]
public class SongDatabaseServiceCoverageTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _databasePath;
    private readonly SongDatabaseService _databaseService;

    public SongDatabaseServiceCoverageTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "DTXManiaCX_Tests",
            nameof(SongDatabaseServiceCoverageTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _databasePath = Path.Combine(_tempRoot, "songs.db");
        _databaseService = new SongDatabaseService(_databasePath);
    }

    [Fact]
    public void CreateContext_BeforeInitialization_ShouldThrowInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => _databaseService.CreateContext());
    }

    [Fact]
    public async Task InitializeDatabaseAsync_WithInvalidDatabaseFile_ShouldRecreateValidDatabase()
    {
        await File.WriteAllTextAsync(_databasePath, "not a sqlite database");

        await _databaseService.InitializeDatabaseAsync();

        Assert.True(await _databaseService.DatabaseExistsAsync());

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={_databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        Assert.Equal(
            (long)SongDatabaseService.CurrentSongSchemaVersion,
            (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task PurgeDatabaseAsync_ShouldDeleteDatabaseAndRequireReinitialization()
    {
        await _databaseService.InitializeDatabaseAsync();

        Assert.True(File.Exists(_databasePath));

        await _databaseService.PurgeDatabaseAsync();

        Assert.False(File.Exists(_databasePath));
        Assert.Throws<InvalidOperationException>(() => _databaseService.CreateContext());
    }

    [Fact]
    public async Task BackupAndRestoreDatabase_ShouldRoundTripSongData()
    {
        await _databaseService.InitializeDatabaseAsync();
        await AddSongAsync("Restore Me", "Coverage Bot", "restore-me.dtx");

        var backupPath = Path.Combine(_tempRoot, "songs.backup.db");

        await _databaseService.BackupDatabaseAsync(backupPath);
        await _databaseService.PurgeDatabaseAsync();
        await _databaseService.RestoreDatabaseAsync(backupPath);
        await _databaseService.InitializeDatabaseAsync();

        var songs = await _databaseService.GetSongsAsync();

        var restoredSong = Assert.Single(songs);
        Assert.Equal("Restore Me", restoredSong.Title);
        // RestoreDatabaseAsync reads from the backup but does not delete it
        Assert.True(File.Exists(backupPath));
    }

    [Fact]
    public async Task BackupDatabaseAsync_WhenDestinationIsDatabaseFile_ShouldThrowWithoutDeletingDatabase()
    {
        await _databaseService.InitializeDatabaseAsync();
        await AddSongAsync("Live Data", "Coverage Bot", "live-data.dtx");

        await Assert.ThrowsAsync<ArgumentException>(
            () => _databaseService.BackupDatabaseAsync(_databasePath));

        Assert.True(File.Exists(_databasePath));
        Assert.Single(await _databaseService.GetSongsAsync());
    }

    [Fact]
    public async Task BackupDatabaseAsync_WhenDestinationIsDatabasePathAlias_ShouldThrowWithoutDeletingDatabase()
    {
        await _databaseService.InitializeDatabaseAsync();
        await AddSongAsync("Live Data", "Coverage Bot", "live-alias.dtx");

        var aliasPath = Path.Combine(_tempRoot, "subdir", "..", "songs.db");

        await Assert.ThrowsAsync<ArgumentException>(
            () => _databaseService.BackupDatabaseAsync(aliasPath));

        Assert.True(File.Exists(_databasePath));
        Assert.Single(await _databaseService.GetSongsAsync());
    }

    [Fact]
    public async Task BackupDatabaseAsync_WhenSnapshotFails_ShouldPreserveExistingBackup()
    {
        var backupPath = Path.Combine(_tempRoot, "existing-backup.db");
        var goodBackupBytes = new byte[] { 1, 2, 3 };
        await File.WriteAllBytesAsync(backupPath, goodBackupBytes);

        var corruptPath = Path.Combine(_tempRoot, "corrupt.db");
        await File.WriteAllTextAsync(corruptPath, "not a sqlite database");
        using var corruptService = new SongDatabaseService(corruptPath);

        var exception = await Record.ExceptionAsync(
            () => corruptService.BackupDatabaseAsync(backupPath));

        Assert.NotNull(exception);
        Assert.Equal(goodBackupBytes, await File.ReadAllBytesAsync(backupPath));
    }

    [Fact]
    public async Task BackupDatabaseAsync_WhenDestinationIsDirectory_ShouldFailWithoutLeavingTempFile()
    {
        await _databaseService.InitializeDatabaseAsync();

        var directoryPath = Path.Combine(_tempRoot, "backup-dir");
        Directory.CreateDirectory(directoryPath);
        var entriesBefore = Directory.GetFileSystemEntries(_tempRoot).OrderBy(entry => entry).ToArray();

        var exception = await Record.ExceptionAsync(
            () => _databaseService.BackupDatabaseAsync(directoryPath));

        Assert.NotNull(exception);
        Assert.True(Directory.Exists(directoryPath));
        Assert.Empty(Directory.GetFiles(directoryPath));
        var entriesAfter = Directory.GetFileSystemEntries(_tempRoot).OrderBy(entry => entry).ToArray();
        Assert.Equal(entriesBefore, entriesAfter);
    }

    [Fact]
    public async Task BackupDatabaseAsync_WhenDestinationIsSymlinkedDirectoryAlias_ShouldThrowWithoutDeletingDatabase()
    {
        await _databaseService.InitializeDatabaseAsync();
        await AddSongAsync("Live Data", "Coverage Bot", "live-symlink-dir.dtx");

        var aliasDirectory = Path.Combine(_tempRoot, "alias-dir");
        Directory.CreateSymbolicLink(aliasDirectory, _tempRoot);
        var aliasPath = Path.Combine(aliasDirectory, "songs.db");

        await Assert.ThrowsAsync<ArgumentException>(
            () => _databaseService.BackupDatabaseAsync(aliasPath));

        Assert.True(File.Exists(_databasePath));
        Assert.Single(await _databaseService.GetSongsAsync());
    }

    [Fact]
    public async Task BackupDatabaseAsync_WhenDestinationIsHardLinkToDatabase_ShouldThrowWithoutDeletingDatabase()
    {
        await _databaseService.InitializeDatabaseAsync();
        await AddSongAsync("Live Data", "Coverage Bot", "live-hardlink.dtx");

        var hardLinkPath = Path.Combine(_tempRoot, "hardlink.db");
        CreateHardLink(hardLinkPath, _databasePath);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _databaseService.BackupDatabaseAsync(hardLinkPath));

        Assert.True(File.Exists(_databasePath));
        Assert.Single(await _databaseService.GetSongsAsync());
    }

    [Fact]
    public async Task BackupDatabaseAsync_WhenFileNamedLikeTempPathExists_ShouldPreserveIt()
    {
        await _databaseService.InitializeDatabaseAsync();
        await AddSongAsync("Backed Up", "Coverage Bot", "stale-tmp.dtx");

        var backupPath = Path.Combine(_tempRoot, "backup.db");
        var foreignBytes = new byte[] { 9, 8, 7 };
        await File.WriteAllBytesAsync(backupPath + ".tmp", foreignBytes);

        await _databaseService.BackupDatabaseAsync(backupPath);

        Assert.True(File.Exists(backupPath));
        Assert.Equal(foreignBytes, await File.ReadAllBytesAsync(backupPath + ".tmp"));
    }

    [Fact]
    public async Task BackupDatabaseAsync_WhenDatabasePathEndsWithTmpSuffix_ShouldNotDeleteDatabase()
    {
        var tmpNamedPath = Path.Combine(_tempRoot, "live.db.tmp");
        using var tmpNamedService = new SongDatabaseService(tmpNamedPath);
        await tmpNamedService.InitializeDatabaseAsync();

        var backupPath = Path.Combine(_tempRoot, "live.db");
        await tmpNamedService.BackupDatabaseAsync(backupPath);

        Assert.True(File.Exists(tmpNamedPath));
        Assert.Empty(await tmpNamedService.GetSongsAsync());
        Assert.True(File.Exists(backupPath));
    }

    [Fact]
    public async Task BackupDatabaseAsync_WhenFileIdentityUnavailable_ShouldFailWithoutMutating()
    {
        var servicePath = Path.Combine(_tempRoot, "identityless.db");
        using var service = new IdentityUnavailableDatabaseService(servicePath);
        await service.InitializeDatabaseAsync();

        var backupPath = Path.Combine(_tempRoot, "identityless-backup.db");
        var existingBackupBytes = new byte[] { 5, 5, 5 };
        await File.WriteAllBytesAsync(backupPath, existingBackupBytes);

        var exception = await Record.ExceptionAsync(
            () => service.BackupDatabaseAsync(backupPath));

        Assert.NotNull(exception);
        Assert.True(File.Exists(servicePath));
        Assert.Equal(existingBackupBytes, await File.ReadAllBytesAsync(backupPath));
    }

    [Fact]
    public async Task BackupDatabaseAsync_WhenIdentityUnavailableAndTempFileReplaced_ShouldPreserveForeignFile()
    {
        var servicePath = Path.Combine(_tempRoot, "identityless-swap.db");
        var foreignBytes = new byte[] { 4, 2, 0 };
        using var service = new TempFileReplacingDatabaseService(servicePath, foreignBytes);
        await service.InitializeDatabaseAsync();

        var backupPath = Path.Combine(_tempRoot, "identityless-swap-backup.db");

        var exception = await Record.ExceptionAsync(
            () => service.BackupDatabaseAsync(backupPath));

        Assert.NotNull(exception);
        Assert.NotNull(service.ObservedTempPath);
        Assert.Equal(foreignBytes, await File.ReadAllBytesAsync(service.ObservedTempPath!));
        Assert.True(File.Exists(servicePath));
        Assert.False(File.Exists(backupPath));
    }

    [Fact]
    public async Task BackupDatabaseAsync_WhenTempFileReplacedBeforeIdentityCapture_ShouldPreserveForeignFile()
    {
        var servicePath = Path.Combine(_tempRoot, "reopen-swap.db");
        var foreignBytes = new byte[] { 7, 1, 3 };
        using var service = new ReopenCaptureReplacingDatabaseService(servicePath, foreignBytes);
        await service.InitializeDatabaseAsync();

        var backupPath = Path.Combine(_tempRoot, "reopen-swap-backup.db");

        var exception = await Record.ExceptionAsync(
            () => service.BackupDatabaseAsync(backupPath));

        Assert.NotNull(exception);
        Assert.NotNull(service.ObservedTempPath);
        Assert.Equal(foreignBytes, await File.ReadAllBytesAsync(service.ObservedTempPath!));
        Assert.True(File.Exists(servicePath));
        Assert.False(File.Exists(backupPath));
    }

    [Fact]
    public async Task RestoreDatabaseAsync_WhenFileIdentityUnavailable_ShouldFailWithoutMutating()
    {
        var servicePath = Path.Combine(_tempRoot, "identityless.db");
        using var service = new IdentityUnavailableDatabaseService(servicePath);
        await service.InitializeDatabaseAsync();
        using (var context = service.CreateContext())
        {
            context.Songs.Add(CreateSong("Keep Me", "Coverage Bot"));
            await context.SaveChangesAsync();
        }

        var backupPath = Path.Combine(_tempRoot, "identityless-backup.db");
        await service.BackupDatabaseAsync(backupPath);

        var exception = await Record.ExceptionAsync(
            () => service.RestoreDatabaseAsync(backupPath));

        Assert.NotNull(exception);
        Assert.Single(await service.GetSongsAsync());
    }

    [Fact]
    public async Task RestoreDatabaseAsync_WhenSourceIsDatabaseFile_ShouldThrowWithoutMutating()
    {
        await _databaseService.InitializeDatabaseAsync();
        await AddSongAsync("Keep Me", "Coverage Bot", "keep-me.dtx");

        await Assert.ThrowsAsync<ArgumentException>(
            () => _databaseService.RestoreDatabaseAsync(_databasePath));

        Assert.Single(await _databaseService.GetSongsAsync());
    }

    [Fact]
    public async Task RestoreDatabaseAsync_WhenSourceIsDatabasePathAlias_ShouldThrowWithoutMutating()
    {
        await _databaseService.InitializeDatabaseAsync();
        await AddSongAsync("Keep Me", "Coverage Bot", "keep-alias.dtx");

        var aliasPath = Path.Combine(_tempRoot, "subdir", "..", "songs.db");

        await Assert.ThrowsAsync<ArgumentException>(
            () => _databaseService.RestoreDatabaseAsync(aliasPath));

        Assert.Single(await _databaseService.GetSongsAsync());
    }

    [Fact]
    public async Task RestoreDatabaseAsync_WhenSourceIsSymlinkedDirectoryAlias_ShouldThrowWithoutMutating()
    {
        await _databaseService.InitializeDatabaseAsync();
        await AddSongAsync("Keep Me", "Coverage Bot", "keep-symlink-dir.dtx");

        var aliasDirectory = Path.Combine(_tempRoot, "alias-dir");
        Directory.CreateSymbolicLink(aliasDirectory, _tempRoot);
        var aliasPath = Path.Combine(aliasDirectory, "songs.db");

        await Assert.ThrowsAsync<ArgumentException>(
            () => _databaseService.RestoreDatabaseAsync(aliasPath));

        Assert.Single(await _databaseService.GetSongsAsync());
    }

    [Fact]
    public async Task RestoreDatabaseAsync_WhenSourceIsHardLinkToDatabase_ShouldThrowWithoutMutating()
    {
        await _databaseService.InitializeDatabaseAsync();
        await AddSongAsync("Keep Me", "Coverage Bot", "keep-hardlink.dtx");

        var hardLinkPath = Path.Combine(_tempRoot, "hardlink.db");
        CreateHardLink(hardLinkPath, _databasePath);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _databaseService.RestoreDatabaseAsync(hardLinkPath));

        Assert.Single(await _databaseService.GetSongsAsync());
    }

    [Fact]
    public async Task RestoreDatabaseAsync_OverLiveWalDatabase_ShouldReplaceContent()
    {
        await _databaseService.InitializeDatabaseAsync();
        await AddSongAsync("Snapshot Song", "Coverage Bot", "snapshot.dtx");

        var backupPath = Path.Combine(_tempRoot, "restore-source.db");
        await _databaseService.BackupDatabaseAsync(backupPath);

        await AddSongAsync("Later Song", "Coverage Bot", "later.dtx");

        using var heldContext = _databaseService.CreateContext();
        await heldContext.Database.GetDbConnection().OpenAsync();

        await _databaseService.RestoreDatabaseAsync(backupPath);

        using var verify = _databaseService.CreateContext();
        var titles = await verify.Songs
            .OrderBy(song => song.Title)
            .Select(song => song.Title)
            .ToListAsync();
        Assert.Equal(new[] { "Snapshot Song" }, titles);
    }

    [Fact]
    public async Task RestoreDatabaseAsync_WhenSourceIsNotSqlite_ShouldPreserveExistingDatabase()
    {
        await _databaseService.InitializeDatabaseAsync();
        await AddSongAsync("Kept", "Coverage Bot", "kept.dtx");

        var badBackupPath = Path.Combine(_tempRoot, "bad-backup.db");
        await File.WriteAllTextAsync(badBackupPath, "not a sqlite database");

        var exception = await Record.ExceptionAsync(
            () => _databaseService.RestoreDatabaseAsync(badBackupPath));

        Assert.NotNull(exception);

        var songs = await _databaseService.GetSongsAsync();
        Assert.Single(songs, song => song.Title == "Kept");

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={_databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        Assert.Equal(
            (long)SongDatabaseService.CurrentSongSchemaVersion,
            (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task RestoreDatabaseAsync_WhenSourceVersionIsLegacyZero_ShouldPreserveExistingDatabase()
    {
        await _databaseService.InitializeDatabaseAsync();
        await AddSongAsync("Kept", "Coverage Bot", "kept-v0.dtx");

        var backupPath = Path.Combine(_tempRoot, "legacy-v0-backup.db");
        await using (var legacy = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={backupPath};Pooling=False"))
        {
            await legacy.OpenAsync();
            await using var createTable = legacy.CreateCommand();
            createTable.CommandText = "CREATE TABLE Legacy (Id INTEGER PRIMARY KEY);";
            await createTable.ExecuteNonQueryAsync();
        }
        Assert.Equal(0L, await ReadUserVersionAsync(backupPath));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _databaseService.RestoreDatabaseAsync(backupPath));

        Assert.Single(await _databaseService.GetSongsAsync());
        Assert.Equal(
            (long)SongDatabaseService.CurrentSongSchemaVersion,
            await ReadUserVersionAsync(_databasePath));
    }

    [Fact]
    public async Task RestoreDatabaseAsync_WhenSourceVersionMismatch_ShouldPreserveExistingDatabase()
    {
        await _databaseService.InitializeDatabaseAsync();
        await AddSongAsync("Kept", "Coverage Bot", "kept-mismatch.dtx");

        var backupPath = Path.Combine(_tempRoot, "mismatch-backup.db");
        await _databaseService.BackupDatabaseAsync(backupPath);
        await SetUserVersionAsync(backupPath, 999);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _databaseService.RestoreDatabaseAsync(backupPath));

        Assert.Single(await _databaseService.GetSongsAsync());
        Assert.Equal(
            (long)SongDatabaseService.CurrentSongSchemaVersion,
            await ReadUserVersionAsync(_databasePath));
    }

    [Fact]
    public async Task BackupDatabaseAsync_WhenDestinationHasLiveWal_ShouldPreserveExistingBackup()
    {
        await _databaseService.InitializeDatabaseAsync();
        await AddSongAsync("Backed Up", "Coverage Bot", "live-wal-dest.dtx");

        var backupPath = Path.Combine(_tempRoot, "live-wal-backup.db");
        await using var heldConnection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={backupPath};Pooling=False");
        await heldConnection.OpenAsync();
        await ExecuteNonQueryAsync(heldConnection, "PRAGMA journal_mode=WAL;");
        await ExecuteNonQueryAsync(
            heldConnection,
            "CREATE TABLE Payload (Id INTEGER PRIMARY KEY);");
        await ExecuteNonQueryAsync(
            heldConnection,
            "INSERT INTO Payload (Id) VALUES (1);");
        Assert.True(File.Exists(backupPath + "-wal"));

        var destinationSnapshot = await ReadAllBytesSharedAsync(backupPath);
        var walSnapshot = await ReadAllBytesSharedAsync(backupPath + "-wal");
        var entriesBefore = Directory.GetFileSystemEntries(_tempRoot)
            .OrderBy(entry => entry)
            .ToArray();

        var exception = await Record.ExceptionAsync(
            () => _databaseService.BackupDatabaseAsync(backupPath));

        Assert.NotNull(exception);
        Assert.Equal(destinationSnapshot, await ReadAllBytesSharedAsync(backupPath));
        Assert.Equal(walSnapshot, await ReadAllBytesSharedAsync(backupPath + "-wal"));

        await using var verify = heldConnection.CreateCommand();
        verify.CommandText = "SELECT COUNT(*) FROM Payload";
        Assert.Equal(1L, (long)(await verify.ExecuteScalarAsync())!);

        var entriesAfter = Directory.GetFileSystemEntries(_tempRoot)
            .OrderBy(entry => entry)
            .ToArray();
        Assert.Equal(entriesBefore, entriesAfter);
    }

    [Theory]
    [InlineData("-wal")]
    [InlineData("-shm")]
    [InlineData("-journal")]
    public async Task BackupDatabaseAsync_WhenDestinationHasJournalSidecar_ShouldPreserveExistingBackup(
        string sidecarSuffix)
    {
        await _databaseService.InitializeDatabaseAsync();

        var backupPath = Path.Combine(_tempRoot, "sidecar-backup.db");
        var backupBytes = new byte[] { 3, 1, 4, 1, 5 };
        var sidecarBytes = new byte[] { 9, 2, 6 };
        await File.WriteAllBytesAsync(backupPath, backupBytes);
        await File.WriteAllBytesAsync(backupPath + sidecarSuffix, sidecarBytes);

        var exception = await Record.ExceptionAsync(
            () => _databaseService.BackupDatabaseAsync(backupPath));

        Assert.NotNull(exception);
        Assert.Equal(backupBytes, await File.ReadAllBytesAsync(backupPath));
        Assert.Equal(sidecarBytes, await File.ReadAllBytesAsync(backupPath + sidecarSuffix));
    }

    [Fact]
    public async Task BackupDatabaseAsync_WhenDestinationHasHeldDeleteModeConnection_ShouldUpdateDestinationInPlace()
    {
        await _databaseService.InitializeDatabaseAsync();
        await AddSongAsync("In Place", "Coverage Bot", "in-place.dtx");

        var backupPath = Path.Combine(_tempRoot, "held-delete-backup.db");
        await using var heldConnection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={backupPath};Pooling=False");
        await heldConnection.OpenAsync();
        await ExecuteNonQueryAsync(heldConnection, "PRAGMA journal_mode=DELETE;");
        await ExecuteNonQueryAsync(
            heldConnection,
            "CREATE TABLE Payload (Id INTEGER PRIMARY KEY);");
        await ExecuteNonQueryAsync(
            heldConnection,
            "INSERT INTO Payload (Id) VALUES (1);");
        Assert.False(File.Exists(backupPath + "-journal"));
        Assert.False(File.Exists(backupPath + "-wal"));
        Assert.False(File.Exists(backupPath + "-shm"));

        await _databaseService.BackupDatabaseAsync(backupPath);

        await using var verify = heldConnection.CreateCommand();
        verify.CommandText = "SELECT Title FROM Songs";
        Assert.Equal("In Place", (string)(await verify.ExecuteScalarAsync())!);
        Assert.False(File.Exists(backupPath + "-journal"));
        Assert.False(File.Exists(backupPath + "-wal"));
        Assert.False(File.Exists(backupPath + "-shm"));
    }

    [Fact]
    public async Task BackupDatabaseAsync_WhenDestinationConnectionWritesDuringBackup_ShouldUpdateDestinationInPlace()
    {
        var servicePath = Path.Combine(_tempRoot, "interleave-src.db");
        var backupPath = Path.Combine(_tempRoot, "interleave-backup.db");

        await using var heldConnection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={backupPath};Pooling=False");
        await heldConnection.OpenAsync();
        await ExecuteNonQueryAsync(heldConnection, "PRAGMA journal_mode=DELETE;");
        await ExecuteNonQueryAsync(
            heldConnection,
            "CREATE TABLE Payload (Id INTEGER PRIMARY KEY);");
        await ExecuteNonQueryAsync(
            heldConnection,
            "INSERT INTO Payload (Id) VALUES (1);");

        var journalObservedDuringBackup = false;
        using var service = new InterleavedWriteDatabaseService(servicePath, async () =>
        {
            await ExecuteNonQueryAsync(heldConnection, "BEGIN IMMEDIATE;");
            await ExecuteNonQueryAsync(
                heldConnection,
                "INSERT INTO Payload (Id) VALUES (2);");
            journalObservedDuringBackup = File.Exists(backupPath + "-journal");
            await ExecuteNonQueryAsync(heldConnection, "COMMIT;");
        });
        await service.InitializeDatabaseAsync();
        await service.AddSongAsync(
            CreateSong("Interleaved", "Coverage Bot"),
            CreateChart("interleaved.dtx", 40));

        await service.BackupDatabaseAsync(backupPath);

        Assert.True(journalObservedDuringBackup);
        await using var verify = heldConnection.CreateCommand();
        verify.CommandText = "SELECT Title FROM Songs";
        Assert.Equal("Interleaved", (string)(await verify.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task BackupDatabaseAsync_WhenDestinationHoldsOpenWriteTransaction_ShouldFailAndPreserveDestination()
    {
        var servicePath = Path.Combine(_tempRoot, "busy-src.db");
        var backupPath = Path.Combine(_tempRoot, "busy-backup.db");

        await using var heldConnection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={backupPath};Pooling=False");
        await heldConnection.OpenAsync();
        await ExecuteNonQueryAsync(heldConnection, "PRAGMA journal_mode=DELETE;");
        await ExecuteNonQueryAsync(
            heldConnection,
            "CREATE TABLE Payload (Id INTEGER PRIMARY KEY);");
        await ExecuteNonQueryAsync(
            heldConnection,
            "INSERT INTO Payload (Id) VALUES (1);");

        using var service = new InterleavedWriteDatabaseService(servicePath, async () =>
        {
            await ExecuteNonQueryAsync(heldConnection, "BEGIN IMMEDIATE;");
            await ExecuteNonQueryAsync(
                heldConnection,
                "INSERT INTO Payload (Id) VALUES (2);");
        });
        await service.InitializeDatabaseAsync();
        await service.AddSongAsync(
            CreateSong("Busy", "Coverage Bot"),
            CreateChart("busy.dtx", 40));

        var exception = await Record.ExceptionAsync(
            () => service.BackupDatabaseAsync(backupPath));

        Assert.NotNull(exception);
        Assert.True(File.Exists(backupPath + "-journal"));

        await ExecuteNonQueryAsync(heldConnection, "COMMIT;");

        await using var verify = heldConnection.CreateCommand();
        verify.CommandText = "SELECT COUNT(*) FROM Payload";
        Assert.Equal(2L, (long)(await verify.ExecuteScalarAsync())!);

        await using var noSnapshot = heldConnection.CreateCommand();
        noSnapshot.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE name = 'Songs'";
        Assert.Equal(0L, (long)(await noSnapshot.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task BackupDatabaseAsync_WhenDestinationIsNotSqlite_ShouldPreserveDestination()
    {
        await _databaseService.InitializeDatabaseAsync();
        await AddSongAsync("Kept", "Coverage Bot", "non-sqlite-dest.dtx");

        var backupPath = Path.Combine(_tempRoot, "not-a-database.db");
        var foreignBytes = new byte[] { 1, 2, 3 };
        await File.WriteAllBytesAsync(backupPath, foreignBytes);

        var exception = await Record.ExceptionAsync(
            () => _databaseService.BackupDatabaseAsync(backupPath));

        Assert.NotNull(exception);
        Assert.Equal(foreignBytes, await File.ReadAllBytesAsync(backupPath));
    }

    [Fact]
    public async Task TryGetFileIdentity_WhenMetadataChanges_ShouldRemainStable()
    {
        var filePath = Path.Combine(_tempRoot, "identity-meta.db");
        await File.WriteAllTextAsync(filePath, "x");

        Assert.True(_databaseService.TryGetFileIdentity(filePath, out var baseline));

        File.SetAttributes(filePath, File.GetAttributes(filePath) | FileAttributes.ReadOnly);
        Assert.True(_databaseService.TryGetFileIdentity(filePath, out var afterModeChange));
        Assert.Equal(baseline, afterModeChange);
        File.SetAttributes(filePath, File.GetAttributes(filePath) & ~FileAttributes.ReadOnly);

        var linkPath = Path.Combine(_tempRoot, "identity-meta-link.db");
        CreateHardLink(linkPath, filePath);
        Assert.True(_databaseService.TryGetFileIdentity(filePath, out var afterLinkChange));
        Assert.Equal(baseline, afterLinkChange);
        Assert.True(_databaseService.TryGetFileIdentity(linkPath, out var linkIdentity));
        Assert.Equal(baseline, linkIdentity);
    }

    [Fact]
    public async Task TryGetFileIdentity_ForDistinctFiles_ShouldDiffer()
    {
        var firstPath = Path.Combine(_tempRoot, "identity-first.db");
        var secondPath = Path.Combine(_tempRoot, "identity-second.db");
        await File.WriteAllTextAsync(firstPath, "x");
        await File.WriteAllTextAsync(secondPath, "x");

        Assert.True(_databaseService.TryGetFileIdentity(firstPath, out var firstIdentity));
        Assert.True(_databaseService.TryGetFileIdentity(secondPath, out var secondIdentity));
        Assert.NotEqual(firstIdentity, secondIdentity);
    }

    [Fact]
    public async Task SearchSongsAsync_ShouldMatchTitleAndArtist()
    {
        await _databaseService.InitializeDatabaseAsync();
        await AddSongAsync("Blue Sky", "Alice", "blue-sky.dtx");
        await AddSongAsync("Red Moon", "Bob", "red-moon.dtx");

        var byTitle = await _databaseService.SearchSongsAsync("Blue");
        var byArtist = await _databaseService.SearchSongsAsync("Bob");

        var titleMatch = Assert.Single(byTitle);
        Assert.Equal("Blue Sky", titleMatch.Title);

        var artistMatch = Assert.Single(byArtist);
        Assert.Equal("Red Moon", artistMatch.Title);
    }

    [Fact]
    public async Task GetSongWithChartsAsync_WhenSongExists_ShouldReturnSongAndCharts()
    {
        await _databaseService.InitializeDatabaseAsync();

        var (songId, firstChart) = await AddSongAsync("Grouped Song", "Coverage Bot", "grouped-basic.dtx", drumLevel: 25);
        var secondChart = CreateChart("grouped-advanced.dtx", drumLevel: 65);

        await _databaseService.AddSongAsync(
            CreateSong("Grouped Song", "Coverage Bot"),
            secondChart);

        var result = await _databaseService.GetSongWithChartsAsync(songId);

        Assert.NotNull(result);
        Assert.Equal("Grouped Song", result!.Value.song.Title);
        Assert.Equal(2, result.Value.charts.Length);
        Assert.Contains(result.Value.charts, chart => chart.FilePath == firstChart.FilePath);
        Assert.Contains(result.Value.charts, chart => chart.FilePath == secondChart.FilePath);
    }

    [Fact]
    public async Task GetSongWithChartsAsync_WhenSongDoesNotExist_ShouldReturnNull()
    {
        await _databaseService.InitializeDatabaseAsync();

        var result = await _databaseService.GetSongWithChartsAsync(9999);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSongWithChartsAsync_ShouldIncludePersistedScores()
    {
        await _databaseService.InitializeDatabaseAsync();

        var (songId, chart) = await AddSongAsync("Scored Song", "Coverage Bot", "scored-song.dtx", drumLevel: 50);

        // Submit a score so there is a persisted SongScore row
        await _databaseService.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, 850_000, 88.0, fullCombo: false);

        var result = await _databaseService.GetSongWithChartsAsync(songId);

        Assert.NotNull(result);
        var returnedChart = Assert.Single(result!.Value.charts);
        Assert.NotNull(returnedChart.Scores);
        var persistedScore = Assert.Single(returnedChart.Scores);
        Assert.Equal(850_000, persistedScore.BestScore);
        Assert.Equal(88.0, persistedScore.BestAchievementRate);
        Assert.Equal(1, persistedScore.PlayCount);
    }

    [Fact]
    public async Task GetSongsAsync_ShouldIncludeScoreScopedPerformanceHistory()
    {
        await _databaseService.InitializeDatabaseAsync();

        var (songId, chart) = await AddSongAsync("History Song", "Coverage Bot", "history-song.dtx", drumLevel: 55);

        await _databaseService.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, 850_000, 88.0, fullCombo: false);
        var savedScore = await LoadScoreAsync(chart.Id, EInstrumentPart.DRUMS);

        using (var context = _databaseService.CreateContext())
        {
            context.PerformanceHistory.Add(new PerformanceHistory
            {
                SongId = songId,
                SongScoreId = savedScore.Id,
                PerformedAt = new DateTime(2026, 6, 13, 12, 0, 0, DateTimeKind.Utc),
                DisplayOrder = 1,
                HistoryLine = "1.26/6/13 Cleared (A: 88.00)"
            });
            await context.SaveChangesAsync();
        }

        var songs = await _databaseService.GetSongsAsync();

        var loadedSong = Assert.Single(songs, song => song.Id == songId);
        var loadedChart = Assert.Single(loadedSong.Charts);
        var loadedScore = Assert.Single(loadedChart.Scores);
        var history = Assert.Single(loadedScore.PerformanceHistory);
        Assert.Equal(savedScore.Id, history.SongScoreId);
        Assert.Equal("1.26/6/13 Cleared (A: 88.00)", history.HistoryLine);
    }

    [Fact]
    public async Task UpdateScoreAsync_WhenNewBestScoreSubmitted_ShouldUpdateBestLastAndCounters()
    {
        await _databaseService.InitializeDatabaseAsync();
        var (_, chart) = await AddSongAsync("High Score", "Coverage Bot", "high-score.dtx", drumLevel: 75);

        await _databaseService.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, 900_000, 95.5, fullCombo: true);

        var score = await LoadScoreAsync(chart.Id, EInstrumentPart.DRUMS);

        Assert.Equal(900_000, score.BestScore);
        Assert.Equal(900_000, score.LastScore);
        Assert.Equal(95.5, score.BestAchievementRate);
        Assert.True(score.FullCombo);
        Assert.Equal(1, score.PlayCount);
        Assert.Equal(1, score.ClearCount);
        Assert.NotNull(score.LastPlayedAt);
    }

    [Fact]
    public async Task UpdateScoreAsync_WhenScoreIsNotANewBest_ShouldPreserveBestButAdvanceLastAndCounts()
    {
        await _databaseService.InitializeDatabaseAsync();
        var (_, chart) = await AddSongAsync("Score History", "Coverage Bot", "score-history.dtx", drumLevel: 70);

        await _databaseService.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, 900_000, 95.5, fullCombo: true);
        await _databaseService.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, 500_000, 55.0, fullCombo: false);

        var score = await LoadScoreAsync(chart.Id, EInstrumentPart.DRUMS);

        Assert.Equal(900_000, score.BestScore);
        Assert.Equal(95.5, score.BestAchievementRate);
        Assert.True(score.FullCombo);
        Assert.Equal(500_000, score.LastScore);
        Assert.Equal(2, score.PlayCount);
        Assert.Equal(2, score.ClearCount);
    }

    [Fact]
    public async Task UpdateScoreAsync_NewBestScoreWithoutFullCombo_ShouldPreserveExistingFullCombo()
    {
        // Regression: the older overload also unconditionally overwrote FullCombo on new best.
        await _databaseService.InitializeDatabaseAsync();
        var (_, chart) = await AddSongAsync("FC Regression", "Coverage Bot", "fc-regression.dtx", drumLevel: 70);

        await _databaseService.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, 900_000, 95.5, fullCombo: true);
        await _databaseService.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, 950_000, 96.0, fullCombo: false);

        var score = await LoadScoreAsync(chart.Id, EInstrumentPart.DRUMS);

        Assert.Equal(950_000, score.BestScore);
        Assert.True(score.FullCombo);  // preserved from the first clear, not overwritten
    }

    [Fact]
    public async Task GetTopScoresAsync_ShouldReturnScoresSortedByBestScoreAndRespectLimit()
    {
        await _databaseService.InitializeDatabaseAsync();

        var (_, firstChart) = await AddSongAsync("First", "Coverage Bot", "first.dtx", drumLevel: 50);
        var (_, secondChart) = await AddSongAsync("Second", "Coverage Bot", "second.dtx", drumLevel: 50);
        var (_, thirdChart) = await AddSongAsync("Third", "Coverage Bot", "third.dtx", drumLevel: 50);

        await _databaseService.UpdateScoreAsync(firstChart.Id, EInstrumentPart.DRUMS, 600_000, 60.0, fullCombo: false);
        await _databaseService.UpdateScoreAsync(secondChart.Id, EInstrumentPart.DRUMS, 950_000, 98.0, fullCombo: true);
        await _databaseService.UpdateScoreAsync(thirdChart.Id, EInstrumentPart.DRUMS, 800_000, 83.0, fullCombo: false);

        var topScores = await _databaseService.GetTopScoresAsync(EInstrumentPart.DRUMS, limit: 2);

        Assert.Equal(2, topScores.Count);
        Assert.Equal(950_000, topScores[0].BestScore);
        Assert.Equal("Second", topScores[0].Chart.Song.Title);
        Assert.Equal(800_000, topScores[1].BestScore);
        Assert.Equal("Third", topScores[1].Chart.Song.Title);
    }

    [Fact]
    public async Task CleanupStaleChartsAsync_ShouldRemoveMissingChartsAndOrphanedSongs()
    {
        await _databaseService.InitializeDatabaseAsync();

        await AddSongAsync("Keep Me", "Coverage Bot", "keep-me.dtx", drumLevel: 40);

        var missingChartPath = Path.Combine(_tempRoot, "missing-chart.dtx");
        await _databaseService.AddSongAsync(
            CreateSong("Remove Me", "Coverage Bot"),
            new SongChart
            {
                FilePath = missingChartPath,
                Duration = 120,
                Bpm = 180,
                DrumLevel = 50,
                HasDrumChart = true
            });

        await _databaseService.CleanupStaleChartsAsync();

        var songs = await _databaseService.GetSongsAsync();

        var remainingSong = Assert.Single(songs);
        Assert.Equal("Keep Me", remainingSong.Title);
        Assert.Single(remainingSong.Charts);
        Assert.DoesNotContain(songs, song => song.Title == "Remove Me");
    }

    public void Dispose()
    {
        _databaseService.Dispose();

        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }
        catch
        {
            // Best-effort cleanup for temp test assets.
        }
    }

    private async Task<(int SongId, SongChart Chart)> AddSongAsync(
        string title,
        string artist,
        string fileName,
        int drumLevel = 40)
    {
        var song = CreateSong(title, artist);
        var chart = CreateChart(fileName, drumLevel);
        var songId = await _databaseService.AddSongAsync(song, chart);
        return (songId, chart);
    }

    private SongEntity CreateSong(string title, string artist)
    {
        return new SongEntity
        {
            Title = title,
            Artist = artist,
            Genre = "Coverage"
        };
    }

    private SongChart CreateChart(string fileName, int drumLevel)
    {
        var filePath = Path.Combine(_tempRoot, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, "#TITLE:Coverage");

        return new SongChart
        {
            FilePath = filePath,
            Duration = 123.45,
            Bpm = 180.0,
            DrumLevel = drumLevel,
            HasDrumChart = drumLevel > 0
        };
    }

    private async Task<SongScoreEntity> LoadScoreAsync(int chartId, EInstrumentPart instrument)
    {
        using var context = _databaseService.CreateContext();
        return await context.SongScores.SingleAsync(score => score.ChartId == chartId && score.Instrument == instrument);
    }

    private static async Task<long> ReadUserVersionAsync(string databasePath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task SetUserVersionAsync(string databasePath, long version)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version};";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteNonQueryAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<byte[]> ReadAllBytesSharedAsync(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var snapshot = new MemoryStream();
        await stream.CopyToAsync(snapshot);
        return snapshot.ToArray();
    }

    private static void CreateHardLink(string linkPath, string targetPath)
    {
        var created = OperatingSystem.IsWindows()
            ? CreateHardLinkW(linkPath, targetPath, IntPtr.Zero)
            : link(targetPath, linkPath) == 0;
        if (!created)
        {
            throw new IOException(
                $"Failed to create hard link '{linkPath}' (error {Marshal.GetLastPInvokeError()})");
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int link(string oldpath, string newpath);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);

    private sealed class IdentityUnavailableDatabaseService : SongDatabaseService
    {
        public IdentityUnavailableDatabaseService(string databasePath)
            : base(databasePath)
        {
        }

        internal override bool TryGetOpenFileIdentity(
            SafeFileHandle handle,
            out (ulong, ulong) identity)
        {
            identity = default;
            return false;
        }
    }

    private sealed class TempFileReplacingDatabaseService : SongDatabaseService
    {
        private readonly byte[] _replacementBytes;

        public TempFileReplacingDatabaseService(
            string databasePath,
            byte[] replacementBytes)
            : base(databasePath)
        {
            _replacementBytes = replacementBytes;
        }

        public string? ObservedTempPath { get; private set; }

        internal override bool TryGetOpenFileIdentity(
            SafeFileHandle handle,
            out (ulong, ulong) identity)
        {
            identity = default;
            return false;
        }

        internal override async Task WriteSnapshotToTempFileAsync(
            SongDbContext context,
            string tempBackupPath)
        {
            ObservedTempPath = tempBackupPath;
            File.Delete(tempBackupPath);
            await File.WriteAllBytesAsync(tempBackupPath, _replacementBytes);
            throw new IOException("Simulated snapshot failure after replacement");
        }
    }

    private sealed class ReopenCaptureReplacingDatabaseService : SongDatabaseService
    {
        private readonly byte[] _replacementBytes;
        private bool _replacementInstalled;

        public ReopenCaptureReplacingDatabaseService(
            string databasePath,
            byte[] replacementBytes)
            : base(databasePath)
        {
            _replacementBytes = replacementBytes;
        }

        public string? ObservedTempPath { get; private set; }

        internal override bool TryGetFileIdentity(
            string fullPath,
            out (ulong, ulong) identity)
        {
            if (!_replacementInstalled
                && Path.GetFileName(fullPath) != Path.GetFileName(DatabasePath))
            {
                _replacementInstalled = true;
                File.Delete(fullPath);
                File.WriteAllBytes(fullPath, _replacementBytes);
            }
            return base.TryGetFileIdentity(fullPath, out identity);
        }

        internal override Task WriteSnapshotToTempFileAsync(
            SongDbContext context,
            string tempBackupPath)
        {
            ObservedTempPath = tempBackupPath;
            throw new IOException("Simulated snapshot failure");
        }
    }

    private sealed class InterleavedWriteDatabaseService : SongDatabaseService
    {
        private readonly Func<Task> _interleave;

        public InterleavedWriteDatabaseService(
            string databasePath,
            Func<Task> interleave)
            : base(databasePath)
        {
            _interleave = interleave;
        }

        internal override async Task WriteSnapshotToTempFileAsync(
            SongDbContext context,
            string tempBackupPath)
        {
            await base.WriteSnapshotToTempFileAsync(context, tempBackupPath);
            await _interleave();
        }
    }
}
