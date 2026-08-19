using System.Collections.Generic;
using System.IO;
using System.Linq;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Utilities;
using Microsoft.Data.Sqlite;

namespace DTXMania.Test.Config;

[Collection("AppPaths")]
[Trait("Category", "Unit")]
public sealed class SongRootConfigTests
{
    [Fact]
    public void LoadConfig_ShouldClearSongRootsBeforeSecondParse()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var firstRoot = Path.Combine(root, "roots", "first");
            var secondRoot = Path.Combine(root, "roots", "second");
            var replacementRoot = Path.Combine(root, "roots", "replacement");
            var dbPath = Path.Combine(root, "config.db");
            var store = new SqliteConfigStore(dbPath);
            store.Save(new Dictionary<string, string>
            {
                ["SongRoot.0"] = firstRoot,
                ["SongRoot.1"] = secondRoot,
            });
            var manager = new ConfigManager(dbPath, Path.Combine(root, "Config.ini"));

            manager.LoadConfig();
            store.Save(new Dictionary<string, string>
            {
                ["SongRoot.0"] = replacementRoot,
            });
            manager.LoadConfig();

            Assert.Equal([replacementRoot], manager.Config.SongRoots);
            Assert.Equal(replacementRoot, manager.Config.DTXPath);
        });
    }

    [Fact]
    public void LoadConfig_ShouldReadIndexedRootsInNumericOrder()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var rootZero = Path.Combine(root, "roots", "zero");
            var rootTwo = Path.Combine(root, "roots", "two");
            var rootTen = Path.Combine(root, "roots", "ten=preserved");
            var iniPath = Path.Combine(root, "Config.ini");
            File.WriteAllLines(iniPath,
            [
                "[System]",
                $"SongRoot.10={rootTen}",
                "[Other]",
                $"SongRoot.2={rootTwo}",
                $"SongRoot.0={rootZero}",
            ]);

            var manager = CreateManager(root, iniPath);
            manager.LoadConfig();

            Assert.Equal([rootZero, rootTwo, rootTen], manager.Config.SongRoots);
            Assert.Equal(rootZero, manager.Config.DTXPath);
        });
    }

    [Fact]
    public void LoadConfig_ShouldUseLastDuplicateIndex()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var rootZero = Path.Combine(root, "roots", "zero");
            var firstRootOne = Path.Combine(root, "roots", "first-one");
            var lastRootOne = Path.Combine(root, "roots", "last-one");
            var iniPath = Path.Combine(root, "Config.ini");
            File.WriteAllLines(iniPath,
            [
                "[System]",
                $"SongRoot.1={firstRootOne}",
                "[Other]",
                $"SongRoot.0={rootZero}",
                $"SongRoot.1={lastRootOne}",
            ]);

            var manager = CreateManager(root, iniPath);
            manager.LoadConfig();

            Assert.Equal([rootZero, lastRootOne], manager.Config.SongRoots);
        });
    }

    [Fact]
    public void LoadConfig_ShouldPreferIndexedRootsOverLegacyDTXPath()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var legacyRoot = Path.Combine(root, "roots", "legacy");
            // Indexed roots are authoritative custom roots, including an authored
            // path named Songs; only the legacy DTXPath representation migrates.
            var indexedRoot = Path.Combine(root, "Songs");
            var iniPath = Path.Combine(root, "Config.ini");
            File.WriteAllLines(iniPath,
            [
                "[System]",
                $"DTXPath={legacyRoot}",
                $"SongRoot.0={indexedRoot}",
            ]);

            var manager = CreateManager(root, iniPath);
            manager.LoadConfig();

            Assert.Equal([indexedRoot], manager.Config.SongRoots);
            Assert.Equal(indexedRoot, manager.Config.DTXPath);
        });
    }

    [Fact]
    public void LoadConfig_ShouldMigrateLegacyDtxPathImportIntoIndexedDbRow()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var legacyRoot = Path.Combine(root, "roots", "legacy");
            var iniPath = Path.Combine(root, "Config.ini");
            var iniBeforeImport = $"[System]\nDTXPath={legacyRoot}\n";
            File.WriteAllText(iniPath, iniBeforeImport);

            var manager = CreateManager(root, iniPath);
            manager.LoadConfig();

            Assert.Equal([legacyRoot], manager.Config.SongRoots);
            Assert.Equal(legacyRoot, manager.Config.DTXPath);

            // The migration persists SongRoot.0 to the database — DTXPath stays
            // an in-memory mirror only — and leaves the legacy INI untouched.
            var rows = new SqliteConfigStore(Path.Combine(root, "config.db")).Load();
            Assert.Equal(legacyRoot, rows["SongRoot.0"]);
            Assert.False(rows.ContainsKey("DTXPath"));
            Assert.Equal(iniBeforeImport, File.ReadAllText(iniPath));
            Assert.False(Directory.Exists(legacyRoot));
        });
    }

    [Fact]
    public void LoadConfig_LegacyCaseVariant_ShouldFollowPlatformSongPathPolicy()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var defaultRoot = AppPaths.GetDefaultSongsPath();
            var caseVariantLegacyRoot = Path.Combine(root, "SONGS");
            var iniPath = Path.Combine(root, "Config.ini");
            File.WriteAllLines(iniPath,
            [
                "[System]",
                $"DTXPath={caseVariantLegacyRoot}",
            ]);

            var manager = CreateManager(root, iniPath);
            manager.LoadConfig();

            var expectsCaseInsensitiveSongPaths =
                OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
            var expectedRoot = expectsCaseInsensitiveSongPaths
                ? defaultRoot
                : caseVariantLegacyRoot;
            Assert.Equal([expectedRoot], manager.Config.SongRoots);
        });
    }

    [Fact]
    public void LoadConfig_CaseVariantManagedDefault_ShouldCreateDirectoryBySongPathPolicy()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var defaultRoot = AppPaths.GetDefaultSongsPath();
            var caseVariantDefaultRoot = Path.Combine(root, "dtxfiles");
            var iniPath = Path.Combine(root, "Config.ini");
            File.WriteAllLines(iniPath,
            [
                "[System]",
                $"SongRoot.0={caseVariantDefaultRoot}",
            ]);

            var manager = CreateManager(root, iniPath);
            manager.LoadConfig();

            var expectsCaseInsensitiveSongPaths =
                OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
            Assert.Equal(expectsCaseInsensitiveSongPaths, Directory.Exists(defaultRoot));
        });
    }

    [Fact]
    public void FlushPendingSave_ShouldWriteDenseIndexesAndFirstRootMirror()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var firstRoot = Path.Combine(root, "roots", "first");
            var secondRoot = Path.Combine(root, "roots", "second");
            var manager = CreateManager(root, Path.Combine(root, "Config.ini"));
            manager.LoadConfig();
            manager.Config.SongRoots.Clear();
            manager.Config.SongRoots.Add(firstRoot);
            manager.Config.SongRoots.Add(secondRoot);
            manager.Config.DTXPath = Path.Combine(root, "roots", "stale-mirror");

            manager.SetNoFail(!manager.Config.NoFail); // marks a deferred save
            manager.FlushPendingSave();

            var rows = new SqliteConfigStore(Path.Combine(root, "config.db")).Load();
            Assert.Equal(firstRoot, manager.Config.DTXPath);
            Assert.Equal(firstRoot, rows["SongRoot.0"]);
            Assert.Equal(secondRoot, rows["SongRoot.1"]);
            Assert.DoesNotContain(rows.Keys, key => key.StartsWith("SongRoot.2", System.StringComparison.Ordinal));
            // DTXPath is intentionally NOT a persisted row.
            Assert.False(rows.ContainsKey("DTXPath"));
            Assert.False(Directory.Exists(firstRoot));
            Assert.False(Directory.Exists(secondRoot));
        });
    }

    [Fact]
    public void LoadConfig_ShouldRemainSectionAgnostic()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var firstRoot = Path.Combine(root, "roots", "first");
            var secondRoot = Path.Combine(root, "roots", "second");
            var iniPath = Path.Combine(root, "Config.ini");
            File.WriteAllLines(iniPath,
            [
                "[System]",
                $"SongRoot.1={secondRoot}",
                "[Other]",
                "DTXManiaVersion=ReadFromOtherSection",
                $"SongRoot.0={firstRoot}",
            ]);

            var manager = CreateManager(root, iniPath);
            manager.LoadConfig();

            Assert.Equal([firstRoot, secondRoot], manager.Config.SongRoots);
            Assert.Equal("ReadFromOtherSection", manager.Config.DTXManiaVersion);
        });
    }

    [Fact]
    public void LoadConfig_ShouldIgnoreMalformedBlankAndNegativeIndexedRoots()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var acceptedRoot = Path.Combine(root, "roots", "accepted");
            var iniPath = Path.Combine(root, "Config.ini");
            File.WriteAllLines(iniPath,
            [
                "[System]",
                "SongRoot.-1=negative",
                "SongRoot.invalid=malformed",
                "SongRoot.1=",
                $"SongRoot.2={acceptedRoot}",
            ]);

            var manager = CreateManager(root, iniPath);
            manager.LoadConfig();

            Assert.Equal([acceptedRoot], manager.Config.SongRoots);
        });
    }

    [Fact]
    public void LoadConfig_WhenIndexedRootHasInvalidPathCharacter_ShouldDiscardAndFallBackToManagedDefault()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var defaultRoot = AppPaths.GetDefaultSongsPath();
            var iniPath = Path.Combine(root, "Config.ini");
            // A NUL character is an illegal path character that Path.GetFullPath
            // rejects. Without per-entry recovery this aborts LoadConfig entirely.
            File.WriteAllLines(iniPath,
            [
                "[System]",
                "SongRoot.0=bad\0root",
            ]);

            var manager = CreateManager(root, iniPath);
            manager.LoadConfig();

            Assert.Equal([defaultRoot], manager.Config.SongRoots);
            Assert.Equal(defaultRoot, manager.Config.DTXPath);
        });
    }

    [Fact]
    public void LoadConfig_WhenSomeIndexedRootsAreInvalid_ShouldDiscardInvalidAndKeepValid()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var validRoot = Path.Combine(root, "roots", "valid");
            var iniPath = Path.Combine(root, "Config.ini");
            File.WriteAllLines(iniPath,
            [
                "[System]",
                "SongRoot.0=bad\0root",
                $"SongRoot.1={validRoot}",
            ]);

            var manager = CreateManager(root, iniPath);
            manager.LoadConfig();

            Assert.Equal([validRoot], manager.Config.SongRoots);
            Assert.Equal(validRoot, manager.Config.DTXPath);
        });
    }

    [Fact]
    public void LoadAndSave_ShouldNotCreateMissingCustomRoots()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var indexedCustomRoot = Path.Combine(root, "missing", "indexed");
            var legacyCustomRoot = Path.Combine(root, "missing", "legacy");
            var indexedIni = Path.Combine(root, "indexed.ini");
            var legacyIni = Path.Combine(root, "legacy.ini");
            File.WriteAllLines(indexedIni,
            [
                "[System]",
                $"SongRoot.0={indexedCustomRoot}",
            ]);
            File.WriteAllLines(legacyIni,
            [
                "[System]",
                $"DTXPath={legacyCustomRoot}",
            ]);

            var manager = CreateManager(root, indexedIni, "indexed.db");
            manager.LoadConfig();
            manager.SetNoFail(!manager.Config.NoFail);
            manager.FlushPendingSave();

            Assert.Equal([indexedCustomRoot], manager.Config.SongRoots);
            Assert.False(Directory.Exists(indexedCustomRoot));

            var legacyManager = CreateManager(root, legacyIni, "legacy.db");
            legacyManager.LoadConfig();
            legacyManager.SetNoFail(!legacyManager.Config.NoFail);
            legacyManager.FlushPendingSave();

            Assert.Equal([legacyCustomRoot], legacyManager.Config.SongRoots);
            Assert.False(Directory.Exists(legacyCustomRoot));
            var rows = new SqliteConfigStore(Path.Combine(root, "legacy.db")).Load();
            Assert.Equal(legacyCustomRoot, rows["SongRoot.0"]);
        });
    }

    [Fact]
    public void LoadConfig_ShouldCreateManagedDefaultRootWhenRestored()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var defaultRoot = AppPaths.GetDefaultSongsPath();
            var iniPath = Path.Combine(root, "Config.ini");
            File.WriteAllLines(iniPath,
            [
                "[System]",
                "DTXPath=",
            ]);

            Assert.False(Directory.Exists(defaultRoot));

            var manager = CreateManager(root, iniPath);
            manager.LoadConfig();

            Assert.Equal([defaultRoot], manager.Config.SongRoots);
            Assert.Equal(defaultRoot, manager.Config.DTXPath);
            Assert.True(Directory.Exists(defaultRoot));
        });
    }

    [Fact]
    public void LoadAndSave_ShouldNotDeleteExistingCustomRoots()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var customRoot = Path.Combine(root, "existing-custom");
            var marker = Path.Combine(customRoot, "keep.txt");
            var iniPath = Path.Combine(root, "Config.ini");
            Directory.CreateDirectory(customRoot);
            File.WriteAllText(marker, "keep");
            File.WriteAllLines(iniPath,
            [
                "[System]",
                $"SongRoot.0={customRoot}",
            ]);

            var manager = CreateManager(root, iniPath);
            manager.LoadConfig();
            manager.SetNoFail(!manager.Config.NoFail);
            manager.FlushPendingSave();

            Assert.True(Directory.Exists(customRoot));
            Assert.True(File.Exists(marker));
        });
    }

    [Fact]
    public void SongRootUpdateResult_ShouldCopyMutableInputSnapshots()
    {
        var roots = new List<string> { "first-root" };
        var diagnostics = new List<SongRootDiagnostic>
        {
            new("first-root", "warning", true),
        };

        var result = new SongRootUpdateResult(SongRootUpdateStatus.Updated, roots, diagnostics);
        roots[0] = "changed-root";
        roots.Add("second-root");
        diagnostics.Clear();

        Assert.Equal(["first-root"], result.CanonicalRoots);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("first-root", diagnostic.Path);
        Assert.Equal("warning", diagnostic.Message);
        Assert.True(diagnostic.IsWarning);
    }

    [Fact]
    public void SongRootsChangedEventArgs_ShouldCopyMutableInputSnapshots()
    {
        var oldRoots = new List<string> { "old-root" };
        var newRoots = new List<string> { "new-root" };

        var args = new SongRootsChangedEventArgs(oldRoots, newRoots);
        oldRoots[0] = "changed-old-root";
        oldRoots.Add("another-old-root");
        newRoots.Clear();

        Assert.Equal(["old-root"], args.OldRoots);
        Assert.Equal(["new-root"], args.NewRoots);
    }

    private static ConfigManager CreateManager(string root, string legacyIniPath, string dbFileName = "config.db") =>
        new(Path.Combine(root, dbFileName), legacyIniPath);

    private static void WithTemporaryAppDataRoot(System.Action<string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "dtxmania-song-roots-" + System.Guid.NewGuid().ToString("N"));
        var previousRoot = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", root);

        try
        {
            test(root);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", previousRoot);
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
