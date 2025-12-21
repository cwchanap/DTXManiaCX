using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Graphics;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Stage
{
    internal interface IStageGame
    {
        GraphicsDevice GraphicsDevice { get; }
        IStageManager StageManager { get; }
        IConfigManager ConfigManager { get; }
        InputManagerCompat InputManager { get; }
        IGraphicsManager GraphicsManager { get; }
        IResourceManager ResourceManager { get; }

        bool CanPerformStageTransition();
        void MarkStageTransition();
    }
}
