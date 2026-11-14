using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Xna.Framework.Audio;
using Moq;
using Xunit;
namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Unit")]
    public sealed class ChipSoundCacheTests
    {
        [Fact]
        public void Constructor_WithBorrowedMap_ExposesEntries()
        {
            var first = Mock.Of<ISound>();
            var second = Mock.Of<ISound>();

            using var cache = new ChipSoundCache(
                new Dictionary<string, ISound>
                {
                    ["01"] = first,
                    ["02"] = second,
                });

            Assert.Equal(2, cache.Count);
            Assert.True(cache.Contains("01"));
            Assert.True(cache.Contains("02"));
        }

        [Fact]
        public void Constructor_WithNullMap_CreatesEmptyCache()
        {
            using var cache = new ChipSoundCache();

            Assert.Equal(0, cache.Count);
            Assert.Equal(0, cache.ActiveInstanceCount);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Contains_WithMissingId_ReturnsFalse(string? wavId)
        {
            using var cache = new ChipSoundCache();

            Assert.False(cache.Contains(wavId!));
        }

        [Fact]
        public void Play_WithUnknownOrEmptyId_IsSilent()
        {
            using var cache = new ChipSoundCache();

            Assert.Null(Record.Exception(() => cache.Play("NOPE")));
            Assert.Null(Record.Exception(() => cache.Play("")));
            Assert.Null(Record.Exception(() => cache.Play(null!)));
        }

        [Fact]
        public void Play_DefaultValues_UsesFastPath()
        {
            var sound = new Mock<ISound>();
            using var cache = CreateCache(sound.Object);

            cache.Play("01");

            sound.Verify(value => value.Play(), Times.Once);
            sound.Verify(
                value => value.Play(
                    It.IsAny<float>(),
                    It.IsAny<float>(),
                    It.IsAny<float>()),
                Times.Never);
        }

        [Fact]
        public void Play_NonDefaultValues_UsesParameterizedPath()
        {
            var sound = new Mock<ISound>();
            using var cache = CreateCache(sound.Object);

            cache.Play("01", volume: 0.5f, pitch: 0.25f, pan: -0.3f);

            sound.Verify(value => value.Play(), Times.Never);
            sound.Verify(value => value.Play(0.5f, 0.25f, -0.3f), Times.Once);
        }

        [Fact]
        public void Play_NonZeroPitch_BypassesDefaultFastPath()
        {
            var sound = new Mock<ISound>();
            using var cache = CreateCache(sound.Object);

            cache.Play("01", volume: 1.0f, pitch: 0.5f, pan: 0.0f);

            sound.Verify(value => value.Play(), Times.Never);
            sound.Verify(value => value.Play(1.0f, 0.5f, 0.0f), Times.Once);
        }

        [Fact]
        public void Play_WhenBorrowedSoundThrows_SwallowsFailure()
        {
            var sound = new Mock<ISound>();
            sound
                .Setup(value => value.Play())
                .Throws(new InvalidOperationException("audio device error"));
            using var cache = CreateCache(sound.Object);

            var exception = Record.Exception(() => cache.Play("01"));

            Assert.Null(exception);
            sound.Verify(value => value.Play(), Times.Once);
        }

        [Fact]
        public void Dispose_ClearsMapWithoutDisposingBorrowedSounds()
        {
            var first = new Mock<ISound>();
            var second = new Mock<ISound>();
            var cache = new ChipSoundCache(
                new Dictionary<string, ISound>
                {
                    ["01"] = first.Object,
                    ["02"] = second.Object,
                });

            cache.Dispose();
            cache.Dispose();

            Assert.Equal(0, cache.Count);
            first.Verify(value => value.Dispose(), Times.Never);
            second.Verify(value => value.Dispose(), Times.Never);
        }

        [Fact]
        public void Play_AfterDispose_DoesNotUseBorrowedSound()
        {
            var sound = new Mock<ISound>();
            var cache = CreateCache(sound.Object);
            cache.Dispose();

            cache.Play("01");

            sound.Verify(value => value.Play(), Times.Never);
        }

        [Fact]
        public void StopAndCleanup_WithNoInstances_DoNotThrow()
        {
            using var cache = new ChipSoundCache();

            Assert.Null(Record.Exception(cache.StopAll));
            Assert.Null(Record.Exception(cache.CleanupStoppedInstances));
        }

        private static ChipSoundCache CreateCache(ISound sound) =>
            new(new Dictionary<string, ISound> { ["01"] = sound });

        [Fact]
        public void Play_ReturnedInstance_IsTracked()
        {
            var sound = new Mock<ISound>();
            sound.Setup(value => value.Play()).Returns(CreateSoundEffectInstance());
            using var cache = CreateCache(sound.Object);

            cache.Play("01");

            Assert.Equal(1, cache.ActiveInstanceCount);
        }

        [Fact]
        public void Play_MultipleReturnedInstances_AreTracked()
        {
            var sound = new Mock<ISound>();
            sound.Setup(value => value.Play()).Returns(CreateSoundEffectInstance);
            using var cache = CreateCache(sound.Object);

            cache.Play("01");
            cache.Play("01");

            Assert.Equal(2, cache.ActiveInstanceCount);
        }

        [Fact]
        public void Play_NullInstance_DoesNotAddTrackingEntry()
        {
            var sound = new Mock<ISound>();
            using var cache = CreateCache(sound.Object);

            cache.Play("01");

            Assert.Equal(0, cache.ActiveInstanceCount);
        }

        [Fact]
        public void CleanupStoppedInstances_RemovesStoppedInstances()
        {
            var sound = new Mock<ISound>();
            sound.Setup(value => value.Play()).Returns(CreateSoundEffectInstance);
            using var cache = CreateCache(sound.Object);
            cache.Play("01");
            cache.Play("01");

            cache.CleanupStoppedInstances();

            Assert.Equal(0, cache.ActiveInstanceCount);
        }

        [Fact]
        public void Play_AboveInstanceLimit_PerformsAutomaticCleanup()
        {
            var sound = new Mock<ISound>();
            sound.Setup(value => value.Play()).Returns(CreateSoundEffectInstance);
            using var cache = CreateCache(sound.Object);

            for (var index = 0; index <= ChipSoundCache.MaxActiveInstances; index++)
                cache.Play("01");

            Assert.True(cache.ActiveInstanceCount <= ChipSoundCache.MaxActiveInstances);
        }

        [Fact]
        public void Dispose_WithActiveInstances_ClearsTrackingAndBorrowedMap()
        {
            var sound = new Mock<ISound>();
            sound.Setup(value => value.Play()).Returns(CreateSoundEffectInstance);
            var cache = CreateCache(sound.Object);
            cache.Play("01");
            cache.Play("01");

            cache.Dispose();

            Assert.Equal(0, cache.ActiveInstanceCount);
            Assert.Equal(0, cache.Count);
            sound.Verify(value => value.Dispose(), Times.Never);
        }

        [Fact]
        public void Constructor_IgnoresEmptyIdsAndNullSounds()
        {
            var validSound = Mock.Of<ISound>();
            using var cache = new ChipSoundCache(
                new Dictionary<string, ISound>
                {
                    [""] = validSound,
                    ["01"] = null!,
                    ["02"] = validSound,
                });

            Assert.Equal(1, cache.Count);
            Assert.False(cache.Contains(""));
            Assert.False(cache.Contains("01"));
            Assert.True(cache.Contains("02"));
        }

        [Fact]
        public void Play_WithVolumeAndPan_ForwardsZeroPitch()
        {
            var sound = new Mock<ISound>();
            using var cache = new ChipSoundCache(
                new Dictionary<string, ISound> { ["01"] = sound.Object });

            cache.Play("01", volume: 0.5f, pan: -0.25f);

            sound.Verify(
                value => value.Play(0.5f, 0.0f, -0.25f),
                Times.Once);
            sound.Verify(value => value.Play(), Times.Never);
        }

        private static SoundEffectInstance CreateSoundEffectInstance()
        {
#pragma warning disable SYSLIB0050
            return (SoundEffectInstance)FormatterServices.GetUninitializedObject(
                typeof(SoundEffectInstance));
#pragma warning restore SYSLIB0050
        }
    }
}
