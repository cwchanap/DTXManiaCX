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
                var home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                basePath = Path.Combine(home, "Library", "Application Support");
            }
            else
            {
                basePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }

            return Path.Combine(basePath, AppName);
        }

        public static string GetConfigFilePath()
        {
            return Path.Combine(GetAppDataRoot(), "Config.ini");
        }

        public static string GetDefaultSongsPath()
        {
            return Path.Combine(GetAppDataRoot(), "Songs");
        }

        public static string GetDefaultSystemSkinRoot()
        {
            return Path.Combine(GetAppDataRoot(), "System");
        }

        public static string GetSongsDatabasePath()
        {
            return Path.Combine(GetAppDataRoot(), "songs.db");
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

            if (Path.IsPathRooted(path))
                return Path.GetFullPath(path);

            return Path.GetFullPath(Path.Combine(basePath, path));
        }

        public static void EnsureDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            Directory.CreateDirectory(path);
        }
    }
}
