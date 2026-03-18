using DTXMania.Game.Lib.Config;
using Xunit;

namespace DTXMania.Test.Config
{
    /// <summary>
    /// Tests for GameConstants to verify expected constant values.
    /// These tests document the intended values and guard against accidental changes.
    /// </summary>
    public class GameConstantsTests
    {
        #region StageTransition Constants

        [Fact]
        public void StageTransition_DebounceDelaySeconds_ShouldBeHalfSecond()
        {
            Assert.Equal(0.5, GameConstants.StageTransition.DebounceDelaySeconds);
        }

        #endregion

        #region JsonRpc Constants

        [Fact]
        public void JsonRpc_MaxRequestBodyBytes_ShouldBe8KB()
        {
            Assert.Equal(8 * 1024, GameConstants.JsonRpc.MaxRequestBodyBytes);
        }

        [Fact]
        public void JsonRpc_DefaultPort_ShouldBe8080()
        {
            Assert.Equal(8080, GameConstants.JsonRpc.DefaultPort);
        }

        [Fact]
        public void JsonRpc_ShutdownTimeoutMs_ShouldBe5000()
        {
            Assert.Equal(5000, GameConstants.JsonRpc.ShutdownTimeoutMs);
        }

        [Fact]
        public void JsonRpc_MaxRequestBodyBytes_ShouldBePositive()
        {
            Assert.True(GameConstants.JsonRpc.MaxRequestBodyBytes > 0);
        }

        #endregion

        #region Input Constants

        [Fact]
        public void Input_DeviceScanIntervalMs_ShouldBe3000()
        {
            Assert.Equal(3000.0, GameConstants.Input.DeviceScanIntervalMs);
        }

        [Fact]
        public void Input_TargetUpdateLatencyMs_ShouldBe1ms()
        {
            Assert.Equal(1.0, GameConstants.Input.TargetUpdateLatencyMs);
        }

        [Fact]
        public void Input_DeviceScanIntervalMs_ShouldBeGreaterThanUpdateLatency()
        {
            Assert.True(GameConstants.Input.DeviceScanIntervalMs > GameConstants.Input.TargetUpdateLatencyMs);
        }

        #endregion

        #region Performance Constants

        [Fact]
        public void Performance_SongEndBufferSeconds_ShouldBe3()
        {
            Assert.Equal(3.0, GameConstants.Performance.SongEndBufferSeconds);
        }

        [Fact]
        public void Performance_ReadyCountdownSeconds_ShouldBe1()
        {
            Assert.Equal(1.0, GameConstants.Performance.ReadyCountdownSeconds);
        }

        [Fact]
        public void Performance_SongEndBufferSeconds_ShouldBePositive()
        {
            Assert.True(GameConstants.Performance.SongEndBufferSeconds > 0);
        }

        #endregion

        #region Resources Constants

        [Fact]
        public void Resources_DefaultSkinPath_ShouldBeSystemSlash()
        {
            Assert.Equal("System/", GameConstants.Resources.DefaultSkinPath);
        }

        [Fact]
        public void Resources_FallbackSkinPath_ShouldBeSystemSlash()
        {
            Assert.Equal("System/", GameConstants.Resources.FallbackSkinPath);
        }

        [Fact]
        public void Resources_DefaultAndFallbackSkinPath_ShouldBeEqual()
        {
            Assert.Equal(GameConstants.Resources.DefaultSkinPath, GameConstants.Resources.FallbackSkinPath);
        }

        [Fact]
        public void Resources_DefaultSkinPath_ShouldEndWithSlash()
        {
            Assert.EndsWith("/", GameConstants.Resources.DefaultSkinPath);
        }

        #endregion

        #region Tasks Constants

        [Fact]
        public void Tasks_CleanupTimeoutMs_ShouldBe1000()
        {
            Assert.Equal(1000, GameConstants.Tasks.CleanupTimeoutMs);
        }

        [Fact]
        public void Tasks_CleanupTimeoutMs_ShouldBePositive()
        {
            Assert.True(GameConstants.Tasks.CleanupTimeoutMs > 0);
        }

        #endregion
    }
}
