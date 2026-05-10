using System;
using System.Reflection;
using DTXMania.Game.Lib.UI.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace DTXMania.Test.UI
{
    [Trait("Category", "Unit")]
    public class WindowTextInputSourceTests
    {
        private sealed class StubGameWindow : GameWindow
        {
            public override bool AllowUserResizing { get; set; }
            public override Rectangle ClientBounds => default;
            public override Point Position { get; set; }
            public override DisplayOrientation CurrentOrientation => DisplayOrientation.Default;
            public override nint Handle => 0;
            public override string ScreenDeviceName => "";
            public override void BeginScreenDeviceChange(bool willBeFullScreen) { }
            public override void EndScreenDeviceChange(string screenDeviceName, int clientWidth, int clientHeight) { }
            protected override void SetSupportedOrientations(DisplayOrientation orientations) { }
            protected override void SetTitle(string title) { }

            private static readonly MethodInfo OnTextInputMethod =
                typeof(GameWindow).GetMethod("OnTextInput", BindingFlags.Instance | BindingFlags.NonPublic)!;

            public void RaiseTextInput(char c)
            {
                OnTextInputMethod.Invoke(this, [new TextInputEventArgs(c, Keys.None)]);
            }
        }

        [Fact]
        public void Constructor_NullWindow_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new WindowTextInputSource(null!));
        }

        [Fact]
        public void TextInput_ForwardsEvents()
        {
            var window = new StubGameWindow();
            var source = new WindowTextInputSource(window);
            char? received = null;
            source.TextInput += (_, e) => received = e.Character;

            window.RaiseTextInput('x');

            Assert.Equal('x', received);
        }

        [Fact]
        public void Dispose_UnsubscribesFromWindow()
        {
            var window = new StubGameWindow();
            var source = new WindowTextInputSource(window);
            var count = 0;
            source.TextInput += (_, _) => count++;

            source.Dispose();
            window.RaiseTextInput('a');

            Assert.Equal(0, count);
        }

        [Fact]
        public void DoubleDispose_IsSafe()
        {
            var window = new StubGameWindow();
            var source = new WindowTextInputSource(window);

            source.Dispose();
            source.Dispose();
        }

        [Fact]
        public void AfterDispose_TextInputDoesNotForward()
        {
            var window = new StubGameWindow();
            var source = new WindowTextInputSource(window);
            var count = 0;
            source.TextInput += (_, _) => count++;

            source.Dispose();
            window.RaiseTextInput('a');
            window.RaiseTextInput('b');

            Assert.Equal(0, count);
        }
    }
}
