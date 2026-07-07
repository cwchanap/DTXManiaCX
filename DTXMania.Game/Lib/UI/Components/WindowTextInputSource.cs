#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace DTXMania.Game.Lib.UI.Components
{
    public sealed class WindowTextInputSource : ITextInputSource
    {
        private readonly GameWindow _window;
        private bool _disposed;

        public WindowTextInputSource(GameWindow window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _window.TextInput += OnTextInput;
        }

        public event EventHandler<TextInputEventArgs>? TextInput;

        private void OnTextInput(object? sender, TextInputEventArgs e)
        {
            TextInput?.Invoke(sender, e);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _window.TextInput -= OnTextInput;
            _disposed = true;
        }
    }
}
