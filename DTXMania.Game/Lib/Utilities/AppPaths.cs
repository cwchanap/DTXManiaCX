using System;
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

        /// <summary>
        /// Get the base application data directory for the current OS.
        /// Windows: %LOCALAPPDATA%\DTXManiaCX
        /// macOS: ~/Library/Application Support/DTXManiaCX
        /// Other: $XDG_CONFIG_HOME/DTXManiaCX (or ~/.config/DTXManiaCX)
        /// </summary>
        public static string GetAppDataRoot()
        {
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
                path = Path.Combine(home, path);
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
