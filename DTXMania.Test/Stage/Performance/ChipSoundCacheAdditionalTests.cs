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
    public sealed class ChipSoundCacheAdditionalTests
    {
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

        private static ChipSoundCache CreateCache(ISound sound) =>
            new(new Dictionary<string, ISound> { ["01"] = sound });

        private static SoundEffectInstance CreateSoundEffectInstance()
        {
#pragma warning disable SYSLIB0050
            return (SoundEffectInstance)FormatterServices.GetUninitializedObject(
                typeof(SoundEffectInstance));
#pragma warning restore SYSLIB0050
        }
    }
}
