# Song Selection UI Implementation Analysis

## Overview

The song selection UI in DTXMania is a complex system built around a main stage controller (`CStageSongSelection`) that coordinates multiple specialized components to provide a rich song browsing and selection experience. The implementation is located in the `DTXMania/Code/Stage/05.SongSelection/` directory.

## Architecture

### Main Components

#### 1. CStageSongSelection (Main Stage Controller)
**File**: `CStageSongSelection.cs`
**Role**: Central coordinator that manages all song selection components and handles the overall flow.

**Key Properties**:
- `rSelectedSong`: Currently selected song node
- `rChosenScore`: Selected score for the chosen song
- `nConfirmedSongDifficulty`: Confirmed difficulty level
- `bScrolling`: Whether the song list is currently scrolling
- `bIsEnumeratingSongs`: Whether songs are being enumerated

**Key Methods**:
- `tSelectedSongChanged()`: Notifies all child components when selection changes
- `tSelectSong()`: Confirms song selection and transitions to next stage
- `tMoveCursorUp()/tMoveCursorDown()`: Handles cursor navigation

#### 2. CActSelectSongList (Core Song List UI)
**File**: `CActSelectSongList.cs`
**Role**: Primary visual interface for browsing and selecting songs.

**Key Features**:
- Displays 13 visible song bars at once (centered selection)
- Smooth scrolling animation with acceleration
- Preview image generation and display
- Clear lamp indicators for completion status
- Difficulty level management

**Key Properties**:
- `rSelectedSong`: Currently selected song
- `rSelectedScore`: Score for current difficulty
- `n現在のアンカ難易度レベル`: Current anchor difficulty level
- `bScrolling`: Scrolling state indicator

**Key Methods**:
- `t現在選択中の曲を元に曲バーを再構成する()`: Rebuilds song bars based on current selection
- `tMoveToNext()/tMoveToPrevious()`: Navigation methods
- `tGoIntoBOX()/tExitBOX()`: Folder navigation
- `tGenerateSongNameBar()`: Creates song title textures
- `tGeneratePreviewImageTexture()`: Handles preview image loading
- `tGenerateClearLampTexture()`: Creates clear status indicators

### Child Components

#### 3. CActSelectStatusPanel
**File**: `CActSelectStatusPanel.cs`
**Role**: Displays detailed song information and difficulty statistics.

**Features**:
- Shows difficulty levels for all available charts
- Displays best ranks and skill values
- Shows full combo status
- Handles multiple difficulty labels

#### 4. CActSelectPreimagePanel
**File**: `CActSelectPreimagePanel.cs`
**Role**: Manages preview content (images and videos).

**Features**:
- Delayed preview loading (configurable wait time)
- Support for both static images and AVI videos
- Fallback to background image extraction
- Automatic resource cleanup

**Key Methods**:
- `t選択曲が変更された()`: Triggers preview content change
- `tプレビュー画像_動画の変更()`: Handles content switching
- `tプレビュー動画の指定があれば構築する()`: Video preview setup

#### 5. CActSelectPresound
**File**: `CActSelectPresound.cs`
**Role**: Handles audio preview playback.

**Features**:
- Configurable preview delay
- Background music fade in/out
- Automatic sound cleanup
- Volume control

**Key Methods**:
- `t選択曲が変更された()`: Initiates preview sound change
- `tプレビューサウンドの作成()`: Creates and plays preview audio
- `tBGMフェードアウト開始()/tBGMフェードイン開始()`: BGM fade controls

#### 6. CActSelectArtistComment
**File**: `CActSelectArtistComment.cs`
**Role**: Displays artist information and song comments.

**Features**:
- Dynamic text rendering with proper sizing
- Artist name display with length management
- Comment scrolling for long text
- Font rendering optimization

#### 7. CActSelectQuickConfig
**File**: `CActSelectQuickConfig.cs`
**Role**: Provides quick access to game configuration options.

**Features**:
- Instrument-specific settings (Drums/Guitar/Bass)
- Auto mode configuration
- Scroll speed and play speed adjustment
- Hidden/Sudden options
- Configuration set switching

#### 8. CActSortSongs
**File**: `CActSortSongs.cs`
**Role**: Provides song sorting functionality.

**Sorting Options**:
- Title (alphabetical)
- Difficulty level
- Best rank achieved
- Play count
- Author
- Skill points
- Date modified

## Input Handling Flow

### Navigation Input Processing
The main input handling occurs in `CStageSongSelection.OnUpdateAndDraw()`:

1. **Cursor Movement**:
   - Up/Down arrows, drum pads (HT/LT), or guitar buttons
   - Calls `tMoveCursorUp()/tMoveCursorDown()`
   - Updates `nTargetScrollCounter` in song list

2. **Song Selection**:
   - Enter key or decide button
   - Handles different node types:
     - **Regular Song**: Calls `tSelectSong()`
     - **BOX**: Calls `tGoIntoBOX()` for folder entry
     - **BACKBOX**: Calls `tExitBOX()` for folder exit
     - **RANDOM**: Calls `tSelectSongRandomly()`

3. **Difficulty Selection**:
   - Left/Right arrows or specific buttons
   - Calls `t難易度レベルをひとつ進める()`
   - Updates anchor difficulty level

### Folder Navigation
- **BOX Entry**: `tGoIntoBOX()` navigates into song folders
- **BOX Exit**: `tExitBOX()` returns to parent folder
- Skin changes can be triggered during navigation
- Selection state is preserved during transitions

## Scrolling System

### Smooth Scrolling Implementation
The scrolling system uses a target-based approach:

1. **Target Setting**: Navigation input sets `nTargetScrollCounter`
2. **Animation**: `nCurrentScrollCounter` gradually approaches target
3. **Acceleration**: Speed increases with distance to target
4. **Bar Updates**: Song bars are regenerated during scroll

### Scroll Acceleration Logic
```
Distance <= 100: acceleration = 2
Distance <= 300: acceleration = 3  
Distance <= 500: acceleration = 4
Distance > 500:  acceleration = 8
```

## Song Bar Generation

### Bar Information Structure
Each visible bar contains:
- Song title texture
- Preview image texture  
- Clear lamp texture
- Bar type (Score/Box/Other)
- Color information

### Generation Process
1. **Title Generation**: `tGenerateSongNameBar()` creates text textures
2. **Preview Images**: `tGeneratePreviewImageTexture()` loads/generates preview content
3. **Clear Lamps**: `tGenerateClearLampTexture()` creates completion indicators
4. **Bar Reconstruction**: `t現在選択中の曲を元に曲バーを再構成する()` rebuilds all 13 bars

## Preview Content System

### Image Preview Priority
1. Dedicated preview image (PREIMAGE tag)
2. Preview video (PREMOVIE tag) 
3. Background image extraction
4. Default "no preview" image

### Video Preview Support
- AVI format support through CAVI class
- Frame-by-frame rendering
- Automatic playback timing
- Resource management and cleanup

## Clear Lamp System

### Lamp Types
- **Type 1**: Categorized difficulties (with difficulty labels)
- **Type 2**: Uncategorized clears (without difficulty labels)
- **5 Difficulty Levels**: Each with distinct colors

### Lamp Generation
- Dynamic bitmap creation with colored rectangles
- Per-instrument lamp display (Drums/Guitar/Bass)
- Real-time updates based on score data

## Component Communication

### Selection Change Notification
When a song selection changes, `tSelectedSongChanged()` notifies:
1. Preview image panel
2. Preview sound handler  
3. Performance history panel
4. Status panel
5. Artist comment display
6. Plugin system (if applicable)

### State Synchronization
- All components maintain references to current selection
- Centralized state management through main stage
- Automatic updates when selection or difficulty changes

## Performance Considerations

### Resource Management
- Texture cleanup on selection changes
- Delayed loading for preview content
- Efficient font rendering with caching
- Memory management for large song lists

### Optimization Features
- Preview content loading delays to reduce system load
- Configurable wait times for preview activation
- Efficient scrolling with minimal redraws
- Smart resource allocation for visible content only

## Configuration Integration

### User Preferences
- Preview delays (image and sound)
- Auto-play volume levels
- Scroll speed settings
- Skin selection per folder
- Sort preferences

### Quick Configuration
- In-game settings adjustment
- Instrument-specific options
- Real-time configuration changes
- Configuration set management

This implementation provides a comprehensive and user-friendly song selection experience with smooth animations, rich preview content, and extensive customization options.

## UI Layout Structure

### Critical Implementation Details from DTXManiaNX

#### Curved Song Bar Layout Coordinates (ptバーの基本座標)
**Source**: `CActSelectSongList.cs:1334`

The most important aspect of DTXManiaNX's song selection is the **curved layout positioning system**:

```csharp
private readonly Point[] ptバーの基本座標 = new Point[] { 
    new Point(0x2c4, 5),     // 708, 5    - Bar 0 (top)
    new Point(0x272, 56),    // 626, 56   - Bar 1
    new Point(0x242, 107),   // 578, 107  - Bar 2
    new Point(0x222, 158),   // 546, 158  - Bar 3
    new Point(0x210, 209),   // 528, 209  - Bar 4
    new Point(0x1d0, 270),   // 464, 270  - Bar 5 (CENTER/SELECTED)
    new Point(0x224, 362),   // 548, 362  - Bar 6
    new Point(0x242, 413),   // 578, 413  - Bar 7
    new Point(0x270, 464),   // 624, 464  - Bar 8
    new Point(0x2ae, 515),   // 686, 515  - Bar 9
    new Point(0x314, 566),   // 788, 566  - Bar 10
    new Point(0x3e4, 617),   // 996, 617  - Bar 11
    new Point(0x500, 668)    // 1280, 668 - Bar 12 (bottom)
};
```

**Key Points**:
- **13 bars total** (indices 0-12)
- **Bar 5 (index 5)** is the **selected/center position** at coordinates (464, 270)
- Creates a **curved layout** where bars curve outward from center
- Bars above center curve to the right (increasing X)
- Bars below center curve further right (increasing X)
- Selected bar is positioned leftmost for visual emphasis

#### Component Initialization and Coordination
**Source**: `CStageSongSelection.cs:114-127`

```csharp
base.listChildActivities.Add( this.actSongList = new CActSelectSongList() );
base.listChildActivities.Add( this.actStatusPanel = new CActSelectStatusPanel() );
base.listChildActivities.Add( this.actPerHistoryPanel = new CActSelectPerfHistoryPanel() );
base.listChildActivities.Add( this.actPreimagePanel = new CActSelectPreimagePanel() );
base.listChildActivities.Add( this.actPresound = new CActSelectPresound() );
base.listChildActivities.Add( this.actArtistComment = new CActSelectArtistComment() );
base.listChildActivities.Add( this.actInformation = new CActSelectInformation() );
base.listChildActivities.Add( this.actSortSongs = new CActSortSongs() );
base.listChildActivities.Add( this.actShowCurrentPosition = new CActSelectShowCurrentPosition() );
base.listChildActivities.Add( this.actBackgroundVideoAVI = new CActSelectBackgroundAVI() );
base.listChildActivities.Add( this.actQuickConfig = new CActSelectQuickConfig() );
```

#### Panel Positioning and Background Graphics
**Source**: `CStageSongSelection.cs:276-281, 394-399`

```csharp
// Graphics loading
this.txBackground = CDTXMania.tGenerateTexture( CSkin.Path( @"Graphics\5_background.jpg" ), false );
this.txTopPanel = CDTXMania.tGenerateTexture( CSkin.Path( @"Graphics\5_header panel.png" ), false );
this.txBottomPanel = CDTXMania.tGenerateTexture( CSkin.Path( @"Graphics\5_footer panel.png" ), false );

// Drawing positions
if( this.txTopPanel != null )
    this.txTopPanel.tDraw2D( CDTXMania.app.Device, 0, y );  // y varies with animation
    
if( this.txBottomPanel != null )
    this.txBottomPanel.tDraw2D( CDTXMania.app.Device, 0, 720 - this.txBottomPanel.szImageSize.Height );
```

#### Selected Song Bar Positioning Logic
**Source**: `CActSelectSongList.cs:1091-1095, 1178-1181`

```csharp
int i選曲バーX座標 = 673;         // Regular song bar X coordinate  
int i選択曲バーX座標 = 665;       // Selected song bar X coordinate (special highlighting)
int y選曲 = 269;                  // Selected song Y position

// Drawing the selected song bar with special offset
this.tDrawBar(i選択曲バーX座標, y選曲 - 30, this.stBarInformation[nパネル番号].eBarType, true);
```

#### Graphics Resource Files and Bar Types
**Source**: `CActSelectSongList.cs:557-565`

```csharp
// Different bar types for different content
this.txSongNameBar.Score = CDTXMania.tGenerateTexture( CSkin.Path( @"Graphics\5_bar score.png" ), false );
this.txSongNameBar.Box = CDTXMania.tGenerateTexture( CSkin.Path( @"Graphics\5_bar box.png" ), false );
this.txSongNameBar.Other = CDTXMania.tGenerateTexture( CSkin.Path( @"Graphics\5_bar other.png" ), false );
this.txSongSelectionBar.Score = CDTXMania.tGenerateTexture( CSkin.Path( @"Graphics\5_bar score selected.png" ), false );
this.txSongSelectionBar.Box = CDTXMania.tGenerateTexture( CSkin.Path( @"Graphics\5_bar box selected.png" ), false );
this.txSongSelectionBar.Other = CDTXMania.tGenerateTexture( CSkin.Path( @"Graphics\5_bar other selected.png" ), false );
```

#### Bar Information Structure
**Source**: `CActSelectSongList.cs:1261-1272`

```csharp
private struct STBarInformation
{
    public CActSelectSongList.EBarType eBarType;      // Score, Box, or Other
    public string strTitleString;                     // Song title text
    public CTexture txTitleName;                      // Generated title texture
    public STDGBVALUE<int> nSkillValue;              // Skill/difficulty values
    public Color colLetter;                          // Text color
    public CTexture txPreviewImage;                  // Preview image texture
    public CTexture txClearLamp;                     // Clear status lamp
    public string strPreviewImageFullPath;           // Preview image path
    public STDGBVALUE<int[]> nClearLamps;           // Clear lamp data per instrument
}
```

### Screen Layout Overview

Based on the actual DTXManiaNX implementation:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Header Panel (5_header panel.png)                                          │
├─────────────────────────────────────────────────────────────────────────────┤
│ Background (5_background.jpg)                                              │
│                                                                             │
│                    Song Bar 0 (708, 5)                                     │
│                Song Bar 1 (626, 56)                                        │
│              Song Bar 2 (578, 107)                                         │
│            Song Bar 3 (546, 158)                                           │
│           Song Bar 4 (528, 209)                                            │
│       ► Song Bar 5 (464, 270) ◄ [SELECTED/CENTER]                         │
│            Song Bar 6 (548, 362)                                           │
│              Song Bar 7 (578, 413)                                         │
│                Song Bar 8 (624, 464)                                       │
│                  Song Bar 9 (686, 515)                                     │
│                    Song Bar 10 (788, 566)                                  │
│                         Song Bar 11 (996, 617)                             │
│                                Song Bar 12 (1280, 668)                     │
│                                                                             │
├─────────────────────────────────────────────────────────────────────────────┤
│ Footer Panel (5_footer panel.png)                                          │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Key Implementation Requirements

1. **Curved Layout System**:
   - Must use exact `ptバーの基本座標` coordinates for authentic feel
   - Bar 5 (index 5) is always the selected song at (464, 270)
   - Songs scroll through these fixed positions
   - Different X coordinates create the curved visual effect

2. **Song Bar Types**:
   - **Score bars**: Regular songs with different graphics for normal/selected
   - **Box bars**: Folders with different graphics for normal/selected  
   - **Other bars**: Special items (random, back navigation)

3. **Component Coordination**:
   - Main stage (`CStageSongSelection`) coordinates all components
   - Song list (`CActSelectSongList`) handles curved layout and scrolling
   - Status panel, preview panel, sound preview work in concert
   - All components notified via `tSelectedSongChanged()` when selection changes

4. **Graphics Resources**:
   - Background: `Graphics\5_background.jpg`
   - Header panel: `Graphics\5_header panel.png`
   - Footer panel: `Graphics\5_footer panel.png`
   - Song bars: `Graphics\5_bar score.png`, `Graphics\5_bar box.png`, etc.
   - Selected bars: `Graphics\5_bar score selected.png`, etc.

5. **Animation System**:
   - Smooth scrolling with target-based animation
   - Acceleration based on distance to target
   - Song bars regenerated during scroll animation
   - Preview content updates with delays

### Navigation Flow

1. **Song Navigation**: Up/Down arrows move songs through curved positions
2. **Difficulty Selection**: Left/Right arrows cycle through available difficulties
3. **Folder Navigation**: Enter key enters folders, Escape/Back exits
4. **Song Selection**: Enter key on a song starts gameplay

This curved layout system is the **defining visual characteristic** of DTXMania's song selection interface and must be implemented exactly to achieve authentic DTXMania behavior.
