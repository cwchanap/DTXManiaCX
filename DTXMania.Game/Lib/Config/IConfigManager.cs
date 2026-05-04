using System;

namespace DTXMania.Game.Lib.Config
{
    public interface IConfigManager
    {
        ConfigData Config { get; }
        void LoadConfig(string filePath);
        void SaveConfig(string filePath);
        void ResetToDefaults();

        /// <summary>
        /// Raised when the scroll-speed setting changes via SetScrollSpeed or AdjustScrollSpeed.
        /// Not raised by direct mutation of Config.ScrollSpeed or by LoadConfig.
        /// </summary>
        event EventHandler<ScrollSpeedChangedEventArgs>? ScrollSpeedChanged;

        /// <summary>
        /// Sets the scroll speed (percent), snapping to the nearest allowed step and
        /// clamping to the allowed range. Persists to the given file path and raises
        /// ScrollSpeedChanged when the value actually changes.
        /// No-op (and no save) if the new value equals the current value.
        /// </summary>
        void SetScrollSpeed(string configFilePath, int percent);

        /// <summary>
        /// Adjusts scroll speed by stepDelta * Step. Equivalent to
        /// SetScrollSpeed(path, current + stepDelta * Step).
        /// </summary>
        void AdjustScrollSpeed(string configFilePath, int stepDelta);
    }
}
