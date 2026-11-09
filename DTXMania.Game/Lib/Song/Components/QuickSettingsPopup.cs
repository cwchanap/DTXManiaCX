#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PopupLayout = DTXMania.Game.Lib.UI.Layout.SongSelectionUILayout.QuickSettingsPopup;
using ScreenLayout = DTXMania.Game.Lib.UI.Layout.SongSelectionUILayout.SongListDisplay;
using LaneType = DTXMania.Game.Lib.UI.Layout.PerformanceUILayout.LaneType;

namespace DTXMania.Game.Lib.Song.Components
{
    /// <summary>
    /// Song-select Quick Settings popup, modelled on DTXManiaNX's quick config (bass drum x2).
    /// Controls follow NX: up/down move the cursor; Enter steps a list/toggle value, or on a
    /// number toggles edit mode where up/down change it; Enter on Return or Back closes.
    /// Left/right also change the value, for keyboard users.
    /// </summary>
    public class QuickSettingsPopup : UIElement
    {
        public const string Title = "QUICK SETTINGS";
        public const string ReturnLabel = "Return";

        private IReadOnlyList<IConfigItem> _items = Array.Empty<IConfigItem>();

        public QuickSettingsPopup()
        {
            Visible = false;
            Enabled = false;
        }

        public event EventHandler? Closed;

        public bool IsOpen { get; private set; }

        /// <summary>Cursor row; <c>Items.Count</c> is the Return row.</summary>
        public int SelectedIndex { get; private set; }

        /// <summary>True while a number is being edited with up/down.</summary>
        public bool IsEditingValue { get; private set; }

        public IReadOnlyList<IConfigItem> Items => _items;

        private int RowCount => _items.Count + 1;

        private IConfigItem? SelectedItem => SelectedIndex < _items.Count ? _items[SelectedIndex] : null;

        public const string CustomAutoMode = "Custom";

        /// <summary>NX auto-mode presets that CX's per-lane AutoPlay can express.</summary>
        internal static readonly (string Label, int[] Lanes)[] AutoModes =
        {
            ("OFF", Array.Empty<int>()),
            ("All Auto", Enumerable.Range(0, 10).ToArray()),
            ("Auto BD", new[] { (int)LaneType.DB }),
            ("Auto LP", new[] { (int)LaneType.LP }),
            ("2 Pedal Auto", new[] { (int)LaneType.LP, (int)LaneType.DB }),
        };

        internal static string GetAutoMode(IReadOnlySet<int> autoLanes)
        {
            foreach (var (label, lanes) in AutoModes)
            {
                if (lanes.Length == autoLanes.Count && lanes.All(autoLanes.Contains))
                    return label;
            }
            return CustomAutoMode;
        }

        internal static void SetAutoMode(IConfigManager config, string label)
        {
            config.SetAllAutoPlayLanes(false);
            foreach (var lane in AutoModes.First(mode => mode.Label == label).Lanes)
                config.SetAutoPlayLane(lane, true);
        }

        /// <summary>
        /// The NX quick-config items CX has, wired to the same setters as the Config screen.
        /// </summary>
        public static IReadOnlyList<IConfigItem> CreateItems(IConfigManager config, Func<bool> isPlaySpeedAvailable)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(isPlaySpeedAvailable);

            return new IConfigItem[]
            {
                new DropdownConfigItem("Auto Mode",
                    () => GetAutoMode(config.Config.AutoPlayLanes),
                    AutoModes.Select(mode => mode.Label).ToArray(),
                    label => SetAutoMode(config, label)),
                new IntegerConfigItem("Scroll Speed",
                    () => config.Config.ScrollSpeed,
                    config.SetScrollSpeed,
                    ScrollSpeedRange.Min, ScrollSpeedRange.Max, ScrollSpeedRange.Step, ScrollSpeedRange.Format),
                new IntegerConfigItem("Play Speed",
                    () => config.Config.PlaySpeedPercent,
                    // Off-speed playback needs FFmpeg; without it the Config screen pins 100% too.
                    value => { if (isPlaySpeedAvailable()) config.SetPlaySpeedPercent(value); },
                    PlaySpeedRange.Min, PlaySpeedRange.Max, PlaySpeedRange.Step, PlaySpeedRange.Format),
                new IntegerConfigItem("Risky",
                    () => config.Config.Risky,
                    config.SetRisky,
                    RiskyRange.Min, RiskyRange.Max, RiskyRange.Step, RiskyRange.Format),
                new DropdownConfigItem("Lane Display",
                    () => config.Config.LaneDisplayMode.ToLabel(),
                    DrumsLaneDisplayModeExtensions.Labels.ToArray(),
                    label => config.SetLaneDisplayMode(DrumsLaneDisplayModeExtensions.FromLabel(label))),
                new DropdownConfigItem("Note Display",
                    () => config.Config.NoteDisplayStyle.ToLabel(),
                    DrumsNoteDisplayStyleExtensions.Labels.ToArray(),
                    label => config.SetNoteDisplayStyle(DrumsNoteDisplayStyleExtensions.FromLabel(label))),
                new ToggleConfigItem("No Fail",
                    () => config.Config.NoFail,
                    config.SetNoFail),
            };
        }

        public void Open(IReadOnlyList<IConfigItem> items)
        {
            _items = items ?? throw new ArgumentNullException(nameof(items));
            SelectedIndex = 0;
            IsEditingValue = false;
            IsOpen = true;
            Visible = true;
            Enabled = true;
            var bounds = PopupLayout.Bounds;
            Position = new Vector2(bounds.X, bounds.Y);
            Size = new Vector2(bounds.Width, bounds.Height);
        }

        public void Close()
        {
            if (!IsOpen)
                return;

            IsOpen = false;
            IsEditingValue = false;
            Visible = false;
            Enabled = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }

        public void HandleCommand(InputCommandType command)
        {
            if (!IsOpen)
                return;

            switch (command)
            {
                case InputCommandType.MoveUp:
                    if (IsEditingValue) SelectedItem?.NextValue();
                    else SelectedIndex = (SelectedIndex - 1 + RowCount) % RowCount;
                    break;
                case InputCommandType.MoveDown:
                    if (IsEditingValue) SelectedItem?.PreviousValue();
                    else SelectedIndex = (SelectedIndex + 1) % RowCount;
                    break;
                case InputCommandType.MoveLeft:
                    SelectedItem?.PreviousValue();
                    break;
                case InputCommandType.MoveRight:
                    SelectedItem?.NextValue();
                    break;
                case InputCommandType.Activate:
                    ActivateSelectedRow();
                    break;
                case InputCommandType.Back:
                    Close();
                    break;
            }
        }

        private void ActivateSelectedRow()
        {
            var item = SelectedItem;
            if (item == null)
                Close();
            else if (item is IntegerConfigItem)
                IsEditingValue = !IsEditingValue;
            else
                item.NextValue();
        }

        public Texture2D? WhitePixel { get; set; }
        public SpriteFont? Font { get; set; }

        private IResourceManager? _resourceManager;
        private ITexture? _backgroundTexture;
        private ITexture? _cursorTexture;

        /// <summary>
        /// Loads the skin's NX popup art: the song-select popup background and the
        /// orange menu cursor. Missing art falls back to flat panels in the same colours.
        /// </summary>
        public void Initialize(IResourceManager? resourceManager)
        {
            ReleaseTextures();
            _resourceManager = resourceManager;
            if (resourceManager == null)
                return;

            _backgroundTexture = TryLoad(resourceManager, TexturePath.QuickSettingsBackground);
            _cursorTexture = TryLoad(resourceManager, TexturePath.QuickSettingsCursor);
        }

        private static ITexture? TryLoad(IResourceManager resourceManager, string path)
        {
            try
            {
                return resourceManager.LoadTexture(path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"QuickSettingsPopup: failed to load {path}: {ex.Message}");
                return null;
            }
        }

        private void ReleaseTextures()
        {
            _backgroundTexture?.RemoveReference();
            _backgroundTexture = null;
            _cursorTexture?.RemoveReference();
            _cursorTexture = null;
        }

        /// <summary>Swallow UI input while open so clicks never reach the song list behind.</summary>
        public override bool HandleInput(IInputState inputState) => IsOpen;

        /// <summary>Splits "Name: value" display text into the value part.</summary>
        internal static string GetValueText(IConfigItem item)
        {
            string text = item.GetDisplayText();
            string prefix = item.Name + ": ";
            return text.StartsWith(prefix, StringComparison.Ordinal) ? text.Substring(prefix.Length) : text;
        }

        [ExcludeFromCodeCoverage]
        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (!IsOpen || WhitePixel == null || Font == null)
                return;

            var theme = _resourceManager?.CurrentTheme ?? SkinTheme.Empty;
            var origin = new Vector2(PopupLayout.X, PopupLayout.Y);

            spriteBatch.Draw(WhitePixel, new Rectangle(0, 0, ScreenLayout.Width, ScreenLayout.Height),
                Color.Black * PopupLayout.BackdropOpacity);

            if (_backgroundTexture != null)
                _backgroundTexture.Draw(spriteBatch, origin);
            else
                spriteBatch.Draw(WhitePixel, PopupLayout.PanelFallbackBounds, PopupLayout.PanelFallbackColor);

            var textColor = theme.GetColor("SongSelect.PopupText", theme.GetColor("UI.TextPrimary", Color.White));
            var editColor = theme.GetColor("SongSelect.PopupEdit", DTXManiaVisualTheme.FontEffects.DefaultTextColor);
            // Wide display faces (CX Neon's Orbitron) set a smaller scale so rows fit the panel.
            float scale = theme.GetFloat("SongSelect.PopupTextScale", 1f);
            float lineHeight = Font.LineSpacing * scale;

            DrawText(spriteBatch, Title,
                new Vector2(origin.X + (PopupLayout.Width - Font.MeasureString(Title).X * scale) / 2, origin.Y + PopupLayout.TitleY),
                textColor, scale);

            DrawCursor(spriteBatch, new Rectangle(
                (int)origin.X + PopupLayout.CursorX,
                (int)origin.Y + PopupLayout.FirstRowY + SelectedIndex * PopupLayout.RowHeight,
                PopupLayout.CursorWidth,
                PopupLayout.RowHeight));

            for (int row = 0; row < RowCount; row++)
            {
                float rowY = origin.Y + PopupLayout.FirstRowY + row * PopupLayout.RowHeight
                    + (PopupLayout.RowHeight - lineHeight) / 2f;
                bool selected = row == SelectedIndex;
                var namePosition = new Vector2(origin.X + PopupLayout.NameX, rowY);

                if (row == _items.Count)
                {
                    DrawText(spriteBatch, ReturnLabel, namePosition, textColor, scale);
                    continue;
                }

                var item = _items[row];
                DrawText(spriteBatch, item.Name, namePosition, textColor, scale);
                // NX's value column, pushed right when a long name would run into it.
                float valueX = Math.Max(origin.X + PopupLayout.ValueX,
                    namePosition.X + Font.MeasureString(item.Name).X * scale + PopupLayout.ValueGap);
                DrawText(spriteBatch, GetValueText(item), new Vector2(valueX, rowY),
                    selected && IsEditingValue ? editColor : textColor, scale);
            }

            string hint = IsEditingValue ? "HT/LT: Change   CY: Done" : "HT/LT: Move   CY: Change   LC: Close";
            float hintWidth = Font.MeasureString(hint).X;
            float hintScale = Math.Min(PopupLayout.HintScale * scale, (PopupLayout.Width - PopupLayout.NameX * 2) / hintWidth);
            DrawText(spriteBatch, hint,
                new Vector2(origin.X + (PopupLayout.Width - hintWidth * hintScale) / 2, origin.Y + PopupLayout.HintY),
                textColor * 0.75f, hintScale);
        }

        /// <summary>NX draws the 32x32 cursor as a 16px left cap, a repeated middle slice and a 16px right cap.</summary>
        [ExcludeFromCodeCoverage]
        private void DrawCursor(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_cursorTexture == null)
            {
                spriteBatch.Draw(WhitePixel!, bounds, PopupLayout.CursorFallbackColor);
                return;
            }

            const int cap = 16;
            int middleWidth = bounds.Width - cap * 2;
            _cursorTexture.Draw(spriteBatch, new Rectangle(bounds.X, bounds.Y, cap, bounds.Height),
                new Rectangle(0, 0, cap, 32), Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0f);
            _cursorTexture.Draw(spriteBatch, new Rectangle(bounds.X + cap, bounds.Y, middleWidth, bounds.Height),
                new Rectangle(8, 0, cap, 32), Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0f);
            _cursorTexture.Draw(spriteBatch, new Rectangle(bounds.Right - cap, bounds.Y, cap, bounds.Height),
                new Rectangle(cap, 0, cap, 32), Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0f);
        }

        [ExcludeFromCodeCoverage]
        private void DrawText(SpriteBatch spriteBatch, string text, Vector2 position, Color color, float scale = 1f)
        {
            spriteBatch.DrawString(Font!, text, position + DTXManiaVisualTheme.FontEffects.DefaultShadowOffset,
                DTXManiaVisualTheme.FontEffects.DefaultShadowColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            spriteBatch.DrawString(Font!, text, position, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                ReleaseTextures();
            base.Dispose(disposing);
        }
    }
}
