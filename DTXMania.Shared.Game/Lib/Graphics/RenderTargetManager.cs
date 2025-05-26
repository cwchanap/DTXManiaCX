using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace DTX.Graphics
{
    /// <summary>
    /// Manages render targets and handles automatic recreation on device reset
    /// </summary>
    public class RenderTargetManager : IDisposable
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly Dictionary<string, RenderTargetInfo> _renderTargets;
        private bool _disposed = false;

        public RenderTargetManager(GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _renderTargets = new Dictionary<string, RenderTargetInfo>();
        }

        /// <summary>
        /// Creates or gets a render target with the specified parameters
        /// </summary>
        /// <param name="name">Unique name for the render target</param>
        /// <param name="width">Width of the render target</param>
        /// <param name="height">Height of the render target</param>
        /// <param name="format">Surface format</param>
        /// <param name="depthFormat">Depth format</param>
        /// <param name="multiSampleCount">Multi-sample count</param>
        /// <returns>The render target</returns>
        public RenderTarget2D GetOrCreateRenderTarget(string name, int width, int height, 
            SurfaceFormat format = SurfaceFormat.Color, 
            DepthFormat depthFormat = DepthFormat.None,
            int multiSampleCount = 0)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Name cannot be null or empty", nameof(name));

            if (_renderTargets.TryGetValue(name, out var existingInfo))
            {
                // Check if parameters match
                if (existingInfo.Width == width && 
                    existingInfo.Height == height &&
                    existingInfo.Format == format &&
                    existingInfo.DepthFormat == depthFormat &&
                    existingInfo.MultiSampleCount == multiSampleCount &&
                    existingInfo.RenderTarget != null &&
                    !existingInfo.RenderTarget.IsDisposed)
                {
                    return existingInfo.RenderTarget;
                }

                // Parameters don't match or render target is disposed, recreate
                existingInfo.RenderTarget?.Dispose();
                _renderTargets.Remove(name);
            }

            // Create new render target
            var renderTarget = new RenderTarget2D(_graphicsDevice, width, height, false, 
                format, depthFormat, multiSampleCount, RenderTargetUsage.DiscardContents);

            var info = new RenderTargetInfo
            {
                RenderTarget = renderTarget,
                Width = width,
                Height = height,
                Format = format,
                DepthFormat = depthFormat,
                MultiSampleCount = multiSampleCount
            };

            _renderTargets[name] = info;
            return renderTarget;
        }

        /// <summary>
        /// Gets an existing render target by name
        /// </summary>
        /// <param name="name">Name of the render target</param>
        /// <returns>The render target, or null if not found</returns>
        public RenderTarget2D GetRenderTarget(string name)
        {
            if (_renderTargets.TryGetValue(name, out var info))
            {
                return info.RenderTarget?.IsDisposed == false ? info.RenderTarget : null;
            }
            return null;
        }

        /// <summary>
        /// Removes and disposes a render target
        /// </summary>
        /// <param name="name">Name of the render target to remove</param>
        public void RemoveRenderTarget(string name)
        {
            if (_renderTargets.TryGetValue(name, out var info))
            {
                info.RenderTarget?.Dispose();
                _renderTargets.Remove(name);
            }
        }

        /// <summary>
        /// Recreates all render targets (useful after device reset)
        /// </summary>
        public void RecreateAllRenderTargets()
        {
            var recreateList = new List<(string name, RenderTargetInfo info)>();
            
            // Collect all render targets that need recreation
            foreach (var kvp in _renderTargets)
            {
                recreateList.Add((kvp.Key, kvp.Value));
            }

            // Dispose and recreate each one
            foreach (var (name, info) in recreateList)
            {
                info.RenderTarget?.Dispose();
                
                var newRenderTarget = new RenderTarget2D(_graphicsDevice, 
                    info.Width, info.Height, false, 
                    info.Format, info.DepthFormat, info.MultiSampleCount, 
                    RenderTargetUsage.DiscardContents);

                info.RenderTarget = newRenderTarget;
                _renderTargets[name] = info;
            }
        }

        /// <summary>
        /// Gets the number of managed render targets
        /// </summary>
        public int Count => _renderTargets.Count;

        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (var info in _renderTargets.Values)
                {
                    info.RenderTarget?.Dispose();
                }
                _renderTargets.Clear();
                _disposed = true;
            }
        }

        private class RenderTargetInfo
        {
            public RenderTarget2D RenderTarget { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public SurfaceFormat Format { get; set; }
            public DepthFormat DepthFormat { get; set; }
            public int MultiSampleCount { get; set; }
        }
    }
}
