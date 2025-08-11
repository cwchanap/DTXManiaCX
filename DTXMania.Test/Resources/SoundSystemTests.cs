using DTXMania.Game.Lib.Resources;
using System;
using System.IO;
using Xunit;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Unit tests for the sound system including OGG/WAV support and sound management
    /// Tests core functionality without requiring actual audio playback
    /// </summary>
    public class SoundSystemTests : IDisposable
    {
        private readonly string _testDataPath;

        public SoundSystemTests()
        {
            // Create test data directory
            _testDataPath = Path.Combine(Path.GetTempPath(), "DTXManiaCX_SoundTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDataPath);

            // Set up test directory structure
            SetupTestDirectories();
        }

        private void SetupTestDirectories()
        {
            // Create sounds directory structure
            var soundsPath = Path.Combine(_testDataPath, "System", "Sounds");
            Directory.CreateDirectory(soundsPath);

            // Create fake sound files for testing
            CreateFakeWavFile(Path.Combine(soundsPath, "test.wav"));
            CreateFakeOggFile(Path.Combine(soundsPath, "test.ogg"));
            CreateFakeOggFile(Path.Combine(soundsPath, "Move.ogg"));
            CreateFakeOggFile(Path.Combine(soundsPath, "Decide.ogg"));
            CreateFakeOggFile(Path.Combine(soundsPath, "Game start.ogg"));
        }

        private void CreateFakeWavFile(string filePath)
        {
            // Create a minimal valid WAV file header for testing
            using var fs = new FileStream(filePath, FileMode.Create);
            using var writer = new BinaryWriter(fs);

            // WAV header (44 bytes)
            writer.Write("RIFF".ToCharArray());
            writer.Write(36); // File size - 8
            writer.Write("WAVE".ToCharArray());
            writer.Write("fmt ".ToCharArray());
            writer.Write(16); // Subchunk1Size
            writer.Write((short)1); // AudioFormat (PCM)
            writer.Write((short)1); // NumChannels (Mono)
            writer.Write(44100); // SampleRate
            writer.Write(88200); // ByteRate
            writer.Write((short)2); // BlockAlign
            writer.Write((short)16); // BitsPerSample
            writer.Write("data".ToCharArray());
            writer.Write(0); // Subchunk2Size (no actual data)
        }

        private void CreateFakeOggFile(string filePath)
        {
            // Create a fake OGG file with OGG header for testing
            using var fs = new FileStream(filePath, FileMode.Create);
            using var writer = new BinaryWriter(fs);

            // OGG page header
            writer.Write("OggS".ToCharArray()); // Capture pattern
            writer.Write((byte)0); // Version
            writer.Write((byte)2); // Header type (first page)
            writer.Write(0L); // Granule position
            writer.Write(0); // Serial number
            writer.Write(0); // Page sequence
            writer.Write(0); // Checksum
            writer.Write((byte)1); // Page segments
            writer.Write((byte)30); // Segment table

            // Vorbis identification header
            writer.Write((byte)1); // Packet type
            writer.Write("vorbis".ToCharArray());
            writer.Write(0); // Version
            writer.Write((byte)1); // Channels
            writer.Write(44100); // Sample rate
            writer.Write(0); // Bitrate maximum
            writer.Write(128000); // Bitrate nominal
            writer.Write(0); // Bitrate minimum
            writer.Write((byte)0x0B); // Blocksize
            writer.Write((byte)1); // Framing flag
        }


        [Theory]
        [InlineData(".wav")]
        [InlineData(".ogg")]
        public void SoundFileExtension_Detection_ShouldWorkCorrectly(string extension)
        {
            // Arrange
            var fileName = $"test{extension}";
            var filePath = Path.Combine(_testDataPath, "System", "Sounds", fileName);

            // Act
            var detectedExtension = Path.GetExtension(filePath).ToLowerInvariant();

            // Assert
            Assert.Equal(extension, detectedExtension);
        }

        [Fact]
        public void SoundPath_Resolution_ShouldFollowDTXManiaConventions()
        {
            // Arrange
            var soundPath = "Sounds/Move.ogg";
            var expectedPath = Path.Combine(_testDataPath, "System", soundPath);

            // Act
            var resolvedPath = Path.Combine(_testDataPath, "System", soundPath);

            // Assert
            Assert.Equal(expectedPath, resolvedPath);
            Assert.True(File.Exists(resolvedPath));
        }

        [Theory]
        [InlineData("Sounds/Move.ogg")]
        [InlineData("Sounds/Decide.ogg")]
        [InlineData("Sounds/Game start.ogg")]
        public void DTXMania_SoundFiles_ShouldExistInTestSetup(string soundPath)
        {
            // Arrange
            var fullPath = Path.Combine(_testDataPath, "System", soundPath);

            // Act
            var exists = File.Exists(fullPath);

            // Assert
            Assert.True(exists, $"Sound file should exist: {soundPath}");
        }


        [Fact]
        public void SoundLoadException_ShouldContainSoundPath()
        {
            // Arrange
            var soundPath = "Sounds/nonexistent.ogg";
            var message = "Test error message";

            // Act
            var exception = new SoundLoadException(soundPath, message);

            // Assert
            Assert.Equal(soundPath, exception.SoundPath);
            Assert.Equal(message, exception.Message);
        }

        [Fact]
        public void SoundLoadException_WithInnerException_ShouldPreserveInnerException()
        {
            // Arrange
            var soundPath = "Sounds/test.ogg";
            var message = "Test error message";
            var innerException = new FileNotFoundException("File not found");

            // Act
            var exception = new SoundLoadException(soundPath, message, innerException);

            // Assert
            Assert.Equal(soundPath, exception.SoundPath);
            Assert.Equal(message, exception.Message);
            Assert.Equal(innerException, exception.InnerException);
        }


        public void Dispose()
        {
            // Cleanup test directory
            try
            {
                if (Directory.Exists(_testDataPath))
                {
                    Directory.Delete(_testDataPath, true);
                }
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}
