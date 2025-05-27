using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.Stage;
using DTXMania.Shared.Game;

namespace DTX.Stage
{
    public class StartupStage : IStage
    {
        private readonly BaseGame _game;
        private double _elapsedTime;
        private SpriteBatch _spriteBatch;
        private Texture2D _whitePixel;

        public StageType Type => StageType.Startup;

        public StartupStage(BaseGame game)
        {
            _game = game;
        }

        public void Activate()
        {
            _elapsedTime = 0;
            // Load startup resources
            // For Phase 1, we'll just display text
            var graphicsDevice = _game.GraphicsDevice;
            _spriteBatch = new SpriteBatch(graphicsDevice);

            _whitePixel = new Texture2D(graphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });
        }

        public void Update(double deltaTime)
        {
            _elapsedTime += deltaTime;

            // Auto-transition to UI test stage after 3 seconds
            if (_elapsedTime > 3.0)
            {
                _game.StageManager.ChangeStage(StageType.Config); // UI test stage
            }
        }

        public void Draw(double deltaTime)
        {
            // For Phase 1, just draw basic text
            // In later phases, this will show splash screen
            _spriteBatch.Begin();

            // Draw a simple loading text
            var text = "DTXMania NX - MonoGame Edition";
            var subtext = $"Loading... {_elapsedTime:F1}s";

            // Since we don't have fonts yet, draw rectangles to simulate text
            DrawTextRect(100, 100, text.Length * 10, 20, Color.White);
            DrawTextRect(100, 130, subtext.Length * 8, 16, Color.Gray);

            // Draw a simple progress bar
            var progressWidth = (int)((_elapsedTime / 3.0) * 400);
            DrawTextRect(100, 200, 400, 10, Color.DarkGray); // Background
            DrawTextRect(100, 200, progressWidth, 10, Color.LightGreen); // Progress

            _spriteBatch.End();
        }

        private void DrawTextRect(int x, int y, int width, int height, Color color)
        {
            _spriteBatch.Draw(_whitePixel, new Rectangle(x, y, width, height), color);
        }

        public void Deactivate()
        {
            // Cleanup if needed
            _whitePixel?.Dispose();
            _spriteBatch?.Dispose();
        }
    }
}