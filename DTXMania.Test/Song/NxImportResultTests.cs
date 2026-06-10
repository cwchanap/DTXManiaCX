using DTXMania.Game.Lib.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class NxImportResultTests
    {
        [Fact]
        public void NxImportResult_Properties_CanBeSetAndRead()
        {
            var result = new NxImportResult
            {
                Scanned = 10,
                Imported = 5,
                Skipped = 3,
                Errors = 2
            };

            Assert.Equal(10, result.Scanned);
            Assert.Equal(5, result.Imported);
            Assert.Equal(3, result.Skipped);
            Assert.Equal(2, result.Errors);
        }

        [Fact]
        public void NxImportProgress_Properties_CanBeSetAndRead()
        {
            var progress = new NxImportProgress
            {
                Scanned = 10,
                Imported = 5,
                Skipped = 3,
                Errors = 2,
                CurrentFile = "test.dtx"
            };

            Assert.Equal(10, progress.Scanned);
            Assert.Equal(5, progress.Imported);
            Assert.Equal(3, progress.Skipped);
            Assert.Equal(2, progress.Errors);
            Assert.Equal("test.dtx", progress.CurrentFile);
        }

        [Fact]
        public void NxImportProgress_DefaultCurrentFile_IsEmptyString()
        {
            var progress = new NxImportProgress();
            Assert.Equal("", progress.CurrentFile);
        }

        [Fact]
        public void NxImportResult_DbUnavailable_DefaultsFalse()
        {
            var result = new NxImportResult();
            Assert.False(result.DbUnavailable);
        }

        [Fact]
        public void NxImportResult_DbUnavailable_CanBeSet()
        {
            var result = new NxImportResult { DbUnavailable = true, Scanned = 0 };
            Assert.True(result.DbUnavailable);
            Assert.Equal(0, result.Scanned);
        }
    }
}
