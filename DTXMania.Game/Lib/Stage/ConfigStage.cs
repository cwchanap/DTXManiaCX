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

        private IConfigManager _configManager;
        private List<ConfigCategory> _categories = new();
        private int _currentCategoryIndex = 0;
        private bool _focusOnMenu = true;

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
        private ITexture? _itemBoxOtherTexture;
        private ITexture? _itemBoxCursorTexture;
        private ITexture? _descriptionPanelTexture;

        // Light text reads on the dark NX config background.
        private static readonly Color LightText = new(235, 238, 248);
        private static readonly Color SelectedText = Color.Yellow;
        private static readonly Color ValueText = new(255, 226, 150);
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
            System.Diagnostics.Debug.WriteLine("Activating Config Stage");

            InitializeGraphics();
            SetupConfigItems();
            InitializePanels();

            _currentCategoryIndex = 0;
            _focusOnMenu = true;

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

            DrawConfigBackground();
            DrawItemBar();
            DrawCategoryMenu();
            DrawItemList();
            DrawDescriptionPanel();
            DrawHeaderFooter();
            DrawImportStatus();

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

            FlushPendingSaveSafely();

            _importCts?.Cancel();

            _activePanel?.Deactivate();
            _activePanel = null;

            _font?.RemoveReference();
            _font = null;
            _boldFont?.RemoveReference();
            _boldFont = null;
            ReleaseTextures();

            _previousKeyboardState = default;
            _currentKeyboardState = default;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                System.Diagnostics.Debug.WriteLine("Disposing Config Stage resources");

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
            _itemBoxOtherTexture = TryLoadTexture(TexturePath.ConfigItemBoxOther);
            _itemBoxCursorTexture = TryLoadTexture(TexturePath.ConfigItemBoxCursor);
            _descriptionPanelTexture = TryLoadTexture(TexturePath.ConfigDescriptionPanel);
        }

        private ITexture? TryLoadTexture(string path)
        {
            try
            {
                return _resourceManager.LoadTexture(path);
            }
            catch
            {
                return null;
            }
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
            _itemBoxOtherTexture?.RemoveReference();
            _itemBoxOtherTexture = null;
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
                try
                {
                    var result = await SongManager.Instance.ImportNxScoresAsync(progress, token);
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
            System.Diagnostics.Debug.WriteLine("Config: Returning to Title stage");
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
                System.Diagnostics.Debug.WriteLine($"ConfigStage: failed to flush pending save: {ex}");
            }
        }

        #endregion

        #region Drawing (NX master-detail)

        private void DrawConfigBackground()
        {
            var viewport = GetViewport();
            var full = new Rectangle(0, 0, viewport.Width, viewport.Height);

            if (_backgroundTexture?.Texture != null)
                _spriteBatch.Draw(_backgroundTexture.Texture, full, Color.White);
            else
                DrawFilledRectangle(full, FallbackBackgroundColor);
        }

        private void DrawItemBar()
        {
            if (_itemBarTexture?.Texture != null)
                _spriteBatch.Draw(_itemBarTexture.Texture, ConfigUILayout.ItemBarRect, Color.White);
            else
                DrawFilledRectangle(ConfigUILayout.ItemBarRect, PanelFallbackColor);
        }

        private void DrawCategoryMenu()
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
                var color = selected ? SelectedText : LightText;
                var label = _categories[i].Name;
                var size = font.MeasureString(label);
                var pos = new Vector2(ConfigUILayout.MenuLabelCenterX - size.X / 2f, ConfigUILayout.MenuRowY(i));
                font.DrawString(_spriteBatch, label, pos, color);
            }
        }

        private void DrawItemList()
        {
            if (_categories.Count == 0)
                return;

            var category = _categories[_currentCategoryIndex];
            var items = category.Items;

            for (int row = 0; row < items.Count; row++)
            {
                var box = (items[row] is NavigationConfigItem || items[row] is ReadOnlyConfigItem)
                    ? _itemBoxOtherTexture
                    : _itemBoxTexture;
                if (box?.Texture != null)
                    _spriteBatch.Draw(box.Texture, ConfigUILayout.ItemRowRect(row), Color.White);
                else
                    DrawFilledRectangle(ConfigUILayout.ItemRowRect(row), ItemBoxFallbackColor);
            }

            if (!_focusOnMenu && category.HasItems)
            {
                var cursorRect = ConfigUILayout.ItemCursorRect(category.SelectedIndex);
                if (_itemBoxCursorTexture?.Texture != null)
                    _spriteBatch.Draw(_itemBoxCursorTexture.Texture, cursorRect, Color.White);
                else
                    DrawFilledRectangle(cursorRect, ItemCursorFallback);
            }

            if (_font == null || _boldFont == null)
                return;

            for (int row = 0; row < items.Count; row++)
            {
                var item = items[row];
                bool selected = !_focusOnMenu && row == category.SelectedIndex;
                var font = selected ? _boldFont : _font;

                font.DrawString(_spriteBatch, item.Name, ConfigUILayout.ItemNamePos(row),
                    selected ? SelectedText : LightText);

                var value = GetItemValueText(item);
                if (!string.IsNullOrEmpty(value))
                {
                    font.DrawString(_spriteBatch, value, ConfigUILayout.ItemValuePos(row),
                        selected ? SelectedText : ValueText);
                }
            }
        }

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

        private void DrawDescriptionPanel()
        {
            if (_categories.Count == 0)
                return;

            var category = _categories[_currentCategoryIndex];
            string text = _focusOnMenu
                ? category.Description
                : (category.SelectedItem?.Description ?? string.Empty);

            if (string.IsNullOrEmpty(text))
                return;

            if (_descriptionPanelTexture?.Texture != null)
                _spriteBatch.Draw(_descriptionPanelTexture.Texture, ConfigUILayout.DescriptionPanelRect, Color.White);
            else
                DrawFilledRectangle(ConfigUILayout.DescriptionPanelRect, PanelFallbackColor);

            if (_font == null)
                return;

            var pos = ConfigUILayout.DescriptionTextPos;
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

        private void DrawHeaderFooter()
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
