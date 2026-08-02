using System.Collections.Generic;
using System.IO;
using System.Linq;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Utilities;

namespace DTXMania.Test.Config;

[Collection("AppPaths")]
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
            var firstConfig = Path.Combine(root, "first.ini");
            var secondConfig = Path.Combine(root, "second.ini");
            File.WriteAllLines(firstConfig,
            [
                "[System]",
                $"SongRoot.0={firstRoot}",
                $"SongRoot.1={secondRoot}",
            ]);
            File.WriteAllLines(secondConfig,
            [
                "[Other]",
                $"SongRoot.0={replacementRoot}",
            ]);

            var manager = new ConfigManager();

            manager.LoadConfig(firstConfig);
            manager.LoadConfig(secondConfig);

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
            var configFile = Path.Combine(root, "Config.ini");
            File.WriteAllLines(configFile,
            [
                "[System]",
                $"SongRoot.10={rootTen}",
                "[Other]",
                $"SongRoot.2={rootTwo}",
                $"SongRoot.0={rootZero}",
            ]);

            var manager = new ConfigManager();

            manager.LoadConfig(configFile);

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
            var configFile = Path.Combine(root, "Config.ini");
            File.WriteAllLines(configFile,
            [
                "[System]",
                $"SongRoot.1={firstRootOne}",
                "[Other]",
                $"SongRoot.0={rootZero}",
                $"SongRoot.1={lastRootOne}",
            ]);

            var manager = new ConfigManager();

            manager.LoadConfig(configFile);

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
            var configFile = Path.Combine(root, "Config.ini");
            File.WriteAllLines(configFile,
            [
                "[System]",
                $"DTXPath={legacyRoot}",
                $"SongRoot.0={indexedRoot}",
            ]);

            var manager = new ConfigManager();

            manager.LoadConfig(configFile);

            Assert.Equal([indexedRoot], manager.Config.SongRoots);
            Assert.Equal(indexedRoot, manager.Config.DTXPath);
        });
    }

    [Fact]
    public void LoadConfig_ShouldMigrateLegacyDTXPathAndPersistIndexedRoot()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var legacyRoot = Path.Combine(root, "roots", "legacy");
            var configFile = Path.Combine(root, "Config.ini");
            File.WriteAllLines(configFile,
            [
                "[System]",
                $"DTXPath={legacyRoot}",
            ]);

            var manager = new ConfigManager();

            manager.LoadConfig(configFile);

            var configText = File.ReadAllText(configFile);
            Assert.Equal([legacyRoot], manager.Config.SongRoots);
            Assert.Equal(legacyRoot, manager.Config.DTXPath);
            Assert.Contains($"SongRoot.0={legacyRoot}", configText);
            Assert.Contains($"DTXPath={legacyRoot}", configText);
            Assert.False(Directory.Exists(legacyRoot));
        });
    }

    [Fact]
    public void SaveConfig_ShouldWriteDenseIndexesAndFirstRootMirror()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var firstRoot = Path.Combine(root, "roots", "first");
            var secondRoot = Path.Combine(root, "roots", "second");
            var configFile = Path.Combine(root, "Config.ini");
            var manager = new ConfigManager();
            manager.Config.SongRoots.Clear();
            manager.Config.SongRoots.Add(firstRoot);
            manager.Config.SongRoots.Add(secondRoot);
            manager.Config.DTXPath = Path.Combine(root, "roots", "stale-mirror");

            manager.SaveConfig(configFile);

            var configLines = File.ReadAllLines(configFile);
            Assert.Equal(firstRoot, manager.Config.DTXPath);
            Assert.Contains($"SongRoot.0={firstRoot}", configLines);
            Assert.Contains($"SongRoot.1={secondRoot}", configLines);
            Assert.DoesNotContain(configLines, line => line.StartsWith("SongRoot.2=", StringComparison.Ordinal));
            Assert.Contains($"DTXPath={firstRoot}", configLines);
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
            var configFile = Path.Combine(root, "Config.ini");
            File.WriteAllLines(configFile,
            [
                "[System]",
                $"SongRoot.1={secondRoot}",
                "[Other]",
                "DTXManiaVersion=ReadFromOtherSection",
                $"SongRoot.0={firstRoot}",
            ]);

            var manager = new ConfigManager();

            manager.LoadConfig(configFile);

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
            var configFile = Path.Combine(root, "Config.ini");
            File.WriteAllLines(configFile,
            [
                "[System]",
                "SongRoot.-1=negative",
                "SongRoot.invalid=malformed",
                "SongRoot.1=",
                $"SongRoot.2={acceptedRoot}",
            ]);

            var manager = new ConfigManager();

            manager.LoadConfig(configFile);

            Assert.Equal([acceptedRoot], manager.Config.SongRoots);
        });
    }

    [Fact]
    public void LoadAndSave_ShouldNotCreateMissingCustomRoots()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var indexedCustomRoot = Path.Combine(root, "missing", "indexed");
            var legacyCustomRoot = Path.Combine(root, "missing", "legacy");
            var indexedConfig = Path.Combine(root, "indexed.ini");
            var legacyConfig = Path.Combine(root, "legacy.ini");
            File.WriteAllLines(indexedConfig,
            [
                "[System]",
                $"SongRoot.0={indexedCustomRoot}",
            ]);
            File.WriteAllLines(legacyConfig,
            [
                "[System]",
                $"DTXPath={legacyCustomRoot}",
            ]);

            var manager = new ConfigManager();
            manager.LoadConfig(indexedConfig);
            manager.SaveConfig(indexedConfig);

            Assert.Equal([indexedCustomRoot], manager.Config.SongRoots);
            Assert.False(Directory.Exists(indexedCustomRoot));

            manager.LoadConfig(legacyConfig);
            manager.SaveConfig(legacyConfig);

            Assert.Equal([legacyCustomRoot], manager.Config.SongRoots);
            Assert.False(Directory.Exists(legacyCustomRoot));
            Assert.Contains($"SongRoot.0={legacyCustomRoot}", File.ReadAllText(legacyConfig));
        });
    }

    [Fact]
    public void LoadConfig_ShouldCreateManagedDefaultRootWhenRestored()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var defaultRoot = AppPaths.GetDefaultSongsPath();
            var configFile = Path.Combine(root, "Config.ini");
            File.WriteAllLines(configFile,
            [
                "[System]",
                "DTXPath=",
            ]);

            Assert.False(Directory.Exists(defaultRoot));

            var manager = new ConfigManager();
            manager.LoadConfig(configFile);

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
            var configFile = Path.Combine(root, "Config.ini");
            Directory.CreateDirectory(customRoot);
            File.WriteAllText(marker, "keep");
            File.WriteAllLines(configFile,
            [
                "[System]",
                $"SongRoot.0={customRoot}",
            ]);

            var manager = new ConfigManager();
            manager.LoadConfig(configFile);
            manager.SaveConfig(configFile);

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

    private static void WithTemporaryAppDataRoot(Action<string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "dtxmania-song-roots-" + Guid.NewGuid().ToString("N"));
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
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
