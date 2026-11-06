#nullable enable

using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>Recording <see cref="IVideoFrameSurface"/> for headless player tests.</summary>
    internal sealed class FakeVideoFrameSurface : IVideoFrameSurface
    {
        public int PresentCount { get; private set; }

        public int LastWidth { get; private set; }

        public int LastHeight { get; private set; }

        public int ResetCount { get; private set; }

        public int DisposeCount { get; private set; }

        public Texture2D? Texture => null;

        public void Present(byte[] rgba, int width, int height)
        {
            PresentCount++;
            LastWidth = width;
            LastHeight = height;
        }

        public void Reset()
        {
            ResetCount++;
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
