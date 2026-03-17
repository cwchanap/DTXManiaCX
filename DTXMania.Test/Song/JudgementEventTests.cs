using System;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Tests for JudgementEvent, TimingConstants, InputLane, and InputLaneExtensions
    /// </summary>
    public class JudgementEventTests
    {
        #region JudgementEvent Constructor Tests

        [Fact]
        public void DefaultConstructor_ShouldSetMissTypeAndRecentTimestamp()
        {
            var before = DateTime.UtcNow;
            var evt = new JudgementEvent();
            var after = DateTime.UtcNow;

            Assert.Equal(JudgementType.Miss, evt.Type);
            Assert.InRange(evt.Timestamp, before, after);
            Assert.Equal(0, evt.NoteRef);
            Assert.Equal(0, evt.Lane);
            Assert.Equal(0.0, evt.DeltaMs);
        }

        [Fact]
        public void FullConstructor_ShouldSetAllProperties()
        {
            var evt = new JudgementEvent(noteRef: 42, lane: 3, deltaMs: -10.5, type: JudgementType.Great);

            Assert.Equal(42, evt.NoteRef);
            Assert.Equal(3, evt.Lane);
            Assert.Equal(-10.5, evt.DeltaMs);
            Assert.Equal(JudgementType.Great, evt.Type);
        }

        [Fact]
        public void DeltaConstructor_PerfectTiming_ShouldCalculateJustType()
        {
            var evt = new JudgementEvent(noteRef: 1, lane: 0, deltaMs: 0.0);
            Assert.Equal(JudgementType.Just, evt.Type);
        }

        [Fact]
        public void DeltaConstructor_25MsDelta_ShouldCalculateJustType()
        {
            var evt = new JudgementEvent(noteRef: 1, lane: 0, deltaMs: 25.0);
            Assert.Equal(JudgementType.Just, evt.Type);
        }

        [Fact]
        public void DeltaConstructor_26MsDelta_ShouldCalculateGreatType()
        {
            var evt = new JudgementEvent(noteRef: 1, lane: 0, deltaMs: 26.0);
            Assert.Equal(JudgementType.Great, evt.Type);
        }

        [Fact]
        public void DeltaConstructor_LargePositiveDelta_ShouldCalculateMissType()
        {
            var evt = new JudgementEvent(noteRef: 1, lane: 0, deltaMs: 201.0);
            Assert.Equal(JudgementType.Miss, evt.Type);
        }

        [Fact]
        public void DeltaConstructor_LargeNegativeDelta_ShouldCalculateMissType()
        {
            var evt = new JudgementEvent(noteRef: 1, lane: 0, deltaMs: -201.0);
            Assert.Equal(JudgementType.Miss, evt.Type);
        }

        #endregion

        #region JudgementEvent Method Tests

        [Theory]
        [InlineData(JudgementType.Just, 1000)]
        [InlineData(JudgementType.Great, 700)]
        [InlineData(JudgementType.Good, 400)]
        [InlineData(JudgementType.Poor, 100)]
        [InlineData(JudgementType.Miss, 0)]
        public void GetScoreValue_ShouldReturnCorrectPoints(JudgementType type, int expectedScore)
        {
            var evt = new JudgementEvent(1, 0, 0.0, type);
            Assert.Equal(expectedScore, evt.GetScoreValue());
        }

        [Theory]
        [InlineData(JudgementType.Just, true)]
        [InlineData(JudgementType.Great, true)]
        [InlineData(JudgementType.Good, true)]
        [InlineData(JudgementType.Poor, true)]
        [InlineData(JudgementType.Miss, false)]
        public void IsHit_ShouldReturnTrueForNonMiss(JudgementType type, bool expectedHit)
        {
            var evt = new JudgementEvent(1, 0, 0.0, type);
            Assert.Equal(expectedHit, evt.IsHit());
        }

        [Fact]
        public void IsEarly_WithNegativeDelta_ShouldReturnTrue()
        {
            var evt = new JudgementEvent(1, 0, -15.0, JudgementType.Just);
            Assert.True(evt.IsEarly());
            Assert.False(evt.IsLate());
        }

        [Fact]
        public void IsLate_WithPositiveDelta_ShouldReturnTrue()
        {
            var evt = new JudgementEvent(1, 0, 15.0, JudgementType.Just);
            Assert.True(evt.IsLate());
            Assert.False(evt.IsEarly());
        }

        [Fact]
        public void IsEarly_IsLate_WithZeroDelta_ShouldBothReturnFalse()
        {
            var evt = new JudgementEvent(1, 0, 0.0, JudgementType.Just);
            Assert.False(evt.IsEarly());
            Assert.False(evt.IsLate());
        }

        [Fact]
        public void GetAbsoluteDelta_WithNegativeValue_ShouldReturnPositive()
        {
            var evt = new JudgementEvent(1, 0, -42.5, JudgementType.Good);
            Assert.Equal(42.5, evt.GetAbsoluteDelta());
        }

        [Fact]
        public void GetAbsoluteDelta_WithPositiveValue_ShouldReturnSameValue()
        {
            var evt = new JudgementEvent(1, 0, 30.0, JudgementType.Great);
            Assert.Equal(30.0, evt.GetAbsoluteDelta());
        }

        [Fact]
        public void ToString_WithHitEvent_ShouldContainTypeAndScore()
        {
            var evt = new JudgementEvent(1, 4, -10.0, JudgementType.Just);
            var result = evt.ToString();

            Assert.Contains("Just", result);
            Assert.Contains("1000", result);
        }

        [Fact]
        public void ToString_WithMissEvent_ShouldContainMissAndZeroScore()
        {
            var evt = new JudgementEvent(1, 0, 0.0, JudgementType.Miss);
            var result = evt.ToString();

            Assert.Contains("Miss", result);
            Assert.Contains("0", result);
        }

        [Fact]
        public void ToString_WithEarlyHit_ShouldContainEarly()
        {
            var evt = new JudgementEvent(1, 5, -20.0, JudgementType.Just);
            var result = evt.ToString();
            Assert.Contains("early", result);
        }

        [Fact]
        public void ToString_WithLateHit_ShouldContainLate()
        {
            var evt = new JudgementEvent(1, 5, 20.0, JudgementType.Just);
            var result = evt.ToString();
            Assert.Contains("late", result);
        }

        [Fact]
        public void ToString_WithPerfectTiming_ShouldContainPerfect()
        {
            var evt = new JudgementEvent(1, 5, 0.0, JudgementType.Just);
            var result = evt.ToString();
            Assert.Contains("perfect", result);
        }

        [Fact]
        public void ToString_WithUnknownLane_ShouldFallbackToLaneDisplay()
        {
            var evt = new JudgementEvent(1, 99, 0.0, JudgementType.Good);
            var result = evt.ToString();
            Assert.Contains("Lane 99", result);
        }

        #endregion

        #region TimingConstants Tests

        [Fact]
        public void TimingConstants_WindowValues_ShouldBeCorrect()
        {
            Assert.Equal(25.0, TimingConstants.JustWindowMs);
            Assert.Equal(50.0, TimingConstants.GreatWindowMs);
            Assert.Equal(100.0, TimingConstants.GoodWindowMs);
            Assert.Equal(150.0, TimingConstants.PoorWindowMs);
            Assert.Equal(200.0, TimingConstants.MissThresholdMs);
        }

        [Fact]
        public void TimingConstants_ScoreValues_ShouldBeCorrect()
        {
            Assert.Equal(1000, TimingConstants.JustScore);
            Assert.Equal(700, TimingConstants.GreatScore);
            Assert.Equal(400, TimingConstants.GoodScore);
            Assert.Equal(100, TimingConstants.PoorScore);
            Assert.Equal(0, TimingConstants.MissScore);
        }

        [Theory]
        [InlineData(0.0, JudgementType.Just)]
        [InlineData(25.0, JudgementType.Just)]
        [InlineData(-25.0, JudgementType.Just)]
        [InlineData(25.1, JudgementType.Great)]
        [InlineData(50.0, JudgementType.Great)]
        [InlineData(-50.0, JudgementType.Great)]
        [InlineData(50.1, JudgementType.Good)]
        [InlineData(100.0, JudgementType.Good)]
        [InlineData(100.1, JudgementType.Poor)]
        [InlineData(150.0, JudgementType.Poor)]
        [InlineData(150.1, JudgementType.Miss)]
        [InlineData(200.0, JudgementType.Miss)]
        [InlineData(999.0, JudgementType.Miss)]
        public void GetJudgementType_ShouldReturnCorrectType(double deltaMs, JudgementType expected)
        {
            Assert.Equal(expected, TimingConstants.GetJudgementType(deltaMs));
        }

        [Theory]
        [InlineData(JudgementType.Just, 1000)]
        [InlineData(JudgementType.Great, 700)]
        [InlineData(JudgementType.Good, 400)]
        [InlineData(JudgementType.Poor, 100)]
        [InlineData(JudgementType.Miss, 0)]
        public void GetScoreValue_AllTypes_ShouldReturnCorrectScore(JudgementType type, int expectedScore)
        {
            Assert.Equal(expectedScore, TimingConstants.GetScoreValue(type));
        }

        [Fact]
        public void GetScoreValue_InvalidType_ShouldReturnZero()
        {
            Assert.Equal(0, TimingConstants.GetScoreValue((JudgementType)999));
        }

        #endregion

        #region InputLaneExtensions Tests

        [Theory]
        [InlineData(0x1A, InputLane.Splash)]
        [InlineData(0x18, InputLane.FloorTomLeftCymbal)]
        [InlineData(0x11, InputLane.FloorTomLeftCymbal)]
        [InlineData(0x1B, InputLane.HiHatFootLeftCrash)]
        [InlineData(0x1C, InputLane.HiHatFootLeftCrash)]
        [InlineData(0x12, InputLane.LeftPedal)]
        [InlineData(0x14, InputLane.SnareDrum)]
        [InlineData(0x13, InputLane.HiHat)]
        [InlineData(0x15, InputLane.BassDrum)]
        [InlineData(0x16, InputLane.HighTom)]
        [InlineData(0x17, InputLane.LowTomRightCymbal)]
        [InlineData(0x19, InputLane.LowTomRightCymbal)]
        public void FromChannel_ValidChannels_ShouldReturnCorrectLane(int channel, InputLane expected)
        {
            var result = InputLaneExtensions.FromChannel(channel);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0x00)]
        [InlineData(0xFF)]
        [InlineData(-1)]
        public void FromChannel_InvalidChannel_ShouldReturnNull(int channel)
        {
            var result = InputLaneExtensions.FromChannel(channel);
            Assert.Null(result);
        }

        [Theory]
        [InlineData(InputLane.Splash, 0)]
        [InlineData(InputLane.FloorTomLeftCymbal, 1)]
        [InlineData(InputLane.HiHatFootLeftCrash, 2)]
        [InlineData(InputLane.LeftPedal, 3)]
        [InlineData(InputLane.SnareDrum, 4)]
        [InlineData(InputLane.HiHat, 5)]
        [InlineData(InputLane.BassDrum, 6)]
        [InlineData(InputLane.HighTom, 7)]
        [InlineData(InputLane.LowTomRightCymbal, 8)]
        public void ToLaneIndex_AllLanes_ShouldReturnCorrectIndex(InputLane lane, int expectedIndex)
        {
            Assert.Equal(expectedIndex, lane.ToLaneIndex());
        }

        [Fact]
        public void ToLaneIndex_InvalidLane_ShouldReturnMinusOne()
        {
            Assert.Equal(-1, ((InputLane)999).ToLaneIndex());
        }

        [Theory]
        [InlineData(0, InputLane.Splash)]
        [InlineData(1, InputLane.FloorTomLeftCymbal)]
        [InlineData(2, InputLane.HiHatFootLeftCrash)]
        [InlineData(3, InputLane.LeftPedal)]
        [InlineData(4, InputLane.SnareDrum)]
        [InlineData(5, InputLane.HiHat)]
        [InlineData(6, InputLane.BassDrum)]
        [InlineData(7, InputLane.HighTom)]
        [InlineData(8, InputLane.LowTomRightCymbal)]
        public void FromLaneIndex_ValidIndices_ShouldReturnCorrectLane(int laneIndex, InputLane expected)
        {
            var result = InputLaneExtensions.FromLaneIndex(laneIndex);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(9)]
        [InlineData(100)]
        public void FromLaneIndex_InvalidIndex_ShouldReturnNull(int laneIndex)
        {
            var result = InputLaneExtensions.FromLaneIndex(laneIndex);
            Assert.Null(result);
        }

        [Theory]
        [InlineData(InputLane.Splash, "Splash/Crash")]
        [InlineData(InputLane.FloorTomLeftCymbal, "Floor Tom/Left Cymbal")]
        [InlineData(InputLane.HiHatFootLeftCrash, "Hi-Hat Foot/Left Crash")]
        [InlineData(InputLane.LeftPedal, "Left Pedal")]
        [InlineData(InputLane.SnareDrum, "Snare Drum")]
        [InlineData(InputLane.HiHat, "Hi-Hat")]
        [InlineData(InputLane.BassDrum, "Bass Drum")]
        [InlineData(InputLane.HighTom, "High Tom")]
        [InlineData(InputLane.LowTomRightCymbal, "Low Tom/Right Cymbal")]
        public void GetDisplayName_AllLanes_ShouldReturnCorrectName(InputLane lane, string expectedName)
        {
            Assert.Equal(expectedName, lane.GetDisplayName());
        }

        [Fact]
        public void GetDisplayName_InvalidLane_ShouldReturnUnknown()
        {
            Assert.Equal("Unknown", ((InputLane)999).GetDisplayName());
        }

        [Fact]
        public void ToLaneIndex_RoundTrip_ShouldReturnOriginalLane()
        {
            var originalLane = InputLane.SnareDrum;
            var index = originalLane.ToLaneIndex();
            var roundTripped = InputLaneExtensions.FromLaneIndex(index);

            Assert.Equal(originalLane, roundTripped);
        }

        #endregion
    }
}
