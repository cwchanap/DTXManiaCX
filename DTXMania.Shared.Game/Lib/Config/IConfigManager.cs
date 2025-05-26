namespace DTX.Config
{
    public interface IConfigManager
    {
        ConfigData Config { get; }
        void LoadConfig(string filePath);
        void SaveConfig(string filePath);
        void ResetToDefaults();
    }
}