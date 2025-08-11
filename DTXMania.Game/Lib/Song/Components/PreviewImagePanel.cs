using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.Resources;

namespace DTXMania.Game.Lib.Song.Components
{
    /// <summary>
    /// DTXManiaNX-compatible preview image panel for displaying album art and preview videos
    /// Equivalent to CActSelectPreimagePanel from DTXManiaNX
    /// </summary>
    public class PreviewImagePanel : UIElement
    {
        #region Fields

        private SongListNode _currentSong;
        private ITexture _currentPreviewTexture;
        private ITexture _defaultPreviewTexture;
        private IResourceManager _resourceManager;
        private Texture2D _whitePixel;

        // DTXManiaNX layout constants (authentic positioning)
        // Position determined by status panel presence:
        // - Without status panel: X:18, Y:88 (368×368 pixels)
        // - With status panel: X:250, Y:34 (292×292 pixels)
        private const int WITHOUT_STATUS_PANEL_X = 18;
        private const int WITHOUT_STATUS_PANEL_Y = 88;
        private const int WITHOUT_STATUS_PANEL_SIZE = 368;

        private const int WITH_STATUS_PANEL_X = 250;
        private const int WITH_STATUS_PANEL_Y = 34;
        private const int WITH_STATUS_PANEL_SIZE = 292;

        // Content offsets from DTXManiaNX
        private const int CONTENT_OFFSET_WITHOUT_STATUS = 37; // X+37, Y+24
        private const int CONTENT_OFFSET_Y_WITHOUT_STATUS = 24;
        private const int CONTENT_OFFSET_WITH_STATUS = 8; // X+8, Y+8

        private bool _hasStatusPanel = true; // Default to with status panel

        // Display delay timing (moved from SongSelectionStage)
        private double _displayDelay = 0.0;
        private const double DISPLAY_DELAY_SECONDS = 0.5; // 500ms delay before showing preview

        #endregion

        #region Properties

        /// <summary>
        /// Whether the status panel is present (affects positioning and size)
        /// </summary>
        public bool HasStatusPanel
        {
            get => _hasStatusPanel;
            set
            {
                if (_hasStatusPanel != value)
                {
                    _hasStatusPanel = value;
                    UpdatePositionAndSize();
                }
            }
        }

        /// <summary>
        /// White pixel texture for backgrounds
        /// </summary>
        public Texture2D WhitePixel
        {
            get => _whitePixel;
            set => _whitePixel = value;
        }

        #endregion

        #region Constructor

        public PreviewImagePanel()
        {
            UpdatePositionAndSize();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initialize with resource manager for loading preview images
        /// </summary>
        public void Initialize(IResourceManager resourceManager)
        {
            _resourceManager = resourceManager;
            LoadDefaultPreviewTexture();
        }

        /// <summary>
        /// Update the displayed song (DTXManiaNX t選択曲が変更された equivalent)
        /// </summary>
        public void UpdateSelectedSong(SongListNode song)
        {
            if (_currentSong == song)
                return;

            _currentSong = song;

            // Reset display delay when selection changes
            ResetDisplayDelay();

            // Only load preview image if this is actually a song (not a folder or back bar)
            if (song?.Type == NodeType.Score)
            {
                // Load preview image using comprehensive method
                LoadPreviewImageComprehensive(song);
            }
            else
            {
                // Clear preview immediately when not on a song
                _currentPreviewTexture = null;
            }
        }

        /// <summary>
        /// Update timing and delay logic (called from stage)
        /// </summary>
        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);

            // Update display delay counter
            if (_displayDelay < DISPLAY_DELAY_SECONDS)
            {
                _displayDelay += deltaTime;
            }
        }

        #endregion

        #region Protected Methods

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (!Visible)
            {
                return;
            }

            var bounds = Bounds;

            // Draw background panel
            DrawBackground(spriteBatch, bounds);

            // Draw preview content
            DrawPreviewContent(spriteBatch, bounds);

            base.OnDraw(spriteBatch, deltaTime);
        }

        #endregion

        #region Private Methods

        private void UpdatePositionAndSize()
        {
            if (_hasStatusPanel)
            {
                // With status panel: X:250, Y:34 (292×292 pixels)
                Position = new Vector2(WITH_STATUS_PANEL_X, WITH_STATUS_PANEL_Y);
                Size = new Vector2(WITH_STATUS_PANEL_SIZE, WITH_STATUS_PANEL_SIZE);
            }
            else
            {
                // Without status panel: X:18, Y:88 (368×368 pixels)
                Position = new Vector2(WITHOUT_STATUS_PANEL_X, WITHOUT_STATUS_PANEL_Y);
                Size = new Vector2(WITHOUT_STATUS_PANEL_SIZE, WITHOUT_STATUS_PANEL_SIZE);
            }
        }

        /// <summary>
        /// Load default preview texture
        /// </summary>
        private void LoadDefaultPreviewTexture()
        {
            try
            {
                if (_resourceManager != null)
                {
                    _defaultPreviewTexture = _resourceManager.LoadTexture(TexturePath.DefaultPreview);

                    // Verify the loaded texture is valid
                    if (_defaultPreviewTexture != null && _defaultPreviewTexture.IsDisposed)
                    {
                        _defaultPreviewTexture = null;
                    }
                }
                else
                {
                    _defaultPreviewTexture = null;
                }
            }
            catch (Exception)
            {
                // Default texture is optional
                _defaultPreviewTexture = null;
            }
        }

        /// <summary>
        /// Reset display delay when selection changes
        /// </summary>
        private void ResetDisplayDelay()
        {
            _displayDelay = 0.0;
        }

        /// <summary>
        /// Check if preview should be displayed (after delay)
        /// </summary>
        private bool ShouldDisplayPreview()
        {
            return _displayDelay >= DISPLAY_DELAY_SECONDS &&
                   _currentSong?.Type == NodeType.Score &&
                   (_currentPreviewTexture != null || _defaultPreviewTexture != null);
        }

        /// <summary>
        /// Load preview image for the selected song (comprehensive version from SongSelectionStage)
        /// </summary>
        private void LoadPreviewImageComprehensive(SongListNode songNode)
        {
            // Check if resource manager is still valid
            if (_resourceManager == null)
            {
                return;
            }

            // Clear current preview reference (don't dispose - ResourceManager handles that)
            _currentPreviewTexture = null;

            // If not a song (folder, back button, etc.), don't load any preview
            if (songNode?.Type != NodeType.Score)
            {
                return;
            }

            try
            {
                // Get the song directory from the node
                string songDirectory = GetSongDirectoryFromNode(songNode);

                if (string.IsNullOrEmpty(songDirectory))
                {
                    // Use default texture if available and not disposed
                    if (_defaultPreviewTexture != null && !_defaultPreviewTexture.IsDisposed)
                    {
                        _currentPreviewTexture = _defaultPreviewTexture;
                    }
                    return;
                }

                // Resolve to absolute path
                string resolvedDirectory = ResolveSongDirectoryPath(songDirectory);

                // Verify directory exists before trying to find preview files
                if (!System.IO.Directory.Exists(resolvedDirectory))
                {
                    if (_defaultPreviewTexture != null && !_defaultPreviewTexture.IsDisposed)
                    {
                        _currentPreviewTexture = _defaultPreviewTexture;
                    }
                    return;
                }

                // Find preview image file
                string previewPath = FindPreviewImageFile(resolvedDirectory);

                if (previewPath != null)
                {
                    // Try to load the texture, with additional error handling
                    try
                    {
                        _currentPreviewTexture = _resourceManager.LoadTexture(previewPath);

                        // Verify the loaded texture is valid
                        if (_currentPreviewTexture != null && _currentPreviewTexture.IsDisposed)
                        {
                            _currentPreviewTexture = _defaultPreviewTexture;
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        _currentPreviewTexture = _defaultPreviewTexture;
                    }
                }
                else
                {
                    // Use default texture if available and not disposed
                    if (_defaultPreviewTexture != null && !_defaultPreviewTexture.IsDisposed)
                    {
                        _currentPreviewTexture = _defaultPreviewTexture;
                    }
                    else
                    {
                        _currentPreviewTexture = null;
                    }
                }
            }
            catch (Exception)
            {
                // Try to use default texture as fallback, but check if it's valid first
                if (_defaultPreviewTexture != null && !_defaultPreviewTexture.IsDisposed)
                {
                    _currentPreviewTexture = _defaultPreviewTexture;
                }
                else
                {
                    _currentPreviewTexture = null;
                }
            }
        }

        /// <summary>
        /// Get the song directory from a SongListNode by checking DatabaseSong charts and DirectoryPath
        /// </summary>
        private string GetSongDirectoryFromNode(SongListNode songNode)
        {
            string songDirectory = null;

            // Try to get song directory from DatabaseSong
            if (songNode.DatabaseSong?.Charts?.Count > 0)
            {
                var chartPath = songNode.DatabaseSong.Charts.FirstOrDefault()?.FilePath;
                if (!string.IsNullOrEmpty(chartPath))
                {
                    songDirectory = System.IO.Path.GetDirectoryName(chartPath);
                }
            }

            // If we couldn't get directory from chart, try the DirectoryPath property
            if (string.IsNullOrEmpty(songDirectory) && !string.IsNullOrEmpty(songNode.DirectoryPath))
            {
                songDirectory = songNode.DirectoryPath;
            }

            return songDirectory;
        }

        /// <summary>
        /// Resolve a song directory to an absolute path by checking multiple possible locations
        /// </summary>
        private string ResolveSongDirectoryPath(string songDirectory)
        {
            // Convert to absolute path if needed - handle both relative and already absolute paths
            if (!System.IO.Path.IsPathRooted(songDirectory))
            {
                // Try to resolve relative path from current working directory or known song directories
                try
                {
                    var workingDir = Environment.CurrentDirectory;

                    var possiblePaths = new[]
                    {
                        System.IO.Path.GetFullPath(songDirectory), // From current directory
                        System.IO.Path.GetFullPath(System.IO.Path.Combine(workingDir, songDirectory)), // From app directory
                        System.IO.Path.GetFullPath(System.IO.Path.Combine(workingDir, "DTXFiles", songDirectory)), // From DTXFiles folder
                        System.IO.Path.GetFullPath(System.IO.Path.Combine(workingDir, "Songs", songDirectory)), // From Songs folder
                        System.IO.Path.GetFullPath(System.IO.Path.Combine(workingDir, "..", "Songs", songDirectory)), // Parent Songs folder
                        System.IO.Path.GetFullPath(System.IO.Path.Combine(workingDir, "..", "DTXFiles", songDirectory)) // Parent DTXFiles folder
                    };

                    foreach (var testPath in possiblePaths)
                    {
                        if (System.IO.Directory.Exists(testPath))
                        {
                            songDirectory = testPath;
                            break;
                        }
                    }
                }
                catch (Exception)
                {
                    songDirectory = System.IO.Path.GetFullPath(songDirectory); // Fallback to simple resolution
                }
            }

            return songDirectory;
        }

        /// <summary>
        /// Find a preview image file by searching for known preview filenames with supported extensions
        /// </summary>
        private string FindPreviewImageFile(string songDirectory)
        {
            string[] previewExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };
            string[] previewNames = { "preview", "jacket", "banner" };

            // Try to find preview image
            foreach (var name in previewNames)
            {
                foreach (var ext in previewExtensions)
                {
                    var testPath = System.IO.Path.Combine(songDirectory, name + ext);
                    if (System.IO.File.Exists(testPath))
                    {
                        return testPath;
                    }
                }
            }

            return null;
        }

        private void DrawBackground(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_whitePixel == null)
                return;

            // Draw semi-transparent background
            var backgroundColor = Color.Black * 0.8f;
            spriteBatch.Draw(_whitePixel, bounds, backgroundColor);

            // Draw border
            var borderColor = Color.White * 0.4f;
            var borderThickness = 2;

            // Top border
            spriteBatch.Draw(_whitePixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, borderThickness), borderColor);
            // Bottom border
            spriteBatch.Draw(_whitePixel, new Rectangle(bounds.X, bounds.Bottom - borderThickness, bounds.Width, borderThickness), borderColor);
            // Left border
            spriteBatch.Draw(_whitePixel, new Rectangle(bounds.X, bounds.Y, borderThickness, bounds.Height), borderColor);
            // Right border
            spriteBatch.Draw(_whitePixel, new Rectangle(bounds.Right - borderThickness, bounds.Y, borderThickness, bounds.Height), borderColor);
        }

        private void DrawPreviewContent(SpriteBatch spriteBatch, Rectangle bounds)
        {
            // Calculate content area with DTXManiaNX offsets
            Rectangle contentBounds;
            if (_hasStatusPanel)
            {
                // With status panel: X+8, Y+8 offset
                contentBounds = new Rectangle(
                    bounds.X + CONTENT_OFFSET_WITH_STATUS,
                    bounds.Y + CONTENT_OFFSET_WITH_STATUS,
                    bounds.Width - (CONTENT_OFFSET_WITH_STATUS * 2),
                    bounds.Height - (CONTENT_OFFSET_WITH_STATUS * 2)
                );
            }
            else
            {
                // Without status panel: X+37, Y+24 offset
                contentBounds = new Rectangle(
                    bounds.X + CONTENT_OFFSET_WITHOUT_STATUS,
                    bounds.Y + CONTENT_OFFSET_Y_WITHOUT_STATUS,
                    bounds.Width - (CONTENT_OFFSET_WITHOUT_STATUS * 2),
                    bounds.Height - (CONTENT_OFFSET_Y_WITHOUT_STATUS * 2)
                );
            }

            // Only show preview image if delay has passed and we should display it
            ITexture textureToUse = null;
            if (ShouldDisplayPreview())
            {
                textureToUse = _currentPreviewTexture ?? _defaultPreviewTexture;
            }

            if (textureToUse != null && !textureToUse.IsDisposed)
            {
                try
                {
                    // Maintain aspect ratio and center the image
                    var sourceSize = new Vector2(textureToUse.Width, textureToUse.Height);
                    var targetSize = new Vector2(contentBounds.Width, contentBounds.Height);

                    var scale = Math.Min(targetSize.X / sourceSize.X, targetSize.Y / sourceSize.Y);
                    var scaledSize = sourceSize * scale;

                    var centeredPosition = new Vector2(
                        contentBounds.X + (contentBounds.Width - scaledSize.X) / 2,
                        contentBounds.Y + (contentBounds.Height - scaledSize.Y) / 2
                    );

                    var destRect = new Rectangle(
                        (int)centeredPosition.X,
                        (int)centeredPosition.Y,
                        (int)scaledSize.X,
                        (int)scaledSize.Y
                    );

                    textureToUse.Draw(spriteBatch, destRect, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0f);
                }
                catch (ObjectDisposedException)
                {
                    // Texture was disposed during draw, clear the reference
                    _currentPreviewTexture = null;
                }
                catch (Exception)
                {
                    // Fallback to placeholder on any other error
                    DrawPlaceholder(spriteBatch, contentBounds);
                }
            }
            else
            {
                // Draw placeholder when no image is available or delay hasn't passed
                DrawPlaceholder(spriteBatch, contentBounds);
            }
        }

        private void DrawPlaceholder(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_whitePixel == null)
                return;

            // Draw placeholder background
            var placeholderColor = Color.DarkGray * 0.5f;
            spriteBatch.Draw(_whitePixel, bounds, placeholderColor);

            // Draw placeholder icon (simple cross pattern)
            var centerX = bounds.X + bounds.Width / 2;
            var centerY = bounds.Y + bounds.Height / 2;
            var iconSize = Math.Min(bounds.Width, bounds.Height) / 4;

            var iconColor = Color.Gray;
            var iconThickness = 4;

            // Horizontal line
            spriteBatch.Draw(_whitePixel,
                new Rectangle(centerX - iconSize / 2, centerY - iconThickness / 2, iconSize, iconThickness),
                iconColor);

            // Vertical line
            spriteBatch.Draw(_whitePixel,
                new Rectangle(centerX - iconThickness / 2, centerY - iconSize / 2, iconThickness, iconSize),
                iconColor);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _currentPreviewTexture = null;
                _defaultPreviewTexture = null;
            }

            base.Dispose(disposing);
        }

        #endregion
    }
}
