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

        // Game settings
        public int ScrollSpeed { get; set; } = 100;
        public bool AutoPlay { get; set; } = false;
        public bool NoFail { get; set; } = false;

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
        /// Leave empty to allow unauthenticated access (not recommended for production).
        /// </summary>
        public string GameApiKey { get; set; } = "";
    }
}
