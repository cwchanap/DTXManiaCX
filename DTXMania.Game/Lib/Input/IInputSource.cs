using System;
using System.Collections.Generic;

namespace DTXMania.Game.Lib.Input
{
    /// <summary>
    /// Represents a single input button state from any input device
    /// </summary>
    public class ButtonState
    {
        /// <summary>
        /// Unique ID for this button (e.g., "Key.A", "MIDI.36", "Pad.A")
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Whether the button is currently pressed down
        /// </summary>
        public bool IsPressed { get; set; }

        /// <summary>
        /// Velocity/intensity of the press (0.0 to 1.0)
        /// </summary>
        public float Velocity { get; set; }

        /// <summary>
        /// Timestamp when this state was captured
        /// </summary>
        public DateTime Timestamp { get; set; }

        public ButtonState(string id, bool isPressed, float velocity = 1.0f)
        {
            Id = id;
            IsPressed = isPressed;
            Velocity = velocity;
            Timestamp = DateTime.UtcNow;
        }

        public override string ToString()
        {
            return $"ButtonState[{Id}] {(IsPressed ? "PRESSED" : "RELEASED")} (vel: {Velocity:F2})";
        }
    }

    /// <summary>
    /// Interface for input sources (keyboard, MIDI, gamepad)
    /// </summary>
    public interface IInputSource : IDisposable
    {
        /// <summary>
        /// Friendly name for this input source
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Whether this input source is currently available
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Initializes the input source
        /// </summary>
        void Initialize();

        /// <summary>
        /// Updates the input source and returns button states
        /// </summary>
        /// <returns>Collection of button states that have changed</returns>
        IEnumerable<ButtonState> Update();

        /// <summary>
        /// Gets all currently pressed buttons
        /// </summary>
        /// <returns>Collection of currently pressed button states</returns>
        IEnumerable<ButtonState> GetPressedButtons();
    }

    /// <summary>
    /// Event args for lane hit events
    /// </summary>
    public class LaneHitEventArgs : EventArgs
    {
        /// <summary>
        /// Lane index (0-9 for the 10-lane drum layout)
        /// </summary>
        public int Lane { get; set; }

        /// <summary>
        /// Button that triggered this hit
        /// </summary>
        public ButtonState Button { get; set; }

        /// <summary>
        /// Timestamp when the hit occurred
        /// </summary>
        public DateTime Timestamp { get; set; }

        public LaneHitEventArgs(int lane, ButtonState button)
        {
            Lane = lane;
            Button = button;
            Timestamp = DateTime.UtcNow;
        }
    }
}
