using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Pad visual states
    /// </summary>
    public enum PadState
    {
        Idle,
        Pressed
    }

    /// <summary>
    /// Visual state data for a single pad
    /// </summary>
    public class PadVisual
    {
        public PadState State { get; set; } = PadState.Idle;
        public double TimePressed { get; set; } = 0.0;
    }

    /// <summary>
    /// Renders visual pad indicators under each lane with idle and pressed states.
    /// Supports sprite sheet format with 2 rows × N columns (idle on row 0, pressed on row 1).
    /// Flashes when player hits pads, autoplay triggers, or during lane testing.
    /// </summary>
    public class PadRenderer : IDisposable
    {
        #region Constants

        /// <summary>
        /// Baseline Y position for pads - positioned below gauge bar as requested
        /// Gauge frame is at Y=626 with fill height of 31, so pads go below at Y=670
        /// </summary>
        private const int PadsY = 670; // Below gauge bar (626 + ~44px spacing)

        /// <summary>
        /// Height of pad indicators - increased for better visibility
        /// </summary>
        private const int PadsHeight = 60;

        /// <summary>
        /// Press duration for judged hits (milliseconds)
        /// </summary>
        private const double PressDurationJudged = 90.0;

        /// <summary>
        /// Press duration for key-down without judge (milliseconds)
        /// </summary>
        private const double PressDurationKeyDown = 60.0;

        /// <summary>
        /// Expected sprite sheet rows (3 rows: cymbals, toms, pedals)
        /// </summary>
        private const int SpriteSheetRows = 3;

        /// <summary>
        /// Expected sprite sheet columns (4 columns for 7_pads.png layout)
        /// </summary>
        private const int SpriteSheetColumns = 4;

        /// <summary>
        /// Drum lane order for mapping to sprite columns: LC, HH, SD, HT, LT, FT, RC, LP
        /// </summary>
        private static readonly int[] DrumLaneOrder = { 0, 1, 3, 4, 6, 7, 8, 2 }; // Maps lanes to sprite columns

        #endregion

        #region Private Fields

        private readonly GraphicsDevice _graphicsDevice;
        private readonly IResourceManager _resourceManager;
        private readonly PadVisual[] _padVisuals;
        
        private ITexture _padSpriteSheet;
        private int _cellWidth;
        private int _cellHeight;
        private int _spriteColumns;
        private bool _disposed;
        private Texture2D _fallbackTexture;

        /// <summary>
        /// Base depth for pad rendering (configurable to ensure proper layering)
        /// </summary>
        public float BaseDepth { get; set; } = 0.1f; // Much closer to front to ensure visibility

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of PadRenderer
        /// </summary>
        /// <param name="graphicsDevice">Graphics device for rendering</param>
        /// <param name="resourceManager">Resource manager for loading textures</param>
        public PadRenderer(GraphicsDevice graphicsDevice, IResourceManager resourceManager)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
            
            // Initialize pad visuals for all lanes
            _padVisuals = new PadVisual[PerformanceUILayout.LaneCount];
            for (int i = 0; i < _padVisuals.Length; i++)
            {
                _padVisuals[i] = new PadVisual();
            }

            LoadPadTexture();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Triggers a pad press effect for the specified lane
        /// </summary>
        /// <param name="laneIndex">Lane index (0-based)</param>
        /// <param name="isJudgedHit">Whether this was a judged hit or just key input</param>
        public void TriggerPadPress(int laneIndex, bool isJudgedHit = true)
        {
            if (laneIndex < 0 || laneIndex >= _padVisuals.Length)
                return;

            _padVisuals[laneIndex].State = PadState.Pressed;
            _padVisuals[laneIndex].TimePressed = 0.0;
        }

        /// <summary>
        /// Updates pad visual states
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last update in seconds</param>
        public void Update(double deltaTime)
        {
            for (int i = 0; i < _padVisuals.Length; i++)
            {
                var pad = _padVisuals[i];
                if (pad.State == PadState.Pressed)
                {
                    pad.TimePressed += deltaTime * 1000.0; // Convert to milliseconds

                    // Check if press duration has elapsed
                    if (pad.TimePressed >= PressDurationJudged)
                    {
                        pad.State = PadState.Idle;
                        pad.TimePressed = 0.0;
                    }
                }
            }
        }

        /// <summary>
        /// Draws pad indicators for all lanes
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        public void Draw(SpriteBatch spriteBatch)
        {
            if (spriteBatch == null || _disposed)
                return;

            for (int laneIndex = 0; laneIndex < _padVisuals.Length; laneIndex++)
            {
                DrawPadForLane(spriteBatch, laneIndex);
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Loads the pad sprite sheet texture and calculates cell dimensions
        /// </summary>
        private void LoadPadTexture()
        {
            try
            {
                // Try to load pad sprite sheet texture - use existing pad textures
                string[] padTexturePaths = 
                {
                    TexturePath.PadCaps,                           // Primary: TexturePath constant
                    "Graphics/7_pads.png",                          // Standard graphics path
                    "Graphics/ScreenPlayDrums/7_PadCaps.png",      // DTXManiaNX path
                    "Graphics/pads.png",
                    "Graphics/pad_caps.png"
                };

                foreach (var path in padTexturePaths)
                {
                    try
                    {
                        _padSpriteSheet = _resourceManager.LoadTexture(path);
                        if (_padSpriteSheet != null)
                        {
                            System.Console.WriteLine($"[DEBUG] PadRenderer loaded texture from: {path}");
                            break;
                        }
                    }
                    catch
                    {
                        // Continue trying other paths
                    }
                }

                if (_padSpriteSheet != null)
                {
                    CalculateCellDimensions();
                }
            }
            catch (Exception ex)
            {
                // Failed to load pad texture, pads won't be rendered
                _padSpriteSheet = null;
            }
        }

        /// <summary>
        /// Calculates sprite sheet cell dimensions using defined constants
        /// </summary>
        private void CalculateCellDimensions()
        {
            if (_padSpriteSheet == null)
                return;

            var texW = _padSpriteSheet.Width;
            var texH = _padSpriteSheet.Height;
            
            System.Console.WriteLine($"[DEBUG] PadRenderer texture dimensions: {texW}x{texH}");

            // Use defined sprite sheet dimensions
            _spriteColumns = SpriteSheetColumns;
            _cellWidth = texW / SpriteSheetColumns;
            _cellHeight = texH / SpriteSheetRows;
            
            System.Console.WriteLine($"[DEBUG] PadRenderer cell dimensions: {_cellWidth}x{_cellHeight} (columns={_spriteColumns}, rows={SpriteSheetRows})");
        }

        /// <summary>
        /// Draws a pad indicator for a specific lane
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="laneIndex">Lane index to draw pad for</param>
        private void DrawPadForLane(SpriteBatch spriteBatch, int laneIndex)
        {
            if (laneIndex < 0 || laneIndex >= _padVisuals.Length)
                return;

            var pad = _padVisuals[laneIndex];
            
            // Calculate destination rectangle using lane position
            var laneLeftX = PerformanceUILayout.GetLaneLeftX(laneIndex);
            var laneWidth = PerformanceUILayout.GetLaneWidth(laneIndex);
            
            // Calculate pad width to maintain aspect ratio if we have sprite sheet
            int padWidth = laneWidth;
            if (_padSpriteSheet != null && _cellWidth > 0 && _cellHeight > 0)
            {
                // Maintain sprite aspect ratio
                float aspectRatio = (float)_cellWidth / _cellHeight;
                padWidth = (int)(PadsHeight * aspectRatio);
                
                // Center the pad within the lane if it's smaller than lane width
                if (padWidth < laneWidth)
                {
                    laneLeftX += (laneWidth - padWidth) / 2;
                }
                else
                {
                    padWidth = laneWidth; // Use full lane width if calculated width is too large
                }
            }
            
            var destRect = new Rectangle(
                laneLeftX,
                PadsY,
                padWidth,
                PadsHeight
            );
            
            // Add bounds checking for debugging
            bool isOnScreen = PadsY >= -50 && PadsY <= 800 && laneLeftX >= -50 && laneLeftX <= 1400;
            
            // Try sprite sheet rendering first
            if (_padSpriteSheet != null && TryDrawSpriteSheetPad(spriteBatch, pad, laneIndex, destRect))
            {
                return; // Successfully drew sprite
            }

            // Fallback: Draw colored rectangle when no sprite sheet available
            DrawFallbackPad(spriteBatch, pad, destRect, laneIndex);
        }

        /// <summary>
        /// Attempts to draw pad using sprite sheet
        /// </summary>
        private bool TryDrawSpriteSheetPad(SpriteBatch spriteBatch, PadVisual pad, int laneIndex, Rectangle destRect)
        {
            // Check if cell dimensions are valid
            if (_cellWidth <= 0 || _cellHeight <= 0)
                return false;
            
            var (columnIndex, rowIndex) = GetSpritePositionForLane(laneIndex);
            
            // Skip if we don't have a valid position mapping
            if (columnIndex < 0 || columnIndex >= _spriteColumns || rowIndex < 0 || rowIndex >= SpriteSheetRows)
                return false;

            // Calculate source rectangle based on lane type
            var sourceRect = new Rectangle(
                columnIndex * _cellWidth,
                rowIndex * _cellHeight,
                _cellWidth,
                _cellHeight
            );

            // Use color tinting to show pressed state (brighter when pressed)
            Color tint = pad.State == PadState.Pressed ? Color.White * 1.5f : Color.White;
            
            // Draw with point sampling and alpha blending at configured depth
            _padSpriteSheet.Draw(
                spriteBatch,
                destRect,
                sourceRect,
                tint,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                BaseDepth  // Use configurable depth for proper layering
            );
            return true;
        }

        /// <summary>
        /// Draws fallback colored rectangle when sprite sheet is unavailable
        /// </summary>
        private void DrawFallbackPad(SpriteBatch spriteBatch, PadVisual pad, Rectangle destRect, int laneIndex = -1)
        {
            // Create fallback texture if needed
            if (_fallbackTexture == null)
            {
                _fallbackTexture = new Texture2D(_graphicsDevice, 1, 1);
                _fallbackTexture.SetData(new[] { Color.White });
            }

            // Draw colored rectangle fallback
            Color padColor = pad.State == PadState.Pressed ? Color.Red : Color.Yellow;
            spriteBatch.Draw(_fallbackTexture, destRect, null, padColor, 0f, Vector2.Zero, SpriteEffects.None, BaseDepth);
        }

        /// <summary>
        /// Maps a lane index to sprite sheet position (column, row)
        /// Based on 4×3 pad texture layout: 4 columns, 3 rows
        /// Sprite sheet layout (left to right, top to bottom):
        ///   Row 0: LC, HH, CY, RD
        ///   Row 1: SN, HT, LT, FT
        ///   Row 2: LP, BD, (unused), (unused)
        /// </summary>
        /// <param name="laneIndex">Lane index (0-based)</param>
        /// <returns>Tuple of (columnIndex, rowIndex), or (-1, -1) if no mapping</returns>
        private (int columnIndex, int rowIndex) GetSpritePositionForLane(int laneIndex)
        {
            // DTXManiaCX 10-lane mapping: LC, HH, LP, SN, HT, DB, LT, FT, CY, RD
            // Map to 4×3 sprite sheet based on actual texture layout
            return laneIndex switch
            {
                0 => (0, 0), // LC (Left Crash) -> Row 0, Col 0
                1 => (1, 0), // HH (Hi-Hat) -> Row 0, Col 1
                2 => (0, 2), // LP (Left Pedal) -> Row 2, Col 0
                3 => (0, 1), // SN (Snare) -> Row 1, Col 0
                4 => (1, 1), // HT (High Tom) -> Row 1, Col 1
                5 => (1, 2), // DB (Bass Drum) -> Row 2, Col 1
                6 => (2, 1), // LT (Low Tom) -> Row 1, Col 2
                7 => (3, 1), // FT (Floor Tom) -> Row 1, Col 3
                8 => (2, 0), // CY (Right Cymbal) -> Row 0, Col 2
                9 => (3, 0), // RD (Ride) -> Row 0, Col 3
                _ => (-1, -1) // Invalid lane
            };
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes of resources used by the PadRenderer
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected dispose method
        /// </summary>
        /// <param name="disposing">Whether disposing from Dispose() call</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _padSpriteSheet?.RemoveReference();
                _padSpriteSheet = null;
                
                _fallbackTexture?.Dispose();
                _fallbackTexture = null;
                
                _disposed = true;
            }
        }

        #endregion
    }
}