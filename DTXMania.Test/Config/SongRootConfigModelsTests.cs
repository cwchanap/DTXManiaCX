#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using DTXMania.Game.Lib.Config;
using Xunit;

namespace DTXMania.Test.Config;

[Trait("Category", "Unit")]
public sealed class SongRootConfigModelsTests
{
    [Fact]
    public void SongRootUpdateResult_ShouldRejectNullCanonicalRoots()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SongRootUpdateResult(
                SongRootUpdateStatus.Updated,
                canonicalRoots: null!,
                Array.Empty<SongRootDiagnostic>()));
    }

    [Fact]
    public void SongRootUpdateResult_ShouldRejectNullDiagnostics()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SongRootUpdateResult(
                SongRootUpdateStatus.Updated,
                Array.Empty<string>(),
                diagnostics: null!));
    }

    [Fact]
    public void SongRootUpdateResult_ShouldCopyInputsAndExposeReadOnlyCollections()
    {
        var roots = new List<string> { "/a", "/b" };
        var diagnostics = new List<SongRootDiagnostic>
        {
            new("/a", "missing", IsWarning: true),
        };

        var result = new SongRootUpdateResult(
            SongRootUpdateStatus.Updated,
            roots,
            diagnostics);

        roots.Add("/c");
        diagnostics.Add(new SongRootDiagnostic("/c", "late", IsWarning: false));

        Assert.Equal(SongRootUpdateStatus.Updated, result.Status);
        Assert.Equal(new[] { "/a", "/b" }, result.CanonicalRoots);
        Assert.Single(result.Diagnostics);
        Assert.Equal("missing", result.Diagnostics[0].Message);

        // The exposed collections must be read-only wrappers, matching the
        // SongRootUpdateResult contract that guards against caller mutation.
        Assert.IsType<ReadOnlyCollection<string>>(result.CanonicalRoots);
        Assert.IsType<ReadOnlyCollection<SongRootDiagnostic>>(result.Diagnostics);
    }

    [Fact]
    public void Deconstruct_ShouldReturnStatusCanonicalRootsAndDiagnostics()
    {
        var result = new SongRootUpdateResult(
            SongRootUpdateStatus.ValidationFailed,
            new[] { "/x" },
            new[] { new SongRootDiagnostic("/x", "bad", IsWarning: false) });

        var (status, canonicalRoots, diagnostics) = result;

        Assert.Equal(SongRootUpdateStatus.ValidationFailed, status);
        Assert.Equal(new[] { "/x" }, canonicalRoots);
        Assert.Single(diagnostics);
    }

    [Fact]
    public void SongRootsChangedEventArgs_ShouldRejectNullOldRoots()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SongRootsChangedEventArgs(
                oldRoots: null!,
                Array.Empty<string>()));
    }

    [Fact]
    public void SongRootsChangedEventArgs_ShouldRejectNullNewRoots()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SongRootsChangedEventArgs(
                Array.Empty<string>(),
                newRoots: null!));
    }

    [Fact]
    public void SongRootsChangedEventArgs_ShouldCopyInputsAndExposeReadOnlyCollections()
    {
        var oldRoots = new List<string> { "/old" };
        var newRoots = new List<string> { "/new" };

        var args = new SongRootsChangedEventArgs(oldRoots, newRoots);

        oldRoots.Add("/late-old");
        newRoots.Add("/late-new");

        Assert.Equal(new[] { "/old" }, args.OldRoots);
        Assert.Equal(new[] { "/new" }, args.NewRoots);
    }

    [Fact]
    public void SongRootDiagnostic_ShouldRecordPathMessageAndWarningFlag()
    {
        var diagnostic = new SongRootDiagnostic("/songs", "overlaps", IsWarning: false);

        Assert.Equal("/songs", diagnostic.Path);
        Assert.Equal("overlaps", diagnostic.Message);
        Assert.False(diagnostic.IsWarning);
    }
}
