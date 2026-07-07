using System;
using DTXMania.Game.Lib.UI.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Test.TestData
{
    /// <summary>
    /// Minimal <see cref="ITextInputSource"/> test double that lets tests fire synthetic
    /// text-input events via <see cref="Fire"/>. Shared across the SongSearchFilterModal
    /// and UITextInput test suites so each suite does not redeclare its own copy.
    /// </summary>
    internal sealed class FakeTextInputSource : ITextInputSource
    {
        public event EventHandler<TextInputEventArgs>? TextInput;

        public void Fire(char c) =>
            TextInput?.Invoke(this, new TextInputEventArgs(c, Keys.None));

        public void Dispose() { }
    }
}
