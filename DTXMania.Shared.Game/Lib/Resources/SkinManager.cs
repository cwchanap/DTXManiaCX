using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;

namespace DTX.Resources
{
    /// <summary>
    /// Skin management system for DTXManiaCX
    /// Based on DTXMania's CSkin skin discovery and validation patterns
    /// </summary>
    public class SkinManager : IDisposable
    {
        #region Private Fields

        private readonly IResourceManager _resourceManager;
        private readonly string _systemSkinRoot;
        private string[] _availableSystemSkins = Array.Empty<string>();
        private string[] _availableBoxDefSkins = Array.Empty<string>();
        private bool _disposed = false;

        #endregion

        #region Constructor

        public SkinManager(IResourceManager resourceManager, string systemSkinRoot = "System/")
        {
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
            _systemSkinRoot = NormalizePath(systemSkinRoot);
            RefreshAvailableSkins();
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Available system skins (in System/ directory)
        /// </summary>
        public IReadOnlyList<string> AvailableSystemSkins => _availableSystemSkins;

        /// <summary>
        /// Available box.def skins (custom song skins)
        /// </summary>
        public IReadOnlyList<string> AvailableBoxDefSkins => _availableBoxDefSkins;

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
                _availableSystemSkins = Array.Empty<string>();
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
        }

        /// <summary>
        /// Get skin name from full path
        /// Based on DTXMania's GetSkinName() method
        /// </summary>
        /// <param name="skinPathFullName">Full path to skin directory</param>
        /// <returns>Skin name or null if invalid</returns>
        public static string? GetSkinName(string skinPathFullName)
        {
            if (string.IsNullOrEmpty(skinPathFullName))
                return null;

            try
            {
                var parts = skinPathFullName.TrimEnd(Path.DirectorySeparatorChar, '/')
                                          .Split(Path.DirectorySeparatorChar, '/');
                return parts.LastOrDefault();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Validate if a path contains a valid skin
        /// Based on DTXMania's bIsValid() method
        /// </summary>
        /// <param name="skinPath">Path to validate</param>
        /// <returns>True if valid skin</returns>
        public static bool ValidateSkinPath(string skinPath)
        {
            if (string.IsNullOrEmpty(skinPath))
                return false;

            // Check for key validation files (DTXMania pattern)
            // Use full paths relative to current working directory
            var validationFiles = new[]
            {
                Path.GetFullPath(Path.Combine(skinPath, "Graphics", "1_background.jpg")),
                Path.GetFullPath(Path.Combine(skinPath, "Graphics", "2_background.jpg"))
            };

            return validationFiles.Any(File.Exists);
        }

        #endregion

        #region Private Methods

        private string[] DiscoverSystemSkins()
        {
            var fullSystemSkinRoot = Path.GetFullPath(_systemSkinRoot);

            if (!Directory.Exists(fullSystemSkinRoot))
            {
                Debug.WriteLine($"SkinManager: System skin root not found: {fullSystemSkinRoot}");
                return Array.Empty<string>();
            }

            var skinPaths = new List<string>();

            try
            {
                var directories = Directory.GetDirectories(fullSystemSkinRoot, "*", SearchOption.TopDirectoryOnly);

                foreach (var directory in directories)
                {
                    // Convert back to relative path for consistency
                    var relativePath = Path.GetRelativePath(Environment.CurrentDirectory, directory);
                    var normalizedPath = NormalizePath(relativePath);

                    if (ValidateSkinPath(normalizedPath))
                    {
                        skinPaths.Add(normalizedPath);
                        Debug.WriteLine($"SkinManager: Found valid skin: {normalizedPath}");
                    }
                    else
                    {
                        Debug.WriteLine($"SkinManager: Invalid skin: {normalizedPath}");
                    }
                }

                // Sort for consistent ordering
                skinPaths.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SkinManager: Error discovering skins: {ex.Message}");
            }

            return skinPaths.ToArray();
        }

        private string? GetSkinPathFromName(string skinName)
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
