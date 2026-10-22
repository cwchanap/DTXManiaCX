#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Game.Lib.Input
{
    /// <summary>
    /// Represents the key bindings configuration for all input devices
    /// Maps button IDs to lane indices (0-9 for the 10-lane drum layout)
    /// </summary>
    public class KeyBindings
    {
        /// <summary>
        /// Private backing field for button-to-lane mappings
        /// </summary>
        private readonly Dictionary<string, int> _buttonToLane;

        /// <summary>
        /// Maps button IDs to lane indices (read-only)
        /// Key: Button ID (e.g., "Key.A", "MIDI.36", "Pad.A")
        /// Value: Lane index (0-9)
        /// </summary>
        public IReadOnlyDictionary<string, int> ButtonToLane => _buttonToLane;

        /// <summary>
        /// Event raised when bindings are changed
        /// </summary>
        public event EventHandler? BindingsChanged;

        public KeyBindings()
        {
            _buttonToLane = new Dictionary<string, int>();
            LoadDefaultBindings();
        }

        /// <summary>
        /// Loads default keyboard bindings
        /// Aligned with DTXChartParser channel-to-lane mapping
        /// (0 LC, 1 HH, 2 LP, 3 SN, 4 HT, 5 DB, 6 LT, 7 FT, 8 CY, 9 RD)
        /// </summary>
        public void LoadDefaultBindings()
        {
            _buttonToLane.Clear();

            _buttonToLane["Key.A"] = 0;            // Left Cymbal
            _buttonToLane["Key.J"] = 1;            // Hi-Hat
            _buttonToLane["Key.G"] = 2;            // Left Pedal
            _buttonToLane["Key.S"] = 3;            // Snare
            _buttonToLane["Key.K"] = 4;            // High Tom
            _buttonToLane["Key.Space"] = 5;        // Bass Drum
            _buttonToLane["Key.L"] = 6;            // Low Tom
            _buttonToLane["Key.F"] = 7;            // Floor Tom
            _buttonToLane["Key.D"] = 8;            // Cymbal
            _buttonToLane["Key.OemSemicolon"] = 9; // Ride
        }

        /// <summary>
        /// Gets the lane index for a button ID, or -1 if not bound
        /// </summary>
        /// <param name="buttonId">Button ID to look up</param>
        /// <returns>Lane index (0-9) or -1 if not bound</returns>
        public int GetLane(string buttonId)
        {
            return ButtonToLane.TryGetValue(buttonId, out var lane) ? lane : -1;
        }

        /// <summary>
        /// Gets all button IDs bound to a specific lane
        /// </summary>
        /// <param name="lane">Lane index (0-9)</param>
        /// <returns>Collection of button IDs bound to this lane</returns>
        public IEnumerable<string> GetButtonsForLane(int lane)
        {
            return ButtonToLane.Where(kvp => kvp.Value == lane).Select(kvp => kvp.Key);
        }

        /// <summary>
        /// Binds a button to a lane
        /// </summary>
        /// <param name="buttonId">Button ID to bind</param>
        /// <param name="lane">Lane index (0-9)</param>
        public void BindButton(string buttonId, int lane)
        {
            if (lane < 0 || lane > 9)
                throw new ArgumentOutOfRangeException(nameof(lane), "Lane must be between 0 and 9");

            _buttonToLane[buttonId] = lane;
            OnBindingsChanged();
        }

        /// <summary>
        /// Unbinds a button from all lanes
        /// </summary>
        /// <param name="buttonId">Button ID to unbind</param>
        public void UnbindButton(string buttonId)
        {
            if (_buttonToLane.Remove(buttonId))
            {
                OnBindingsChanged();
            }
        }

        /// <summary>
        /// Unbinds all buttons from a specific lane
        /// </summary>
        /// <param name="lane">Lane index (0-9)</param>
        public void UnbindLane(int lane)
        {
            var buttonsToRemove = GetButtonsForLane(lane).ToList();
            foreach (var buttonId in buttonsToRemove)
            {
                _buttonToLane.Remove(buttonId);
            }

            if (buttonsToRemove.Count > 0)
            {
                OnBindingsChanged();
            }
        }

        public void UnbindKeyboardButtonsForLane(int lane)
        {
            var buttonsToRemove = GetButtonsForLane(lane)
                .Where(IsKeyboardButtonId)
                .ToList();
            foreach (var buttonId in buttonsToRemove)
            {
                _buttonToLane.Remove(buttonId);
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
            _buttonToLane.Clear();
            OnBindingsChanged();
        }

        /// <summary>
        /// Returns a deep copy of this KeyBindings instance with no shared state.
        /// </summary>
        public KeyBindings Clone()
        {
            var clone = new KeyBindings();
            clone.ClearAllBindings();
            foreach (var kvp in _buttonToLane)
                clone.BindButton(kvp.Key, kvp.Value);
            return clone;
        }

        /// <summary>
        /// Gets a human-readable description of the bindings for a lane
        /// </summary>
        /// <param name="lane">Lane index (0-9)</param>
        /// <returns>Human-readable description</returns>
        public string GetLaneDescription(int lane)
        {
            var buttons = GetButtonsForLane(lane).ToList();
            if (!buttons.Any())
                return "Unbound";

            return string.Join(", ", buttons.Select(FormatButtonId));
        }

        public static bool IsKeyboardButtonId(string buttonId)
        {
            return !string.IsNullOrWhiteSpace(buttonId)
                && buttonId.StartsWith("Key.", StringComparison.Ordinal);
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
        /// Attempts to parse a button ID in the exact "MIDI.&lt;note&gt;" format.
        /// </summary>
        /// <param name="buttonId">Button ID to parse.</param>
        /// <param name="noteNumber">Parsed MIDI note number when valid; otherwise 0.</param>
        /// <returns>true if the ID has the exact MIDI prefix and a note number from 0 to 127; otherwise false.</returns>
        public static bool TryParseMidiButtonId(string buttonId, out int noteNumber)
        {
            noteNumber = default;
            const string prefix = "MIDI.";
            if (string.IsNullOrWhiteSpace(buttonId) ||
                !buttonId.StartsWith(prefix, StringComparison.Ordinal) ||
                buttonId.Length <= prefix.Length)
            {
                return false;
            }

            var noteText = buttonId.Substring(prefix.Length);
            // Reject anything that isn't canonical decimal form (no leading zeros except for "0"
            // itself) so only IDs produced by CreateMidiButtonId round-trip. This keeps saved
            // bindings stable and prevents "MIDI.036"/"MIDI.00" from colliding with "MIDI.36"/"MIDI.0".
            if (noteText.Length == 0 ||
                noteText.Any(ch => ch < '0' || ch > '9') ||
                (noteText.Length > 1 && noteText[0] == '0') ||
                !int.TryParse(noteText, out var parsedNoteNumber) ||
                parsedNoteNumber < 0 ||
                parsedNoteNumber > 127)
            {
                return false;
            }

            noteNumber = parsedNoteNumber;
            return true;
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
        /// Gets the canonical friendly lane name for display
        /// (single source shared by ConfigStage AutoPlay menu, DrumCapturePopup,
        /// KeyConflictChecker and the drum-kit zones)
        /// </summary>
        /// <param name="lane">Lane index (0-9)</param>
        /// <returns>Human-readable lane name</returns>
        public static string GetLaneName(int lane)
        {
            return lane switch
            {
                0 => "Left Cymbal",
                1 => "Hi-Hat",
                2 => "Left Pedal",
                3 => "Snare",
                4 => "High Tom",
                5 => "Bass Drum",
                6 => "Low Tom",
                7 => "Floor Tom",
                8 => "Cymbal",
                9 => "Ride",
                _ => $"Lane {lane}"
            };
        }
    }
}
