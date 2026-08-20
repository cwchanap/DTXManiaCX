#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song;
using Xunit;

namespace DTXMania.Test.Song;

[Trait("Category", "Unit")]
[Collection("AppPaths")]
public sealed class SongRootPolicyTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void DuplicatePolicy_ShouldFollowInjectedCaseMode(
        bool ignoreCase,
        bool expectedDuplicate)
    {
        var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(ignoreCase));

        var result = policy.Validate(["/policy/Songs", "/policy/SONGS"]);

        Assert.Equal(!expectedDuplicate, result.IsValid);
        Assert.Equal(expectedDuplicate, result.Diagnostics.Any(
            diagnostic => !diagnostic.IsWarning &&
                diagnostic.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData(true, "/Users/me/Songs", "/Users/me/SONGS/Extra", true)]
    [InlineData(true, @"C:\Songs", @"c:\songs\Pack", true)]
    [InlineData(false, "/songs", "/Songs/Pack", false)]
    public void Validate_ShouldRejectOverlappingRootsUsingInjectedSegmentComparison(
        bool ignoreCase,
        string parent,
        string child,
        bool expectedOverlap)
    {
        var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(ignoreCase));

        var result = policy.Validate([parent, child]);

        Assert.Equal(!expectedOverlap, result.IsValid);
        Assert.Equal(expectedOverlap, result.Diagnostics.Any(
            diagnostic => !diagnostic.IsWarning &&
                diagnostic.Message.Contains("overlap", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Validate_ShouldNormalizeAndPreserveFirstOccurrenceOrder()
    {
        WithTemporaryDirectory(root =>
        {
            var first = Path.Combine(root, "first");
            var second = Path.Combine(root, "second");
            var suppliedRoots = new List<string>
            {
                Path.Combine(first, "."),
                second,
            };
            var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(false));

            var result = policy.Validate(suppliedRoots);
            suppliedRoots[0] = second;

            Assert.True(result.IsValid);
            Assert.Equal(
                [Path.GetFullPath(first), Path.GetFullPath(second)],
                result.CanonicalRoots);
        });
    }

    [Fact]
    public void Probe_ShouldDistinguishAvailableAndMissingRootsWithoutCreatingDirectories()
    {
        WithTemporaryDirectory(root =>
        {
            var available = Path.Combine(root, "available");
            var missing = Path.Combine(root, "missing");
            Directory.CreateDirectory(available);
            var policy = SongRootPolicy.ForCurrentPlatform();

            Assert.Equal(
                SongRootAvailability.Available,
                policy.Probe(Path.GetFullPath(available)));
            Assert.Equal(
                SongRootAvailability.Missing,
                policy.Probe(Path.GetFullPath(missing)));
            Assert.False(Directory.Exists(missing));
        });
    }

    [Fact]
    public void SetSongRoots_ShouldPersistCanonicalRootsBeforeRaisingOneEvent()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var oldRoot = Path.Combine(root, "old");
            var firstRoot = Path.Combine(root, "first");
            var secondRoot = Path.Combine(root, "second");
            Directory.CreateDirectory(oldRoot);
            Directory.CreateDirectory(firstRoot);
            Directory.CreateDirectory(secondRoot);
            var manager = new ConfigManager(
                Path.Combine(root, "config.db"),
                Path.Combine(root, "Config.ini"),
                new SongRootPolicy(SongRootPolicy.CreateComparer(false)));
            manager.LoadConfig();
            manager.Config.SongRoots.Clear();
            manager.Config.SongRoots.Add(oldRoot);
            manager.Config.DTXPath = oldRoot;
            SongRootsChangedEventArgs? raised = null;
            var eventCount = 0;
            var dbExistedWhenRaised = false;
            manager.SongRootsChanged += (_, args) =>
            {
                eventCount++;
                raised = args;
                dbExistedWhenRaised = File.Exists(Path.Combine(root, "config.db"));
                // Prove persistence PRECEDES the event: the canonical roots
                // are already the committed rows when the handler runs.
                var persisted = new SqliteConfigStore(Path.Combine(root, "config.db")).Load();
                Assert.Equal(Path.GetFullPath(firstRoot), persisted["SongRoot.0"]);
                Assert.Equal(Path.GetFullPath(secondRoot), persisted["SongRoot.1"]);
            };

            var result = manager.SetSongRoots(
                [Path.Combine(firstRoot, "."), secondRoot]);

            Assert.True(dbExistedWhenRaised,
                "The config database should exist when SongRootsChanged is raised.");
            Assert.Equal(SongRootUpdateStatus.Updated, result.Status);
            Assert.Equal(
                [Path.GetFullPath(firstRoot), Path.GetFullPath(secondRoot)],
                manager.Config.SongRoots);
            Assert.Equal(Path.GetFullPath(firstRoot), manager.Config.DTXPath);
            Assert.Equal(1, eventCount);
            Assert.NotNull(raised);
            Assert.Equal([Path.GetFullPath(oldRoot)], raised!.OldRoots);
            Assert.Equal(
                [Path.GetFullPath(firstRoot), Path.GetFullPath(secondRoot)],
                raised.NewRoots);
            var rows = new SqliteConfigStore(Path.Combine(root, "config.db")).Load();
            Assert.Equal(Path.GetFullPath(firstRoot), rows["SongRoot.0"]);
            Assert.Equal(Path.GetFullPath(secondRoot), rows["SongRoot.1"]);
        });
    }

    [Fact]
    public void SetSongRoots_WhenCanonicalOrderedRootsAreUnchanged_ShouldNotWriteOrRaiseEvent()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var songsRoot = Path.Combine(root, "songs");
            Directory.CreateDirectory(songsRoot);
            var manager = new ConfigManager(
                Path.Combine(root, "config.db"),
                Path.Combine(root, "Config.ini"),
                new SongRootPolicy(SongRootPolicy.CreateComparer(false)));
            manager.LoadConfig();
            manager.Config.SongRoots.Clear();
            manager.Config.SongRoots.Add(songsRoot);
            manager.Config.DTXPath = songsRoot;
            var eventCount = 0;
            manager.SongRootsChanged += (_, _) => eventCount++;

            var result = manager.SetSongRoots(
                [Path.Combine(songsRoot, ".")]);

            Assert.Equal(SongRootUpdateStatus.Unchanged, result.Status);
            Assert.Equal(0, eventCount);
        });
    }

    [Fact]
    public void SetSongRoots_WhenNoRootsAreSupplied_ShouldRejectTheEmptyConfiguration()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var existingRoot = Path.Combine(root, "existing");
            Directory.CreateDirectory(existingRoot);
            var manager = new ConfigManager(
                Path.Combine(root, "config.db"),
                Path.Combine(root, "Config.ini"),
                new SongRootPolicy(SongRootPolicy.CreateComparer(false)));
            manager.LoadConfig();
            manager.Config.SongRoots.Clear();
            manager.Config.SongRoots.Add(existingRoot);
            manager.Config.DTXPath = existingRoot;

            var result = manager.SetSongRoots(Array.Empty<string>());

            Assert.Equal(SongRootUpdateStatus.ValidationFailed, result.Status);
            Assert.Empty(result.CanonicalRoots);
            Assert.Contains(result.Diagnostics, diagnostic =>
                !diagnostic.IsWarning &&
                diagnostic.Message.Contains("at least one", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(new[] { existingRoot }, manager.Config.SongRoots);
            Assert.Equal(existingRoot, manager.Config.DTXPath);
        });
    }

    [Fact]
    public void SetSongRoots_WhenImmediatePersistenceFails_ShouldRestoreMemoryAndNotRaiseEvent()
    {
        WithTemporaryAppDataRoot(root =>
        {
            var oldRoot = Path.Combine(root, "old");
            var newRoot = Path.Combine(root, "new");
            Directory.CreateDirectory(oldRoot);
            Directory.CreateDirectory(newRoot);
            var manager = new ConfigManager(
                Path.Combine(root, "config.db"),
                Path.Combine(root, "Config.ini"),
                new SongRootPolicy(SongRootPolicy.CreateComparer(false)));
            manager.LoadConfig();
            manager.Config.SongRoots.Clear();
            manager.Config.SongRoots.Add(oldRoot);
            manager.Config.DTXPath = oldRoot;
            var eventCount = 0;
            manager.SongRootsChanged += (_, _) => eventCount++;

            // Break the store's directory: replacing the root with a regular
            // file makes the store's directory creation throw on save.
            Directory.Delete(root, recursive: true);
            File.WriteAllText(root, "blocker");
            try
            {
                var result = manager.SetSongRoots([newRoot]);

                Assert.Equal(SongRootUpdateStatus.PersistenceFailed, result.Status);
                Assert.Equal([oldRoot], manager.Config.SongRoots);
                Assert.Equal(oldRoot, manager.Config.DTXPath);
                Assert.Equal(0, eventCount);
            }
            finally
            {
                File.Delete(root);
                Directory.CreateDirectory(root);
            }
        });
    }

    [Fact]
    public void Validate_WhenRootIsBlank_ShouldReportNonWarningDiagnosticWithoutAddingCanonicalRoot()
    {
        var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(false));

        var result = policy.Validate(["/policy/Songs", "   "]);

        Assert.False(result.IsValid);
        Assert.Single(result.CanonicalRoots);
        Assert.Contains(result.Diagnostics, diagnostic =>
            !diagnostic.IsWarning &&
            diagnostic.Message.Contains("blank", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WhenRootCannotBeNormalized_ShouldReportInvalidDiagnosticWithoutAddingCanonicalRoot()
    {
        var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(false));

        // A NUL character is an illegal path character that Path.GetFullPath rejects.
        var result = policy.Validate(["/policy/Songs", "/bad\0root"]);

        Assert.False(result.IsValid);
        Assert.Single(result.CanonicalRoots);
        Assert.Contains(result.Diagnostics, diagnostic =>
            !diagnostic.IsWarning &&
            diagnostic.Message.Contains("invalid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WhenConfiguredRootIsMissing_ShouldReportWarningAndKeepRootValid()
    {
        WithTemporaryDirectory(root =>
        {
            var missing = Path.Combine(root, "does-not-exist");
            var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(false));

            var result = policy.Validate([missing]);

            Assert.True(result.IsValid);
            Assert.Equal([Path.GetFullPath(missing)], result.CanonicalRoots);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.IsWarning &&
                diagnostic.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Validate_WhenConfiguredRootIsInaccessible_ShouldReportWarningAndKeepRootValid()
    {
        WithTemporaryDirectory(root =>
        {
            var restricted = Path.Combine(root, "restricted");
            Directory.CreateDirectory(restricted);
            if (!TryDenyEnumeration(restricted))
            {
                // Running as root or on a filesystem that ignores POSIX permission bits
                // (e.g. a FAT mount) cannot produce an inaccessible directory; skip the
                // assertion rather than reporting a false failure.
                return;
            }

            try
            {
                var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(false));

                var result = policy.Validate([restricted]);

                Assert.True(result.IsValid);
                Assert.Equal([restricted], result.CanonicalRoots);
                Assert.Contains(result.Diagnostics, diagnostic =>
                    diagnostic.IsWarning &&
                    diagnostic.Message.Contains("inaccessible", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                RestoreEnumeration(restricted);
            }
        });
    }

    [Fact]
    public void Probe_WhenRootIsInaccessible_ShouldReturnInaccessible()
    {
        WithTemporaryDirectory(root =>
        {
            var restricted = Path.Combine(root, "restricted");
            Directory.CreateDirectory(restricted);
            if (!TryDenyEnumeration(restricted))
                return;

            try
            {
                var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(false));

                Assert.Equal(
                    SongRootAvailability.Inaccessible,
                    policy.Probe(restricted));
            }
            finally
            {
                RestoreEnumeration(restricted);
            }
        });
    }

    [Fact]
    public void IsAncestor_WhenPathsCannotBeNormalized_ShouldReturnFalse()
    {
        var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(false));

        Assert.False(policy.IsAncestor("/bad\0parent", "/child"));
        Assert.False(policy.IsAncestor("/parent", "/bad\0child"));
    }

    [Fact]
    public void NormalizeWindowsDrivePath_ShouldResolveParentSegmentDuringOverlapCheck()
    {
        // A Windows-style drive path with a parent (..) segment must collapse before
        // the overlap check so a child of the resolved root is detected as overlapping.
        var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(true));

        var result = policy.Validate([@"C:\Songs", @"C:\Songs\..\Songs\Pack"]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            !diagnostic.IsWarning &&
            diagnostic.Message.Contains("overlap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateComparer_WhenIgnoreCaseIsFalse_ShouldProduceAnOrdinalComparer()
    {
        var comparer = SongRootPolicy.CreateComparer(ignoreCase: false);

        Assert.NotEqual(comparer, StringComparer.OrdinalIgnoreCase);
        Assert.False(comparer.Equals("/Songs", "/SONGS"));
    }

    private static bool TryDenyEnumeration(string directory)
    {
        // Remove all permissions so enumeration fails with EACCES for non-root
        // callers. Uses chmod so the test does not depend on a Mono.Unix binding.
        return RunChmod("000", directory);
    }

    private static void RestoreEnumeration(string directory)
    {
        // Best-effort restore; the temporary directory is deleted by the caller.
        RunChmod("755", directory);
    }

    private static bool RunChmod(string mode, string path)
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/chmod",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add(mode);
            info.ArgumentList.Add(path);
            using var process = System.Diagnostics.Process.Start(info);
            if (process == null)
                return false;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            nameof(SongRootPolicyTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            action(root);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Temporary directory that also sandboxes DTXMANIA_APPDATA_ROOT —
    /// required for tests that call <see cref="ConfigManager.LoadConfig"/>,
    /// whose normalization resolves and creates default app-data directories.
    /// </summary>
    private static void WithTemporaryAppDataRoot(Action<string> action)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            nameof(SongRootPolicyTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
        Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", root);
        try
        {
            action(root);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", previous);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
