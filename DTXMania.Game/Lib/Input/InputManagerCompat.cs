#nullable enable

using System;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Game.Lib.Input
{
    /// <summary>
    /// Compatibility wrapper that makes ModularInputManager work as a drop-in replacement
    /// for the existing InputManager while providing the new functionality
    /// </summary>
    public class InputManagerCompat : InputManager, IInputManagerCompat
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

        public override void ClearPendingCommands()
        {
            base.ClearPendingCommands();
            _modularInputManager?.ClearInjectedState();
        }

        public override void Update(double deltaTime)
        {
            // Update the modular input manager
            _modularInputManager?.Update(deltaTime);

            // Also call base update for compatibility (reads hardware keyboard)
            base.Update(deltaTime);

            // Dispatch navigation commands for injected (MCP/API) key-press events only.
            // base.Update() already handles physical keyboard input via UpdateKeyRepeatStates().
            // We use event-driven dispatch (one command per press event) to correctly handle
            // rapid successive injections without relying on frame-by-frame state edge detection.
            if (_modularInputManager != null)
            {
                var pressEvents = _modularInputManager.DrainInjectedPressEvents();
                var hardwareKeyboardState = Keyboard.GetState();
                while (pressEvents.Count > 0)
                {
                    var keyCode = pressEvents.Dequeue();
                    var key = (Keys)keyCode;
                    if (KeyMapping.TryGetValue(key, out var commandType))
                    {
                        if (hardwareKeyboardState.IsKeyDown(key))
                        {
                            continue;
                        }

                        EnqueueCommand(new InputCommand(commandType, CurrentTime, false));
                    }
                }
            }
        }

        public override bool IsKeyPressed(int keyCode)
        {
            // Use the modular input manager's logic
            var result = _modularInputManager?.IsKeyPressed(keyCode) ?? base.IsKeyPressed(keyCode);
            if (result)
            {
                System.Diagnostics.Debug.WriteLine($"[InputManagerCompat] IsKeyPressed({keyCode}) = true");
            }
            return result;
        }

        public override bool IsKeyDown(int keyCode)
        {
            // Use the modular input manager's logic
            return _modularInputManager?.IsKeyDown(keyCode) ?? base.IsKeyDown(keyCode);
        }

        public override bool IsKeyReleased(int keyCode)
        {
            // Use the modular input manager's logic
            return _modularInputManager?.IsKeyReleased(keyCode) ?? base.IsKeyReleased(keyCode);
        }

        /// <summary>
        /// Check if a key was just triggered (edge-trigger)
        /// Uses the modular input manager's debounced logic
        /// </summary>
        public override bool IsKeyTriggered(int keyCode)
        {
            // Use the modular input manager's logic if available
            return _modularInputManager?.IsKeyTriggered(keyCode) ?? base.IsKeyPressed(keyCode);
        }

        /// <summary>
        /// Consolidated method to detect "back" action from both ESC key and controller Back button
        /// Uses proper debouncing for ESC key to prevent repeat triggers
        /// </summary>
        public override bool IsBackActionTriggered()
        {
            return base.IsBackActionTriggered();
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
