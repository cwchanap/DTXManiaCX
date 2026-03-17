using System;
using System.IO;
using DTXMania.Game.Lib.Utilities;
using Xunit;

namespace DTXMania.Test.Utilities
{
    /// <summary>
    /// Extended tests for PathValidator static utility class
    /// </summary>
    public class PathValidatorExtendedTests : IDisposable
    {
        private readonly string _tempDir;

        public PathValidatorExtendedTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"DTXMania_PathValidatorTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        #region IsValidDirectory Tests

        [Fact]
        public void IsValidDirectory_ExistingDirectory_ShouldReturnTrue()
        {
            Assert.True(PathValidator.IsValidDirectory(_tempDir));
        }

        [Fact]
        public void IsValidDirectory_NonExistentDirectory_ShouldReturnFalse()
        {
            var nonExistent = Path.Combine(_tempDir, "does_not_exist");
            Assert.False(PathValidator.IsValidDirectory(nonExistent));
        }

        [Fact]
        public void IsValidDirectory_NullPath_ShouldReturnFalse()
        {
            Assert.False(PathValidator.IsValidDirectory(null));
        }

        [Fact]
        public void IsValidDirectory_EmptyPath_ShouldReturnFalse()
        {
            Assert.False(PathValidator.IsValidDirectory(""));
        }

        #endregion

        #region EnsureDirectory Tests

        [Fact]
        public void EnsureDirectory_ExistingDirectory_ShouldReturnTrue()
        {
            Assert.True(PathValidator.EnsureDirectory(_tempDir));
        }

        [Fact]
        public void EnsureDirectory_NonExistentDirectory_WithoutCreate_ShouldReturnFalse()
        {
            var newDir = Path.Combine(_tempDir, "new_dir");
            Assert.False(PathValidator.EnsureDirectory(newDir, createIfMissing: false));
            Assert.False(Directory.Exists(newDir));
        }

        [Fact]
        public void EnsureDirectory_NonExistentDirectory_WithCreate_ShouldCreateAndReturnTrue()
        {
            var newDir = Path.Combine(_tempDir, "created_dir");
            var result = PathValidator.EnsureDirectory(newDir, createIfMissing: true);

            Assert.True(result);
            Assert.True(Directory.Exists(newDir));
        }

        [Fact]
        public void EnsureDirectory_IntermediateSegmentIsFile_WithCreate_ReturnsFalse()
        {
            var intermediatePath = Path.Combine(_tempDir, "intermediate");
            File.WriteAllText(intermediatePath, "file content");
            try
            {
                var childPath = Path.Combine(intermediatePath, "child");
                var result = PathValidator.EnsureDirectory(childPath, createIfMissing: true);
                Assert.False(result);
                Assert.False(Directory.Exists(childPath));
            }
            finally
            {
                if (File.Exists(intermediatePath))
                    File.Delete(intermediatePath);
            }
        }

        [Fact]
        public void EnsureDirectory_NullPath_ShouldReturnFalse()
        {
            Assert.False(PathValidator.EnsureDirectory(null));
        }

        [Fact]
        public void EnsureDirectory_EmptyPath_ShouldReturnFalse()
        {
            Assert.False(PathValidator.EnsureDirectory(""));
        }

        [Fact]
        public void EnsureDirectory_DefaultParameter_ShouldNotCreate()
        {
            var newDir = Path.Combine(_tempDir, "should_not_create");
            var result = PathValidator.EnsureDirectory(newDir); // default is false
            Assert.False(result);
            Assert.False(Directory.Exists(newDir));
        }

        #endregion

        #region IsValidFile Tests

        [Fact]
        public void IsValidFile_ExistingFile_ShouldReturnTrue()
        {
            var filePath = Path.Combine(_tempDir, "test.txt");
            File.WriteAllText(filePath, "test content");

            Assert.True(PathValidator.IsValidFile(filePath));
        }

        [Fact]
        public void IsValidFile_NonExistentFile_ShouldReturnFalse()
        {
            var nonExistent = Path.Combine(_tempDir, "nonexistent.txt");
            Assert.False(PathValidator.IsValidFile(nonExistent));
        }

        [Fact]
        public void IsValidFile_NullPath_ShouldReturnFalse()
        {
            Assert.False(PathValidator.IsValidFile(null));
        }

        [Fact]
        public void IsValidFile_EmptyPath_ShouldReturnFalse()
        {
            Assert.False(PathValidator.IsValidFile(""));
        }

        #endregion

        #region IsValidSkinPath Tests

        [Fact]
        public void IsValidSkinPath_NoGraphicsFiles_ShouldReturnFalse()
        {
            // Directory exists but doesn't have required files
            Assert.False(PathValidator.IsValidSkinPath(_tempDir));
        }

        [Fact]
        public void IsValidSkinPath_WithBackground1_ShouldReturnTrue()
        {
            var graphicsDir = Path.Combine(_tempDir, "Graphics");
            Directory.CreateDirectory(graphicsDir);
            File.WriteAllText(Path.Combine(graphicsDir, "1_background.jpg"), "fake jpg");

            Assert.True(PathValidator.IsValidSkinPath(_tempDir));
        }

        [Fact]
        public void IsValidSkinPath_WithBackground2_ShouldReturnTrue()
        {
            var graphicsDir = Path.Combine(_tempDir, "Graphics");
            Directory.CreateDirectory(graphicsDir);
            File.WriteAllText(Path.Combine(graphicsDir, "2_background.jpg"), "fake jpg");

            Assert.True(PathValidator.IsValidSkinPath(_tempDir));
        }

        [Fact]
        public void IsValidSkinPath_NonExistentPath_ShouldReturnFalse()
        {
            Assert.False(PathValidator.IsValidSkinPath(Path.Combine(_tempDir, "nonexistent_skin")));
        }

        [Fact]
        public void IsValidSkinPath_NullPath_ShouldReturnFalse()
        {
            Assert.False(PathValidator.IsValidSkinPath(null));
        }

        #endregion

        #region IsValidSkinPath (with requiredFiles) Tests

        [Fact]
        public void IsValidSkinPath_WithRequiredFiles_AllExist_ShouldReturnTrue()
        {
            var file1 = Path.Combine(_tempDir, "file1.txt");
            var file2 = Path.Combine(_tempDir, "file2.txt");
            File.WriteAllText(file1, "content");
            File.WriteAllText(file2, "content");

            var result = PathValidator.IsValidSkinPath(_tempDir, new[] { "file1.txt", "file2.txt" });
            Assert.True(result);
        }

        [Fact]
        public void IsValidSkinPath_WithRequiredFiles_SomeMissing_ShouldReturnFalse()
        {
            var file1 = Path.Combine(_tempDir, "file1.txt");
            File.WriteAllText(file1, "content");

            var result = PathValidator.IsValidSkinPath(_tempDir, new[] { "file1.txt", "missing.txt" });
            Assert.False(result);
        }

        [Fact]
        public void IsValidSkinPath_WithRequiredFiles_NullRequired_ShouldReturnFalse()
        {
            var result = PathValidator.IsValidSkinPath(_tempDir, null);
            Assert.False(result);
        }

        [Fact]
        public void IsValidSkinPath_WithRequiredFiles_InvalidDirectory_ShouldReturnFalse()
        {
            var result = PathValidator.IsValidSkinPath(Path.Combine(_tempDir, "nonexistent"), new[] { "file.txt" });
            Assert.False(result);
        }

        #endregion

        #region GetMissingSkinFiles Tests

        [Fact]
        public void GetMissingSkinFiles_AllPresent_ShouldReturnEmpty()
        {
            var file1 = Path.Combine(_tempDir, "file1.txt");
            File.WriteAllText(file1, "content");

            var missing = PathValidator.GetMissingSkinFiles(_tempDir, new[] { "file1.txt" });
            Assert.Empty(missing);
        }

        [Fact]
        public void GetMissingSkinFiles_SomeMissing_ShouldReturnMissingOnes()
        {
            var file1 = Path.Combine(_tempDir, "file1.txt");
            File.WriteAllText(file1, "content");

            var missing = PathValidator.GetMissingSkinFiles(_tempDir, new[] { "file1.txt", "file2.txt", "file3.txt" });
            Assert.Equal(2, missing.Length);
            Assert.Contains("file2.txt", missing);
            Assert.Contains("file3.txt", missing);
        }

        [Fact]
        public void GetMissingSkinFiles_InvalidDirectory_ShouldReturnAllRequired()
        {
            var requiredFiles = new[] { "file1.txt", "file2.txt" };
            var missing = PathValidator.GetMissingSkinFiles(Path.Combine(_tempDir, "nonexistent"), requiredFiles);
            Assert.Equal(2, missing.Length);
            Assert.Contains("file1.txt", missing);
            Assert.Contains("file2.txt", missing);
        }

        [Fact]
        public void GetMissingSkinFiles_NullRequired_ShouldReturnEmpty()
        {
            var missing = PathValidator.GetMissingSkinFiles(_tempDir, null);
            Assert.Empty(missing);
        }

        [Fact]
        public void GetMissingSkinFiles_NullDirectory_ShouldReturnAllRequired()
        {
            var requiredFiles = new[] { "file1.txt" };
            var missing = PathValidator.GetMissingSkinFiles(null, requiredFiles);
            Assert.Equal(1, missing.Length);
        }

        #endregion
    }
}
