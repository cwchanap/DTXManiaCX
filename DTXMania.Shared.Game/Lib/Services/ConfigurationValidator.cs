using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using DTX.Config;

namespace DTX.Services
{
    /// <summary>
    /// Service for validating DTXMania configuration
    /// Based on DTXManiaNX configuration validation patterns
    /// </summary>
    public class ConfigurationValidator
    {
        #region Private Fields

        private readonly List<ValidationResult> _validationResults = new();

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the validation results from the last validation run
        /// </summary>
        public IReadOnlyList<ValidationResult> ValidationResults => _validationResults.AsReadOnly();

        /// <summary>
        /// Gets whether the last validation passed without errors
        /// </summary>
        public bool IsValid => _validationResults.TrueForAll(r => r.Severity != ValidationSeverity.Error);

        #endregion

        #region Public Methods

        /// <summary>
        /// Validate the provided configuration
        /// </summary>
        /// <param name="config">Configuration to validate</param>
        /// <returns>True if validation passed, false if there are errors</returns>
        public bool ValidateConfiguration(ConfigData config)
        {
            _validationResults.Clear();

            Debug.WriteLine("ConfigurationValidator: Starting configuration validation");

            try
            {
                // Validate system settings
                ValidateSystemSettings(config);

                // Validate skin settings
                ValidateSkinSettings(config);

                // Validate display settings
                ValidateDisplaySettings(config);

                // Validate sound settings
                ValidateSoundSettings(config);

                // Validate game settings
                ValidateGameSettings(config);

                // Validate paths
                ValidatePaths(config);

                Debug.WriteLine($"ConfigurationValidator: Validation complete. {_validationResults.Count} issues found");

                // Log validation results
                foreach (var result in _validationResults)
                {
                    var severityText = result.Severity switch
                    {
                        ValidationSeverity.Error => "ERROR",
                        ValidationSeverity.Warning => "WARNING",
                        ValidationSeverity.Info => "INFO",
                        _ => "UNKNOWN"
                    };
                    Debug.WriteLine($"ConfigurationValidator: {severityText}: {result.Message}");
                }

                return IsValid;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ConfigurationValidator: Exception during validation: {ex.Message}");
                AddResult(ValidationSeverity.Error, "Configuration validation failed due to exception", "System");
                return false;
            }
        }

        /// <summary>
        /// Get validation results for a specific category
        /// </summary>
        /// <param name="category">Category to filter by</param>
        /// <returns>Validation results for the category</returns>
        public IEnumerable<ValidationResult> GetResultsByCategory(string category)
        {
            return _validationResults.Where(r =>
                r.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region Private Methods - Validation

        private void ValidateSystemSettings(ConfigData config)
        {
            // Validate DTXMania version
            if (string.IsNullOrEmpty(config.DTXManiaVersion))
            {
                AddResult(ValidationSeverity.Warning, "DTXMania version not specified", "System");
            }

            // Validate version format
            if (!string.IsNullOrEmpty(config.DTXManiaVersion) &&
                !config.DTXManiaVersion.Contains("NX") && !config.DTXManiaVersion.Contains("MG"))
            {
                AddResult(ValidationSeverity.Info, "Non-standard DTXMania version format", "System");
            }
        }

        private void ValidateSkinSettings(ConfigData config)
        {
            // Validate skin path
            if (string.IsNullOrEmpty(config.SkinPath))
            {
                AddResult(ValidationSeverity.Error, "Skin path not specified", "Skin");
            }
            else if (!Directory.Exists(config.SkinPath))
            {
                AddResult(ValidationSeverity.Warning, $"Skin directory not found: {config.SkinPath}", "Skin");
            }

            // Validate system skin root
            if (string.IsNullOrEmpty(config.SystemSkinRoot))
            {
                AddResult(ValidationSeverity.Error, "System skin root not specified", "Skin");
            }
            else if (!Directory.Exists(config.SystemSkinRoot))
            {
                AddResult(ValidationSeverity.Warning, $"System skin root not found: {config.SystemSkinRoot}", "Skin");
            }

            // Validate last used skin
            if (string.IsNullOrEmpty(config.LastUsedSkin))
            {
                AddResult(ValidationSeverity.Info, "Last used skin not specified, will use default", "Skin");
            }
        }

        private void ValidateDisplaySettings(ConfigData config)
        {
            // Validate screen resolution
            if (config.ScreenWidth <= 0 || config.ScreenHeight <= 0)
            {
                AddResult(ValidationSeverity.Error, "Invalid screen resolution", "Display");
            }
            else if (config.ScreenWidth < 800 || config.ScreenHeight < 600)
            {
                AddResult(ValidationSeverity.Warning, "Screen resolution may be too low for optimal experience", "Display");
            }

            // Check for common resolutions
            var commonResolutions = new[] { (1280, 720), (1920, 1080), (1366, 768), (1024, 768) };
            if (!commonResolutions.Any(res => res.Item1 == config.ScreenWidth && res.Item2 == config.ScreenHeight))
            {
                AddResult(ValidationSeverity.Info, "Using non-standard screen resolution", "Display");
            }
        }

        private void ValidateSoundSettings(ConfigData config)
        {
            // Validate volume levels
            if (config.MasterVolume < 0 || config.MasterVolume > 100)
            {
                AddResult(ValidationSeverity.Error, "Master volume out of range (0-100)", "Sound");
            }

            if (config.BGMVolume < 0 || config.BGMVolume > 100)
            {
                AddResult(ValidationSeverity.Error, "BGM volume out of range (0-100)", "Sound");
            }

            if (config.SEVolume < 0 || config.SEVolume > 100)
            {
                AddResult(ValidationSeverity.Error, "SE volume out of range (0-100)", "Sound");
            }

            // Validate buffer size
            if (config.BufferSizeMs <= 0)
            {
                AddResult(ValidationSeverity.Error, "Invalid buffer size", "Sound");
            }
            else if (config.BufferSizeMs < 50 || config.BufferSizeMs > 500)
            {
                AddResult(ValidationSeverity.Warning, "Buffer size may cause audio issues", "Sound");
            }
        }

        private void ValidateGameSettings(ConfigData config)
        {
            // Validate scroll speed
            if (config.ScrollSpeed <= 0)
            {
                AddResult(ValidationSeverity.Error, "Invalid scroll speed", "Game");
            }
            else if (config.ScrollSpeed < 50 || config.ScrollSpeed > 2000)
            {
                AddResult(ValidationSeverity.Warning, "Scroll speed may be too extreme", "Game");
            }

            // Validate key bindings
            if (config.KeyBindings == null)
            {
                AddResult(ValidationSeverity.Warning, "Key bindings not initialized", "Game");
            }
        }

        private void ValidatePaths(ConfigData config)
        {
            // Validate DTX path
            if (string.IsNullOrEmpty(config.DTXPath))
            {
                AddResult(ValidationSeverity.Warning, "DTX path not specified", "Paths");
            }
            else if (!Directory.Exists(config.DTXPath))
            {
                AddResult(ValidationSeverity.Info, $"DTX directory will be created: {config.DTXPath}", "Paths");
            }
        }

        private void AddResult(ValidationSeverity severity, string message, string category)
        {
            _validationResults.Add(new ValidationResult
            {
                Severity = severity,
                Message = message,
                Category = category,
                Timestamp = DateTime.Now
            });
        }

        #endregion
    }

    #region Supporting Classes

    /// <summary>
    /// Result of a configuration validation check
    /// </summary>
    public class ValidationResult
    {
        public ValidationSeverity Severity { get; set; }
        public string Message { get; set; } = "";
        public string Category { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Severity levels for validation results
    /// </summary>
    public enum ValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    #endregion
}
