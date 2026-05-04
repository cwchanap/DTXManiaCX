using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Xna.Framework.Audio;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Unit")]
    public class ChipSoundCacheAdditionalTests
    {
        [Fact]
        public async Task Play_SoundReturnsInstance_TracksActiveInstance()
        {
            var (dir, file1, _) = CreateTempWavFiles("a.wav", null);
            try
            {
                var instance = CreateSoundEffectInstance();
                var soundMock = new Mock<ISound>();
                soundMock.Setup(s => s.Play()).Returns(instance);

                using var cache = new ChipSoundCache(_ => soundMock.Object);
                await cache.PreloadAsync(new Dictionary<string, string> { ["01"] = file1 });

                Assert.Equal(0, cache.ActiveInstanceCount);
                cache.Play("01");
                Assert.Equal(1, cache.ActiveInstanceCount);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task Play_MultipleTimes_IncrementsActiveInstanceCount()
        {
            var (dir, file1, _) = CreateTempWavFiles("a.wav", null);
            try
            {
                var callCount = 0;
                var soundMock = new Mock<ISound>();
                soundMock.Setup(s => s.Play()).Returns(() =>
                {
                    callCount++;
                    return CreateSoundEffectInstance();
                });

                using var cache = new ChipSoundCache(_ => soundMock.Object);
                await cache.PreloadAsync(new Dictionary<string, string> { ["01"] = file1 });

                cache.Play("01");
                cache.Play("01");
                Assert.Equal(2, cache.ActiveInstanceCount);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task CleanupStoppedInstances_RemovesAllStoppedInstances()
        {
            var (dir, file1, _) = CreateTempWavFiles("a.wav", null);
            try
            {
                var soundMock = new Mock<ISound>();
                soundMock.Setup(s => s.Play()).Returns(CreateSoundEffectInstance);

                using var cache = new ChipSoundCache(_ => soundMock.Object);
                await cache.PreloadAsync(new Dictionary<string, string> { ["01"] = file1 });

                cache.Play("01");
                cache.Play("01");
                cache.Play("01");
                Assert.Equal(3, cache.ActiveInstanceCount);

                cache.CleanupStoppedInstances();

                Assert.Equal(0, cache.ActiveInstanceCount);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task StopAll_WithActiveInstances_DoesNotThrow()
        {
            var (dir, file1, _) = CreateTempWavFiles("a.wav", null);
            try
            {
                var soundMock = new Mock<ISound>();
                soundMock.Setup(s => s.Play()).Returns(CreateSoundEffectInstance);

                using var cache = new ChipSoundCache(_ => soundMock.Object);
                await cache.PreloadAsync(new Dictionary<string, string> { ["01"] = file1 });

                cache.Play("01");
                cache.Play("01");

                var ex = Record.Exception(() => cache.StopAll());
                Assert.Null(ex);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task Dispose_WithActiveInstances_StopsAndDisposesAll()
        {
            var (dir, file1, _) = CreateTempWavFiles("a.wav", null);
            try
            {
                var soundMock = new Mock<ISound>();
                soundMock.Setup(s => s.Play()).Returns(CreateSoundEffectInstance);

                var cache = new ChipSoundCache(_ => soundMock.Object);
                await cache.PreloadAsync(new Dictionary<string, string> { ["01"] = file1 });
                cache.Play("01");
                cache.Play("01");
                Assert.Equal(2, cache.ActiveInstanceCount);

                cache.Dispose();

                Assert.Equal(0, cache.ActiveInstanceCount);
                Assert.Equal(0, cache.Count);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task Play_ExceedsMaxActiveInstances_TriggersAutoCleanup()
        {
            var (dir, file1, _) = CreateTempWavFiles("a.wav", null);
            try
            {
                var soundMock = new Mock<ISound>();
                soundMock.Setup(s => s.Play()).Returns(CreateSoundEffectInstance);

                using var cache = new ChipSoundCache(_ => soundMock.Object);
                await cache.PreloadAsync(new Dictionary<string, string> { ["01"] = file1 });

                for (int i = 0; i < ChipSoundCache.MaxActiveInstances; i++)
                {
                    cache.Play("01");
                }

                Assert.Equal(ChipSoundCache.MaxActiveInstances, cache.ActiveInstanceCount);

                cache.Play("01");

                Assert.True(cache.ActiveInstanceCount <= ChipSoundCache.MaxActiveInstances);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void StopAll_WithNoActiveInstances_DoesNotThrow()
        {
            using var cache = new ChipSoundCache(_ => Mock.Of<ISound>());
            var ex = Record.Exception(() => cache.StopAll());
            Assert.Null(ex);
        }

        [Fact]
        public void CleanupStoppedInstances_WithNoActiveInstances_DoesNotThrow()
        {
            using var cache = new ChipSoundCache(_ => Mock.Of<ISound>());
            var ex = Record.Exception(() => cache.CleanupStoppedInstances());
            Assert.Null(ex);
        }

        [Fact]
        public async Task Play_CalledManyTimes_GrowsActiveInstanceList()
        {
            var (dir, file1, _) = CreateTempWavFiles("a.wav", null);
            try
            {
                var soundMock = new Mock<ISound>();
                soundMock.Setup(s => s.Play()).Returns(CreateSoundEffectInstance);

                using var cache = new ChipSoundCache(_ => soundMock.Object);
                await cache.PreloadAsync(new Dictionary<string, string> { ["01"] = file1 });

                for (int i = 0; i < 10; i++)
                {
                    cache.Play("01");
                }

                Assert.Equal(10, cache.ActiveInstanceCount);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        private static SoundEffectInstance CreateSoundEffectInstance()
        {
#pragma warning disable SYSLIB0050
            return (SoundEffectInstance)FormatterServices.GetUninitializedObject(typeof(SoundEffectInstance));
#pragma warning restore SYSLIB0050
        }

        private static (string dir, string file1, string file2) CreateTempWavFiles(string name1, string? name2)
        {
            var dir = Path.Combine(Path.GetTempPath(), $"chipcache-add-{Guid.NewGuid()}");
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
