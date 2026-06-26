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
        public int SelectedIndex { get; set; }

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
