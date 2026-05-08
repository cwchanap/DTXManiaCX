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

        [Fact]
        public void Backspace_RemovesCharBeforeCaret()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };

            src.Fire('a');
            src.Fire('b');
            src.Fire('c');
            input.Backspace();

            Assert.Equal("ab", input.Text);
            Assert.Equal(2, input.CaretIndex);
        }

        [Fact]
        public void Backspace_OnEmpty_NoOp()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };

            input.Backspace();

            Assert.Equal("", input.Text);
            Assert.Equal(0, input.CaretIndex);
        }

        [Fact]
        public void MoveCaret_LeftRight_ClampedToTextRange()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };
            src.Fire('a');
            src.Fire('b');

            input.MoveCaret(-5);
            Assert.Equal(0, input.CaretIndex);

            input.MoveCaret(+10);
            Assert.Equal(2, input.CaretIndex);
        }

        [Fact]
        public void Backspace_FromMiddleOfText_RemovesCorrectChar()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };
            src.Fire('a');
            src.Fire('b');
            src.Fire('c');
            input.MoveCaret(-1); // caret between 'b' and 'c'

            input.Backspace();

            Assert.Equal("ac", input.Text);
            Assert.Equal(1, input.CaretIndex);
        }

        [Fact]
        public void Clear_ResetsTextAndCaret()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };
            src.Fire('a');
            src.Fire('b');

            input.Clear();

            Assert.Equal("", input.Text);
            Assert.Equal(0, input.CaretIndex);
        }

        private sealed class CountingSource : ITextInputSource
        {
            public event EventHandler<TextInputEventArgs>? TextInput;
            public int HandlerCount =>
                TextInput?.GetInvocationList().Length ?? 0;
            public void Fire(char c) =>
                TextInput?.Invoke(this, new TextInputEventArgs(c, Microsoft.Xna.Framework.Input.Keys.None));
        }

        [Fact]
        public void Focused_True_SubscribesToSource()
        {
            var src = new CountingSource();
            var input = new UITextInput(src);

            input.Focused = true;

            Assert.Equal(1, src.HandlerCount);
        }

        [Fact]
        public void Focused_False_UnsubscribesFromSource()
        {
            var src = new CountingSource();
            var input = new UITextInput(src) { Focused = true };

            input.Focused = false;

            Assert.Equal(0, src.HandlerCount);
        }

        [Fact]
        public void Focused_TrueTwice_SubscribesOnlyOnce()
        {
            var src = new CountingSource();
            var input = new UITextInput(src);

            input.Focused = true;
            input.Focused = true;

            Assert.Equal(1, src.HandlerCount);
        }

        [Fact]
        public void Dispose_UnsubscribesFromSource()
        {
            var src = new CountingSource();
            var input = new UITextInput(src) { Focused = true };

            input.Dispose();

            Assert.Equal(0, src.HandlerCount);
        }
    }
}
