using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.UI;
using DTX.Song;
using DTX.Resources;
using System;
using System.Threading.Tasks;

namespace DTX.UI.Components
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
        private double _loadDelayTime = 0.5; // Configurable wait time for delayed loading

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
        /// Delay time before loading preview images (in seconds)
        /// </summary>
        public double LoadDelayTime
        {
            get => _loadDelayTime;
            set => _loadDelayTime = Math.Max(0, value);
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

            // Only load preview image if this is actually a song (not a folder or back bar)
            if (song?.Type == NodeType.Score)
            {

                // Load preview image asynchronously to avoid blocking navigation
                _ = Task.Run(() => LoadPreviewImageAsync());
            }
            else
            {
                // Clear preview immediately when not on a song
                _currentPreviewTexture = null;

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

        private void LoadDefaultPreviewTexture()
        {
            try
            {
                // Try to load default preview image
                _defaultPreviewTexture = _resourceManager?.LoadTexture("Graphics/5_default_preview.png");
            }
            catch (Exception ex)
            {
                // Only log critical errors to reduce debug noise
                if (ex is OutOfMemoryException || ex is System.IO.DirectoryNotFoundException)
                {
                    System.Diagnostics.Debug.WriteLine($"PreviewImagePanel: Failed to load default preview: {ex.Message}");
                }
            }
        }

        private void LoadPreviewImageAsync()
        {
            var currentSong = _currentSong; // Capture current song to avoid race conditions

            // Clear current preview immediately for responsive UI
            _currentPreviewTexture = null;

            // Use EF Core entities
            var chart = currentSong?.DatabaseChart;
            var previewImage = chart?.PreviewImage;
            var songFilePath = chart?.FilePath;

            if (string.IsNullOrEmpty(previewImage) || string.IsNullOrEmpty(songFilePath))
            {
                return;
            }

            try
            {
                var songDirectory = System.IO.Path.GetDirectoryName(songFilePath);
                if (songDirectory != null)
                {
                    var previewPath = System.IO.Path.Combine(songDirectory, previewImage);


                    if (System.IO.File.Exists(previewPath))
                    {
                        // Only update if this is still the current song (avoid race conditions)
                        if (_currentSong == currentSong)
                        {
                            _currentPreviewTexture = _resourceManager?.LoadTexture(previewPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Only log critical errors to reduce debug noise
                if (ex is OutOfMemoryException || ex is System.IO.DirectoryNotFoundException)
                {
                    System.Diagnostics.Debug.WriteLine($"PreviewImagePanel: Failed to load preview image for {currentSong?.Title}: {ex.Message}");
                }
            }
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

            // Only show preview image if we're on a song (not folder or back bar)
            ITexture textureToUse = null;
            if (_currentSong?.Type == NodeType.Score)
            {
                textureToUse = _currentPreviewTexture ?? _defaultPreviewTexture;

            }

            if (textureToUse != null)
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
            else
            {

                // Draw placeholder when no image is available
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
