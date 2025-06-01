# DTXMania Song Selection UI System Implementation

## 📊 Current Implementation Status

### ✅ COMPLETED (Phase 1)
- **Phase 1**: UI Components & Stage Integration - **✅ COMPLETE** with working song selection UI

### 📋 PLANNED (Phases 2-3)
- **Phase 2**: Preview System & Audio Integration
- **Phase 3**: Advanced Navigation & Polish

### 🎯 Key Achievements
- ✅ **UI component library** with DTXMania-style effects and rendering
- ✅ **Stage management system** with transitions and phase handling
- ✅ **Working song selection stage** with visible UI and keyboard navigation
- ✅ **Font integration** with proper SpriteFont support for UI components
- ✅ **Background texture rendering** for UI list visibility

### 🚀 Recent Fixes
1. **✅ Fixed UI rendering issue** - UIList now properly displays with background texture and font
2. **✅ Added font loading** - SpriteFont integration for text rendering in UI components
3. **✅ Enhanced debug output** - Better logging for troubleshooting UI issues
4. **✅ Improved error handling** - Graceful fallbacks when fonts fail to load

### 🚀 Next Steps
1. **Enhance visual styling** - Improve colors, spacing, and DTXMania-style effects
2. **Add preview sound system** with fade effects and timing control
3. **Implement smooth scrolling** with counter-based animation
4. **Add difficulty selection** with visual indicators

### 📋 Prerequisites
> **Note**: This document focuses on UI and stage integration. For song data management (file parsing, database, enumeration), see [Song Data Management System](song-data-management-system.md).

## Overview

This document provides a comprehensive implementation plan for DTXMania's Song Selection UI System based on examination of the original DTXManiaNX source code. The system handles UI components, stage management, list navigation, preview playback, and visual effects for the song selection experience.

## 📋 Analysis Summary

### Key Files Analyzed
- **CStageSongSelection.cs** - Main song selection stage with input handling and UI coordination
- **CActSelectSongList.cs** - Song list implementation with scrolling, navigation, and bar rendering
- **CSongListNode.cs** - Song/folder node structure with hierarchical organization
- **CSongManager.cs** - Song database management with caching and enumeration
- **CDTX.cs** - DTX file parsing for metadata extraction
- **CActSelectStatusPanel.cs** - Status panel showing song info, difficulty, and statistics
- **CActSelectPresound.cs** - Preview sound player with fade effects

## � UI Architecture Overview

This section focuses on the user interface components and visual systems for the song selection experience.

### Key UI Components
- **UIList**: Scrollable song list with 13-item display window and smooth scrolling
- **Stage Management**: BaseStage system with phase transitions and state management
- **Font System**: SpriteFont integration for text rendering with Japanese character support
- **Visual Effects**: DTXMania-style shadows, outlines, and selection highlighting
- **Input Handling**: Keyboard navigation with arrow keys and Enter/Escape controls

## 🎮 Navigation & Scrolling System

### Song List Display Architecture
- **13-Item Visible Window**: Shows 13 songs simultaneously with center selection
- **Smooth Scrolling**: Counter-based animation with 100-unit increments per song
- **Circular Navigation**: Wraps around at list boundaries
- **BOX Navigation**: Enter/exit folder hierarchies with breadcrumb tracking

### Scrolling Implementation
```csharp
public class CActSelectSongList
{
    private int nTargetScrollCounter;     // Target scroll position
    private int nCurrentScrollCounter;    // Current scroll position
    private int nSelectedRow = 6;         // Center row (0-12)
    
    public void tMoveToNext()
    {
        this.nTargetScrollCounter += 100; // Move down one song
    }
    
    public void tMoveToPrevious()
    {
        this.nTargetScrollCounter -= 100; // Move up one song
    }
}
```

### Performance Optimization
- **Lazy Loading**: Song metadata loaded on-demand during scrolling
- **Texture Caching**: Song title bars pre-rendered and cached
- **Preview Image Caching**: Album art loaded asynchronously
- **Scroll Throttling**: Limits scroll speed during enumeration
- **Background Threading**: Song enumeration runs on separate thread

## 🎵 Preview Sound System

### Preview Playback Architecture
```csharp
public class CActSelectPresound
{
    private CSound sound;                    // Current preview sound
    private CCounter ct再生待ちウェイト;      // Playback delay timer
    private CCounter ctBGMフェードアウト用;  // BGM fade out
    private CCounter ctBGMフェードイン用;    // BGM fade in
    
    public void t選択曲が変更された()
    {
        // Stop current preview
        this.tサウンド停止();
        
        // Start BGM fade in
        this.tBGMフェードイン開始();
        
        // Set preview delay timer
        this.ct再生待ちウェイト = new CCounter(0, 
            CDTXMania.ConfigIni.n曲が選択されてからプレビュー音が鳴るまでのウェイトms, 
            1, CDTXMania.Timer);
    }
}
```

### Audio Fade System
- **BGM Fade Out**: Background music fades when preview starts
- **BGM Fade In**: Background music returns when preview stops
- **Preview Delay**: Configurable delay before preview starts (default: 1000ms)
- **Volume Control**: Preview volume separate from BGM (default: 80%)
- **Scroll Prevention**: No preview during active scrolling

## 📊 Difficulty & Metadata Display

### Difficulty Level Management
```csharp
public class CActSelectSongList
{
    private int n現在のアンカ難易度レベル;    // Current anchor difficulty (0-4)
    
    public int n現在のアンカ難易度レベルに最も近い難易度レベルを返す(CSongListNode song)
    {
        // Find closest available difficulty to anchor level
        // Search upward first, then downward if needed
        // Ensures consistent difficulty selection across songs
    }
    
    public void t難易度レベルをひとつ進める()
    {
        // Cycle through available difficulties
        // Play difficulty-specific sound effects
        // Update status panel display
        // Notify other components of change
    }
}
```

### Status Panel Information
- **Song Metadata**: Title, artist, genre, BPM, duration
- **Difficulty Info**: Level numbers, difficulty labels, note counts
- **Performance Stats**: Best rank, high score, full combo status
- **Skill Points**: Calculated skill values per difficulty
- **Progress Tracking**: Completion percentage per instrument

## 🎯 Implementation Status for DTXManiaCX

### Phase 1: UI Components & Stage Integration ✅ COMPLETE
**Objective**: Create song selection UI components and integrate with stage system

**Current Status:**
- ✅ **Base UI System**: Complete UI component library with DTXMania-style effects
- ✅ **Stage System**: Full stage management with transitions and phase handling
- ✅ **Song Selection Stage**: Complete implementation with keyboard navigation
- ✅ **SET.def Parser**: Fixed for proper DTXMania format with Shift_JIS encoding
- ✅ **BOX Navigation**: Folder enter/exit with breadcrumb tracking
- ✅ **Difficulty Selection**: Left/right arrow navigation between difficulties
- ✅ **UI Components**: UILabel, UIButton, UIPanel, UIList, UIImage with shadow/outline effects

**Files Available:**
- `DTXMania.Shared.Game/Lib/UI/Components/UIList.cs` - Scrollable list component (ready for song integration)
- `DTXMania.Shared.Game/Lib/Stage/BaseStage.cs` - Stage base class with phase management
- `DTXMania.Shared.Game/Lib/Stage/StageManager.cs` - Stage transition system
- `DTXMania.Shared.Game/Lib/Stage/TitleStage.cs` - Example stage implementation
- `DTXMania.Shared.Game/Lib/Stage/UITestStage.cs` - UI component demonstration

**Implementation Tasks:**
1. **Song Selection Stage**: Create stage class integrating SongManager with UI components
2. **Song List Display**: Enhance UIList for 13-item song display with smooth scrolling
3. **BOX Navigation**: Implement folder enter/exit with breadcrumb tracking
4. **Selection Management**: Current song and difficulty tracking with visual feedback
5. **Performance Optimization**: Lazy loading and texture caching for song titles

### Phase 2: Preview System & Audio Integration 📋 PLANNED
**Objective**: Implement preview sound system with fade effects

**Implementation Tasks:**
1. **Audio Integration**: Preview sound loading and playback using existing resource system
2. **Fade Effects**: BGM fade in/out during preview transitions
3. **Timing Control**: Configurable preview delay and duration
4. **Resource Management**: Proper audio disposal and cleanup
5. **Scroll Integration**: Prevent preview during active scrolling

### Phase 3: Advanced Navigation & Polish 📋 PLANNED
**Objective**: Complete song selection experience with advanced features

**Implementation Tasks:**
1. **Smooth Scrolling**: Counter-based animation with 100-unit increments per song
2. **Difficulty Selection**: Visual difficulty indicator and cycling with sound effects
3. **Input Handling**: Keyboard, gamepad, and drum pad support
4. **Visual Effects**: Selection highlighting, transition animations
5. **Status Panel**: Metadata and statistics display

## 🔧 Technical Considerations

### Performance Requirements
- **Large Libraries**: Support for 10,000+ songs
- **Smooth Scrolling**: 60fps animation during navigation
- **Memory Management**: Efficient texture and audio caching
- **Startup Time**: Fast initial song enumeration

### Cross-Platform Compatibility
- **File Paths**: Handle Windows/Unix path differences
- **Audio Formats**: Support OGG, WAV, MP3 preview files
- **Text Encoding**: Proper Japanese character handling
- **Resource Loading**: MonoGame-compatible asset management

### Integration Points
- **Resource Manager**: Use existing texture/audio loading system
- **Stage System**: Integrate with stage transition framework
- **Input System**: Leverage enhanced input state management
- **UI Components**: Utilize existing UI component library

## 🏗️ UI Implementation Architecture

### Core UI Classes Structure

#### SongSelectionStage (Main Stage)
```csharp
public class SongSelectionStage : BaseStage
{
    private UIManager _uiManager;
    private UIList _songList;
    private SongManager _songManager;
    private ITexture _whitePixel;
    private BitmapFont _bitmapFont;

    // Constants
    private const int VISIBLE_SONGS = 13;
    private const int CENTER_POSITION = 6;

    // Properties
    public SongListNode SelectedSong { get; private set; }
    public int CurrentDifficulty { get; private set; }

    // Stage lifecycle
    public override void Initialize();
    public override void Update(GameTime gameTime);
    public override void Draw(SpriteBatch spriteBatch);
    public override void HandleInput(InputState inputState);
}
```

#### UIList (Song List Component)
```csharp
public class UIList : UIElement
{
    // Display properties
    public Vector2 Position { get; set; }
    public Vector2 Size { get; set; }
    public int VisibleItemCount { get; set; } = 13;
    public int ItemHeight { get; set; } = 30;

    // Visual styling
    public Color BackgroundColor { get; set; }
    public Color SelectedItemColor { get; set; }
    public Color HoverItemColor { get; set; }
    public Color TextColor { get; set; }
    public Color SelectedTextColor { get; set; }
    public ITexture BackgroundTexture { get; set; }
    public SpriteFont Font { get; set; }

    // Data and selection
    public List<string> Items { get; set; } = new();
    public int SelectedIndex { get; set; }

    // Methods
    public override void Update(GameTime gameTime);
    public override void Draw(SpriteBatch spriteBatch);
    public void MoveNext();
    public void MovePrevious();
}
```

#### BaseStage (Stage Management)
```csharp
public abstract class BaseStage : IDisposable
{
    // Stage lifecycle
    public abstract void Initialize();
    public abstract void Update(GameTime gameTime);
    public abstract void Draw(SpriteBatch spriteBatch);
    public abstract void HandleInput(InputState inputState);

    // Phase management
    public StagePhase CurrentPhase { get; protected set; }
    public bool IsActive { get; protected set; }

    // Transition support
    public virtual void OnEnter() { }
    public virtual void OnExit() { }
    public virtual void Dispose() { }
}

public enum StagePhase
{
    Initializing,
    FadingIn,
    Active,
    FadingOut,
    Complete
}
```

#### UIManager (Component Management)
```csharp
public class UIManager
{
    private List<UIElement> _elements = new();
    private UIElement _focusedElement;

    // Component management
    public void AddElement(UIElement element);
    public void RemoveElement(UIElement element);
    public void SetFocus(UIElement element);

    // Update and rendering
    public void Update(GameTime gameTime);
    public void Draw(SpriteBatch spriteBatch);

    // Input handling
    public void HandleInput(InputState inputState);
}
```

### UI Implementation Details

#### Input Handling System
```csharp
public class SongSelectionInputHandler
{
    private readonly SongSelectionStage _stage;
    private readonly UIList _songList;

    public void HandleInput(InputState inputState)
    {
        // Navigation controls
        if (inputState.IsKeyPressed(Keys.Up) || inputState.IsKeyPressed(Keys.W))
        {
            _songList.MovePrevious();
            PlayNavigationSound();
        }
        else if (inputState.IsKeyPressed(Keys.Down) || inputState.IsKeyPressed(Keys.S))
        {
            _songList.MoveNext();
            PlayNavigationSound();
        }

        // Difficulty selection
        if (inputState.IsKeyPressed(Keys.Left) || inputState.IsKeyPressed(Keys.A))
        {
            _stage.CycleDifficultyPrevious();
            PlayDifficultySound();
        }
        else if (inputState.IsKeyPressed(Keys.Right) || inputState.IsKeyPressed(Keys.D))
        {
            _stage.CycleDifficultyNext();
            PlayDifficultySound();
        }

        // Selection and navigation
        if (inputState.IsKeyPressed(Keys.Enter) || inputState.IsKeyPressed(Keys.Space))
        {
            _stage.SelectCurrentSong();
        }
        else if (inputState.IsKeyPressed(Keys.Escape))
        {
            _stage.ExitToParent();
        }
    }

    private void PlayNavigationSound() { /* Play sound effect */ }
    private void PlayDifficultySound() { /* Play sound effect */ }
}
```

#### Visual Effects System
```csharp
public class UIEffects
{
    // DTXMania-style text rendering with shadows and outlines
    public static void DrawTextWithEffects(
        SpriteBatch spriteBatch,
        SpriteFont font,
        string text,
        Vector2 position,
        Color textColor,
        Color shadowColor = default,
        Color outlineColor = default,
        int shadowOffset = 2,
        int outlineThickness = 1)
    {
        // Draw outline
        if (outlineColor != Color.Transparent)
        {
            for (int x = -outlineThickness; x <= outlineThickness; x++)
            {
                for (int y = -outlineThickness; y <= outlineThickness; y++)
                {
                    if (x != 0 || y != 0)
                    {
                        spriteBatch.DrawString(font, text,
                            position + new Vector2(x, y), outlineColor);
                    }
                }
            }
        }

        // Draw shadow
        if (shadowColor != Color.Transparent)
        {
            spriteBatch.DrawString(font, text,
                position + new Vector2(shadowOffset, shadowOffset), shadowColor);
        }

        // Draw main text
        spriteBatch.DrawString(font, text, position, textColor);
    }

    // Selection highlighting with smooth transitions
    public static void DrawSelectionHighlight(
        SpriteBatch spriteBatch,
        ITexture backgroundTexture,
        Rectangle bounds,
        Color highlightColor,
        float alpha = 1.0f)
    {
        var color = highlightColor * alpha;
        spriteBatch.Draw(backgroundTexture.Texture2D, bounds, color);
    }
}
```

#### Preview Sound System (Phase 2)
```csharp
public class PreviewSoundManager
{
    private ISound _currentPreview;
    private ISound _backgroundMusic;
    private Timer _previewDelayTimer;
    private readonly IResourceManager _resourceManager;

    public int PreviewDelayMs { get; set; } = 1000;
    public int PreviewVolumePercent { get; set; } = 80;
    public bool IsScrolling { get; set; }

    public async Task PlayPreviewAsync(string previewPath)
    {
        // Stop current preview
        StopPreview();

        if (!string.IsNullOrEmpty(previewPath))
        {
            // Set delay timer before preview starts
            _previewDelayTimer = new Timer(async _ =>
            {
                if (!IsScrolling)
                {
                    await StartPreviewPlayback(previewPath);
                }
            }, null, PreviewDelayMs, Timeout.Infinite);
        }
    }

    private async Task StartPreviewPlayback(string previewPath)
    {
        try
        {
            _currentPreview = _resourceManager.LoadSound(previewPath);
            _currentPreview.Volume = PreviewVolumePercent / 100f;
            _currentPreview.IsLooping = true;
            _currentPreview.Play();

            // Start BGM fade out
            StartBGMFadeOut();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to play preview: {ex.Message}");
        }
    }

    public void StopPreview()
    {
        _previewDelayTimer?.Dispose();
        _previewDelayTimer = null;

        if (_currentPreview != null)
        {
            _currentPreview.Stop();
            _currentPreview.Dispose();
            _currentPreview = null;

            // Start BGM fade in
            StartBGMFadeIn();
        }
    }

    private void StartBGMFadeOut()
    {
        // Implement smooth BGM volume fade
        var startVolume = _backgroundMusic?.Volume ?? 1.0f;
        AnimateVolume(_backgroundMusic, startVolume, 0.0f, 500);
    }

    private void StartBGMFadeIn()
    {
        // Implement smooth BGM volume fade
        var targetVolume = 1.0f;
        AnimateVolume(_backgroundMusic, 0.0f, targetVolume, 1000);
    }

    private void AnimateVolume(ISound sound, float from, float to, int durationMs)
    {
        // Implement smooth volume transition animation
        // This will be part of Phase 2 implementation
    }
}
```

## � Implementation Files

### Current UI Files (Phase 1 - Complete)
- `DTXMania.Shared.Game/Lib/Stage/SongSelectionStage.cs` - Main song selection stage with UI integration
- `DTXMania.Shared.Game/Lib/UI/Components/UIList.cs` - Scrollable list component with song display
- `DTXMania.Shared.Game/Lib/UI/Components/UIManager.cs` - UI component management system
- `DTXMania.Shared.Game/Lib/Stage/BaseStage.cs` - Stage base class with lifecycle management
- `DTXMania.Shared.Game/Lib/Stage/StageManager.cs` - Stage transition and management system

### Planned Files (Phases 2-3)
- `DTXMania.Shared.Game/Lib/Audio/PreviewSoundManager.cs` - Preview audio system with fade effects
- `DTXMania.Shared.Game/Lib/UI/Effects/UIEffects.cs` - DTXMania-style visual effects
- `DTXMania.Shared.Game/Lib/UI/Components/StatusPanel.cs` - Song metadata and statistics display
- `DTXMania.Shared.Game/Lib/Input/SongSelectionInputHandler.cs` - Specialized input handling
- `DTXMania.Shared.Game/Lib/UI/Animation/ScrollAnimation.cs` - Smooth scrolling animation system

### Integration Points
- **Song Data**: Integrates with [Song Data Management System](song-data-management-system.md) for song information
- **Resource Manager**: Uses existing texture and font loading systems
- **Input System**: Leverages enhanced input state management
- **Audio System**: Integrates with MonoGame audio for preview playback

## 🎯 Summary

This document provides a complete implementation plan for DTXMania's Song Selection UI System, focusing on user interface components, stage management, and visual effects. The system is designed to integrate seamlessly with the song data management system while providing a smooth, responsive user experience.

### ✅ **Completed (Phase 1)**
- **UI Component Library**: Complete set of UI elements with DTXMania-style effects
- **Stage Management**: Full stage lifecycle with transitions and phase handling
- **Song Selection Stage**: Working implementation with visible UI and keyboard navigation
- **Font Integration**: Proper SpriteFont support for text rendering
- **Background Rendering**: Fixed UI list visibility with texture and font support

### 📋 **Next Steps (Phases 2-3)**
- **Phase 2**: Preview sound system with BGM fade effects and timing control
- **Phase 3**: Advanced navigation with smooth scrolling, difficulty selection, and visual polish

### 🔗 **Related Documentation**
- [Song Data Management System](song-data-management-system.md) - Data structures, file parsing, and database management
- [UI Architecture Summary](ui-architecture-summary.md) - Complete UI component library documentation

The song selection UI system provides the foundation for an authentic DTXMania experience with modern C# and MonoGame implementation.
