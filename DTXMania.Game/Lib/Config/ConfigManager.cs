#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTXMania.Game.Lib.Config
{
    public class ConfigManager : IConfigManager
    {
        private readonly ILogger<ConfigManager> _logger;
        public ConfigData Config { get; private set; }

        public ConfigManager(ILogger<ConfigManager>? logger = null)
        {
            _logger = logger ?? NullLogger<ConfigManager>.Instance;
            Config = new ConfigData();
        }

        public void LoadConfig(string filePath)
        {
            EnsureConfigDirectory(filePath);
            if (!File.Exists(filePath))
            {
                NormalizeConfigPaths();
                SaveConfig(filePath); // Create default config
                return;
            }

            var lines = File.ReadAllLines(filePath, Encoding.UTF8);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';'))
                    continue;

                var parts = line.Split('=');
                if (parts.Length != 2)
                    continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                ParseConfigLine(key, value);
            }

            NormalizeConfigPaths();

            // Security: If Game API is enabled but no API key is set, generate one and save
            if (Config.EnableGameApi && string.IsNullOrEmpty(Config.GameApiKey))
            {
                var previousApiKey = Config.GameApiKey;
                var generatedApiKey = GenerateSecureApiKey();
                Config.GameApiKey = generatedApiKey;

                try
                {
                    SaveConfig(filePath);
                    _logger.LogInformation("Generated a new API key for Game API and saved it to the config file.");
                }
                catch (Exception ex)
                {
                    Config.GameApiKey = previousApiKey;
                    _logger.LogError(ex, "Failed to save generated Game API key to config file: {ErrorMessage}", ex.Message);
                    return;
                }
            }
        }

        /// <summary>
        /// Generates a cryptographically secure random API key.
        /// </summary>
        /// <returns>A 32-character hex string API key</returns>
        private static string GenerateSecureApiKey()
        {
            // Generate 16 random bytes (128 bits of entropy) and convert to hex string
            var randomBytes = RandomNumberGenerator.GetBytes(16);
            return Convert.ToHexString(randomBytes).ToLowerInvariant();
        }

        public void LoadKeyBindings(KeyBindings keyBindings)
        {
            // Load key bindings from config data
            foreach (var kvp in Config.KeyBindings)
            {
                keyBindings.BindButton(kvp.Key, kvp.Value);
            }
        }

        public void SaveKeyBindings(KeyBindings keyBindings)
        {
            // Save key bindings to config data
            // Create a temporary set of default bindings to compare against
            var defaultBindings = new DTXMania.Game.Lib.Input.KeyBindings();
            defaultBindings.LoadDefaultBindings();
            
            Config.KeyBindings.Clear();
            foreach (var kvp in keyBindings.ButtonToLane)
            {
                // Only save non-default bindings to config
                if (!defaultBindings.ButtonToLane.TryGetValue(kvp.Key, out int defaultValue) || defaultValue != kvp.Value)
                {
                    Config.KeyBindings[kvp.Key] = kvp.Value;
                }
            }
        }

        private void ParseConfigLine(string key, string value)
        {
            switch (key)
            {
                case "DTXManiaVersion":
                    Config.DTXManiaVersion = value;
                    break;
                case "SkinPath":
                    Config.SkinPath = value;
                    break;
                case "DTXPath":
                    Config.DTXPath = value;
                    break;
                case "UseBoxDefSkin":
                    Config.UseBoxDefSkin = value.ToLower() == "true";
                    break;
                case "SystemSkinRoot":
                    Config.SystemSkinRoot = value;
                    break;
                case "LastUsedSkin":
                    Config.LastUsedSkin = value;
                    break;
                case "ScreenWidth":
                    if (int.TryParse(value, out var width))
                        Config.ScreenWidth = width;
                    break;
                case "ScreenHeight":
                    if (int.TryParse(value, out var height))
                        Config.ScreenHeight = height;
                    break;
                case "FullScreen":
                    Config.FullScreen = value.ToLower() == "true";
                    break;
                case "VSyncWait":
                    Config.VSyncWait = value.ToLower() == "true";
                    break;
                case "ScrollSpeed":
                    if (int.TryParse(value, out var scrollSpeed))
                        Config.ScrollSpeed = scrollSpeed;
                    break;
                case "AutoPlay":
                    if (TryParseBool(value, out var autoPlay))
                        Config.AutoPlay = autoPlay;
                    break;
                case "NoFail":
                    if (TryParseBool(value, out var noFail))
                        Config.NoFail = noFail;
                    break;
                case "EnableGameApi":
                    if (TryParseBool(value, out var enableGameApi))
                        Config.EnableGameApi = enableGameApi;
                    break;
                case "GameApiPort":
                    if (int.TryParse(value, out var apiPort))
                        Config.GameApiPort = apiPort;
                    break;
                case "GameApiKey":
                    Config.GameApiKey = value;
                    break;
                // Handle key bindings from config file
                default:
                    if (key.StartsWith("Key.") && int.TryParse(value, out var lane))
                    {
                        if (lane >= 0 && lane <= 9)
                        {
                            Config.KeyBindings[key] = lane;
                        }
                    }
                    break;
            }
        }

        public void SaveConfig(string filePath)
        {
            EnsureConfigDirectory(filePath);
            var sb = new StringBuilder();
            sb.AppendLine("; DTXMania Configuration File");
            sb.AppendLine($"; Generated: {DateTime.Now}");
            sb.AppendLine();

            sb.AppendLine("[System]");
            sb.AppendLine($"DTXManiaVersion={Config.DTXManiaVersion}");
            sb.AppendLine($"SkinPath={Config.SkinPath}");
            sb.AppendLine($"DTXPath={Config.DTXPath}");
            sb.AppendLine();

            sb.AppendLine("[Skin]");
            sb.AppendLine($"UseBoxDefSkin={Config.UseBoxDefSkin}");
            sb.AppendLine($"SystemSkinRoot={Config.SystemSkinRoot}");
            sb.AppendLine($"LastUsedSkin={Config.LastUsedSkin}");
            sb.AppendLine();
            
            sb.AppendLine("[Display]");
            sb.AppendLine($"ScreenWidth={Config.ScreenWidth}");
            sb.AppendLine($"ScreenHeight={Config.ScreenHeight}");
            sb.AppendLine($"FullScreen={Config.FullScreen}");
            sb.AppendLine($"VSyncWait={Config.VSyncWait}");
            sb.AppendLine();
            
            sb.AppendLine("[Game]");
            sb.AppendLine($"ScrollSpeed={Config.ScrollSpeed}");
            sb.AppendLine($"AutoPlay={Config.AutoPlay}");
            sb.AppendLine($"NoFail={Config.NoFail}");

            sb.AppendLine();
            sb.AppendLine("[Api]");
            sb.AppendLine($"EnableGameApi={Config.EnableGameApi}");
            sb.AppendLine($"GameApiPort={Config.GameApiPort}");
            sb.AppendLine($"GameApiKey={Config.GameApiKey}");

            // Save key bindings to config file
            if (Config.KeyBindings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[KeyBindings]");
                foreach (var kvp in Config.KeyBindings)
                {
                    sb.AppendLine($"{kvp.Key}={kvp.Value}");
                }
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        public void ResetToDefaults()
        {
            Config = new ConfigData();
        }
        
        /// <summary>
        /// Helper method for robust boolean parsing
        /// </summary>
        private static bool TryParseBool(string value, out bool result)
        {
            result = false;
            if (string.IsNullOrEmpty(value))
                return false;
                
            var trimmed = value.Trim().ToLowerInvariant();
            if (trimmed == "true" || trimmed == "1" || trimmed == "yes" || trimmed == "on")
            {
                result = true;
                return true;
            }
            if (trimmed == "false" || trimmed == "0" || trimmed == "no" || trimmed == "off")
            {
                result = false;
                return true;
            }
            return false;
        }

        private static void EnsureConfigDirectory(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory))
                return;

            Directory.CreateDirectory(directory);
        }

        private void NormalizeConfigPaths()
        {
            var defaultSystemSkinRoot = AppPaths.GetDefaultSystemSkinRoot();
            var defaultSongsPath = AppPaths.GetDefaultSongsPath();

            // Migration: If the old relative default "Songs" is used, migrate to the new absolute default path (DTXFiles under AppData)
            // Only match explicit legacy values to avoid replacing user paths like "D:\Games\CustomSongs"
            // Trim trailing separators to handle variants like "Songs/", "Songs\\", "./Songs/", etc.
            var trimmedDtxPath = Config.DTXPath?.TrimEnd('/', '\\');
            if (!string.IsNullOrWhiteSpace(trimmedDtxPath) &&
                (trimmedDtxPath.Equals("Songs", StringComparison.OrdinalIgnoreCase) ||
                 trimmedDtxPath.Equals("./Songs", StringComparison.OrdinalIgnoreCase) ||
                 trimmedDtxPath.Equals(".\\Songs", StringComparison.OrdinalIgnoreCase)))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ConfigManager: Migrating DTXPath from legacy '{Config.DTXPath}' to '{defaultSongsPath}'");
                Config.DTXPath = defaultSongsPath;
            }

            Config.SystemSkinRoot = AppPaths.ResolvePathOrDefault(Config.SystemSkinRoot, defaultSystemSkinRoot);
            Config.DTXPath = AppPaths.ResolvePathOrDefault(Config.DTXPath, defaultSongsPath);
            Config.SkinPath = AppPaths.ResolvePathOrDefault(Config.SkinPath, Config.SystemSkinRoot);

            void EnsureDirectorySafe(string path)
            {
                try
                {
                    AppPaths.EnsureDirectory(path);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to ensure directory for {Path}", path);
                }
            }

            EnsureDirectorySafe(Config.SystemSkinRoot);
            EnsureDirectorySafe(Config.DTXPath);
        }
    }
}
