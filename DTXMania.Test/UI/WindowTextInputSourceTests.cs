using System;
using DTXMania.Game.Lib.UI.Components;
using Xunit;

namespace DTXMania.Test.UI
{
    public class WindowTextInputSourceTests
    {
        [Fact]
        public void Constructor_NullWindow_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new WindowTextInputSource(null!));
        }
    }
}
