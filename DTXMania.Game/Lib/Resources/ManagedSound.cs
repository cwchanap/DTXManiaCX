using Microsoft.Xna.Framework.Audio;
using System;
using System.IO;
using System.Threading;
using NVorbis;
using FFMpegCore;
using FFMpegCore.Pipes;

namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Managed sound implementation with reference counting
    /// Based on DTXMania's sound management patterns
    /// </summary>
    public class ManagedSound : ISound
    {
        #region Private Fields

        private SoundEffect _soundEffect;
        private readonly string _sourcePath;
        private int _referenceCount = 0;
        private bool _disposed = false;

        #endregion

        #region Properties

        public SoundEffect SoundEffect => _soundEffect;
        public string SourcePath => _sourcePath;
        public TimeSpan Duration => _soundEffect?.Duration ?? TimeSpan.Zero;
        public bool IsDisposed => _disposed;
        public int ReferenceCount => _referenceCount;

        #endregion

        #region Constructor

        /// <summary>
        /// Create sound from file path
        /// </summary>
        public ManagedSound(string filePath, string sourcePath = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            _sourcePath = sourcePath ?? filePath;

            try
            {
                LoadSoundFromFile(filePath);
            }
            catch (Exception ex)
            {
                throw new SoundLoadException(_sourcePath, $"Failed to load sound from {filePath}", ex);
            }
        }

        /// <summary>
        /// Create sound from existing SoundEffect
        /// </summary>
        public ManagedSound(SoundEffect soundEffect, string sourcePath)
        {
            _soundEffect = soundEffect ?? throw new ArgumentNullException(nameof(soundEffect));
            _sourcePath = sourcePath ?? "Unknown";
        }

        #endregion

        #region Static Constructor

        static ManagedSound()
        {
            // Configure FFMpeg to use bundled binaries from MMTools packages
            try
            {
                // Determine the runtime-specific path for ffmpeg
                string binaryFolder = GetBundledFFmpegPath();
                
                if (!string.IsNullOrEmpty(binaryFolder) && Directory.Exists(binaryFolder))
                {
                    GlobalFFOptions.Configure(options => 
                    {
                        options.BinaryFolder = binaryFolder;
                    });
                }
                else
                {
                    // Fall back to PATH
                    GlobalFFOptions.Configure(options => 
                    {
                        // Let FFMpegCore find ffmpeg in PATH automatically
                    });
                }
            }
            catch (Exception ex)
            {
                // Log warning but don't fail - MP3 support will just not work
                System.Diagnostics.Debug.WriteLine($"ManagedSound: Failed to configure FFMpeg: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the path to bundled ffmpeg binaries from MMTools packages
        /// </summary>
        private static string GetBundledFFmpegPath()
        {
            try
            {
                // Get the directory where the current assembly is located
                string assemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                
                if (string.IsNullOrEmpty(assemblyDir))
                    return null;

                // Check for platform-specific bundled ffmpeg
                string[] possiblePaths = {
                    // macOS (from MMTools.Executables.MacOS.X64)
                    Path.Combine(assemblyDir, "runtimes", "osx-x64", "MMTools"),
                    
                    // Windows (from MMTools.Executables.Windows.X64)
                    Path.Combine(assemblyDir, "runtimes", "win-x64", "MMTools"),
                    Path.Combine(assemblyDir, "runtimes", "win-x86", "MMTools"),
                    
                    // Linux (if we add support later)
                    Path.Combine(assemblyDir, "runtimes", "linux-x64", "MMTools"),
                };

                foreach (string path in possiblePaths)
                {
                    string ffmpegPath = Path.Combine(path, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
                    
                    if (File.Exists(ffmpegPath))
                    {
                        return path;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ManagedSound: Error finding bundled ffmpeg: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Reference Counting

        public void AddReference()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ManagedSound));

            Interlocked.Increment(ref _referenceCount);
        }

        public void RemoveReference()
        {
            if (_disposed)
                return;

            var newCount = Interlocked.Decrement(ref _referenceCount);
            if (newCount <= 0)
            {
                Dispose();
            }
        }

        #endregion

        #region Playback

        public SoundEffectInstance Play()
        {
            if (_disposed || _soundEffect == null)
                return null;

            return _soundEffect.CreateInstance();
        }



        public SoundEffectInstance Play(float volume)
        {
            return Play(volume, false); // Default to no loop
        }

        public SoundEffectInstance Play(float volume, float pitch, float pan)
        {
            return Play(volume, pitch, pan, false); // Default to no loop
        }

        public SoundEffectInstance Play(float volume, bool loop = false)
        {
            var instance = CreateInstance();
            if (instance != null)
            {
                instance.Volume = Math.Clamp(volume, 0.0f, 1.0f);
                instance.IsLooped = loop;
                instance.Play();
            }
            return instance;
        }

        public SoundEffectInstance Play(float volume, float pitch, float pan, bool loop = false)
        {
            var instance = CreateInstance();
            if (instance != null)
            {
                instance.Volume = Math.Clamp(volume, 0.0f, 1.0f);
                instance.Pitch = Math.Clamp(pitch, -1.0f, 1.0f);
                instance.Pan = Math.Clamp(pan, -1.0f, 1.0f);
                instance.IsLooped = loop;
                instance.Play();
            }
            return instance;
        }

        public SoundEffectInstance CreateInstance()
        {
            if (_disposed || _soundEffect == null)
                return null;

            return _soundEffect.CreateInstance();
        }

        #endregion

        #region Private Methods

        private void LoadSoundFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Sound file not found: {filePath}");

            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            switch (extension)
            {
                case ".wav":
                    LoadWavFile(filePath);
                    break;
                case ".ogg":
                    LoadOggFile(filePath);
                    break;
                case ".mp3":
                    LoadMp3File(filePath);
                    break;
                default:
                    throw new NotSupportedException($"Audio format not supported: {extension}");
            }
        }

        private void LoadWavFile(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            _soundEffect = SoundEffect.FromStream(stream);
        }

        private void LoadOggFile(string filePath)
        {
            using var vorbisReader = new VorbisReader(filePath);

            // Get audio properties
            var sampleRate = vorbisReader.SampleRate;
            var channels = vorbisReader.Channels;
            var totalSamples = (int)vorbisReader.TotalSamples;

            // Read all samples into a float array
            var floatBuffer = new float[totalSamples * channels];
            var samplesRead = vorbisReader.ReadSamples(floatBuffer, 0, floatBuffer.Length);

            // Convert float samples to 16-bit PCM
            var pcmData = new byte[samplesRead * 2]; // 16-bit = 2 bytes per sample
            for (int i = 0; i < samplesRead; i++)
            {
                // Clamp and convert float [-1.0, 1.0] to short [-32768, 32767]
                var sample = Math.Clamp(floatBuffer[i], -1.0f, 1.0f);
                var pcmSample = (short)(sample * 32767);

                // Write as little-endian 16-bit
                pcmData[i * 2] = (byte)(pcmSample & 0xFF);
                pcmData[i * 2 + 1] = (byte)((pcmSample >> 8) & 0xFF);
            }

            // Create SoundEffect from PCM data
            var audioChannels = channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo;
            _soundEffect = new SoundEffect(pcmData, sampleRate, audioChannels);
        }

        private void LoadMp3File(string filePath)
        {
            try
            {
                // Check if file exists
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"MP3 file not found: {filePath}");
                }
                
                // First, probe the file to get audio information
                var mediaInfo = FFProbe.Analyse(filePath);
                var audioStream = mediaInfo.PrimaryAudioStream;
                
                if (audioStream == null)
                {
                    throw new InvalidOperationException($"No audio stream found in file: {filePath}");
                }
                
                // Detect original channel count and preserve it
                var originalChannels = audioStream.Channels;
                var targetChannels = originalChannels > 0 ? originalChannels : 2; // Default to stereo if unknown
                
                // Use FFMpegCore to convert MP3 to raw PCM data
                using var outputStream = new MemoryStream();
                
                // Convert MP3 to raw PCM data (no WAV header, just samples)
                // Preserve original channel count instead of forcing stereo output
                // Note: MonoGame only supports Mono/Stereo, so 3+ channels will be downmixed to stereo
                FFMpegArguments
                    .FromFileInput(filePath)
                    .OutputToPipe(new StreamPipeSink(outputStream), options => options
                        .WithAudioCodec("pcm_s16le") // 16-bit signed little-endian PCM
                        .WithAudioSamplingRate(44100) // Standard sample rate
                        .WithCustomArgument($"-ac {targetChannels}") // Preserve original channel count
                        .ForceFormat("s16le")) // Raw 16-bit little-endian format (no container)
                    .ProcessSynchronously();

                // Validate output stream
                if (outputStream.Length == 0)
                {
                    throw new InvalidOperationException("FFMpeg produced empty output stream");
                }
                
                // Get the raw PCM data
                var pcmData = outputStream.ToArray();

                // Create SoundEffect with appropriate channel configuration
                const int sampleRate = 44100;
                // Map channel count to MonoGame's AudioChannels enum (only supports Mono/Stereo)
                var audioChannels = targetChannels == 1 ? AudioChannels.Mono : AudioChannels.Stereo;
                
                _soundEffect = new SoundEffect(pcmData, sampleRate, audioChannels);
            }
            catch (FileNotFoundException ex) when (ex.Message.Contains("ffmpeg"))
            {
                throw new SoundLoadException(_sourcePath, 
                    $"FFMpeg binary not found. MP3 support requires bundled ffmpeg binaries from MMTools packages.\n" +
                    $"Ensure the appropriate MMTools.Executables package is installed for your platform:\n" +
                    $"  macOS: MMTools.Executables.MacOS.X64\n" +
                    $"  Windows: MMTools.Executables.Windows.X64\n" +
                    $"Alternatively, convert {Path.GetFileName(filePath)} to WAV or OGG format.", ex);
            }
            catch (Exception ex)
            {
                throw new SoundLoadException(_sourcePath, 
                    $"Failed to convert MP3 file using bundled FFMpeg: {filePath}. " +
                    $"Error: {ex.Message}. Consider converting the file to WAV or OGG format for better compatibility.", ex);
            }
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _soundEffect?.Dispose();
                    _soundEffect = null;
                }
                _disposed = true;
            }
        }

        #endregion
    }

    /// <summary>
    /// Exception thrown when sound loading fails
    /// </summary>
    public class SoundLoadException : Exception
    {
        public string SoundPath { get; }

        public SoundLoadException(string soundPath, string message) : base(message)
        {
            SoundPath = soundPath;
        }

        public SoundLoadException(string soundPath, string message, Exception innerException) : base(message, innerException)
        {
            SoundPath = soundPath;
        }
    }
}
