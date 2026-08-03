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
        WithTemporaryDirectory(root =>
        {
            var oldRoot = Path.Combine(root, "old");
            var firstRoot = Path.Combine(root, "first");
            var secondRoot = Path.Combine(root, "second");
            Directory.CreateDirectory(oldRoot);
            Directory.CreateDirectory(firstRoot);
            Directory.CreateDirectory(secondRoot);
            var configFile = Path.Combine(root, "Config.ini");
            var manager = new ConfigManager(
                new SongRootPolicy(SongRootPolicy.CreateComparer(false)));
            manager.Config.SongRoots.Clear();
            manager.Config.SongRoots.Add(oldRoot);
            manager.Config.DTXPath = oldRoot;
            SongRootsChangedEventArgs? raised = null;
            var eventCount = 0;
            manager.SongRootsChanged += (_, args) =>
            {
                eventCount++;
                raised = args;
                Assert.True(File.Exists(configFile));
            };

            var result = manager.SetSongRoots(
                configFile,
                [Path.Combine(firstRoot, "."), secondRoot]);

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
            Assert.Contains($"SongRoot.0={Path.GetFullPath(firstRoot)}", File.ReadAllText(configFile));
            Assert.Contains($"SongRoot.1={Path.GetFullPath(secondRoot)}", File.ReadAllText(configFile));
        });
    }

    [Fact]
    public void SetSongRoots_WhenCanonicalOrderedRootsAreUnchanged_ShouldNotWriteOrRaiseEvent()
    {
        WithTemporaryDirectory(root =>
        {
            var songsRoot = Path.Combine(root, "songs");
            Directory.CreateDirectory(songsRoot);
            var configFile = Path.Combine(root, "Config.ini");
            var manager = new ConfigManager(
                new SongRootPolicy(SongRootPolicy.CreateComparer(false)));
            manager.Config.SongRoots.Clear();
            manager.Config.SongRoots.Add(songsRoot);
            manager.Config.DTXPath = songsRoot;
            var eventCount = 0;
            manager.SongRootsChanged += (_, _) => eventCount++;

            var result = manager.SetSongRoots(
                configFile,
                [Path.Combine(songsRoot, ".")]);

            Assert.Equal(SongRootUpdateStatus.Unchanged, result.Status);
            Assert.False(File.Exists(configFile));
            Assert.Equal(0, eventCount);
        });
    }

    [Fact]
    public void SetSongRoots_WhenNoRootsAreSupplied_ShouldRejectTheEmptyConfiguration()
    {
        WithTemporaryDirectory(root =>
        {
            var existingRoot = Path.Combine(root, "existing");
            Directory.CreateDirectory(existingRoot);
            var configFile = Path.Combine(root, "Config.ini");
            var manager = new ConfigManager(
                new SongRootPolicy(SongRootPolicy.CreateComparer(false)));
            manager.Config.SongRoots.Clear();
            manager.Config.SongRoots.Add(existingRoot);
            manager.Config.DTXPath = existingRoot;

            var result = manager.SetSongRoots(configFile, Array.Empty<string>());

            Assert.Equal(SongRootUpdateStatus.ValidationFailed, result.Status);
            Assert.Empty(result.CanonicalRoots);
            Assert.Contains(result.Diagnostics, diagnostic =>
                !diagnostic.IsWarning &&
                diagnostic.Message.Contains("at least one", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(new[] { existingRoot }, manager.Config.SongRoots);
            Assert.Equal(existingRoot, manager.Config.DTXPath);
            Assert.False(File.Exists(configFile));
        });
    }

    [Fact]
    public void SetSongRoots_WhenImmediatePersistenceFails_ShouldRestoreMemoryAndNotRaiseEvent()
    {
        WithTemporaryDirectory(root =>
        {
            var oldRoot = Path.Combine(root, "old");
            var newRoot = Path.Combine(root, "new");
            Directory.CreateDirectory(oldRoot);
            Directory.CreateDirectory(newRoot);
            var blockingFile = Path.Combine(root, "not-a-directory");
            File.WriteAllText(blockingFile, "block");
            var configFile = Path.Combine(blockingFile, "Config.ini");
            var manager = new ConfigManager(
                new SongRootPolicy(SongRootPolicy.CreateComparer(false)));
            manager.Config.SongRoots.Clear();
            manager.Config.SongRoots.Add(oldRoot);
            manager.Config.DTXPath = oldRoot;
            var eventCount = 0;
            manager.SongRootsChanged += (_, _) => eventCount++;

            var result = manager.SetSongRoots(configFile, [newRoot]);

            Assert.Equal(SongRootUpdateStatus.PersistenceFailed, result.Status);
            Assert.Equal([oldRoot], manager.Config.SongRoots);
            Assert.Equal(oldRoot, manager.Config.DTXPath);
            Assert.Equal(0, eventCount);
        });
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
}
