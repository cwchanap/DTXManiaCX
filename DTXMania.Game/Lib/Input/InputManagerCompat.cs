#nullable enable

using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Input.Midi;
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
        private readonly ConfigManager _configManager;
        private readonly HashSet<InputCommandType> _injectedCommandsThisFrame = new();
        private readonly HashSet<InputCommandType> _padCommandsThisFrame = new();
        private bool _disposed = false;

        public InputManagerCompat(IConfigManager configManager)
            : this(configManager, MidiDeviceBackendFactory.CreateDefault())
        {
        }

        internal InputManagerCompat(IConfigManager configManager, IMidiDeviceBackend midiDeviceBackend)
        {
            if (midiDeviceBackend is null)
                throw new ArgumentNullException(nameof(midiDeviceBackend));

            if (configManager is ConfigManager cm)
            {
                _configManager = cm;
                _modularInputManager = new ModularInputManager(cm, midiDeviceBackend);
                
                // Wire up debugging for lane hits
                _modularInputManager.OnLaneHit += OnModularLaneHit;

                // Live-apply: Config → runtime (unidirectional). When Config edits fire these
                // events, rebuild the runtime binding maps from Config. ReloadKeyBindings()
                // suppresses its internal BindingsChanged during reload, so no feedback loop.
                _configManager.KeyBindingsChanged += OnConfigKeyBindingsChanged;
                _configManager.SystemKeyBindingsChanged += OnConfigSystemKeyBindingsChanged;
            }
            else
            {
                throw new ArgumentException("ConfigManager must be of type DTX.Config.ConfigManager", nameof(configManager));
            }
        }

        /// <summary>
        /// Maps a drum-pad lane hit to a system command, or null to ignore it.
        /// BaseGame points this at <c>DrumPadSystemKeys</c> for the current stage.
        /// </summary>
        public Func<int, InputCommandType?>? PadCommandResolver { get; set; }

        private void OnModularLaneHit(object? sender, LaneHitEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[InputManagerCompat] Lane {e.Lane} hit by {e.Button.Id} (velocity: {e.Button.Velocity:F2})");

            // Keyboard drum keys keep their letters; only real pads act as system keys.
            if (!e.Button.IsPressed || KeyBindings.IsKeyboardButtonId(e.Button.Id))
                return;

            if (PadCommandResolver?.Invoke(e.Lane) is { } command)
            {
                _injectedCommandsThisFrame.Add(command);
                _padCommandsThisFrame.Add(command);
                EnqueueCommand(new InputCommand(command, CurrentTime, false));
            }
        }

        private void OnConfigKeyBindingsChanged(object? sender, EventArgs e) => _modularInputManager.ReloadKeyBindings();

        private void OnConfigSystemKeyBindingsChanged(object? sender, EventArgs e) => _configManager.LoadSystemKeyBindings(this);

        /// <summary>
        /// Gets the underlying ModularInputManager for advanced features
        /// </summary>
        public ModularInputManager ModularInputManager => _modularInputManager;

        public override void ClearPendingCommands()
        {
            base.ClearPendingCommands();
            _injectedCommandsThisFrame.Clear();
            _padCommandsThisFrame.Clear();
            _modularInputManager?.ClearInjectedState();
        }

        public override void Update(double deltaTime)
        {
            _injectedCommandsThisFrame.Clear();
            _padCommandsThisFrame.Clear();

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

                        _injectedCommandsThisFrame.Add(commandType);
                        EnqueueCommand(new InputCommand(commandType, CurrentTime, false));
                    }
                }
            }
        }

        public override bool IsCommandPressed(InputCommandType command)
        {
            return _injectedCommandsThisFrame.Contains(command) || base.IsCommandPressed(command);
        }

        /// <summary>
        /// True when a drum pad (not a key) raised this command this frame. Pads never type
        /// text, so text fields can still accept them while ignoring remapped keys.
        /// </summary>
        public bool IsPadCommandPressed(InputCommandType command) => _padCommandsThisFrame.Contains(command);

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
            return IsCommandPressed(InputCommandType.Back);
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
                _configManager.KeyBindingsChanged -= OnConfigKeyBindingsChanged;
                _configManager.SystemKeyBindingsChanged -= OnConfigSystemKeyBindingsChanged;
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }
}
