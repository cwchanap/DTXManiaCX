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

        /// <summary>
        /// Loads the configuration from the SQLite config database (the live
        /// store). A legacy Config.ini is imported only when the database is
        /// absent; an invalid database fails loudly with no INI fallback.
        /// </summary>
        void LoadConfig();

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
        /// Raised after a validated song-root edit has been persisted successfully.
        /// </summary>
        event EventHandler<SongRootsChangedEventArgs>? SongRootsChanged
        {
            add { }
            remove { }
        }

        /// <summary>
        /// Validates, persists, and publishes an ordered song-root update. Existing
        /// interface implementations that do not own persisted song roots can opt out.
        /// </summary>
        SongRootUpdateResult SetSongRoots(IReadOnlyList<string> roots) =>
            throw new NotSupportedException(
                "This configuration manager does not support song-root updates.");

        /// <summary>
        /// Sets the scroll speed (percent), snapping to the nearest allowed step and
        /// clamping to the allowed range. Marks a deferred save pending and raises
        /// ScrollSpeedChanged when the value actually changes.
        /// No-op (and no save) if the new value equals the current value.
        /// </summary>
        void SetScrollSpeed(int percent);

        /// <summary>
        /// Adjusts scroll speed by stepDelta * Step. Equivalent to
        /// SetScrollSpeed(current + stepDelta * Step).
        /// </summary>
        void AdjustScrollSpeed(int stepDelta);

        /// <summary>
        /// Sets gameplay speed, snapping to <see cref="PlaySpeedRange"/> and marking a
        /// deferred save only when the canonical value changes.
        /// </summary>
        void SetPlaySpeedPercent(int percent);

        /// <summary>
        /// Sets independent pitch, snapping to <see cref="PitchRange"/> and marking a
        /// deferred save only when the canonical value changes.
        /// </summary>
        void SetPitchSemitones(int semitones);

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

        /// <summary>Sets Risky, clamped to the supported range, and marks a deferred save pending.</summary>
        void SetRisky(int value);

        /// <summary>Sets the gauge damage level and marks a deferred save pending.</summary>
        void SetDamageLevel(GaugeDamageLevel value);

        /// <summary>Sets Auto Add Gauge and marks a deferred save pending.</summary>
        void SetAutoAddGauge(bool value);

        /// <summary>Sets Metronome and marks a deferred save pending when the value changes. No event raised.</summary>
        void SetMetronome(bool value);

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
        void SetSkinPath(string skinPath);

        /// <summary>
        /// Flushes any deferred config changes to disk. Call this on stage exit
        /// or game shutdown to ensure pending writes are persisted.
        /// </summary>
        void FlushPendingSave();
    }
}
