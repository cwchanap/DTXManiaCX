using System;
using DTXMania.Game.Lib.Resources;
using Xunit;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Tests for DTXMania.Game.Lib.Resources.Constants.
    /// Verifies that constant values remain stable and match expected DTXMania conventions.
    /// </summary>
    [Trait("Category", "Unit")]
    public class ConstantsTests
    {
        #region SongPaths Constants

        [Fact]
        public void SongPaths_DTXFiles_ShouldEqualDTXFiles()
        {
            Assert.Equal("DTXFiles", Constants.SongPaths.DTXFiles);
        }

        [Fact]
        public void SongPaths_DTXFiles_ShouldNotBeNullOrEmpty()
        {
            Assert.False(string.IsNullOrEmpty(Constants.SongPaths.DTXFiles));
        }

        [Fact]
        public void SongPaths_Default_ShouldNotBeNull()
        {
            Assert.NotNull(Constants.SongPaths.Default);
        }

        [Fact]
        public void SongPaths_Default_ShouldContainAtLeastOneEntry()
        {
            Assert.NotEmpty(Constants.SongPaths.Default);
        }

        [Fact]
        public void SongPaths_Default_AllEntriesShouldBeNonEmpty()
        {
            foreach (var path in Constants.SongPaths.Default)
            {
                Assert.False(string.IsNullOrWhiteSpace(path),
                    "Every entry in SongPaths.Default should be a non-blank path.");
            }
        }

        [Fact]
        public void SongPaths_Default_ShouldBeRootedPaths()
        {
            // Default song paths are resolved to absolute paths via AppPaths.
            foreach (var path in Constants.SongPaths.Default)
            {
                Assert.True(System.IO.Path.IsPathRooted(path),
                    $"Expected '{path}' to be a rooted (absolute) path.");
            }
        }

        [Fact]
        public void SongPaths_Default_FirstEntryShouldContainDTXFiles()
        {
            // The first default path should contain the DTXFiles folder name.
            var first = Constants.SongPaths.Default[0];
            Assert.Contains("DTXFiles", first, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
