using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using DTXMania.Game.Lib.Utilities;

#nullable enable

namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Skin management system for DTXManiaCX
    /// Based on DTXMania's CSkin skin discovery and validation patterns
    /// </summary>
    public class SkinManager : IDisposable
    {
        #region Private Fields

        private readonly IResourceManager _resourceManager;
        private readonly string _systemSkinRoot; private string[] _availableSystemSkins = new string[0];
        // The "Default" skin path selected during the last RefreshAvailableSkins:
        // either the writable app-data System root (when it validates) or the
        // read-only bundled System root. Pinned to the top of AvailableSystemSkins
        // by the DiscoverSystemSkins sort comparator regardless of which root it
        // came from, so the dropdown always lists Default first.
        private string? _defaultSkinPath;
        private bool _disposed = false;

        #endregion

        #region Constructor

        public SkinManager(IResourceManager resourceManager, string? systemSkinRoot = null)
        {
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
            var resolvedSystemSkinRoot = AppPaths.ResolvePath(systemSkinRoot ?? AppPaths.GetDefaultSystemSkinRoot(), AppPaths.GetAppDataRoot());
            _systemSkinRoot = NormalizePath(resolvedSystemSkinRoot);
            RefreshAvailableSkins();
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Available system skins (in System/ directory)
        /// </summary>
        public IReadOnlyList<string> AvailableSystemSkins => _availableSystemSkins;

        /// <summary>
        /// Current effective skin path being used
        /// </summary>
        public string CurrentSkinPath => _resourceManager.GetCurrentEffectiveSkinPath();

        #endregion

        #region Public Methods

        /// <summary>
        /// Refresh the list of available skins
        /// Based on DTXMania's ReloadSkinPaths() method
        /// </summary>
        public void RefreshAvailableSkins()
        {
            try
            {
                _availableSystemSkins = DiscoverSystemSkins();
                Debug.WriteLine($"SkinManager: Found {_availableSystemSkins.Length} system skins");

                foreach (var skin in _availableSystemSkins)
                {
                    Debug.WriteLine($"  - {GetSkinName(skin)}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SkinManager: Error refreshing skins: {ex.Message}");
                _availableSystemSkins = new string[0];
            }
        }

        /// <summary>
        /// Switch to a system skin by name
        /// </summary>
        /// <param name="skinName">Name of the skin to switch to</param>
        /// <returns>True if successful, false if skin not found</returns>
        public bool SwitchToSystemSkin(string skinName)
        {
            if (string.IsNullOrEmpty(skinName))
                return false;

            var skinPath = GetSkinPathFromName(skinName);
            if (skinPath == null)
            {
                Debug.WriteLine($"SkinManager: Skin '{skinName}' not found");
                return false;
            }

            // Revalidate on disk before mutating any state. The discovered list is
            // cached at RefreshAvailableSkins time, so a skin deleted (or stripped of
            // its validation files) between discovery and selection would otherwise
            // pass here: SetSkinPath only logs validation failure, so without this
            // guard ConfigStage.SwitchSkin would persist the stale path to Config.ini
            // and force ResourceManager onto fallback assets.
            if (!ValidateSkinPath(skinPath))
            {
                Debug.WriteLine($"SkinManager: Skin '{skinName}' no longer valid on disk: {skinPath}");
                return false;
            }

            try
            {
                // Clear any box.def skin override
                _resourceManager.SetBoxDefSkinPath("");

                // Set the new system skin
                _resourceManager.SetSkinPath(skinPath);

                Debug.WriteLine($"SkinManager: Switched to system skin '{skinName}' at {skinPath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SkinManager: Error switching to skin '{skinName}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Switch to a skin by absolute path rather than by discovered name.
        /// Used for skins that live outside the system skin root (e.g. a dev-preview
        /// checkout) so they remain selectable from the config dropdown after the
        /// player switches away from them. Validates the path on disk before
        /// mutating state, mirroring <see cref="SwitchToSystemSkin"/>.
        /// </summary>
        /// <param name="skinPath">Absolute path to the skin directory to switch to.</param>
        /// <returns>True if successful, false if the path is invalid or switching failed.</returns>
        public bool SwitchToSkinPath(string skinPath)
        {
            if (string.IsNullOrEmpty(skinPath))
                return false;

            if (!ValidateSkinPath(skinPath))
            {
                Debug.WriteLine($"SkinManager: External skin path no longer valid on disk: {skinPath}");
                return false;
            }

            try
            {
                _resourceManager.SetBoxDefSkinPath("");
                _resourceManager.SetSkinPath(skinPath);

                Debug.WriteLine($"SkinManager: Switched to skin at {skinPath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SkinManager: Error switching to skin path '{skinPath}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Set a box.def skin path for temporary override
        /// </summary>
        /// <param name="boxDefSkinPath">Path to box.def skin</param>
        /// <returns>True if successful</returns>
        public bool SetBoxDefSkin(string boxDefSkinPath)
        {
            if (string.IsNullOrEmpty(boxDefSkinPath))
            {
                _resourceManager.SetBoxDefSkinPath("");
                return true;
            }

            if (!ValidateSkinPath(boxDefSkinPath))
            {
                Debug.WriteLine($"SkinManager: Invalid box.def skin path: {boxDefSkinPath}");
                return false;
            }

            try
            {
                _resourceManager.SetBoxDefSkinPath(boxDefSkinPath);
                Debug.WriteLine($"SkinManager: Set box.def skin: {boxDefSkinPath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SkinManager: Error setting box.def skin: {ex.Message}");
                return false;
            }
        }        /// <summary>
                 /// Get skin name from full path
                 /// Based on DTXMania's GetSkinName() method
                 /// </summary>
                 /// <param name="skinPathFullName">Full path to skin directory</param>
                 /// <returns>Skin name or empty string if invalid</returns>
        public static string GetSkinName(string skinPathFullName)
        {
            if (string.IsNullOrEmpty(skinPathFullName))
                return "";

            try
            {
                var normalizedPath = skinPathFullName.TrimEnd(Path.DirectorySeparatorChar, '/', '\\');

                // Handle default skin case (System/ -> "Default")
                // Split by both forward and backward slashes and get the last non-empty part
                var parts = normalizedPath.Split(new char[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    return "";

                var lastSegment = parts[parts.Length - 1];
                if (lastSegment.Equals("System", StringComparison.OrdinalIgnoreCase))
                {
                    return "Default";
                }

                // Handle custom skin case (System/SkinName/ -> "SkinName")
                return lastSegment;
            }
            catch
            {
                return "";
            }
        }/// <summary>
         /// Validate if a path contains a valid skin
         /// Based on DTXMania's bIsValid() method
         /// </summary>
         /// <param name="skinPath">Path to validate</param>
         /// <returns>True if valid skin</returns>
        public static bool ValidateSkinPath(string skinPath)
        {
            return PathValidator.IsValidSkinPath(skinPath);
        }

        #endregion

        #region Private Methods

        private string[] DiscoverSystemSkins()
        {
            var fullSystemSkinRoot = Path.GetFullPath(_systemSkinRoot);
            var skinPaths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // The "Default" skin: prefer the writable app-data System root; fall
            // back to the read-only bundled System skin (macOS .app
            // Contents/Resources/System or portable System/ sibling) so a clean
            // install still lists Default even when app-data is empty.
            // ResourceManager uses the same bundled root as its ultimate fallback,
            // so the dropdown stays consistent with runtime asset resolution.
            string? defaultRoot = null;
            if (ValidateSkinPath(fullSystemSkinRoot))
            {
                defaultRoot = fullSystemSkinRoot;
            }
            else
            {
                defaultRoot = ResolveBundledSystemSkinRoot();
            }

            if (defaultRoot != null)
            {
                var normalizedDefault = NormalizePath(Path.GetFullPath(defaultRoot));
                if (seen.Add(normalizedDefault))
                {
                    skinPaths.Add(normalizedDefault);
                    Debug.WriteLine($"SkinManager: Found default skin: {normalizedDefault}");
                }
            }
            _defaultSkinPath = defaultRoot != null ? NormalizePath(Path.GetFullPath(defaultRoot)) : null;

            // Custom skins live under the writable app-data System root (the
            // bundled root is read-only). Scan it even when it doesn't validate as
            // a default on its own, since a user may have installed custom skins
            // there without the default's validation files at the root.
            if (Directory.Exists(fullSystemSkinRoot))
            {
                var directories = Directory.GetDirectories(fullSystemSkinRoot, "*", SearchOption.TopDirectoryOnly);

                foreach (var directory in directories)
                {
                    var normalizedPath = NormalizePath(Path.GetFullPath(directory));

                    if (seen.Contains(normalizedPath))
                        continue;

                    if (ValidateSkinPath(normalizedPath))
                    {
                        if (seen.Add(normalizedPath))
                        {
                            skinPaths.Add(normalizedPath);
                            Debug.WriteLine($"SkinManager: Found custom skin: {normalizedPath}");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"SkinManager: Invalid skin: {normalizedPath}");
                    }
                }
            }

            // Sort for consistent ordering (default skin first). The comparator
            // pins _defaultSkinPath to the top regardless of whether it came from
            // app-data or the bundled root, then falls back to ordinal name order.
            skinPaths.Sort((a, b) =>
            {
                if (a == _defaultSkinPath) return -1;
                if (b == _defaultSkinPath) return 1;
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });

            return skinPaths.ToArray();
        }

        /// <summary>
        /// Resolve the read-only bundled System skin root from
        /// <see cref="AppPaths.GetBundledSystemSkinRootCandidates"/>, returning the
        /// first candidate that exists and validates as a skin, or null when none
        /// does. Mirrors ResourceManager.ResolveBundledSystemSkinRoot so the
        /// discovery list and the runtime fallback agree on the bundled root.
        /// </summary>
        private string? ResolveBundledSystemSkinRoot()
        {
            return ResolveBundledSystemSkinRootFromCandidates(AppPaths.GetBundledSystemSkinRootCandidates());
        }

        /// <summary>
        /// Core logic of <see cref="ResolveBundledSystemSkinRoot"/> extracted as an
        /// internal static method so the candidate iteration, validation, and
        /// trailing-separator normalization are unit-testable without a real
        /// assembly directory or files on disk.
        /// </summary>
        /// <param name="candidates">Ordered candidate bundled System skin root paths.</param>
        /// <returns>The first existing, validating candidate with a trailing separator, or null.</returns>
        internal static string? ResolveBundledSystemSkinRootFromCandidates(IEnumerable<string> candidates)
        {
            foreach (var candidate in candidates)
            {
                try
                {
                    if (ValidateSkinPath(candidate))
                    {
                        var normalized = NormalizePath(Path.GetFullPath(candidate));
                        Debug.WriteLine($"SkinManager: Using bundled System skin root: {normalized}");
                        return normalized;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SkinManager: Bundled candidate '{candidate}' check failed: {ex.Message}");
                }
            }
            return null;
        }
        private string GetSkinPathFromName(string skinName)
        {
            return _availableSystemSkins.FirstOrDefault(path =>
                string.Equals(GetSkinName(path), skinName, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Ensure path ends with directory separator
            return path.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            if (!_disposed)
            {
                // No resources to dispose currently
                _disposed = true;
            }
        }

        #endregion
    }
}
