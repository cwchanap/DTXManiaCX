using System;
using DTXMania.Game.Lib.UI.Components;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.UI
{
    [Trait("Category", "Unit")]
    public class UITextInputExtendedTests
    {
        private sealed class FakeSource : ITextInputSource
        {
            public event EventHandler<TextInputEventArgs>? TextInput;
            public void Fire(char c) =>
                TextInput?.Invoke(this, new TextInputEventArgs(c, Microsoft.Xna.Framework.Input.Keys.None));
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
        public void TextSetter_WhenMaxLengthZero_ShouldAlwaysBeEmpty()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { MaxLength = 0 };

            input.Text = "hello";

            Assert.Equal("", input.Text);
        }

        [Fact]
        public void TextSetter_WhenMaxLengthNegative_ShouldAlwaysBeEmpty()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { MaxLength = -1 };

            input.Text = "hello";

            Assert.Equal("", input.Text);
        }

        [Fact]
        public void TextSetter_WhenMaxLengthZero_CaretShouldBeClampedToZero()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { MaxLength = 5 };
            input.Text = "abc";

            input.MaxLength = 0;
            input.Text = "xyz";

            Assert.Equal("", input.Text);
            Assert.Equal(0, input.CaretIndex);
        }

        [Fact]
        public void Dispose_WhenNotFocused_ShouldNotThrow()
        {
            var src = new CountingSource();
            var input = new UITextInput(src) { Focused = false };

            input.Dispose();

            Assert.Equal(0, src.HandlerCount);
        }

        [Fact]
        public void Focused_WhenToggledOnOff_ShouldSubscribeThenUnsubscribe()
        {
            var src = new CountingSource();
            var input = new UITextInput(src);

            input.Focused = true;
            Assert.Equal(1, src.HandlerCount);

            input.Focused = false;
            Assert.Equal(0, src.HandlerCount);
        }

        [Fact]
        public void TextSetter_WhenMaxLengthZeroAndValueEmpty_ShouldBeEmpty()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { MaxLength = 0 };

            input.Text = "";

            Assert.Equal("", input.Text);
        }

        [Fact]
        public void TextSetter_WhenMaxLengthNegativeAndValueNull_ShouldBeEmpty()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { MaxLength = -5 };

            input.Text = null;

            Assert.Equal("", input.Text);
        }
    }
}
