namespace DTXMania.Automation.Process;

public static class GameProjectPaths
{
    public const string Windows = "DTXMania.Game/DTXMania.Game.Windows.csproj";
    public const string Mac = "DTXMania.Game/DTXMania.Game.Mac.csproj";

    public static string Current
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                return Windows;
            }

            if (OperatingSystem.IsMacOS())
            {
                return Mac;
            }

            throw new PlatformNotSupportedException("DTXMania automation supports Windows and macOS only.");
        }
    }
}
