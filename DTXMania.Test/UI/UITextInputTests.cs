using System;
using DTXMania.Game.Lib.UI.Components;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.UI
{
    [Trait("Category", "Unit")]
    public class UITextInputTests
    {
        private sealed class FakeSource : ITextInputSource
        {
            public event EventHandler<TextInputEventArgs>? TextInput;
            public void Fire(char c) =>
                TextInput?.Invoke(this, new TextInputEventArgs(c, ToKey(c)));
            private static Microsoft.Xna.Framework.Input.Keys ToKey(char c) =>
                Microsoft.Xna.Framework.Input.Keys.None;
            public void Dispose() { }
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
            public void Dispose() { }
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

        [Fact]
        public void TextSetter_WhenExceedsMaxLength_ShouldTruncate()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true, MaxLength = 5 };

            input.Text = "abcdefghij";

            Assert.Equal("abcde", input.Text);
        }

        [Fact]
        public void TextSetter_WhenWithinMaxLength_ShouldNotTruncate()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true, MaxLength = 10 };

            input.Text = "hello";

            Assert.Equal("hello", input.Text);
        }

        [Fact]
        public void TextSetter_WhenNull_ShouldSetEmpty()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };

            input.Text = null;

            Assert.Equal("", input.Text);
        }

        [Fact]
        public void TextSetter_WhenTruncated_CaretShouldBeClamped()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true, MaxLength = 3 };
            input.Text = "abc";
            input.MoveCaret(3); // caret at end (3)

            input.Text = "abcdefgh"; // gets truncated to "abc"

            Assert.Equal(3, input.CaretIndex); // clamped to new length
        }

        [Fact]
        public void OnTextInput_BackslashCharCode_Ignored()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };

            src.Fire('\b');

            Assert.Equal("", input.Text);
        }

        [Fact]
        public void OnTextInput_ReturnCharCode_Ignored()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };

            src.Fire('\r');

            Assert.Equal("", input.Text);
        }

        [Fact]
        public void OnTextInput_NewlineCharCode_Ignored()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };

            src.Fire('\n');

            Assert.Equal("", input.Text);
        }

        [Fact]
        public void OnTextInput_TabCharCode_Ignored()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };

            src.Fire('\t');

            Assert.Equal("", input.Text);
        }

        [Fact]
        public void OnTextInput_ControlChar_Ignored()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };

            src.Fire('\u0001');

            Assert.Equal("", input.Text);
        }

        [Fact]
        public void Backspace_WhenCaretAtPosition0_IsNoOp()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };
            src.Fire('a');
            input.MoveCaret(-1); // caret at 0

            input.Backspace();

            Assert.Equal("a", input.Text);
            Assert.Equal(0, input.CaretIndex);
        }

        [Fact]
        public void OnTextInput_WhenMaxLengthExactlyReached_ShouldNotInsertMore()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true, MaxLength = 2 };
            src.Fire('a');
            src.Fire('b');
            src.Fire('c');

            Assert.Equal("ab", input.Text);
            Assert.Equal(2, input.CaretIndex);
        }

        [Fact]
        public void MoveCaret_WhenAtEndAndMoveRight_ShouldClamp()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };
            src.Fire('x');
            input.MoveCaret(1);

            Assert.Equal(1, input.CaretIndex);
        }

        [Fact]
        public void OnTextInput_WhenNotFocused_ShouldNotInsert()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = false };

            src.Fire('a');

            Assert.Equal("", input.Text);
            Assert.Equal(0, input.CaretIndex);
        }

        [Fact]
        public void Constructor_WhenNullSource_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() => new UITextInput(null!));
        }

        [Fact]
        public void Backspace_WhenTextEmptyAndCaretNonZero_ShouldBeNoOp()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true, MaxLength = 5 };
            input.Text = "ab";
            input.Clear();
            Assert.Equal("", input.Text);

            input.Backspace();

            Assert.Equal("", input.Text);
        }
    }
}
