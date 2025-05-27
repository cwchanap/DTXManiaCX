using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTX.Stage;
using DTXMania.Shared.Game;
using System;

namespace DTX.Stage
{
    /// <summary>
    /// Placeholder Config stage - will be implemented later
    /// </summary>
    public class ConfigStage : IStage
    {
        private readonly BaseGame _game;
        private SpriteBatch _spriteBatch;
        private Texture2D _whitePixel;
        private KeyboardState _previousKeyboardState;
        private KeyboardState _currentKeyboardState;

        public StageType Type => StageType.Config;

        public ConfigStage(BaseGame game)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
        }

        public void Activate()
        {
            System.Diagnostics.Debug.WriteLine("Activating Config Stage (Placeholder)");

            var graphicsDevice = _game.GraphicsDevice;
            _spriteBatch = new SpriteBatch(graphicsDevice);

            _whitePixel = new Texture2D(graphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });

            _previousKeyboardState = Keyboard.GetState();
            _currentKeyboardState = Keyboard.GetState();
        }

        public void Update(double deltaTime)
        {
            _previousKeyboardState = _currentKeyboardState;
            _currentKeyboardState = Keyboard.GetState();

            // Handle ESC key - return to title stage (previous stage)
            if (IsKeyPressed(Keys.Escape))
            {
                System.Diagnostics.Debug.WriteLine("Config: ESC pressed - returning to Title stage");
                _game.StageManager?.ChangeStage(StageType.Title);
            }
        }

        public void Draw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;

            _spriteBatch.Begin();

            // Draw background
            var viewport = _game.GraphicsDevice.Viewport;
            _spriteBatch.Draw(_whitePixel,
                new Rectangle(0, 0, viewport.Width, viewport.Height),
                new Color(32, 32, 64));

            // Draw placeholder text
            DrawTextRect(100, 100, "CONFIG STAGE (PLACEHOLDER)".Length * 12, 24, Color.White);
            DrawTextRect(100, 150, "Press ESC to return to Title".Length * 8, 16, Color.Gray);

            _spriteBatch.End();
        }

        public void Deactivate()
        {
            System.Diagnostics.Debug.WriteLine("Deactivating Config Stage");

            _whitePixel?.Dispose();
            _spriteBatch?.Dispose();

            _whitePixel = null;
            _spriteBatch = null;
        }

        private bool IsKeyPressed(Keys key)
        {
            return _currentKeyboardState.IsKeyDown(key) && !_previousKeyboardState.IsKeyDown(key);
        }

        private void DrawTextRect(int x, int y, int width, int height, Color color)
        {
            if (_whitePixel != null)
            {
                _spriteBatch.Draw(_whitePixel, new Rectangle(x, y, width, height), color);
            }
        }
    }
}
