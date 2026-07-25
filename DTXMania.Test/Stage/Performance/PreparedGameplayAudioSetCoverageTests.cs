#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using Moq;
using Xunit;
namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Unit")]
    public sealed class PreparedGameplayAudioSetCoverageTests
    {
        [Fact]
        public async Task PrepareAsync_InvalidArguments_ShouldThrow()
        {
            var defaultModifiers = new PlaybackModifiers(100, 0);
            var nonDefaultModifiers = new PlaybackModifiers(50, 0);
            var emptyChips = new Dictionary<string, string>();
            Func<string, ISound> loader = _ => Mock.Of<ISound>();
            Func<PreparedAudioArtifact, string, ISound> factory =
                (_, _) => Mock.Of<ISound>();

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                PreparedGameplayAudioSet.PrepareAsync(
                    null,
                    null!,
                    emptyChips,
                    defaultModifiers,
                    null,
                    null,
                    null,
                    CancellationToken.None,
                    loader,
                    factory));
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                PreparedGameplayAudioSet.PrepareAsync(
                    null,
                    Array.Empty<string>(),
                    null!,
                    defaultModifiers,
                    null,
                    null,
                    null,
                    CancellationToken.None,
                    loader,
                    factory));
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                PreparedGameplayAudioSet.PrepareAsync(
                    null,
                    Array.Empty<string>(),
                    emptyChips,
                    defaultModifiers,
                    null,
                    null,
                    null,
                    CancellationToken.None,
                    null!,
                    factory));
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                PreparedGameplayAudioSet.PrepareAsync(
                    null,
                    Array.Empty<string>(),
                    emptyChips,
                    defaultModifiers,
                    null,
                    null,
                    null,
                    CancellationToken.None,
                    loader,
                    null!));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                PreparedGameplayAudioSet.PrepareAsync(
                    null,
                    Array.Empty<string>(),
                    emptyChips,
                    defaultModifiers,
                    null,
                    null,
                    null,
                    CancellationToken.None,
                    loader,
                    factory,
                    decodedPcmBudgetBytes: 0));
            var cacheDirectory = CreateTempDirectory();
            try
            {
                await Assert.ThrowsAsync<ArgumentNullException>(() =>
                    PreparedGameplayAudioSet.PrepareAsync(
                        null,
                        Array.Empty<string>(),
                        emptyChips,
                        nonDefaultModifiers,
                        null,
                        new PlaybackAudioVariantCache(
                            Path.Combine(cacheDirectory, "cache")),
                        null,
                        CancellationToken.None,
                        loader,
                        factory));
                await Assert.ThrowsAsync<ArgumentNullException>(() =>
                    PreparedGameplayAudioSet.PrepareAsync(
                        null,
                        Array.Empty<string>(),
                        emptyChips,
                        nonDefaultModifiers,
                        Mock.Of<IAudioVariantProcessor>(),
                        null,
                        null,
                        CancellationToken.None,
                        loader,
                        factory));
            }
            finally
            {
                Directory.Delete(cacheDirectory, recursive: true);
            }
        }

        [Fact]
        public async Task PrepareAsync_MissingAndBlankPaths_ShouldBeIgnored()
        {
            var directory = CreateTempDirectory();
            try
            {
                var loaderCalls = 0;

                using var prepared = await PreparedGameplayAudioSet.PrepareAsync(
                    Path.Combine(directory, "missing-main.wav"),
                    new[] { " ", Path.Combine(directory, "missing-scheduled.wav") },
                    new Dictionary<string, string>
                    {
                        [" "] = Path.Combine(directory, "ignored-key.wav"),
                        ["01"] = Path.Combine(directory, "missing-chip.wav"),
                    },
                    new PlaybackModifiers(100, 0),
                    null,
                    null,
                    null,
                    CancellationToken.None,
                    _ =>
                    {
                        loaderCalls++;
                        return Mock.Of<ISound>();
                    },
                    (_, _) => Mock.Of<ISound>());

                Assert.Equal(0, loaderCalls);
                Assert.Null(prepared.MainBackground);
                Assert.Empty(prepared.ScheduledBgmBySourcePath);
                Assert.Empty(prepared.ChipSoundsByWavId);
                Assert.Equal(0, prepared.DecodedPcmBytes);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public async Task PrepareAsync_DefaultProfile_ShouldReportScheduledAndChipRoles()
        {
            var directory = CreateTempDirectory();
            try
            {
                var scheduled = CreateSource(directory, "scheduled.wav");
                var chip = CreateSource(directory, "chip.wav");
                var reports = new List<AudioPreparationProgress>();
                var sounds = new List<Mock<ISound>>();

                ISound Load(string _)
                {
                    var sound = new Mock<ISound>();
                    sounds.Add(sound);
                    return sound.Object;
                }

                using var prepared = await PreparedGameplayAudioSet.PrepareAsync(
                    null,
                    new[] { scheduled },
                    new Dictionary<string, string> { ["01"] = chip },
                    new PlaybackModifiers(100, 0),
                    null,
                    null,
                    new InlineProgress<AudioPreparationProgress>(reports.Add),
                    CancellationToken.None,
                    Load,
                    (_, _) => Mock.Of<ISound>());

                var roles = reports.Select(report => report.CurrentRole).ToArray();
                Assert.Contains("scheduled BGM", roles);
                Assert.Contains("chip", roles);
                Assert.Equal(4, reports.Count);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public async Task PrepareAsync_DefaultLoaderCancellation_ShouldPropagate()
        {
            var directory = CreateTempDirectory();
            try
            {
                var source = CreateSource(directory, "cancel.wav");

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    PreparedGameplayAudioSet.PrepareAsync(
                        source,
                        Array.Empty<string>(),
                        new Dictionary<string, string>(),
                        new PlaybackModifiers(100, 0),
                        null,
                        null,
                        null,
                        CancellationToken.None,
                        _ => throw new OperationCanceledException(),
                        (_, _) => Mock.Of<ISound>()));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public async Task PrepareAsync_PreparedFactoryFailure_ShouldDisposeCreatedSounds()
        {
            var directory = CreateTempDirectory();
            try
            {
                var first = CreateSource(directory, "first.wav");
                var second = CreateSource(directory, "second.wav");
                var cache = new PlaybackAudioVariantCache(Path.Combine(directory, "cache"));
                var processor = new Mock<IAudioVariantProcessor>();
                processor
                    .Setup(value => value.PrepareAsync(
                        It.IsAny<string>(),
                        It.IsAny<PlaybackModifiers>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new PreparedAudioArtifact(44100, 1, new byte[] { 1, 0 }));
                var created = new Mock<ISound>();
                var factoryCalls = 0;

                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    PreparedGameplayAudioSet.PrepareAsync(
                        first,
                        new[] { second },
                        new Dictionary<string, string>(),
                        new PlaybackModifiers(50, 0),
                        processor.Object,
                        cache,
                        null,
                        CancellationToken.None,
                        _ => Mock.Of<ISound>(),
                        (_, _) =>
                        {
                            factoryCalls++;
                            if (factoryCalls == 1)
                                return created.Object;
                            throw new InvalidOperationException("sound construction failed");
                        }));

                created.Verify(sound => sound.Dispose(), Times.Once);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public async Task Dispose_WhenOneSoundThrows_ShouldContinueAndRemainIdempotent()
        {
            var directory = CreateTempDirectory();
            try
            {
                var first = CreateSource(directory, "first.wav");
                var second = CreateSource(directory, "second.wav");
                var throwing = new Mock<ISound>();
                throwing
                    .Setup(sound => sound.Dispose())
                    .Throws(new InvalidOperationException("dispose failed"));
                var healthy = new Mock<ISound>();
                var loadCalls = 0;

                var prepared = await PreparedGameplayAudioSet.PrepareAsync(
                    first,
                    new[] { second },
                    new Dictionary<string, string>(),
                    new PlaybackModifiers(100, 0),
                    null,
                    null,
                    null,
                    CancellationToken.None,
                    _ => ++loadCalls == 1 ? throwing.Object : healthy.Object,
                    (_, _) => Mock.Of<ISound>());

                var exception = Record.Exception(prepared.Dispose);
                prepared.Dispose();

                Assert.Null(exception);
                throwing.Verify(sound => sound.Dispose(), Times.Once);
                healthy.Verify(sound => sound.Dispose(), Times.Once);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "DTXMania_PreparedAudio_Coverage_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static string CreateSource(string directory, string name)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllBytes(path, new byte[] { 1 });
            return Path.GetFullPath(path);
        }

        private sealed class InlineProgress<T> : IProgress<T>
        {
            private readonly Action<T> _callback;

            public InlineProgress(Action<T> callback)
            {
                _callback = callback;
            }

            public void Report(T value)
            {
                _callback(value);
            }
        }
    }
}
