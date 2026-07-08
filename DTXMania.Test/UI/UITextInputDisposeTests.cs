using System;
using DTXMania.Game.Lib.UI.Components;
using Microsoft.Xna.Framework;
using Xunit;
using static DTXMania.Test.TestData.ReflectionHelpers;

namespace DTXMania.Test.UI
{
    [Trait("Category", "Unit")]
    public class UITextInputDisposeTests
    {
        private sealed class FakeSource : ITextInputSource
        {
            public event EventHandler<TextInputEventArgs>? TextInput;
            public void Fire(char c) =>
                TextInput?.Invoke(this, new TextInputEventArgs(c, Microsoft.Xna.Framework.Input.Keys.None));
            public void Dispose() { }
        }

        [Fact]
        public void Dispose_WhenSubscribed_ShouldUnsubscribe()
        {
            var source = new FakeSource();
            var input = new UITextInput(source);

            input.Focused = true;
            source.Fire('A');

            input.Dispose();

            source.Fire('B');
            Assert.Equal("A", input.Text);
        }

        [Fact]
        public void Dispose_WhenAlreadyDisposed_ShouldNotThrow()
        {
            var source = new FakeSource();
            var input = new UITextInput(source);

            input.Dispose();

            var exception = Record.Exception(() => input.Dispose());
            Assert.Null(exception);
        }

        [Fact]
        public void OnDraw_NullFont_ShouldNotThrow()
        {
            var source = new FakeSource();
            var input = new UITextInput(source);

            var exception = Record.Exception(() =>
                InvokePrivateMethod(input, "OnDraw", null!, 0.0));

            Assert.Null(exception);
        }

        [Fact]
        public void Clear_ShouldResetTextAndCaret()
        {
            var source = new FakeSource();
            var input = new UITextInput(source);

            input.Focused = true;
            source.Fire('H');
            source.Fire('i');

            Assert.Equal("Hi", input.Text);

            input.Clear();

            Assert.Equal("", input.Text);
            Assert.Equal(0, input.CaretIndex);
        }
    }
}
