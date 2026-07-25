using System;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Mac-safe unit tests for AudioLoader's borrowed-sound lifecycle
    /// (BindBorrowedBackground, UnloadCurrentSound ownership, disposal).
    /// These tests use mock ISound instances and do not require a graphics
    /// device, so they run on both Windows and Mac test projects.
    /// </summary>
    [Trait("Category", "Unit")]
    public sealed class AudioLoaderBorrowedSoundTests
    {
        private static Mock<IResourceManager> CreateResourceManager()
        {
            return new Mock<IResourceManager>();
        }

        private static Mock<ISound> CreateMockSound()
        {
            var sound = new Mock<ISound>();
            sound.SetupGet(s => s.IsDisposed).Returns(false);
            return sound;
        }

        #region Constructor

        [Fact]
        public void Constructor_WithNullResourceManager_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(
                () => new AudioLoader(null!));
            Assert.Equal("resourceManager", ex.ParamName);
        }

        #endregion

        #region BindBorrowedBackground

        [Fact]
        public void BindBorrowedBackground_WithValidSound_SetsLoadedState()
        {
            using var loader = new AudioLoader(CreateResourceManager().Object);
            var sound = CreateMockSound();

            loader.BindBorrowedBackground(sound.Object, "/chart/bgm.wav");

            Assert.True(loader.IsLoaded);
            Assert.Equal("/chart/bgm.wav", loader.LoadedAudioPath);
        }

        [Fact]
        public void BindBorrowedBackground_WithNullSound_ClearsLoadedState()
        {
            using var loader = new AudioLoader(CreateResourceManager().Object);
            // First bind a real sound
            loader.BindBorrowedBackground(CreateMockSound().Object, "/chart/bgm.wav");
            Assert.True(loader.IsLoaded);

            // Then bind null
            loader.BindBorrowedBackground(null, null);

            Assert.False(loader.IsLoaded);
            Assert.Equal("", loader.LoadedAudioPath);
        }

        [Fact]
        public void BindBorrowedBackground_WithNullSourcePath_UsesEmptyString()
        {
            using var loader = new AudioLoader(CreateResourceManager().Object);
            var sound = CreateMockSound();

            loader.BindBorrowedBackground(sound.Object, null);

            Assert.True(loader.IsLoaded);
            Assert.Equal("", loader.LoadedAudioPath);
        }

        [Fact]
        public void BindBorrowedBackground_ReplacesPreviousBorrowedSoundWithoutRemoveReference()
        {
            using var loader = new AudioLoader(CreateResourceManager().Object);
            var firstSound = CreateMockSound();
            loader.BindBorrowedBackground(firstSound.Object, "/chart/first.wav");

            var secondSound = CreateMockSound();
            loader.BindBorrowedBackground(secondSound.Object, "/chart/second.wav");

            Assert.True(loader.IsLoaded);
            Assert.Equal("/chart/second.wav", loader.LoadedAudioPath);
            // Borrowed sounds must NOT have RemoveReference called — the owner
            // (PreparedGameplayAudioSet) manages their lifetime.
            firstSound.Verify(s => s.RemoveReference(), Times.Never);
        }

        [Fact]
        public void BindBorrowedBackground_WhenDisposed_ThrowsObjectDisposedException()
        {
            var loader = new AudioLoader(CreateResourceManager().Object);
            loader.Dispose();

            Assert.Throws<ObjectDisposedException>(
                () => loader.BindBorrowedBackground(CreateMockSound().Object, "/bgm.wav"));
        }

        #endregion

        #region UnloadCurrentSound with Borrowed Sound

        [Fact]
        public void UnloadCurrentSound_WithBorrowedSound_DoesNotCallRemoveReference()
        {
            using var loader = new AudioLoader(CreateResourceManager().Object);
            var sound = CreateMockSound();
            loader.BindBorrowedBackground(sound.Object, "/chart/bgm.wav");

            loader.UnloadCurrentSound();

            Assert.False(loader.IsLoaded);
            Assert.Equal("", loader.LoadedAudioPath);
            sound.Verify(s => s.RemoveReference(), Times.Never);
        }

        [Fact]
        public void UnloadCurrentSound_WhenNotLoaded_DoesNotThrow()
        {
            using var loader = new AudioLoader(CreateResourceManager().Object);

            loader.UnloadCurrentSound();

            Assert.False(loader.IsLoaded);
        }

        #endregion

        #region GetAudioInfo with Borrowed Sound

        [Fact]
        public void GetAudioInfo_WithBorrowedSound_ReturnsInfoWithPath()
        {
            using var loader = new AudioLoader(CreateResourceManager().Object);
            loader.BindBorrowedBackground(
                CreateMockSound().Object,
                "/nonexistent/bgm.wav");

            var info = loader.GetAudioInfo();

            Assert.NotNull(info);
            Assert.Equal("/nonexistent/bgm.wav", info.FilePath);
            Assert.Equal("bgm.wav", info.FileName);
            Assert.True(info.IsLoaded);
            // File doesn't exist on disk → FileSize is 0
            Assert.Equal(0, info.FileSize);
        }

        [Fact]
        public void GetAudioInfo_WhenNotLoaded_ReturnsNull()
        {
            using var loader = new AudioLoader(CreateResourceManager().Object);

            Assert.Null(loader.GetAudioInfo());
        }

        #endregion

        #region CreateSongTimer with Not-Loaded

        [Fact]
        public void CreateSongTimer_WhenNotLoaded_ReturnsNull()
        {
            using var loader = new AudioLoader(CreateResourceManager().Object);

            Assert.Null(loader.CreateSongTimer());
            Assert.Null(loader.CreateSongTimer(125, -2.0f));
        }

        [Fact]
        public void CreateSongTimer_WhenNotLoaded_InvokesLogger()
        {
            using var loader = new AudioLoader(CreateResourceManager().Object);
            var logged = false;

            loader.CreateSongTimer(message => logged = true);

            Assert.True(logged);
        }

        #endregion

        #region Disposal

        [Fact]
        public void Dispose_WithBorrowedSound_DoesNotCallRemoveReference()
        {
            var loader = new AudioLoader(CreateResourceManager().Object);
            var sound = CreateMockSound();
            loader.BindBorrowedBackground(sound.Object, "/chart/bgm.wav");

            loader.Dispose();

            Assert.False(loader.IsLoaded);
            sound.Verify(s => s.RemoveReference(), Times.Never);
        }

        [Fact]
        public void Dispose_CalledMultipleTimes_DoesNotThrow()
        {
            var loader = new AudioLoader(CreateResourceManager().Object);

            loader.Dispose();
            loader.Dispose();
            loader.Dispose();
        }

        [Fact]
        public void IsLoaded_AfterDisposal_ReturnsFalse()
        {
            var loader = new AudioLoader(CreateResourceManager().Object);
            loader.BindBorrowedBackground(CreateMockSound().Object, "/bgm.wav");

            loader.Dispose();

            Assert.False(loader.IsLoaded);
        }

        #endregion
    }
}
