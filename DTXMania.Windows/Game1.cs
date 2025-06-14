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
        base.Initialize();
    }

    protected override void LoadContent()
    {
        base.LoadContent();
        
        // Configure platform-specific font factory after content is loaded
        ResourceManagerFactory.SetFontFactory(new WindowsFontFactory(Content));
    }
}
