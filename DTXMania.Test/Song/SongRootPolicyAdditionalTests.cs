#nullable enable

using System;
using System.IO;
using System.Linq;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song;
using Xunit;

namespace DTXMania.Test.Song;

[Trait("Category", "Unit")]
public sealed class SongRootPolicyAdditionalTests
{
    [Fact]
    public void ForCurrentPlatform_ShouldReturnPolicyWithAComparer()
    {
        var policy = SongRootPolicy.ForCurrentPlatform();

        Assert.NotNull(policy);
        // Windows and macOS use case-insensitive path comparison; Linux uses
        // case-sensitive. Derive the expectation from the same condition the
        // policy uses so the test is correct on every supported platform.
        var expectIgnoreCase = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
        Assert.Equal(expectIgnoreCase, policy.Comparer.Equals("/Songs", "/SONGS"));
    }

    [Fact]
    public void Validate_WithNullRoots_ShouldThrowArgumentNullException()
    {
        var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(false));

        Assert.Throws<ArgumentNullException>(() => policy.Validate(null!));
    }

    [Fact]
    public void Constructor_WithNullComparer_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new SongRootPolicy(null!));
    }

    [Fact]
    public void NormalizeWindowsDrivePath_WithDotSegments_ShouldCollapseToCanonicalRoot()
    {
        // A Windows drive path with redundant "." segments must collapse so the
        // duplicate/overlap checks compare against the canonical form.
        var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(true));

        var result = policy.Validate([@"C:\Songs\.\Pack", @"C:\Songs\Pack"]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, d =>
            !d.IsWarning &&
            d.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NormalizeWindowsDrivePath_WithParentSegmentResolvingToRoot_ShouldProduceDriveRoot()
    {
        var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(true));

        var result = policy.Validate([@"C:\Songs", @"C:\Songs\..\.."]);

        // C:\Songs\..\.. resolves to C:\ which is an ancestor of C:\Songs -> overlap.
        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, d =>
            !d.IsWarning &&
            d.Message.Contains("overlap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NormalizeWindowsDrivePath_RootOnly_ShouldBeAcceptedAsCanonical()
    {
        var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(true));

        var result = policy.Validate([@"C:\"]);

        Assert.True(result.IsValid);
        Assert.Equal(new[] { @"C:\" }, result.CanonicalRoots);
    }

    [Fact]
    public void Validate_WithDuplicateWindowsDrivePathsCaseInsensitive_ShouldReportDuplicate()
    {
        var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(true));

        var result = policy.Validate([@"D:\Songs", @"d:\songs"]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, d =>
            !d.IsWarning &&
            d.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IsAncestor_WithWindowsDriveParent_ShouldDetectChildRelationship()
    {
        var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(true));

        Assert.True(policy.IsAncestor(@"C:\Songs", @"C:\Songs\Pack"));
    }

    [Fact]
    public void IsAncestor_WithDifferentWindowsDrives_ShouldNotMatch()
    {
        var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(true));

        Assert.False(policy.IsAncestor(@"C:\Songs", @"D:\Songs\Pack"));
    }

    [Fact]
    public void IsAncestor_WhenParentEqualsChild_ShouldNotMatchAsAncestor()
    {
        var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(false));

        // A path is not a strict ancestor of itself.
        Assert.False(policy.IsAncestor("/songs", "/songs"));
    }

    [Fact]
    public void Validate_WhenRootOverlapsAsChildOfExisting_ShouldReportOverlap()
    {
        // The overlap check tests both directions (existing ancestor of new, and
        // new ancestor of existing). Supply the ancestor second so the
        // "new ancestor of existing" branch is exercised.
        var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(false));

        var result = policy.Validate(["/songs/pack", "/songs"]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, d =>
            !d.IsWarning &&
            d.Message.Contains("overlap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WhenAllRootsAreValidAndPresent_ShouldProduceNoDiagnostics()
    {
        WithTemporaryDirectory(root =>
        {
            var first = Path.Combine(root, "first");
            var second = Path.Combine(root, "second");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(false));

            var result = policy.Validate([first, second]);

            Assert.True(result.IsValid);
            Assert.Empty(result.Diagnostics);
            Assert.Equal(
                new[] { Path.GetFullPath(first), Path.GetFullPath(second) },
                result.CanonicalRoots);
        });
    }

    [Fact]
    public void Probe_WhenPathIsAFileNotADirectory_ShouldReturnMissing()
    {
        WithTemporaryDirectory(root =>
        {
            var filePath = Path.Combine(root, "not-a-directory");
            File.WriteAllText(filePath, "x");
            var policy = new SongRootPolicy(SongRootPolicy.CreateComparer(false));

            Assert.Equal(SongRootAvailability.Missing, policy.Probe(filePath));
        });
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            nameof(SongRootPolicyAdditionalTests),
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
