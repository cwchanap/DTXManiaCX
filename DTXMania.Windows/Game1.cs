using DTXMania.Shared.Game;

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
        // TODO: Add your initialization logic here

        base.Initialize();
    }
}
