using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Renders scrolling notes for the 9-lane GITADORA XG layout
    /// Handles note positioning, scrolling, and visual effects
    /// </summary>
    public class NoteRenderer : IDisposable
    {
        #region Constants

        /// <summary>
        /// Drum chips sprite sheet dimensions: 12 columns x 11 rows, 64px height per sprite
        /// </summary>
        private const int DrumChipsSpriteColumns = 12;
        private const int DrumChipsSpriteRows = 11;
        private const int DrumChipsSpriteHeight = 64;
        
        /// <summary>
        /// Base animation frames (rows 2-9, 8 frames total)
        /// </summary>
        private const int BaseAnimationStartRow = 2;
        private const int BaseAnimationFrameCount = 8;
        
        /// <summary>
        /// Overlay animation frames (rows 0, 1, 10 - 3 frames total)
        /// </summary>
        private static readonly int[] OverlayAnimationRows = { 0, 1, 10 };
        
        /// <summary>
        /// Default note size for fallback rendering
        /// </summary>
        private static readonly Vector2 DefaultNoteSize = new Vector2(32, 16);



        /// <summary>
        /// Grace period below judgement line before notes are dropped (20 pixels as specified)
        /// </summary>
        private const int DropGracePeriod = 20;

        #endregion

        #region Private Fields

        private readonly GraphicsDevice _graphicsDevice;
        private readonly IResourceManager _resourceManager;
        private Texture2D _whiteTexture;
        private ManagedSpriteTexture _drumChipsTexture;
        private bool _disposed = false;

        // Cached lane positions for performance
        private readonly Vector2[] _lanePositions;
        private readonly Color[] _laneColors;

        // Scroll speed calculation
        private double _scrollPixelsPerMs;
        
        // Animation timing
        private double _animationTimeMs = 0.0;
        private const double AnimationFrameDurationMs = 100.0; // 100ms per frame

        // Lane flash effects
        private readonly float[] _laneFlashAlpha;

        #endregion

        #region Properties

        /// <summary>
        /// Current BPM for scroll speed calculation
        /// </summary>
        public double Bpm { get; private set; } = 120.0;

        /// <summary>
        /// Judgement line Y position
        /// </summary>
        public int JudgementY => PerformanceUILayout.JudgementLineY;

        /// <summary>
        /// Whether the renderer is ready to draw
        /// </summary>
        public bool IsReady => !_disposed && _whiteTexture != null && _scrollPixelsPerMs > 0;

        /// <summary>
        /// Current scroll speed in pixels per millisecond
        /// </summary>
        public double ScrollPixelsPerMs => _scrollPixelsPerMs;

        /// <summary>
        /// Current effective look-ahead time in milliseconds (for GetActiveNotes consistency)
        /// </summary>
        public double EffectiveLookAheadMs { get; private set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new NoteRenderer
        /// </summary>
        /// <param name="graphicsDevice">Graphics device for rendering</param>
        /// <param name="resourceManager">Resource manager for loading textures</param>
        public NoteRenderer(GraphicsDevice graphicsDevice, IResourceManager resourceManager)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));

            // Initialize white texture for drawing rectangles
            CreateWhiteTexture();
            
            // Load drum chips sprite texture
            LoadDrumChipsTexture();

            // Cache lane positions and colors for performance
            _lanePositions = new Vector2[PerformanceUILayout.LaneCount];
            _laneColors = new Color[PerformanceUILayout.LaneCount];
            _laneFlashAlpha = new float[PerformanceUILayout.LaneCount];

            for (int i = 0; i < PerformanceUILayout.LaneCount; i++)
            {
                var laneX = PerformanceUILayout.GetLaneX(i);
                _lanePositions[i] = new Vector2(laneX - DefaultNoteSize.X / 2, 0); // Center note in lane
                _laneColors[i] = PerformanceUILayout.GetLaneColor(i);
                _laneFlashAlpha[i] = 0.0f; // Initialize flash alpha to 0
            }

            // Initialize scroll speed with default value (will be set properly later)
            _scrollPixelsPerMs = 0.0;
            EffectiveLookAheadMs = 0.0;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Sets the scroll speed based on look-ahead time and user preference
        /// </summary>
        /// <param name="scrollSpeedSetting">User scroll speed setting (100 = normal, 200 = 2x faster, 50 = 0.5x slower)</param>
        public void SetScrollSpeed(int scrollSpeedSetting = 100)
        {
            if (scrollSpeedSetting <= 0)
                throw new ArgumentException("Scroll speed must be greater than 0", nameof(scrollSpeedSetting));


            // Base look-ahead time: 1.5 seconds for faster scrolling
            var baseLookAheadMs = 1500.0;

            // User scroll speed multiplier (100 = normal, 200 = 2x faster, 50 = 0.5x slower)
            var scrollSpeedMultiplier = scrollSpeedSetting / 100.0;

            // Effective look-ahead time (faster scroll = less look-ahead time)
            var effectiveLookAheadMs = baseLookAheadMs / scrollSpeedMultiplier;
            
            // Store effective look-ahead time for consistency with GetActiveNotes
            EffectiveLookAheadMs = effectiveLookAheadMs;

            // Calculate scroll speed: distance / time
            var scrollDistance = JudgementY; // Distance from top (Y=0) to judgement line
            _scrollPixelsPerMs = scrollDistance / effectiveLookAheadMs;

        }

        /// <summary>
        /// Sets the BPM
        /// </summary>
        /// <param name="bpm">New BPM value</param>
        public void SetBpm(double bpm)
        {
            if (bpm <= 0)
                throw new ArgumentException("BPM must be greater than 0", nameof(bpm));

            Bpm = bpm;
        }



        /// <summary>
        /// Renders active notes on screen
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="activeNotes">Notes currently visible on screen</param>
        /// <param name="currentSongTimeMs">Current song time in milliseconds</param>
        public void DrawNotes(SpriteBatch spriteBatch, IEnumerable<Note> activeNotes, double currentSongTimeMs)
        {
            if (!IsReady || spriteBatch == null || activeNotes == null)
                return;

            foreach (var note in activeNotes)
            {
                DrawNote(spriteBatch, note, currentSongTimeMs);
            }

            // Draw lane flash effects on top of notes
            DrawLaneFlashEffects(spriteBatch);
        }

        /// <summary>
        /// Renders a single note
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="note">Note to render</param>
        /// <param name="currentSongTimeMs">Current song time in milliseconds</param>
        public void DrawNote(SpriteBatch spriteBatch, Note note, double currentSongTimeMs)
        {
            if (!IsReady || spriteBatch == null || note == null)
                return;

            // Validate lane index
            if (note.LaneIndex < 0 || note.LaneIndex >= PerformanceUILayout.LaneCount)
                return;

            // Calculate Y position: y = JudgementY - (scrollPxPerMs * (noteTime – songTime))
            var timeDifference = note.TimeMs - currentSongTimeMs;
            var yOffset = _scrollPixelsPerMs * timeDifference;
            var noteY = JudgementY - yOffset;


            // Skip notes that have passed the drop grace period
            if (noteY > JudgementY + DropGracePeriod)
                return;

            // Skip notes that are too far above the screen
            if (noteY < -DefaultNoteSize.Y)
                return;

            // Get lane position and color
            var lanePosition = _lanePositions[note.LaneIndex];
            var noteColor = _laneColors[note.LaneIndex];

            // Create note rectangle
            var noteRect = new Rectangle(
                (int)lanePosition.X,
                (int)noteY,
                (int)DefaultNoteSize.X,
                (int)DefaultNoteSize.Y
            );

            // Draw the note using drum chip sprite if available, otherwise use colored rectangle
            if (_drumChipsTexture != null)
            {
                // Map lane index to sprite column (iconFrameIndex)
                var spriteColumn = GetSpriteColumnForLane(note.LaneIndex);
                if (spriteColumn >= 0)
                {
                    // Calculate sprite position centered on the lane
                    var spriteWidth = GetSpriteWidthForLane(note.LaneIndex);
                    var centeredX = noteRect.X + (DefaultNoteSize.X - spriteWidth) / 2;
                    var spritePosition = new Vector2(centeredX, noteRect.Y);
                    
                    // Check if this is a snare/tom note that uses overlay frames
                    if (IsSnareOrTomLane(note.LaneIndex))
                    {
                        // For snare/toms: Show overlay frame instead of base animation
                        // Use first overlay frame (row 0) as the primary visible sprite
                        var overlayRow = OverlayAnimationRows[0]; // Row 0
                        _drumChipsTexture.DrawSprite(spriteBatch, overlayRow, spriteColumn, spritePosition);
                    }
                    else
                    {
                        // For other notes: Draw base animation first
                        var animationFrame = (int)(_animationTimeMs / AnimationFrameDurationMs) % BaseAnimationFrameCount;
                        var baseRow = BaseAnimationStartRow + animationFrame;
                        _drumChipsTexture.DrawSprite(spriteBatch, baseRow, spriteColumn, spritePosition);
                        
                        // TODO: Add overlay rendering for hit effects when needed
                        // This would be called separately for hit feedback effects
                    }
                }
                else
                {
                    // Invalid lane, fallback to colored rectangle
                    if (_whiteTexture != null)
                        spriteBatch.Draw(_whiteTexture, noteRect, noteColor);
                }
            }
            else 
            {
                // Debug logging for texture issues (only log occasionally to avoid spam)
                if (note.LaneIndex == 0 && currentSongTimeMs % 1000 < 50) // Log once per second for lane 0 only
                {
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] NoteRenderer: Using fallback rectangles - drum chips texture is null");
                }
                
                // Fallback to colored rectangles
                if (_whiteTexture != null)
                    spriteBatch.Draw(_whiteTexture, noteRect, noteColor);
            }

            // Optional: Draw note value for debugging (can be removed in final version)
            #if DEBUG
            // This would require a font to be loaded, so we'll skip it for now
            // DrawNoteDebugInfo(spriteBatch, note, noteRect);
            #endif
        }

        /// <summary>
        /// Filters notes that should be visible on screen
        /// </summary>
        /// <param name="notes">All notes to check</param>
        /// <param name="currentSongTimeMs">Current song time in milliseconds</param>
        /// <param name="lookAheadMs">How far ahead to look for notes (default 1500ms)</param>
        /// <returns>Notes that should be rendered</returns>
        public IEnumerable<Note> FilterVisibleNotes(IEnumerable<Note> notes, double currentSongTimeMs, double lookAheadMs = 1500.0)
        {
            if (notes == null)
                return Enumerable.Empty<Note>();

            var startTime = currentSongTimeMs - 200.0; // Small buffer for notes just past judgement
            var endTime = currentSongTimeMs + lookAheadMs;

            return notes.Where(note => note.TimeMs >= startTime && note.TimeMs <= endTime);
        }

        /// <summary>
        /// Gets the screen Y position for a note at a given time
        /// </summary>
        /// <param name="noteTimeMs">Note timing in milliseconds</param>
        /// <param name="currentSongTimeMs">Current song time in milliseconds</param>
        /// <returns>Y position on screen</returns>
        public double GetNoteScreenY(double noteTimeMs, double currentSongTimeMs)
        {
            var timeDifference = noteTimeMs - currentSongTimeMs;
            var yOffset = _scrollPixelsPerMs * timeDifference;
            return JudgementY - yOffset;
        }

        /// <summary>
        /// Checks if a note has passed the judgement line and should be dropped
        /// </summary>
        /// <param name="note">Note to check</param>
        /// <param name="currentSongTimeMs">Current song time in milliseconds</param>
        /// <returns>True if the note should be dropped</returns>
        public bool ShouldDropNote(Note note, double currentSongTimeMs)
        {
            var noteY = GetNoteScreenY(note.TimeMs, currentSongTimeMs);
            return noteY > JudgementY + DropGracePeriod;
        }

        /// <summary>
        /// Updates the renderer (call this every frame)
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last update</param>
        public void Update(double deltaTime)
        {
            // Check if resources need to be reloaded (e.g., after device lost/reset)
            CheckAndReloadResources();
            
            // Update animation timing
            _animationTimeMs += deltaTime * 1000.0; // Convert to milliseconds
            
            // Update lane flash effects - exponential decay
            const float flashDecayRate = 8.0f; // Decay rate per second
            for (int i = 0; i < _laneFlashAlpha.Length; i++)
            {
                if (_laneFlashAlpha[i] > 0.0f)
                {
                    _laneFlashAlpha[i] *= (float)Math.Exp(-flashDecayRate * deltaTime);
                    
                    // Clamp to 0 when very small to avoid floating point precision issues
                    if (_laneFlashAlpha[i] < 0.01f)
                    {
                        _laneFlashAlpha[i] = 0.0f;
                    }
                }
            }
        }

        /// <summary>
        /// Triggers a flash effect for the specified lane
        /// </summary>
        /// <param name="laneIndex">Lane index (0-8) to trigger flash for</param>
        public void TriggerLaneFlash(int laneIndex)
        {
            if (laneIndex >= 0 && laneIndex < _laneFlashAlpha.Length)
            {
                _laneFlashAlpha[laneIndex] = 1.0f;
            }
        }

        /// <summary>
        /// Draws overlay effect for a specific lane (rows 0, 1, 10)
        /// Used for hit effects or special visual feedback
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="laneIndex">Lane index (0-8)</param>
        /// <param name="overlayFrameIndex">Overlay frame index (0-2)</param>
        /// <param name="position">Position to draw the overlay</param>
        public void DrawOverlayEffect(SpriteBatch spriteBatch, int laneIndex, int overlayFrameIndex, Vector2 position)
        {
            if (!IsReady || spriteBatch == null || _drumChipsTexture == null)
                return;

            if (overlayFrameIndex < 0 || overlayFrameIndex >= OverlayAnimationRows.Length)
                return;

            var spriteColumn = GetSpriteColumnForLane(laneIndex);
            if (spriteColumn < 0)
                return;

            var spriteRow = OverlayAnimationRows[overlayFrameIndex];
            _drumChipsTexture.DrawSprite(spriteBatch, spriteRow, spriteColumn, position);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Checks if resources are invalid and reloads them if necessary
        /// This handles cases where textures become disposed after stage transitions
        /// </summary>
        private void CheckAndReloadResources()
        {
            // Check if white texture needs to be reloaded
            if (_whiteTexture == null)
            {
                CreateWhiteTexture();
            }

            // Check if drum chips texture needs to be reloaded
            if (_drumChipsTexture == null)
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] NoteRenderer: Drum chips texture is null, loading...");
                LoadDrumChipsTexture();
            }
        }

        /// <summary>
        /// Draws lane flash effects as semi-transparent white quads
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        private void DrawLaneFlashEffects(SpriteBatch spriteBatch)
        {
            if (_whiteTexture == null || spriteBatch == null)
                return;

            for (int i = 0; i < _laneFlashAlpha.Length; i++)
            {
                if (_laneFlashAlpha[i] > 0.0f)
                {
                    // Get lane rectangle (full height)
                    var laneRect = PerformanceUILayout.GetLaneRectangle(i);
                    
                    // Create semi-transparent white color based on flash alpha
                    var flashColor = Color.White * _laneFlashAlpha[i];
                    
                    // Draw the flash effect
                    spriteBatch.Draw(_whiteTexture, laneRect, flashColor);
                }
            }
        }

        /// <summary>
        /// Loads the drum chips sprite texture for note rendering
        /// </summary>
        private void LoadDrumChipsTexture()
        {
            try
            {
                // Clean up existing texture reference
                if (_drumChipsTexture != null)
                {
                    _drumChipsTexture.RemoveReference();
                    _drumChipsTexture = null;
                }

                // Load the base texture
                var baseTexture = _resourceManager.LoadTexture(TexturePath.DrumChips);
                if (baseTexture?.Texture != null)
                {
                    // Create ManagedSpriteTexture from the loaded texture
                    // Sprite sheet: 12 columns x 11 rows, variable width per column, 64px height
                    var spriteWidth = baseTexture.Texture.Width / DrumChipsSpriteColumns;
                    _drumChipsTexture = new ManagedSpriteTexture(
                        _graphicsDevice,
                        baseTexture.Texture,
                        TexturePath.DrumChips,
                        spriteWidth,
                        DrumChipsSpriteHeight
                    );
                        
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] NoteRenderer: Successfully loaded drum chips texture. Dimensions: {baseTexture.Texture.Width}x{baseTexture.Texture.Height}, SpriteWidth: {spriteWidth}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[WARNING] NoteRenderer: Failed to load drum chips texture. Base texture is null. Falling back to colored rectangles.");
                    _drumChipsTexture = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WARNING] NoteRenderer: Failed to load drum chips texture. Falling back to colored rectangles. Exception: {ex}");
                _drumChipsTexture = null;
            }
        }

        /// <summary>
        /// Creates a white texture for drawing colored rectangles
        /// </summary>
        private void CreateWhiteTexture()
        {
            try
            {
                // Clean up existing texture if it exists
                if (_whiteTexture != null)
                {
                    _whiteTexture.Dispose();
                }

                _whiteTexture = new Texture2D(_graphicsDevice, 1, 1);
                _whiteTexture.SetData(new[] { Color.White });
            }
            catch (Exception ex)
            {
                // Log the full exception details for debugging purposes.
                // Failing to create this texture is a critical error for the renderer.
                System.Diagnostics.Debug.WriteLine($"[CRITICAL] NoteRenderer: Failed to create white texture. Renderer will not be functional. Exception: {ex}");

                // Rethrow the exception to ensure the error is not silently ignored.
                // This prevents the Note-renderer from being instantiated in an invalid state
                // where it would fail silently during the render loop.
                throw new InvalidOperationException("Failed to create essential rendering resources for NoteRenderer.", ex);
            }
        }

        /// <summary>
        /// Maps DTX lane index to sprite sheet column based on CORRECT lane configuration
        /// 
        /// CORRECT Lane Config: LC->HH/HHC->LP/LB->SN->HT->BD->LT->FT->CY/BD
        /// 
        /// noteOrder columns: ['13', '19', '12', '14', '15', '17', '16', '11', '1C', '1A', '18', '1B']
        /// Column meanings:
        ///   0='13' RightBassDrum,  1='19' RideCymbal,    2='12' Snare,        3='14' HighTom,
        ///   4='15' LowTom,         5='17' FloorTom,      6='16' RightCymbal,  7='11' HiHatClose,
        ///   8='1C' LeftBassDrum,   9='1A' LeftCymbal,    10='18' HiHatOpen,   11='1B' LeftPedal
        /// </summary>
        /// <param name="laneIndex">DTX lane index (0-8)</param>
        /// <returns>Sprite column index or -1 if invalid</returns>
        private int GetSpriteColumnForLane(int laneIndex)
        {
            return laneIndex switch
            {
                0 => 9,  // Lane 0: LC (Left Crash) → Column 9 ('1A' - LeftCymbal) ✅ Working
                1 => 7,  // Lane 1: HH/HHC (Hi-Hat Close) → Column 7 ('11' - HiHatClose) 
                2 => 8,  // Lane 2: LP/LB (Left Pedal/Bass) → Column 8 ('1C' - LeftBassDrum)
                3 => 2,  // Lane 3: SD (Snare Drum) → Column 2 ('12' - Snare)
                4 => 3,  // Lane 4: BD (Bass Drum) BUT should show as HT → Column 3 ('14' - HighTom) 
                5 => 0,  // Lane 5: HT (High Tom) BUT should show as BD → Column 0 ('13' - RightBassDrum)
                6 => 4,  // Lane 6: LT (Low Tom) → Column 4 ('15' - LowTom)
                7 => 5,  // Lane 7: FT (Floor Tom) → Column 5 ('17' - FloorTom) ✅ Working
                8 => 1,  // Lane 8: CY/BD (Cymbal) → Column 1 ('19' - RideCymbal) ✅ Working
                _ => -1  // Invalid lane
            };
        }

        /// <summary>
        /// Gets the sprite width for a specific lane based on noteOrder mapping
        /// Column widths based on drum type from original JSON config
        /// </summary>
        /// <param name="laneIndex">DTX lane index (0-8)</param>
        /// <returns>Sprite width in pixels</returns>
        private int GetSpriteWidthForLane(int laneIndex)
        {
            // Map lane to column, then column to width
            var columnIndex = GetSpriteColumnForLane(laneIndex);
            return columnIndex switch
            {
                0 => 70,  // Column 0: BD ('13') - Bass Drum width
                1 => 58,  // Column 1: CY ('19') - Ride/Cymbal width  
                2 => 64,  // Column 2: SN ('12') - Snare width
                3 => 56,  // Column 3: HT ('14') - High Tom width
                4 => 56,  // Column 4: LT ('15') - Low Tom width
                5 => 56,  // Column 5: FT ('17') - Floor Tom width
                6 => 74,  // Column 6: CY ('16') - Right Cymbal width
                7 => 48,  // Column 7: HHC ('11') - Hi-Hat width
                8 => 58,  // Column 8: LB ('1C') - Left Bass width
                9 => 74,  // Column 9: LC ('1A') - Left Cymbal width
                10 => 48, // Column 10: HHO ('18') - Hi-Hat Open width
                11 => 58, // Column 11: LP ('1B') - Left Pedal width
                _ => 64   // Default width
            };
        }

        /// <summary>
        /// Determines if a lane represents snare or tom drums that use overlay frames
        /// instead of base animation frames
        /// </summary>
        /// <param name="laneIndex">DTX lane index (0-8)</param>
        /// <returns>True if this is a snare/tom lane that uses overlay frames</returns>
        private bool IsSnareOrTomLane(int laneIndex)
        {
            return laneIndex switch
            {
                3 => true,  // Lane 3: SN (Snare) - uses overlay frames
                4 => true,  // Lane 4: HT (High Tom) - uses overlay frames  
                6 => true,  // Lane 6: LT (Low Tom) - uses overlay frames
                _ => false  // All other lanes use base animation frames
            };
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the renderer and releases resources
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
                _whiteTexture?.Dispose();
                _drumChipsTexture?.RemoveReference();
                _drumChipsTexture = null;
                _disposed = true;
            }
        }

        #endregion
    }
}
