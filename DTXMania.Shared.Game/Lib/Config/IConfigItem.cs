using System;

namespace DTX.Config
{
    /// <summary>
    /// Interface for configuration items that can be displayed and edited in the config screen
    /// Based on DTXManiaNX CItemBase patterns
    /// </summary>
    public interface IConfigItem
    {
        /// <summary>
        /// Display name of the configuration item
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Get the current display text for this config item (name + current value)
        /// </summary>
        /// <returns>Display text for the UI list</returns>
        string GetDisplayText();

        /// <summary>
        /// Move to the previous value (Left arrow key)
        /// </summary>
        void PreviousValue();

        /// <summary>
        /// Move to the next value (Right arrow key)
        /// </summary>
        void NextValue();

        /// <summary>
        /// Toggle the value (Enter key) - primarily for boolean items
        /// </summary>
        void ToggleValue();

        /// <summary>
        /// Event fired when the value changes
        /// </summary>
        event EventHandler ValueChanged;
    }
}
