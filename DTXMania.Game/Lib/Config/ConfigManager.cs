#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.KeyAssign;
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

        private static bool IsRequiredSystemCommand(InputCommandType command)
        {
            return KeyConflictChecker.IsRequiredCommand(command);
        }

        private static InputCommandType[] GetRequiredSystemCommands()
        {
            return RequiredSystemCommands;
        }

        private readonly ILogger<ConfigManager> _logger;
        public ConfigData Config { get; private set; }

        /// <summary>
        /// Path of the config file that has a deferred (debounced) write pending.
        /// Null when no write is pending.
        /// </summary>
        private string? _pendingSavePath;

        /// <summary>
        /// Path captured in <see cref="LoadConfig"/> so setters can mark dirty without a path arg.
        /// </summary>
        private string? _loadedConfigPath;

        public ConfigManager(ILogger<ConfigManager>? logger = null)
        {
            _logger = logger ?? NullLogger<ConfigManager>.Instance;
            Config = new ConfigData();
        }

        public void LoadConfig(string filePath)
        {
            _loadedConfigPath = filePath;
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
                    else
                    {
                        RemoveSystemKeyBinding(inputManager, command);
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
            EvictDrumKeyConflicts(inputManager, drumKeys);
        }

        /// <summary>
        /// Removes non-required system key bindings that collide with drum keys,
        /// so that a key used for gameplay does not also fire a system command
        /// (e.g. scroll-speed adjust) during performance.
        /// Required commands are never evicted; their fallback logic in
        /// <see cref="EnsureRequiredSystemKeyBindings"/> already handles this.
        /// </summary>
        private void EvictDrumKeyConflicts(InputManager inputManager, HashSet<Keys> drumKeys)
        {
            var snapshot = inputManager.GetKeyMappingSnapshot();
            foreach (var kvp in snapshot)
            {
                if (!drumKeys.Contains(kvp.Key))
                    continue;

                if (IsRequiredSystemCommand(kvp.Value))
                    continue;

                _logger.LogDebug("Evicting system binding: {Key} -> {Command} (conflicts with drum key)", kvp.Key, kvp.Value);
                inputManager.RemoveKeyMapping(kvp.Key);
            }
        }

        public void SaveSystemKeyBindings(InputManager inputManager)
        {
            var snapshot = inputManager.GetKeyMappingSnapshot();
            ApplySystemKeyBindings(snapshot);
        }

        private void ApplySystemKeyBindings(IReadOnlyDictionary<Keys, InputCommandType> workingBindings)
        {
            var existingBindings = new Dictionary<string, string>(Config.SystemKeyBindings);
            // Drum keys claim their physical key for gameplay; a system binding that points at a
            // drum key is stale (the runtime would filter it out on load anyway). Filtering here
            // prevents persisting such entries, which could otherwise be resurrected later when
            // the drum key is unbound. Mirrors the drum-key filtering in LoadSystemKeyBindings.
            var drumKeys = GetConfiguredDrumKeyboardKeys();
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
                    // Preserve the user's prior binding for this required command, but drop any
                    // keys that are now drum keys. If nothing survives, fall back to the default
                    // so a required command is never left pointing at a drum key.
                    var preserved = existingBindings.TryGetValue(configKey, out var existingValue) &&
                        !string.IsNullOrWhiteSpace(existingValue)
                        ? ParseSystemBindingKeys(existingValue).Where(k => !drumKeys.Contains(k)).ToList()
                        : new List<Keys>();

                    if (preserved.Count > 0)
                    {
                        Config.SystemKeyBindings[configKey] = string.Join(",", preserved.Select(key => key.ToString()));
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
                        Config.ScrollSpeed = ScrollSpeedRange.SnapAndClamp(scrollSpeed);
                    break;
                case "AutoPlay":
                    if (TryParseBool(value, out var autoPlay))
                        Config.AutoPlay = autoPlay;
                    break;
                case "NoFail":
                    if (TryParseBool(value, out var noFail))
                        Config.NoFail = noFail;
                    break;
                case "AudioLatencyOffsetMs":
                    if (int.TryParse(value, out var audioLatencyOffsetMs))
                        Config.AudioLatencyOffsetMs = Math.Max(0, audioLatencyOffsetMs);
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
            sb.AppendLine($"AudioLatencyOffsetMs={Config.AudioLatencyOffsetMs}");

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

            // Write atomically via temp-file to avoid truncation on crash/disk-full.
            // Write to temp first — if it fails, original remains intact.
            var tempFile = filePath + ".tmp";
            File.WriteAllText(tempFile, sb.ToString(), Encoding.UTF8);
            File.Move(tempFile, filePath, overwrite: true);
        }

        public void ResetToDefaults()
        {
            Config = new ConfigData();
        }

        public event EventHandler<ScrollSpeedChangedEventArgs>? ScrollSpeedChanged;

        public event EventHandler<EventArgs>? KeyBindingsChanged;

        public event EventHandler<EventArgs>? SystemKeyBindingsChanged;

        public void SetScrollSpeed(string configFilePath, int percent)
        {
            var snapped = ScrollSpeedRange.SnapAndClamp(percent);
            var old = Config.ScrollSpeed;
            if (snapped == old)
                return;

            Config.ScrollSpeed = snapped;

            // Defer disk write — mark dirty and flush later via FlushPendingSave.
            MarkDirty(configFilePath);

            RaiseEvent(ScrollSpeedChanged, new ScrollSpeedChangedEventArgs(old, snapped));
        }

        public void AdjustScrollSpeed(string configFilePath, int stepDelta)
        {
            SetScrollSpeed(configFilePath, Config.ScrollSpeed + stepDelta * ScrollSpeedRange.Step);
        }

        /// <summary>
        /// Writes <paramref name="keyBindings"/> into <see cref="Config"/>, marks the edit
        /// dirty for a deferred save, and raises <see cref="KeyBindingsChanged"/>.
        /// </summary>
        /// <remarks>
        /// Requires a prior <see cref="LoadConfig"/> call for the edit to be persisted;
        /// calling before LoadConfig mutates in-memory Config only.
        /// </remarks>
        public void SetKeyBindings(KeyBindings keyBindings)
        {
            SaveKeyBindings(keyBindings);
            MarkDirty();
            RaiseEvent(KeyBindingsChanged, EventArgs.Empty);
        }

        /// <summary>
        /// Writes <paramref name="workingBindings"/> into <see cref="Config"/>, marks the
        /// edit dirty for a deferred save, and raises
        /// <see cref="SystemKeyBindingsChanged"/>.
        /// </summary>
        /// <remarks>
        /// Requires a prior <see cref="LoadConfig"/> call for the edit to be persisted;
        /// calling before LoadConfig mutates in-memory Config only.
        /// </remarks>
        public void SetSystemKeyBindings(IReadOnlyDictionary<Keys, InputCommandType> workingBindings)
        {
            ApplySystemKeyBindings(workingBindings);
            MarkDirty();
            RaiseEvent(SystemKeyBindingsChanged, EventArgs.Empty);
        }

        /// <summary>Sets AutoPlay and marks a deferred save pending. No event raised.</summary>
        public void SetAutoPlay(bool value) { Config.AutoPlay = value; MarkDirty(); }

        /// <summary>Sets NoFail and marks a deferred save pending. No event raised.</summary>
        public void SetNoFail(bool value) { Config.NoFail = value; MarkDirty(); }

        /// <summary>Sets audio latency (<see cref="ConfigData.AudioLatencyOffsetMs"/>, in ms, clamped to &gt;= 0) and marks a deferred save pending. No event raised.</summary>
        public void SetAudioLatency(int value) { Config.AudioLatencyOffsetMs = Math.Max(0, value); MarkDirty(); }

        /// <summary>Sets resolution (width x height) and marks a deferred save pending. No event raised.</summary>
        public void SetResolution(int width, int height) { Config.ScreenWidth = width; Config.ScreenHeight = height; MarkDirty(); }

        /// <summary>Sets fullscreen (<see cref="ConfigData.FullScreen"/>) and marks a deferred save pending. No event raised.</summary>
        public void SetFullscreen(bool value) { Config.FullScreen = value; MarkDirty(); }

        /// <summary>Sets VSync (<see cref="ConfigData.VSyncWait"/>) and marks a deferred save pending. No event raised.</summary>
        public void SetVSync(bool value) { Config.VSyncWait = value; MarkDirty(); }

        /// <inheritdoc/>
        public void FlushPendingSave()
        {
            var path = _pendingSavePath;
            if (path == null)
                return;

            try
            {
                SaveConfig(path);
                _pendingSavePath = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist deferred config changes to {Path}; in-memory values are still up to date. Will retry on next flush.", path);
            }
        }

        /// <summary>
        /// Marks a deferred save as pending. A no-op if <paramref name="path"/> is null
        /// and no config path is known yet (i.e., before any <see cref="LoadConfig"/> call).
        /// </summary>
        private void MarkDirty(string? path = null)
        {
            _pendingSavePath = path ?? _loadedConfigPath ?? _pendingSavePath;
        }

        /// <summary>
        /// Raises <paramref name="handler"/> with per-subscriber try/catch so one bad
        /// listener cannot break the edit or roll back <see cref="Config"/>. <see cref="Config"/>
        /// stays the truth; a failing subscriber is logged and the remaining subscribers
        /// still receive the event. Matches the persist-on-edit design's error-handling contract.
        /// </summary>
        private void RaiseEvent<TArgs>(EventHandler<TArgs>? handler, TArgs args) where TArgs : EventArgs
        {
            if (handler == null)
                return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try
                {
                    ((EventHandler<TArgs>)subscriber)(this, args);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ConfigManager event subscriber {Subscriber} threw; Config remains the truth and other subscribers still fire.", subscriber.Target?.GetType().FullName ?? subscriber.Method.DeclaringType?.FullName);
                }
            }
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
