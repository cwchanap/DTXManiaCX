using DTXMania.Game.Lib.Config;
using Xunit;

namespace DTXMania.Test.Config
{
    /// <summary>
    /// Tests for ConfigData API settings and remaining properties not covered in ConfigDataTests.
    /// ConfigData is a simple data-transfer class; these tests guard default values and
    /// that setters work correctly.
    /// </summary>
    public class ConfigDataApiSettingsTests
    {
        #region API Settings – Default Values

        [Fact]
        public void ConfigData_EnableGameApi_DefaultShouldBeFalse()
        {
            var config = new ConfigData();
            Assert.False(config.EnableGameApi);
        }

        [Fact]
        public void ConfigData_GameApiPort_DefaultShouldBe8080()
        {
            var config = new ConfigData();
            Assert.Equal(8080, config.GameApiPort);
        }

        [Fact]
        public void ConfigData_GameApiKey_DefaultShouldBeEmpty()
        {
            var config = new ConfigData();
            Assert.Equal(string.Empty, config.GameApiKey);
        }

        #endregion

        #region API Settings – Setter Tests

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ConfigData_SetEnableGameApi_ShouldUpdateCorrectly(bool value)
        {
            var config = new ConfigData();
            config.EnableGameApi = value;
            Assert.Equal(value, config.EnableGameApi);
        }

        [Theory]
        [InlineData(8080)]
        [InlineData(8090)]
        [InlineData(3000)]
        [InlineData(1)]
        [InlineData(65535)]
        public void ConfigData_SetGameApiPort_ShouldUpdateCorrectly(int port)
        {
            var config = new ConfigData();
            config.GameApiPort = port;
            Assert.Equal(port, config.GameApiPort);
        }

        [Theory]
        [InlineData("")]
        [InlineData("my-secret-key")]
        [InlineData("abc123-XYZ-!@#")]
        public void ConfigData_SetGameApiKey_ShouldUpdateCorrectly(string key)
        {
            var config = new ConfigData();
            config.GameApiKey = key;
            Assert.Equal(key, config.GameApiKey);
        }

        #endregion

        #region Game Settings – Default Values and Setters

        [Fact]
        public void ConfigData_NoFail_DefaultShouldBeFalse()
        {
            var config = new ConfigData();
            Assert.False(config.NoFail);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ConfigData_SetNoFail_ShouldUpdateCorrectly(bool value)
        {
            var config = new ConfigData();
            config.NoFail = value;
            Assert.Equal(value, config.NoFail);
        }

        [Fact]
        public void ConfigData_ScrollSpeed_DefaultShouldBe100()
        {
            var config = new ConfigData();
            Assert.Equal(100, config.ScrollSpeed);
        }

        [Theory]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(200)]
        [InlineData(500)]
        public void ConfigData_SetScrollSpeed_ShouldUpdateCorrectly(int speed)
        {
            var config = new ConfigData();
            config.ScrollSpeed = speed;
            Assert.Equal(speed, config.ScrollSpeed);
        }

        [Fact]
        public void ConfigData_AutoPlay_DefaultShouldBeFalse()
        {
            var config = new ConfigData();
            Assert.False(config.AutoPlay);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ConfigData_SetAutoPlay_ShouldUpdateCorrectly(bool value)
        {
            var config = new ConfigData();
            config.AutoPlay = value;
            Assert.Equal(value, config.AutoPlay);
        }

        #endregion

        #region Sound Settings – Buffer Size

        [Fact]
        public void ConfigData_BufferSizeMs_DefaultShouldBe100()
        {
            var config = new ConfigData();
            Assert.Equal(100, config.BufferSizeMs);
        }

        [Theory]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(200)]
        [InlineData(512)]
        public void ConfigData_SetBufferSizeMs_ShouldUpdateCorrectly(int bufferSize)
        {
            var config = new ConfigData();
            config.BufferSizeMs = bufferSize;
            Assert.Equal(bufferSize, config.BufferSizeMs);
        }

        #endregion
    }
}
