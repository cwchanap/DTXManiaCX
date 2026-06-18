using System.Collections.Generic;
using DTXMania.Game.Lib.Utilities;

namespace DTXMania.Game.Lib.Config
{
    public class ConfigData
    {
        // System settings
        public string DTXManiaVersion { get; set; } = "NX1.5.0-MG";
        public string SkinPath { get; set; } = AppPaths.GetDefaultSystemSkinRoot();
        public string DTXPath { get; set; } = AppPaths.GetDefaultSongsPath();

        // Skin settings
        public bool UseBoxDefSkin { get; set; } = true;
        public string SystemSkinRoot { get; set; } = AppPaths.GetDefaultSystemSkinRoot();
        public string LastUsedSkin { get; set; } = "Default";

        // Display settings
        public int ScreenWidth { get; set; } = 1280;
        public int ScreenHeight { get; set; } = 720;
        public bool FullScreen { get; set; } = false;
        public bool VSyncWait { get; set; } = true;

        // Sound settings
        public int MasterVolume { get; set; } = 100;
        public int BGMVolume { get; set; } = 100;
        public int SEVolume { get; set; } = 100;
        public int BufferSizeMs { get; set; } = 100;        // Input settings
        public Dictionary<string, int> KeyBindings { get; set; } = new();
        public HashSet<int> UnboundDrumLanes { get; set; } = new();
        public HashSet<string> UnboundDrumButtons { get; set; } = new();
        public Dictionary<string, string> SystemKeyBindings { get; set; } = new();

        // Game settings
        public int ScrollSpeed { get; set; } = ScrollSpeedRange.Default;
        public bool AutoPlay { get; set; } = false;
        public bool NoFail { get; set; } = false;

        /// <summary>
        /// Audio output latency compensation in milliseconds. This value is subtracted from
        /// the raw song clock when evaluating player input timing, so that judgement windows
        /// are aligned with what the player actually hears rather than when audio was submitted
        /// to the output buffer.
        /// <para>
        /// SongTimer returns wall-clock time since Play() was called — the moment audio was
        /// queued. The actual audible output lags behind by the audio buffer + driver latency.
        /// On MonoGame DesktopGL (OpenAL) with BufferSizeMs=100, total output latency is
        /// typically 100-200ms. Without compensation, a player with perfect reaction time
        /// would always be judged ~200ms late because the clock reads T+200 but the note
        /// they're reacting to was heard at T.
        /// </para>
        /// <para>
        /// This offset only affects player judgement timing. Autoplay, note visuals, BGM
        /// events, song progress, and stage completion all use the raw song clock to stay
        /// synchronized with the chart. Equivalent to DTXManiaNX's nInputAdjustTimeMs
        /// (which defaults to 0 and is user-configured in range -99..+99).
        /// </para>
        /// </summary>
        public int AudioLatencyOffsetMs { get; set; } = 200;

        // API settings
        /// <summary>
        /// Enables the Game API server for MCP (Model Context Protocol) communication.
        /// Defaults to false for security. Set to true explicitly to enable external tool access.
        /// </summary>
        public bool EnableGameApi { get; set; } = false;

        /// <summary>
        /// The port number for the Game API server. Default is 8080.
        /// Change this if the default port is already in use.
        /// </summary>
        public int GameApiPort { get; set; } = 8080;

        /// <summary>
        /// API key for authenticating requests to the Game API server.
        /// If set, all API requests must include this key in the X-Api-Key header.
        /// Use an empty string to represent "no key configured"; this property should not be null.
        /// Leave empty to allow unauthenticated access (not recommended for production).
        /// </summary>
        public string GameApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Captures a point-in-time copy of the four binding-related collections so a failed save can
        /// restore them. See <see cref="BindingState"/>; the snapshot is restored via
        /// <see cref="RestoreBindingState"/>.
        /// </summary>
        public BindingState SnapshotBindingState()
            => new BindingState(
                new Dictionary<string, int>(KeyBindings),
                new HashSet<int>(UnboundDrumLanes),
                new HashSet<string>(UnboundDrumButtons),
                new Dictionary<string, string>(SystemKeyBindings));

        /// <summary>
        /// Clears the four binding-related collections and refills them from <paramref name="state"/>.
        /// Uses Clear+re-add (not reference replacement) to preserve the collection identities that
        /// external observers and existing tests rely on. The counterpart of
        /// <see cref="SnapshotBindingState"/>.
        /// </summary>
        public void RestoreBindingState(in BindingState state)
        {
            KeyBindings.Clear();
            foreach (var kvp in state.KeyBindings) KeyBindings[kvp.Key] = kvp.Value;
            UnboundDrumLanes.Clear();
            foreach (var lane in state.UnboundDrumLanes) UnboundDrumLanes.Add(lane);
            UnboundDrumButtons.Clear();
            foreach (var buttonId in state.UnboundDrumButtons) UnboundDrumButtons.Add(buttonId);
            SystemKeyBindings.Clear();
            foreach (var kvp in state.SystemKeyBindings) SystemKeyBindings[kvp.Key] = kvp.Value;
        }
    }
}
