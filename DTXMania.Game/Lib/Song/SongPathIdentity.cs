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

        /// <summary>
        /// Whether path identity is case-sensitive on the current platform.
        /// Windows and macOS default to case-insensitive filesystems; Linux and
        /// other Unix systems are case-sensitive. Mirrors the comparer selected
        /// by <see cref="SongRootPolicy.ForCurrentPlatform"/>.
        /// </summary>
        public static bool IsCaseSensitive =>
            !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS();

        /// <summary>
        /// Produces a stable root identity key from an already-normalized root
        /// path. On case-insensitive platforms (Windows, macOS) the key is
        /// lowercased so that differently-cased paths representing the same
        /// configured root produce the same storage key. On case-sensitive
        /// platforms the path is returned unchanged.
        /// <para>
        /// This must be used consistently for per-root watermark storage and
        /// lookup so that a casing-only change in configuration cannot make a
        /// root's watermark appear absent.
        /// </para>
        /// </summary>
        public static string GetStableRootKey(string normalizedRoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(normalizedRoot);
            return IsCaseSensitive
                ? normalizedRoot
                : normalizedRoot.ToLowerInvariant();
        }

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
            return IsUnderNormalizedRoot(Normalize(path), Normalize(root));
        }

        /// <summary>
        /// Checks whether <paramref name="normalizedPath"/> is contained within
        /// <paramref name="normalizedRoot"/>. Both arguments must already be
        /// normalized via <see cref="Normalize"/>. Avoids redundant normalization
        /// when checking many paths against a pre-normalized root.
        /// </summary>
        public static bool IsUnderNormalizedRoot(
            string normalizedPath,
            string normalizedRoot)
        {
            var relative = Path.GetRelativePath(normalizedRoot, normalizedPath);
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
