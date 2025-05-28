using System.Collections.Generic;

namespace DTX.Config
{
    public class ConfigData
    {
        // System settings
        public string DTXManiaVersion { get; set; } = "NX1.5.0-MG";
        public string SkinPath { get; set; } = "System/Default/";
        public string DTXPath { get; set; } = "DTXFiles/";

        // Skin settings
        public bool UseBoxDefSkin { get; set; } = true;
        public string SystemSkinRoot { get; set; } = "System/";
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
        public int BufferSizeMs { get; set; } = 100;

        // Input settings
        public Dictionary<string, int> KeyBindings { get; set; } = new();

        // Game settings
        public int ScrollSpeed { get; set; } = 100;
        public bool AutoPlay { get; set; } = false;
    }
}