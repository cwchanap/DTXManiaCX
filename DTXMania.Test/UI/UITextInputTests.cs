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
        public void WhenFocused_ShouldAppendTypedCharacters()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };

            src.Fire('h');
            src.Fire('i');

            Assert.Equal("hi", input.Text);
            Assert.Equal(2, input.CaretIndex);
        }

        [Fact]
        public void WhenUnfocused_ShouldIgnoreTypedCharacters()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = false };

            src.Fire('x');

            Assert.Equal("", input.Text);
        }

        [Fact]
        public void WhenMaxLengthExceeded_ShouldClampInsert()
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
        public void Backspace_WhenCharsExist_ShouldRemoveCharBeforeCaret()
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
        public void Backspace_OnEmpty_ShouldBeNoOp()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };

            input.Backspace();

            Assert.Equal("", input.Text);
            Assert.Equal(0, input.CaretIndex);
        }

        [Fact]
        public void MoveCaret_WhenLeftRight_ShouldBeClampedToTextRange()
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
        public void Backspace_WhenFromMiddleOfText_ShouldRemoveCorrectChar()
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
        public void Clear_WhenTextPresent_ShouldResetTextAndCaret()
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
        public void Focused_WhenSetTrue_ShouldSubscribeToSource()
        {
            var src = new CountingSource();
            var input = new UITextInput(src);

            input.Focused = true;

            Assert.Equal(1, src.HandlerCount);
        }

        [Fact]
        public void Focused_WhenSetFalse_ShouldUnsubscribeFromSource()
        {
            var src = new CountingSource();
            var input = new UITextInput(src) { Focused = true };

            input.Focused = false;

            Assert.Equal(0, src.HandlerCount);
        }

        [Fact]
        public void Focused_WhenSetTrueTwice_ShouldSubscribeOnlyOnce()
        {
            var src = new CountingSource();
            var input = new UITextInput(src);

            input.Focused = true;
            input.Focused = true;

            Assert.Equal(1, src.HandlerCount);
        }

        [Fact]
        public void Dispose_WhenFocused_ShouldUnsubscribeFromSource()
        {
            var src = new CountingSource();
            var input = new UITextInput(src) { Focused = true };

            input.Dispose();

            Assert.Equal(0, src.HandlerCount);
        }
    }
}
