using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Components;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Input;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Song transition stage that displays song information before transitioning to performance
    /// Shows song name, artist, difficulty level, and preview image
    /// </summary>
    public class SongTransitionStage : BaseStage, IStageTelemetryProvider
    {
        #region Fields

        private SpriteBatch _spriteBatch;
        private IResourceManager _resourceManager;
        private InputManager _inputManager;
        
        // Song information
        private SongListNode _selectedSong;
        private int _selectedDifficulty;
        private int _songId;
        
        // UI Components
        private UIManager _uiManager;
        private UIPanel _mainPanel;
        
        // Font rendering
        private IFont _titleFont;
        private IFont _artistFont;
        // Latin-only display faces (theme-driven); null when the theme names none.
        private IFont _titleDisplayFont;
        private IFont _artistDisplayFont;
        
        // Text content
        private string _songTitle;
        private string _artistName;
        private string _difficultyName;
        
        // Preview image with rotation support
        private ITexture _previewTexture;
        
        // Difficulty sprite
        private ManagedSpriteTexture _difficultySprite;
        
        private IFont _levelNumberFont;
        
        // Background and styling
        private Texture2D _whitePixel;
        private ITexture _backgroundTexture;
        
        // Sound
        private ISound _nowLoadingSound;
        
        // Timing
        private double _elapsedTime;

        // Chart data
        private ParsedChart _parsedChart = null!;

        // Note: Using global stage transition debouncing from BaseGame

        public override StageType Type => StageType.SongTransition;

        #endregion

        #region Constructor

        public SongTransitionStage(IStageGame game) : base(game)
        {
            _inputManager = CreateConfiguredInputManager();
        }

        #endregion

        #region Stage Lifecycle

        public override void Activate(Dictionary<string, object> sharedData = null)
        {
            base.Activate(sharedData);
            
            // Get song information from shared data
            if (sharedData != null)
            {
                if (sharedData.TryGetValue("selectedSong", out var songObj) && songObj is SongListNode song)
                {
                    _selectedSong = song;
                }
                
                if (sharedData.TryGetValue("selectedDifficulty", out var difficultyObj) && difficultyObj is int difficulty)
                {
                    _selectedDifficulty = difficulty;
                }
                
                if (sharedData.TryGetValue("songId", out var songIdObj) && songIdObj is int songId)
                {
                    _songId = songId;
                }
            }
            
            // Initialize graphics resources
            _spriteBatch = new SpriteBatch(_game.GraphicsDevice);
            _resourceManager = _game.ResourceManager;
            
            // Ensure InputManager is available (recreate if disposed)
            if (_inputManager == null)
            {
                _inputManager = CreateConfiguredInputManager();
            }
            
            // Create white pixel for drawing
            _whitePixel = new Texture2D(_game.GraphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });
            
            // Load background
            LoadBackground();
            
            // Load sound
            LoadSound();
            
            // Initialize UI
            InitializeUI();
            
            // Play the now loading sound
            PlayNowLoadingSound();
            
            // Reset timing and start in normal phase (no fade)
            _elapsedTime = 0;
            _currentPhase = StagePhase.Normal;
            
        }

        public override void Deactivate()
        {
            
            // Clean up UI
            if (_uiManager != null)
            {
                _uiManager?.Dispose();
                _uiManager = null;
            }
            
            // Clean up input manager
            if (_inputManager != null)
            {
                _inputManager?.Dispose();
                _inputManager = null;
            }
            
            // Clean up graphics resources
            if (_whitePixel != null)
            {
                _whitePixel?.Dispose();
                _whitePixel = null;
            }
            if (_spriteBatch != null)
            {
                _spriteBatch?.Dispose();
                _spriteBatch = null;
            }
            
            // Clean up background texture (using reference counting)
            if (_backgroundTexture != null)
            {
                _backgroundTexture?.RemoveReference();
                _backgroundTexture = null;
            }
            
            // Clean up preview texture (using reference counting)
            if (_previewTexture != null)
            {
                _previewTexture?.RemoveReference();
                _previewTexture = null;
            }
            
            // Clean up difficulty sprite (using reference counting)
            if (_difficultySprite != null)
            {
                _difficultySprite?.RemoveReference();
                _difficultySprite = null;
            }
            
            // Clean up level number font
            _levelNumberFont?.RemoveReference();
            _levelNumberFont = null;
            
            // Clean up fonts
            if (_titleFont != null)
            {
                _titleFont?.RemoveReference();
                _titleFont = null;
            }
            if (_artistFont != null)
            {
                _artistFont?.RemoveReference();
                _artistFont = null;
            }
            _titleDisplayFont?.RemoveReference();
            _titleDisplayFont = null;
            _artistDisplayFont?.RemoveReference();
            _artistDisplayFont = null;
            
            // Clean up sounds
            if (_nowLoadingSound != null)
            {
                _nowLoadingSound?.RemoveReference();
                _nowLoadingSound = null;
            }

            // base.Deactivate() sets _currentPhase = Inactive after running
            // CleanupStageBackground(). Setting it here first would short-circuit
            // the base guard and skip background cleanup, leaving a disposed
            // texture reference after a skin switch.
            base.Deactivate();
        }

        #endregion


        #region Initialization

        private void LoadBackground()
        {
            try
            {
                // Clean up existing background texture first
                if (_backgroundTexture != null)
                {
                    _backgroundTexture.RemoveReference();
                    _backgroundTexture = null;
                }
                
                // Try to load a background texture using layout constants
                _backgroundTexture = _resourceManager.LoadTexture(SongTransitionUILayout.Background.DefaultBackgroundPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to load background texture: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void LoadSound()
        {
            try
            {
                // Clean up existing sound first
                if (_nowLoadingSound != null)
                {
                    _nowLoadingSound.RemoveReference();
                    _nowLoadingSound = null;
                }
                
                // Load now loading sound for song selection
                _nowLoadingSound = _resourceManager.LoadSound(SoundPath.NowLoading);
            }
            catch (Exception)
            {
                try
                {
                    // Fallback to decide sound if Now loading.ogg doesn't work
                    _nowLoadingSound = _resourceManager.LoadSound(SoundPath.Decide);
                }
                catch (Exception)
                {
                    _nowLoadingSound = null;
                }
            }
        }

        private void PlayNowLoadingSound()
        {
            try
            {
                if (_nowLoadingSound == null)
                    return;
                
                _nowLoadingSound.Play(0.9f); // Play at 90% volume
            }
            catch (Exception)
            {
                // Continue without sound
            }
        }


        private void InitializeUI()
        {
            _uiManager = new UIManager();
            
            // Create main panel sized to the fixed virtual resolution. InitializeUI runs during
            // OnActivate (not during draw), so GraphicsDevice.Viewport is the back buffer (window
            // size), NOT the 1280x720 render target.
            _mainPanel = new UIPanel
            {
                Position = Vector2.Zero,
                Size = new Vector2(GameConstants.Display.VirtualWidth, GameConstants.Display.VirtualHeight),
                BackgroundColor = Color.DarkBlue * 0.8f, // More visible background
                LayoutMode = PanelLayoutMode.Manual
            };
            
            // Get song information from SongManager if available
            _songTitle = _selectedSong?.DisplayTitle ?? "Unknown Song";
            _artistName = _selectedSong?.DatabaseSong?.Artist ?? "Unknown Artist";
            _difficultyName = GetDifficultyName(_selectedDifficulty);
            
            // Load fonts for text rendering
            LoadFonts();
            
            // Load preview image if available
            LoadPreviewImage();
            
            // Load difficulty sprite
            LoadDifficultySprite();
            
            // Load level number font
            LoadLevelNumberFont();
            
            // Add panel to UI manager
            _uiManager.AddRootContainer(_mainPanel);
            
            // Activate the main panel
            _mainPanel.Activate();
        }

        private void LoadFonts()
        {
            
            try
            {
                // Clean up existing title font first
                if (_titleFont != null)
                {
                    _titleFont.RemoveReference();
                    _titleFont = null;
                }
                
                // Load title font using layout configuration
                _titleFont = _resourceManager.LoadFont("NotoSerifJP", SongTransitionUILayout.SongTitle.FontSize);
            }
            catch (Exception ex)
            {
                // Title font load failed, continue without it
                System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to load title font: {ex.GetType().Name}: {ex.Message}");
            }
            
            try
            {
                // Clean up existing artist font first
                if (_artistFont != null)
                {
                    _artistFont.RemoveReference();
                    _artistFont = null;
                }

                // Load artist font using layout configuration
                _artistFont = _resourceManager.LoadFont("NotoSerifJP", SongTransitionUILayout.Artist.FontSize);
            }
            catch (Exception ex)
            {
                // Artist font load failed, continue without it
                System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to load artist font: {ex.GetType().Name}: {ex.Message}");
            }

            LoadDisplayFonts();
        }

        /// <summary>
        /// Loads the theme's Latin display faces for the title/artist. The serif
        /// fonts above stay loaded as the CJK fallback, so a failure here (or an
        /// unthemed skin) simply keeps NX rendering.
        /// </summary>
        private void LoadDisplayFonts()
        {
            _titleDisplayFont?.RemoveReference();
            _titleDisplayFont = null;
            _artistDisplayFont?.RemoveReference();
            _artistDisplayFont = null;

            var theme = _resourceManager?.CurrentTheme ?? SkinTheme.Empty;

            try
            {
                var titleFamily = ResolveTitleFontFamily(theme);
                if (titleFamily.Length > 0)
                    _titleDisplayFont = _resourceManager.LoadFont(titleFamily, ResolveTitleFontSize(theme));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to load title display font: {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                var artistFamily = ResolveArtistFontFamily(theme);
                if (artistFamily.Length > 0)
                    _artistDisplayFont = _resourceManager.LoadFont(artistFamily, ResolveArtistFontSize(theme));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to load artist display font: {ex.GetType().Name}: {ex.Message}");
            }
        }
        
        private void LoadPreviewImage()
        {
            
            try
            {
                // Clean up existing preview texture first
                if (_previewTexture != null)
                {
                    _previewTexture.RemoveReference();
                    _previewTexture = null;
                }
                
                // Try to load preview image from song data - use PreviewImage field for images, not PreviewFile (which is for audio)
                if (_selectedSong?.DatabaseChart?.PreviewImage != null)
                {
                    
                    // Get the chart directory and combine with relative preview image path
                    string chartPath = _selectedSong.DatabaseChart.FilePath;
                    string chartDirectory = System.IO.Path.GetDirectoryName(chartPath);
                    string previewImagePath = System.IO.Path.Combine(chartDirectory, _selectedSong.DatabaseChart.PreviewImage);
                    
                    // Convert to absolute path to avoid ResourceManager's skin-based path resolution
                    string absolutePreviewImagePath = System.IO.Path.GetFullPath(previewImagePath);
                    
                    if (System.IO.File.Exists(absolutePreviewImagePath))
                    {
                        _previewTexture = _resourceManager.LoadTexture(absolutePreviewImagePath);
                        if (_previewTexture != null)
                        {
                            return; // Successfully loaded, exit early
                        }
                    }
                }
                
                // Try fallback preview image if primary not found
                if (_previewTexture == null && _selectedSong != null)
                {
                    
                    // Look for common preview file names in song directory
                    var songDir = _selectedSong.DirectoryPath ?? 
                                 (_selectedSong.DatabaseChart?.FilePath != null ? 
                                  System.IO.Path.GetDirectoryName(_selectedSong.DatabaseChart.FilePath) : null);
                    if (!string.IsNullOrEmpty(songDir))
                    {
                        var fallbackFiles = new[] { "preview.jpg", "preview.png", "jacket.jpg", "jacket.png" };
                        foreach (var fallbackFile in fallbackFiles)
                        {
                            var fallbackPath = System.IO.Path.Combine(songDir, fallbackFile);
                            if (System.IO.File.Exists(fallbackPath))
                            {
                                _previewTexture = _resourceManager.LoadTexture(fallbackPath);
                                if (_previewTexture != null)
                                {
                                    return; // Successfully loaded, exit early
                                }
                            }
                        }
                    }
                }
                
                // Use default preview image as final fallback
                if (_previewTexture == null)
                {
                    try
                    {
                        _previewTexture = _resourceManager.LoadTexture(TexturePath.ResultDefaultPreview);
                    }
                    catch (Exception)
                    {
                        // Default preview load failed, continue without preview
                    }
                }
            }
            catch (Exception)
            {
                // Preview image loading failed, continue without preview
            }
        }

        private void LoadDifficultySprite()
        {
            try
            {
                // Clean up existing difficulty sprite first
                if (_difficultySprite != null)
                {
                    _difficultySprite.RemoveReference();
                    _difficultySprite = null;
                }
                
                // Load the base texture through ResourceManager first
                var baseTexture = _resourceManager.LoadTexture(TexturePath.DifficultySprite);
                if (baseTexture != null && baseTexture.Texture != null)
                {
                    // Create ManagedSpriteTexture from the loaded texture
                    _difficultySprite = new ManagedSpriteTexture(
                        _game.GraphicsDevice,
                        baseTexture.Texture,
                        TexturePath.DifficultySprite,
                        SongTransitionUILayout.DifficultySprite.SpriteWidth,
                        SongTransitionUILayout.DifficultySprite.SpriteHeight
                    );
                    
                }
            }
            catch (Exception)
            {
                // Continue without difficulty sprite
            }
        }

        private void LoadLevelNumberFont()
        {
            var oldFont = _levelNumberFont;
            _levelNumberFont = null;
            try
            {
                var theme = _resourceManager?.CurrentTheme ?? SkinTheme.Empty;
                _levelNumberFont = _resourceManager.LoadFont(
                    ResolveLevelFontFamily(theme), ResolveLevelFontSize(theme));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to load level number font: {ex.GetType().Name}: {ex.Message}");
                // Restore old font on failure
                _levelNumberFont = oldFont;
                oldFont = null; // prevent double-release in finally
            }
            finally
            {
                oldFont?.RemoveReference();
            }
        }
        

        private string GetDifficultyName(int difficulty)
        {
            return difficulty switch
            {
                0 => "Basic",
                1 => "Advanced",
                2 => "Extreme",
                3 => "Master",
                4 => "Ultimate",
                _ => "Unknown"
            };
        }
        
        private float GetCurrentDifficultyLevel()
        {
            // Guard against null song - can happen if shared data was missing
            if (_selectedSong == null)
                return 0;
            
            // Get the chart for the current difficulty level
            var chart = _selectedSong.GetCurrentDifficultyChart(_selectedDifficulty);
            if (chart == null)
                return 0;
            
            // Get the appropriate level based on the current instrument
            // For now, assuming drums mode - this could be expanded later
            return chart.DrumLevel > 0 ? chart.DrumLevel : 
                   chart.GuitarLevel > 0 ? chart.GuitarLevel : 
                   chart.BassLevel > 0 ? chart.BassLevel : 0;
        }
        
        

        #endregion

        #region Update and Draw

        private InputManager CreateConfiguredInputManager()
        {
            if (_game.ConfigManager is ConfigManager concreteConfig)
            {
                return concreteConfig.CreateConfiguredInputManager();
            }

            return new InputManager();
        }

        protected override void OnUpdate(double deltaTime)
        {
            _elapsedTime += deltaTime;
            
            // Update input manager
            if (_inputManager != null)
            {
                _inputManager.Update(deltaTime);
            }
            
            // Handle input
            HandleInput();
            
            // Update UI
            _uiManager?.Update(deltaTime);
            
            // Auto transition after delay (only if user hasn't pressed anything and debounce allows)
            if (_elapsedTime >= SongTransitionUILayout.Timing.AutoTransitionDelay && _currentPhase == StagePhase.Normal)
            {
                if (_game.CanPerformStageTransition())
                {
                    _game.MarkStageTransition();
                    TransitionToPerformance();
                }
            }
            
            // Update phase
            UpdatePhase();
        }

        [ExcludeFromCodeCoverage]
        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;

            _spriteBatch.Begin();
            
            // Draw background
            DrawBackground();
            
            // Draw UI without fade effects (simpler approach)
            _uiManager?.Draw(_spriteBatch, deltaTime);
            
            // Draw text with ManagedFont
            DrawText();
            
            // Draw difficulty background rectangle
            DrawDifficultyBackground();
            
            // Draw difficulty sprite
            DrawDifficultySprite();
            
            // Draw difficulty level number
            DrawDifficultyLevelNumber();
            
            // Draw preview image with rotation separately
            DrawPreviewImage();
            
            _spriteBatch.End();
        }

        private void DrawBackground()
        {
            var viewport = _game.GraphicsDevice.Viewport;
            
            if (_backgroundTexture != null)
            {
                _backgroundTexture.Draw(_spriteBatch, Vector2.Zero);
            }
            else if (_whitePixel != null)
            {
                // Fallback to a simple gradient background using layout constants
                var topColor = SongTransitionUILayout.Background.GradientTopColor;
                var bottomColor = SongTransitionUILayout.Background.GradientBottomColor;
                
                // Simple vertical gradient using fewer draws for better performance
                for (int y = 0; y < viewport.Height; y += SongTransitionUILayout.Background.GradientLineSpacing)
                {
                    float ratio = (float)y / viewport.Height;
                    var color = Color.Lerp(topColor, bottomColor, ratio);
                    var lineRect = new Rectangle(0, y, viewport.Width, SongTransitionUILayout.Background.GradientLineSpacing);
                    _spriteBatch.Draw(_whitePixel, lineRect, color);
                }
            }
            else
            {
                // Last resort - just clear with a color
                _game.GraphicsDevice.Clear(Color.DarkBlue);
            }
        }

        [ExcludeFromCodeCoverage]
        private void DrawText()
        {
            try
            {
                // Draw song title using layout configuration. The theme's display
                // face is Latin-only, so CJK titles keep the serif font.
                var titleFont = _titleDisplayFont != null && IsAsciiDisplayable(_songTitle ?? string.Empty)
                    ? _titleDisplayFont
                    : _titleFont;
                if (titleFont != null && !string.IsNullOrEmpty(_songTitle))
                {
                    var titleColor = SongTransitionUILayout.SongTitle.TextColor;
                    // The preview jacket draws after (over) the title; the themed
                    // cap keeps long titles from running underneath it. Instead of
                    // truncating, the full title shrinks and/or wraps to fit.
                    var maxWidth = ResolveTitleMaxWidth(
                        _resourceManager?.CurrentTheme ?? DTXMania.Game.Lib.Resources.SkinTheme.Empty);
                    var (lines, scale) = ComputeTitleLayout(
                        text => titleFont.MeasureString(text).X, _songTitle, maxWidth);

                    var lineHeight = titleFont.LineSpacing * scale;
                    var position = SongTransitionUILayout.SongTitle.Position;
                    // Anchor multi-row titles around the single-row position so the
                    // block stays visually centered between the header and artist.
                    position.Y -= (lines.Length - 1) * lineHeight / 2f;
                    foreach (var line in lines)
                    {
                        DrawScaledTextWithShadow(titleFont, line, position, titleColor,
                            scale, new Vector2(2, 2));
                        position.Y += lineHeight;
                    }
                }

                // Draw artist name using layout configuration
                var artistFont = _artistDisplayFont != null && IsAsciiDisplayable(_artistName ?? string.Empty)
                    ? _artistDisplayFont
                    : _artistFont;
                if (artistFont != null && !string.IsNullOrEmpty(_artistName))
                {
                    var artistPosition = SongTransitionUILayout.Artist.Position;
                    var artistColor = SongTransitionUILayout.Artist.TextColor;

                    // Draw with shadow for better visibility
                    artistFont.DrawStringWithShadow(_spriteBatch, _artistName, artistPosition,
                        artistColor, Color.Black * 0.8f, new Vector2(1, 1));
                }

                // Note: Difficulty is now drawn as sprite in DrawDifficultySprite method
            }
            catch (Exception)
            {
                // Text drawing failed, continue without text
            }
        }

        [ExcludeFromCodeCoverage]
        private void DrawScaledTextWithShadow(IFont font, string text, Vector2 position,
            Color color, float scale, Vector2 shadowOffset)
        {
            if (scale >= 1f)
            {
                font.DrawStringWithShadow(_spriteBatch, text, position, color,
                    Color.Black * 0.8f, shadowOffset);
                return;
            }

            var scaleVector = new Vector2(scale, scale);
            font.DrawString(_spriteBatch, text, position + shadowOffset, Color.Black * 0.8f,
                rotation: 0f, origin: Vector2.Zero, scale: scaleVector,
                effects: SpriteEffects.None, layerDepth: 0f);
            font.DrawString(_spriteBatch, text, position, color,
                rotation: 0f, origin: Vector2.Zero, scale: scaleVector,
                effects: SpriteEffects.None, layerDepth: 0f);
        }
        
        [ExcludeFromCodeCoverage]
        /// <summary>
        /// Difficulty plate backdrop color: "Transition.DifficultyPanel" → NX
        /// opaque gray. Skins with dark panel styling (CX Neon) substitute a
        /// translucent panel tone so the plate matches their other panels.
        /// </summary>
        internal static Color ResolveDifficultyPanelColor(DTXMania.Game.Lib.Resources.ISkinTheme theme) =>
            theme.GetColor("Transition.DifficultyPanel", Color.Gray);

        /// <summary>
        /// Max pixel width for the song title (0 = unlimited, NX). The rotated
        /// jacket art starts at x~620 and draws over the title, so skins cap
        /// the title to keep long names from running underneath it.
        /// </summary>
        internal static int ResolveTitleMaxWidth(DTXMania.Game.Lib.Resources.ISkinTheme theme) =>
            theme.GetInt("Transition.TitleMaxWidth", 0);

        /// <summary>
        /// Optional display font family for the title/artist (e.g. "Orbitron").
        /// Only applied to Latin-only text — the display faces carry no CJK
        /// glyphs, so non-ASCII strings keep the serif font. Empty = NX serif.
        /// </summary>
        internal static string ResolveTitleFontFamily(DTXMania.Game.Lib.Resources.ISkinTheme theme) =>
            theme.GetString("Transition.TitleFontFamily", string.Empty);

        internal static int ResolveTitleFontSize(DTXMania.Game.Lib.Resources.ISkinTheme theme) =>
            theme.GetInt("Transition.TitleFontSize", SongTransitionUILayout.SongTitle.FontSize);

        internal static string ResolveArtistFontFamily(DTXMania.Game.Lib.Resources.ISkinTheme theme) =>
            theme.GetString("Transition.ArtistFontFamily", string.Empty);

        internal static int ResolveArtistFontSize(DTXMania.Game.Lib.Resources.ISkinTheme theme) =>
            theme.GetInt("Transition.ArtistFontSize", SongTransitionUILayout.Artist.FontSize);

        /// <summary>
        /// Font for the numeric level beside the difficulty plate (always ASCII
        /// digits): "Transition.LevelFontFamily"/"Transition.LevelFontSize" →
        /// NX NotoSerifJP 24. An empty themed value is treated as the default
        /// so a malformed `Transition.LevelFontFamily=` line cannot leave the
        /// level number undrawn (LoadFont rejects empty paths).
        /// </summary>
        internal static string ResolveLevelFontFamily(DTXMania.Game.Lib.Resources.ISkinTheme theme)
        {
            var family = theme.GetString("Transition.LevelFontFamily", "NotoSerifJP");
            return string.IsNullOrWhiteSpace(family) ? "NotoSerifJP" : family;
        }

        internal static int ResolveLevelFontSize(DTXMania.Game.Lib.Resources.ISkinTheme theme) =>
            theme.GetInt("Transition.LevelFontSize", 24);

        /// <summary>
        /// True when every character is printable ASCII, i.e. the text can be
        /// rendered by a Latin-only display SpriteFont without glyph fallback.
        /// </summary>
        internal static bool IsAsciiDisplayable(string text)
            => DTXMania.Game.Lib.UI.DisplayText.IsAsciiDisplayable(text);

        // Single-line shrink is preferred until the scale would drop below this;
        // beyond that the title wraps to extra rows instead of getting tiny.
        private const float TitleMinSingleLineScale = 0.75f;
        // Hard floor for the shared scale once wrapping is in play.
        private const float TitleMinScale = 0.6f;
        private const int TitleMaxLines = 2;

        /// <summary>
        /// Lays out the full song title inside <paramref name="maxWidth"/> without
        /// ever dropping characters: a fitting title stays one full-size line, a
        /// slightly-wide title shrinks in place, and anything wider wraps to up
        /// to two rows (shrinking the shared scale when even that is not enough).
        /// </summary>
        internal static (string[] lines, float scale) ComputeTitleLayout(
            Func<string, float> measure, string title, int maxWidth)
        {
            if (string.IsNullOrEmpty(title) || maxWidth <= 0)
                return (new[] { title ?? string.Empty }, 1f);

            float width = measure(title);
            if (width <= maxWidth)
                return (new[] { title }, 1f);

            float singleLineScale = maxWidth / width;
            if (singleLineScale >= TitleMinSingleLineScale)
                return (new[] { title }, singleLineScale);

            var lines = WrapToWidth(measure, title, maxWidth);
            if (lines.Length <= TitleMaxLines)
                return (lines, 1f);

            // Too many rows at full scale: shrink the shared scale (widening the
            // effective per-row budget) until the wrap fits the row cap.
            for (float scale = 0.95f; scale >= TitleMinScale - 0.001f; scale -= 0.05f)
            {
                lines = WrapToWidth(measure, title, maxWidth / scale);
                if (lines.Length <= TitleMaxLines)
                    return (lines, scale);
            }

            // Pathological title: keep every character at the floor scale even if
            // that needs more rows than the cap.
            return (WrapToWidth(measure, title, maxWidth / TitleMinScale), TitleMinScale);
        }

        /// <summary>
        /// Greedy word wrap; words wider than the limit (and spaceless CJK text)
        /// fall back to per-character breaking. Never drops characters: the
        /// original whitespace runs between words on a kept line are preserved
        /// exactly (repeated spaces are not collapsed), and the separator at a
        /// line break is consumed by the break itself.
        /// </summary>
        internal static string[] WrapToWidth(Func<string, float> measure, string text, float limit)
        {
            var lines = new List<string>();
            var current = new System.Text.StringBuilder();
            // Whitespace run saved from the previous word; appended before the
            // next word when it fits on the current line, dropped when the line
            // breaks (the break replaces the separator).
            var pendingSep = string.Empty;

            int i = 0;
            while (i < text.Length)
            {
                // Read the next non-space run (word).
                int wordStart = i;
                while (i < text.Length && text[i] != ' ') i++;
                var word = text.Substring(wordStart, i - wordStart);

                // Read the following whitespace run (separator), preserved so
                // repeated spaces between words are not collapsed.
                int sepStart = i;
                while (i < text.Length && text[i] == ' ') i++;
                var sep = text.Substring(sepStart, i - sepStart);

                // On a fresh line, leading whitespace is invisible and would
                // only force an immediate wrap, so drop it.
                var addition = current.Length == 0 ? word : pendingSep + word;
                if (measure(current.ToString() + addition) <= limit)
                {
                    current.Append(addition);
                    pendingSep = sep;
                    continue;
                }

                // Doesn't fit. Flush the current line.
                if (current.Length > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }

                // Start the new line with this word (dropping the leading
                // separator, which the line break replaces).
                if (measure(word) <= limit)
                {
                    current.Append(word);
                    pendingSep = sep;
                    continue;
                }

                // Word alone exceeds the limit — break it by characters.
                foreach (var ch in word)
                {
                    if (current.Length > 0 && measure(current.ToString() + ch) > limit)
                    {
                        lines.Add(current.ToString());
                        current.Clear();
                    }
                    current.Append(ch);
                }
                pendingSep = sep;
            }

            if (current.Length > 0)
                lines.Add(current.ToString());

            return lines.Count == 0 ? new[] { string.Empty } : lines.ToArray();
        }

        private void DrawDifficultyBackground()
        {
            if (_whitePixel == null)
                return;

            try
            {
                // Backdrop rectangle behind the tier plate + level number.
                var position = SongTransitionUILayout.DifficultySprite.Position;
                var size = SongTransitionUILayout.DifficultySprite.BackgroundSize;
                var rectangle = new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);
                var panelColor = ResolveDifficultyPanelColor(
                    _resourceManager?.CurrentTheme ?? DTXMania.Game.Lib.Resources.SkinTheme.Empty);

                _spriteBatch.Draw(_whitePixel, rectangle, panelColor);
            }
            catch (Exception)
            {
                // Continue without difficulty background
            }
        }
        
        [ExcludeFromCodeCoverage]
        private void DrawDifficultySprite()
        {
            if (_difficultySprite == null)
                return;
            
            try
            {
                // Get the sprite index for the current difficulty
                var spriteIndex = SongTransitionUILayout.DifficultySprite.GetSpriteIndex(_selectedDifficulty);
                var position = SongTransitionUILayout.DifficultySprite.Position;
                
                // Draw the difficulty sprite at the specified position
                _difficultySprite.DrawSprite(_spriteBatch, spriteIndex, position);
            }
            catch (Exception)
            {
                // Continue without difficulty sprite
            }
        }
        
        private void DrawDifficultyLevelNumber()
        {
            if (_levelNumberFont == null)
                return;
            
            try
            {
                // Get the difficulty level from the current song chart
                var difficultyLevel = GetCurrentDifficultyLevel();
                if (difficultyLevel <= 0)
                    return;
                
                // Format the level number similar to SongStatusPanel (divide by 10 for decimal format)
                var levelText = (difficultyLevel / 10.0f).ToString("F2"); // Show 38 as "3.80", 60 as "6.00", etc.
                
                // Use layout configuration for position and color
                var position = SongTransitionUILayout.DifficultyLevelNumber.Position;
                var textColor = SongTransitionUILayout.DifficultyLevelNumber.TextColor;
                
                _levelNumberFont.DrawString(_spriteBatch, levelText, position, textColor);
            }
            catch (Exception)
            {
                // Continue without level number
            }
        }
        
        private void DrawPreviewImage()
        {
            if (_previewTexture == null)
                return;
            
            try
            {
                
                // Use SongTransitionUILayout constants for positioning and rotation
                var position = SongTransitionUILayout.PreviewImage.Position;
                var size = SongTransitionUILayout.PreviewImage.Size;
                var rotation = SongTransitionUILayout.PreviewImage.RotationRadians;
                var origin = SongTransitionUILayout.PreviewImage.Origin;
                var tintColor = SongTransitionUILayout.PreviewImage.TintColor;
                
                // Calculate scale to fit the image within the specified size
                var textureSize = new Vector2(_previewTexture.Width, _previewTexture.Height);
                var targetSize = size;
                var scale = new Vector2(
                    targetSize.X / textureSize.X,
                    targetSize.Y / textureSize.Y
                );
                
                // Use smaller scale to maintain aspect ratio (letterbox/pillarbox)
                var uniformScale = Math.Min(scale.X, scale.Y);
                var finalScale = new Vector2(uniformScale, uniformScale);
                
                // Draw the preview image with absolute positioning
                // Use the layout position directly for consistent placement regardless of image size
                var drawPosition = position;
                
                _previewTexture.Draw(_spriteBatch, drawPosition, finalScale, rotation, Vector2.Zero);
            }
            catch (Exception)
            {
                // Preview image drawing failed, continue without preview
            }
        }

        private void UpdatePhase()
        {
            switch (_currentPhase)
            {
                case StagePhase.FadeIn:
                    if (_elapsedTime >= SongTransitionUILayout.Timing.FadeInDuration)
                    {
                        _currentPhase = StagePhase.Normal;
                    }
                    break;
                    
                case StagePhase.FadeOut:
                    if (_elapsedTime >= SongTransitionUILayout.Timing.FadeOutDuration)
                    {
                        // Transition complete
                        PerformTransition();
                    }
                    break;
            }
        }

        #endregion

        #region Input Handling

        private void HandleInput()
        {
            if (_inputManager == null)
                return;
                
            var commands = _inputManager.GetInputCommands();
            while (commands.Count > 0)
            {
                var command = commands.Dequeue();
                ExecuteInputCommand(command);
            }
        }

        private void ExecuteInputCommand(InputCommand command)
        {
            switch (command.Type)
            {
                case InputCommandType.Activate:
                    if (_game.CanPerformStageTransition())
                    {
                        _game.MarkStageTransition();
                        TransitionToPerformance();
                    }
                    break;
                    
                case InputCommandType.Back:
                    if (_game.CanPerformStageTransition())
                    {
                        _game.MarkStageTransition();
                        TransitionBackToSongSelect();
                    }
                    break;
            }
        }

        #endregion

        #region Telemetry

        public void PopulateTelemetry(GameTelemetrySnapshot telemetry)
        {
            ArgumentNullException.ThrowIfNull(telemetry);

            telemetry.SelectedSongTitle = _selectedSong?.DisplayTitle ?? _selectedSong?.Title;
            telemetry.SelectedDifficulty = _selectedDifficulty;
            telemetry.ChartLoaded = _selectedSong?.GetCurrentDifficultyChart(_selectedDifficulty) != null;
        }

        #endregion

        #region Stage Transitions

        private void TransitionToPerformance()
        {
            if (_currentPhase == StagePhase.FadeOut)
                return; // Already transitioning
            
            // Transition immediately without fade
            PerformTransition();
        }

        private void TransitionBackToSongSelect()
        {
            // Go back to song selection immediately
            StageManager?.ChangeStage(StageType.SongSelect, new InstantTransition());
        }

        private void PerformTransition()
        {
            // Create shared data for performance stage
            var sharedData = new Dictionary<string, object>();
            if (_selectedSong != null)
            {
                sharedData["selectedSong"] = _selectedSong;
                sharedData["selectedDifficulty"] = _selectedDifficulty;
                sharedData["songId"] = _songId;

                // Pass parsed chart data if available
                if (_parsedChart != null)
                {
                    sharedData["parsedChart"] = _parsedChart;
                }
            }
            
            // Transition to performance stage immediately
            StageManager?.ChangeStage(StageType.Performance, new InstantTransition(), sharedData);
        }

        #endregion
    }
}
