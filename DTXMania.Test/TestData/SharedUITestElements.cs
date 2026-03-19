using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.UI;

namespace DTXMania.Test
{
    /// <summary>
    /// Minimal concrete UIElement used across UI test classes where a non-abstract instance is needed.
    /// Inherits the default behavior from UIElement without adding additional logic.
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
