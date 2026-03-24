using System;
using DTXMania.Game.Lib.Resources;
using Xunit;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Unit tests for SoundInstanceWrapper.
    /// SoundEffectInstance is a sealed MonoGame class that requires an audio device to
    /// be initialised, so the tests focus on the contract behaviour that can be verified
    /// without a real audio engine: constructor guards, interface compliance, and type
    /// relationships.
    /// </summary>
    public class SoundInstanceWrapperTests
    {
        #region Constructor Guard Tests

        [Fact]
        public void Constructor_WithNullInstance_ShouldThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new SoundInstanceWrapper(null!));
        }

        [Fact]
        public void Constructor_NullInstance_ExceptionShouldNameParameter()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new SoundInstanceWrapper(null!));

            Assert.Equal("instance", ex.ParamName);
        }

        #endregion

        #region Interface / Type Compliance Tests

        [Fact]
        public void SoundInstanceWrapper_ShouldImplementISoundInstance()
        {
            Assert.True(typeof(ISoundInstance).IsAssignableFrom(typeof(SoundInstanceWrapper)));
        }

        [Fact]
        public void SoundInstanceWrapper_ShouldImplementIDisposable()
        {
            Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(SoundInstanceWrapper)));
        }

        [Fact]
        public void SoundInstanceWrapper_ShouldBePublic()
        {
            Assert.True(typeof(SoundInstanceWrapper).IsPublic);
        }

        [Fact]
        public void SoundInstanceWrapper_ShouldNotBeAbstract()
        {
            Assert.False(typeof(SoundInstanceWrapper).IsAbstract);
        }

        #endregion
    }
}
