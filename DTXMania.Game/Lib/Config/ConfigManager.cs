#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Song;
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

        private const string MidiVelocityPrefix = "MidiVelocity.";
        private const string SongRootPrefix = "SongRoot.";
        private const string AutoPlayLanePrefix = "AutoPlay.";

        private enum SongRootsLoadSource
        {
            Indexed,
            LegacyDTXPath,
            ManagedDefault,
        }

        /// <summary>
        /// Logical token persisted in the config database (and, before
        /// HPA-190, the legacy Config.ini) to represent the bundled default
        /// skin. Resolved at runtime to the current validating bundled System
        /// root (see <see cref="ResolveSkinPath"/>) so the config survives
        /// application relocations — moving the .app bundle or portable folder
        /// to a different directory does not stale the persisted path.
        /// </summary>
        public const string DefaultSkinPathToken = "Default";

        private static bool IsRequiredSystemCommand(InputCommandType command)
        {
            return KeyConflictChecker.IsRequiredCommand(command);
        }

        private readonly ILogger<ConfigManager> _logger;
        private readonly SongRootPolicy _songRootPolicy;
        private readonly SqliteConfigStore _store;
        private readonly string _legacyIniPath;
        private readonly string _baseDir;
        public ConfigData Config { get; private set; }

        /// <summary>
        /// True once this instance has loaded the store (or created it for the
        /// first time). Guards the store's load-before-save ordering: the v1
        /// store would silently stamp schema v1 over a higher-version database,
        /// so no save may happen before a successful load/first-create.
        /// </summary>
        private bool _storeLoaded;

        /// <summary>
        /// Whether a deferred (debounced) SQLite save is pending. Cleared only
        /// after a successful store save.
        /// </summary>
        private bool _hasPendingSave;

        public ConfigManager(ILogger<ConfigManager>? logger = null)
            : this(
                AppPaths.GetConfigDatabasePath(),
                AppPaths.GetLegacyConfigFilePath(),
                songRootPolicy: null,
                baseDir: null,
                logger)
        {
        }

        /// <summary>
        /// Test seam owning the persistence inputs explicitly: the SQLite
        /// config database path, the legacy Config.ini import path, the song
        /// root validation policy, and the base directory bundled System skin
        /// roots resolve from. Required for parallel-safe unit tests that must
        /// not share the process-wide app-data root paths.
        /// </summary>
        internal ConfigManager(
            string databasePath,
            string legacyIniPath,
            SongRootPolicy? songRootPolicy = null,
            string? baseDir = null,
            ILogger<ConfigManager>? logger = null)
        {
            _store = new SqliteConfigStore(databasePath);
            _legacyIniPath = legacyIniPath;
            _songRootPolicy = songRootPolicy ?? SongRootPolicy.ForCurrentPlatform();
            _baseDir = baseDir ?? AppContext.BaseDirectory;
            _logger = logger ?? NullLogger<ConfigManager>.Instance;
            Config = new ConfigData();
        }

        /// <summary>
        /// Loads the configuration from the SQLite store (HPA-190). The store
        /// is authoritative: when the database exists it is loaded and the
        /// legacy Config.ini is ignored. When the database is absent but a
        /// legacy Config.ini exists, the INI is imported once into a newly
        /// created database (the INI file itself is left untouched). When
        /// neither exists, defaults are persisted to a newly created database.
        /// An invalid existing database fails loudly — there is no INI
        /// fallback.
        /// </summary>
        public void LoadConfig()
        {
            Config.MidiVelocityThresholds.Clear();
            Config.AutoPlayLanes.Clear();
            Config.SongRoots.Clear();
            var indexedSongRoots = new Dictionary<int, string>();
            string? legacyDtxPath = null;

            if (_store.Exists)
            {
                // Throws on unreadable/unsupported databases: fail loudly, no
                // INI fallback.
                foreach (var pair in _store.Load())
                {
                    ParseConfigLine(pair.Key, pair.Value, indexedSongRoots, ref legacyDtxPath);
                }

                _storeLoaded = true;
            }
            else if (File.Exists(_legacyIniPath))
            {
                ImportLegacyIni(indexedSongRoots, ref legacyDtxPath);
            }
            else
            {
                // First launch: persist the defaults so the database exists.
                NormalizeConfigPaths(_baseDir);
                CreateStoreSnapshot();
                return;
            }

            var songRootsLoadSource = FinalizeSongRoots(indexedSongRoots, legacyDtxPath);
            var songRootsMigrated = songRootsLoadSource != SongRootsLoadSource.Indexed;

            // Capture the pre-normalization SkinPath so we can detect whether
            // NormalizeConfigPaths migrated it (e.g. absolute bundled path →
            // "Default" token). Migration changes must be persisted immediately
            // — otherwise a relocation before the next setter-triggered save
            // would leave the stale absolute path stored, breaking the
            // token's relocation-survival guarantee.
            var skinPathBeforeNormalization = Config.SkinPath;

            // Capture the pre-normalization SongRoots so we can detect whether
            // NormalizeConfigPaths discarded malformed entries, replaced them
            // with resolved paths, or inserted the managed default. Those
            // corrections must be persisted alongside skin/SongRoot migrations
            // so a corrupted or stale SongRoot.* row does not survive a
            // relocation-triggered reload.
            var songRootsBeforeNormalization = Config.SongRoots.ToList();

            NormalizeConfigPaths(_baseDir, songRootsLoadSource == SongRootsLoadSource.LegacyDTXPath);

            var skinPathMigrated = !string.Equals(
                skinPathBeforeNormalization, Config.SkinPath,
                AppPaths.SkinPathComparison);

            var songRootsCorrected = !SongRootsSequenceEqual(
                songRootsBeforeNormalization, Config.SongRoots);

            // An import must always write its snapshot once (first create);
            // an existing store is only rewritten when a migration/correction
            // was applied.
            var mustPersist = !_storeLoaded || skinPathMigrated || songRootsMigrated || songRootsCorrected;
            if (mustPersist)
            {
                try
                {
                    if (_storeLoaded)
                    {
                        SaveSnapshotToStore();
                    }
                    else
                    {
                        CreateStoreSnapshot();
                    }

                    if (skinPathMigrated)
                    {
                        _logger.LogInformation(
                            "Migrated SkinPath from '{OldPath}' to '{NewPath}' and persisted the change to the config database.",
                            skinPathBeforeNormalization, Config.SkinPath);
                    }

                    if (songRootsMigrated)
                    {
                        _logger.LogInformation(
                            "Persisted SongRoot migration using {SongRootCount} configured root(s).",
                            Config.SongRoots.Count);
                    }

                    if (songRootsCorrected && !songRootsMigrated)
                    {
                        _logger.LogInformation(
                            "NormalizeConfigPaths corrected {SongRootCount} SongRoot entry/entries; persisting the updated values to the config database.",
                            Config.SongRoots.Count);
                    }
                }
                catch (Exception ex)
                {
                    if (!_storeLoaded)
                    {
                        // First create/import failed: continuing would run
                        // the game without its sole live store — MarkDirty
                        // refuses to schedule saves before a successful
                        // load/first-create, so every later edit would be
                        // silently lost. Fail loudly per the spec (same
                        // contract as an unreadable existing database).
                        throw;
                    }

                    // The store was already loaded: only this normalization
                    // correction failed. In-memory values are correct; mark
                    // the write pending so the next flush (or the shutdown
                    // save) retries it.
                    _hasPendingSave = true;
                    _logger.LogError(ex,
                        "Failed to persist configuration migration. In-memory values are correct; " +
                        "the database will be updated on the next save.");
                }
            }

            // Security: If Game API is enabled but no API key is set, generate one and save
            if (Config.EnableGameApi && string.IsNullOrEmpty(Config.GameApiKey))
            {
                var previousApiKey = Config.GameApiKey;
                var generatedApiKey = GenerateSecureApiKey();
                Config.GameApiKey = generatedApiKey;

                try
                {
                    if (_storeLoaded)
                    {
                        SaveSnapshotToStore();
                    }
                    else
                    {
                        CreateStoreSnapshot();
                    }

                    _logger.LogInformation("Generated a new API key for Game API and saved it to the config database.");
                }
                catch (Exception ex)
                {
                    Config.GameApiKey = previousApiKey;
                    _logger.LogError(ex, "Failed to save generated Game API key to the config database: {ErrorMessage}", ex.Message);
                    return;
                }
            }
        }

        /// <summary>
        /// Parses the legacy Config.ini (read only when the SQLite database is
        /// absent) into the same parser state the DB load path uses. The INI
        /// file is never modified — the imported snapshot is persisted to the
        /// new database by <see cref="LoadConfig"/> after normalization.
        /// </summary>
        private void ImportLegacyIni(
            Dictionary<int, string> indexedSongRoots,
            ref string? legacyDtxPath)
        {
            var lines = File.ReadAllLines(_legacyIniPath, Encoding.UTF8);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';'))
                    continue;

                // Split on the first '=' only so values containing '='
                // (e.g. skin paths like "Skins/CX=Neon") survive a round-trip.
                var parts = line.Split('=', 2);
                if (parts.Length != 2)
                    continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                ParseConfigLine(key, value, indexedSongRoots, ref legacyDtxPath);
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

        private void ParseConfigLine(
            string key,
            string value,
            Dictionary<int, string> indexedSongRoots,
            ref string? legacyDtxPath)
        {
            if (TryCaptureIndexedSongRoot(key, value, indexedSongRoots))
                return;

            switch (key)
            {
                case "DTXManiaVersion":
                    Config.DTXManiaVersion = value;
                    break;
                case "SkinPath":
                    Config.SkinPath = value;
                    break;
                case "DTXPath":
                    legacyDtxPath = value;
                    break;
                case "UseBoxDefSkin":
                    if (TryParseBool(value, out var useBoxDefSkin))
                        Config.UseBoxDefSkin = useBoxDefSkin;
                    break;
                case "SystemSkinRoot":
                    Config.SystemSkinRoot = value;
                    break;
                case "LastUsedSkin":
                    Config.LastUsedSkin = value;
                    break;
                case "ScreenWidth":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width))
                        Config.ScreenWidth = width;
                    break;
                case "ScreenHeight":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
                        Config.ScreenHeight = height;
                    break;
                case "FullScreen":
                    if (TryParseBool(value, out var fullScreen))
                        Config.FullScreen = fullScreen;
                    break;
                case "VSyncWait":
                    if (TryParseBool(value, out var vSyncWait))
                        Config.VSyncWait = vSyncWait;
                    break;
                case "ScrollSpeed":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scrollSpeed))
                        Config.ScrollSpeed = ScrollSpeedRange.SnapAndClamp(scrollSpeed);
                    break;
                case "PlaySpeedPercent":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var playSpeedPercent))
                        Config.PlaySpeedPercent = PlaySpeedRange.SnapAndClamp(playSpeedPercent);
                    break;
                case "PitchSemitones":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pitchSemitones))
                        Config.PitchSemitones = PitchRange.SnapAndClamp(pitchSemitones);
                    break;
                case "Metronome":
                    if (TryParseBool(value, out var metronome))
                        Config.Metronome = metronome;
                    break;
                case "RandomSelectFromSubBox":
                case "RandomFromSubBox":
                    if (TryParseBool(value, out var randomSelectFromSubBox))
                        Config.RandomSelectFromSubBox = randomSelectFromSubBox;
                    break;
                case "AutoPlay":
                    // Obsolete global flag (HPA-18): recognized only to warn. The
                    // value is neither translated into lanes nor persisted again.
                    _logger.LogWarning(
                        "Ignoring obsolete global AutoPlay setting; configure AutoPlay.0 through AutoPlay.9 instead.");
                    break;
                case "NoFail":
                    if (TryParseBool(value, out var noFail))
                        Config.NoFail = noFail;
                    break;
                case "Risky":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var risky))
                        Config.Risky = RiskyRange.Clamp(risky);
                    break;
                case "DamageLevel":
                {
                    var matchedName = Enum.GetNames<GaugeDamageLevel>()
                        .FirstOrDefault(name =>
                            string.Equals(name, value, StringComparison.OrdinalIgnoreCase));

                    if (matchedName != null)
                        Config.DamageLevel = Enum.Parse<GaugeDamageLevel>(matchedName);
                    break;
                }
                case "LaneDisplayMode":
                {
                    var matchedName = Enum.GetNames<DrumsLaneDisplayMode>()
                        .FirstOrDefault(name =>
                            string.Equals(name, value, StringComparison.OrdinalIgnoreCase));

                    if (matchedName != null)
                        Config.LaneDisplayMode = Enum.Parse<DrumsLaneDisplayMode>(matchedName);
                    break;
                }
                case "ShowJudgementLine":
                    if (TryParseBool(value, out var showJudgementLine))
                        Config.ShowJudgementLine = showJudgementLine;
                    break;
                case "EnableLaneFlush":
                    if (TryParseBool(value, out var enableLaneFlush))
                        Config.EnableLaneFlush = enableLaneFlush;
                    break;
                case "ShowCombo":
                    if (TryParseBool(value, out var showCombo))
                        Config.ShowCombo = showCombo;
                    break;
                case "AutoAddGauge":
                    if (TryParseBool(value, out var autoAddGauge))
                        Config.AutoAddGauge = autoAddGauge;
                    break;
                case "AudioLatencyOffsetMs":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var audioLatencyOffsetMs))
                        Config.AudioLatencyOffsetMs = Math.Max(0, audioLatencyOffsetMs);
                    break;
                case "EnableGameApi":
                    if (TryParseBool(value, out var enableGameApi))
                        Config.EnableGameApi = enableGameApi;
                    break;
                case "GameApiPort":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var apiPort))
                        Config.GameApiPort = apiPort;
                    break;
                case "GameApiKey":
                    Config.GameApiKey = value;
                    break;
                // Handle key bindings from config file
                default:
                    if (TryParseMidiVelocityThresholdKey(key, out var midiNoteNumber))
                    {
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var midiThreshold))
                        {
                            SetMidiVelocityThresholdInMemory(midiNoteNumber, midiThreshold);
                        }
                        else
                        {
                            // Mirror IsSupportedBindingKeyOrLog: a hand-edited
                            // "MidiVelocity.36=abc" should not silently vanish — the
                            // user's sensitivity setting otherwise disappears with no
                            // clue why their kit feels wrong after reload.
                            _logger.LogWarning(
                                "Ignoring malformed MIDI velocity threshold '{Key}={Value}': " +
                                "value must be an integer in the 0–127 range.",
                                key, value);
                        }
                    }
                    else if (key.StartsWith(AutoPlayLanePrefix, StringComparison.Ordinal))
                    {
                        if (TryParseAutoPlayLaneKey(key, out var autoPlayLane) &&
                            TryParseBool(value, out var isAutoPlayLaneEnabled) &&
                            isAutoPlayLaneEnabled)
                        {
                            Config.AutoPlayLanes.Add(autoPlayLane);
                        }
                    }
                    else if (key.StartsWith("Key.Unbound.") &&
                        int.TryParse(key.Substring("Key.Unbound.".Length), NumberStyles.None, CultureInfo.InvariantCulture, out var unboundLane))
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
                        if (IsSupportedBindingKeyOrLog(buttonId) &&
                            TryParseBool(value, out var isUnboundButton) &&
                            isUnboundButton)
                        {
                            Config.UnboundDrumButtons.Add(buttonId);
                        }
                    }
                    else if (IsSupportedBindingKeyOrLog(key) &&
                        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lane))
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

        private bool TryCaptureIndexedSongRoot(
            string key,
            string value,
            Dictionary<int, string> indexedSongRoots)
        {
            if (!key.StartsWith(SongRootPrefix, StringComparison.Ordinal))
                return false;

            var indexText = key.Substring(SongRootPrefix.Length);
            if (!int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                _logger.LogWarning(
                    "Ignoring malformed SongRoot entry '{Key}={Value}': index must be a non-negative decimal integer.",
                    key,
                    value);
                return true;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                _logger.LogWarning(
                    "Ignoring blank SongRoot entry '{Key}'.",
                    key);
                return true;
            }

            if (indexedSongRoots.ContainsKey(index))
            {
                _logger.LogWarning(
                    "SongRoot index {Index} appeared more than once; using the last value.",
                    index);
            }

            indexedSongRoots[index] = value;
            return true;
        }

        private SongRootsLoadSource FinalizeSongRoots(
            IReadOnlyDictionary<int, string> indexedSongRoots,
            string? legacyDtxPath)
        {
            if (indexedSongRoots.Count > 0)
            {
                foreach (var root in indexedSongRoots.OrderBy(pair => pair.Key).Select(pair => pair.Value))
                {
                    Config.SongRoots.Add(root);
                }

                Config.DTXPath = Config.SongRoots[0];
                return SongRootsLoadSource.Indexed;
            }

            if (!string.IsNullOrWhiteSpace(legacyDtxPath))
            {
                Config.SongRoots.Add(legacyDtxPath);
                Config.DTXPath = legacyDtxPath;
                return SongRootsLoadSource.LegacyDTXPath;
            }

            var defaultSongsPath = AppPaths.GetDefaultSongsPath();
            Config.SongRoots.Add(defaultSongsPath);
            Config.DTXPath = defaultSongsPath;
            return SongRootsLoadSource.ManagedDefault;
        }

        /// <summary>
        /// Builds the logical key/value snapshot persisted to the SQLite
        /// store (formerly the body of the INI <c>SaveConfig</c>). The
        /// duplicate <c>DTXPath</c> representation is deliberately excluded:
        /// <c>DTXPath</c> is a legacy input/in-memory mirror only, and
        /// <c>SongRoot.0</c> is the persisted form.
        /// </summary>
        private IReadOnlyDictionary<string, string> BuildPersistedEntries()
        {
            var songRoots = Config.SongRoots.ToArray();
            if (songRoots.Length > 0)
            {
                Config.DTXPath = songRoots[0];
            }

            var entries = new Dictionary<string, string>(StringComparer.Ordinal);

            // Defensive coalescing: these five string properties can hold null
            // at runtime despite their non-nullable annotations (e.g. via
            // deserialization); null must never reach SqliteConfigStore.Save
            // (NOT NULL schema).
            entries["DTXManiaVersion"] = Config.DTXManiaVersion ?? string.Empty;
            entries["SkinPath"] = Config.SkinPath ?? string.Empty;
            for (var index = 0; index < songRoots.Length; index++)
            {
                entries[$"{SongRootPrefix}{index}"] = songRoots[index];
            }

            entries["UseBoxDefSkin"] = Config.UseBoxDefSkin.ToString();
            entries["SystemSkinRoot"] = Config.SystemSkinRoot ?? string.Empty;
            entries["LastUsedSkin"] = Config.LastUsedSkin ?? string.Empty;

            entries["ScreenWidth"] = Config.ScreenWidth.ToString(CultureInfo.InvariantCulture);
            entries["ScreenHeight"] = Config.ScreenHeight.ToString(CultureInfo.InvariantCulture);
            entries["FullScreen"] = Config.FullScreen.ToString();
            entries["VSyncWait"] = Config.VSyncWait.ToString();

            entries["ScrollSpeed"] = Config.ScrollSpeed.ToString(CultureInfo.InvariantCulture);
            entries["PlaySpeedPercent"] = PlaySpeedRange.SnapAndClamp(Config.PlaySpeedPercent)
                .ToString(CultureInfo.InvariantCulture);
            entries["PitchSemitones"] = PitchRange.SnapAndClamp(Config.PitchSemitones)
                .ToString(CultureInfo.InvariantCulture);
            entries["Metronome"] = Config.Metronome.ToString();
            entries["RandomSelectFromSubBox"] = Config.RandomSelectFromSubBox.ToString();
            foreach (var lane in Config.AutoPlayLanes
                .Where(lane => lane >= 0 && lane <= 9)
                .OrderBy(lane => lane))
            {
                entries[$"{AutoPlayLanePrefix}{lane}"] = bool.TrueString.ToLowerInvariant();
            }
            entries["NoFail"] = Config.NoFail.ToString();
            entries["Risky"] = RiskyRange.Clamp(Config.Risky)
                .ToString(CultureInfo.InvariantCulture);
            entries["DamageLevel"] = Config.DamageLevel.ToString();
            entries["AutoAddGauge"] = Config.AutoAddGauge.ToString();
            entries["LaneDisplayMode"] = Config.LaneDisplayMode.ToString();
            entries["ShowJudgementLine"] = Config.ShowJudgementLine.ToString();
            entries["EnableLaneFlush"] = Config.EnableLaneFlush.ToString();
            entries["ShowCombo"] = Config.ShowCombo.ToString();
            entries["AudioLatencyOffsetMs"] = Config.AudioLatencyOffsetMs.ToString(CultureInfo.InvariantCulture);

            entries["EnableGameApi"] = Config.EnableGameApi.ToString();
            entries["GameApiPort"] = Config.GameApiPort.ToString(CultureInfo.InvariantCulture);
            entries["GameApiKey"] = Config.GameApiKey ?? string.Empty;

            foreach (var kvp in Config.KeyBindings)
            {
                entries[kvp.Key] = kvp.Value.ToString(CultureInfo.InvariantCulture);
            }
            foreach (var lane in Config.UnboundDrumLanes.OrderBy(lane => lane))
            {
                entries[$"Key.Unbound.{lane}"] = bool.TrueString.ToLowerInvariant();
            }
            foreach (var buttonId in Config.UnboundDrumButtons.OrderBy(buttonId => buttonId, StringComparer.Ordinal))
            {
                entries[$"Key.UnboundButton.{buttonId}"] = bool.TrueString.ToLowerInvariant();
            }

            foreach (var kvp in Config.SystemKeyBindings)
            {
                entries[kvp.Key] = kvp.Value;
            }

            var savedMidiThresholds = Config.MidiVelocityThresholds
                .Where(kvp => kvp.Key >= 0 && kvp.Key <= 127 && kvp.Value > 0)
                .OrderBy(kvp => kvp.Key)
                .ToList();
            foreach (var kvp in savedMidiThresholds)
            {
                entries[$"{MidiVelocityPrefix}{kvp.Key}"] =
                    Math.Clamp(kvp.Value, 0, 127).ToString(CultureInfo.InvariantCulture);
            }

            return entries;
        }

        /// <summary>
        /// Replaces the persisted snapshot. Requires a prior successful load
        /// (or first-create) in this instance: the v1 store would silently
        /// stamp schema v1 over a higher-version database.
        /// </summary>
        private void SaveSnapshotToStore()
        {
            if (!_storeLoaded)
            {
                throw new InvalidOperationException(
                    "Configuration must be loaded before it can be saved.");
            }

            _store.Save(BuildPersistedEntries());
            _hasPendingSave = false;
        }

        /// <summary>
        /// Creates the store snapshot for the first time (no prior database
        /// exists, so no version can be clobbered) and marks this instance
        /// eligible for further saves.
        /// </summary>
        private void CreateStoreSnapshot()
        {
            _store.Save(BuildPersistedEntries());
            _storeLoaded = true;
            _hasPendingSave = false;
        }

        public void ResetToDefaults()
        {
            Config = new ConfigData();
        }

        public event EventHandler<ScrollSpeedChangedEventArgs>? ScrollSpeedChanged;

        public event EventHandler<EventArgs>? KeyBindingsChanged;

        public event EventHandler<EventArgs>? SystemKeyBindingsChanged;

        public event EventHandler<SongRootsChangedEventArgs>? SongRootsChanged;

        /// <summary>
        /// Persists an ordered set of validated song-library roots immediately. A failed
        /// write restores the exact in-memory roots and legacy compatibility mirror.
        /// </summary>
        public SongRootUpdateResult SetSongRoots(IReadOnlyList<string> roots)
        {
            ArgumentNullException.ThrowIfNull(roots);

            var validation = _songRootPolicy.Validate(roots);
            if (!validation.IsValid)
            {
                return new SongRootUpdateResult(
                    SongRootUpdateStatus.ValidationFailed,
                    validation.CanonicalRoots,
                    validation.Diagnostics);
            }

            var oldRoots = Config.SongRoots.ToArray();
            var oldCanonicalRoots = _songRootPolicy.Validate(oldRoots)
                .CanonicalRoots;
            if (oldCanonicalRoots.SequenceEqual(
                validation.CanonicalRoots,
                _songRootPolicy.Comparer))
            {
                return new SongRootUpdateResult(
                    SongRootUpdateStatus.Unchanged,
                    validation.CanonicalRoots,
                    validation.Diagnostics);
            }

            var oldDtxPath = Config.DTXPath;
            Config.SongRoots.Clear();
            Config.SongRoots.AddRange(validation.CanonicalRoots);
            Config.DTXPath = validation.CanonicalRoots.FirstOrDefault() ?? string.Empty;

            try
            {
                SaveSnapshotToStore();
            }
            catch (Exception ex)
            {
                Config.SongRoots.Clear();
                Config.SongRoots.AddRange(oldRoots);
                Config.DTXPath = oldDtxPath;
                _logger.LogError(
                    ex,
                    "Failed to persist song roots to {Path}; restored the in-memory configuration.",
                    _store.DatabasePath);

                var diagnostics = validation.Diagnostics
                    .Concat(
                    [
                        new SongRootDiagnostic(
                            _store.DatabasePath,
                            $"Could not persist song roots: {ex.Message}",
                            IsWarning: false),
                    ])
                    .ToArray();
                return new SongRootUpdateResult(
                    SongRootUpdateStatus.PersistenceFailed,
                    validation.CanonicalRoots,
                    diagnostics);
            }

            RaiseEvent(
                SongRootsChanged,
                new SongRootsChangedEventArgs(
                    oldCanonicalRoots,
                    validation.CanonicalRoots));
            return new SongRootUpdateResult(
                SongRootUpdateStatus.Updated,
                validation.CanonicalRoots,
                validation.Diagnostics);
        }

        public void SetScrollSpeed(int percent)
        {
            var snapped = ScrollSpeedRange.SnapAndClamp(percent);
            var old = Config.ScrollSpeed;
            if (snapped == old)
                return;

            Config.ScrollSpeed = snapped;

            // Defer disk write — mark dirty and flush later via FlushPendingSave.
            MarkDirty();

            RaiseEvent(ScrollSpeedChanged, new ScrollSpeedChangedEventArgs(old, snapped));
        }

        public void AdjustScrollSpeed(int stepDelta)
        {
            SetScrollSpeed(Config.ScrollSpeed + stepDelta * ScrollSpeedRange.Step);
        }

        public void SetPlaySpeedPercent(int percent)
        {
            var snapped = PlaySpeedRange.SnapAndClamp(percent);
            if (snapped == Config.PlaySpeedPercent)
                return;

            Config.PlaySpeedPercent = snapped;
            MarkDirty();
        }

        public void SetPitchSemitones(int semitones)
        {
            var snapped = PitchRange.SnapAndClamp(semitones);
            if (snapped == Config.PitchSemitones)
                return;

            Config.PitchSemitones = snapped;
            MarkDirty();
        }

        /// <summary>Sets Metronome and marks a deferred save pending only when the value changes. No event raised.</summary>
        public void SetMetronome(bool value)
        {
            if (value == Config.Metronome)
                return;

            Config.Metronome = value;
            MarkDirty();
        }

        /// <summary>Sets Random Select descendant-BOX inclusion and marks a deferred save when changed.</summary>
        public void SetRandomSelectFromSubBox(bool value)
        {
            if (value == Config.RandomSelectFromSubBox)
                return;

            Config.RandomSelectFromSubBox = value;
            MarkDirty();
        }

        /// <summary>Sets Risky, clamped to the supported range, and marks a deferred save when changed.</summary>
        public void SetRisky(int value)
        {
            var clamped = RiskyRange.Clamp(value);
            if (clamped == Config.Risky)
                return;

            Config.Risky = clamped;
            MarkDirty();
        }

        /// <summary>Sets gauge damage level, normalizing undefined values to Normal, and marks a deferred save when changed.</summary>
        public void SetDamageLevel(GaugeDamageLevel value)
        {
            if (!Enum.IsDefined(value))
                value = GaugeDamageLevel.Normal;

            if (value == Config.DamageLevel)
                return;

            Config.DamageLevel = value;
            MarkDirty();
        }

        /// <summary>Sets Auto Add Gauge and marks a deferred save when changed.</summary>
        public void SetAutoAddGauge(bool value)
        {
            if (value == Config.AutoAddGauge)
                return;

            Config.AutoAddGauge = value;
            MarkDirty();
        }

        /// <summary>Sets drum lane display mode, normalizing undefined values to AllOn, and marks a deferred save when changed.</summary>
        public void SetLaneDisplayMode(DrumsLaneDisplayMode value)
        {
            if (!Enum.IsDefined(value))
                value = DrumsLaneDisplayMode.AllOn;

            if (value == Config.LaneDisplayMode)
                return;

            Config.LaneDisplayMode = value;
            MarkDirty();
        }

        /// <summary>Sets Judge Line visibility and marks a deferred save when changed.</summary>
        public void SetShowJudgementLine(bool value)
        {
            if (value == Config.ShowJudgementLine)
                return;

            Config.ShowJudgementLine = value;
            MarkDirty();
        }

        /// <summary>Sets Lane Flush and marks a deferred save when changed.</summary>
        public void SetEnableLaneFlush(bool value)
        {
            if (value == Config.EnableLaneFlush)
                return;

            Config.EnableLaneFlush = value;
            MarkDirty();
        }

        /// <summary>Sets Combo visibility and marks a deferred save when changed.</summary>
        public void SetShowCombo(bool value)
        {
            if (value == Config.ShowCombo)
                return;

            Config.ShowCombo = value;
            MarkDirty();
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

        public int GetMidiVelocityThreshold(int noteNumber)
        {
            if (noteNumber < 0 || noteNumber > 127)
                return 0;

            return Config.MidiVelocityThresholds.TryGetValue(noteNumber, out var threshold)
                ? Math.Clamp(threshold, 0, 127)
                : 0;
        }

        public void SetMidiVelocityThreshold(int noteNumber, int threshold)
        {
            if (noteNumber < 0 || noteNumber > 127)
                return;

            SetMidiVelocityThresholdInMemory(noteNumber, threshold);
            MarkDirty();
        }

        private void SetMidiVelocityThresholdInMemory(int noteNumber, int threshold)
        {
            if (noteNumber < 0 || noteNumber > 127)
                return;

            var clamped = Math.Clamp(threshold, 0, 127);
            if (clamped == 0)
            {
                Config.MidiVelocityThresholds.Remove(noteNumber);
                return;
            }

            Config.MidiVelocityThresholds[noteNumber] = clamped;
        }

        private static bool TryParseMidiVelocityThresholdKey(string key, out int noteNumber)
        {
            noteNumber = default;
            if (string.IsNullOrWhiteSpace(key) ||
                !key.StartsWith(MidiVelocityPrefix, StringComparison.Ordinal) ||
                key.Length <= MidiVelocityPrefix.Length)
            {
                return false;
            }

            return int.TryParse(
                       key.Substring(MidiVelocityPrefix.Length),
                       NumberStyles.None,
                       CultureInfo.InvariantCulture,
                       out noteNumber) &&
                   noteNumber >= 0 &&
                   noteNumber <= 127;
        }

        private static bool TryParseAutoPlayLaneKey(string key, out int lane)
        {
            lane = default;
            if (string.IsNullOrWhiteSpace(key) ||
                !key.StartsWith(AutoPlayLanePrefix, StringComparison.Ordinal) ||
                key.Length <= AutoPlayLanePrefix.Length)
            {
                return false;
            }

            return int.TryParse(
                       key.Substring(AutoPlayLanePrefix.Length),
                       NumberStyles.None,
                       CultureInfo.InvariantCulture,
                       out lane) &&
                   lane >= 0 &&
                   lane <= 9;
        }

        public void SetAutoPlayLane(int lane, bool enabled)
        {
            if (lane < 0 || lane > 9)
                return;

            var changed = enabled
                ? Config.AutoPlayLanes.Add(lane)
                : Config.AutoPlayLanes.Remove(lane);
            if (changed)
                MarkDirty();
        }

        public void SetAllAutoPlayLanes(bool enabled)
        {
            var target = enabled
                ? Enumerable.Range(0, 10)
                : Enumerable.Empty<int>();
            if (Config.AutoPlayLanes.SetEquals(target))
                return;

            Config.AutoPlayLanes.Clear();
            if (enabled)
                Config.AutoPlayLanes.UnionWith(Enumerable.Range(0, 10));

            MarkDirty();
        }

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

        /// <summary>Sets the skin path (<see cref="ConfigData.SkinPath"/>) and marks a deferred save pending. No event raised. No-op when null/whitespace or unchanged.</summary>
        public void SetSkinPath(string skinPath)
        {
            if (string.IsNullOrWhiteSpace(skinPath))
                return;

            // Compare normalized forms rather than raw strings: the incoming
            // value comes from ResourceManager.GetCurrentEffectiveSkinPath()
            // (normalized with a trailing separator + forward slashes), while
            // Config.SkinPath may have been loaded verbatim from the store
            // (no trailing separator, possibly backslashes on Windows). A raw
            // equality check would miss equivalent paths and spuriously mark
            // the config dirty (triggering a redundant write) on every switch.
            if (string.Equals(NormalizeSkinPathForComparison(skinPath),
                              NormalizeSkinPathForComparison(Config.SkinPath),
                              AppPaths.SkinPathComparison))
                return;

            Config.SkinPath = skinPath;
            MarkDirty();
        }

        /// <summary>
        /// Reduces a skin path to a canonical form for equality comparison:
        /// trims trailing directory separators and unifies separators to '/'.
        /// Does not resolve relative paths or case — callers persist the
        /// original string; this is only used to detect no-op writes.
        /// </summary>
        private static string NormalizeSkinPathForComparison(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            return path.Replace('\\', '/').TrimEnd('/');
        }

        /// <summary>
        /// Compares two SongRoot lists element-by-element using the platform's
        /// path comparison so that discarded, replaced, or inserted entries are
        /// detected after <see cref="NormalizeConfigPaths"/> runs.
        /// </summary>
        private static bool SongRootsSequenceEqual(
            IReadOnlyList<string> first, IReadOnlyList<string> second)
        {
            if (first.Count != second.Count)
                return false;
            for (var i = 0; i < first.Count; i++)
            {
                if (!string.Equals(first[i], second[i], AppPaths.SkinPathComparison))
                    return false;
            }
            return true;
        }

        /// <inheritdoc/>
        public void FlushPendingSave()
        {
            if (!_hasPendingSave)
                return;

            try
            {
                SaveSnapshotToStore();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist deferred config changes to {Path}; in-memory values are still up to date. Will retry on next flush.", _store.DatabasePath);
            }
        }

        /// <summary>
        /// Marks a deferred save as pending. A no-op before any successful
        /// <see cref="LoadConfig"/> call (in-memory mutation only), matching
        /// the store's load-before-save ordering constraint.
        /// </summary>
        private void MarkDirty()
        {
            if (_storeLoaded)
            {
                _hasPendingSave = true;
            }
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
            if (string.IsNullOrWhiteSpace(key))
                return false;

            if (key.StartsWith("MIDI.", StringComparison.Ordinal))
            {
                // Reject malformed MIDI note IDs (e.g. "MIDI.036", "MIDI.abc", "MIDI.128")
                // so they don't silently load as bindings that can never be matched at
                // lookup time — TryParseMidiButtonId enforces canonical decimal form (no
                // leading zeros) and a 0–127 note range.
                return KeyBindings.TryParseMidiButtonId(key, out _);
            }

            return key.StartsWith("Key.", StringComparison.Ordinal)
                || key.StartsWith("Pad.", StringComparison.Ordinal);
        }

        /// <summary>
        /// Validates a config binding key, logging a warning when a MIDI-prefixed key is
        /// rejected due to a malformed note number (e.g. leading zeros, out-of-range).
        /// This helps users diagnose hand-edited configs that would otherwise be silently ignored.
        /// </summary>
        private bool IsSupportedBindingKeyOrLog(string key)
        {
            if (IsSupportedButtonBindingKey(key))
                return true;

            if (key.StartsWith("MIDI.", StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Ignoring malformed MIDI button binding '{Key}': note IDs must be canonical decimal "
                    + "(e.g. 'MIDI.36', not 'MIDI.036') within the 0–127 range.",
                    key);
            }

            return false;
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
                var missingCommands = RequiredSystemCommands
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

        private static string NormalizePathForComparison(string path)
        {
            return path.Trim().Replace('\\', '/').TrimEnd('/');
        }

        /// <summary>
        /// Resolves the first validating bundled System skin root from
        /// <see cref="AppPaths.GetBundledSystemSkinRootCandidates"/>, or null
        /// when none exists on disk. Used to migrate the default SkinPath from
        /// the old app-data location to the application-managed bundled root.
        /// </summary>
        private static string? ResolveValidatingBundledSystemSkinRoot()
        {
            return ResolveValidatingBundledSystemSkinRoot(AppContext.BaseDirectory);
        }

        /// <summary>
        /// Internal overload accepting an explicit base directory so relocation
        /// scenarios (bundled root A → bundled root B) can be tested
        /// deterministically without mutating <see cref="AppContext.BaseDirectory"/>,
        /// which is immutable at runtime.
        /// </summary>
        internal static string? ResolveValidatingBundledSystemSkinRoot(string baseDir)
        {
            foreach (var candidate in AppPaths.GetBundledSystemSkinRootCandidates(baseDir))
            {
                try
                {
                    if (PathValidator.IsValidSkinPath(candidate))
                        return Path.GetFullPath(candidate);
                }
                catch
                {
                    // Candidate doesn't exist or is inaccessible — try the next.
                }
            }
            return null;
        }

        /// <summary>
        /// Resolves a configured SkinPath value to an absolute path suitable
        /// for <see cref="IResourceManager.SetSkinPath"/>. The
        /// <see cref="DefaultSkinPathToken"/> token maps to the current
        /// validating bundled System skin root (or the app-data default when no
        /// bundled root validates), so a persisted "Default" survives app
        /// relocations. Any other value is returned as-is — custom skin paths
        /// are already absolute.
        /// </summary>
        public static string ResolveSkinPath(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath) ||
                string.Equals(configuredPath.Trim(), DefaultSkinPathToken, StringComparison.OrdinalIgnoreCase))
            {
                return ResolveDefaultSkinPath();
            }
            return configuredPath;
        }

        /// <summary>
        /// Internal overload accepting an explicit base directory so the
        /// "Default" token's resolution to the bundled root can be tested
        /// across simulated relocation targets without mutating
        /// <see cref="AppContext.BaseDirectory"/>.
        /// </summary>
        internal static string ResolveSkinPath(string configuredPath, string baseDir)
        {
            if (string.IsNullOrWhiteSpace(configuredPath) ||
                string.Equals(configuredPath.Trim(), DefaultSkinPathToken, StringComparison.OrdinalIgnoreCase))
            {
                return ResolveDefaultSkinPath(baseDir);
            }
            return configuredPath;
        }

        /// <summary>
        /// Returns the absolute path for the default skin: the first validating
        /// bundled System skin root, or the app-data System root when no bundled
        /// root validates (e.g. dev builds without a bundled skin).
        /// </summary>
        private static string ResolveDefaultSkinPath()
        {
            var bundled = ResolveValidatingBundledSystemSkinRoot();
            if (bundled != null)
                return bundled;
            return AppPaths.GetDefaultSystemSkinRoot();
        }

        /// <summary>
        /// Overload accepting an explicit base directory for deterministic
        /// relocation testing. Falls back to the app-data default when no
        /// bundled root validates from the given base directory.
        /// </summary>
        private static string ResolveDefaultSkinPath(string baseDir)
        {
            var bundled = ResolveValidatingBundledSystemSkinRoot(baseDir);
            if (bundled != null)
                return bundled;
            return AppPaths.GetDefaultSystemSkinRoot();
        }

        private static bool IsLegacyDefaultSongsPath(string? path, string defaultSongsPath)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var normalized = NormalizePathForComparison(path);
            var legacyDefaultSongsPath = NormalizePathForComparison(
                Path.Combine(Path.GetDirectoryName(defaultSongsPath) ?? string.Empty, "Songs"));

            // Only match the specific legacy defaults, not every path ending in "Songs".
            return SongPathIdentity.LegacyAliasComparer.Equals(normalized, legacyDefaultSongsPath)
                || SongPathIdentity.LegacyAliasComparer.Equals(normalized, "Songs")
                || SongPathIdentity.LegacyAliasComparer.Equals(normalized, "./Songs");
        }

        private void NormalizeConfigPaths(string baseDir, bool migrateLegacySongsPath = false)
        {
            var defaultSystemSkinRoot = AppPaths.GetDefaultSystemSkinRoot();
            var defaultSongsPath = AppPaths.GetDefaultSongsPath();

            // Honor configured paths first, fallback to defaults if not set
            Config.SystemSkinRoot = AppPaths.ResolvePathOrDefault(Config.SystemSkinRoot, defaultSystemSkinRoot);

            if (Config.SongRoots.Count == 0)
            {
                Config.SongRoots.Add(defaultSongsPath);
            }

            if (migrateLegacySongsPath && IsLegacyDefaultSongsPath(Config.SongRoots[0], defaultSongsPath))
            {
                _logger.LogInformation(
                    "Migrating legacy DTXPath '{LegacyPath}' to '{DefaultSongsPath}'",
                    Config.SongRoots[0],
                    defaultSongsPath);
                Config.SongRoots[0] = defaultSongsPath;
            }

            // Resolve each persisted root, discarding entries whose stored value
            // is malformed (e.g. contains illegal path characters or a NUL).
            // ResolvePathOrDefault eventually calls Path.GetFullPath, which throws
            // for such values; without per-entry recovery a single corrupted
            // SongRoot.* line would abort LoadConfig entirely and prevent the
            // application from starting. Invalid entries are logged and dropped;
            // when no valid root survives, the managed default restores a usable
            // library root so configuration loading always completes.
            var resolvedSongRoots = new List<string>(Config.SongRoots.Count);
            for (var index = 0; index < Config.SongRoots.Count; index++)
            {
                var persistedRoot = Config.SongRoots[index];
                try
                {
                    resolvedSongRoots.Add(
                        AppPaths.ResolvePathOrDefault(persistedRoot, defaultSongsPath));
                }
                catch (Exception ex) when (
                    ex is ArgumentException or PathTooLongException
                        or NotSupportedException or IOException
                        or UnauthorizedAccessException
                        or System.Security.SecurityException)
                {
                    _logger.LogWarning(
                        ex,
                        "Discarding malformed SongRoot entry '{PersistedRoot}' that could not be resolved to an absolute path.",
                        persistedRoot);
                }
            }

            if (resolvedSongRoots.Count == 0)
            {
                _logger.LogWarning(
                    "No configured SongRoot entries resolved to a valid path; " +
                    "falling back to the managed default songs root.");
                resolvedSongRoots.Add(defaultSongsPath);
            }

            Config.SongRoots.Clear();
            Config.SongRoots.AddRange(resolvedSongRoots);

            Config.DTXPath = Config.SongRoots[0];

            // SkinPath: persist the "Default" token when the configured path is
            // empty, the app-data default, or a validating bundled root candidate.
            // The token resolves to the current bundled root at runtime (see
            // ResolveSkinPath), so the persisted config survives app relocations
            // — moving the .app bundle or portable folder does not stale the
            // path. Explicitly selected custom skins are left alone.
            //
            // This also migrates configs from the previous format (commit
            // 4134a68) where the absolute bundled root was persisted directly:
            // such a path is recognized as a bundled candidate and remapped to
            // the token.
            if (IsDefaultSkinPathToken(Config.SkinPath))
            {
                Config.SkinPath = DefaultSkinPathToken;
            }
            else
            {
                var resolvedSkinPath = AppPaths.ResolvePathOrDefault(Config.SkinPath, Config.SystemSkinRoot);
                Config.SkinPath = IsDefaultSkinPath(resolvedSkinPath, baseDir)
                    ? DefaultSkinPathToken
                    : resolvedSkinPath;
            }

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
            if (Config.SongRoots.Any(root => SongPathIdentity.LegacyAliasComparer.Equals(
                NormalizePathForComparison(root),
                NormalizePathForComparison(defaultSongsPath))))
            {
                EnsureDirectorySafe(defaultSongsPath);
            }
        }

        /// <summary>
        /// Returns true when the value equals the <see cref="DefaultSkinPathToken"/>
        /// token (case-insensitive), i.e. the config already stores the logical
        /// default rather than an absolute path.
        /// </summary>
        private static bool IsDefaultSkinPathToken(string? path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && string.Equals(path.Trim(), DefaultSkinPathToken, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns true when the resolved absolute path is the app-data default
        /// System root or any validating bundled System skin root candidate —
        /// all representations that should be persisted as the
        /// <see cref="DefaultSkinPathToken"/> token. Uses
        /// <see cref="AppContext.BaseDirectory"/> for bundled candidate
        /// resolution; see <see cref="IsDefaultSkinPath(string, string)"/> for
        /// the testable overload that accepts an explicit base directory.
        /// </summary>
        private static bool IsDefaultSkinPath(string resolvedPath)
            => IsDefaultSkinPath(resolvedPath, AppContext.BaseDirectory);

        /// <summary>
        /// Testable overload of <see cref="IsDefaultSkinPath(string)"/> that
        /// resolves bundled candidates from an explicit
        /// <paramref name="baseDir"/> instead of
        /// <see cref="AppContext.BaseDirectory"/> (immutable at runtime). This
        /// is the seam that lets the load-and-persist migration test exercise a
        /// GENUINE bundled path — writing a fake install's validating System
        /// root into the config store (via a legacy Config.ini import) and
        /// verifying LoadConfig rewrites it to the
        /// <see cref="DefaultSkinPathToken"/> token — without mutating
        /// <see cref="AppContext.BaseDirectory"/>.
        /// </summary>
        /// <param name="resolvedPath">Resolved absolute skin path to test.</param>
        /// <param name="baseDir">
        /// Base directory to resolve bundled System skin root candidates from
        /// (mirrors <see cref="AppPaths.GetBundledSystemSkinRootCandidates(string)"/>).
        /// </param>
        /// <returns>
        /// True when <paramref name="resolvedPath"/> is the app-data default
        /// System root or any validating bundled candidate from
        /// <paramref name="baseDir"/>.
        /// </returns>
        internal static bool IsDefaultSkinPath(string resolvedPath, string baseDir)
        {
            if (string.IsNullOrEmpty(resolvedPath))
                return false;

            // App-data default System root.
            if (string.Equals(NormalizePathForComparison(resolvedPath),
                              NormalizePathForComparison(AppPaths.GetDefaultSystemSkinRoot()),
                              AppPaths.SkinPathComparison))
                return true;

            // Any validating bundled root candidate from the given base dir
            // (includes the previous format's absolute bundled path and the
            // current location's bundled root).
            foreach (var candidate in AppPaths.GetBundledSystemSkinRootCandidates(baseDir))
            {
                try
                {
                    if (PathValidator.IsValidSkinPath(candidate) &&
                        string.Equals(NormalizePathForComparison(resolvedPath),
                                      NormalizePathForComparison(candidate),
                                      AppPaths.SkinPathComparison))
                        return true;
                }
                catch
                {
                    // Candidate doesn't exist — skip.
                }
            }
            return false;
        }
    }
}
