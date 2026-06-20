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
using DTXMania.Game.Lib.Utilities;


namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Configuration stage with basic settings management.
    /// Reads <see cref="IConfigManager.Config"/> as the single source of truth; every edit
    /// applies immediately through the typed setters (which live-apply to the runtime via the
    /// Phase 2 events and mark a deferred save dirty). The Back command (Esc) or the Exit
    /// button flushes the pending save and leaves — there is no working copy, no
    /// discard/rollback. Follows DTXMania patterns for config item handling.
    /// </summary>
    public class ConfigStage : BaseStage
    {
        #region Private Fields

        private IConfigManager _configManager;
        private List<IConfigItem> _configItems;
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

        // Light fallback used when the startup background texture is missing/unavailable, so the
        // dark UI text stays readable instead of going dark-on-dark.
        private static readonly Color FallbackBackgroundColor = new(220, 222, 230);

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
            System.Diagnostics.Debug.WriteLine("Activating Config Stage");

            InitializeGraphics();

            // Config is the single source of truth; reads pull directly from ConfigManager.Config
            // and the runtime (which mirrors Config). There is no working copy to (re)load.
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

            // Persist-on-edit safety net: flush any pending deferred save when leaving the stage.
            // Back/Exit already flushes; this covers other deactivation paths so an edited Config
            // is never left dirty in memory only.
            FlushPendingSaveSafely();

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

        private void SetupConfigItems()
        {
            _configItems = new List<IConfigItem>();

            // Screen Resolution dropdown
            var resolutionItem = new DropdownConfigItem(
                "Screen Resolution",
                () => $"{_configManager.Config.ScreenWidth}x{_configManager.Config.ScreenHeight}",
                new[] { "1280x720", "1920x1080", "2560x1440", "3840x2160" },
                value =>
                {
                    var parts = value.Split('x');
                    if (parts.Length == 2 && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height))
                    {
                        _configManager.SetResolution(width, height);
                        System.Diagnostics.Debug.WriteLine($"Resolution changed to {width}x{height}");
                    }
                }
            );

            // Fullscreen checkbox
            var fullscreenItem = new ToggleConfigItem(
                "Fullscreen",
                () => _configManager.Config.FullScreen,
                value =>
                {
                    _configManager.SetFullscreen(value);
                    System.Diagnostics.Debug.WriteLine($"Fullscreen changed to {value}");
                }
            );

            // VSync toggle
            var vsyncItem = new ToggleConfigItem(
                "VSync Wait",
                () => _configManager.Config.VSyncWait,
                value =>
                {
                    _configManager.SetVSync(value);
                    System.Diagnostics.Debug.WriteLine($"VSync changed to {value}");
                });

            // NoFail toggle
            var noFailItem = new ToggleConfigItem(
                "No Fail",
                () => _configManager.Config.NoFail,
                value =>
                {
                    _configManager.SetNoFail(value);
                    System.Diagnostics.Debug.WriteLine($"NoFail changed to {value}");
                });

            // AutoPlay toggle
            var autoPlayItem = new ToggleConfigItem(
                "Auto Play",
                () => _configManager.Config.AutoPlay,
                value =>
                {
                    _configManager.SetAutoPlay(value);
                    System.Diagnostics.Debug.WriteLine($"AutoPlay changed to {value}");
                });

            var scrollSpeedItem = new IntegerConfigItem(
                "Scroll Speed",
                () => _configManager.Config.ScrollSpeed,
                value =>
                {
                    // SetScrollSpeed snaps+clamps internally and raises ScrollSpeedChanged (which
                    // the runtime mirrors), so the live-apply that ConfigStage previously bypassed
                    // now happens here.
                    _configManager.SetScrollSpeed(AppPaths.GetConfigFilePath(), value);
                },
                minValue: ScrollSpeedRange.Min,
                maxValue: ScrollSpeedRange.Max,
                step: ScrollSpeedRange.Step,
                valueFormatter: ScrollSpeedRange.Format);

            var audioLatencyItem = new IntegerConfigItem(
                "Audio Latency Offset",
                () => _configManager.Config.AudioLatencyOffsetMs,
                value =>
                {
                    _configManager.SetAudioLatency(value);
                },
                minValue: 0,
                maxValue: 500,
                step: 10,
                valueFormatter: v => $"{v} ms");

            var dtxFolderItem = new ReadOnlyConfigItem(
                "DTX Folder",
                () => _configManager.Config.DTXPath);

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
            var inputManagerCompat = _game.InputManager
                ?? throw new InvalidOperationException("InputManager not available");

            _systemPanel = new SystemKeyAssignPanel(inputManagerCompat);
            // Providers read the RUNTIME, which always mirrors Config via the Phase 2 events
            // (ConfigManager.KeyBindingsChanged/SystemKeyBindingsChanged -> InputManagerCompat
            // reloads). Config is truth, so there is no working copy to read.
            _systemPanel._workingMappingProvider =
                () => new Dictionary<Keys, InputCommandType>(inputManagerCompat.GetKeyMappingSnapshot());
            _systemPanel._liveDrumBindingsProvider =
                () => new Dictionary<string, int>(inputManagerCompat.ModularInputManager.KeyBindings.ButtonToLane);
            _systemPanel._navigationMappingProvider =
                () => new Dictionary<Keys, InputCommandType>(inputManagerCompat.GetKeyMappingSnapshot());
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
        /// Navigates to DrumConfigStage. DrumConfigStage reads ConfigManager as its single source of
        /// truth (the runtime mirrors Config via the Phase 2 events), so there is no pending handoff
        /// to forward.
        /// </summary>
        private void NavigateToDrumConfig()
        {
            ChangeStage(StageType.DrumConfig, new InstantTransition());
        }

        private void OnPanelSaved(object? sender, EventArgs e)
        {
            // Persist-on-edit: the panel's working snapshot is written to Config immediately via the
            // typed setter, which live-applies to the runtime (SystemKeyBindingsChanged) and marks a
            // deferred save dirty. Flushed on stage exit.
            if (sender == _systemPanel)
            {
                _configManager.SetSystemKeyBindings(_systemPanel.GetWorkingMappingSnapshot());
            }
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
                // Back = exit: edits are already live+dirty, so flush the pending save and leave.
                System.Diagnostics.Debug.WriteLine("Config: Returning to Title stage");
                FlushPendingSaveSafely();
                ChangeStage(StageType.Title, new CrossfadeTransition(0.3));
                return;
            }

            // Handle navigation
            if (IsConfigNavigationCommandPressed(InputCommandType.MoveUp))
            {
                _selectedIndex = (_selectedIndex - 1 + _configItems.Count + 1) % (_configItems.Count + 1); // +1 for Exit button
            }
            else if (IsConfigNavigationCommandPressed(InputCommandType.MoveDown))
            {
                _selectedIndex = (_selectedIndex + 1) % (_configItems.Count + 1); // +1 for Exit button
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
                // Handle button selection (the only button is Exit at buttonIndex 0)
                if (IsConfigNavigationCommandPressed(InputCommandType.Activate))
                {
                    int buttonIndex = _selectedIndex - _configItems.Count;
                    if (buttonIndex == 0) // Exit button
                    {
                        OnExitButtonClicked(null, EventArgs.Empty);
                    }
                }
            }
        }

        private bool IsConfigNavigationCommandPressed(InputCommandType command)
        {
            if (_game.InputManager?.IsCommandPressed(command) == true)
                return true;

            // Read the runtime system map (= Config truth; the runtime mirrors Config via the
            // Phase 2 events). There is no separate navigation snapshot.
            var systemMap = _game.InputManager?.GetKeyMappingSnapshot();
            return systemMap != null && systemMap.Any(kvp =>
                kvp.Value == command &&
                _currentKeyboardState.IsKeyDown(kvp.Key) &&
                !_previousKeyboardState.IsKeyDown(kvp.Key));
        }

        private bool IsPanelCommandPressed(InputCommandType command)
        {
            var systemMap = _game.InputManager?.GetKeyMappingSnapshot();
            return systemMap != null && systemMap.Any(kvp =>
                kvp.Value == command &&
                ((_game.InputManager?.IsKeyPressed((int)kvp.Key) == true)
                 || (_currentKeyboardState.IsKeyDown(kvp.Key) && !_previousKeyboardState.IsKeyDown(kvp.Key))));
        }

        #endregion

        #region Event Handlers

        private void OnExitButtonClicked(object sender, EventArgs e)
        {
            // Edits are already live+dirty; Exit just flushes to disk and leaves.
            System.Diagnostics.Debug.WriteLine("Exit button clicked - returning to Title stage");
            FlushPendingSaveSafely();
            ChangeStage(StageType.Title, new CrossfadeTransition(0.3));
        }

        /// <summary>
        /// Flushes the pending deferred save, swallowing disk failures so a save error can never
        /// trap the user on the config screen. Under persist-on-edit (Decision A), a flush failure
        /// is logged and the dirty flag is retained for retry on the next flush; the stage always
        /// proceeds to leave. <see cref="ConfigManager.FlushPendingSave"/> already swallows
        /// internally, but this guards against a custom <see cref="IConfigManager"/> whose
        /// FlushPendingSave propagates.
        /// </summary>
        private void FlushPendingSaveSafely()
        {
            try
            {
                _configManager.FlushPendingSave();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ConfigStage: failed to flush pending save: {ex}");
            }
        }

        #endregion

        #region Drawing (DTXMania Style)

        private void DrawBackground()
        {
            var viewport = GetViewport();
            var full = new Rectangle(0, 0, viewport.Width, viewport.Height);

            // Bright startup artwork, calmed by a translucent light scrim so the dark text reads;
            // a light fill covers the case where the texture could not be loaded, keeping DarkText
            // legible instead of going dark-on-dark.
            if (_backgroundTexture?.Texture != null)
            {
                _spriteBatch.Draw(_backgroundTexture.Texture, full, Color.White);
                // Premultiplied light scrim (Color.White * a scales RGB and alpha together) so it
                // reads as a ~25% white wash under the default premultiplied AlphaBlend, not a wipe.
                _spriteBatch.Draw(_whitePixel, full, Color.White * 0.25f);
            }
            else
            {
                DrawFilledRectangle(full, FallbackBackgroundColor);
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

            // Exit button (the sole action button; persist-on-edit removed the save/discard split)
            bool exitSelected = (_selectedIndex == _configItems.Count);
            if (exitSelected)
            {
                DrawTextRect(x - 5, y - 2, 120, 30, new Color(64, 96, 64, 150));
            }

            if (_font != null)
            {
                var textColor = exitSelected ? Color.Yellow : DarkText;
                var font = exitSelected ? _boldFont : _font;
                font.DrawString(_spriteBatch, "EXIT", new Vector2(x, y + 5), textColor);
            }
            else
            {
                var color = exitSelected ? Color.Yellow : Color.Green;
                DrawTextRect(x, y + 5, 32, 16, color);
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

        private void DrawInstructions()
        {
            // Persist-on-edit: changes are applied live and saved automatically, so ESC exits
            // (flushing any pending save) rather than discarding edits. Saying "cancel" here would
            // mislead users into expecting their changes to be reverted on exit.
            const string instructions = "Use UP/DOWN to navigate, LEFT/RIGHT to change values, ENTER to select, ESC to exit (changes save automatically)";
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
