using DTXMania.Shared.Game;
using DTXMania.Windows.Resources;
using DTX.Resources;

namespace DTXMania.Windows;

public class Game1 : BaseGame
{
    public Game1()
    {
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
    }

    protected override void Initialize()
    {
        // Configure platform-specific font factory before base initialization
        ResourceManagerFactory.SetFontFactory(new WindowsFontFactory());

        base.Initialize();
    }
}
