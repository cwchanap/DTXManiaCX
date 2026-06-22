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
        public async Task Play_WithReducedVolumeOrPan_InvokesVolumePanOverload()
        {
            var (dir, file1, _) = CreateTempWavFiles("a.wav", null);
            try
            {
                var soundMock = new Mock<ISound>();
                using var cache = new ChipSoundCache(_ => soundMock.Object);
                await cache.PreloadAsync(new Dictionary<string, string> { ["01"] = file1 });

                cache.Play("01", 0.5f, -0.3f);

                soundMock.Verify(s => s.Play(0.5f, 0.0f, -0.3f), Times.Once);
                soundMock.Verify(s => s.Play(), Times.Never);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task Play_WithFullVolumeAndCenterPan_InvokesParameterlessPlay()
        {
            // Full volume + centered should stay on the simple Play() path so the
            // common (no #VOLUME/#PAN) case is unchanged.
            var (dir, file1, _) = CreateTempWavFiles("a.wav", null);
            try
            {
                var soundMock = new Mock<ISound>();
                using var cache = new ChipSoundCache(_ => soundMock.Object);
                await cache.PreloadAsync(new Dictionary<string, string> { ["01"] = file1 });

                cache.Play("01", 1.0f, 0.0f);

                soundMock.Verify(s => s.Play(), Times.Once);
                soundMock.Verify(s => s.Play(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()), Times.Never);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task Play_WithVolumePan_AfterDispose_IsSilent()
        {
            var (dir, file1, _) = CreateTempWavFiles("a.wav", null);
            try
            {
                var soundMock = new Mock<ISound>();
                var cache = new ChipSoundCache(_ => soundMock.Object);
                await cache.PreloadAsync(new Dictionary<string, string> { ["01"] = file1 });
                cache.Dispose();

                cache.Play("01", 0.5f, 0.5f);

                soundMock.Verify(s => s.Play(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()), Times.Never);
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

        [Fact]
        public async Task PreloadAsync_FilteredSubset_OnlyLoadsSubset()
        {
            // Simulates filtering WavDefinitions to only note-referenced ids,
            // excluding BGM-only ids that are loaded separately.
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

                // Full WavDefinitions has both ids, but we only preload the note-referenced one
                await cache.PreloadAsync(new Dictionary<string, string>
                {
                    ["01"] = file1,
                    // "02" is BGM-only and excluded from preload
                });

                Assert.Equal(1, cache.Count);
                Assert.True(cache.Contains("01"));
                Assert.False(cache.Contains("02"));
                Assert.Single(factoryCalls);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Play_WhenSoundPlayThrows_ShouldNotThrow()
        {
            var (dir, file1, _) = CreateTempWavFiles("a.wav", null);
            try
            {
                var soundMock = new Mock<ISound>();
                soundMock.Setup(s => s.Play()).Throws(new InvalidOperationException("audio device error"));
                using var cache = new ChipSoundCache(_ => soundMock.Object);
                cache.PreloadAsync(new Dictionary<string, string> { ["01"] = file1 }).GetAwaiter().GetResult();

                var ex = Record.Exception(() => cache.Play("01"));

                Assert.Null(ex);
                soundMock.Verify(s => s.Play(), Times.Once);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Play_AfterDispose_ShouldBeSilent()
        {
            var (dir, file1, _) = CreateTempWavFiles("a.wav", null);
            try
            {
                var soundMock = new Mock<ISound>();
                var cache = new ChipSoundCache(_ => soundMock.Object);
                cache.PreloadAsync(new Dictionary<string, string> { ["01"] = file1 }).GetAwaiter().GetResult();
                cache.Dispose();

                cache.Play("01");

                soundMock.Verify(s => s.Play(), Times.Never);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Contains_WithNullOrEmptyWavId_ReturnsFalse(string? wavId)
        {
            using var cache = new ChipSoundCache(_ => Mock.Of<ISound>());
            Assert.False(cache.Contains(wavId!));
        }

        [Fact]
        public async Task PreloadAsync_EmptyPathValue_ShouldSkipEntry()
        {
            using var cache = new ChipSoundCache(_ => Mock.Of<ISound>());
            await cache.PreloadAsync(new Dictionary<string, string>
            {
                ["01"] = "",
                ["02"] = null!,
            });

            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public async Task PreloadAsync_DuplicateWavId_ShouldKeepFirstEntry()
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
                });
                await cache.PreloadAsync(new Dictionary<string, string>
                {
                    ["01"] = file2,
                });

                Assert.Equal(1, cache.Count);
                Assert.Single(factoryCalls);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Dispose_WhenSoundDisposeThrows_ShouldSwallowErrorAndClear()
        {
            var (dir, file1, file2) = CreateTempWavFiles("a.wav", "b.wav");
            try
            {
                var disposed = new List<ISound>();
                var factory = new Func<string, ISound>(_ =>
                {
                    var m = new Mock<ISound>();
                    m.Setup(s => s.Dispose()).Callback(() =>
                    {
                        disposed.Add(m.Object);
                        if (disposed.Count == 1)
                            throw new InvalidOperationException("dispose error");
                    });
                    return m.Object;
                });

                var cache = new ChipSoundCache(factory);
                cache.PreloadAsync(new Dictionary<string, string>
                {
                    ["01"] = file1,
                    ["02"] = file2,
                }).GetAwaiter().GetResult();

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
        public void Dispose_CalledTwice_ShouldNotThrow()
        {
            var (dir, file1, _) = CreateTempWavFiles("a.wav", null);
            try
            {
                var cache = new ChipSoundCache(_ => Mock.Of<ISound>());
                cache.PreloadAsync(new Dictionary<string, string> { ["01"] = file1 }).GetAwaiter().GetResult();
                cache.Dispose();

                var ex = Record.Exception(() => cache.Dispose());

                Assert.Null(ex);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // --- Instance tracking tests ---

        [Fact]
        public void Play_SoundReturnsNullInstance_DoesNotThrow()
        {
            // ISound.Play() can return null (e.g. disposed sound). Should be handled gracefully.
            var (dir, file1, _) = CreateTempWavFiles("a.wav", null);
            try
            {
                var soundMock = new Mock<ISound>();
                // Default Moq behavior returns null for reference types
                using var cache = new ChipSoundCache(_ => soundMock.Object);
                cache.PreloadAsync(new Dictionary<string, string> { ["01"] = file1 }).GetAwaiter().GetResult();

                var ex = Record.Exception(() => cache.Play("01"));

                Assert.Null(ex);
                soundMock.Verify(s => s.Play(), Times.Once);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void StopAll_AfterDispose_DoesNotThrow()
        {
            var cache = new ChipSoundCache(_ => Mock.Of<ISound>());
            cache.Dispose();

            var ex = Record.Exception(() => cache.StopAll());

            Assert.Null(ex);
        }

        [Fact]
        public void CleanupStoppedInstances_AfterDispose_DoesNotThrow()
        {
            var cache = new ChipSoundCache(_ => Mock.Of<ISound>());
            cache.Dispose();

            var ex = Record.Exception(() => cache.CleanupStoppedInstances());

            Assert.Null(ex);
        }

        [Fact]
        public void ActiveInstanceCount_NewInstance_IsZero()
        {
            using var cache = new ChipSoundCache(_ => Mock.Of<ISound>());
            Assert.Equal(0, cache.ActiveInstanceCount);
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
