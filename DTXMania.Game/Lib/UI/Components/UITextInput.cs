#nullable enable

using System;
using DTXMania.Game.Lib.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.UI.Components
{
    public class UITextInput : UIElement
    {
        private readonly ITextInputSource _source;
        private string _text = "";
        private int _caretIndex;
        private bool _subscribed;

        public UITextInput(ITextInputSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            OnFocus  += (_, _) => Subscribe();
            OnBlur   += (_, _) => Unsubscribe();
        }

        public string Text
        {
            get => _text;
            set
            {
                var incoming = value ?? "";
                var maxLen = Math.Max(0, MaxLength);
                _text = incoming.Length > maxLen
                    ? incoming.Substring(0, maxLen)
                    : incoming;
                _caretIndex = Math.Clamp(_caretIndex, 0, _text.Length);
            }
        }

        public int CaretIndex => _caretIndex;

        public int MaxLength { get; set; } = 64;

        public SpriteFont? Font { get; set; }

        public Color TextColor { get; set; } = Color.White;

        private void Subscribe()
        {
            if (_subscribed) return;
            _source.TextInput += OnTextInput;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _source.TextInput -= OnTextInput;
            _subscribed = false;
        }

        private void OnTextInput(object? sender, TextInputEventArgs e)
        {
            if (!Focused) return;

            char c = e.Character;
            if (c == '\b' || c == '\r' || c == '\n' || c == '\t') return;
            if (char.IsControl(c)) return;
            if (_text.Length >= MaxLength) return;

            _text = _text.Insert(_caretIndex, c.ToString());
            _caretIndex++;
        }

        public void Backspace()
        {
            if (_caretIndex == 0 || _text.Length == 0) return;
            _text = _text.Remove(_caretIndex - 1, 1);
            _caretIndex--;
        }

        public void MoveCaret(int delta)
        {
            _caretIndex = Math.Clamp(_caretIndex + delta, 0, _text.Length);
        }

        public void Clear()
        {
            _text = "";
            _caretIndex = 0;
        }

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (Font == null) return;
            var pos = AbsolutePosition;
            spriteBatch.DrawString(Font, _text, pos, TextColor);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Unsubscribe();
            base.Dispose(disposing);
        }
    }
}
