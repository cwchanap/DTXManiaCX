using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.Stage;
using DTXMania.Shared.Game;
using System;
using System.Collections.Generic;
using System.IO;

namespace DTX.Stage
{
    /// <summary>
    /// Startup stage implementation based on DTXManiaNX CStageStartup
    /// Handles initial loading and displays progress information
    /// </summary>
    public class StartupStage : IStage
    {
        #region Fields

        private readonly BaseGame _game;
        private double _elapsedTime;
        private SpriteBatch _spriteBatch;
        private Texture2D _whitePixel;
        private Texture2D _backgroundTexture;

        // DTXMania pattern: progress tracking
        private readonly List<string> _progressMessages;
        private string _currentProgressMessage = "";
        private StartupPhase _currentPhase = StartupPhase.SystemSounds;
        private bool _isFirstUpdate = true;

        // Loading simulation (since we don't have actual song loading yet)
        private readonly Dictionary<StartupPhase, (string message, double duration)> _phaseInfo;
        private double _phaseStartTime;

        #endregion

        #region Properties

        public StageType Type => StageType.Startup;

        #endregion

        #region Constructor

        public StartupStage(BaseGame game)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));

            _progressMessages = new List<string>();

            // Initialize phase information (based on DTXManiaNX phases)
            _phaseInfo = new Dictionary<StartupPhase, (string, double)>
            {
                { StartupPhase.SystemSounds, ("Loading system sounds...", 0.5) },
                { StartupPhase.SongListDB, ("Loading songlist.db...", 0.3) },
                { StartupPhase.SongsDB, ("Loading songs.db...", 0.4) },
                { StartupPhase.EnumerateSongs, ("Enumerating songs...", 0.8) },
                { StartupPhase.LoadScoreCache, ("Loading score properties from songs.db...", 0.6) },
                { StartupPhase.LoadScoreFiles, ("Loading score properties from files...", 0.7) },
                { StartupPhase.BuildSongLists, ("Building songlists...", 0.3) },
                { StartupPhase.SaveSongsDB, ("Saving songs.db...", 0.2) },
                { StartupPhase.Complete, ("Setup done.", 0.1) }
            };
        }

        #endregion

        #region IStage Implementation

        public void Activate()
        {
            System.Diagnostics.Debug.WriteLine("Activating Startup Stage");

            // Initialize graphics resources
            var graphicsDevice = _game.GraphicsDevice;
            _spriteBatch = new SpriteBatch(graphicsDevice);

            _whitePixel = new Texture2D(graphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });

            // Load background texture (DTXManiaNX uses 1_background.jpg)
            LoadBackgroundTexture();

            // Initialize state
            _elapsedTime = 0;
            _isFirstUpdate = true;
            _currentPhase = StartupPhase.SystemSounds;
            _phaseStartTime = 0;
            _progressMessages.Clear();

            // Add initial messages (DTXMania pattern)
            _progressMessages.Add("DTXMania powered by YAMAHA Silent Session Drums");
            _progressMessages.Add($"Release: DTXManiaCX v1.0.0 [MonoGame Edition]");

            System.Diagnostics.Debug.WriteLine("Startup Stage activated successfully");
        }

        public void Update(double deltaTime)
        {
            _elapsedTime += deltaTime;

            // Handle first update
            if (_isFirstUpdate)
            {
                _isFirstUpdate = false;
                _phaseStartTime = _elapsedTime;
            }

            // Update current phase
            UpdateCurrentPhase();

            // Check if all phases are complete
            if (_currentPhase == StartupPhase.Complete)
            {
                double phaseElapsed = _elapsedTime - _phaseStartTime;
                if (phaseElapsed >= _phaseInfo[_currentPhase].duration)
                {
                    // Transition to Title stage (DTXMania pattern)
                    _game.StageManager?.ChangeStage(StageType.Title);
                }
            }
        }

        public void Draw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;

            _spriteBatch.Begin(samplerState: SamplerState.LinearClamp);

            // Draw background
            DrawBackground();

            // Draw progress messages (DTXMania pattern)
            DrawProgressMessages();

            // Draw current progress
            DrawCurrentProgress();

            _spriteBatch.End();
        }

        public void Deactivate()
        {
            System.Diagnostics.Debug.WriteLine("Deactivating Startup Stage");

            // Cleanup resources
            _backgroundTexture?.Dispose();
            _whitePixel?.Dispose();
            _spriteBatch?.Dispose();

            _backgroundTexture = null;
            _whitePixel = null;
            _spriteBatch = null;
        }

        #endregion

        #region Private Methods - Resource Loading

        private void LoadBackgroundTexture()
        {
            try
            {
                // Try to load from DTXManiaNX graphics folder
                string backgroundPath = Path.Combine("DTXManiaNX", "Runtime", "System", "Graphics", "1_background.jpg");

                if (File.Exists(backgroundPath))
                {
                    using (var fileStream = File.OpenRead(backgroundPath))
                    {
                        _backgroundTexture = Texture2D.FromStream(_game.GraphicsDevice, fileStream);
                        System.Diagnostics.Debug.WriteLine($"Loaded startup background from: {backgroundPath}");
                        return;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Startup background not found at: {backgroundPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load startup background: {ex.Message}");
            }

            // Fallback: create a simple dark background
            CreateFallbackBackground();
        }

        private void CreateFallbackBackground()
        {
            var viewport = _game.GraphicsDevice.Viewport;
            var width = Math.Max(viewport.Width, 1024);
            var height = Math.Max(viewport.Height, 768);

            _backgroundTexture = new Texture2D(_game.GraphicsDevice, width, height);
            var colorData = new Color[width * height];

            // Create a simple dark background
            var backgroundColor = new Color(16, 16, 32);
            for (int i = 0; i < colorData.Length; i++)
            {
                colorData[i] = backgroundColor;
            }

            _backgroundTexture.SetData(colorData);
            System.Diagnostics.Debug.WriteLine("Created fallback startup background");
        }

        #endregion

        #region Private Methods - Update Logic

        private void UpdateCurrentPhase()
        {
            if (_currentPhase == StartupPhase.Complete)
                return;

            double phaseElapsed = _elapsedTime - _phaseStartTime;
            var currentPhaseInfo = _phaseInfo[_currentPhase];

            // Update current progress message
            _currentProgressMessage = currentPhaseInfo.message;

            // Check if current phase is complete
            if (phaseElapsed >= currentPhaseInfo.duration)
            {
                // Add completion message
                _progressMessages.Add($"{currentPhaseInfo.message} OK");

                // Move to next phase
                var nextPhase = GetNextPhase(_currentPhase);
                if (nextPhase != _currentPhase)
                {
                    _currentPhase = nextPhase;
                    _phaseStartTime = _elapsedTime;
                    System.Diagnostics.Debug.WriteLine($"Startup phase changed to: {_currentPhase}");
                }
            }
        }

        private StartupPhase GetNextPhase(StartupPhase currentPhase)
        {
            return currentPhase switch
            {
                StartupPhase.SystemSounds => StartupPhase.SongListDB,
                StartupPhase.SongListDB => StartupPhase.SongsDB,
                StartupPhase.SongsDB => StartupPhase.EnumerateSongs,
                StartupPhase.EnumerateSongs => StartupPhase.LoadScoreCache,
                StartupPhase.LoadScoreCache => StartupPhase.LoadScoreFiles,
                StartupPhase.LoadScoreFiles => StartupPhase.BuildSongLists,
                StartupPhase.BuildSongLists => StartupPhase.SaveSongsDB,
                StartupPhase.SaveSongsDB => StartupPhase.Complete,
                _ => StartupPhase.Complete
            };
        }

        #endregion

        #region Private Methods - Drawing

        private void DrawBackground()
        {
            if (_backgroundTexture != null)
            {
                var viewport = _game.GraphicsDevice.Viewport;
                _spriteBatch.Draw(_backgroundTexture,
                    new Rectangle(0, 0, viewport.Width, viewport.Height),
                    Color.White);
            }
            else if (_whitePixel != null)
            {
                // Fallback: draw solid dark background
                var viewport = _game.GraphicsDevice.Viewport;
                _spriteBatch.Draw(_whitePixel,
                    new Rectangle(0, 0, viewport.Width, viewport.Height),
                    new Color(16, 16, 32));
            }
        }

        private void DrawProgressMessages()
        {
            if (_whitePixel == null)
                return;

            // Draw progress messages (DTXMania pattern)
            int x = 10;
            int y = 10;
            const int lineHeight = 14;

            lock (_progressMessages)
            {
                foreach (string message in _progressMessages)
                {
                    // Draw text as rectangles (since we don't have fonts yet)
                    DrawTextRect(x, y, message.Length * 8, 12, Color.White);
                    y += lineHeight;
                }

                // Draw current progress message
                if (!string.IsNullOrEmpty(_currentProgressMessage))
                {
                    DrawTextRect(x, y, _currentProgressMessage.Length * 8, 12, Color.Yellow);
                }
            }
        }

        private void DrawCurrentProgress()
        {
            if (_whitePixel == null)
                return;

            // Calculate overall progress
            int totalPhases = _phaseInfo.Count;
            int currentPhaseIndex = (int)_currentPhase;
            double phaseElapsed = _elapsedTime - _phaseStartTime;
            double currentPhaseDuration = _phaseInfo[_currentPhase].duration;
            double phaseProgress = Math.Min(phaseElapsed / currentPhaseDuration, 1.0);

            double overallProgress = (currentPhaseIndex + phaseProgress) / totalPhases;

            // Draw progress bar
            const int progressBarX = 10;
            const int progressBarY = 200;
            const int progressBarWidth = 400;
            const int progressBarHeight = 20;

            // Background
            DrawTextRect(progressBarX, progressBarY, progressBarWidth, progressBarHeight, Color.DarkGray);

            // Progress
            int progressWidth = (int)(progressBarWidth * overallProgress);
            DrawTextRect(progressBarX, progressBarY, progressWidth, progressBarHeight, Color.LightGreen);

            // Progress percentage
            string progressText = $"{overallProgress * 100:F1}%";
            DrawTextRect(progressBarX + progressBarWidth + 10, progressBarY + 2, progressText.Length * 8, 16, Color.White);
        }

        private void DrawTextRect(int x, int y, int width, int height, Color color)
        {
            if (_whitePixel != null)
            {
                _spriteBatch.Draw(_whitePixel, new Rectangle(x, y, width, height), color);
            }
        }

        #endregion

        #region Enums

        private enum StartupPhase
        {
            SystemSounds = 0,
            SongListDB = 1,
            SongsDB = 2,
            EnumerateSongs = 3,
            LoadScoreCache = 4,
            LoadScoreFiles = 5,
            BuildSongLists = 6,
            SaveSongsDB = 7,
            Complete = 8
        }

        #endregion
    }
}