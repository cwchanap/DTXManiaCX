#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.KeyAssign;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Song;
using DTXMania.Game;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;


namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Configuration stage with basic settings management
    /// Follows DTXMania patterns for config item handling
    /// </summary>
    public class ConfigStage : BaseStage
    {
        #region Private Fields

        private IConfigManager _configManager;
        private List<IConfigItem> _configItems;
        private ConfigData _workingConfig;
        private KeyBindings _workingDrumBindings = new();
        private Dictionary<Keys, InputCommandType> _workingSystemBindings = new();

        // NullLogger until OnActivate swaps in the game's factory; keeps reflection-based tests
        // (which skip OnActivate) safe when they exercise ApplyConfiguration()'s catch path.
        private ILogger<ConfigStage> _logger = NullLogger<ConfigStage>.Instance;

        // Set when a Save & Exit disk write fails; drawn in red so the user knows changes were not
        // persisted and can retry. Mirrors DrumConfigStage._saveError. Without this, a failed save
        // left the user on the stage with no on-screen signal, and a subsequent Back silently
        // discarded the unsaved edits. Cleared on the next successful save.
        private string? _saveError;
        /// <summary>
        /// Snapshot of system bindings taken at stage activation; used for ConfigStage's own
        /// navigation so that editing/wiping bindings in the system panel cannot lock out
        /// the Save &amp; Back controls before the edit is committed.
        /// </summary>
        private Dictionary<Keys, InputCommandType> _navigationBindings = new();
        private bool _hasUnsavedChanges;
        private int _selectedIndex = 0;

        // Input handling
        private KeyboardState _previousKeyboardState;
        private KeyboardState _currentKeyboardState;

        // Key-assign sub-panels
        private SystemKeyAssignPanel? _systemPanel;
        private IKeyAssignPanel? _activePanel;

        // Graphics resources
        private SpriteBatch _spriteBatch;
        private Texture2D _whitePixel;
        private IFont _font;
        private IFont _boldFont;
        private IResourceManager _resourceManager;
        private ITexture? _backgroundTexture;

        // Dark UI text, for legibility on the bright background image.
        private static readonly Color DarkText = new(26, 30, 46);

        // NX score import status
        private volatile string _importStatus = "";
        private volatile bool _importRunning;
        private CancellationTokenSource? _importCts;

        // DTXMania-style constants
        private const int MenuX = 100;
        private const int MenuY = 100;
        private const int MenuItemHeight = 40;
        private const int MenuItemWidth = 600;

        #endregion

        #region Constructor

        public override StageType Type => StageType.Config;

        public ConfigStage(BaseGame game) : base(game)
        {
            _configManager = game.ConfigManager ?? throw new InvalidOperationException("ConfigManager not found");
        }

        #endregion

        #region Stage Lifecycle

        protected override void OnActivate()
        {
            _logger = _game.LoggerFactory?.CreateLogger<ConfigStage>() ?? _logger;
            _saveError = null; // fresh entry: no outstanding save error

            System.Diagnostics.Debug.WriteLine("Activating Config Stage");

            InitializeGraphics();

            // Opening Drum Key Mapping transitions away to DrumConfigStage and back. Config values
            // (Auto Play, Scroll Speed, ...) live only in this working copy until Save &amp; Exit
            // commits them, so on return we must NOT clobber pending edits.
            //
            // Drum bindings always reload: DrumConfigStage commits drum bindings independently on
            // its Back = Save exit, so the working drum copy must pick those up or a later Save here
            // would overwrite DrumConfig's changes. Pending SYSTEM-key edits, however, must NOT be
            // reloaded: the System Key Mapping panel writes only to _workingSystemBindings (the live
            // InputManager is untouched until Save &amp; Exit here), so reloading from live would
            // discard those edits and a later Save would persist the old mapping. Reload only the
            // drum bindings; eviction keeps DrumConfig's conflict resolution intact (see
            // ReloadWorkingDrumBindings).
            if (_hasUnsavedChanges)
            {
                ReloadWorkingDrumBindings();
            }
            else
            {
                LoadConfiguration();
                LoadWorkingInputBindings();
            }

            SetupConfigItems();
            InitializePanels();

            _previousKeyboardState = Keyboard.GetState();
            _currentKeyboardState = Keyboard.GetState();
        }

        protected override void OnUpdate(double deltaTime)
        {
            _previousKeyboardState = _currentKeyboardState;
            _currentKeyboardState = Keyboard.GetState();

            if (_activePanel?.IsActive == true)
            {
                _activePanel.Update(deltaTime, _currentKeyboardState, _previousKeyboardState);
                return;
            }

            HandleInput();
        }

        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;

            BeginDrawFrame();

            DrawBackground();
            DrawTitle();
            DrawConfigItems();
            DrawButtons();
            DrawImportStatus();
            DrawSaveError();
            DrawInstructions();

            // Draw active panel as overlay within the same sprite batch
            if (_activePanel?.IsActive == true)
            {
                var vp = GetViewport();
                _activePanel.Draw(_spriteBatch, _font, _boldFont, _whitePixel, vp.Width, vp.Height);
            }

            EndDrawFrame();
        }

        protected override void OnDeactivate()
        {
            System.Diagnostics.Debug.WriteLine("Deactivating Config Stage");

            if (_hasUnsavedChanges)
                System.Diagnostics.Debug.WriteLine("Warning: Unsaved configuration changes will be lost");

            // Cancel any in-flight NX score import so it doesn't continue on a deactivated stage.
            _importCts?.Cancel();

            _activePanel?.Deactivate();
            _activePanel = null;

            // Release font references (re-acquired in InitializeGraphics on re-activation)
            _font?.RemoveReference();
            _font = null;
            _boldFont?.RemoveReference();
            _boldFont = null;
            _backgroundTexture?.RemoveReference();
            _backgroundTexture = null;

            _previousKeyboardState = default;
            _currentKeyboardState = default;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                System.Diagnostics.Debug.WriteLine("Disposing Config Stage resources");

                // Cancel and dispose import cancellation token
                _importCts?.Cancel();
                _importCts?.Dispose();
                _importCts = null;

                // Cleanup MonoGame resources
                _whitePixel?.Dispose();
                _spriteBatch?.Dispose();

                // Release font references if still held (e.g. stage disposed without deactivation)
                _font?.RemoveReference();
                _boldFont?.RemoveReference();
                _backgroundTexture?.RemoveReference();

                _whitePixel = null;
                _spriteBatch = null;
                _font = null;
                _boldFont = null;
                _backgroundTexture = null;
                _resourceManager = null; // Do NOT dispose — shared game-wide instance
            }

            base.Dispose(disposing);
        }

        #endregion

        #region Initialization

        protected virtual void InitializeGraphics()
        {
            var graphicsDevice = _game.GraphicsDevice;
            _spriteBatch = new SpriteBatch(graphicsDevice);

            _whitePixel = new Texture2D(graphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });

            // Initialize ResourceManager
            _resourceManager = _game.ResourceManager;

            try
            {
                _font = _resourceManager.LoadFont("NotoSerifJP", 14);
                _boldFont = _resourceManager.LoadFont("NotoSerifJP", 14, FontStyle.Bold);
            }
            catch
            {
                _font?.RemoveReference();
                _font = null;
                _boldFont?.RemoveReference();
                _boldFont = null;
                _whitePixel?.Dispose();
                _whitePixel = null;
                _spriteBatch?.Dispose();
                _spriteBatch = null;
                throw;
            }

            // Background: the bright startup artwork (best-effort; DrawBackground falls back to a
            // dark fill if it is unavailable).
            try
            {
                _backgroundTexture = _resourceManager.LoadTexture(TexturePath.StartupBackground);
            }
            catch
            {
                _backgroundTexture = null;
            }
        }


        private void LoadConfiguration()
        {
            // Create a working copy of the configuration
            var originalConfig = _configManager.Config;
            _workingConfig = new ConfigData
            {
                ScreenWidth = originalConfig.ScreenWidth,
                ScreenHeight = originalConfig.ScreenHeight,
                FullScreen = originalConfig.FullScreen,
                VSyncWait = originalConfig.VSyncWait,
                DTXManiaVersion = originalConfig.DTXManiaVersion,
                SkinPath = originalConfig.SkinPath,
                DTXPath = originalConfig.DTXPath,
                UseBoxDefSkin = originalConfig.UseBoxDefSkin,
                SystemSkinRoot = originalConfig.SystemSkinRoot,
                LastUsedSkin = originalConfig.LastUsedSkin,
                MasterVolume = originalConfig.MasterVolume,
                BGMVolume = originalConfig.BGMVolume,
                SEVolume = originalConfig.SEVolume,
                BufferSizeMs = originalConfig.BufferSizeMs,
                ScrollSpeed = originalConfig.ScrollSpeed,
                AutoPlay = originalConfig.AutoPlay,
                NoFail = originalConfig.NoFail,
                AudioLatencyOffsetMs = originalConfig.AudioLatencyOffsetMs
            };

            _hasUnsavedChanges = false;
        }

        private void LoadWorkingInputBindings()
        {
            var inputManagerCompat = _game.InputManager
                ?? throw new InvalidOperationException("InputManager not available");

            _workingDrumBindings = inputManagerCompat.ModularInputManager.KeyBindings.Clone();
            _workingSystemBindings = new Dictionary<Keys, InputCommandType>(inputManagerCompat.GetKeyMappingSnapshot());
            _navigationBindings = new Dictionary<Keys, InputCommandType>(_workingSystemBindings);
        }

        /// <summary>
        /// Reloads only <see cref="_workingDrumBindings"/> from the live InputManager, leaving
        /// pending system-key edits in <see cref="_workingSystemBindings"/> (and the navigation
        /// snapshot) intact. Used when returning from DrumConfigStage: that stage commits drum
        /// bindings on its Back = Save exit (which must be picked up here), but ConfigStage's own
        /// System Key Mapping panel writes only to the working copy and must survive the round-trip.
        /// </summary>
        /// <remarks>
        /// Also evicts from <see cref="_workingSystemBindings"/> any keyboard key now claimed by a
        /// drum lane in the reloaded drum bindings. DrumConfigStage performs the same eviction at its
        /// commit (see <c>DrumConfigStage.EvictSystemKeysClaimedByDrumLanes</c>); replaying it here
        /// ensures that conflict resolution is respected rather than being silently reverted when
        /// ConfigStage later saves its still-pending system mapping. Mirrors that method's logic.
        /// </remarks>
        private void ReloadWorkingDrumBindings()
        {
            var inputManagerCompat = _game.InputManager
                ?? throw new InvalidOperationException("InputManager not available");

            _workingDrumBindings = inputManagerCompat.ModularInputManager.KeyBindings.Clone();

            foreach (var kvp in _workingDrumBindings.ButtonToLane)
            {
                if (!KeyBindings.IsKeyboardButtonId(kvp.Key))
                    continue;

                // "Key.PageUp" -> Keys.PageUp
                if (Enum.TryParse(kvp.Key.Substring(4), out Keys sysKey))
                    _workingSystemBindings.Remove(sysKey);
            }
        }

        private void SetupConfigItems()
        {
            _configItems = new List<IConfigItem>();

            // Screen Resolution dropdown
            var resolutionItem = new DropdownConfigItem(
                "Screen Resolution",
                () => $"{_workingConfig.ScreenWidth}x{_workingConfig.ScreenHeight}",
                new[] { "1280x720", "1920x1080", "2560x1440", "3840x2160" },
                value =>
                {
                    var parts = value.Split('x');
                    if (parts.Length == 2 && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height))
                    {
                        _workingConfig.ScreenWidth = width;
                        _workingConfig.ScreenHeight = height;
                        _hasUnsavedChanges = true;
                        System.Diagnostics.Debug.WriteLine($"Resolution changed to {width}x{height}");
                    }
                }
            );

            // Fullscreen checkbox
            var fullscreenItem = new ToggleConfigItem(
                "Fullscreen",
                () => _workingConfig.FullScreen,
                value =>
                {
                    _workingConfig.FullScreen = value;
                    _hasUnsavedChanges = true;
                    System.Diagnostics.Debug.WriteLine($"Fullscreen changed to {value}");
                }
            );

            // VSync toggle
            var vsyncItem = new ToggleConfigItem(
                "VSync Wait",
                () => _workingConfig.VSyncWait,
                value =>
                {
                    _workingConfig.VSyncWait = value;
                    _hasUnsavedChanges = true;
                    System.Diagnostics.Debug.WriteLine($"VSync changed to {value}");
                });

            // NoFail toggle
            var noFailItem = new ToggleConfigItem(
                "No Fail",
                () => _workingConfig.NoFail,
                value =>
                {
                    _workingConfig.NoFail = value;
                    _hasUnsavedChanges = true;
                    System.Diagnostics.Debug.WriteLine($"NoFail changed to {value}");
                });

            // AutoPlay toggle
            var autoPlayItem = new ToggleConfigItem(
                "Auto Play",
                () => _workingConfig.AutoPlay,
                value =>
                {
                    _workingConfig.AutoPlay = value;
                    _hasUnsavedChanges = true;
                    System.Diagnostics.Debug.WriteLine($"AutoPlay changed to {value}");
                });

            var scrollSpeedItem = new IntegerConfigItem(
                "Scroll Speed",
                () => _workingConfig.ScrollSpeed,
                value =>
                {
                    _workingConfig.ScrollSpeed = ScrollSpeedRange.SnapAndClamp(value);
                    _hasUnsavedChanges = true;
                },
                minValue: ScrollSpeedRange.Min,
                maxValue: ScrollSpeedRange.Max,
                step: ScrollSpeedRange.Step,
                valueFormatter: ScrollSpeedRange.Format);

            var audioLatencyItem = new IntegerConfigItem(
                "Audio Latency Offset",
                () => _workingConfig.AudioLatencyOffsetMs,
                value =>
                {
                    _workingConfig.AudioLatencyOffsetMs = value;
                    _hasUnsavedChanges = true;
                },
                minValue: 0,
                maxValue: 500,
                step: 10,
                valueFormatter: v => $"{v} ms");

            var dtxFolderItem = new ReadOnlyConfigItem(
                "DTX Folder",
                () => _workingConfig.DTXPath);

            _configItems.Add(resolutionItem);
            _configItems.Add(fullscreenItem);
            _configItems.Add(vsyncItem);
            _configItems.Add(noFailItem);
            _configItems.Add(autoPlayItem);
            _configItems.Add(scrollSpeedItem);
            _configItems.Add(audioLatencyItem);
            _configItems.Add(dtxFolderItem);

            // Drum and system key mapping navigation items
            _configItems.Add(new NavigationConfigItem("Drum Key Mapping",
                () => NavigateToDrumConfig()));
            _configItems.Add(new NavigationConfigItem("System Key Mapping",
                () => OpenPanel(_systemPanel)));

            // NX score import (idempotent best/delta counters; history merged top-5)
            _configItems.Add(new NavigationConfigItem("Import NX Scores",
                () => StartNxScoreImport()));

            if (_configItems.Count > 0)
                _selectedIndex = 0;
        }

        private void InitializePanels()
        {
            var concreteConfig = _configManager as ConfigManager
                ?? throw new InvalidOperationException("ConfigManager must be ConfigManager instance");
            var inputManagerCompat = _game.InputManager
                ?? throw new InvalidOperationException("InputManager not available");

            _systemPanel = new SystemKeyAssignPanel(inputManagerCompat);
            _systemPanel._workingMappingProvider =
                () => new Dictionary<Keys, InputCommandType>(_workingSystemBindings);
            _systemPanel._liveDrumBindingsProvider =
                () => new Dictionary<string, int>(_workingDrumBindings.ButtonToLane);
            _systemPanel._navigationMappingProvider = () => new Dictionary<Keys, InputCommandType>(_workingSystemBindings);
            _systemPanel._commandPressedProvider = IsPanelCommandPressed;
            _systemPanel.Saved += OnPanelSaved;
            _systemPanel.Closed += OnPanelClosed;
        }

        private void OpenPanel(IKeyAssignPanel? panel)
        {
            if (panel == null) return;
            _activePanel = panel;
            _activePanel.Activate();
        }

        /// <summary>
        /// Hands the pending system-key edits to DrumConfigStage so its capture popup can reject a
        /// key the user just assigned to a required command (see DrumConfigStage.OnActivate). A
        /// snapshot is taken so later edits here cannot mutate the map DrumConfigStage rejects
        /// against mid-session. Drum bindings are intentionally NOT passed: this stage never edits
        /// them, so DrumConfigStage cloning the live drum mapping is always correct.
        /// </summary>
        private void NavigateToDrumConfig()
        {
            var shared = new Dictionary<string, object>
            {
                [DrumConfigStage.PendingSystemBindingsKey] =
                    new Dictionary<Keys, InputCommandType>(_workingSystemBindings)
            };
            ChangeStage(StageType.DrumConfig, new InstantTransition(), shared);
        }

        private void OnPanelSaved(object? sender, EventArgs e)
        {
            if (sender == _systemPanel)
            {
                _workingSystemBindings = new Dictionary<Keys, InputCommandType>(_systemPanel.GetWorkingMappingSnapshot());
            }

            _hasUnsavedChanges = true;
        }

        private void OnPanelClosed(object? sender, EventArgs e)
        {
            _activePanel = null;
        }

        /// <summary>
        /// Starts the NX score import asynchronously. Guarded against re-entry; updates
        /// <see cref="_importStatus"/> for the live status line. Best scores and counters
        /// use idempotent max/delta merges (safe to re-run); performance history is merged
        /// and deduplicated (top 5 kept). No confirmation prompt needed.
        /// </summary>
        private void StartNxScoreImport()
        {
            if (_importRunning)
                return;
            _importRunning = true;
            _importStatus = "Importing NX scores...";

            // Cancel any previous import (e.g. if stage was re-activated).
            _importCts?.Cancel();
            _importCts?.Dispose();
            _importCts = new CancellationTokenSource();
            var token = _importCts.Token;

            // Use a synchronous IProgress<T> so the callback runs inline on the thread
            // that calls Report(). Progress<T> would post asynchronously to the ThreadPool
            // (no SynchronizationContext in MonoGame's loop), allowing a stale queued
            // callback to overwrite the final status after the task completes.
            IProgress<NxImportProgress> progress = new InlineProgress<NxImportProgress>(p =>
            {
                _importStatus = $"Importing... {p.Imported} imported / {p.Scanned} scanned";
            });

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var result = await SongManager.Instance.ImportNxScoresAsync(progress, token);
                    _importStatus = result.DbUnavailable
                        ? "NX import unavailable (no database)"
                        : $"Imported {result.Imported} scores ({result.Scanned} charts scanned" +
                          (result.Errors > 0 ? $", {result.Errors} errors)" : ")");

                    // Refresh the in-memory song list so SongSelectionStage sees the
                    // updated play counts, ranks, etc. without requiring a restart.
                    if (result.Imported > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            await SongManager.Instance.RefreshSongListFromDatabaseAsync();
                        }
                        catch (System.Exception ex)
                        {
                            // Import succeeded; refresh failure is non-fatal — the user
                            // can restart or re-enter song select to trigger a rebuild.
                            System.Diagnostics.Debug.WriteLine(
                                $"ConfigStage: NX import succeeded but song list refresh failed: {ex}");
                        }
                    }
                }
                catch (System.OperationCanceledException)
                {
                    _importStatus = "NX import cancelled";
                }
                catch (System.Exception ex)
                {
                    var detail = ex.GetBaseException().Message;
                    _importStatus = $"NX import failed: {detail}";
                    System.Diagnostics.Debug.WriteLine($"ConfigStage: NX import failed: {ex}");
                }
                finally
                {
                    _importRunning = false;
                }
            });
        }

        #endregion

        #region Input Handling

        private void HandleInput()
        {
            if (IsConfigNavigationCommandPressed(InputCommandType.Back))
            {
                if (_hasUnsavedChanges)
                {
                    System.Diagnostics.Debug.WriteLine("Config: Back action with unsaved changes - discarding changes");
                }
                // Back = discard: clear the dirty flag so the next OnActivate takes the fresh
                // path and reloads from committed config. Without this, the cached stage
                // instance keeps the discarded working copy and OnActivate would skip
                // LoadConfiguration() (the preservation branch is only meant for returning
                // from DrumConfigStage, where pending edits must survive the round-trip).
                DiscardPendingChanges();
                System.Diagnostics.Debug.WriteLine("Config: Returning to Title stage");
                ChangeStage(StageType.Title, new CrossfadeTransition(0.3));
                return;
            }

            // Handle navigation
            if (IsConfigNavigationCommandPressed(InputCommandType.MoveUp))
            {
                _selectedIndex = (_selectedIndex - 1 + _configItems.Count + 2) % (_configItems.Count + 2); // +2 for buttons
            }
            else if (IsConfigNavigationCommandPressed(InputCommandType.MoveDown))
            {
                _selectedIndex = (_selectedIndex + 1) % (_configItems.Count + 2); // +2 for buttons
            }

            // Handle config item value editing (only for config items, not buttons)
            if (_selectedIndex < _configItems.Count)
            {
                var selectedItem = _configItems[_selectedIndex];

                // Left/Right arrows for value editing
                if (IsConfigNavigationCommandPressed(InputCommandType.MoveLeft))
                {
                    selectedItem.PreviousValue();
                }
                else if (IsConfigNavigationCommandPressed(InputCommandType.MoveRight))
                {
                    selectedItem.NextValue();
                }
                else if (IsConfigNavigationCommandPressed(InputCommandType.Activate))
                {
                    selectedItem.ToggleValue();
                }
            }
            else
            {
                // Handle button selection
                if (IsConfigNavigationCommandPressed(InputCommandType.Activate))
                {
                    int buttonIndex = _selectedIndex - _configItems.Count;
                    if (buttonIndex == 0) // Back button
                    {
                        OnBackButtonClicked(null, EventArgs.Empty);
                    }
                    else if (buttonIndex == 1) // Save button
                    {
                        OnSaveButtonClicked(null, EventArgs.Empty);
                    }
                }
            }
        }

        private bool IsConfigNavigationCommandPressed(InputCommandType command)
        {
            if (_game.InputManager?.IsCommandPressed(command) == true)
            {
                return true;
            }

            return _navigationBindings.Any(kvp =>
                kvp.Value == command &&
                _currentKeyboardState.IsKeyDown(kvp.Key) &&
                !_previousKeyboardState.IsKeyDown(kvp.Key));
        }

        private bool IsPanelCommandPressed(InputCommandType command)
        {
            return _workingSystemBindings.Any(kvp =>
                kvp.Value == command &&
                ((_game.InputManager?.IsKeyPressed((int)kvp.Key) == true)
                    || (_currentKeyboardState.IsKeyDown(kvp.Key) &&
                        !_previousKeyboardState.IsKeyDown(kvp.Key))));
        }

        #endregion

        #region Event Handlers

        private void OnBackButtonClicked(object sender, EventArgs e)
        {
            if (_hasUnsavedChanges)
            {
                System.Diagnostics.Debug.WriteLine("Back button clicked with unsaved changes - discarding changes");
            }
            // Back = discard: see HandleInput for why the dirty flag must be cleared here too.
            DiscardPendingChanges();
            System.Diagnostics.Debug.WriteLine("Back button clicked - returning to Title stage");
            ChangeStage(StageType.Title, new CrossfadeTransition(0.3));
        }

        private void OnSaveButtonClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Save button clicked - applying configuration");
            if (ApplyConfiguration())
                ChangeStage(StageType.Title, new CrossfadeTransition(0.3));
            else
                System.Diagnostics.Debug.WriteLine("Save failed - staying on Config stage");
        }

        #endregion

        #region Configuration Management

        /// <summary>
        /// Clears the dirty flag so the next <see cref="OnActivate"/> reloads the working
        /// config from the committed state. Called on every Back path (keyboard Back command
        /// and the Back button), which are explicit discard actions. The preservation branch
        /// in <see cref="OnActivate"/> is intended only for the DrumConfigStage round-trip;
        /// leaving it accidentally active after a discard caused the cached stage to surface
        /// (and later save) edits the user had explicitly thrown away.
        /// </summary>
        private void DiscardPendingChanges()
        {
            _hasUnsavedChanges = false;
        }

        /// <summary>
        /// Applies working configuration to disk first, then to live state on success.
        /// Returns true if the disk write succeeded.
        /// </summary>
        private bool ApplyConfiguration()
        {
            var config = _configManager.Config;

            // Snapshot current config so we can roll back if the disk write fails.
            int prevWidth = config.ScreenWidth;
            int prevHeight = config.ScreenHeight;
            bool prevFullScreen = config.FullScreen;
            bool prevVSync = config.VSyncWait;
            bool prevNoFail = config.NoFail;
            bool prevAutoPlay = config.AutoPlay;
            int prevScrollSpeed = config.ScrollSpeed;
            int prevAudioLatencyOffsetMs = config.AudioLatencyOffsetMs;
            // Binding collections are snapshotted/restored as a unit via BindingState (shared with
            // DrumConfigStage); the scalar fields above are local to this stage and restored by hand.
            var bindingSnapshot = config.SnapshotBindingState();

            // Stage 1: prepare in-memory config data from working copies
            config.ScreenWidth = _workingConfig.ScreenWidth;
            config.ScreenHeight = _workingConfig.ScreenHeight;
            config.FullScreen = _workingConfig.FullScreen;
            config.VSyncWait = _workingConfig.VSyncWait;
            config.NoFail = _workingConfig.NoFail;
            config.AutoPlay = _workingConfig.AutoPlay;
            config.ScrollSpeed = _workingConfig.ScrollSpeed;
            config.AudioLatencyOffsetMs = _workingConfig.AudioLatencyOffsetMs;

            if (_configManager is ConfigManager concreteConfig)
            {
                concreteConfig.SetKeyBindings(_workingDrumBindings);
                concreteConfig.SetSystemKeyBindings(_workingSystemBindings);
            }

            // Stage 2: write to disk — roll back in-memory changes on failure
            try
            {
                _configManager.SaveConfig(DTXMania.Game.Lib.Utilities.AppPaths.GetConfigFilePath());
                System.Diagnostics.Debug.WriteLine("Configuration saved successfully");
            }
            catch (Exception ex)
            {
                // Log the full exception (not just .Message) so Release builds — where
                // Debug.WriteLine is compiled out — still leave a diagnostic trail.
                _logger.LogError(ex, "ConfigStage: failed to save configuration");

                // Surface the failure on-screen so the user knows changes were not persisted and
                // can retry (mirrors DrumConfigStage). Without this the failure was invisible and a
                // subsequent Back silently discarded the edits.
                _saveError = $"Failed to save configuration: {ex.Message}";

                config.ScreenWidth = prevWidth;
                config.ScreenHeight = prevHeight;
                config.FullScreen = prevFullScreen;
                config.VSyncWait = prevVSync;
                config.NoFail = prevNoFail;
                config.AutoPlay = prevAutoPlay;
                config.ScrollSpeed = prevScrollSpeed;
                config.AudioLatencyOffsetMs = prevAudioLatencyOffsetMs;
                config.RestoreBindingState(bindingSnapshot);

                return false;
            }

            // Stage 3: disk write succeeded — now apply to live input state. The disk is the source
            // of truth; a throw here (e.g. ApplySystemBindings's remove-all-then-add-all mid-loop)
            // would leave the live input inconsistent with disk. Catch, log, and surface it so the
            // user knows the save persisted but the current session needs a retry/restart — rather
            // than silently completing with partially-applied input. Re-pressing Save & Exit re-writes
            // (idempotent) and retries the apply. Mirrors DrumConfigStage.Save().
            if (_game.InputManager != null)
            {
                try
                {
                    _game.InputManager.ModularInputManager.ReloadKeyBindings();
                    ApplySystemBindings(_game.InputManager, _workingSystemBindings);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ConfigStage: configuration saved to disk but live-input apply failed");
                    _saveError = $"Saved, but could not apply to this session: {ex.Message}. Press Save & Exit to retry.";
                    return false;
                }
            }

            _hasUnsavedChanges = false;
            _saveError = null; // persisted successfully: clear any prior failure banner
            return true;
        }

        #endregion

        #region Drawing (DTXMania Style)

        private void DrawBackground()
        {
            var viewport = GetViewport();
            var full = new Rectangle(0, 0, viewport.Width, viewport.Height);

            // Bright startup artwork, calmed by a translucent light scrim so the dark text reads;
            // a dark fill covers the case where the texture could not be loaded.
            if (_backgroundTexture?.Texture != null)
            {
                _spriteBatch.Draw(_backgroundTexture.Texture, full, Color.White);
                // Premultiplied light scrim (Color.White * a scales RGB and alpha together) so it
                // reads as a ~25% white wash under the default premultiplied AlphaBlend, not a wipe.
                _spriteBatch.Draw(_whitePixel, full, Color.White * 0.25f);
            }
            else
            {
                DrawFilledRectangle(full, new Color(16, 16, 32));
            }
        }

        private void DrawTitle()
        {
            const string titleText = "CONFIGURATION";
            int x = MenuX;
            int y = 50;

            if (_font != null)
            {
                // Center the title
                var textSize = _font.MeasureString(titleText);
                x = (1280 - (int)textSize.X) / 2; // Assume 1280 width for centering
                _font.DrawString(_spriteBatch, titleText, new Vector2(x, y), DarkText);
            }
            else
            {
                // Fallback to rectangle
                DrawTextRect(x, y, titleText.Length * 12, 20, DarkText);
            }
        }

        private void DrawConfigItems()
        {
            int x = MenuX;
            int y = MenuY;

            for (int i = 0; i < _configItems.Count; i++)
            {
                var item = _configItems[i];
                var displayText = item.GetDisplayText();
                bool isSelected = (i == _selectedIndex);

                // Draw selection background
                if (isSelected)
                {
                    DrawTextRect(x - 5, y - 2, MenuItemWidth + 10, MenuItemHeight, new Color(64, 64, 128, 150));
                }

                // Draw text
                if (_font != null)
                {
                    var textColor = isSelected ? Color.Yellow : DarkText;
                    var font = isSelected ? _boldFont : _font;
                    font.DrawString(_spriteBatch, displayText, new Vector2(x, y + 10), textColor);
                }
                else
                {
                    // Fallback to rectangle
                    var color = isSelected ? Color.Yellow : DarkText;
                    DrawTextRect(x, y + 10, displayText.Length * 8, 16, color);
                }

                y += MenuItemHeight;
            }
        }

        private void DrawButtons()
        {
            int x = MenuX;
            int y = MenuY + (_configItems.Count * MenuItemHeight) + 20;

            // Back button
            bool backSelected = (_selectedIndex == _configItems.Count);
            if (backSelected)
            {
                DrawTextRect(x - 5, y - 2, 100, 30, new Color(64, 64, 64, 150));
            }

            if (_font != null)
            {
                var textColor = backSelected ? Color.Yellow : DarkText;
                var font = backSelected ? _boldFont : _font;
                font.DrawString(_spriteBatch, "BACK", new Vector2(x, y + 5), textColor);
            }
            else
            {
                var color = backSelected ? Color.Yellow : DarkText;
                DrawTextRect(x, y + 5, 32, 16, color);
            }

            // Save button
            x += 150;
            bool saveSelected = (_selectedIndex == _configItems.Count + 1);
            if (saveSelected)
            {
                DrawTextRect(x - 5, y - 2, 120, 30, new Color(64, 96, 64, 150));
            }

            if (_font != null)
            {
                var textColor = saveSelected ? Color.Yellow : DarkText;
                var font = saveSelected ? _boldFont : _font;
                font.DrawString(_spriteBatch, "SAVE & EXIT", new Vector2(x, y + 5), textColor);
            }
            else
            {
                var color = saveSelected ? Color.Yellow : Color.Green;
                DrawTextRect(x, y + 5, 88, 16, color);
            }
        }

        private void DrawImportStatus()
        {
            if (string.IsNullOrEmpty(_importStatus) || _font == null)
                return;

            int x = MenuX;
            int y = MenuY + (_configItems.Count * MenuItemHeight) + 60;
            _font.DrawString(_spriteBatch, _importStatus, new Vector2(x, y), new Color(18, 64, 132));
        }

        // Surfaces a save failure so the user knows changes were not persisted and can retry.
        // Color matches DrumConfigStage's banner (and the popup's conflict-notice) for consistency.
        private void DrawSaveError()
        {
            if (string.IsNullOrEmpty(_saveError) || _font == null)
                return;

            var viewport = GetViewport();
            _font.DrawString(_spriteBatch, _saveError,
                new Vector2(MenuX, viewport.Height - 60), new Color(220, 70, 70));
        }

        private void DrawInstructions()
        {
            const string instructions = "Use UP/DOWN to navigate, LEFT/RIGHT to change values, ENTER to select, ESC to cancel";
            int x = 10;
            int y = 720 - 30; // Bottom of screen

            if (_font != null)
            {
                _font.DrawString(_spriteBatch, instructions, new Vector2(x, y), DarkText);
            }
            else
            {
                // Fallback to rectangle
                DrawTextRect(x, y, instructions.Length * 6, 12, DarkText);
            }
        }

        private void DrawTextRect(int x, int y, int width, int height, Color color)
        {
            if (_whitePixel != null)
            {
                DrawFilledRectangle(new Rectangle(x, y, width, height), color);
            }
        }

        [ExcludeFromCodeCoverage]
        protected virtual void BeginDrawFrame()
        {
            _spriteBatch.Begin();
        }

        [ExcludeFromCodeCoverage]
        protected virtual void EndDrawFrame()
        {
            _spriteBatch.End();
        }

        [ExcludeFromCodeCoverage]
        protected virtual void DrawFilledRectangle(Rectangle destinationRectangle, Color color)
        {
            if (_whitePixel == null)
                return;

            _spriteBatch.Draw(_whitePixel, destinationRectangle, color);
        }

        [ExcludeFromCodeCoverage]
        protected virtual Viewport GetViewport()
        {
            return _game.GraphicsDevice.Viewport;
        }

        private static void ApplySystemBindings(InputManager inputManager, IReadOnlyDictionary<Keys, InputCommandType> bindings)
        {
            // Take snapshot once, not once per enum value
            var snapshot = inputManager.GetKeyMappingSnapshot();
            foreach (var kvp in snapshot)
                inputManager.RemoveKeyMapping(kvp.Key);

            foreach (var kvp in bindings)
                inputManager.AddKeyMapping(kvp.Key, kvp.Value);
        }

        #endregion

        /// <summary>
        /// Synchronous <see cref="IProgress{T}"/> that invokes the callback inline
        /// on the thread calling <see cref="Report"/>. Unlike <see cref="System.Progress{T}"/>,
        /// this does not post asynchronously to the synchronization context, preventing
        /// stale progress callbacks from overwriting the final import status.
        /// </summary>
        private sealed class InlineProgress<T> : IProgress<T>
        {
            private readonly Action<T> _callback;
            public InlineProgress(Action<T> callback) => _callback = callback;
            public void Report(T value) => _callback(value);
        }
    }
}
