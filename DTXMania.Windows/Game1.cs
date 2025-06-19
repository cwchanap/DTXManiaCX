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
        
        // Configure platform-specific font factory before content loading
        // This must be done before StartupStage activates in LoadContent()
        ResourceManagerFactory.SetFontFactory(new WindowsFontFactory(Content));
    }

    protected override void LoadContent()
    {
        base.LoadContent();
    }
}
