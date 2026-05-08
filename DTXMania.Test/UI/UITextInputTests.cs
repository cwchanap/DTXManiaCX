using System;
using DTXMania.Game.Lib.UI.Components;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.UI
{
    public class UITextInputTests
    {
        private sealed class FakeSource : ITextInputSource
        {
            public event EventHandler<TextInputEventArgs>? TextInput;
            public void Fire(char c) =>
                TextInput?.Invoke(this, new TextInputEventArgs(c, ToKey(c)));
            private static Microsoft.Xna.Framework.Input.Keys ToKey(char c) =>
                Microsoft.Xna.Framework.Input.Keys.None;
        }

        [Fact]
        public void Focused_AppendsTypedCharacters()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };

            src.Fire('h');
            src.Fire('i');

            Assert.Equal("hi", input.Text);
            Assert.Equal(2, input.CaretIndex);
        }

        [Fact]
        public void Unfocused_IgnoresTypedCharacters()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = false };

            src.Fire('x');

            Assert.Equal("", input.Text);
        }

        [Fact]
        public void MaxLength_ClampsInsert()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true, MaxLength = 3 };

            src.Fire('a');
            src.Fire('b');
            src.Fire('c');
            src.Fire('d');

            Assert.Equal("abc", input.Text);
        }
    }
}
