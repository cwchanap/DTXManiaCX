using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.UI.Layout;

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

            var notesList = activeNotes.ToList(); // Convert to list to avoid multiple enumeration

            // First pass: Draw all base animations
            foreach (var note in notesList)
            {
                DrawNote(spriteBatch, note, currentSongTimeMs);
            }

            // Note: Overlays will be drawn in the effects pass for proper layering

            // Draw lane flash effects on top of everything
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
                // Map note channel to sprite column (use channel-based mapping for shared lanes)
                var spriteColumn = GetSpriteColumnForChannel(note.Channel);
                
                
                if (spriteColumn >= 0)
                {
                    // Calculate sprite position centered on the lane
                    var spriteWidth = GetSpriteWidthForColumn(spriteColumn);
                    var centeredX = noteRect.X + (DefaultNoteSize.X - spriteWidth) / 2;
                    var spritePosition = new Vector2(centeredX, noteRect.Y);
                    
                    // Get custom source rectangle for variable-width sprites
                    var sourceRect = GetCustomSpriteSourceRectangleForColumn(spriteColumn);
                    
                    // Check if this is a snare/tom note that uses overlay frames
                    // Use both channel-based AND lane-based detection for maximum compatibility
                    if (IsSnareOrTomChannel(note.Channel) || IsSnareOrTomLane(note.LaneIndex))
                    {
                        // For snare/toms: Draw overlay animation ONLY (frames 0, 1, 10) at correct note depth
                        var overlayFrameIndex = (int)(_animationTimeMs / AnimationFrameDurationMs) % OverlayAnimationRows.Length;
                        var overlayRow = OverlayAnimationRows[overlayFrameIndex];
                        var overlaySourceRect = new Rectangle(sourceRect.X, overlayRow * DrumChipsSpriteHeight, sourceRect.Width, sourceRect.Height);
                        spriteBatch.Draw(_drumChipsTexture.Texture, spritePosition, overlaySourceRect, Color.White, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.70f);
                    }
                    else
                    {
                        // For all other notes: Draw BOTH base animation (2-9) AND overlay animation (0,1,10)
                        // Use correct depth layer for notes: 0.7f (from PerformanceStage depth structure)
                        // Draw base animation at note depth
                        var baseAnimationFrame = (int)(_animationTimeMs / AnimationFrameDurationMs) % BaseAnimationFrameCount;
                        var baseRow = BaseAnimationStartRow + baseAnimationFrame;
                        var baseSourceRect = new Rectangle(sourceRect.X, baseRow * DrumChipsSpriteHeight, sourceRect.Width, sourceRect.Height);
                        spriteBatch.Draw(_drumChipsTexture.Texture, spritePosition, baseSourceRect, Color.White, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.70f);
                        
                        
                        // TODO: Add overlay rendering for hit effects when needed
                        // This would be called separately for hit feedback effects
                    }
                }
                else
                {
                    // Invalid sprite column, fallback to colored rectangle
                    if (_whiteTexture != null)
                        spriteBatch.Draw(_whiteTexture, noteRect, null, noteColor, 0f, Vector2.Zero, SpriteEffects.None, 0.70f);
                }
            }
            else 
            {
                // Fallback to colored rectangles
                if (_whiteTexture != null)
                    spriteBatch.Draw(_whiteTexture, noteRect, null, noteColor, 0f, Vector2.Zero, SpriteEffects.None, 0.70f);
            }

            // Optional: Draw note value for debugging (can be removed in final version)
            #if DEBUG
            // This would require a font to be loaded, so we'll skip it for now
            // DrawNoteDebugInfo(spriteBatch, note, noteRect);
            #endif
        }

        /// <summary>
        /// Draws overlay animations for all active notes in effects pass (called from PerformanceStage)
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing (additive blend mode)</param>
        /// <param name="activeNotes">Notes currently visible on screen</param>
        /// <param name="currentSongTimeMs">Current song time in milliseconds</param>
        public void DrawNoteOverlays(SpriteBatch spriteBatch, IEnumerable<Note> activeNotes, double currentSongTimeMs)
        {
            if (!IsReady || spriteBatch == null || activeNotes == null)
                return;

            foreach (var note in activeNotes)
            {
                DrawNoteOverlay(spriteBatch, note, currentSongTimeMs);
            }
        }

        /// <summary>
        /// Renders overlay animation for a single note (effects pass)
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="note">Note to render overlay for</param>
        /// <param name="currentSongTimeMs">Current song time in milliseconds</param>
        private void DrawNoteOverlay(SpriteBatch spriteBatch, Note note, double currentSongTimeMs)
        {
            if (!IsReady || spriteBatch == null || note == null)
                return;

            // Only draw overlay for non-snare/tom notes (snare/tom use overlay as their main animation)
            if (IsSnareOrTomChannel(note.Channel) || IsSnareOrTomLane(note.LaneIndex))
                return;

            // Validate lane index
            if (note.LaneIndex < 0 || note.LaneIndex >= PerformanceUILayout.LaneCount)
                return;

            // Calculate Y position: y = JudgementY - (scrollPxPerMs * (noteTime – songTime))
            var timeDifference = note.TimeMs - currentSongTimeMs;
            var yOffset = _scrollPixelsPerMs * timeDifference;
            var noteY = JudgementY - yOffset;

            // Skip notes that have passed the drop grace period or are too far above screen
            if (noteY > JudgementY + DropGracePeriod || noteY < -DefaultNoteSize.Y)
                return;

            // Only draw overlay if we have drum chips texture
            if (_drumChipsTexture == null)
                return;

            // Map note channel to sprite column
            var spriteColumn = GetSpriteColumnForChannel(note.Channel);
            if (spriteColumn < 0)
                return;

            // Calculate sprite position centered on the lane
            var lanePosition = _lanePositions[note.LaneIndex];
            var spriteWidth = GetSpriteWidthForColumn(spriteColumn);
            var noteRect = new Rectangle((int)lanePosition.X, (int)noteY, (int)DefaultNoteSize.X, (int)DefaultNoteSize.Y);
            var centeredX = noteRect.X + (DefaultNoteSize.X - spriteWidth) / 2;
            var spritePosition = new Vector2(centeredX, noteRect.Y);

            // Get custom source rectangle for variable-width sprites
            var sourceRect = GetCustomSpriteSourceRectangleForColumn(spriteColumn);

            // Draw overlay animation with full opacity (zero transparency) and depth sorting
            var overlayFrameIndex = (int)(_animationTimeMs / AnimationFrameDurationMs) % OverlayAnimationRows.Length;
            var overlayRow = OverlayAnimationRows[overlayFrameIndex];
            var overlaySourceRect = new Rectangle(sourceRect.X, overlayRow * DrumChipsSpriteHeight, sourceRect.Width, sourceRect.Height);
            spriteBatch.Draw(_drumChipsTexture.Texture, spritePosition, overlaySourceRect, Color.White, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.5f);
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
                    
                    // Draw the flash effect with layer depth 0.69f (above background, below notes)
                    spriteBatch.Draw(_whiteTexture, laneRect, null, flashColor, 0f, Vector2.Zero, SpriteEffects.None, 0.69f);
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
                if (_drumChipsTexture?.Texture?.IsDisposed == true)
                {
                    _drumChipsTexture = null;
                }
                
                if (_drumChipsTexture != null)
                {
                    _drumChipsTexture.RemoveReference();
                    _drumChipsTexture = null;
                }

                // Load the base texture
                var baseTexture = _resourceManager.LoadTexture(TexturePath.DrumChips);
                if (baseTexture?.Texture != null)
                {
                    try
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
                        
                        // Release the original reference after creating ManagedSpriteTexture
                        baseTexture.RemoveReference();
                        baseTexture = null;
                            
                    }
                    catch (Exception ex)
                    {
                        // Clean up on error
                        if (baseTexture != null)
                        {
                            baseTexture.RemoveReference();
                            baseTexture = null;
                        }
                        _drumChipsTexture = null;
                        throw;
                    }
                }
                else
                {
                    _drumChipsTexture = null;
                }
            }
            catch (Exception ex)
            {
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

                // Rethrow the exception to ensure the error is not silently ignored.
                // This prevents the Note-renderer from being instantiated in an invalid state
                // where it would fail silently during the render loop.
                throw new InvalidOperationException("Failed to create essential rendering resources for NoteRenderer.", ex);
            }
        }

        /// <summary>
        /// Maps DTX channel directly to sprite sheet column
        /// This ensures the correct sprite is shown based on the note's channel,
        /// especially important for lanes that have multiple channels (like lane 1 and 8)
        /// 
        /// noteOrder: ['13', '19', '12', '14', '15', '17', '16', '11', '1C', '1A', '18', '1B']
        /// Column positions:
        ///   0='13' BassDrum,      1='19' RightCymbal,     2='12' Snare,          3='14' HighTom,
        ///   4='15' LowTom,        5='17' FloorTom,        6='16' RideCymbal,     7='11' HiHatClose,
        ///   8='1C' LeftCrash,     9='1A' LeftCrash,       10='18' HiHatOpen,     11='1B' LeftPedal
        /// </summary>
        /// <param name="channel">DTX channel (hex value)</param>
        /// <returns>Sprite column index or -1 if invalid</returns>
        private int GetSpriteColumnForChannel(int channel)
        {
            return channel switch
            {
                0x13 => 0,  // '13' BassDrum -> Column 0
                0x19 => 1,  // '19' RightCymbal -> Column 1
                0x12 => 2,  // '12' Snare -> Column 2
                0x14 => 3,  // '14' HighTom -> Column 3
                0x15 => 4,  // '15' LowTom -> Column 4
                0x17 => 5,  // '17' FloorTom -> Column 5
                0x16 => 6,  // '16' RideCymbal -> Column 6
                0x11 => 7,  // '11' HiHatClose -> Column 7
                0x1C => 8,  // '1C' LeftCrash -> Column 8
                0x1A => 9,  // '1A' LeftCrash -> Column 9
                0x18 => 10, // '18' HiHatOpen -> Column 10
                0x1B => 11, // '1B' LeftPedal -> Column 11
                _ => -1     // Invalid channel
            };
        }

        /// <summary>
        /// Legacy method - Maps DTX lane index to sprite sheet column
        /// Kept for compatibility with overlay effects
        /// Updated for CORRECT lane order: LC, HH, LP, SN, HT, DB, LT, FT, CY
        /// </summary>
        /// <param name="laneIndex">DTX lane index (0-8)</param>
        /// <returns>Sprite column index or -1 if invalid</returns>
        private int GetSpriteColumnForLane(int laneIndex)
        {
            // Use fallback mapping based on primary channel for each lane
            return laneIndex switch
            {
                0 => 9,  // Lane 0: 0x1A (Left Crash) -> Column 9
                1 => 10, // Lane 1: 0x18 (Hi-Hat Open) -> Column 10 (primary for lane 1)
                2 => 11, // Lane 2: 0x1B (Left Pedal) -> Column 11
                3 => 2,  // Lane 3: 0x12 (Snare) -> Column 2 (now in correct position)
                4 => 3,  // Lane 4: 0x14 (High Tom) -> Column 3
                5 => 0,  // Lane 5: 0x13 (Bass Drum) -> Column 0 (now in correct position)
                6 => 4,  // Lane 6: 0x15 (Low Tom) -> Column 4 (now in correct position)
                7 => 5,  // Lane 7: 0x17 (Floor Tom) -> Column 5
                8 => 1,  // Lane 8: 0x19 (Right Cymbal) -> Column 1 (primary for lane 8)
                9 => 6,  // Lane 9: RD (Ride) -> Column 6
                _ => -1  // Invalid lane (lanes 0-9 supported)
            };
        }

        /// <summary>
        /// Gets the sprite width for a specific column based on noteOrder mapping
        /// Column widths based on actual noteOrder: ['13', '19', '12', '14', '15', '17', '16', '11', '1C', '1A', '18', '1B']
        /// Your provided widths: LC:74, HH:48, CY:74, RD:58, SN:64, HT:56, LT:56, FT:56, BD:70, LP:58
        /// </summary>
        /// <param name="columnIndex">Sprite sheet column index (0-11)</param>
        /// <returns>Sprite width in pixels</returns>
        private int GetSpriteWidthForColumn(int columnIndex)
        {
            return columnIndex switch
            {
                0 => 70,  // Column 0: '13' BassDrum - BD width: 70
                1 => 58,  // Column 1: '19' RideCymbal - RD width: 58  
                2 => 64,  // Column 2: '12' Snare - SN width: 64
                3 => 56,  // Column 3: '14' HighTom - HT width: 56
                4 => 56,  // Column 4: '15' LowTom - LT width: 56
                5 => 56,  // Column 5: '17' FloorTom - FT width: 56
                6 => 74,  // Column 6: '16' RightCrash - CY width: 74
                7 => 48,  // Column 7: '11' HiHatClose - HH width: 48
                8 => 58,  // Column 8: '1C' LeftBass - LC width: 58
                9 => 74,  // Column 9: '1A' LeftCrash - LC width: 74
                10 => 48, // Column 10: '18' HiHatOpen - HH width: 48
                11 => 58, // Column 11: '1B' LeftPedal - LP width: 58
                _ => 64   // Default width
            };
        }

        /// <summary>
        /// Legacy method - Gets the sprite width for a specific lane
        /// Kept for compatibility
        /// </summary>
        /// <param name="laneIndex">DTX lane index (0-8)</param>
        /// <returns>Sprite width in pixels</returns>
        private int GetSpriteWidthForLane(int laneIndex)
        {
            var columnIndex = GetSpriteColumnForLane(laneIndex);
            return GetSpriteWidthForColumn(columnIndex);
        }

        /// <summary>
        /// Determines if a channel represents snare or tom drums that use overlay frames
        /// instead of base animation frames
        /// </summary>
        /// <param name="channel">DTX channel (hex value)</param>
        /// <returns>True if this is a snare/tom channel that uses overlay frames</returns>
        private bool IsSnareOrTomChannel(int channel)
        {
            return channel switch
            {
                0x12 => true, // Snare - uses overlay frames
                0x14 => true, // High Tom - uses overlay frames  
                0x15 => true, // Low Tom - uses overlay frames
                0x17 => true, // Floor Tom - uses overlay frames
                _ => false    // All other channels use base animation frames
            };
        }

        /// <summary>
        /// Legacy method - Determines if a lane represents snare or tom drums
        /// Kept for compatibility
        /// Updated for CORRECT lane order: LC, HH, LP, SN, HT, DB, LT, FT, CY
        /// </summary>
        /// <param name="laneIndex">DTX lane index (0-8)</param>
        /// <returns>True if this is a snare/tom lane that uses overlay frames</returns>
        private bool IsSnareOrTomLane(int laneIndex)
        {
            return laneIndex switch
            {
                3 => true,  // Lane 3: SN (Snare) - uses overlay frames (now in correct position)
                4 => true,  // Lane 4: HT (High Tom) - uses overlay frames  
                6 => true,  // Lane 6: LT (Low Tom) - uses overlay frames (now in correct position)
                7 => true,  // Lane 7: FT (Floor Tom) - uses overlay frames
                _ => false  // All other lanes use base animation frames
            };
        }

        /// <summary>
        /// Gets custom source rectangle for variable-width sprites by column
        /// Uses correct sprite sheet column ordering and widths
        /// </summary>
        /// <param name="columnIndex">Sprite sheet column index (0-11)</param>
        /// <returns>Source rectangle for the sprite</returns>
        private Rectangle GetCustomSpriteSourceRectangleForColumn(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= 12) 
                return new Rectangle(0, 0, 64, DrumChipsSpriteHeight); // Default

            // Calculate X position dynamically based on accumulated widths from GetSpriteWidthForColumn
            // This ensures single source of truth for sprite widths
            int xPosition = 0;
            for (int i = 0; i < columnIndex; i++)
            {
                xPosition += GetSpriteWidthForColumn(i);
            }
            
            var spriteWidth = GetSpriteWidthForColumn(columnIndex);

            return new Rectangle(
                xPosition, 
                0, // Y will be set by animation frame
                spriteWidth, 
                DrumChipsSpriteHeight
            );
        }

        /// <summary>
        /// Legacy method - Gets custom source rectangle for variable-width sprites by lane
        /// Kept for compatibility
        /// </summary>
        /// <param name="laneIndex">DTX lane index (0-8)</param>
        /// <returns>Source rectangle for the sprite</returns>
        private Rectangle GetCustomSpriteSourceRectangle(int laneIndex)
        {
            var columnIndex = GetSpriteColumnForLane(laneIndex);
            return GetCustomSpriteSourceRectangleForColumn(columnIndex);
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
