using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using DTXMania.Game.Lib.Resources;
using DTXMania.Test.Utilities;
using FFMpegCore;
using Xunit;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Test collection for tests that manipulate the <see cref="FfmpegRuntime"/>
    /// static configuration.  Tests in this collection must not run in parallel
    /// with other tests that call <see cref="FfmpegRuntime.EnsureConfigured"/>
    /// (e.g. <see cref="ManagedSoundTests"/>, <see cref="FfmpegBundledRuntimeTests"/>,
    /// <see cref="FfmpegAudioVariantProcessorTests"/>, <see cref="FfmpegRuntimeTests"/>).
    /// </summary>
    /// <remarks>
    /// <see cref="CollectionDefinitionAttribute.DisableParallelization"/> is set to
    /// <see langword="true"/> because members of this collection mutate process-wide
    /// state (<see cref="FfmpegRuntime.Configuration"/>, <c>GlobalFFOptions</c>, and
    /// the process <c>PATH</c> environment variable).  A plain collection only
    /// prevents its own members from running concurrently with each other; other
    /// collections would still execute in parallel by default and could observe an
    /// empty <c>PATH</c> or the stubbed FFmpeg global configuration, producing a
    /// scheduling race.  Disabling parallelization at the collection level is the
    /// appropriate scope for process-global mutation.
    /// </remarks>
    [CollectionDefinition("FfmpegRuntimeState", DisableParallelization = true)]
    public class FfmpegRuntimeStateCollection { }

    /// <summary>
    /// Unit tests for <see cref="ManagedSound"/> error-handling paths that depend
    /// on the bundled FFmpeg runtime availability.  These tests use reflection to
    /// temporarily replace the <c>FfmpegRuntime.Configuration</c> lazy singleton
    /// so the "runtime unavailable" and "binary missing" branches can be exercised
    /// deterministically regardless of the host platform.
    /// </summary>
    [Collection("FfmpegRuntimeState")]
    [Trait("Category", AudioTestUtils.AudioTestCategory)]
    public class ManagedSoundErrorPathTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly FieldInfo _configField;
        private readonly object? _originalConfiguration;
        private readonly Action<object?> _setConfigField;
        private string? _originalBinaryFolder;

        public ManagedSoundErrorPathTests()
        {
            _tempDir = Path.Combine(
                Path.GetTempPath(),
                "DTXMania_ErrorPath_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tempDir);

            _configField = typeof(FfmpegRuntime).GetField(
                "Configuration",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.NotNull(_configField);
            _originalConfiguration = _configField.GetValue(null);

            // Build a DynamicMethod that uses the stsfld IL instruction to write
            // the readonly (initonly) static field.  Regular FieldInfo.SetValue
            // throws FieldAccessException on initonly static fields in .NET 8.
            _setConfigField = CreateStaticFieldSetter(_configField);

            // Capture the current GlobalFFOptions BinaryFolder so we can restore it.
            try
            {
                _originalBinaryFolder = GlobalFFOptions.Current.BinaryFolder;
            }
            catch
            {
                _originalBinaryFolder = null;
            }
        }

        public void Dispose()
        {
            // Restore the original FfmpegRuntime.Configuration lazy so subsequent
            // tests in other classes see the real runtime state.
            _setConfigField(_originalConfiguration);

            // Restore GlobalFFOptions BinaryFolder.
            try
            {
                GlobalFFOptions.Configure(o => o.BinaryFolder = _originalBinaryFolder ?? string.Empty);
            }
            catch
            {
                // Best-effort restoration.
            }

            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, true); } catch { }
            }
        }

        /// <summary>
        /// When the bundled FFmpeg runtime is unavailable, loading an MP3 file
        /// should throw <see cref="SoundLoadException"/> with the
        /// "Bundled FFmpeg runtime is not available" diagnostic.
        ///
        /// This exercises:
        /// - <see cref="ManagedSound"/> constructor's
        ///   <c>catch (SoundLoadException) { throw; }</c> rethrow
        /// - <see cref="ManagedSound.LoadMp3File"/>'s runtime-unavailable throw
        /// - <see cref="ManagedSound.LoadMp3File"/>'s
        ///   <c>catch (SoundLoadException) { throw; }</c> rethrow
        /// </summary>
        [Fact]
        public void Constructor_WithMp3AndRuntimeUnavailable_ShouldThrowBundledRuntimeDiagnostic()
        {
            // Arrange — force the runtime to report unavailable.
            SetRuntimeAvailability(new FfmpegRuntimeAvailability(
                IsAvailable: false,
                DiagnosticReason: "Test: bundled runtime unavailable",
                BinaryFolder: null));

            var mp3File = Path.Combine(_tempDir, "tone.mp3");
            File.WriteAllBytes(mp3File, new byte[] { 0xFF, 0xFB, 0x90, 0x00, 0x00, 0x00 });

            // Act
            var ex = Assert.Throws<SoundLoadException>(() => new ManagedSound(mp3File));

            // Assert — the SoundLoadException from LoadMp3File's runtime check
            // must propagate unchanged through both rethrow catch blocks.
            Assert.Contains("Bundled FFmpeg runtime is not available", ex.Message);
            Assert.Contains("Test: bundled runtime unavailable", ex.Message);
            Assert.Contains("convert", ex.Message);
            Assert.Equal(mp3File, ex.SoundPath);
        }

        /// <summary>
        /// When the bundled FFmpeg runtime is unavailable, the
        /// <see cref="SoundLoadException"/> must preserve a custom source path
        /// supplied via the <see cref="ManagedSound(string, string)"/>
        /// constructor overload.
        /// </summary>
        [Fact]
        public void Constructor_WithMp3RuntimeUnavailableAndCustomSourcePath_ShouldPreserveSourcePath()
        {
            SetRuntimeAvailability(new FfmpegRuntimeAvailability(
                IsAvailable: false,
                DiagnosticReason: "Test: unavailable",
                BinaryFolder: null));

            var mp3File = Path.Combine(_tempDir, "tone.mp3");
            File.WriteAllBytes(mp3File, new byte[] { 0xFF, 0xFB });
            const string customSource = "preview/custom.mp3";

            var ex = Assert.Throws<SoundLoadException>(() => new ManagedSound(mp3File, customSource));

            Assert.Equal(customSource, ex.SoundPath);
            Assert.Contains("Bundled FFmpeg runtime is not available", ex.Message);
        }

        /// <summary>
        /// When the runtime reports available but the configured BinaryFolder
        /// does not contain the ffmpeg binary, FFMpegCore should fail and the
        /// resulting <see cref="SoundLoadException"/> should carry a helpful
        /// diagnostic.
        ///
        /// This test exercises the
        /// <c>catch (FileNotFoundException ex) when (ex.Message.Contains("ffmpeg"))</c>
        /// branch and the generic <c>catch (Exception ex)</c> branch.  The
        /// process PATH is cleared for the duration of the load so that
        /// FFMpegCore cannot fall back to a system ffmpeg (e.g. via Homebrew),
        /// making the missing-binary outcome deterministic regardless of the
        /// host platform.
        /// </summary>
        [Fact]
        public void Constructor_WithMp3AndMissingFfmpegBinary_ShouldThrowSoundLoadExceptionWhenFfmpegNotOnPath()
        {
            // Arrange — point the runtime at an empty temp folder so FFMpegCore
            // cannot find the ffmpeg/ffprobe binaries in BinaryFolder.
            var fakeBinFolder = Path.Combine(_tempDir, "fakebin");
            Directory.CreateDirectory(fakeBinFolder);

            SetRuntimeAvailability(new FfmpegRuntimeAvailability(
                IsAvailable: true,
                DiagnosticReason: null,
                BinaryFolder: fakeBinFolder));
            GlobalFFOptions.Configure(o => o.BinaryFolder = fakeBinFolder);

            // Use the committed MP3 fixture so FFProbe has real audio data.
            var mp3File = Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "Audio",
                "ffmpeg-tone.mp3");
            Assert.True(File.Exists(mp3File), $"Missing committed audio fixture: {mp3File}");

            // Clear PATH for the process under test so FFMpegCore's subprocess
            // cannot locate ffmpeg/ffprobe via the system PATH (e.g. Homebrew).
            var savedPath = Environment.GetEnvironmentVariable("PATH");
            Environment.SetEnvironmentVariable("PATH", string.Empty);
            try
            {
                // Act — the load must fail because ffmpeg is neither in the
                // empty BinaryFolder nor on PATH.
                var ex = Assert.Throws<SoundLoadException>(() => new ManagedSound(mp3File));

                // Assert — accept either the FileNotFoundException-specific
                // message or the generic conversion-failure message.
                Assert.True(
                    ex.Message.Contains("FFMpeg binary not found") ||
                    ex.Message.Contains("Failed to convert MP3 file using bundled FFMpeg"),
                    $"Unexpected MP3 failure message: {ex.Message}");
                Assert.Equal(mp3File, ex.SoundPath);
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", savedPath);
            }
        }

        private void SetRuntimeAvailability(FfmpegRuntimeAvailability availability)
        {
            var lazy = new Lazy<FfmpegRuntimeAvailability>(
                () => availability,
                LazyThreadSafetyMode.ExecutionAndPublication);
            _setConfigField(lazy);
        }

        /// <summary>
        /// Creates a delegate that writes to a static field using the
        /// <c>stsfld</c> IL instruction, bypassing the <c>initonly</c>
        /// (readonly) check that prevents <see cref="FieldInfo.SetValue"/>
        /// from writing to <c>readonly static</c> fields in .NET 8.
        /// </summary>
        private static Action<object?> CreateStaticFieldSetter(FieldInfo field)
        {
            var method = new DynamicMethod(
                "Set_" + field.Name,
                returnType: typeof(void),
                parameterTypes: new[] { typeof(object) },
                restrictedSkipVisibility: true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, field.FieldType);
            il.Emit(OpCodes.Stsfld, field);
            il.Emit(OpCodes.Ret);
            return (Action<object?>)method.CreateDelegate(typeof(Action<object?>));
        }
    }
}
