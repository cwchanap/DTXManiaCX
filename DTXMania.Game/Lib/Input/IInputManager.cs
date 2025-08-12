using System;

namespace DTXMania.Game.Lib.Input
{
    /// <summary>
    /// Interface for input management functionality
    /// Enables proper mocking and dependency injection for testing
    /// </summary>
    public interface IInputManager : IDisposable
    {
        /// <summary>
        /// Updates the input state with the given delta time
        /// </summary>
        /// <param name="deltaTime">Time since last update in seconds</param>
        void Update(double deltaTime);

        /// <summary>
        /// Checks if a key was just pressed (edge-triggered)
        /// </summary>
        /// <param name="keyCode">The key code to check</param>
        /// <returns>True if the key was just pressed this frame</returns>
        bool IsKeyPressed(int keyCode);

        /// <summary>
        /// Checks if a key is currently being held down
        /// </summary>
        /// <param name="keyCode">The key code to check</param>
        /// <returns>True if the key is currently pressed</returns>
        bool IsKeyDown(int keyCode);

        /// <summary>
        /// Checks if a key was just released (edge-triggered)
        /// </summary>
        /// <param name="keyCode">The key code to check</param>
        /// <returns>True if the key was just released this frame</returns>
        bool IsKeyReleased(int keyCode);

        /// <summary>
        /// Checks if a key was just triggered (same as IsKeyPressed)
        /// </summary>
        /// <param name="keyCode">The key code to check</param>
        /// <returns>True if the key was just triggered this frame</returns>
        bool IsKeyTriggered(int keyCode);

        /// <summary>
        /// Checks if the "back" action was just triggered
        /// </summary>
        /// <returns>True if the back action was just triggered</returns>
        bool IsBackActionTriggered();

        /// <summary>
        /// Gets the next input command from the queue, if any
        /// </summary>
        /// <returns>The next input command, or null if none available</returns>
        InputCommand? GetNextCommand();

        /// <summary>
        /// Checks if there are any pending input commands
        /// </summary>
        bool HasPendingCommands { get; }

        /// <summary>
        /// Checks if a specific input command type was just pressed
        /// </summary>
        /// <param name="commandType">The command type to check</param>
        /// <returns>True if the command was just pressed</returns>
        bool IsCommandPressed(InputCommandType commandType);
    }
}