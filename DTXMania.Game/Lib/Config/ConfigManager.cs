#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Game.Lib.Config
{
    public class ConfigManager : IConfigManager
    {
        private static readonly InputCommandType[] RequiredSystemCommands =
        {
            InputCommandType.MoveUp,
            InputCommandType.MoveDown,
            InputCommandType.MoveLeft,
            InputCommandType.MoveRight,
            InputCommandType.Activate,
            InputCommandType.Back,
        };

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
            foreach (var lane in Config.UnboundDrumLanes.OrderBy(lane => lane))
            {
                keyBindings.UnbindLane(lane);
            }

            foreach (var buttonId in Config.UnboundDrumButtons.OrderBy(buttonId => buttonId, StringComparer.Ordinal))
            {
                keyBindings.UnbindButton(buttonId);
            }

            foreach (var kvp in Config.KeyBindings)
            {
                keyBindings.BindButton(kvp.Key, kvp.Value);
            }
        }

        public void SaveKeyBindings(KeyBindings keyBindings)
        {
            ArgumentNullException.ThrowIfNull(keyBindings);
            Config.KeyBindings.Clear();
            Config.UnboundDrumLanes.Clear();
            Config.UnboundDrumButtons.Clear();
            foreach (var kvp in keyBindings.ButtonToLane)
            {
                Config.KeyBindings[kvp.Key] = kvp.Value;
            }

            for (int lane = 0; lane < 10; lane++)
            {
                if (!keyBindings.GetButtonsForLane(lane).Any(KeyBindings.IsKeyboardButtonId))
                {
                    Config.UnboundDrumLanes.Add(lane);
                }
            }

            foreach (var buttonId in GetExplicitlyUnboundDefaultDrumButtons(keyBindings))
            {
                Config.UnboundDrumButtons.Add(buttonId);
            }
        }

        public InputManager CreateConfiguredInputManager()
        {
            var inputManager = new InputManager();
            LoadSystemKeyBindings(inputManager);
            return inputManager;
        }

        public void LoadSystemKeyBindings(InputManager inputManager)
        {
            const string prefix = "SystemKey.";
            var drumKeys = GetConfiguredDrumKeyboardKeys();
            foreach (var kvp in Config.SystemKeyBindings)
            {
                // Key format: "SystemKey.MoveUp", value format: "Up"
                if (string.IsNullOrEmpty(kvp.Key) ||
                    !kvp.Key.StartsWith(prefix, StringComparison.Ordinal) ||
                    kvp.Key.Length <= prefix.Length)
                    continue;

                var suffix = kvp.Key.Substring(prefix.Length);
                if (!Enum.TryParse<InputCommandType>(suffix, true, out var command))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(kvp.Value))
                {
                    if (IsRequiredSystemCommand(command))
                    {
                        EnsureRequiredSystemKeyBinding(inputManager, command);
                        continue;
                    }

                    RemoveSystemKeyBinding(inputManager, command);
                    continue;
                }

                var keys = ParseSystemBindingKeys(kvp.Value)
                    .Where(key => !drumKeys.Contains(key))
                    .Distinct()
                    .ToList();
                if (keys.Count == 0)
                {
                    if (IsRequiredSystemCommand(command))
                    {
                        EnsureRequiredSystemKeyBinding(inputManager, command);
                    }

                    continue;
                }

                RemoveSystemKeyBinding(inputManager, command);
                foreach (var key in keys)
                {
                    inputManager.AddKeyMapping(key, command);
                }
            }

            EnsureRequiredSystemKeyBindings(inputManager);
        }

        public void SaveSystemKeyBindings(InputManager inputManager)
        {
            var snapshot = inputManager.GetKeyMappingSnapshot();
            SaveSystemKeyBindings(snapshot);
        }

        public void SaveSystemKeyBindings(IReadOnlyDictionary<Keys, InputCommandType> workingBindings)
        {
            var existingBindings = new Dictionary<string, string>(Config.SystemKeyBindings);
            Config.SystemKeyBindings.Clear();
            foreach (var command in Enum.GetValues<InputCommandType>())
            {
                var configKey = $"SystemKey.{command}";
                var keys = workingBindings
                    .Where(kvp => kvp.Value == command)
                    .Select(kvp => kvp.Key.ToString())
                    .ToArray();

                if (keys.Length > 0)
                {
                    Config.SystemKeyBindings[configKey] = string.Join(",", keys);
                    continue;
                }

                if (IsRequiredSystemCommand(command))
                {
                    if (existingBindings.TryGetValue(configKey, out var existingValue) &&
                        !string.IsNullOrWhiteSpace(existingValue))
                    {
                        Config.SystemKeyBindings[configKey] = existingValue;
                    }
                    else
                    {
                        Config.SystemKeyBindings[configKey] = string.Join(",", GetFallbackSystemBindingKeys(command).Select(key => key.ToString()));
                    }

                    continue;
                }

                Config.SystemKeyBindings[configKey] = string.Empty;
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
                    if (key.StartsWith("Key.Unbound.") &&
                        int.TryParse(key.Substring("Key.Unbound.".Length), out var unboundLane))
                    {
                        if (unboundLane >= 0 && unboundLane <= 9 &&
                            TryParseBool(value, out var isUnbound) &&
                            isUnbound)
                        {
                            Config.UnboundDrumLanes.Add(unboundLane);
                        }
                    }
                    else if (key.StartsWith("Key.UnboundButton.", StringComparison.Ordinal))
                    {
                        var buttonId = key.Substring("Key.UnboundButton.".Length);
                        if (IsSupportedButtonBindingKey(buttonId) &&
                            TryParseBool(value, out var isUnboundButton) &&
                            isUnboundButton)
                        {
                            Config.UnboundDrumButtons.Add(buttonId);
                        }
                    }
                    else if (IsSupportedButtonBindingKey(key) && int.TryParse(value, out var lane))
                    {
                        if (lane >= 0 && lane <= 9)
                        {
                            Config.KeyBindings[key] = lane;
                        }
                    }
                    else if (key.StartsWith("SystemKey."))
                    {
                        Config.SystemKeyBindings[key] = value;
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
            if (Config.KeyBindings.Count > 0 || Config.UnboundDrumLanes.Count > 0 || Config.UnboundDrumButtons.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[KeyBindings]");
                foreach (var kvp in Config.KeyBindings)
                {
                    sb.AppendLine($"{kvp.Key}={kvp.Value}");
                }
                foreach (var lane in Config.UnboundDrumLanes.OrderBy(lane => lane))
                {
                    sb.AppendLine($"Key.Unbound.{lane}=true");
                }
                foreach (var buttonId in Config.UnboundDrumButtons.OrderBy(buttonId => buttonId, StringComparer.Ordinal))
                {
                    sb.AppendLine($"Key.UnboundButton.{buttonId}=true");
                }
            }

            if (Config.SystemKeyBindings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[SystemKeyBindings]");
                foreach (var kvp in Config.SystemKeyBindings)
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

        private static void RemoveSystemKeyBinding(InputManager inputManager, InputCommandType command)
        {
            var keysToRemove = inputManager.GetKeyMappingSnapshot()
                .Where(kvp => kvp.Value == command)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                inputManager.RemoveKeyMapping(key);
            }
        }

        private static List<Keys> ParseSystemBindingKeys(string rawValue)
        {
            var keys = new List<Keys>();
            foreach (var token in rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Enum.TryParse<Keys>(token, true, out var key))
                {
                    keys.Add(key);
                }
            }

            return keys;
        }

        private HashSet<Keys> GetConfiguredDrumKeyboardKeys()
        {
            var keyBindings = new KeyBindings();
            LoadKeyBindings(keyBindings);

            return keyBindings.ButtonToLane.Keys
                .Select(ParseKeyboardButtonId)
                .Where(key => key.HasValue)
                .Select(key => key!.Value)
                .ToHashSet();
        }

        private static Keys? ParseKeyboardButtonId(string buttonId)
        {
            const string prefix = "Key.";
            if (string.IsNullOrWhiteSpace(buttonId) ||
                !buttonId.StartsWith(prefix, StringComparison.Ordinal) ||
                buttonId.Length <= prefix.Length)
            {
                return null;
            }

            var keyName = buttonId.Substring(prefix.Length);
            return Enum.TryParse<Keys>(keyName, true, out var key) ? key : null;
        }

        private static bool IsSupportedButtonBindingKey(string key)
        {
            return !string.IsNullOrWhiteSpace(key)
                && (key.StartsWith("Key.", StringComparison.Ordinal)
                    || key.StartsWith("MIDI.", StringComparison.Ordinal)
                    || key.StartsWith("Pad.", StringComparison.Ordinal));
        }

        private static IEnumerable<string> GetExplicitlyUnboundDefaultDrumButtons(KeyBindings keyBindings)
        {
            var defaultBindings = new KeyBindings();
            var currentButtons = keyBindings.ButtonToLane.Keys.ToHashSet(StringComparer.Ordinal);
            var explicitlyUnboundButtons = new HashSet<string>(StringComparer.Ordinal);

            for (var lane = 0; lane < 10; lane++)
            {
                if (!keyBindings.GetButtonsForLane(lane).Any(KeyBindings.IsKeyboardButtonId))
                {
                    continue;
                }

                foreach (var defaultButtonId in defaultBindings.GetButtonsForLane(lane).Where(KeyBindings.IsKeyboardButtonId))
                {
                    if (!currentButtons.Contains(defaultButtonId))
                    {
                        explicitlyUnboundButtons.Add(defaultButtonId);
                    }
                }
            }

            return explicitlyUnboundButtons;
        }

        private static bool IsRequiredSystemCommand(InputCommandType command)
        {
            return RequiredSystemCommands.Contains(command);
        }

        private static InputCommandType[] GetRequiredSystemCommands()
        {
            return RequiredSystemCommands;
        }

        private static bool HasSystemKeyBinding(InputManager inputManager, InputCommandType command)
        {
            return inputManager.GetKeyMappingSnapshot().Values.Contains(command);
        }

        private static void EnsureRequiredSystemKeyBindings(InputManager inputManager)
        {
            for (var pass = 0; pass < RequiredSystemCommands.Length; pass++)
            {
                var missingCommands = GetRequiredSystemCommands()
                    .Where(command => !HasSystemKeyBinding(inputManager, command))
                    .ToList();
                if (missingCommands.Count == 0)
                {
                    return;
                }

                foreach (var command in missingCommands)
                {
                    EnsureRequiredSystemKeyBinding(inputManager, command);
                }
            }
        }

        private static void EnsureRequiredSystemKeyBinding(InputManager inputManager, InputCommandType command)
        {
            if (HasSystemKeyBinding(inputManager, command))
            {
                return;
            }

            foreach (var key in GetFallbackSystemBindingKeys(command))
            {
                inputManager.AddKeyMapping(key, command);
            }
        }

        private static Keys[] GetFallbackSystemBindingKeys(InputCommandType command)
        {
            return command switch
            {
                InputCommandType.MoveUp => new[] { Keys.Up },
                InputCommandType.MoveDown => new[] { Keys.Down },
                InputCommandType.MoveLeft => new[] { Keys.Left },
                InputCommandType.MoveRight => new[] { Keys.Right },
                InputCommandType.Activate => new[] { Keys.Enter },
                InputCommandType.Back => new[] { Keys.Escape },
                _ => Array.Empty<Keys>(),
            };
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

        private static string NormalizePathForComparison(string path)
        {
            return path.Trim().Replace('\\', '/').TrimEnd('/');
        }

        private static bool IsLegacyDefaultSongsPath(string? path, string defaultSongsPath)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var normalized = NormalizePathForComparison(path);
            var legacyDefaultSongsPath = NormalizePathForComparison(
                Path.Combine(Path.GetDirectoryName(defaultSongsPath) ?? string.Empty, "Songs"));

            // Only match the specific legacy defaults, not every path ending in "Songs".
            return string.Equals(normalized, legacyDefaultSongsPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "Songs", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "./Songs", StringComparison.OrdinalIgnoreCase);
        }

        private void NormalizeConfigPaths()
        {
            var defaultSystemSkinRoot = AppPaths.GetDefaultSystemSkinRoot();
            var defaultSongsPath = AppPaths.GetDefaultSongsPath();
            var shouldMigrateLegacySongsPath = IsLegacyDefaultSongsPath(Config.DTXPath, defaultSongsPath);

            if (shouldMigrateLegacySongsPath)
            {
                _logger.LogInformation(
                    "Migrating legacy DTXPath '{LegacyPath}' to '{DefaultSongsPath}'",
                    Config.DTXPath,
                    defaultSongsPath);
            }

            // Honor configured paths first, fallback to defaults if not set
            Config.SystemSkinRoot = AppPaths.ResolvePathOrDefault(Config.SystemSkinRoot, defaultSystemSkinRoot);
            Config.DTXPath = shouldMigrateLegacySongsPath
                ? defaultSongsPath
                : AppPaths.ResolvePathOrDefault(Config.DTXPath, defaultSongsPath);
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
