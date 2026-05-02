using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Unit")]
    public class ChipSoundCacheTests
    {
        [Fact]
        public void Count_NewInstance_IsZero()
        {
            using var cache = new ChipSoundCache(_ => Mock.Of<ISound>());
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void Play_UnknownWavId_DoesNotThrow()
        {
            using var cache = new ChipSoundCache(_ => Mock.Of<ISound>());
            var ex = Record.Exception(() => cache.Play("NOPE"));
            Assert.Null(ex);
        }

        [Fact]
        public void Play_NullOrEmptyWavId_DoesNotThrow()
        {
            using var cache = new ChipSoundCache(_ => Mock.Of<ISound>());
            Assert.Null(Record.Exception(() => cache.Play(null!)));
            Assert.Null(Record.Exception(() => cache.Play("")));
        }

        [Fact]
        public async Task PreloadAsync_LoadsExistingFilesViaFactory()
        {
            var (dir, file1, file2) = CreateTempWavFiles("a.wav", "b.wav");
            try
            {
                var factoryCalls = new List<string>();
                var factory = new Func<string, ISound>(path =>
                {
                    factoryCalls.Add(path);
                    return Mock.Of<ISound>();
                });

                using var cache = new ChipSoundCache(factory);
                await cache.PreloadAsync(new Dictionary<string, string>
                {
                    ["01"] = file1,
                    ["02"] = file2,
                });

                Assert.Equal(2, cache.Count);
                Assert.True(cache.Contains("01"));
                Assert.True(cache.Contains("02"));
                Assert.Equal(2, factoryCalls.Count);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task PreloadAsync_MissingFile_SkipsAndContinues()
        {
            var (dir, validFile, _) = CreateTempWavFiles("ok.wav", "ignored.wav");
            try
            {
                using var cache = new ChipSoundCache(_ => Mock.Of<ISound>());
                await cache.PreloadAsync(new Dictionary<string, string>
                {
                    ["01"] = validFile,
                    ["02"] = Path.Combine(dir, "does-not-exist.wav"),
                });

                Assert.Equal(1, cache.Count);
                Assert.True(cache.Contains("01"));
                Assert.False(cache.Contains("02"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task PreloadAsync_FactoryThrows_SkipsThatEntry()
        {
            var (dir, file1, file2) = CreateTempWavFiles("a.wav", "b.wav");
            try
            {
                var factory = new Func<string, ISound>(path =>
                {
                    if (path.EndsWith("a.wav", StringComparison.Ordinal))
                        throw new InvalidOperationException("simulated load failure");
                    return Mock.Of<ISound>();
                });

                using var cache = new ChipSoundCache(factory);
                await cache.PreloadAsync(new Dictionary<string, string>
                {
                    ["01"] = file1,
                    ["02"] = file2,
                });

                Assert.Equal(1, cache.Count);
                Assert.False(cache.Contains("01"));
                Assert.True(cache.Contains("02"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task PreloadAsync_NullDict_DoesNothing()
        {
            using var cache = new ChipSoundCache(_ => Mock.Of<ISound>());
            await cache.PreloadAsync(null!);
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public async Task Play_KnownWavId_InvokesPlayOnSound()
        {
            var (dir, file1, _) = CreateTempWavFiles("a.wav", null);
            try
            {
                var soundMock = new Mock<ISound>();
                using var cache = new ChipSoundCache(_ => soundMock.Object);
                await cache.PreloadAsync(new Dictionary<string, string> { ["01"] = file1 });

                cache.Play("01");

                soundMock.Verify(s => s.Play(), Times.Once);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task Dispose_ReleasesAllSounds()
        {
            var (dir, file1, file2) = CreateTempWavFiles("a.wav", "b.wav");
            try
            {
                var disposed = new List<ISound>();
                var factory = new Func<string, ISound>(_ =>
                {
                    var m = new Mock<ISound>();
                    m.Setup(s => s.Dispose()).Callback(() => disposed.Add(m.Object));
                    return m.Object;
                });

                var cache = new ChipSoundCache(factory);
                await cache.PreloadAsync(new Dictionary<string, string>
                {
                    ["01"] = file1,
                    ["02"] = file2,
                });

                cache.Dispose();

                Assert.Equal(2, disposed.Count);
                Assert.Equal(0, cache.Count);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task PreloadAsync_AfterDispose_Throws()
        {
            using var cache = new ChipSoundCache(_ => Mock.Of<ISound>());
            cache.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => cache.PreloadAsync(new Dictionary<string, string>()));
        }

        // --- helpers ---

        private static (string dir, string file1, string file2) CreateTempWavFiles(string name1, string? name2)
        {
            var dir = Path.Combine(Path.GetTempPath(), $"chipcache-{Guid.NewGuid()}");
            Directory.CreateDirectory(dir);
            var f1 = Path.Combine(dir, name1);
            File.WriteAllBytes(f1, new byte[] { 0 });
            string f2 = "";
            if (name2 != null)
            {
                f2 = Path.Combine(dir, name2);
                File.WriteAllBytes(f2, new byte[] { 0 });
            }
            return (dir, f1, f2);
        }
    }
}
