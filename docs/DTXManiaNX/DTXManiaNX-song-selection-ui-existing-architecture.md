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

### Screen Layout Overview
The DTXManiaNX song selection screen follows this layout structure:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Title Bar & Breadcrumb Navigation                                          │
├─────────────────────────────────────┬───────────────────────────────────────┤
│                                     │                                       │
│ Song List (Left Panel)              │ Status Panel (Right Panel)           │
│ ┌─────────────────────────────────┐ │ ┌───────────────────────────────────┐ │
│ │ [BOX] Folder Name               │ │ │ ♪ SONG                            │ │
│ │ Song Title 1 - Artist 1         │ │ │ Song Title                        │ │
│ │ Song Title 2 - Artist 2         │ │ │                                   │ │
│ │ Song Title 3 - Artist 3         │ │ │ Artist: Artist Name               │ │
│ │ Song Title 4 - Artist 4         │ │ │ Genre: Rock                       │ │
│ │ Song Title 5 - Artist 5         │ │ │ BPM: 120                          │ │
│ │ ► Song Title 6 - Artist 6 ◄     │ │ │ Duration: 3:45                    │ │
│ │ Song Title 7 - Artist 7         │ │ │                                   │ │
│ │ Song Title 8 - Artist 8         │ │ │ Difficulties:                     │ │
│ │ Song Title 9 - Artist 9         │ │ │ ► DRUMS: Level 85 (1,234 notes)  │ │
│ │ Song Title 10 - Artist 10       │ │ │   GUITAR: Level 78 (987 notes)   │ │
│ │ Song Title 11 - Artist 11       │ │ │   BASS: Level 65 (654 notes)     │ │
│ │ Song Title 12 - Artist 12       │ │ │                                   │ │
│ │ Song Title 13 - Artist 13       │ │ │ Performance:                      │ │
│ │ └─────────────────────────────────┘ │ │ Best Score: 95,432                │ │
│                                     │ │ Best Rank: A                      │ │
│                                     │ │ Full Combo: Yes                   │ │
│                                     │ │ Play Count: 15                    │ │
│                                     │ └───────────────────────────────────┘ │
├─────────────────────────────────────┴───────────────────────────────────────┤
│ Preview Image/Video Area & Artist Comments                                  │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Key Layout Principles

1. **Song List (Left Panel)**:
   - 13 visible items with center selection (item 6)
   - Smooth scrolling with acceleration
   - Shows song titles, artists, and folder navigation
   - Up/Down arrows navigate through songs
   - Enter activates selection (play song or enter folder)

2. **Status Panel (Right Panel)**:
   - Shows detailed information for currently selected song
   - Displays ALL available difficulties simultaneously
   - Current difficulty highlighted with ► indicator
   - Left/Right arrows cycle through difficulties
   - Shows performance statistics and metadata

3. **Difficulty Selection**:
   - **NOT a separate navigation mode**
   - Difficulties shown in status panel at all times
   - Left/Right arrows change current difficulty
   - Current difficulty highlighted in status panel
   - Song list remains visible and navigable

### Navigation Flow

1. **Song Navigation**: Up/Down arrows move through song list
2. **Difficulty Selection**: Left/Right arrows cycle through available difficulties
3. **Folder Navigation**: Enter key enters folders, Escape/Back exits
4. **Song Selection**: Enter key on a song starts gameplay

This layout ensures that users can see both the song list and all available difficulties simultaneously, providing a comprehensive view of available content without requiring separate navigation modes.
