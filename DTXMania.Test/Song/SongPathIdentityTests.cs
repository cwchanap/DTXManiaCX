using System;
using System.IO;
using DTXMania.Game.Lib.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public sealed class SongPathIdentityTests
    {
        [Fact]
        public void Normalize_ShouldReturnAbsoluteTrimmedPlatformPath()
        {
            var root = Path.Combine(Path.GetTempPath(), "HPA-192", Guid.NewGuid().ToString("N"));
            var input = Path.Combine(root, "Songs", ".", "chart.dtx");

            var normalized = SongPathIdentity.Normalize(input);

            Assert.Equal(Path.GetFullPath(Path.Combine(root, "Songs", "chart.dtx")), normalized);
        }

        [Fact]
        public void Normalize_ShouldCollapseRelativeAndRedundantNativeSegments()
        {
            var root = Path.Combine(Path.GetTempPath(), "HPA-192", Guid.NewGuid().ToString("N"));
            var input = Path.Combine(root, "Songs", "nested", "..", ".", "chart.dtx");

            Assert.Equal(
                Path.GetFullPath(Path.Combine(root, "Songs", "chart.dtx")),
                SongPathIdentity.Normalize(input));
        }

        [Fact]
        public void LegacyAliasComparer_ShouldFollowDocumentedPlatformPolicy()
        {
            var first = SongPathIdentity.Normalize("/songs/Case/chart.dtx");
            var second = SongPathIdentity.Normalize("/songs/case/chart.dtx");

            Assert.Equal(
                OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(),
                SongPathIdentity.LegacyAliasComparer.Equals(first, second));
        }

        [Fact]
        public void TryNormalize_MalformedPersistedPath_ShouldReturnFalse()
        {
            Assert.False(SongPathIdentity.TryNormalize("\0legacy", out _));
        }

        [Fact]
        public void IsUnderRoot_ShouldRejectSiblingPrefix()
        {
            var root = Path.Combine(Path.GetTempPath(), "Songs");
            var sibling = Path.Combine(Path.GetTempPath(), "Songs-Backup", "chart.dtx");

            Assert.False(SongPathIdentity.IsUnderRoot(sibling, root));
        }

        [Fact]
        public void OrdinaryGroupKey_ShouldIncludeDirectoryTitleAndArtist()
        {
            var first = SongPathIdentity.ForOrdinaryChart("/songs/a/one.dtx", "Same", "Artist");
            var second = SongPathIdentity.ForOrdinaryChart("/songs/b/two.dtx", "Same", "Artist");

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void SetDefinitionGroupKey_ShouldNormalizePathAndDistinguishDifferentSetDefs()
        {
            // A non-canonical spelling of the same set.def path must produce the
            // same key as the canonical spelling (normalization contract).
            var canonical = "/songs/group/set.def";
            var nonCanonical = "/songs/group/./set.def";
            var first = SongPathIdentity.ForSetDefinition(canonical);
            var second = SongPathIdentity.ForSetDefinition(nonCanonical);

            Assert.Equal(first, second);
            Assert.StartsWith("set|", first, StringComparison.Ordinal);

            // A different set.def path must produce a different key (distinct-path
            // contract), proving the key is not a constant.
            var other = SongPathIdentity.ForSetDefinition("/songs/other/set.def");
            Assert.NotEqual(first, other);
        }
    }
}
