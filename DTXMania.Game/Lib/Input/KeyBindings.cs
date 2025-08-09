using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Game.Lib.Input
{
    /// <summary>
    /// Represents the key bindings configuration for all input devices
    /// Maps button IDs to lane indices (0-8 for the 9-lane drum layout)
    /// </summary>
    public class KeyBindings
    {
        /// <summary>
        /// Maps button IDs to lane indices
        /// Key: Button ID (e.g., "Key.A", "MIDI.36", "Pad.A")
        /// Value: Lane index (0-8)
        /// </summary>
        public Dictionary<string, int> ButtonToLane { get; set; }

        /// <summary>
        /// Event raised when bindings are changed
        /// </summary>
        public event EventHandler? BindingsChanged;

        public KeyBindings()
        {
            ButtonToLane = new Dictionary<string, int>();
            LoadDefaultBindings();
        }

        /// <summary>
        /// Loads default keyboard bindings
        /// Aligned with DTXChartParser channel-to-lane mapping
        /// </summary>
        public void LoadDefaultBindings()
        {
            ButtonToLane.Clear();

            // Default keyboard bindings aligned with parser mapping
            // Lane 0: 1A (Splash/Crash)
            ButtonToLane["Key.A"] = 0;         // Splash/Crash
            // Lane 1: 18&11 (Floor Tom & Left Cymbal)
            ButtonToLane["Key.S"] = 1;         // Floor Tom & Left Cymbal
            // Lane 2: 1B&1C (Hi-Hat Foot & Left Crash)
            ButtonToLane["Key.D"] = 2;         // Hi-Hat Foot & Left Crash
            // Lane 3: 12 (Left Pedal)
            ButtonToLane["Key.G"] = 3;         // Left Pedal
            // Lane 4: 14 (Snare Drum) - F key for snare
            ButtonToLane["Key.F"] = 4;         // Snare Drum
            // Lane 5: 13 (Hi-Hat)
            ButtonToLane["Key.J"] = 5;         // Hi-Hat
            // Lane 6: 15 (Bass Drum)
            ButtonToLane["Key.Space"] = 6;     // Bass Drum
            // Lane 7: 16 (High Tom)
            ButtonToLane["Key.K"] = 7;         // High Tom
            // Lane 8: 17&19 (Low Tom & Right Cymbal)
            ButtonToLane["Key.L"] = 8; // Low Tom & Right Cymbal
            
        }

        /// <summary>
        /// Gets the lane index for a button ID, or -1 if not bound
        /// </summary>
        /// <param name="buttonId">Button ID to look up</param>
        /// <returns>Lane index (0-8) or -1 if not bound</returns>
        public int GetLane(string buttonId)
        {
            var result = ButtonToLane.TryGetValue(buttonId, out var lane) ? lane : -1;
            
            // DEBUG: Log lane mapping lookups
            System.Diagnostics.Debug.WriteLine($"[KeyBindings] GetLane(\"{buttonId}\") -> {result} {(result >= 0 ? $"({GetLaneName(result)})" : "(unmapped)")}");
            
            return result;
        }

        /// <summary>
        /// Gets all button IDs bound to a specific lane
        /// </summary>
        /// <param name="lane">Lane index (0-8)</param>
        /// <returns>Collection of button IDs bound to this lane</returns>
        public IEnumerable<string> GetButtonsForLane(int lane)
        {
            return ButtonToLane.Where(kvp => kvp.Value == lane).Select(kvp => kvp.Key);
        }

        /// <summary>
        /// Binds a button to a lane
        /// </summary>
        /// <param name="buttonId">Button ID to bind</param>
        /// <param name="lane">Lane index (0-8)</param>
        public void BindButton(string buttonId, int lane)
        {
            if (lane < 0 || lane > 8)
                throw new ArgumentOutOfRangeException(nameof(lane), "Lane must be between 0 and 8");

            ButtonToLane[buttonId] = lane;
            OnBindingsChanged();
        }

        /// <summary>
        /// Unbinds a button from all lanes
        /// </summary>
        /// <param name="buttonId">Button ID to unbind</param>
        public void UnbindButton(string buttonId)
        {
            if (ButtonToLane.Remove(buttonId))
            {
                OnBindingsChanged();
            }
        }

        /// <summary>
        /// Unbinds all buttons from a specific lane
        /// </summary>
        /// <param name="lane">Lane index (0-8)</param>
        public void UnbindLane(int lane)
        {
            var buttonsToRemove = GetButtonsForLane(lane).ToList();
            foreach (var buttonId in buttonsToRemove)
            {
                ButtonToLane.Remove(buttonId);
            }

            if (buttonsToRemove.Count > 0)
            {
                OnBindingsChanged();
            }
        }

        /// <summary>
        /// Clears all bindings
        /// </summary>
        public void ClearAllBindings()
        {
            ButtonToLane.Clear();
            OnBindingsChanged();
        }

        /// <summary>
        /// Gets a human-readable description of the bindings for a lane
        /// </summary>
        /// <param name="lane">Lane index (0-8)</param>
        /// <returns>Human-readable description</returns>
        public string GetLaneDescription(int lane)
        {
            var buttons = GetButtonsForLane(lane).ToList();
            if (!buttons.Any())
                return "Unbound";

            return string.Join(", ", buttons.Select(FormatButtonId));
        }

        /// <summary>
        /// Formats a button ID for display
        /// </summary>
        /// <param name="buttonId">Button ID to format</param>
        /// <returns>Human-readable button name</returns>
        public static string FormatButtonId(string buttonId)
        {
            // Convert button IDs to human-readable format
            if (buttonId.StartsWith("Key."))
            {
                var keyName = buttonId.Substring(4);
                return keyName switch
                {
                    "Space" => "Space",
                    "OemSemicolon" => ";",
                    _ => keyName
                };
            }
            else if (buttonId.StartsWith("MIDI."))
            {
                var noteNumber = buttonId.Substring(5);
                return $"MIDI {noteNumber}";
            }
            else if (buttonId.StartsWith("Pad."))
            {
                var padButton = buttonId.Substring(4);
                return $"Pad {padButton}";
            }

            return buttonId;
        }

        /// <summary>
        /// Creates a button ID from a keyboard key
        /// </summary>
        /// <param name="key">MonoGame Keys enum value</param>
        /// <returns>Button ID string</returns>
        public static string CreateKeyButtonId(Keys key)
        {
            return $"Key.{key}";
        }

        /// <summary>
        /// Creates a button ID from a MIDI note number
        /// </summary>
        /// <param name="noteNumber">MIDI note number (0-127)</param>
        /// <returns>Button ID string</returns>
        public static string CreateMidiButtonId(int noteNumber)
        {
            return $"MIDI.{noteNumber}";
        }

        /// <summary>
        /// Creates a button ID from a gamepad button
        /// </summary>
        /// <param name="button">Gamepad button identifier</param>
        /// <returns>Button ID string</returns>
        public static string CreatePadButtonId(string button)
        {
            return $"Pad.{button}";
        }

        /// <summary>
        /// Raises the BindingsChanged event
        /// </summary>
        protected virtual void OnBindingsChanged()
        {
            BindingsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Gets lane names for display
        /// </summary>
        /// <param name="lane">Lane index (0-8)</param>
        /// <returns>Human-readable lane name</returns>
        public static string GetLaneName(int lane)
        {
            return lane switch
            {
                0 => "Splash/Crash",        // 1A
                1 => "Floor Tom/Left Cymbal", // 18&11
                2 => "Hi-Hat Foot/Left Crash", // 1B&1C
                3 => "Left Pedal",           // 12
                4 => "Snare Drum",           // 14
                5 => "Hi-Hat",               // 13
                6 => "Bass Drum",            // 15
                7 => "High Tom",             // 16
                8 => "Low Tom/Right Cymbal", // 17&19
                _ => $"Lane {lane}"
            };
        }
    }
}
