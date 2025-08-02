using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.Song.Components;
using DTX.Stage.Performance;
using DTX.Resources;

namespace DTXMania.Test.Helpers
{
    /// <summary>
    /// Mock performance stage for stress testing without full game initialization
    /// Simulates note scrolling, hit detection, and effect spawning for performance validation
    /// </summary>
    public class MockPerformanceStage : IDisposable
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly MockResourceManager _resourceManager;
        private ParsedChart _loadedChart;
        private PooledEffectsManager _effectsManager;
        private SpriteBatch _spriteBatch;
        
        // Gameplay simulation state
        private double _currentSongTime;
        private readonly List<Note> _upcomingNotes;
        private readonly List<Note> _activeNotes;
        private readonly Random _hitSimulator;
        
        // Performance tracking
        private int _totalNotesProcessed;
        private int _effectsSpawned;

        public MockPerformanceStage(GraphicsDevice graphicsDevice, MockResourceManager resourceManager)
        {
            _graphicsDevice = graphicsDevice;
            _resourceManager = resourceManager;
            _spriteBatch = new SpriteBatch(graphicsDevice);
            _upcomingNotes = new List<Note>();
            _activeNotes = new List<Note>();
            _hitSimulator = new Random(123); // Fixed seed for consistent testing
            _currentSongTime = 0.0;
        }

        /// <summary>
        /// Loads a chart for performance testing
        /// </summary>
        public void LoadChart(ParsedChart chart)
        {
            _loadedChart = chart ?? throw new ArgumentNullException(nameof(chart));
            
            // Prepare notes for processing
            _upcomingNotes.Clear();
            _upcomingNotes.AddRange(chart.Notes.OrderBy(n => n.Tick));
            _activeNotes.Clear();
            
            _currentSongTime = 0.0;
            _totalNotesProcessed = 0;
            _effectsSpawned = 0;
        }

        /// <summary>
        /// Sets the effects manager for testing
        /// </summary>
        public void SetEffectsManager(PooledEffectsManager effectsManager)
        {
            _effectsManager = effectsManager;
        }

        /// <summary>
        /// Gets the current effects manager
        /// </summary>
        public PooledEffectsManager GetEffectsManager()
        {
            return _effectsManager;
        }

        /// <summary>
        /// Updates the mock performance stage, simulating gameplay
        /// </summary>
        public void Update(GameTime gameTime)
        {
            var deltaTime = gameTime.ElapsedGameTime.TotalSeconds;
            _currentSongTime += deltaTime;

            // Process upcoming notes that should become active
            ProcessUpcomingNotes();

            // Simulate note hits and generate effects
            SimulateNoteHits(deltaTime);

            // Update effects manager
            _effectsManager?.Update(deltaTime);

            // Remove expired notes
            RemoveExpiredNotes();
        }

        /// <summary>
        /// Draws the mock performance stage
        /// </summary>
        public void Draw(GameTime gameTime)
        {
            // Simulate drawing operations without actual rendering
            // This maintains the same call patterns as the real performance stage
            
            _spriteBatch.Begin();
            
            // Simulate drawing background, lanes, notes, etc.
            SimulateBackgroundDrawing();
            SimulateNoteDrawing();
            
            _spriteBatch.End();

            // Draw effects with additive blending (simulated)
            _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive);
            _effectsManager?.Draw(_spriteBatch);
            _spriteBatch.End();

            // Resume normal drawing for UI elements (simulated)
            _spriteBatch.Begin();
            SimulateUIDrawing();
            _spriteBatch.End();
        }

        /// <summary>
        /// Processes notes that should become active based on current song time
        /// </summary>
        private void ProcessUpcomingNotes()
        {
            if (_loadedChart == null) return;

            var currentTick = ConvertTimeToTick(_currentSongTime);
            var lookaheadTick = currentTick + 200; // Look ahead ~1 second worth of notes

            for (int i = _upcomingNotes.Count - 1; i >= 0; i--)
            {
                var note = _upcomingNotes[i];
                if (note.Tick <= lookaheadTick)
                {
                    _activeNotes.Add(note);
                    _upcomingNotes.RemoveAt(i);
                    _totalNotesProcessed++;
                }
                else
                {
                    break; // Notes are ordered, so we can stop here
                }
            }
        }

        /// <summary>
        /// Simulates note hits and spawns effects accordingly
        /// </summary>
        private void SimulateNoteHits(double deltaTime)
        {
            var currentTick = ConvertTimeToTick(_currentSongTime);

            foreach (var note in _activeNotes.ToList())
            {
                // Check if note is in hit window
                var noteTick = note.Tick;
                var tickDifference = Math.Abs(currentTick - noteTick);
                
                if (tickDifference <= 50) // Hit window
                {
                    // Simulate hit with high probability (90% hit rate)
                    if (_hitSimulator.NextDouble() < 0.9)
                    {
                        // Spawn hit effect
                        _effectsManager?.SpawnHitEffect(note.LaneIndex);
                        _effectsSpawned++;
                    }
                    
                    _activeNotes.Remove(note);
                }
            }
        }

        /// <summary>
        /// Removes notes that have passed the hit window
        /// </summary>
        private void RemoveExpiredNotes()
        {
            var currentTick = ConvertTimeToTick(_currentSongTime);
            
            _activeNotes.RemoveAll(note => (currentTick - note.Tick) > 100); // Miss window
        }

        /// <summary>
        /// Converts time in seconds to DTX ticks
        /// </summary>
        private long ConvertTimeToTick(double timeSeconds)
        {
            // Simplified conversion assuming constant BPM
            var bpm = _loadedChart?.Bpm ?? 120.0;
            var beatsPerSecond = bpm / 60.0;
            var ticksPerSecond = beatsPerSecond * 192; // 192 ticks per beat
            return (long)(timeSeconds * ticksPerSecond);
        }

        /// <summary>
        /// Simulates background drawing operations
        /// </summary>
        private void SimulateBackgroundDrawing()
        {
            // Simulate expensive drawing operations by doing some calculations
            var dummy = 0.0;
            for (int i = 0; i < 100; i++)
            {
                dummy += Math.Sin(i * 0.1) * Math.Cos(i * 0.2);
            }
        }

        /// <summary>
        /// Simulates note drawing operations
        /// </summary>
        private void SimulateNoteDrawing()
        {
            // Simulate drawing each active note
            foreach (var note in _activeNotes)
            {
                // Simulate texture draw operations
                var dummy = note.LaneIndex * note.Tick * 0.001;
            }
        }

        /// <summary>
        /// Simulates UI element drawing
        /// </summary>
        private void SimulateUIDrawing()
        {
            // Simulate drawing score, combo, gauge, etc.
            var dummy = _totalNotesProcessed * _effectsSpawned * 0.001;
        }

        /// <summary>
        /// Gets performance statistics
        /// </summary>
        public (int TotalNotesProcessed, int EffectsSpawned, int ActiveNotes, int UpcomingNotes) GetPerformanceStats()
        {
            return (_totalNotesProcessed, _effectsSpawned, _activeNotes.Count, _upcomingNotes.Count);
        }

        public void Dispose()
        {
            _spriteBatch?.Dispose();
            _effectsManager?.Dispose();
        }
    }
}
