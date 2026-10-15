#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Game.Lib.Input
{
    /// <summary>
    /// Folds keys injected through <see cref="ModularInputManager.InjectButton"/> (the GameApi
    /// <c>sendInput</c> / MCP <c>game_send_key</c> path) into a hardware <see cref="KeyboardState"/>
    /// pair, so UI that edge-detects on raw keyboard state (the Config overlay panels) also responds
    /// to automation. Injected input never appears in <see cref="Keyboard.GetState()"/>.
    /// </summary>
    public static class InjectedKeyboardState
    {
        /// <summary>
        /// Merges the injected keys of <paramref name="input"/> into the frame's keyboard states.
        /// Returns the inputs unchanged when <paramref name="input"/> is null or holds no injected keys.
        /// </summary>
        public static (KeyboardState Current, KeyboardState Previous) Merge(
            KeyboardState hardwareCurrent,
            KeyboardState previous,
            ModularInputManager? input)
        {
            if (input == null)
                return (hardwareCurrent, previous);

            return Merge(hardwareCurrent, previous, input.InjectedKeysDown, input.InjectedKeysPressedThisFrame);
        }

        /// <summary>
        /// Builds this frame's (current, previous) pair. Current is the hardware state plus every
        /// injected key that is held or was pressed this frame — a press and release drained in the
        /// same frame still counts. Previous drops each key freshly pressed by injection this frame,
        /// so the press reads as a rising edge even when an earlier injected tap left it down last
        /// frame; a key physically held is left alone, mirroring <see cref="InputManagerCompat"/>,
        /// which ignores an injected press of a key the hardware already holds. With no injected
        /// keys the hardware states are returned as-is, so real keyboard behaviour is unchanged.
        /// </summary>
        public static (KeyboardState Current, KeyboardState Previous) Merge(
            KeyboardState hardwareCurrent,
            KeyboardState previous,
            IReadOnlyCollection<int> injectedDown,
            IReadOnlyCollection<int> injectedPressedThisFrame)
        {
            if (injectedDown.Count == 0 && injectedPressedThisFrame.Count == 0)
                return (hardwareCurrent, previous);

            var currentKeys = new HashSet<Keys>(hardwareCurrent.GetPressedKeys());
            foreach (var keyCode in injectedDown)
                currentKeys.Add((Keys)keyCode);
            foreach (var keyCode in injectedPressedThisFrame)
                currentKeys.Add((Keys)keyCode);

            var current = new KeyboardState([.. currentKeys], hardwareCurrent.CapsLock, hardwareCurrent.NumLock);

            HashSet<Keys>? previousKeys = null;
            foreach (var keyCode in injectedPressedThisFrame)
            {
                var key = (Keys)keyCode;
                if (!previous.IsKeyDown(key) || hardwareCurrent.IsKeyDown(key))
                    continue;

                previousKeys ??= new HashSet<Keys>(previous.GetPressedKeys());
                previousKeys.Remove(key);
            }

            var adjustedPrevious = previousKeys == null
                ? previous
                : new KeyboardState([.. previousKeys], previous.CapsLock, previous.NumLock);

            return (current, adjustedPrevious);
        }
    }
}
