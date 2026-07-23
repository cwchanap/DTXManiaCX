using System;
using DTXMania.Game.Lib.Stage.Performance;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    public sealed class PlaybackModifiersTests
    {
        [Fact]
        public void DefaultProfile_UsesDirectPathValues()
        {
            var modifiers = new PlaybackModifiers(100, 0);

            Assert.True(modifiers.IsDefault);
            Assert.Equal(1.0, modifiers.Speed, 12);
            Assert.Equal(1.0, modifiers.PitchFactor, 12);
            Assert.Equal(1.0, modifiers.FfmpegTempoFactor, 12);
            Assert.Equal(0.0f, modifiers.MonoGamePitch, 6);
        }

        [Theory]
        [InlineData(50, 12, 0.5, 2.0, 0.25, 1.0)]
        [InlineData(50, -12, 0.5, 0.5, 1.0, -1.0)]
        [InlineData(150, 12, 1.5, 2.0, 0.75, 1.0)]
        [InlineData(150, -12, 1.5, 0.5, 3.0, -1.0)]
        public void RangeBoundaries_DeriveExpectedAudioFactors(
            int playSpeedPercent,
            int pitchSemitones,
            double expectedSpeed,
            double expectedPitchFactor,
            double expectedTempoFactor,
            double expectedMonoGamePitch)
        {
            var modifiers = new PlaybackModifiers(playSpeedPercent, pitchSemitones);

            Assert.False(modifiers.IsDefault);
            Assert.Equal(expectedSpeed, modifiers.Speed, 12);
            Assert.Equal(expectedPitchFactor, modifiers.PitchFactor, 12);
            Assert.Equal(expectedTempoFactor, modifiers.FfmpegTempoFactor, 12);
            Assert.Equal((float)expectedMonoGamePitch, modifiers.MonoGamePitch, 6);
        }

        [Fact]
        public void PitchFactor_UsesEqualTemperedSemitones()
        {
            var modifiers = new PlaybackModifiers(100, 7);

            Assert.Equal(Math.Pow(2.0, 7.0 / 12.0), modifiers.PitchFactor, 12);
            Assert.Equal(1.0 / modifiers.PitchFactor, modifiers.FfmpegTempoFactor, 12);
        }

        [Theory]
        [InlineData(100, 1)]
        [InlineData(95, 0)]
        public void AnyNonDefaultComponent_DisablesDefaultBypass(
            int playSpeedPercent,
            int pitchSemitones)
        {
            Assert.False(new PlaybackModifiers(playSpeedPercent, pitchSemitones).IsDefault);
        }
    }
}