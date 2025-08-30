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
            foreach (var kvp in Config.KeyBindings)
            {
                keyBindings.BindButton(kvp.Key, kvp.Value);
            }
        }

        public void SaveKeyBindings(KeyBindings keyBindings)
        {
            // Save key bindings to config data
            // Create a temporary set of default bindings to compare against
            var defaultBindings = new DTXMania.Game.Lib.Input.KeyBindings();
            defaultBindings.LoadDefaultBindings();
            
            Config.KeyBindings.Clear();
            foreach (var kvp in keyBindings.ButtonToLane)
            {
                // Only save non-default bindings to config
                if (!defaultBindings.ButtonToLane.TryGetValue(kvp.Key, out int defaultValue) || defaultValue != kvp.Value)
                {
                    Config.KeyBindings[kvp.Key] = kvp.Value;
                }
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
                case "ScrollSpeed":
                    if (int.TryParse(value, out var scrollSpeed))
                        Config.ScrollSpeed = scrollSpeed;
                    break;
                case "AutoPlay":
                    if (TryParseBool(value, out var autoPlay))
                        Config.AutoPlay = autoPlay;
                    break;
                case "NoFail":
                    if (TryParseBool(value, out var noFail))
                        Config.NoFail = noFail;
                    break;
                // Handle key bindings from config file
                default:
                    if (key.StartsWith("Key.") && int.TryParse(value, out var lane))
                    {
                        if (lane >= 0 && lane <= 8)
                        {
                            Config.KeyBindings[key] = lane;
                        }
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
            sb.AppendLine();
            
            sb.AppendLine("[Game]");
            sb.AppendLine($"ScrollSpeed={Config.ScrollSpeed}");
            sb.AppendLine($"AutoPlay={Config.AutoPlay}");
            sb.AppendLine($"NoFail={Config.NoFail}");
            
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
        
        /// <summary>
        /// Helper method for robust boolean parsing
        /// </summary>
        private static bool TryParseBool(string value, out bool result)
        {
            result = false;
            if (string.IsNullOrEmpty(value))
                return false;
                
            var trimmed = value.Trim().ToLowerInvariant();
            if (trimmed == "true" || trimmed == "1" || trimmed == "yes" || trimmed == "on")
            {
                result = true;
                return true;
            }
            if (trimmed == "false" || trimmed == "0" || trimmed == "no" || trimmed == "off")
            {
                result = false;
                return true;
            }
            return false;
        }
    }
}