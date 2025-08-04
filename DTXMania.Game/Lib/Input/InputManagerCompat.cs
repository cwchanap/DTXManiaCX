using System;
using DTX.Config;
using DTX.Input;

namespace DTXMania.Game.Lib.Input
{
    /// <summary>
    /// Compatibility wrapper that makes ModularInputManager work as a drop-in replacement
    /// for the existing InputManager while providing the new functionality
    /// </summary>
    public class InputManagerCompat : InputManager
    {
        private readonly ModularInputManager _modularInputManager;
        private bool _disposed = false;

        public InputManagerCompat(IConfigManager configManager)
        {
            if (configManager is ConfigManager cm)
            {
                _modularInputManager = new ModularInputManager(cm);
                
                // Wire up debugging for lane hits
                _modularInputManager.OnLaneHit += OnModularLaneHit;
            }
            else
            {
                throw new ArgumentException("ConfigManager must be of type DTX.Config.ConfigManager", nameof(configManager));
            }
        }

        private void OnModularLaneHit(object? sender, LaneHitEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[InputManagerCompat] Lane {e.Lane} hit by {e.Button.Id} (velocity: {e.Button.Velocity:F2})");
        }

        /// <summary>
        /// Gets the underlying ModularInputManager for advanced features
        /// </summary>
        public ModularInputManager ModularInputManager => _modularInputManager;

        public new void Update(double deltaTime)
        {
            // Update the modular input manager
            _modularInputManager?.Update(deltaTime);
            
            // Also call base update for compatibility
            base.Update(deltaTime);
        }

        public new bool IsKeyPressed(int keyCode)
        {
            // Use the modular input manager's logic
            var result = _modularInputManager?.IsKeyPressed(keyCode) ?? base.IsKeyPressed(keyCode);
            if (result)
            {
                System.Diagnostics.Debug.WriteLine($"[InputManagerCompat] IsKeyPressed({keyCode}) = true");
            }
            return result;
        }

        public new bool IsKeyDown(int keyCode)
        {
            // Use the modular input manager's logic
            return _modularInputManager?.IsKeyDown(keyCode) ?? base.IsKeyDown(keyCode);
        }

        public new bool IsKeyReleased(int keyCode)
        {
            // Use the modular input manager's logic
            return _modularInputManager?.IsKeyReleased(keyCode) ?? base.IsKeyReleased(keyCode);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                if (_modularInputManager != null)
                {
                    _modularInputManager.OnLaneHit -= OnModularLaneHit;
                    _modularInputManager.Dispose();
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }
}
