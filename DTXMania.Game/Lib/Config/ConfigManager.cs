using System;
using System.IO;
using System.Text;
using DTXMania.Game.Lib.Input;

namespace DTXMania.Game.Lib.Config
{
    public class ConfigManager : IConfigManager
    {
        public ConfigData Config { get; private set; }

        public ConfigManager()
        {
            Config = new ConfigData();
        }

        public void LoadConfig(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Config file not found. Creating default config at {filePath}");
                SaveConfig(filePath); // Create default config
                return;
            }

            var lines = File.ReadAllLines(filePath, Encoding.UTF8);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';'))
                    continue;

                var parts = line.Split('=');
                if (parts.Length != 2)
                    continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                ParseConfigLine(key, value);
            }
        }

        public void LoadKeyBindings(KeyBindings keyBindings)
        {
            // Load key bindings from config data
            System.Diagnostics.Debug.WriteLine($"[ConfigManager] LoadKeyBindings() called with {Config.KeyBindings.Count} config entries");
            
            foreach (var kvp in Config.KeyBindings)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigManager] Overriding binding: {kvp.Key} → Lane {kvp.Value}");
                keyBindings.BindButton(kvp.Key, kvp.Value);
            }
            
            if (Config.KeyBindings.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[ConfigManager] No key bindings in config, keeping defaults");
            }
        }

        public void SaveKeyBindings(KeyBindings keyBindings)
        {
            // Save key bindings to config data
            Config.KeyBindings.Clear();
            foreach (var kvp in keyBindings.ButtonToLane)
            {
                Config.KeyBindings[kvp.Key] = kvp.Value;
            }
        }

        private void ParseConfigLine(string key, string value)
        {
            switch (key)
            {
                case "DTXManiaVersion":
                    Config.DTXManiaVersion = value;
                    break;
                case "SkinPath":
                    Config.SkinPath = value;
                    break;
                case "DTXPath":
                    Config.DTXPath = value;
                    break;
                case "UseBoxDefSkin":
                    Config.UseBoxDefSkin = value.ToLower() == "true";
                    break;
                case "SystemSkinRoot":
                    Config.SystemSkinRoot = value;
                    break;
                case "LastUsedSkin":
                    Config.LastUsedSkin = value;
                    break;
                case "ScreenWidth":
                    if (int.TryParse(value, out var width))
                        Config.ScreenWidth = width;
                    break;
                case "ScreenHeight":
                    if (int.TryParse(value, out var height))
                        Config.ScreenHeight = height;
                    break;
                case "FullScreen":
                    Config.FullScreen = value.ToLower() == "true";
                    break;
                case "VSyncWait":
                    Config.VSyncWait = value.ToLower() == "true";
                    break;
                // Handle key bindings from config file
                default:
                    if (key.StartsWith("Key.") && int.TryParse(value, out var lane))
                    {
                        Config.KeyBindings[key] = lane;
                        System.Diagnostics.Debug.WriteLine($"[ConfigManager] Loaded key binding from config: {key} → Lane {lane}");
                    }
                    break;
            }
        }

        public void SaveConfig(string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("; DTXMania Configuration File");
            sb.AppendLine($"; Generated: {DateTime.Now}");
            sb.AppendLine();

            sb.AppendLine("[System]");
            sb.AppendLine($"DTXManiaVersion={Config.DTXManiaVersion}");
            sb.AppendLine($"SkinPath={Config.SkinPath}");
            sb.AppendLine($"DTXPath={Config.DTXPath}");
            sb.AppendLine();

            sb.AppendLine("[Skin]");
            sb.AppendLine($"UseBoxDefSkin={Config.UseBoxDefSkin}");
            sb.AppendLine($"SystemSkinRoot={Config.SystemSkinRoot}");
            sb.AppendLine($"LastUsedSkin={Config.LastUsedSkin}");
            sb.AppendLine();
            
            sb.AppendLine("[Display]");
            sb.AppendLine($"ScreenWidth={Config.ScreenWidth}");
            sb.AppendLine($"ScreenHeight={Config.ScreenHeight}");
            sb.AppendLine($"FullScreen={Config.FullScreen}");
            sb.AppendLine($"VSyncWait={Config.VSyncWait}");
            
            // Save key bindings to config file
            if (Config.KeyBindings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[KeyBindings]");
                foreach (var kvp in Config.KeyBindings)
                {
                    sb.AppendLine($"{kvp.Key}={kvp.Value}");
                }
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        public void ResetToDefaults()
        {
            Config = new ConfigData();
        }
    }
}