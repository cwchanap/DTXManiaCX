using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using DTX.Utilities;

namespace DTX.Resources
{
    /// <summary>
    /// Skin information metadata
    /// </summary>
    public class SkinInfo
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author { get; set; } = "";
        public string Version { get; set; } = "";
        public bool IsValid { get; set; }
        public bool IsDefault { get; set; }
        public DateTime LastModified { get; set; }
        public long SizeBytes { get; set; }
        public List<string> MissingFiles { get; set; } = new List<string>();
    }

    /// <summary>
    /// Service for discovering and analyzing available skins
    /// Based on DTXMania's skin discovery patterns
    /// </summary>
    public class SkinDiscoveryService
    {
        #region Private Fields

        private readonly string _systemSkinRoot;
        private readonly string[] _requiredFiles = new[]
        {
            Path.Combine("Graphics", "1_background.jpg"),
            Path.Combine("Graphics", "2_background.jpg")
        };

        private readonly string[] _commonFiles = new[]
        {
            Path.Combine("Graphics", "7_background.jpg"),
            Path.Combine("Graphics", "5_background.jpg"),
            Path.Combine("Sounds", "Decide.ogg"),
            Path.Combine("Sounds", "Cancel.ogg"),
            Path.Combine("Sounds", "Move.ogg")
        };

        #endregion

        #region Constructor

        public SkinDiscoveryService(string systemSkinRoot = "System/")
        {
            _systemSkinRoot = NormalizePath(systemSkinRoot);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Discover all available skins with detailed information
        /// </summary>
        /// <returns>List of skin information</returns>
        public List<SkinInfo> DiscoverSkins()
        {
            var skins = new List<SkinInfo>();

            try
            {
                var fullSystemSkinRoot = Path.GetFullPath(_systemSkinRoot);

                if (!Directory.Exists(fullSystemSkinRoot))
                {
                    Debug.WriteLine($"SkinDiscoveryService: System skin root not found: {fullSystemSkinRoot}");
                    return skins;
                }

                var directories = Directory.GetDirectories(fullSystemSkinRoot, "*", SearchOption.TopDirectoryOnly);

                foreach (var directory in directories)
                {
                    // Use the original directory path for analysis
                    // AnalyzeSkin can handle both relative and absolute paths
                    var skinInfo = AnalyzeSkin(directory);
                    if (skinInfo != null && skinInfo.IsValid)
                    {
                        skins.Add(skinInfo);
                    }
                }

                // Sort by name
                skins.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SkinDiscoveryService: Error discovering skins: {ex.Message}");
            }

            return skins;
        }        /// <summary>
                 /// Analyze a specific skin directory
                 /// </summary>
                 /// <param name="skinPath">Path to skin directory</param>
                 /// <returns>Skin information or null if invalid</returns>
        public SkinInfo AnalyzeSkin(string skinPath)
        {
            if (string.IsNullOrEmpty(skinPath))
                return null;

            var fullSkinPath = Path.GetFullPath(skinPath);
            if (!Directory.Exists(fullSkinPath))
                return null;

            try
            {
                var skinInfo = new SkinInfo
                {
                    FullPath = NormalizePath(skinPath), // Keep relative path
                    Name = GetSkinName(skinPath),
                    LastModified = Directory.GetLastWriteTime(fullSkinPath)
                };

                // Check if this is the default skin (System/ path)
                skinInfo.IsDefault = string.Equals(skinInfo.Name, "Default", StringComparison.OrdinalIgnoreCase) ||
                                   skinPath.TrimEnd(Path.DirectorySeparatorChar, '/').Equals("System", StringComparison.OrdinalIgnoreCase);

                // Validate required files
                var missingFiles = new List<string>();
                foreach (var requiredFile in _requiredFiles)
                {
                    var filePath = Path.GetFullPath(Path.Combine(skinPath, requiredFile));
                    if (!File.Exists(filePath))
                    {
                        missingFiles.Add(requiredFile);
                    }
                }

                skinInfo.MissingFiles = missingFiles;
                skinInfo.IsValid = missingFiles.Count == 0;

                // Calculate total size
                skinInfo.SizeBytes = CalculateDirectorySize(fullSkinPath);

                // Try to read skin metadata from SkinConfig.ini if it exists
                ReadSkinMetadata(skinInfo);

                return skinInfo;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SkinDiscoveryService: Error analyzing skin {skinPath}: {ex.Message}");
                return null;
            }
        }        /// <summary>
                 /// Validate if a skin path contains required files
                 /// </summary>
                 /// <param name="skinPath">Path to validate</param>
                 /// <returns>True if valid</returns>
        public bool ValidateSkin(string skinPath)
        {
            return PathValidator.IsValidSkinPath(skinPath, _requiredFiles);
        }

        /// <summary>
        /// Get completeness percentage of a skin
        /// </summary>
        /// <param name="skinPath">Path to skin</param>
        /// <returns>Percentage (0-100) of completeness</returns>
        public int GetSkinCompleteness(string skinPath)
        {
            if (string.IsNullOrEmpty(skinPath))
                return 0;

            var fullSkinPath = Path.GetFullPath(skinPath);
            if (!Directory.Exists(fullSkinPath))
                return 0;

            var allFiles = _requiredFiles.Concat(_commonFiles).ToArray();
            var existingFiles = allFiles.Count(file => File.Exists(Path.GetFullPath(Path.Combine(skinPath, file))));

            return (int)Math.Round((double)existingFiles / allFiles.Length * 100);
        }

        #endregion

        #region Private Methods

        private void ReadSkinMetadata(SkinInfo skinInfo)
        {
            var configPath = Path.GetFullPath(Path.Combine(skinInfo.FullPath, "SkinConfig.ini"));
            if (!File.Exists(configPath))
                return;

            try
            {
                var lines = File.ReadAllLines(configPath);
                foreach (var line in lines)
                {
                    if (line.StartsWith(";") || !line.Contains("="))
                        continue;

                    var parts = line.Split('=', 2);
                    if (parts.Length != 2)
                        continue;

                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    switch (key.ToLowerInvariant())
                    {
                        case "displayname":
                        case "skinname":
                            if (!string.IsNullOrEmpty(value))
                                skinInfo.Description = value; // Use description instead of overriding name
                            break;
                        case "description":
                            skinInfo.Description = value;
                            break;
                        case "author":
                            skinInfo.Author = value;
                            break;
                        case "version":
                            skinInfo.Version = value;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SkinDiscoveryService: Error reading skin metadata: {ex.Message}");
            }
        }

        private long CalculateDirectorySize(string directoryPath)
        {
            try
            {
                var directoryInfo = new DirectoryInfo(directoryPath);
                return directoryInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                                   .Sum(file => file.Length);
            }
            catch
            {
                return 0;
            }
        }

        private static string GetSkinName(string skinPath)
        {
            try
            {
                return new DirectoryInfo(skinPath).Name;
            }
            catch
            {
                return Path.GetFileName(skinPath.TrimEnd(Path.DirectorySeparatorChar));
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            return path.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        #endregion
    }
}
