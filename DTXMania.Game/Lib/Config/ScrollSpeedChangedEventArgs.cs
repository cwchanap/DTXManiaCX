using System;

namespace DTXMania.Game.Lib.Config
{
    public sealed class ScrollSpeedChangedEventArgs : EventArgs
    {
        public int OldPercent { get; }
        public int NewPercent { get; }

        public ScrollSpeedChangedEventArgs(int oldPercent, int newPercent)
        {
            OldPercent = oldPercent;
            NewPercent = newPercent;
        }
    }
}
