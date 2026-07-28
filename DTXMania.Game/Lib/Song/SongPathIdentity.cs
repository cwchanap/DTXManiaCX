#nullable enable
using System;
using System.IO;

namespace DTXMania.Game.Lib.Song
{
    internal static class SongPathIdentity
    {
        public static StringComparer CanonicalComparer { get; } =
            StringComparer.Ordinal;

        public static StringComparer LegacyAliasComparer { get; } =
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        public static string Normalize(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            var fullPath = Path.GetFullPath(path)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return Path.TrimEndingDirectorySeparator(fullPath);
        }

        public static bool TryNormalize(string? path, out string normalized)
        {
            try
            {
                normalized = Normalize(path!);
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException
                    or PathTooLongException)
            {
                normalized = string.Empty;
                return false;
            }
        }

        public static bool IsUnderRoot(string path, string root)
        {
            var relative = Path.GetRelativePath(Normalize(root), Normalize(path));
            return relative != ".."
                && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !Path.IsPathRooted(relative);
        }

        public static string ForSetDefinition(string setDefPath) =>
            $"set|{Normalize(setDefPath)}";

        public static string ForOrdinaryChart(string chartPath, string title, string artist)
        {
            var directory = Path.GetDirectoryName(Normalize(chartPath))
                ?? throw new InvalidOperationException("A chart path must have a directory.");
            return $"dir|{directory}\u001f{title}\u001f{artist}";
        }
    }
}
