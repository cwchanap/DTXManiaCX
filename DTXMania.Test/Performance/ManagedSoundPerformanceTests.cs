using DTXMania.Game.Lib.Resources;
using DTXMania.Test.Utilities;
using System;
using System.Diagnostics;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace DTXMania.Test.Performance
{
    /// <summary>
    /// Performance tests for ManagedSound MP3 functionality
    /// These tests ensure the MP3 loading and looping features perform adequately
    /// </summary>
    public class ManagedSoundPerformanceTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _tempDir;

        public ManagedSoundPerformanceTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDir = Path.Combine(Path.GetTempPath(), "DTXMania_Perf_Test_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                try
                {
                    Directory.Delete(_tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors in tests
                }
            }
        }

        [Fact]
        public void ManagedSound_Creation_IsReasonablyFast()
        {
            // Arrange
            var wavPath = AudioTestUtils.CreateTestWavFile(_tempDir, "test.wav");
            var stopwatch = Stopwatch.StartNew();

            // Act
            using var sound = new ManagedSound(wavPath);
            stopwatch.Stop();

            // Assert
            _output.WriteLine($"ManagedSound creation took: {stopwatch.ElapsedMilliseconds}ms");
            Assert.True(stopwatch.ElapsedMilliseconds < 2000, $"Sound creation took too long: {stopwatch.ElapsedMilliseconds}ms"); // Increased threshold for slower test environments
            Assert.NotNull(sound.SoundEffect);
        }

        [Fact]
        public void ManagedSound_MultipleInstances_CreateEfficiently()
        {
            // Arrange
            var wavPath = AudioTestUtils.CreateTestWavFile(_tempDir, "test.wav");
            using var sound = new ManagedSound(wavPath);
            var stopwatch = Stopwatch.StartNew();

            // Act - Create multiple instances
            using var instance1 = sound.CreateInstance();
            using var instance2 = sound.CreateInstance();
            using var instance3 = sound.CreateInstance();
            using var instance4 = sound.CreateInstance();
            using var instance5 = sound.CreateInstance();
            stopwatch.Stop();

            // Assert
            _output.WriteLine($"Creating 5 instances took: {stopwatch.ElapsedMilliseconds}ms");
            Assert.True(stopwatch.ElapsedMilliseconds < 50, $"Instance creation took too long: {stopwatch.ElapsedMilliseconds}ms");
            Assert.NotNull(instance1);
            Assert.NotNull(instance5);
        }

        [Fact]
        public void ManagedSound_PlayWithLoop_ConfiguresQuickly()
        {
            // Arrange
            var wavPath = AudioTestUtils.CreateTestWavFile(_tempDir, "test.wav");
            using var sound = new ManagedSound(wavPath);
            var stopwatch = Stopwatch.StartNew();

            // Act
            using var instance = sound.Play(0.8f, true);
            stopwatch.Stop();

            // Assert
            _output.WriteLine($"Play with loop configuration took: {stopwatch.ElapsedMilliseconds}ms");
            Assert.True(stopwatch.ElapsedMilliseconds < 20, $"Play configuration took too long: {stopwatch.ElapsedMilliseconds}ms");
            Assert.NotNull(instance);
            Assert.True(instance.IsLooped);
        }

        [Fact]
        public void ManagedSound_ReferenceCountingOperations_AreEfficient()
        {
            // Arrange
            var wavPath = AudioTestUtils.CreateTestWavFile(_tempDir, "test.wav");
            var sound = new ManagedSound(wavPath);
            var stopwatch = Stopwatch.StartNew();

            // Act - Perform many reference counting operations
            for (int i = 0; i < 1000; i++)
            {
                sound.AddReference();
            }
            
            for (int i = 0; i < 1000; i++)
            {
                sound.RemoveReference();
            }
            stopwatch.Stop();

            // Assert
            _output.WriteLine($"1000 ref count operations took: {stopwatch.ElapsedMilliseconds}ms");
            Assert.True(stopwatch.ElapsedMilliseconds < 100, $"Reference counting took too long: {stopwatch.ElapsedMilliseconds}ms");
            Assert.Equal(0, sound.ReferenceCount);
            Assert.True(sound.IsDisposed); // Should be disposed after ref count reaches 0
        }

        [Fact]
        public void ManagedSound_MemoryUsage_IsReasonable()
        {
            // Arrange
            var wavPath = AudioTestUtils.CreateTestWavFile(_tempDir, "test.wav");
            var initialMemory = GC.GetTotalMemory(true);

            // Act - Create multiple sounds
            var sounds = new ManagedSound[10];
            for (int i = 0; i < sounds.Length; i++)
            {
                sounds[i] = new ManagedSound(wavPath);
            }

            var memoryAfterCreation = GC.GetTotalMemory(false);
            var memoryUsed = memoryAfterCreation - initialMemory;

            // Cleanup
            for (int i = 0; i < sounds.Length; i++)
            {
                sounds[i].Dispose();
            }

            var memoryAfterDisposal = GC.GetTotalMemory(true);

            // Assert
            _output.WriteLine($"Memory used for 10 sounds: {memoryUsed / 1024}KB");
            _output.WriteLine($"Memory after disposal: {(memoryAfterDisposal - initialMemory) / 1024}KB");
            
            // Memory usage should be reasonable (less than 1MB for 10 small test sounds)
            Assert.True(memoryUsed < 1024 * 1024, $"Memory usage too high: {memoryUsed / 1024}KB");
        }

        [Fact]
        public void ManagedSound_ParallelAccess_IsThreadSafe()
        {
            // Arrange
            var wavPath = AudioTestUtils.CreateTestWavFile(_tempDir, "test.wav");
            using var sound = new ManagedSound(wavPath);
            var stopwatch = Stopwatch.StartNew();

            // Act - Parallel access to instance creation (safer than reference counting)
            System.Threading.Tasks.Parallel.For(0, 100, i =>
            {
                using var instance = sound.CreateInstance();
                Assert.NotNull(instance);
                // Test some basic operations
                instance.Volume = 0.5f;
                Assert.Equal(0.5f, instance.Volume, 0.01f);
            });

            stopwatch.Stop();

            // Assert
            _output.WriteLine($"Parallel access (100 operations) took: {stopwatch.ElapsedMilliseconds}ms");
            Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"Parallel access took too long: {stopwatch.ElapsedMilliseconds}ms"); // Increased threshold for CI
            Assert.Equal(0, sound.ReferenceCount);
        }

        [Fact]
        public void SoundLoadException_Creation_IsEfficient()
        {
            // Arrange
            var testPath = "/test/path.mp3";
            var testMessage = "Test error message";
            var innerException = new InvalidOperationException("Inner error");
            var stopwatch = Stopwatch.StartNew();

            // Act - Create many exceptions (simulating error scenarios)
            for (int i = 0; i < 1000; i++)
            {
                var exception = new SoundLoadException(testPath, testMessage, innerException);
                Assert.Equal(testPath, exception.SoundPath);
            }
            stopwatch.Stop();

            // Assert
            _output.WriteLine($"Creating 1000 exceptions took: {stopwatch.ElapsedMilliseconds}ms");
            Assert.True(stopwatch.ElapsedMilliseconds < 100, $"Exception creation took too long: {stopwatch.ElapsedMilliseconds}ms");
        }

        /// <summary>
        /// Test that verifies disposal cleanup is efficient
        /// </summary>
        [Fact]
        public void ManagedSound_DisposalCleanup_IsEfficient()
        {
            // Arrange
            var wavPath = AudioTestUtils.CreateTestWavFile(_tempDir, "test.wav");
            var sounds = new ManagedSound[50];
            
            // Create sounds
            for (int i = 0; i < sounds.Length; i++)
            {
                sounds[i] = new ManagedSound(wavPath);
            }

            var stopwatch = Stopwatch.StartNew();

            // Act - Dispose all sounds
            for (int i = 0; i < sounds.Length; i++)
            {
                sounds[i].Dispose();
            }
            stopwatch.Stop();

            // Assert
            _output.WriteLine($"Disposing 50 sounds took: {stopwatch.ElapsedMilliseconds}ms");
            Assert.True(stopwatch.ElapsedMilliseconds < 100, $"Disposal took too long: {stopwatch.ElapsedMilliseconds}ms");
            
            // Verify all are disposed
            foreach (var sound in sounds)
            {
                Assert.True(sound.IsDisposed);
            }
        }

        [Fact]
        public void ManagedSound_ReferenceCountingThreadSafety_WorksCorrectly()
        {
            // Arrange
            var wavPath = AudioTestUtils.CreateTestWavFile(_tempDir, "test.wav");
            var sound = new ManagedSound(wavPath);
            const int iterationsPerThread = 50;
            const int threadCount = 4;
            var totalIterations = iterationsPerThread * threadCount;
            
            // Pre-add references to keep the object alive during parallel operations
            for (int i = 0; i < totalIterations; i++)
            {
                sound.AddReference();
            }

            var stopwatch = Stopwatch.StartNew();

            // Act - Parallel reference removal (object stays alive throughout)
            System.Threading.Tasks.Parallel.For(0, threadCount, threadIndex =>
            {
                for (int i = 0; i < iterationsPerThread; i++)
                {
                    // Create instance (should work since object is kept alive)
                    using var instance = sound.CreateInstance();
                    Assert.NotNull(instance);
                    
                    // Remove one reference
                    sound.RemoveReference();
                }
            });

            stopwatch.Stop();

            // Assert
            _output.WriteLine($"Reference counting thread safety test took: {stopwatch.ElapsedMilliseconds}ms");
            Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"Reference counting took too long: {stopwatch.ElapsedMilliseconds}ms");
            Assert.Equal(0, sound.ReferenceCount);
            Assert.True(sound.IsDisposed); // Should be disposed now that reference count is 0
        }
    }
}
