#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
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
                    Activate();
                    break;
                case InputCommandType.Back:
                    Close();
                    break;
            }
        }

        private void Activate()
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

        /// <summary>Swallow UI input while open so clicks never reach the song list behind.</summary>
        public override bool HandleInput(IInputState inputState) => IsOpen;

        [ExcludeFromCodeCoverage]
        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (!IsOpen || WhitePixel == null || Font == null)
                return;

            var bounds = PopupLayout.Bounds;
            spriteBatch.Draw(WhitePixel, new Rectangle(0, 0, ScreenLayout.Width, ScreenLayout.Height), new Color(0, 0, 0, 160));
            spriteBatch.Draw(WhitePixel, bounds, new Color(28, 28, 32));

            spriteBatch.DrawString(Font, Title,
                new Vector2(bounds.X + (bounds.Width - Font.MeasureString(Title).X) / 2, bounds.Y + PopupLayout.TitleY),
                Color.White);

            for (int row = 0; row < RowCount; row++)
            {
                var rowRect = new Rectangle(
                    bounds.X + PopupLayout.RowX,
                    bounds.Y + PopupLayout.FirstRowY + row * PopupLayout.RowHeight,
                    PopupLayout.RowWidth,
                    PopupLayout.RowHeight - 4);
                bool selected = row == SelectedIndex;
                var background = selected && IsEditingValue ? new Color(90, 70, 30)
                    : selected ? new Color(60, 60, 80)
                    : new Color(40, 40, 50);
                spriteBatch.Draw(WhitePixel, rowRect, background);

                string text = row < _items.Count ? _items[row].GetDisplayText() : ReturnLabel;
                var textSize = Font.MeasureString(text);
                spriteBatch.DrawString(Font, text,
                    new Vector2(rowRect.X + PopupLayout.RowTextPadding, rowRect.Y + (rowRect.Height - textSize.Y) / 2),
                    selected ? Color.White : Color.LightGray);
            }

            const string hint = "HT/LT move   CY change   LC close";
            spriteBatch.DrawString(Font, hint,
                new Vector2(bounds.X + (bounds.Width - Font.MeasureString(hint).X) / 2, bounds.Y + PopupLayout.HintY),
                Color.Gray);
        }
    }
}
