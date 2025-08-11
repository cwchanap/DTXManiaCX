using System;
using System.IO;
using System.Linq;

namespace DTXMania.Game.Lib.Utilities
{
    /// <summary>
    /// Centralized path and directory validation utilities
    /// Consolidates redundant validation logic from ConfigurationValidator, SkinManager, and SkinDiscoveryService
    /// </summary>
    public static class PathValidator
    {
        #region Directory Validation

        /// <summary>
        /// Validate if a directory exists and is accessible
        /// </summary>
        /// <param name="directoryPath">Path to validate</param>
        /// <returns>True if directory exists and is accessible</returns>
        public static bool IsValidDirectory(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath))
                return false;

            try
            {
                return Directory.Exists(directoryPath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validate if a directory exists, with option to create it
        /// </summary>
        /// <param name="directoryPath">Path to validate</param>
        /// <param name="createIfMissing">Create directory if it doesn't exist</param>
        /// <returns>True if directory exists or was created successfully</returns>
        public static bool EnsureDirectory(string directoryPath, bool createIfMissing = false)
        {
            if (string.IsNullOrEmpty(directoryPath))
                return false;

            try
            {
                if (Directory.Exists(directoryPath))
                    return true;

                if (createIfMissing)
                {
                    Directory.CreateDirectory(directoryPath);
                    return Directory.Exists(directoryPath);
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Skin Validation

        /// <summary>
        /// Validate if a path contains a valid DTXMania skin
        /// Consolidates logic from SkinManager.ValidateSkinPath() and SkinDiscoveryService.ValidateSkin()
        /// </summary>
        /// <param name="skinPath">Path to validate</param>
        /// <returns>True if path contains a valid skin</returns>
        public static bool IsValidSkinPath(string skinPath)
        {
            if (!IsValidDirectory(skinPath))
                return false;

            // Check for key validation files (DTXMania pattern)
            var validationFiles = new[]
            {
                Path.Combine(skinPath, "Graphics", "1_background.jpg"),
                Path.Combine(skinPath, "Graphics", "2_background.jpg")
            };

            return validationFiles.Any(File.Exists);
        }

        /// <summary>
        /// Validate skin path with comprehensive file checking
        /// Based on SkinDiscoveryService validation requirements
        /// </summary>
        /// <param name="skinPath">Path to validate</param>
        /// <param name="requiredFiles">List of required files to check</param>
        /// <returns>True if all required files exist</returns>
        public static bool IsValidSkinPath(string skinPath, string[] requiredFiles)
        {
            if (!IsValidDirectory(skinPath) || requiredFiles == null)
                return false;

            return requiredFiles.All(file => File.Exists(Path.Combine(skinPath, file)));
        }

        /// <summary>
        /// Get missing files from skin validation
        /// </summary>
        /// <param name="skinPath">Path to check</param>
        /// <param name="requiredFiles">List of required files</param>
        /// <returns>List of missing files</returns>
        public static string[] GetMissingSkinFiles(string skinPath, string[] requiredFiles)
        {
            if (!IsValidDirectory(skinPath) || requiredFiles == null)
                return requiredFiles ?? Array.Empty<string>();

            return requiredFiles.Where(file => !File.Exists(Path.Combine(skinPath, file))).ToArray();
        }

        #endregion

        #region File Validation

        /// <summary>
        /// Validate if a file exists and is accessible
        /// </summary>
        /// <param name="filePath">Path to validate</param>
        /// <returns>True if file exists and is accessible</returns>
        public static bool IsValidFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            try
            {
                return File.Exists(filePath);
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
