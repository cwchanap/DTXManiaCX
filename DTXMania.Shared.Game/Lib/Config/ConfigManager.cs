using System;
using System.IO;
using System.Text;

namespace DTX.Config
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
                    // Add more cases as needed
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

            sb.AppendLine("[Display]");
            sb.AppendLine($"ScreenWidth={Config.ScreenWidth}");
            sb.AppendLine($"ScreenHeight={Config.ScreenHeight}");
            sb.AppendLine($"FullScreen={Config.FullScreen}");
            sb.AppendLine($"VSyncWait={Config.VSyncWait}");

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        public void ResetToDefaults()
        {
            Config = new ConfigData();
        }
    }
}