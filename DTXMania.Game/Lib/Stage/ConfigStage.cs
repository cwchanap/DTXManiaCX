#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Config;
using DTXMania.Game.Lib.Stage.KeyAssign;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using DTXMania.Game.Lib.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// NX-style master-detail configuration stage: a left category menu (System / Drums / Exit),
    /// a right per-category item list, a description panel, and header/footer panels. Reads
    /// <see cref="IConfigManager.Config"/> as the single source of truth; every edit applies
    /// immediately through the typed setters and marks a deferred save dirty. Back/Exit flushes
    /// the pending save and leaves — there is no working copy. Two focus states mirror the NX
    /// bFocusIsOnMenu pattern.
    /// </summary>
    public class ConfigStage : BaseStage
    {
        #region Private Fields

        // NullLogger until OnActivate swaps in the game's factory. Lets reflection-based tests
        // (which skip OnActivate) exercise the helpers without NullReferenceException, while real
        // runtime gets structured logging that survives Release builds (Debug.WriteLine is
        // [Conditional("DEBUG")] and compiles out in Release).
        private ILogger<ConfigStage> _logger = NullLogger<ConfigStage>.Instance;

        private IConfigManager _configManager;
        private List<ConfigCategory> _categories = new();
        private int _currentCategoryIndex = 0;
        private bool _focusOnMenu = true;

        // Eased scroll position (fractional item index currently at the focus row). Tracks the
        // selected item; snaps on a wrap/jump so last<->first never scrolls the whole list.
        private double _itemScroll;

        private KeyboardState _previousKeyboardState;
        private KeyboardState _currentKeyboardState;

        private SystemKeyAssignPanel? _systemPanel;
        private IKeyAssignPanel? _activePanel;

        private SpriteBatch _spriteBatch;
        private Texture2D _whitePixel;
        private IFont _font;
        private IFont _boldFont;
        private IResourceManager _resourceManager;

        private ITexture? _backgroundTexture;
        private ITexture? _itemBarTexture;
        private ITexture? _menuPanelTexture;
        private ITexture? _menuCursorTexture;
        private ITexture? _headerPanelTexture;
        private ITexture? _footerPanelTexture;
        private ITexture? _itemBoxTexture;
        private ITexture? _itemBoxCursorTexture;
        private ITexture? _descriptionPanelTexture;

        // Light text reads on the dark NX config background.
        private static readonly Color LightText = new(235, 238, 248);
        // Value text sits on the itembox's white right cell, so it must be dark.
        private static readonly Color ValueDarkText = new(24, 24, 32);
        // Selected name / nav marker sit on a dark cell -> bright warm highlight.
        private static readonly Color SelectedNameText = new(255, 238, 120);
        // Selected value sits on the white cell -> dark warm highlight (readable on white).
        private static readonly Color SelectedValueText = new(168, 52, 0);
        // Selected menu label sits on the light menu cursor bar -> dark text.
        private static readonly Color SelectedMenuText = new(36, 24, 72);
        // Description title sits on the panel's white upper region -> dark text.
        private static readonly Color DescriptionTitleText = new(24, 24, 32);
        private static readonly Color MenuCursorFallback = new(64, 96, 160, 180);
        private static readonly Color ItemCursorFallback = new(96, 96, 160, 180);
        private static readonly Color ImportStatusColor = new(180, 220, 255);

        // Dark fill behind the UI when the background art is unavailable, so the light text stays
        // legible (light-on-light was the failure mode to avoid).
        private static readonly Color FallbackBackgroundColor = new(18, 20, 34);

        // Semi-transparent panel stand-ins drawn when the NX skin art is missing, so each panel
        // region stays visually delimited and text remains legible without the texture.
        private static readonly Color PanelFallbackColor = new(28, 32, 54, 220);
        private static readonly Color ItemBoxFallbackColor = new(34, 40, 68, 200);

        // Inner board: dark translucent fill with a purple frame (matching the itembox border),
        // drawn over the background to contain the config content and keep it readable.
        private static readonly Color InnerBoardColor = new(8, 10, 22, 196);
        private static readonly Color InnerBoardBorderColor = new(74, 62, 150, 224);

        private volatile string _importStatus = "";
        private volatile bool _importRunning;
        private CancellationTokenSource? _importCts;

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
            _logger.LogDebug("Activating Config Stage");

            InitializeGraphics();
            SetupConfigItems();
            InitializePanels();

            _currentCategoryIndex = 0;
            _focusOnMenu = true;
            _itemScroll = 0;

            // Clear any import status left over from a previous visit. StageManager reuses this
            // instance, so without this a stale "Imported N scores" / "cancelled" message would
            // survive leaving and re-entering Config. (_importRunning is intentionally NOT reset
            // here: the background task clears it in its finally, and the StartNxScoreImport guard
            // correctly serializes a still-draining task.)
            _importStatus = "";

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
            UpdateItemScroll(deltaTime);
        }

        // Ease the scroll position toward the selected item so it settles at the focus row.
        // A wrap or multi-row jump snaps instead of scrolling the whole list backward.
        private void UpdateItemScroll(double deltaTime)
        {
            if (_categories.Count == 0)
                return;

            var category = _categories[_currentCategoryIndex];
            double target = category.HasItems ? category.SelectedIndex : 0;
            double delta = target - _itemScroll;
            if (Math.Abs(delta) > 1.5)
            {
                _itemScroll = target;
                return;
            }

            double factor = Math.Min(1.0, deltaTime * 15.0);
            _itemScroll += delta * factor;
            if (Math.Abs(target - _itemScroll) < 0.01)
                _itemScroll = target;
        }

        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;

            BeginDrawFrame();

            DrawConfigBackground();
            DrawInnerBoard();
            DrawItemBar();
            DrawCategoryMenu();
            DrawItemList();
            DrawDescriptionPanel();
            DrawHeaderFooter();
            DrawImportStatus();

            if (_activePanel?.IsActive == true)
            {
                // Panel draws in the same scaled 1280x720 space as the rest of the stage, so it
                // receives the virtual dimensions (not the raw viewport) and centers within them.
                _activePanel.Draw(_spriteBatch, _font, _boldFont, _whitePixel,
                    ConfigUILayout.ScreenWidth, ConfigUILayout.ScreenHeight);
            }

            EndDrawFrame();
        }

        protected override void OnDeactivate()
        {
            _logger.LogDebug("Deactivating Config Stage");

            FlushPendingSaveSafely();

            _importCts?.Cancel();

            _activePanel?.Deactivate();
            _activePanel = null;

            _font?.RemoveReference();
            _font = null;
            _boldFont?.RemoveReference();
            _boldFont = null;
            ReleaseTextures();

            // OnActivate allocates a fresh SpriteBatch/white-pixel each entry. Dispose them here
            // (symmetric with InitializeGraphics and the DrumConfigStage sibling) so a
            // Title→Config→Title round-trip doesn't leak one of each on every visit, since
            // StageManager reuses this instance.
            _spriteBatch?.Dispose();
            _spriteBatch = null!;
            _whitePixel?.Dispose();
            _whitePixel = null!;

            _previousKeyboardState = default;
            _currentKeyboardState = default;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _logger.LogDebug("Disposing Config Stage resources");

                _importCts?.Cancel();
                _importCts?.Dispose();
                _importCts = null;

                _whitePixel?.Dispose();
                _spriteBatch?.Dispose();

                _font?.RemoveReference();
                _boldFont?.RemoveReference();
                ReleaseTextures();

                _whitePixel = null;
                _spriteBatch = null;
                _font = null;
                _boldFont = null;
                _resourceManager = null; // shared game-wide instance; do NOT dispose
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

            // All skin art is best-effort; every draw is null-guarded with a fill/text fallback.
            _backgroundTexture = TryLoadTexture(TexturePath.ConfigBackground);
            _itemBarTexture = TryLoadTexture(TexturePath.ConfigItemBar);
            _menuPanelTexture = TryLoadTexture(TexturePath.ConfigMenuPanel);
            _menuCursorTexture = TryLoadTexture(TexturePath.ConfigMenuCursor);
            _headerPanelTexture = TryLoadTexture(TexturePath.ConfigHeaderPanel);
            _footerPanelTexture = TryLoadTexture(TexturePath.ConfigFooterPanel);
            _itemBoxTexture = TryLoadTexture(TexturePath.ConfigItemBox);
            _itemBoxCursorTexture = TryLoadTexture(TexturePath.ConfigItemBoxCursor);
            _descriptionPanelTexture = TryLoadTexture(TexturePath.ConfigDescriptionPanel);
        }

        /// <summary>
        /// Load an optional skin texture, returning null when the asset is absent. ResourceManager
        /// never throws for a missing/invalid texture — it returns a 1x1 white fallback (see
        /// <see cref="ResourceManager.CreateFallbackTexture"/>), which would stretch a single texel
        /// over the panel and wash the screen white. A real asset is always larger than 1x1, so a
        /// 1x1 result is treated as "not present": its reference is released and null is returned so
        /// each draw method takes its documented fallback-fill branch.
        /// </summary>
        private ITexture? TryLoadTexture(string path)
        {
            ITexture? texture = null;
            try { texture = _resourceManager.LoadTexture(path); }
            catch (Exception ex) { _logger.LogDebug(ex, "optional texture unavailable: {Path}", path); return null; }

            if (texture == null || texture.Width <= 1 || texture.Height <= 1)
            {
                _logger.LogDebug("optional texture unavailable (asset missing or 1x1 fallback): {Path}", path);
                texture?.RemoveReference();
                return null;
            }

            return texture;
        }

        private void ReleaseTextures()
        {
            _backgroundTexture?.RemoveReference();
            _backgroundTexture = null;
            _itemBarTexture?.RemoveReference();
            _itemBarTexture = null;
            _menuPanelTexture?.RemoveReference();
            _menuPanelTexture = null;
            _menuCursorTexture?.RemoveReference();
            _menuCursorTexture = null;
            _headerPanelTexture?.RemoveReference();
            _headerPanelTexture = null;
            _footerPanelTexture?.RemoveReference();
            _footerPanelTexture = null;
            _itemBoxTexture?.RemoveReference();
            _itemBoxTexture = null;
            _itemBoxCursorTexture?.RemoveReference();
            _itemBoxCursorTexture = null;
            _descriptionPanelTexture?.RemoveReference();
            _descriptionPanelTexture = null;
        }

        private void SetupConfigItems()
        {
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
                    }
                })
            { Description = "Sets the game window resolution." };

            var fullscreenItem = new ToggleConfigItem(
                "Fullscreen",
                () => _configManager.Config.FullScreen,
                value => _configManager.SetFullscreen(value))
            { Description = "Toggles fullscreen display mode." };

            var vsyncItem = new ToggleConfigItem(
                "VSync Wait",
                () => _configManager.Config.VSyncWait,
                value => _configManager.SetVSync(value))
            { Description = "Syncs drawing to the monitor refresh rate to reduce tearing." };

            var audioLatencyItem = new IntegerConfigItem(
                "Audio Latency Offset",
                () => _configManager.Config.AudioLatencyOffsetMs,
                value => _configManager.SetAudioLatency(value),
                minValue: 0,
                maxValue: 500,
                step: 10,
                valueFormatter: v => $"{v} ms")
            { Description = "Shifts audio timing to compensate for output latency." };

            var dtxFolderItem = new ReadOnlyConfigItem(
                "DTX Folder",
                () => _configManager.Config.DTXPath)
            { Description = "Folder scanned for songs and charts (read-only)." };

            var systemKeyItem = new NavigationConfigItem("System Key Mapping",
                () => OpenPanel(_systemPanel))
            { Description = "Assign keys for menu and system commands." };

            var importItem = new NavigationConfigItem("Import NX Scores",
                () => StartNxScoreImport())
            { Description = "Import play counts and scores from a DTXManiaNX database." };

            var scrollSpeedItem = new IntegerConfigItem(
                "Scroll Speed",
                () => _configManager.Config.ScrollSpeed,
                value => _configManager.SetScrollSpeed(AppPaths.GetConfigFilePath(), value),
                minValue: ScrollSpeedRange.Min,
                maxValue: ScrollSpeedRange.Max,
                step: ScrollSpeedRange.Step,
                valueFormatter: ScrollSpeedRange.Format)
            { Description = "Sets how fast notes scroll down the lanes." };

            var autoPlayItem = new ToggleConfigItem(
                "Auto Play",
                () => _configManager.Config.AutoPlay,
                value => _configManager.SetAutoPlay(value))
            { Description = "Plays the chart automatically without input." };

            var noFailItem = new ToggleConfigItem(
                "No Fail",
                () => _configManager.Config.NoFail,
                value => _configManager.SetNoFail(value))
            { Description = "Continue playing even when the life gauge is empty." };

            var drumKeyItem = new NavigationConfigItem("Drum Key Mapping",
                () => NavigateToDrumConfig())
            { Description = "Assign keys for each drum lane." };

            var systemItems = new List<IConfigItem>
            {
                resolutionItem, fullscreenItem, vsyncItem, audioLatencyItem,
                dtxFolderItem, systemKeyItem, importItem
            };

            var drumItems = new List<IConfigItem>
            {
                scrollSpeedItem, autoPlayItem, noFailItem, drumKeyItem
            };

            _categories = new List<ConfigCategory>
            {
                new ConfigCategory("System",
                    "System settings: display, audio, file paths, and system key bindings.",
                    systemItems),
                new ConfigCategory("Drums",
                    "Drum gameplay settings and drum pad key bindings.",
                    drumItems),
                new ConfigCategory("Exit",
                    "Save changes and return to the title screen.",
                    new List<IConfigItem>())
            };

            _currentCategoryIndex = 0;
            _focusOnMenu = true;
        }

        private void InitializePanels()
        {
            var inputManagerCompat = _game.InputManager
                ?? throw new InvalidOperationException("InputManager not available");

            _systemPanel = new SystemKeyAssignPanel(inputManagerCompat);
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

        private void NavigateToDrumConfig()
        {
            ChangeStage(StageType.DrumConfig, new InstantTransition());
        }

        private void OnPanelSaved(object? sender, EventArgs e)
        {
            if (sender == _systemPanel)
            {
                _configManager.SetSystemKeyBindings(_systemPanel.GetWorkingMappingSnapshot());
            }
        }

        private void OnPanelClosed(object? sender, EventArgs e)
        {
            _activePanel = null;
        }

        private void StartNxScoreImport()
        {
            if (_importRunning)
                return;
            _importRunning = true;
            _importStatus = "Importing NX scores...";

            _importCts?.Cancel();
            _importCts?.Dispose();
            _importCts = new CancellationTokenSource();
            var token = _importCts.Token;

            IProgress<NxImportProgress> progress = new InlineProgress<NxImportProgress>(p =>
            {
                _importStatus = $"Importing... {p.Imported} imported / {p.Scanned} scanned";
            });

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                NxImportResult? result = null;
                try
                {
                    result = await SongManager.Instance.ImportNxScoresAsync(progress, token);
                    _importStatus = result.DbUnavailable
                        ? "NX import unavailable (no database)"
                        : $"Imported {result.Imported} scores ({result.Scanned} charts scanned" +
                          (result.Errors > 0 ? $", {result.Errors} errors)" : ")");

                    if (result.Imported > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            await SongManager.Instance.RefreshSongListFromDatabaseAsync();
                        }
                        catch (System.Exception ex)
                        {
                            // Scores were written, but the live song list didn't refresh. Surface the
                            // partial failure so the user isn't left looking at a stale list with no
                            // explanation (a restart will pick up the imported scores).
                            _importStatus = $"Imported {result.Imported} scores, but the song list didn't " +
                                $"refresh (restart to see them). {ex.GetBaseException().Message}";
                            _logger.LogWarning(ex, "ConfigStage: NX import succeeded but song list refresh failed");
                        }
                    }
                }
                catch (System.OperationCanceledException)
                {
                    // If cancellation landed AFTER a successful import (between the write and the
                    // song-list refresh, at the ThrowIfCancellationRequested above), scores ARE on
                    // disk — report partial success instead of a bare "cancelled" that hides the
                    // write. Mid-import cancellation (result null / nothing imported) is a real cancel.
                    _importStatus = (result != null && result.Imported > 0)
                        ? $"Imported {result.Imported} scores (cancelled before the song list could refresh)"
                        : "NX import cancelled";
                }
                catch (System.Exception ex)
                {
                    var detail = ex.GetBaseException().Message;
                    _importStatus = $"NX import failed: {detail}";
                    _logger.LogError(ex, "ConfigStage: NX import failed");
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
            // Defensive: SetupConfigItems (called from OnActivate) always populates _categories,
            // so this is never empty during normal play. Guard mirrors the draw methods and keeps
            // the modular-arithmetic / index access below safe if the invariant ever breaks.
            if (_categories.Count == 0)
                return;

            if (_focusOnMenu)
                HandleMenuInput();
            else
                HandleItemInput();
        }

        private void HandleMenuInput()
        {
            if (IsConfigNavigationCommandPressed(InputCommandType.Back))
            {
                ExitToTitle();
                return;
            }

            if (IsConfigNavigationCommandPressed(InputCommandType.MoveUp))
            {
                _currentCategoryIndex = (_currentCategoryIndex - 1 + _categories.Count) % _categories.Count;
            }
            else if (IsConfigNavigationCommandPressed(InputCommandType.MoveDown))
            {
                _currentCategoryIndex = (_currentCategoryIndex + 1) % _categories.Count;
            }

            if (IsConfigNavigationCommandPressed(InputCommandType.Activate) ||
                IsConfigNavigationCommandPressed(InputCommandType.MoveRight))
            {
                var category = _categories[_currentCategoryIndex];
                if (category.HasItems)
                    _focusOnMenu = false;
                else
                    ExitToTitle();
            }
        }

        private void HandleItemInput()
        {
            var category = _categories[_currentCategoryIndex];

            if (IsConfigNavigationCommandPressed(InputCommandType.Back))
            {
                _focusOnMenu = true;
                return;
            }

            if (IsConfigNavigationCommandPressed(InputCommandType.MoveUp))
            {
                category.MoveSelectionUp();
            }
            else if (IsConfigNavigationCommandPressed(InputCommandType.MoveDown))
            {
                category.MoveSelectionDown();
            }

            var item = category.SelectedItem;
            if (item == null)
                return;

            // Navigation items (sub-panel openers, stage transitions, async imports) have no
            // cyclic value to adjust. Left/Right are the value-adjust keys, so letting them
            // trigger navigation would surprise the player — e.g. an instant ChangeStage to
            // DrumConfig or kicking off a long NX import from a stray keypress. Restrict them
            // to the Activate path only (mirrors HandleMenuInput, where Left never activates).
            if (item is NavigationConfigItem)
            {
                if (IsConfigNavigationCommandPressed(InputCommandType.Activate))
                    item.ToggleValue();
                return;
            }

            if (IsConfigNavigationCommandPressed(InputCommandType.MoveLeft))
            {
                item.PreviousValue();
            }
            else if (IsConfigNavigationCommandPressed(InputCommandType.MoveRight))
            {
                item.NextValue();
            }
            else if (IsConfigNavigationCommandPressed(InputCommandType.Activate))
            {
                item.ToggleValue();
            }
        }

        private void ExitToTitle()
        {
            _logger.LogDebug("Config: Returning to Title stage");
            FlushPendingSaveSafely();
            ChangeStage(StageType.Title, new CrossfadeTransition(0.3));
        }

        private bool IsConfigNavigationCommandPressed(InputCommandType command)
        {
            if (_game.InputManager?.IsCommandPressed(command) == true)
                return true;

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

        private void FlushPendingSaveSafely()
        {
            try
            {
                _configManager.FlushPendingSave();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ConfigStage: failed to flush pending save");
            }
        }

        #endregion

        #region Drawing (NX master-detail)

        protected virtual void DrawConfigBackground()
        {
            // Draw the GALAXY WAVE background at the fixed 1280x720 virtual rect (not the raw
            // viewport). The frame-wide viewport transform scales it uniformly to fill the screen,
            // preserving aspect — drawing at viewport size here would stretch it out of proportion.
            var full = ConfigUILayout.BackgroundRect;

            if (_backgroundTexture?.Texture != null)
                _spriteBatch.Draw(_backgroundTexture.Texture, full, Color.White);
            else
                DrawFilledRectangle(full, FallbackBackgroundColor);
        }

        // Dark translucent framed board over the background, behind the menu/item/description
        // panels, so the config content stays readable against the busy background.
        protected virtual void DrawInnerBoard()
        {
            DrawFilledRectangle(ConfigUILayout.InnerBoardBorderRect, InnerBoardBorderColor);
            DrawFilledRectangle(ConfigUILayout.InnerBoardRect, InnerBoardColor);
        }

        protected virtual void DrawItemBar()
        {
            if (_itemBarTexture?.Texture != null)
                _spriteBatch.Draw(_itemBarTexture.Texture, ConfigUILayout.ItemBarRect, Color.White);
            else
                DrawFilledRectangle(ConfigUILayout.ItemBarRect, PanelFallbackColor);
        }

        protected virtual void DrawCategoryMenu()
        {
            if (_categories.Count == 0)
                return;

            if (_menuPanelTexture?.Texture != null)
                _spriteBatch.Draw(_menuPanelTexture.Texture, ConfigUILayout.MenuPanelRect, Color.White);
            else
                DrawFilledRectangle(ConfigUILayout.MenuPanelRect, PanelFallbackColor);

            var cursorRect = ConfigUILayout.MenuCursorRect(_currentCategoryIndex);
            if (_menuCursorTexture?.Texture != null)
            {
                var tint = _focusOnMenu ? Color.White : new Color(255, 255, 255, 128);
                _spriteBatch.Draw(_menuCursorTexture.Texture, cursorRect, tint);
            }
            else
            {
                DrawFilledRectangle(cursorRect, MenuCursorFallback);
            }

            if (_font == null || _boldFont == null)
                return;

            for (int i = 0; i < _categories.Count; i++)
            {
                bool selected = i == _currentCategoryIndex;
                var font = selected ? _boldFont : _font;
                var color = selected ? SelectedMenuText : LightText;
                var label = _categories[i].Name;
                var size = font.MeasureString(label);
                var cursor = ConfigUILayout.MenuCursorRect(i);
                // Center the label horizontally on the panel and vertically within the cursor band.
                var pos = new Vector2(
                    ConfigUILayout.MenuLabelCenterX - size.X / 2f,
                    cursor.Y + (ConfigUILayout.MenuCursorHeight - size.Y) / 2f);
                font.DrawString(_spriteBatch, label, pos, color);
            }
        }

        protected virtual void DrawItemList()
        {
            if (_categories.Count == 0)
                return;

            var category = _categories[_currentCategoryIndex];
            var items = category.Items;

            // Box pass. Every item — value and navigation alike — uses the same normal itembox
            // (dark name cell + white value cell) so the list reads uniformly. Only real items
            // are drawn (non-cyclic), each at its scrolled Y.
            for (int i = 0; i < items.Count; i++)
            {
                int rowTopY = ConfigUILayout.RowTopY(i, _itemScroll);
                if (!ConfigUILayout.IsRowVisible(rowTopY))
                    continue;
                var boxRect = ConfigUILayout.ItemBoxRect(rowTopY, ConfigUILayout.ItemBoxNormalWidth);
                if (_itemBoxTexture?.Texture != null)
                    _spriteBatch.Draw(_itemBoxTexture.Texture, boxRect, Color.White);
                else
                    DrawFilledRectangle(boxRect, ItemBoxFallbackColor);
            }

            // Fixed cursor at the focus row; items scroll under it.
            if (!_focusOnMenu && category.HasItems)
            {
                if (_itemBoxCursorTexture?.Texture != null)
                    _spriteBatch.Draw(_itemBoxCursorTexture.Texture, ConfigUILayout.ItemCursorRect, Color.White);
                else
                    DrawFilledRectangle(ConfigUILayout.ItemCursorRect, ItemCursorFallback);
            }

            if (_font == null || _boldFont == null)
                return;

            // Text pass.
            for (int i = 0; i < items.Count; i++)
            {
                int rowTopY = ConfigUILayout.RowTopY(i, _itemScroll);
                if (!ConfigUILayout.IsRowVisible(rowTopY))
                    continue;
                var item = items[i];
                bool selected = !_focusOnMenu && i == category.SelectedIndex;
                var font = selected ? _boldFont : _font;

                font.DrawString(_spriteBatch, item.Name, ConfigUILayout.ItemNamePos(rowTopY),
                    selected ? SelectedNameText : LightText);

                var value = GetItemValueText(item);
                if (!string.IsNullOrEmpty(value))
                {
                    var displayValue = TextHelper.TruncateToWidth(value, ConfigUILayout.ItemValueMaxWidth, font);
                    var valuePos = ConfigUILayout.ItemValuePos(rowTopY);
                    // Every value (including the nav ">" marker) sits on the itembox white cell -> dark.
                    Color valueColor = selected ? SelectedValueText : ValueDarkText;
                    font.DrawString(_spriteBatch, displayValue, valuePos, valueColor);
                }
            }
        }

        // Extracts the value portion for the two-column item list by stripping the
        // "{Name}: " prefix that GetDisplayText() prepends. This is coupled to every
        // concrete item type's GetDisplayText() format (Dropdown/Toggle/Integer/
        // ReadOnly all use "$"{Name}: {value}""); a future item type that omits the
        // prefix would render an empty value here. Adding a GetValueText() to
        // IConfigItem would decouple this, but that value-semantics change is an
        // explicit non-goal of the NX layout revamp — so the coupling is documented
        // here instead.
        private static string GetItemValueText(IConfigItem item)
        {
            if (item is NavigationConfigItem)
                return ">";

            var text = item.GetDisplayText();
            var prefix = item.Name + ": ";
            return text.StartsWith(prefix, StringComparison.Ordinal)
                ? text.Substring(prefix.Length)
                : string.Empty;
        }

        protected virtual void DrawDescriptionPanel()
        {
            if (_categories.Count == 0)
                return;

            // NX draws the description panel only while focus is on the item list
            // (CStageConfig.cs:260, !bFocusIsOnMenu) — not while browsing the category menu. This
            // keeps the busy GALAXY WAVE background clear on entry and stops the panel from
            // overlapping the item boxes until the player is actually editing an item. The selected
            // item sits at the focus row (y=189), above the panel top (y=270), so it stays readable.
            if (_focusOnMenu)
                return;

            var category = _categories[_currentCategoryIndex];

            if (_descriptionPanelTexture?.Texture != null)
                _spriteBatch.Draw(_descriptionPanelTexture.Texture, ConfigUILayout.DescriptionPanelRect, Color.White);
            else
                DrawFilledRectangle(ConfigUILayout.DescriptionPanelRect, PanelFallbackColor);

            // Title: the selected item's name on the white upper cell.
            string title = category.SelectedItem?.Name ?? category.Name;
            if (!string.IsNullOrEmpty(title) && _boldFont != null)
                _boldFont.DrawString(_spriteBatch, title, ConfigUILayout.DescriptionTitlePos, DescriptionTitleText);

            // Body: the selected item's description, wrapped, on the black lower cell.
            string text = category.SelectedItem?.Description ?? string.Empty;

            if (string.IsNullOrEmpty(text) || _font == null)
                return;

            var pos = ConfigUILayout.DescriptionBodyPos;
            foreach (var line in WrapText(_font, text, ConfigUILayout.DescriptionWrapWidth))
            {
                _font.DrawString(_spriteBatch, line, pos, LightText);
                pos.Y += ConfigUILayout.DescriptionLineHeight;
            }
        }

        private static IEnumerable<string> WrapText(IFont font, string text, int maxWidth)
        {
            var line = new StringBuilder();
            foreach (var word in text.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && font.MeasureString(candidate).X > maxWidth)
                {
                    yield return line.ToString();
                    line.Clear();
                    line.Append(word);
                }
                else
                {
                    if (line.Length > 0)
                        line.Append(' ');
                    line.Append(word);
                }
            }
            if (line.Length > 0)
                yield return line.ToString();
        }

        protected virtual void DrawHeaderFooter()
        {
            if (_headerPanelTexture?.Texture != null)
                _spriteBatch.Draw(_headerPanelTexture.Texture, ConfigUILayout.HeaderRect, Color.White);
            else
                DrawFilledRectangle(ConfigUILayout.HeaderRect, PanelFallbackColor);

            if (_font != null)
            {
                const string title = "CONFIGURATION";
                var size = _font.MeasureString(title);
                var pos = new Vector2((ConfigUILayout.ScreenWidth - size.X) / 2f, ConfigUILayout.TitleY);
                _font.DrawString(_spriteBatch, title, pos, LightText);
            }

            if (_footerPanelTexture?.Texture != null)
                _spriteBatch.Draw(_footerPanelTexture.Texture, ConfigUILayout.FooterRect, Color.White);
            else
                DrawFilledRectangle(ConfigUILayout.FooterRect, PanelFallbackColor);

            if (_font != null)
                _font.DrawString(_spriteBatch, ConfigUILayout.InstructionsText, ConfigUILayout.InstructionsPos, LightText);
        }

        private void DrawImportStatus()
        {
            if (string.IsNullOrEmpty(_importStatus) || _font == null)
                return;

            _font.DrawString(_spriteBatch, _importStatus, ConfigUILayout.ImportStatusPos, ImportStatusColor);
        }

        // Letterbox transform that scales the fixed 1280x720 layout up to fill the actual
        // back-buffer/viewport (which matches the configured screen resolution, e.g. 3840x2160),
        // preserving aspect. Mirrors ResultScreenRenderer.CreateViewportTransform. Without it the
        // stage renders 1:1 in the top-left corner of a high-res back buffer.
        internal static Matrix CreateViewportTransform(Viewport viewport)
        {
            float scaleX = viewport.Width / (float)ConfigUILayout.ScreenWidth;
            float scaleY = viewport.Height / (float)ConfigUILayout.ScreenHeight;
            float scale = Math.Min(scaleX, scaleY);
            float offsetX = viewport.X + (viewport.Width - ConfigUILayout.ScreenWidth * scale) / 2f;
            float offsetY = viewport.Y + (viewport.Height - ConfigUILayout.ScreenHeight * scale) / 2f;
            return Matrix.CreateScale(scale, scale, 1f) * Matrix.CreateTranslation(offsetX, offsetY, 0f);
        }

        [ExcludeFromCodeCoverage]
        protected virtual void BeginDrawFrame()
        {
            _spriteBatch.Begin(transformMatrix: CreateViewportTransform(GetViewport()));
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
        /// Synchronous <see cref="IProgress{T}"/> that invokes the callback inline on the calling
        /// thread, preventing stale queued progress from overwriting the final import status.
        /// </summary>
        private sealed class InlineProgress<T> : IProgress<T>
        {
            private readonly Action<T> _callback;
            public InlineProgress(Action<T> callback) => _callback = callback;
            public void Report(T value) => _callback(value);
        }
    }
}
