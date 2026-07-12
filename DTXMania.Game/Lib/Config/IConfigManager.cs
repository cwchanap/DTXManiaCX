#nullable enable

using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Input;
using Microsoft.Xna.Framework.Input;

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
        /// Raised when drum key bindings change via SetKeyBindings.
        /// Not raised by LoadConfig.
        /// </summary>
        event EventHandler<EventArgs>? KeyBindingsChanged;

        /// <summary>
        /// Raised when system key bindings change via SetSystemKeyBindings.
        /// Not raised by LoadConfig.
        /// </summary>
        event EventHandler<EventArgs>? SystemKeyBindingsChanged;

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

        /// <summary>
        /// Writes <paramref name="keyBindings"/> into <see cref="Config"/>, marks the edit
        /// dirty for a deferred save, and raises <see cref="KeyBindingsChanged"/>.
        /// </summary>
        void SetKeyBindings(KeyBindings keyBindings);

        /// <summary>
        /// Writes <paramref name="workingBindings"/> into <see cref="Config"/>, marks the
        /// edit dirty for a deferred save, and raises <see cref="SystemKeyBindingsChanged"/>.
        /// </summary>
        void SetSystemKeyBindings(IReadOnlyDictionary<Keys, InputCommandType> workingBindings);

        /// <summary>Gets the MIDI minimum velocity threshold for a note. Missing notes default to 0.</summary>
        int GetMidiVelocityThreshold(int noteNumber);

        /// <summary>Sets a MIDI minimum velocity threshold, clamped to 0..127, and marks config dirty.</summary>
        void SetMidiVelocityThreshold(int noteNumber, int threshold);

        /// <summary>Sets AutoPlay and marks a deferred save pending. No event raised.</summary>
        void SetAutoPlay(bool value);

        /// <summary>Sets NoFail and marks a deferred save pending. No event raised.</summary>
        void SetNoFail(bool value);

        /// <summary>Sets audio latency (in ms, clamped to &gt;= 0) and marks a deferred save pending. No event raised.</summary>
        void SetAudioLatency(int value);

        /// <summary>Sets resolution (width x height) and marks a deferred save pending. No event raised.</summary>
        void SetResolution(int width, int height);

        /// <summary>Sets fullscreen and marks a deferred save pending. No event raised.</summary>
        void SetFullscreen(bool value);

        /// <summary>Sets VSync and marks a deferred save pending. No event raised.</summary>
        void SetVSync(bool value);

        /// <summary>
        /// Sets the skin path (<see cref="ConfigData.SkinPath"/>, the directory the resource
        /// manager loads skin assets from) and marks a deferred save pending. No event raised.
        /// No-op when the value is null/whitespace or unchanged.
        /// </summary>
        void SetSkinPath(string configFilePath, string skinPath);

        /// <summary>
        /// Flushes any deferred config changes to disk. Call this on stage exit
        /// or game shutdown to ensure pending writes are persisted.
        /// </summary>
        void FlushPendingSave();
    }
}
