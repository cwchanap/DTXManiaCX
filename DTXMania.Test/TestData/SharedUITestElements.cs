using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.UI;

namespace DTXMania.Test
{
    /// <summary>
    /// Minimal concrete UIElement for use across UI test classes.
    /// Provides no-op implementations of all abstract members.
    /// </summary>
    internal class ConcreteUIElement : UIElement
    {
    }

    /// <summary>
    /// UIElement that records calls to OnUpdate, OnPositionChanged, and
    /// OnSizeChanged so tests can verify those hooks are triggered correctly.
    /// </summary>
    internal class TrackingUIElement : UIElement
    {
        public int UpdateCallCount { get; private set; }
        public bool PositionChangedCalled { get; set; }
        public bool SizeChangedCalled { get; set; }

        protected override void OnUpdate(double deltaTime)
        {
            UpdateCallCount++;
        }

        protected override void OnPositionChanged()
        {
            PositionChangedCalled = true;
        }

        protected override void OnSizeChanged()
        {
            SizeChangedCalled = true;
        }
    }
}
