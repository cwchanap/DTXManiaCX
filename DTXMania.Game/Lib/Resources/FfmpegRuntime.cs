#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using FFMpegCore;

namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Result of configuring and probing the shared FFmpeg runtime.
    /// </summary>
    public sealed record FfmpegRuntimeAvailability(
        bool IsAvailable,
        string? DiagnosticReason,
        string? BinaryFolder);

    /// <summary>
    /// Configures FFMpegCore once for all audio consumers and reports whether
    /// both ffmpeg and ffprobe are available.
    /// </summary>
    public static class FfmpegRuntime
    {
        private static readonly Lazy<FfmpegRuntimeAvailability> Configuration =
            new(ConfigureRuntime, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// Configures FFMpegCore exactly once and returns a non-throwing availability result.
        /// </summary>
        public static FfmpegRuntimeAvailability EnsureConfigured() => Configuration.Value;

        /// <summary>
        /// Returns whether the configured runtime is available, with a diagnostic on failure.
        /// </summary>
        public static bool TryGetAvailability(out string? diagnosticReason)
        {
            var availability = EnsureConfigured();
            diagnosticReason = availability.DiagnosticReason;
            return availability.IsAvailable;
        }

        private static FfmpegRuntimeAvailability ConfigureRuntime()
        {
            try
            {
                var assemblyDirectory = Path.GetDirectoryName(
                    typeof(FfmpegRuntime).Assembly.Location);
                var binaryFolder = GetFFmpegBinaryFolder(
                    assemblyDirectory,
                    IsRunnableFile);

                if (!string.IsNullOrWhiteSpace(binaryFolder))
                {
                    GlobalFFOptions.Configure(options => options.BinaryFolder = binaryFolder);
                    return new FfmpegRuntimeAvailability(
                        true,
                        DiagnosticReason: null,
                        BinaryFolder: binaryFolder);
                }

                // FFMpegCore 5.4.0 treats an empty BinaryFolder as PATH lookup.
                GlobalFFOptions.Configure(options => options.BinaryFolder = string.Empty);
                return ProbePathAvailability(
                    Environment.GetEnvironmentVariable("PATH"),
                    IsRunnableFile);
            }
            catch (Exception ex)
            {
                return new FfmpegRuntimeAvailability(
                    false,
                    $"Failed to configure FFMpegCore: {ex.GetType().Name}: {ex.Message}",
                    BinaryFolder: null);
            }
        }

        /// <summary>
        /// Resolves the first bundled runtime folder containing both ffmpeg and ffprobe.
        /// Candidate order preserves the native Apple Silicon preference.
        /// </summary>
        internal static string? GetFFmpegBinaryFolder(
            string? assemblyDirectory,
            Func<string, bool>? binaryExists)
        {
            if (string.IsNullOrWhiteSpace(assemblyDirectory) || binaryExists == null)
            {
                return null;
            }

            var ffmpegName = GetBinaryName("ffmpeg");
            var ffprobeName = GetBinaryName("ffprobe");
            string[] candidateFolders =
            {
                Path.Combine(assemblyDirectory, "runtimes", "osx-arm64", "MMTools"),
                Path.Combine(assemblyDirectory, "runtimes", "osx-x64", "MMTools"),
                Path.Combine(assemblyDirectory, "runtimes", "win-x64", "MMTools"),
                Path.Combine(assemblyDirectory, "runtimes", "win-x86", "MMTools"),
                Path.Combine(assemblyDirectory, "runtimes", "linux-x64", "MMTools"),
            };

            foreach (var folder in candidateFolders)
            {
                if (binaryExists(Path.Combine(folder, ffmpegName)) &&
                    binaryExists(Path.Combine(folder, ffprobeName)))
                {
                    return folder;
                }
            }

            return null;
        }

        internal static FfmpegRuntimeAvailability ProbePathAvailability(
            string? pathValue,
            Func<string, bool> binaryExists)
        {
            ArgumentNullException.ThrowIfNull(binaryExists);

            var ffmpegPath = FindOnPath(GetBinaryName("ffmpeg"), pathValue, binaryExists);
            var ffprobePath = FindOnPath(GetBinaryName("ffprobe"), pathValue, binaryExists);
            if (ffmpegPath != null && ffprobePath != null)
            {
                return new FfmpegRuntimeAvailability(
                    true,
                    DiagnosticReason: null,
                    BinaryFolder: null);
            }

            var missing = new List<string>(2);
            if (ffmpegPath == null)
            {
                missing.Add(GetBinaryName("ffmpeg"));
            }

            if (ffprobePath == null)
            {
                missing.Add(GetBinaryName("ffprobe"));
            }

            return new FfmpegRuntimeAvailability(
                false,
                $"FFmpeg runtime unavailable. Missing from bundled runtime folders and PATH: {string.Join(", ", missing)}.",
                BinaryFolder: null);
        }

        private static string? FindOnPath(
            string binaryName,
            string? pathValue,
            Func<string, bool> binaryExists)
        {
            if (string.IsNullOrWhiteSpace(pathValue))
            {
                return null;
            }

            foreach (var folder in pathValue.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    var candidate = Path.Combine(folder, binaryName);
                    if (binaryExists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (Exception ex) when (
                    ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    // Ignore an invalid PATH entry and continue probing.
                }
            }

            return null;
        }

        private static string GetBinaryName(string command) =>
            OperatingSystem.IsWindows() ? $"{command}.exe" : command;

        private static bool IsRunnableFile(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            if (OperatingSystem.IsWindows())
            {
                return true;
            }

            try
            {
                var mode = File.GetUnixFileMode(path);
                const UnixFileMode executable =
                    UnixFileMode.UserExecute |
                    UnixFileMode.GroupExecute |
                    UnixFileMode.OtherExecute;
                return (mode & executable) != 0;
            }
            catch (PlatformNotSupportedException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}