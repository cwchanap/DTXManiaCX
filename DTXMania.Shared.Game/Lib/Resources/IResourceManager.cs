using System;
using System.Collections.Generic;

namespace DTX.Resources
{
    /// <summary>
    /// Core resource management interface for DTXManiaCX
    /// Provides centralized loading, caching, and disposal of game resources
    /// Based on DTXMania's CSkin.Path() and resource management patterns
    /// </summary>
    public interface IResourceManager : IDisposable
    {
        #region Resource Loading

        /// <summary>
        /// Load a texture from the specified path
        /// Uses skin path resolution and caching
        /// </summary>
        /// <param name="path">Relative path to texture file</param>
        /// <returns>Texture interface with reference counting</returns>
        ITexture LoadTexture(string path);

        /// <summary>
        /// Load a texture with specific parameters
        /// </summary>
        /// <param name="path">Relative path to texture file</param>
        /// <param name="enableTransparency">Enable black color key transparency</param>
        /// <returns>Texture interface with reference counting</returns>
        ITexture LoadTexture(string path, bool enableTransparency);

        /// <summary>
        /// Load a font from the specified path
        /// Supports both system fonts and private font files
        /// </summary>
        /// <param name="path">Path to font file or system font name</param>
        /// <param name="size">Font size in pixels</param>
        /// <returns>Font interface with rendering capabilities</returns>
        IFont LoadFont(string path, int size);

        /// <summary>
        /// Load a font with specific style
        /// </summary>
        /// <param name="path">Path to font file or system font name</param>
        /// <param name="size">Font size in pixels</param>
        /// <param name="style">Font style (Bold, Italic, etc.)</param>
        /// <returns>Font interface with rendering capabilities</returns>
        IFont LoadFont(string path, int size, FontStyle style);

        #endregion

        #region Path Management

        /// <summary>
        /// Set the current skin path for resource resolution
        /// Based on DTXMania's skin switching system
        /// </summary>
        /// <param name="skinPath">Path to skin directory</param>
        void SetSkinPath(string skinPath);

        /// <summary>
        /// Resolve a relative path to absolute path using current skin
        /// Implements DTXMania's CSkin.Path() pattern
        /// </summary>
        /// <param name="relativePath">Relative path from skin root</param>
        /// <returns>Absolute path to resource</returns>
        string ResolvePath(string relativePath);

        /// <summary>
        /// Check if a resource exists at the specified path
        /// </summary>
        /// <param name="relativePath">Relative path to check</param>
        /// <returns>True if resource exists</returns>
        bool ResourceExists(string relativePath);

        #endregion

        #region Resource Management

        /// <summary>
        /// Unload all cached resources
        /// Implements DTXMania's disposal patterns
        /// </summary>
        void UnloadAll();

        /// <summary>
        /// Unload resources matching the specified pattern
        /// </summary>
        /// <param name="pathPattern">Path pattern to match</param>
        void UnloadByPattern(string pathPattern);

        /// <summary>
        /// Get current resource usage statistics
        /// </summary>
        /// <returns>Resource usage information</returns>
        ResourceUsageInfo GetUsageInfo();

        /// <summary>
        /// Force garbage collection of unused resources
        /// </summary>
        void CollectUnusedResources();

        #endregion

        #region Events

        /// <summary>
        /// Raised when a resource fails to load
        /// </summary>
        event EventHandler<ResourceLoadFailedEventArgs> ResourceLoadFailed;

        /// <summary>
        /// Raised when skin path changes
        /// </summary>
        event EventHandler<SkinChangedEventArgs> SkinChanged;

        #endregion
    }

    /// <summary>
    /// Font style enumeration
    /// </summary>
    [Flags]
    public enum FontStyle
    {
        Regular = 0,
        Bold = 1,
        Italic = 2,
        Underline = 4,
        Strikeout = 8
    }

    /// <summary>
    /// Resource usage statistics
    /// </summary>
    public class ResourceUsageInfo
    {
        public int LoadedTextures { get; set; }
        public int LoadedFonts { get; set; }
        public long TotalMemoryUsage { get; set; }
        public int CacheHits { get; set; }
        public int CacheMisses { get; set; }
        public TimeSpan TotalLoadTime { get; set; }
    }

    /// <summary>
    /// Event args for resource load failures
    /// </summary>
    public class ResourceLoadFailedEventArgs : EventArgs
    {
        public string Path { get; }
        public Exception Exception { get; }
        public string ErrorMessage { get; }

        public ResourceLoadFailedEventArgs(string path, Exception exception, string errorMessage = null)
        {
            Path = path;
            Exception = exception;
            ErrorMessage = errorMessage ?? exception?.Message;
        }
    }

    /// <summary>
    /// Event args for skin changes
    /// </summary>
    public class SkinChangedEventArgs : EventArgs
    {
        public string OldSkinPath { get; }
        public string NewSkinPath { get; }

        public SkinChangedEventArgs(string oldSkinPath, string newSkinPath)
        {
            OldSkinPath = oldSkinPath;
            NewSkinPath = newSkinPath;
        }
    }
}
