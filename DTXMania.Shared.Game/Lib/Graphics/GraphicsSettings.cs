using System;
using System.Collections.Generic;
using System.Linq;

namespace DTX.Graphics
{
    /// <summary>
    /// Represents graphics device settings
    /// </summary>
    public class GraphicsSettings : IEquatable<GraphicsSettings>
    {
        /// <summary>
        /// Screen width in pixels
        /// </summary>
        public int Width { get; set; } = 1280;

        /// <summary>
        /// Screen height in pixels
        /// </summary>
        public int Height { get; set; } = 720;

        /// <summary>
        /// Whether to run in fullscreen mode
        /// </summary>
        public bool IsFullscreen { get; set; } = false;

        /// <summary>
        /// Whether to enable vertical synchronization
        /// </summary>
        public bool VSync { get; set; } = true;

        /// <summary>
        /// Preferred back buffer format
        /// </summary>
        public Microsoft.Xna.Framework.Graphics.SurfaceFormat BackBufferFormat { get; set; } = 
            Microsoft.Xna.Framework.Graphics.SurfaceFormat.Color;

        /// <summary>
        /// Preferred depth stencil format
        /// </summary>
        public Microsoft.Xna.Framework.Graphics.DepthFormat DepthStencilFormat { get; set; } = 
            Microsoft.Xna.Framework.Graphics.DepthFormat.Depth24;

        /// <summary>
        /// Multi-sample anti-aliasing level
        /// </summary>
        public int MultiSampleCount { get; set; } = 0;

        /// <summary>
        /// Creates a copy of the current settings
        /// </summary>
        /// <returns>A new GraphicsSettings instance with the same values</returns>
        public GraphicsSettings Clone()
        {
            return new GraphicsSettings
            {
                Width = Width,
                Height = Height,
                IsFullscreen = IsFullscreen,
                VSync = VSync,
                BackBufferFormat = BackBufferFormat,
                DepthStencilFormat = DepthStencilFormat,
                MultiSampleCount = MultiSampleCount
            };
        }

        /// <summary>
        /// Validates the current settings
        /// </summary>
        /// <returns>True if settings are valid</returns>
        public bool IsValid()
        {
            return Width > 0 && Height > 0 && 
                   Width <= 7680 && Height <= 4320 && // 8K max
                   MultiSampleCount >= 0;
        }

        /// <summary>
        /// Gets common 16:9 resolutions
        /// </summary>
        public static IEnumerable<(int Width, int Height)> GetCommonResolutions()
        {
            return new[]
            {
                (1280, 720),   // 720p
                (1366, 768),   // Common laptop
                (1600, 900),   // 900p
                (1920, 1080),  // 1080p
                (2560, 1440),  // 1440p
                (3840, 2160),  // 4K
                (7680, 4320)   // 8K
            };
        }

        /// <summary>
        /// Gets the aspect ratio of the current resolution
        /// </summary>
        public double AspectRatio => (double)Width / Height;

        public bool Equals(GraphicsSettings other)
        {
            if (other == null) return false;
            
            return Width == other.Width &&
                   Height == other.Height &&
                   IsFullscreen == other.IsFullscreen &&
                   VSync == other.VSync &&
                   BackBufferFormat == other.BackBufferFormat &&
                   DepthStencilFormat == other.DepthStencilFormat &&
                   MultiSampleCount == other.MultiSampleCount;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as GraphicsSettings);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Width, Height, IsFullscreen, VSync, 
                                  BackBufferFormat, DepthStencilFormat, MultiSampleCount);
        }

        public override string ToString()
        {
            return $"{Width}x{Height} {(IsFullscreen ? "Fullscreen" : "Windowed")} " +
                   $"VSync:{VSync} MSAA:{MultiSampleCount}";
        }
    }
}
