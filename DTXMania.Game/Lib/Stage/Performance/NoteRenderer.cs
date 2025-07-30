using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.Resources;
using DTX.Song.Components;

namespace DTX.Stage.Performance
{
    /// <summary>
    /// Renders scrolling notes for the 9-lane GITADORA XG layout
    /// Handles note positioning, scrolling, and visual effects
    /// </summary>
    public class NoteRenderer : IDisposable
    {
        #region Constants

        /// <summary>
        /// Default note size (32x16 pixels as specified)
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
        private bool _disposed = false;

        // Cached lane positions for performance
        private readonly Vector2[] _lanePositions;
        private readonly Color[] _laneColors;

        // Scroll speed calculation
        private double _scrollPixelsPerMs;

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

            // Cache lane positions and colors for performance
            _lanePositions = new Vector2[PerformanceUILayout.LaneCount];
            _laneColors = new Color[PerformanceUILayout.LaneCount];

            for (int i = 0; i < PerformanceUILayout.LaneCount; i++)
            {
                var laneX = PerformanceUILayout.GetLaneX(i);
                _lanePositions[i] = new Vector2(laneX - DefaultNoteSize.X / 2, 0); // Center note in lane
                _laneColors[i] = PerformanceUILayout.GetLaneColor(i);
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
        /// Sets the BPM (must be called before SetScrollSpeed for proper timing)
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

            // Draw the note (only if white texture was created successfully)
            if (_whiteTexture != null)
            {
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
            // No update logic needed for basic note rendering
            // Future: Add note animations, effects, etc.
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Creates a white texture for drawing colored rectangles
        /// </summary>
        private void CreateWhiteTexture()
        {
            try
            {
                _whiteTexture = new Texture2D(_graphicsDevice, 1, 1);
                _whiteTexture.SetData(new[] { Color.White });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NoteRenderer: Failed to create white texture: {ex.Message}");
                _whiteTexture = null;
            }
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
                _disposed = true;
            }
        }

        #endregion
    }
}
