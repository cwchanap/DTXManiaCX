#nullable enable

using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Config;

namespace DTXMania.Game.Lib.Stage.Config
{
    /// <summary>
    /// A left-column category in the NX Config stage: a labeled group of config items plus a
    /// help description. The Exit category is simply a category with no items
    /// (<see cref="HasItems"/> == false); no special flag is needed.
    /// </summary>
    public sealed class ConfigCategory
    {
        public string Name { get; }
        public string Description { get; }
        public IReadOnlyList<IConfigItem> Items { get; }

        private int _selectedIndex;

        /// <summary>
        /// Index of the focused item within <see cref="Items"/>. Clamped on assignment so an
        /// out-of-range value can never produce a phantom cursor row (the draw layer maps this
        /// straight to a screen rectangle). Empty categories clamp to 0. <see cref="SelectedItem"/>
        /// still bounds-checks defensively at read time.
        /// </summary>
        public int SelectedIndex
        {
            get => _selectedIndex;
            set => _selectedIndex = Items.Count == 0 ? 0 : Math.Clamp(value, 0, Items.Count - 1);
        }

        /// <summary>
        /// Creates a category. <paramref name="name"/> is the category's identity and must be
        /// non-null (it is shown in the menu and never blank), so a null name throws. A null
        /// <paramref name="description"/> is coerced to <see cref="string.Empty"/> instead — the
        /// description is optional help text, and an empty string degrades gracefully (the panel
        /// still renders) rather than crashing the caller.
        /// </summary>
        /// <param name="name">Category label (required, non-null).</param>
        /// <param name="description">Help text shown when the category is selected (optional;
        /// null becomes empty).</param>
        /// <param name="items">The config items in this category (required, non-null; may be
        /// empty for an action-only category such as Exit).</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> or
        /// <paramref name="items"/> is null.</exception>
        public ConfigCategory(string name, string description, IReadOnlyList<IConfigItem> items)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? string.Empty;
            Items = items ?? throw new ArgumentNullException(nameof(items));
        }

        public bool HasItems => Items.Count > 0;

        public IConfigItem? SelectedItem =>
            HasItems && SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

        public void MoveSelectionUp()
        {
            if (Items.Count == 0)
                return;
            SelectedIndex = (SelectedIndex - 1 + Items.Count) % Items.Count;
        }

        public void MoveSelectionDown()
        {
            if (Items.Count == 0)
                return;
            SelectedIndex = (SelectedIndex + 1) % Items.Count;
        }
    }
}
