#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Utilities;

namespace DTXMania.Game.Lib.Song
{
    internal enum SongRootAvailability
    {
        Available,
        Missing,
        Inaccessible,
    }

    /// <summary>
    /// The canonicalization and identity policy for configured song-library roots.
    /// Keeping this logic in one place prevents config, enumeration, and cache rebuilds
    /// from disagreeing about equivalent or overlapping directories.
    /// </summary>
    internal sealed class SongRootPolicy
    {
        private readonly StringComparer _comparer;

        internal SongRootPolicy(StringComparer comparer)
        {
            _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
        }

        internal static SongRootPolicy ForCurrentPlatform() =>
            new(CreateComparer(
                OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()));

        internal static StringComparer CreateComparer(bool ignoreCase) =>
            ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        internal StringComparer Comparer => _comparer;

        internal SongRootValidationResult Validate(IReadOnlyList<string> roots)
        {
            ArgumentNullException.ThrowIfNull(roots);

            if (roots.Count == 0)
            {
                return new SongRootValidationResult(
                    Array.Empty<string>(),
                    new[]
                    {
                        new SongRootDiagnostic(
                            string.Empty,
                            "At least one configured song root is required.",
                            IsWarning: false),
                    });
            }

            var canonicalRoots = new List<string>(roots.Count);
            var diagnostics = new List<SongRootDiagnostic>();
            foreach (var configuredRoot in roots)
            {
                if (string.IsNullOrWhiteSpace(configuredRoot))
                {
                    diagnostics.Add(new SongRootDiagnostic(
                        configuredRoot ?? string.Empty,
                        "A configured song root is blank.",
                        IsWarning: false));
                    continue;
                }

                if (!TryNormalize(configuredRoot, out var normalizedRoot, out var error))
                {
                    diagnostics.Add(new SongRootDiagnostic(
                        configuredRoot,
                        $"Configured song root path is invalid: {error}",
                        IsWarning: false));
                    continue;
                }

                if (canonicalRoots.Any(existing => PathsEqual(existing, normalizedRoot)))
                {
                    diagnostics.Add(new SongRootDiagnostic(
                        normalizedRoot,
                        "A configured song root duplicates an earlier root.",
                        IsWarning: false));
                    continue;
                }

                if (canonicalRoots.Any(existing =>
                    IsAncestor(existing, normalizedRoot) ||
                    IsAncestor(normalizedRoot, existing)))
                {
                    diagnostics.Add(new SongRootDiagnostic(
                        normalizedRoot,
                        "A configured song root overlaps an earlier root.",
                        IsWarning: false));
                    continue;
                }

                canonicalRoots.Add(normalizedRoot);

                switch (Probe(normalizedRoot))
                {
                    case SongRootAvailability.Missing:
                        diagnostics.Add(new SongRootDiagnostic(
                            normalizedRoot,
                            $"Configured song root does not exist: {normalizedRoot}",
                            IsWarning: true));
                        break;
                    case SongRootAvailability.Inaccessible:
                        diagnostics.Add(new SongRootDiagnostic(
                            normalizedRoot,
                            $"Configured song root is inaccessible: {normalizedRoot}",
                            IsWarning: true));
                        break;
                }
            }

            return new SongRootValidationResult(canonicalRoots, diagnostics);
        }

        /// <summary>
        /// Returns whether <paramref name="parent"/> is a strict ancestor of
        /// <paramref name="child"/>. It deliberately compares path segments rather than
        /// using relative-path text so a root such as /Songs never matches /SongsBackup.
        /// </summary>
        internal bool IsAncestor(string parent, string child)
        {
            if (!TryNormalize(parent, out var normalizedParent, out _) ||
                !TryNormalize(child, out var normalizedChild, out _))
            {
                return false;
            }

            var parentSegments = Split(normalizedParent);
            var childSegments = Split(normalizedChild);
            if (!_comparer.Equals(parentSegments.Volume, childSegments.Volume) ||
                parentSegments.Segments.Count >= childSegments.Segments.Count)
            {
                return false;
            }

            for (var index = 0; index < parentSegments.Segments.Count; index++)
            {
                if (!_comparer.Equals(
                    parentSegments.Segments[index],
                    childSegments.Segments[index]))
                {
                    return false;
                }
            }

            return true;
        }

        internal SongRootAvailability Probe(string normalizedRoot)
        {
            // Directory.Exists swallows access exceptions internally and returns
            // false for both missing and inaccessible directories, so the catch
            // blocks below would never fire from Exists alone. Distinguish the
            // two by attempting a read access on a directory that Exists reports
            // as present: an inaccessible root surfaces as Inaccessible rather
            // than being misreported as Missing.
            try
            {
                if (!Directory.Exists(normalizedRoot))
                    return SongRootAvailability.Missing;

                // Force a real directory read. EnumerateFileSystemEntries is
                // documented as lazy: constructing the enumerable alone is not
                // guaranteed to open the directory on every runtime, so advance
                // the enumerator once to force the ACL/read check inside this
                // try block. (On .NET 8 macOS construction already throws, but
                // MoveNext makes the eager-read contract runtime-independent.)
                // MoveNext returns false for an empty but readable directory;
                // only whether it throws matters here.
                using var entries = Directory
                    .EnumerateFileSystemEntries(normalizedRoot)
                    .GetEnumerator();
                _ = entries.MoveNext();
                return SongRootAvailability.Available;
            }
            catch (UnauthorizedAccessException)
            {
                return SongRootAvailability.Inaccessible;
            }
            catch (IOException)
            {
                return SongRootAvailability.Inaccessible;
            }
            catch (System.Security.SecurityException)
            {
                return SongRootAvailability.Inaccessible;
            }
        }

        private bool PathsEqual(string first, string second)
        {
            var firstSegments = Split(first);
            var secondSegments = Split(second);
            if (!_comparer.Equals(firstSegments.Volume, secondSegments.Volume) ||
                firstSegments.Segments.Count != secondSegments.Segments.Count)
            {
                return false;
            }

            for (var index = 0; index < firstSegments.Segments.Count; index++)
            {
                if (!_comparer.Equals(
                    firstSegments.Segments[index],
                    secondSegments.Segments[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryNormalize(
            string path,
            out string normalized,
            out string error)
        {
            try
            {
                normalized = IsWindowsDrivePath(path)
                    ? NormalizeWindowsDrivePath(path)
                    : SongPathIdentity.Normalize(AppPaths.ResolvePath(
                        path,
                        AppPaths.GetAppDataRoot()));
                error = string.Empty;
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException
                    or IOException or UnauthorizedAccessException
                    or System.Security.SecurityException)
            {
                normalized = string.Empty;
                error = exception.Message;
                return false;
            }
        }

        private static bool IsWindowsDrivePath(string path) =>
            path.Length >= 3 &&
            char.IsLetter(path[0]) &&
            path[1] == ':' &&
            (path[2] == '\\' || path[2] == '/');

        // Windows-like roots are accepted as configuration data on every host. This
        // lets the policy's injected comparer be tested independently of the host OS.
        private static string NormalizeWindowsDrivePath(string path)
        {
            var parts = path.Substring(3)
                .Replace('/', '\\')
                .Split('\\', StringSplitOptions.RemoveEmptyEntries);
            var segments = new List<string>(parts.Length);
            foreach (var part in parts)
            {
                if (part == ".")
                    continue;
                if (part == "..")
                {
                    if (segments.Count > 0)
                        segments.RemoveAt(segments.Count - 1);
                    continue;
                }

                segments.Add(part);
            }

            var prefix = path.Substring(0, 2);
            return segments.Count == 0
                ? prefix + "\\"
                : prefix + "\\" + string.Join("\\", segments);
        }

        private static PathSegments Split(string normalizedPath)
        {
            if (IsWindowsDrivePath(normalizedPath))
            {
                var segments = normalizedPath.Length <= 3
                    ? Array.Empty<string>()
                    : normalizedPath.Substring(3)
                        .Split('\\', StringSplitOptions.RemoveEmptyEntries);
                return new PathSegments(normalizedPath.Substring(0, 2), segments);
            }

            return new PathSegments(
                Path.DirectorySeparatorChar.ToString(),
                normalizedPath.Split(
                    Path.DirectorySeparatorChar,
                    StringSplitOptions.RemoveEmptyEntries));
        }

        private sealed record PathSegments(
            string Volume,
            IReadOnlyList<string> Segments);
    }

    internal sealed class SongRootValidationResult
    {
        internal SongRootValidationResult(
            IReadOnlyList<string> canonicalRoots,
            IReadOnlyList<SongRootDiagnostic> diagnostics)
        {
            ArgumentNullException.ThrowIfNull(canonicalRoots);
            ArgumentNullException.ThrowIfNull(diagnostics);

            CanonicalRoots = Array.AsReadOnly(canonicalRoots.ToArray());
            Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        }

        internal IReadOnlyList<string> CanonicalRoots { get; }

        internal IReadOnlyList<SongRootDiagnostic> Diagnostics { get; }

        internal bool IsValid => Diagnostics.All(diagnostic => diagnostic.IsWarning);
    }
}
