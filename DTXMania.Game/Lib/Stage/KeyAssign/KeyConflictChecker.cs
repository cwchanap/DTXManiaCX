#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.Input;

namespace DTXMania.Game.Lib.Stage.KeyAssign
{
    /// <summary>
    /// Pure static utility for detecting conflicts between drum key bindings and system key bindings.
    /// </summary>
    public static class KeyConflictChecker
    {
        /// <summary>
        /// System commands that must always remain bound (navigation/core actions).
        /// Non-required commands (e.g. IncreaseScrollSpeed) can be auto-evicted
        /// when the user assigns that key to a drum lane.
        /// </summary>
        private static readonly HashSet<InputCommandType> RequiredCommands = new()
        {
            InputCommandType.MoveUp,
            InputCommandType.MoveDown,
            InputCommandType.MoveLeft,
            InputCommandType.MoveRight,
            InputCommandType.Activate,
            InputCommandType.Back,
        };

        /// <summary>
        /// Returns true if the command is a required system command that cannot be evicted.
        /// </summary>
        public static bool IsRequiredCommand(InputCommandType command) => RequiredCommands.Contains(command);

        /// <summary>
        /// Checks if a candidate key is already mapped as a system navigation key.
        /// Returns a human-readable error message, or null if no conflict.
        /// </summary>
        public static string? CheckAgainstSystemBindings(
            IReadOnlyDictionary<Keys, InputCommandType> systemBindings,
            Keys candidate)
        {
            if (systemBindings.TryGetValue(candidate, out var command))
                return $"{candidate} is already bound to system action: {command}";
            return null;
        }

        /// <summary>
        /// Checks if a candidate key conflicts with a required system command.
        /// Returns the conflicting command if it is required, or null if the key
        /// is either not in system bindings or only bound to non-required commands.
        /// Non-required commands (e.g. IncreaseScrollSpeed) can be auto-evicted
        /// by the caller when assigning a drum lane.
        /// </summary>
        public static InputCommandType? GetRequiredSystemConflict(
            IReadOnlyDictionary<Keys, InputCommandType> systemBindings,
            Keys candidate)
        {
            if (systemBindings.TryGetValue(candidate, out var command) && IsRequiredCommand(command))
                return command;
            return null;
        }

        /// <summary>
        /// Checks if a candidate key is already mapped to a drum lane.
        /// Returns a human-readable error message, or null if no conflict.
        /// </summary>
        public static string? CheckDrumConflict(
            IReadOnlyDictionary<string, int> drumBindings,
            Keys candidate)
        {
            var buttonId = KeyBindings.CreateKeyButtonId(candidate);
            if (drumBindings.TryGetValue(buttonId, out var lane))
                return $"{candidate} is already bound to drum lane: {KeyBindings.GetLaneName(lane)}";
            return null;
        }

        /// <summary>
        /// Checks if a candidate key being assigned to a system action conflicts with any drum binding.
        /// Also checks if the key is already bound to a different system action.
        /// </summary>
        public static string? CheckSystemAssignConflict(
            IReadOnlyDictionary<string, int> drumBindings,
            IReadOnlyDictionary<Keys, InputCommandType> systemBindings,
            Keys candidate,
            InputCommandType targetCommand)
        {
            // Cross-conflict: key is a drum key
            var drumConflict = CheckDrumConflict(drumBindings, candidate);
            if (drumConflict != null)
                return drumConflict;

            // Within-system conflict: key already bound to a DIFFERENT system action
            if (systemBindings.TryGetValue(candidate, out var existingCommand) &&
                existingCommand != targetCommand)
                return $"{candidate} is already bound to system action: {existingCommand}";

            return null;
        }
    }
}
