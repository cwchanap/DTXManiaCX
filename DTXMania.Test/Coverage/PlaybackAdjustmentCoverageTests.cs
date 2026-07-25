using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;
using static DTXMania.Test.TestData.ReflectionHelpers;

namespace DTXMania.Test.Coverage
{
    [Trait("Category", "Unit")]
    public sealed class PreparedAudioArtifactCoverageTests : IDisposable
    {
        private readonly string _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "dtxmania-prepared-artifact-coverage-" + Guid.NewGuid().ToString("N"));

        public PreparedAudioArtifactCoverageTests()
        {
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task WriteAsync_WithBlankPath_ThrowsArgumentException(string? path)
        {
            var artifact = CreateArtifact();

            await Assert.ThrowsAsync<ArgumentException>(
                () => artifact.WriteAsync(path!));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task ReadAsync_WithBlankPath_ThrowsArgumentException(string? path)
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => PreparedAudioArtifact.ReadAsync(path!));
        }

        [Fact]
        public async Task ReadAsync_WithTruncatedHeader_ThrowsInvalidDataException()
        {
            var path = Path.Combine(_tempDirectory, "truncated.dtxpcm");
            await File.WriteAllBytesAsync(
                path,
                new byte[PreparedAudioArtifact.HeaderLength - 1]);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => PreparedAudioArtifact.ReadAsync(path));

            Assert.Contains("header is truncated", exception.Message);
        }

        [Fact]
        public async Task WriteAsync_CreatesParentDirectoryAndOverwritesExistingArtifact()
        {
            var path = Path.Combine(_tempDirectory, "nested", "tone.dtxpcm");
            var first = new PreparedAudioArtifact(
                44100,
                2,
                new byte[] { 0x01, 0x02, 0x03, 0x04 });
            var second = new PreparedAudioArtifact(
                48000,
                1,
                new byte[] { 0x10, 0x20 });

            await first.WriteAsync(path);
            await second.WriteAsync(path);
            var roundTrip = await PreparedAudioArtifact.ReadAsync(path);

            Assert.Equal(48000, roundTrip.SampleRate);
            Assert.Equal(1, roundTrip.ChannelCount);
            Assert.Equal(new byte[] { 0x10, 0x20 }, roundTrip.PcmData.ToArray());
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp-*"));
        }

        [Fact]
        public void Constructor_DefensivelyCopiesPcmData()
        {
            var source = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var artifact = new PreparedAudioArtifact(44100, 2, source);

            source[0] = 0x7F;

            Assert.Equal(
                new byte[] { 0x01, 0x02, 0x03, 0x04 },
                artifact.PcmData.ToArray());
        }

        [Theory]
        [InlineData(PreparedAudioArtifact.SampleRateOffset, PreparedAudioArtifact.MinimumSampleRate - 1)]
        [InlineData(PreparedAudioArtifact.SampleRateOffset, PreparedAudioArtifact.MaximumSampleRate + 1)]
        [InlineData(PreparedAudioArtifact.ChannelCountOffset, 0)]
        [InlineData(PreparedAudioArtifact.ChannelCountOffset, 3)]
        public async Task ReadAsync_WithInvalidIntegerMetadata_ThrowsInvalidDataException(
            int offset,
            int value)
        {
            var path = await WriteValidArtifactAsync();
            var bytes = await File.ReadAllBytesAsync(path);
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(offset, sizeof(int)),
                value);
            await File.WriteAllBytesAsync(path, bytes);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => PreparedAudioArtifact.ReadAsync(path));
        }

        [Theory]
        [InlineData(-2L)]
        [InlineData(0L)]
        [InlineData(1L)]
        [InlineData(2L)]
        [InlineData(PreparedAudioArtifact.MaxPcmByteLength + 2L)]
        public async Task ReadAsync_WithInvalidDeclaredPcmLength_ThrowsInvalidDataException(
            long declaredLength)
        {
            var path = await WriteValidArtifactAsync();
            var bytes = await File.ReadAllBytesAsync(path);
            BinaryPrimitives.WriteInt64LittleEndian(
                bytes.AsSpan(PreparedAudioArtifact.PcmLengthOffset, sizeof(long)),
                declaredLength);
            await File.WriteAllBytesAsync(path, bytes);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => PreparedAudioArtifact.ReadAsync(path));
        }

        private PreparedAudioArtifact CreateArtifact() =>
            new(44100, 2, new byte[] { 0x01, 0x02, 0x03, 0x04 });

        private async Task<string> WriteValidArtifactAsync()
        {
            var path = Path.Combine(
                _tempDirectory,
                Guid.NewGuid().ToString("N") + PreparedAudioArtifact.FileExtension);
            await CreateArtifact().WriteAsync(path);
            return path;
        }
    }

    [Trait("Category", "Unit")]
    public sealed class ChipSoundCacheCoverageRegressionTests
    {
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
    }

    [Trait("Category", "Unit")]
    public sealed class ResultStageSaveFailureCoverageTests
    {
        [Fact]
        public void StartPerformanceSummarySave_WhenSaveThrowsSynchronously_ReportsFailure()
        {
            var stage = CreateStage();
            stage.SynchronousFailure = new InvalidOperationException("database unavailable");
            SetPrivateField(stage, "_performanceSummary", SavableSummary());

            InvokePrivateMethod(
                stage,
                "StartPerformanceSummarySave",
                new SongChart { Id = 42 });

            Assert.Equal(ResultSaveState.Failed, stage.ScoreSaveState);
            Assert.Equal("database unavailable", stage.ScoreSaveError);
            Assert.Equal(1, stage.SaveCalls);
        }

        [Fact]
        public void ObservePerformanceSummarySave_WhenFailureMessageIsNull_UsesDefaultMessage()
        {
            var stage = CreateStage();
            stage.Enqueue(ScoreSaveResult.Failed(null!));
            SetPrivateField(stage, "_performanceSummary", SavableSummary());

            InvokePrivateMethod(
                stage,
                "StartPerformanceSummarySave",
                new SongChart { Id = 42 });
            InvokePrivateMethod(stage, "ObservePerformanceSummarySave");

            Assert.Equal(ResultSaveState.Failed, stage.ScoreSaveState);
            Assert.Equal("The score could not be saved.", stage.ScoreSaveError);
        }

        private static TestResultStage CreateStage()
        {
            var game = new Mock<IStageGame>();
            return new TestResultStage(game.Object);
        }

        private static PerformanceSummary SavableSummary() =>
            new()
            {
                RunId = Guid.NewGuid(),
                PlaySpeedPercent = 100,
                PitchSemitones = 0,
                CompletionReason = CompletionReason.SongComplete,
                ClearFlag = true,
                Score = 900_000,
            };

        private sealed class TestResultStage : ResultStage
        {
            private readonly Queue<Task<ScoreSaveResult>> _results = new();

            public TestResultStage(IStageGame game)
                : base(game)
            {
            }

            public Exception? SynchronousFailure { get; set; }

            public int SaveCalls { get; private set; }

            public void Enqueue(ScoreSaveResult result)
            {
                _results.Enqueue(Task.FromResult(result));
            }

            protected override SpriteBatch CreateSpriteBatch(
                GraphicsDevice graphicsDevice)
            {
                return null!;
            }

            internal override Task<ScoreSaveResult> SavePerformanceSummaryAsync(
                int chartId,
                EInstrumentPart instrument,
                PerformanceSummary summary)
            {
                SaveCalls++;
                if (SynchronousFailure != null)
                    throw SynchronousFailure;

                return _results.Dequeue();
            }
        }
    }
}
