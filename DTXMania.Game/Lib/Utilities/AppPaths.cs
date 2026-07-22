using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace DTXMania.Game.Lib.Utilities
{
    /// <summary>
    /// Centralized application data path utilities (cross-platform)
    /// </summary>
    public static class AppPaths
    {
        private const string AppName = "DTXManiaCX";
        private const string AppDataRootOverrideEnvVar = "DTXMANIA_APPDATA_ROOT";

        /// <summary>
        /// Platform-aware comparer for skin path equality. Windows filesystems
        /// are case-insensitive by default (NTFS/ReFS), so OrdinalIgnoreCase
        /// matches the OS behavior. macOS APFS is case-insensitive by default
        /// but can be case-sensitive; Linux ext4/btrfs are always case-sensitive.
        /// Using Ordinal on non-Windows avoids treating <c>System/Neon</c> and
        /// <c>System/neon</c> as the same skin, which would skip cache eviction,
        /// refuse to persist the new path, and deduplicate discovery entries on
        /// case-sensitive volumes. Display-label comparisons (dropdown text,
        /// skin names shown to the user) should remain OrdinalIgnoreCase
        /// regardless of platform.
        /// </summary>
        public static StringComparer SkinPathComparer =>
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        /// <summary>
        /// <see cref="StringComparison"/> equivalent of
        /// <see cref="SkinPathComparer"/> for use with
        /// <see cref="string.Equals(string, string, StringComparison)"/> and
        /// <see cref="string.Compare(string, string, StringComparison)"/>.
        /// </summary>
        public static StringComparison SkinPathComparison =>
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        /// <summary>
        /// Get the base application data directory for the current OS.
        /// DTXMANIA_APPDATA_ROOT overrides the OS-specific default when set.
        /// Windows: %LOCALAPPDATA%\DTXManiaCX
        /// macOS: ~/Library/Application Support/DTXManiaCX
        /// Other: ~/.config/DTXManiaCX (via SpecialFolder.ApplicationData, which maps to $XDG_CONFIG_HOME or ~/.config on Linux)
        /// </summary>
        public static string GetAppDataRoot()
        {
            var overrideRoot = Environment.GetEnvironmentVariable(AppDataRootOverrideEnvVar);
            if (!string.IsNullOrWhiteSpace(overrideRoot))
            {
                return Path.GetFullPath(ExpandHomePath(overrideRoot));
            }

            string basePath;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var home = GetHomeDirectory();
                basePath = Path.Combine(home, "Library", "Application Support");
            }
            else
            {
                basePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }

            if (string.IsNullOrWhiteSpace(basePath) || !Path.IsPathRooted(basePath))
            {
                var fallbackHome = GetHomeDirectory();
                if (string.IsNullOrWhiteSpace(fallbackHome))
                {
                    throw new InvalidOperationException(
                        $"Unable to determine a valid home directory using {nameof(GetHomeDirectory)}(). " +
                        $"Cannot resolve application data path with basePath='{basePath}' and {nameof(AppName)}='{AppName}'.");
                }
                basePath = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? Path.Combine(fallbackHome, "Library", "Application Support")
                    : Path.Combine(fallbackHome, ".config");
            }

            return Path.GetFullPath(Path.Combine(basePath, AppName));
        }

        private static string GetHomeDirectory()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
            {
                home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            }

            if (string.IsNullOrWhiteSpace(home))
            {
                home = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
            }

            return home;
        }

        public static string GetConfigFilePath()
        {
            return Path.GetFullPath(Path.Combine(GetAppDataRoot(), "Config.ini"));
        }

        public static string GetDefaultSongsPath()
        {
            return Path.GetFullPath(Path.Combine(GetAppDataRoot(), "DTXFiles"));
        }

        public static string GetDefaultSystemSkinRoot()
        {
            return Path.GetFullPath(Path.Combine(GetAppDataRoot(), "System"));
        }

        /// <summary>
        /// Candidate read-only System skin roots bundled with the app, in priority order.
        /// Used as the ultimate fallback when the writable app-data System skin is missing
        /// assets (e.g. a clean macOS .app install where the bundled skin lives in
        /// Contents/Resources/System but app-data is empty).
        ///
        /// Candidates are returned WITHOUT checking existence — callers pick the first
        /// that actually exists on disk. Keeping this pure makes it testable without a
        /// filesystem.
        ///
        /// macOS .app bundle: the executable lives in Contents/MacOS/, so
        /// AppContext.BaseDirectory resolves to .../Contents/MacOS/ and the bundled
        /// System skin is at ../Resources/System.
        /// Portable build (Windows zip, non-bundled): System/ sibling to the executable.
        /// </summary>
        public static IEnumerable<string> GetBundledSystemSkinRootCandidates()
        {
            return GetBundledSystemSkinRootCandidates(AppContext.BaseDirectory);
        }

        /// <summary>
        /// Overload accepting an explicit base directory so the empty/whitespace guard
        /// and candidate layout are unit-testable without depending on
        /// <see cref="AppContext.BaseDirectory"/> (which is never empty during tests).
        /// </summary>
        /// <param name="baseDir">The base directory to resolve candidates from.</param>
        /// <returns>Candidate bundled System skin root paths in priority order.</returns>
        internal static IEnumerable<string> GetBundledSystemSkinRootCandidates(string baseDir)
        {
            if (string.IsNullOrWhiteSpace(baseDir))
                yield break;

            // macOS .app bundle: Contents/Resources/System (relative to Contents/MacOS/)
            yield return Path.GetFullPath(Path.Combine(baseDir, "..", "Resources", "System"));

            // Portable build: System/ sibling to the executable
            yield return Path.GetFullPath(Path.Combine(baseDir, "System"));
        }

        public static string GetSongsDatabasePath()
        {
            return Path.GetFullPath(Path.Combine(GetAppDataRoot(), "songs.db"));
        }

        public static string ResolvePathOrDefault(string? configuredPath, string defaultPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
                return Path.GetFullPath(defaultPath);

            return ResolvePath(configuredPath, GetAppDataRoot());
        }

        public static string ResolvePath(string path, string basePath)
        {
            if (string.IsNullOrWhiteSpace(path))
                return Path.GetFullPath(basePath);

            path = ExpandHomePath(path);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && IsMacLibraryRelativePath(path))
            {
                var home = GetHomeDirectory();
                if (!string.IsNullOrWhiteSpace(home))
                {
                    path = Path.Combine(home, path);
                }
            }

            if (Path.IsPathRooted(path))
                return Path.GetFullPath(path);

            return Path.GetFullPath(Path.Combine(basePath, path));
        }

        private static bool IsMacLibraryRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            return path.StartsWith("Library/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("Library\\", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExpandHomePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            var home = GetHomeDirectory();
            if (string.IsNullOrWhiteSpace(home))
            {
                // Fall back to returning the original path unchanged if home directory cannot be determined
                return path;
            }

            if (path == "~")
                return home;

            if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
            {
                return Path.Combine(home, path.Substring(2));
            }

            return path;
        }

        public static void EnsureDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            Directory.CreateDirectory(path);
        }
    }
}
