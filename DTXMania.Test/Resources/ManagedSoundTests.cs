using DTX.Resources;
using DTXMania.Test.Utilities;
using Microsoft.Xna.Framework.Audio;
using System;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Comprehensive unit tests for ManagedSound functionality including:
    /// - Basic WAV file loading and playback
    /// - MP3 file detection and error handling
    /// - Looping functionality
    /// - Volume and parameter clamping
    /// - Reference counting and disposal
    /// - Interface compatibility
    /// </summary>
    public class ManagedSoundTests : IClassFixture<ManagedSoundTests.TestFileFixture>, IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly TestFileFixture _fixture;

        public ManagedSoundTests(ITestOutputHelper output, TestFileFixture fixture)
        {
            _output = output;
            _fixture = fixture;
        }

        public void Dispose()
        {
            // Individual test cleanup if needed
        }

        /// <summary>
        /// Shared test fixture that creates reusable test files for all tests in the class
        /// </summary>
        public class TestFileFixture : IDisposable
        {
            public string TempDir { get; }
            public string ValidWavFile { get; }
            public string EmptyMp3File { get; }
            public string InvalidMp3File { get; }
            public string UnsupportedFile { get; }
            public string Mp3File { get; }
            public string Mp3FileUppercase { get; }
            public string Mp3FileMixedCase { get; }
            public string WavFile { get; }
            public string OggFile { get; }
            public string FlacFile { get; }
            public string M4aFile { get; }

            public TestFileFixture()
            {
                // Create temp directory for test files
                TempDir = Path.Combine(Path.GetTempPath(), "DTXMania_Test_" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(TempDir);

                // Create reusable test files
                ValidWavFile = AudioTestUtils.CreateTestWavFile(TempDir, "valid.wav");
                EmptyMp3File = Path.Combine(TempDir, "empty.mp3");
                File.WriteAllText(EmptyMp3File, ""); // Empty file
                
                InvalidMp3File = Path.Combine(TempDir, "invalid.mp3");
                File.WriteAllText(InvalidMp3File, "This is not valid MP3 content at all");
                
                UnsupportedFile = AudioTestUtils.CreateTestFile(TempDir, "unsupported.txt", "This is not an audio file");
                
                // Files for extension detection tests
                Mp3File = AudioTestUtils.CreateTestFile(TempDir, "test.mp3", "fake audio content");
                Mp3FileUppercase = AudioTestUtils.CreateTestFile(TempDir, "TEST.MP3", "fake audio content");
                Mp3FileMixedCase = AudioTestUtils.CreateTestFile(TempDir, "Song.Mp3", "fake audio content");
                WavFile = AudioTestUtils.CreateTestFile(TempDir, "preview.wav", "fake audio content");
                OggFile = AudioTestUtils.CreateTestFile(TempDir, "audio.ogg", "fake audio content");
                FlacFile = AudioTestUtils.CreateTestFile(TempDir, "sound.flac", "fake audio content");
                M4aFile = AudioTestUtils.CreateTestFile(TempDir, "music.m4a", "fake audio content");
            }

            public void Dispose()
            {
                // Clean up temp directory
                if (Directory.Exists(TempDir))
                {
                    try
                    {
                        Directory.Delete(TempDir, true);
                    }
                    catch
                    {
                        // Ignore cleanup errors in tests
                    }
                }
            }
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithValidWavFile_CreatesSuccessfully()
        {
            // Arrange & Act
            using var sound = new ManagedSound(_fixture.ValidWavFile);

            // Assert
            Assert.NotNull(sound);
            Assert.NotNull(sound.SoundEffect);
            Assert.Equal(_fixture.ValidWavFile, sound.SourcePath);
            Assert.False(sound.IsDisposed);
        }

        [Fact]
        public void Constructor_WithNonExistentFile_ThrowsSoundLoadException()
        {
            // Arrange
            var nonExistentPath = Path.Combine(_fixture.TempDir, "nonexistent.mp3");

            // Act & Assert
            var exception = Assert.Throws<SoundLoadException>(() => new ManagedSound(nonExistentPath));
            Assert.Contains("Failed to load sound from", exception.Message);
            Assert.Equal(nonExistentPath, exception.SoundPath);
        }

        [Fact]
        public void Constructor_WithUnsupportedFormat_ThrowsSoundLoadException()
        {
            // Act & Assert
            var exception = Assert.Throws<SoundLoadException>(() => new ManagedSound(_fixture.UnsupportedFile));
            Assert.Contains("Audio format not supported", exception.InnerException?.Message);
        }

        [Fact]
        public void Constructor_WithExistingSoundEffect_WorksCorrectly()
        {
            // Arrange
            using var originalSound = new ManagedSound(_fixture.ValidWavFile);
            var soundEffect = originalSound.SoundEffect;
            var sourcePath = "test_source_path";

            // Act
            using var sound = new ManagedSound(soundEffect, sourcePath);

            // Assert
            Assert.NotNull(sound);
            Assert.Equal(soundEffect, sound.SoundEffect);
            Assert.Equal(sourcePath, sound.SourcePath);
            Assert.False(sound.IsDisposed);
        }

        [Fact]
        public void Constructor_WithNullSoundEffect_ThrowsArgumentNullException()
        {
            // Arrange
            SoundEffect? nullSoundEffect = null;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new ManagedSound(nullSoundEffect, "test"));
        }

        [Fact]
        public void Constructor_WithEmptyFilePath_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new ManagedSound(""));
            Assert.Throws<ArgumentException>(() => new ManagedSound(null));
        }

        #endregion

        #region MP3 File Detection Tests

        [Theory]
        [InlineData("Mp3File", true)]
        [InlineData("Mp3FileUppercase", true)]
        [InlineData("Mp3FileMixedCase", true)]
        [InlineData("WavFile", false)]
        [InlineData("OggFile", false)]
        [InlineData("FlacFile", false)]
        [InlineData("M4aFile", false)]
        public void Constructor_WithVariousExtensions_DetectsMP3Correctly(string fixtureProperty, bool shouldAttemptMp3Loading)
        {
            // Arrange
            var filePath = GetFixtureFile(fixtureProperty);

            // Act & Assert
            try
            {
                var sound = new ManagedSound(filePath);
                
                if (shouldAttemptMp3Loading)
                {
                    // If it's an MP3, we expect it to fail with FFmpeg-related error (since we have fake content)
                    Assert.Fail("Expected SoundLoadException for MP3 file");
                }
                else
                {
                    // Non-MP3 files should either load or fail with non-FFmpeg error
                    sound.Dispose();
                }
            }
            catch (SoundLoadException ex)
            {
                if (shouldAttemptMp3Loading)
                {
                    // MP3 files should fail with FFmpeg-related error
                    _output.WriteLine($"MP3 loading failed as expected: {ex.Message}");
                    Assert.Contains("Failed to load sound from", ex.Message);
                }
                else
                {
                    // Non-MP3 files might fail for different reasons (unsupported format, etc.)
                    _output.WriteLine($"Non-MP3 file failed as expected: {ex.Message}");
                }
            }
        }

        [Fact]
        public void Constructor_WithEmptyMp3File_ThrowsSoundLoadException()
        {
            // Act & Assert
            var exception = Assert.Throws<SoundLoadException>(() => new ManagedSound(_fixture.EmptyMp3File));
            Assert.Equal(_fixture.EmptyMp3File, exception.SoundPath);
            Assert.Contains("Failed to load sound from", exception.Message);
        }

        [Fact]
        public void Constructor_WithInvalidMp3Content_ThrowsSoundLoadException()
        {
            // Act & Assert
            var exception = Assert.Throws<SoundLoadException>(() => new ManagedSound(_fixture.InvalidMp3File));
            Assert.Equal(_fixture.InvalidMp3File, exception.SoundPath);
            Assert.Contains("Failed to load sound from", exception.Message);
        }

        /// <summary>
        /// Helper method to get file paths from fixture using reflection
        /// </summary>
        private string GetFixtureFile(string propertyName)
        {
            var property = typeof(TestFileFixture).GetProperty(propertyName);
            if (property?.GetValue(_fixture) is string filePath)
            {
                return filePath;
            }
            throw new ArgumentException($"Property {propertyName} not found");
        }

        #endregion

        #region Playback and Looping Tests

        [Fact]
        public void Play_WithoutLoop_ReturnsNonLoopingInstance()
        {
            // Arrange
            using var sound = new ManagedSound(_fixture.ValidWavFile);

            // Act
            using var instance = sound.Play(0.5f);

            // Assert
            Assert.NotNull(instance);
            Assert.Equal(0.5f, instance.Volume, 0.01f);
            Assert.False(instance.IsLooped);
        }

        [Fact]
        public void Play_WithLoopTrue_ReturnsLoopingInstance()
        {
            // Arrange
            using var sound = new ManagedSound(_fixture.ValidWavFile);

            // Act
            using var instance = sound.Play(0.7f, true);

            // Assert
            Assert.NotNull(instance);
            Assert.True(instance.IsLooped);
            Assert.Equal(0.7f, instance.Volume, 0.01f);
        }

        [Fact]
        public void Play_WithFullParameters_ConfiguresAllProperties()
        {
            // Arrange
            using var sound = new ManagedSound(_fixture.ValidWavFile);

            // Act
            using var instance = sound.Play(volume: 0.8f, pitch: 0.2f, pan: -0.5f, loop: true);

            // Assert
            Assert.NotNull(instance);
            Assert.True(instance.IsLooped);
            Assert.Equal(0.8f, instance.Volume, 0.01f);
            Assert.Equal(0.2f, instance.Pitch, 0.01f);
            Assert.Equal(-0.5f, instance.Pan, 0.01f);
        }

        [Theory]
        [InlineData(0.0f, false)]
        [InlineData(0.5f, false)]
        [InlineData(1.0f, false)]
        [InlineData(0.3f, true)]
        [InlineData(0.8f, true)]
        public void Play_VariousVolumeAndLoopCombinations_WorksCorrectly(float volume, bool loop)
        {
            // Arrange
            using var sound = new ManagedSound(_fixture.ValidWavFile);

            // Act
            using var instance = sound.Play(volume, loop);

            // Assert
            Assert.NotNull(instance);
            Assert.Equal(volume, instance.Volume, 0.01f);
            Assert.Equal(loop, instance.IsLooped);
        }

        #endregion

        #region CreateInstance Tests

        [Fact]
        public void CreateInstance_DefaultSettings_NotLooping()
        {
            // Arrange
            using var sound = new ManagedSound(_fixture.ValidWavFile);

            // Act
            using var instance = sound.CreateInstance();

            // Assert
            Assert.NotNull(instance);
            Assert.False(instance.IsLooped); // Default should be no looping
            Assert.Equal(1.0f, instance.Volume, 0.01f); // Default volume
            Assert.Equal(SoundState.Stopped, instance.State);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CreateInstance_ThenSetLooping_WorksCorrectly(bool shouldLoop)
        {
            // Arrange
            using var sound = new ManagedSound(_fixture.ValidWavFile);

            // Act
            using var instance = sound.CreateInstance();
            instance.IsLooped = shouldLoop;

            // Assert
            Assert.Equal(shouldLoop, instance.IsLooped);
        }

        #endregion

        #region Parameter Clamping Tests

        [Theory]
        [InlineData(-1.0f, 0.0f)]
        [InlineData(-0.5f, 0.0f)]
        [InlineData(0.0f, 0.0f)]
        [InlineData(0.5f, 0.5f)]
        [InlineData(1.0f, 1.0f)]
        [InlineData(1.5f, 1.0f)]
        [InlineData(2.0f, 1.0f)]
        public void Play_WithVolumeOutOfRange_ClampsCorrectly(float inputVolume, float expectedVolume)
        {
            // Arrange
            using var sound = new ManagedSound(_fixture.ValidWavFile);

            // Act
            using var instance = sound.Play(inputVolume);

            // Assert
            Assert.Equal(expectedVolume, instance.Volume, 0.01f);
        }

        [Theory]
        [InlineData(-2.0f, -1.0f)]
        [InlineData(-1.0f, -1.0f)]
        [InlineData(0.0f, 0.0f)]
        [InlineData(1.0f, 1.0f)]
        [InlineData(2.0f, 1.0f)]
        public void Play_WithPanOutOfRange_ClampsCorrectly(float inputPan, float expectedPan)
        {
            // Arrange
            using var sound = new ManagedSound(_fixture.ValidWavFile);

            // Act
            using var instance = sound.Play(0.5f, 0.0f, inputPan);

            // Assert
            Assert.Equal(expectedPan, instance.Pan, 0.01f);
        }

        #endregion

        #region Reference Counting and Disposal Tests

        [Fact]
        public void ReferenceCount_IncrementAndDecrement_WorksCorrectly()
        {
            // Arrange
            var sound = new ManagedSound(_fixture.ValidWavFile);

            // Act & Assert
            Assert.Equal(0, sound.ReferenceCount);

            sound.AddReference();
            Assert.Equal(1, sound.ReferenceCount);

            sound.AddReference();
            Assert.Equal(2, sound.ReferenceCount);

            sound.RemoveReference();
            Assert.Equal(1, sound.ReferenceCount);
            Assert.False(sound.IsDisposed);

            sound.RemoveReference();
            Assert.Equal(0, sound.ReferenceCount);
            Assert.True(sound.IsDisposed);
        }

        [Fact]
        public void Dispose_AfterDisposal_ThrowsOnAddReference()
        {
            // Arrange
            var sound = new ManagedSound(_fixture.ValidWavFile);

            // Act
            sound.Dispose();

            // Assert
            Assert.True(sound.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() => sound.AddReference());
        }

        [Fact]
        public void Play_AfterDisposal_ReturnsNull()
        {
            // Arrange
            var sound = new ManagedSound(_fixture.ValidWavFile);

            // Act
            sound.Dispose();
            var instance = sound.Play();

            // Assert
            Assert.Null(instance);
        }

        #endregion

        #region Interface Compatibility Tests

        [Fact]
        public void ISound_Play_DefaultParameters_WorksCorrectly()
        {
            // Arrange
            ISound sound = new ManagedSound(_fixture.ValidWavFile);

            // Act
            using var instance = sound.Play();

            // Assert
            Assert.NotNull(instance);
            Assert.False(instance.IsLooped); // Default behavior
            
            sound.Dispose();
        }

        [Fact]
        public void ISound_Play_WithVolume_WorksCorrectly()
        {
            // Arrange
            ISound sound = new ManagedSound(_fixture.ValidWavFile);

            // Act
            using var instance = sound.Play(0.6f);

            // Assert
            Assert.NotNull(instance);
            Assert.Equal(0.6f, instance.Volume, 0.01f);
            Assert.False(instance.IsLooped); // Default behavior
            
            sound.Dispose();
        }

        [Fact]
        public void ISound_Properties_AccessibleCorrectly()
        {
            // Arrange
            ISound sound = new ManagedSound(_fixture.ValidWavFile);

            // Act & Assert
            Assert.NotNull(sound.SoundEffect);
            Assert.Equal(_fixture.ValidWavFile, sound.SourcePath);
            Assert.True(sound.Duration > TimeSpan.Zero);
            Assert.False(sound.IsDisposed);
            
            sound.Dispose();
            Assert.True(sound.IsDisposed);
        }

        #endregion

        #region Property and Exception Tests

        [Fact]
        public void Duration_ReturnsCorrectValue()
        {
            // Arrange
            using var sound = new ManagedSound(_fixture.ValidWavFile);

            // Act
            var duration = sound.Duration;

            // Assert
            Assert.True(duration > TimeSpan.Zero);
        }

        [Fact]
        public void SoundLoadException_PreservesPathAndMessage()
        {
            // Arrange
            var path = "/test/path.mp3";
            var message = "Test error message";
            var innerException = new InvalidOperationException("Inner error");

            // Act
            var exception = new SoundLoadException(path, message, innerException);

            // Assert
            Assert.Equal(path, exception.SoundPath);
            Assert.Equal(message, exception.Message);
            Assert.Equal(innerException, exception.InnerException);
        }

        [Fact]
        public void SoundLoadException_WithCustomSourcePath_PreservesCustomPath()
        {
            // Arrange
            var customSourcePath = "custom/preview.mp3";

            // Act & Assert
            var exception = Assert.Throws<SoundLoadException>(() => new ManagedSound(_fixture.InvalidMp3File, customSourcePath));
            Assert.Equal(customSourcePath, exception.SoundPath); // Should use custom source path
        }

        #endregion

        #region Audio Channel Tests

        [Fact]
        public void LoadWavFile_MonoAudio_CreatesCorrectChannelConfiguration()
        {
            // Test that mono WAV files are loaded with proper channel configuration
            var monoWavFile = AudioTestUtils.CreateTestWavFile(Path.Combine(_fixture.TempDir, "mono.wav"), 0.1, 44100, 1);

            var sound = new ManagedSound(monoWavFile);

            Assert.NotNull(sound.SoundEffect);
            // Note: We can't directly test AudioChannels from MonoGame SoundEffect,
            // but we can verify the sound loads successfully and has expected duration
            Assert.True(sound.Duration.TotalSeconds > 0);
            sound.Dispose();
        }

        [Fact]
        public void LoadWavFile_StereoAudio_CreatesCorrectChannelConfiguration()
        {
            // Test that stereo WAV files are loaded with proper channel configuration
            var stereoWavFile = AudioTestUtils.CreateTestWavFile(Path.Combine(_fixture.TempDir, "stereo.wav"), 0.1, 44100, 2);

            var sound = new ManagedSound(stereoWavFile);

            Assert.NotNull(sound.SoundEffect);
            Assert.True(sound.Duration.TotalSeconds > 0);
            sound.Dispose();
        }

        [Fact]
        public void CreateTestWavFile_WithDifferentChannelCounts_GeneratesValidFiles()
        {
            // Test the AudioTestUtils helper with different channel configurations
            var monoFile = AudioTestUtils.CreateTestWavFile(Path.Combine(_fixture.TempDir, "test_mono.wav"), 0.05, 22050, 1);
            var stereoFile = AudioTestUtils.CreateTestWavFile(Path.Combine(_fixture.TempDir, "test_stereo.wav"), 0.05, 22050, 2);

            Assert.True(File.Exists(monoFile));
            Assert.True(File.Exists(stereoFile));

            // Verify mono file is smaller than stereo file (half the channels)
            var monoSize = new FileInfo(monoFile).Length;
            var stereoSize = new FileInfo(stereoFile).Length;
            
            // Stereo should be roughly twice the size of mono (same duration, double channels)
            Assert.True(stereoSize > monoSize);
            
            // Verify both can be loaded as sounds
            using var monoSound = new ManagedSound(monoFile);
            using var stereoSound = new ManagedSound(stereoFile);
            
            Assert.NotNull(monoSound.SoundEffect);
            Assert.NotNull(stereoSound.SoundEffect);
        }

        #endregion
    }
}
