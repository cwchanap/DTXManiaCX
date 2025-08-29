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
        private int _debugFrameCount = 0;

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
            
            System.Console.WriteLine($"[PadRenderer] PadRenderer initialized successfully");
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
            {
                System.Console.WriteLine($"[PadRenderer] Invalid lane index: {laneIndex}");
                return;
            }

            System.Console.WriteLine($"[PadRenderer] Triggering pad press for lane {laneIndex} (judged: {isJudgedHit})");
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
            if (spriteBatch == null)
            {
                System.Console.WriteLine("[PadRenderer] SpriteBatch is null!");
                return;
            }

            // Minimal debug output
            _debugFrameCount++;
            if (_debugFrameCount % 120 == 0) // Every 2 seconds instead of 1
            {
                int pressedCount = _padVisuals.Count(p => p.State == PadState.Pressed);
                System.Console.WriteLine($"[PadRenderer] Drawing {_padVisuals.Length} pads, {pressedCount} pressed");
            }

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
                    "../System/Graphics/7_pads.png",                // Primary: existing pad texture in System directory
                    "System/Graphics/7_pads.png",                   // Alternative System path
                    "Graphics/7_pads.png",                          // Standard graphics path
                    "Graphics/ScreenPlayDrums/7_PadCaps.png",      // DTXManiaNX path
                    TexturePath.PadCaps,                           // TexturePath constant
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
                            System.Console.WriteLine($"[PadRenderer] Successfully loaded pad texture from: {path}");
                            break;
                        }
                    }
                    catch
                    {
                        // Continue trying other paths
                        System.Console.WriteLine($"[PadRenderer] Failed to load pad texture from: {path}");
                    }
                }

                if (_padSpriteSheet != null)
                {
                    System.Console.WriteLine($"[PadRenderer] Successfully loaded pad sprite sheet");
                    CalculateCellDimensions();
                }
                else
                {
                    System.Console.WriteLine($"[PadRenderer] No pad sprite sheet found - will use fallback rendering");
                }
            }
            catch (Exception ex)
            {
                // Failed to load pad texture, pads won't be rendered
                _padSpriteSheet = null;
            }
        }

        /// <summary>
        /// Calculates sprite sheet cell dimensions using resilient sizing rules
        /// </summary>
        private void CalculateCellDimensions()
        {
            if (_padSpriteSheet == null)
                return;

            var texW = _padSpriteSheet.Width;
            var texH = _padSpriteSheet.Height;

            // Cell height: texH / 3 (3 rows: cymbals, toms, pedals)
            _cellHeight = texH / SpriteSheetRows;

            // For the 7_pads.png texture, it's a 4×3 grid
            // Try common column counts (4 for the existing texture, then fallbacks)
            int[] tryColumnCounts = { 4, 8, 7, PerformanceUILayout.LaneCount };
            
            bool foundValidColumnCount = false;
            foreach (var cols in tryColumnCounts)
            {
                if (texW % cols == 0)
                {
                    _spriteColumns = cols;
                    _cellWidth = texW / cols;
                    foundValidColumnCount = true;
                    System.Console.WriteLine($"[PadRenderer] Detected {cols} columns, cell size: {_cellWidth}x{_cellHeight}");
                    break;
                }
            }

            // Fallback: use detected columns from texture
            if (!foundValidColumnCount)
            {
                _spriteColumns = 4; // Default to 4 columns for existing texture
                _cellWidth = texW / _spriteColumns;
                System.Console.WriteLine($"[PadRenderer] Fallback: {_spriteColumns} columns, cell size: {_cellWidth}x{_cellHeight}");
            }
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
            
            // Debug coordinates only for first frame
            if (_debugFrameCount == 1)
            {
                System.Console.WriteLine($"[PadRenderer] Lane {laneIndex}: pos=({laneLeftX},{PadsY}) size=({padWidth}x{PadsHeight})");
            }

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
            
            // Draw with point sampling and alpha blending at highest priority
            _padSpriteSheet.Draw(
                spriteBatch,
                destRect,
                sourceRect,
                tint,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                0.0f  // Highest priority - on top of everything
            );
            return true;
        }

        /// <summary>
        /// Draws fallback colored rectangle when sprite sheet is unavailable
        /// </summary>
        private void DrawFallbackPad(SpriteBatch spriteBatch, PadVisual pad, Rectangle destRect, int laneIndex = -1)
        {
            // Create a simple colored rectangle for debugging
            // We need access to a white pixel texture - let's use the GraphicsDevice to create one
            if (_fallbackTexture == null)
            {
                _fallbackTexture = new Texture2D(_graphicsDevice, 1, 1);
                _fallbackTexture.SetData(new[] { Color.White });
                System.Console.WriteLine("[PadRenderer] Created fallback texture");
            }

            // Draw actual pad rectangles using the SAME method that works for the test rectangle
            Color padColor = pad.State == PadState.Pressed 
                ? Color.Red      // Full bright red when pressed  
                : Color.Yellow;  // Full bright yellow when idle

            // Draw actual pad at the lane position - use the working method
            var padRect = new Rectangle(
                destRect.X, 
                destRect.Y, 
                destRect.Width, 
                destRect.Height
            );
            
            spriteBatch.Draw(_fallbackTexture, padRect, null, padColor, 0f, Vector2.Zero, SpriteEffects.None, 0.0f);
            
            // Debug actual pad drawing and auto-trigger some presses for demo
            if (_debugFrameCount % 30 == 0 && destRect.X < 400)
            {
                System.Console.WriteLine($"[PadRenderer] Drawing actual pad at ({padRect.X},{padRect.Y}) size {padRect.Width}x{padRect.Height}, color {padColor}");
            }

            // DEMO: Auto-trigger pad presses to show they work
            if (_debugFrameCount % 180 == 0 && laneIndex < 3)
            {
                TriggerPadPress(laneIndex, true);
                System.Console.WriteLine($"[PadRenderer] DEMO: Auto-triggered press for lane {laneIndex}");
            }
        }

        /// <summary>
        /// Maps a lane index to sprite sheet position (column, row)
        /// Based on 4×3 pad texture layout: 4 columns, 3 rows
        /// Row 0: Cymbals (LC, HH, RC, Crash)
        /// Row 1: Toms (HT, LT, FT, Snare) 
        /// Row 2: Pedals (LP, Bass)
        /// </summary>
        /// <param name="laneIndex">Lane index (0-based)</param>
        /// <returns>Tuple of (columnIndex, rowIndex), or (-1, -1) if no mapping</returns>
        private (int columnIndex, int rowIndex) GetSpritePositionForLane(int laneIndex)
        {
            // DTXManiaCX 10-lane mapping: LC, HH, LP, SD, HT, DB, LT, FT, CY, RD
            // Map to 4×3 sprite sheet based on drum type
            return laneIndex switch
            {
                0 => (0, 0), // LC (Left Crash) -> Cymbal row, column 0
                1 => (1, 0), // HH (Hi-Hat) -> Cymbal row, column 1
                2 => (0, 2), // LP (Left Pedal) -> Pedal row, column 0
                3 => (3, 1), // SD (Snare) -> Tom row, column 3 (snare position)
                4 => (0, 1), // HT (High Tom) -> Tom row, column 0
                5 => (1, 2), // DB (Bass Drum) -> Pedal row, column 1
                6 => (1, 1), // LT (Low Tom) -> Tom row, column 1
                7 => (2, 1), // FT (Floor Tom) -> Tom row, column 2
                8 => (2, 0), // CY (Right Cymbal) -> Cymbal row, column 2
                9 => (3, 0), // RD (Ride) -> Cymbal row, column 3
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