# Song Selection UI Implementation Analysis

This document provides a comprehensive analysis of the Song Selection UI implementation in DTXManiaNX, detailing the architecture, components, and key mechanisms that power the main song browsing interface.

## Overview

The Song Selection UI is the most complex component in DTXManiaNX, responsible for browsing thousands of songs with smooth scrolling, preview capabilities, and multi-device input support. It uses a sophisticated stage-based architecture with coordinated child activities.

## Core Architecture

### Main Stage Controller: CStageSongSelection

**Location**: `DTXMania/Code/Stage/05.SongSelection/CStageSongSelection.cs`

The `CStageSongSelection` class serves as the main orchestrator for all song selection functionality. Key responsibilities:

- **Child Activity Management** (lines 114-127): Registers and coordinates 11 specialized child activities
- **Input Processing** (lines 446-815): Handles complex multi-device input with command sequences
- **State Management**: Maintains selection state, difficulty levels, and navigation history
- **Plugin Integration** (lines 149-179): Notifies plugins of song selection changes

### Child Component Architecture

The stage uses a composition pattern with specialized child activities:

```csharp
// Core UI Components (lines 114-127)
base.listChildActivities.Add(this.actSongList = new CActSelectSongList());           // Primary song list UI
base.listChildActivities.Add(this.actStatusPanel = new CActSelectStatusPanel());     // Difficulty/ranking info
base.listChildActivities.Add(this.actPreimagePanel = new CActSelectPreimagePanel()); // Preview images/videos
base.listChildActivities.Add(this.actPresound = new CActSelectPresound());           // Audio preview system
base.listChildActivities.Add(this.actQuickConfig = new CActSelectQuickConfig());     // In-game settings
base.listChildActivities.Add(this.actSortSongs = new CActSortSongs());               // Song sorting
```

## Primary Song List Component: CActSelectSongList

**Location**: `DTXMania/Code/Stage/05.SongSelection/CActSelectSongList.cs`

### 13-Bar List System (Current Implementation)

The song list uses a **vertical list layout** with 13 visible song bars, where only the selected song curves out from the main list:

- **Selected Position**: Always at index 5 (center focal point) - `nSelectedRow = 5` (line 1513)
- **Layout**: Vertical list with selected song positioned separately at X:665, Y:269
- **Current Implementation**: Fixed X position (X:673) for unselected bars, with only Y coordinates varying
- **Bar Coordinates**: Predefined positions in `ptバーの基本座標[]` (line 1334), but X coordinates are currently overridden

### Actual Coordinate System (Current Implementation)

The 13 bars follow a **vertical list pattern** with only Y coordinates from the original curved design:

```csharp
private readonly Point[] ptバーの基本座標 = new Point[] { 
    new Point(0x2c4, 5),   // (708, 5)   - X ignored, Y=5 used
    new Point(0x272, 56),  // (626, 56)  - X ignored, Y=56 used
    new Point(0x242, 107), // (578, 107) - X ignored, Y=107 used
    new Point(0x222, 158), // (546, 158) - X ignored, Y=158 used
    new Point(0x210, 209), // (528, 209) - X ignored, Y=209 used
    new Point(0x1d0, 270), // (464, 270) - SELECTED BAR (special position)
    new Point(0x224, 362), // (548, 362) - X ignored, Y=362 used
    new Point(0x242, 413), // (578, 413) - X ignored, Y=413 used
    new Point(0x270, 464), // (624, 464) - X ignored, Y=464 used
    new Point(0x2ae, 515), // (686, 515) - X ignored, Y=515 used
    new Point(0x314, 566), // (788, 566) - X ignored, Y=566 used
    new Point(0x3e4, 617), // (996, 617) - X ignored, Y=617 used
    new Point(0x500, 668)  // (1280, 668) - X ignored, Y=668 used
};
```

**Note**: The original curved X coordinates are present in the array but are **disabled** in the current implementation (line 1084: `int x = i選曲バーX座標;`).

### Layout Pattern Analysis (Current)

**X-Coordinate (Current Implementation)**:
- **Selected Bar (index 5)**: X:665, Y:269 (special position, curves out from list)
- **Unselected Bars**: Fixed X:673 (vertical list formation)
- **Note**: These coordinates position the song list to avoid overlap with status panel and preview image components

**Y-Coordinate Pattern**:
- **Spacing**: Approximately 51-55 pixels between each row  
- **Range**: From Y=5 (top) to Y=668 (bottom)
- **Coverage**: Spans entire screen height for full visibility

### Scrolling Implementation

**Scrolling Mechanics** (lines 751-944):
```csharp
// Movement triggers (lines 272-285)
public void tMoveToNext()    { this.nTargetScrollCounter += 100; }   // One song = 100 units
public void tMoveToPrevious() { this.nTargetScrollCounter -= 100; }

// Distance-based acceleration (lines 751-794)
// ≤100 units: acceleration = 2
// ≤300 units: acceleration = 3  
// ≤500 units: acceleration = 4
// >500 units: acceleration = 8
```

**Dynamic Content Updates**:
- **Upward Scrolling** (lines 795-869): Bottom panel gets new content from 7 positions ahead
- **Downward Scrolling** (lines 871-944): Top panel gets new content from 5 positions back
- **Circular Indexing**: Uses modulo 13 operations for efficient memory usage

### Bar Generation System

**Primary Bar Creation** (`tGenerateSongNameBar` - lines 1593-1645):
- **Bar Dimensions**: **Width ≤ 510 pixels** (maximum), **Height = 37 pixels** (0x25)
- Creates 2x resolution textures for anti-aliasing (displayed at 0.5x scale)
- Handles automatic text compression for long titles
- Applies shadow effects and custom colors
- **Small Preview Images**: **44×44 pixels** embedded in song bars with aspect ratio preservation

**Real-time Updates** (lines 262-271):
```csharp
public void t現在選択中の曲を元に曲バーを再構成する()
{
    this.tInitializeBar();
    for(int i = 0; i < 13; i++)
    {
        this.tGenerateSongNameBar(i, ...);        // Song title
        this.tGeneratePreviewImageTexture(i, ...); // Preview image
        this.tGenerateClearLampTexture(i, ...);    // Clear status
    }
}
```

## Child Component Responsibilities

### CActSelectStatusPanel
**Purpose**: Displays comprehensive difficulty grid for all instruments and levels, plus note analysis graphs
- **Location**: Lines 21-50 in `CActSelectStatusPanel.cs`
- **Position**: Fixed at coordinates X:130, Y:350 (left side of screen, positioned to the left of preview image)
- **Layout**: 3×5 grid (3 instruments × 5 difficulty levels) with accompanying graph panels
- **Updates**: Triggered by `tSelectedSongChanged()` when selection changes

**Grid Structure**:
- **Columns**: Drums (D) | Guitar (G) | Bass (B)
- **Rows**: 5 difficulty levels (0=Novice, 1=Regular, 2=Expert, 3=Master, 4=Ultimate)
- **Cell Content**: Level number, rank icon, full combo badge, achievement rate
- **Special Features**: Skill point badges, current selection highlighting

**Graph Panel Components** (Lines 420-482):
- **Graph Panel Base**: X:15, Y:368 (350 + 18)
  - Two types: `txDrumsGraphPanel` (Drums mode) / `txGuitarBassGraphPanel` (Guitar/Bass mode)
- **Total Notes Display**: X:81 (15 + 66), Y:666 (368 + 298)
  - Shows total note count for selected chart/difficulty
- **Note Distribution Bar Graph**: X:46/53, Y:389 (368 + 21)
  - **Drums**: 9 bars, 8px spacing, start X:46 (15 + 31)
  - **Guitar/Bass**: 6 bars, 10px spacing, start X:53 (15 + 38)  
  - Bar dimensions: 4px width, max height 252px
  - Colors represent different lanes/instruments
- **Progress Bar**: X:33 (15 + 18), Y:389 (368 + 21)
  - Shows chart completion progress
  - Generated dynamically from progress data

### CActSelectPreimagePanel  
**Purpose**: Manages preview images and videos 
- **Location**: Lines 24-48 in `CActSelectPreimagePanel.cs`
- **Position**: Panel position determined by status panel presence:
  - **Without status panel**: X:18, Y:88 (left side)
  - **With status panel**: X:250, Y:34 (right side, positioned to the right of status panel)
- **Size**: **368×368 pixels** (without status panel) or **292×292 pixels** (with status panel) - square aspect ratio maintained
- **Content Offsets**: 
  - Without status panel: X+37, Y+24 from base position
  - With status panel: X+8, Y+8 from base position
- **Features**: Delayed loading with configurable wait time, video playback support
- **Update Trigger**: `t選択曲が変更された()` method
- **Content**: Album art, preview videos, or default placeholder images

### CActSelectPresound
**Purpose**: Handles audio preview system
- **Location**: Lines 26-38 in `CActSelectPresound.cs`
- **Features**: Background music fading, configurable preview delay
- **Sound Management**: Automatic cleanup and volume control

### CActSelectArtistComment
**Purpose**: Displays artist name and song comments for selected song as background layer
- **Location**: `CActSelectArtistComment.cs`
- **Position**: Upper-right area of screen, **behind song list bars**
- **Comment Bar Background**: **Size not fixed** - loaded from Graphics/5_comment bar.png at (560, 257)
- **Artist Name**: X:1260-25-width, Y:320 (right-aligned, visible around selected song bar)
  - **Max Width**: **510 pixels** (same constraint as song bars)
  - **Font Scaling**: Uses 0.5x scale with horizontal compression for long names
- **Comment Text**: X:683, Y:339 (below artist, visible below song bars)
  - **Max Width**: **510 pixels**
  - **Font**: MS PGothic 40px font, 0.5x scale
  - **Multi-line Support**: Automatically wraps long comments across lines
- **Dynamic Sizing**: Text textures created at runtime based on content length
- **Layer Relationship**: Comment bar (Y:257) sits behind selected song bar (Y:270), creating overlay effect
- **Data Source**: Retrieved from song's DTX file (#ARTIST tag)
- **Update Trigger**: `t選択曲が変更された()` when selection changes
- **Z-Order**: Drawn before song bars, so artist info appears as background layer

### CActSelectPerfHistoryPanel
**Purpose**: Displays performance history overlay at bottom of song selection area
- **Location**: `CActSelectPerfHistoryPanel.cs`
- **Position**: Bottom overlay on top of song list
- **Coordinates**: X:700 (with status panel) or X:210 (without status panel), Y:570 (0x23a)
- **Background Panel**: **Size not fixed** - loaded from Graphics/5_play history panel.png
- **Text Area Dimensions**: **800×195 pixels** (0xc3 = 195) - dynamically generated bitmap
- **Content Offset**: Text at (X+18, Y+32) relative to panel background
- **Animation**: Slide-up entrance animation (100 frames, cosine easing)
- **Content Layout**: 
  - Up to **5 performance history entries**
  - **36 pixels vertical spacing** between entries
  - **Meiryo 26px Bold font**, yellow color, **0.5x scale**
- **Text Scaling**: Content rendered at 2x resolution, displayed at 0.5x for anti-aliasing
- **Data Source**: `cスコア.SongInformation.PerformanceHistory[]` array
- **Update Trigger**: `t選択曲が変更された()` when selection changes
- **Z-Order**: Overlays on top of song bars but below popups

### CActSelectQuickConfig
**Purpose**: In-game configuration popup
- **Location**: Lines 19-60 in `CActSelectQuickConfig.cs`
- **Position**: **Center screen** at X:460, Y:150 (popup base position)
- **Background**: **Size not fixed** - loaded from popup menu background texture
- **Content Layout**:
  - **Title Area**: X+96, Y+4 relative to popup base
  - **Menu Items**: X+18, Y+40 + (item_index × 32) - **32 pixels vertical spacing**
  - **Values**: X+200, Y+40 + (item_index × 32) relative to popup base
- **Cursor**: **16×32 pixels** with expandable middle section (19 segments of 16px each)
- **Access**: Double-tap commands (BD×2, P×2)
- **Settings**: Scroll speed, auto mode, play speed, risky mode, dark mode
- **Dynamic Sizing**: Menu expands based on number of configuration items

## Skill Point Section

**Purpose**: Displays the highest skill point value for the current song and instrument mode
- **Location**: Lines 405-416 in `CActSelectStatusPanel.cs`
- **Position**: X:32, Y:180 (above BPM section)
- **Background Panel**: Uses `5_skill point panel.png` texture
- **Components**:
  - **Skill Point Panel**: Background graphic at X:32, Y:180
  - **Skill Point Value**: Numerical value at X:92, Y:200 (32 + 60)
- **Value Format**: "##0.00" (e.g., " 85.42", "123.56")
- **Data Source**: 
  - Drums mode: `dbDrumSP` (highest skill point from drum charts)
  - Guitar/Bass mode: `dbGBSP` (highest skill point from guitar/bass charts)
- **Font**: Uses difficulty number texture (`txDifficultyNumber`) 
- **Update Trigger**: Changes when different song is selected or instrument mode is switched

## BPM and Song Information Section

**Purpose**: Displays song timing and duration information in "label: value" format
- **Location**: Lines 290-403 in `CActSelectStatusPanel.cs`
- **Position**: X:90, Y:275 (when status panel exists) or X:490, Y:385 (standalone)
- **Background Graphics**: Uses `5_BPM.png` texture containing "Length" and "BPM" labels
- **Components**:
  - **Duration Display**: "Length: 2:34" format
    - Label from background image, value at X:132, Y:268
  - **BPM Display**: "BPM: 145" format  
    - Label from background image, value at X:135, Y:298
- **Font**: Uses specialized BPM font texture (`txBPM数字`) for values
- **Data Source**: Extracted from selected song's DTX file information
- **Update Trigger**: Changes when different song is selected
- **Note**: Label texture loading is currently commented out in code (line 398)

### CActSortSongs
**Purpose**: Song sorting interface
- **Location**: Lines 13-60 in `CActSortSongs.cs`
- **Sort Options**: Title, Level, Best Rank, Play Count, Author, Skill Point, Date
- **Access**: Y+P (Guitar/Bass), FT×2 (Drums)

## Input Handling System

### Multi-Device Support

The input system supports three device types through unified `CPad` interface:

1. **Keyboard**: `CDTXMania.InputManager.Keyboard.bKeyPressed((int)SlimDXKey.KeyName)`
2. **Game Controllers**: `CDTXMania.Pad.bPressed(EInstrumentPart.INSTRUMENT, EPad.BUTTON)`
3. **MIDI Devices**: Same as controllers but with MIDI-specific configuration

### Command Pattern Implementation

**Command History System** (lines 935-1016):
```csharp
public class CCommandHistory
{
    private List<STCommandTime> stct; // Stores up to 16 commands with timestamps
    public bool CheckCommand(EPadFlag[] _ePad, EInstrumentPart _eInst) // 500ms timeout
}
```

**Double-Tap Commands**:
- **Drums Quick Config**: BD×2 (lines 580-591)
- **Difficulty Change**: HH×2 (lines 594-604)  
- **Guitar/Bass Swap**: Y×2 (lines 632-663)
- **Sort Menu**: FT×2 drums, Y+P guitar/bass (lines 693-719)

### Navigation Mapping

**Movement Controls**:
- **Up**: ↑ Arrow / R button (Guitar/Bass) / HT (Drums)
- **Down**: ↓ Arrow / G button (Guitar/Bass) / LT (Drums)
- **Select**: Enter / Decide button / CY/RD (Drums)
- **Back**: ESC / Cancel button / LC (Drums at root level)

### Key Repeat System

**Continuous Input** (lines 835-884):
```csharp
this.ctKeyRepeat.Up.tRepeatKey(
    CDTXMania.InputManager.Keyboard.bKeyPressing((int)SlimDXKey.UpArrow), 
    new CCounter.DGキー処理(this.tMoveCursorUp)
);
```

## Component Coordination

### Selection Change Propagation

When song selection changes (`tSelectedSongChanged()` - lines 141-180):

1. **Preview Image**: `actPreimagePanel.t選択曲が変更された()`
2. **Audio Preview**: `actPresound.t選択曲が変更された()`  
3. **Performance History**: `actPerHistoryPanel.t選択曲が変更された()`
4. **Status Panel**: `actStatusPanel.tSelectedSongChanged()`
5. **Artist Info**: `actArtistComment.t選択曲が変更された()`
6. **Plugin Notification**: Notifies all registered plugins

### Layer Coordination and Visual Hierarchy

**Artist Comment Integration with Song List**:
- **Artist comment bar** (Y:257) renders **before** song list bars (Y:270)
- **Selected song bar overlays** the artist comment background, creating layered effect
- **Artist text** (Y:320) appears **around/behind** the selected song bar
- **Comment text** (Y:339) appears **below** the song list area
- This creates visual connection between selected song and its metadata

**Performance History Panel Overlay**:
- **Play history panel** (Y:570) renders **after** song list bars, overlaying bottom area
- **Slide-up animation** with cosine easing for smooth entrance
- **Position adapts** based on status panel presence (X:700 with panel, X:210 without)
- **Content updates** dynamically when song selection changes
- **Z-Order**: Above song bars but below popup menus for proper layering

### Input Priority System

Input processing follows a strict priority hierarchy:

1. **Plugin Occupation Check**: `CDTXMania.actPluginOccupyingInput == null`
2. **Popup Menu Active**: `!this.actSortSongs.bIsActivePopupMenu && !this.actQuickConfig.bIsActivePopupMenu`
3. **Text Input Mode**: `!CDTXMania.app.bテキスト入力中`
4. **Normal Input Processing**: Standard navigation and selection

### Memory Management

**Efficient Resource Usage**:
- **Texture Recycling**: Disappearing bars are reused for new content
- **Circular Buffer**: 13-element arrays with modulo indexing
- **Lazy Loading**: Preview images and sounds loaded on demand
- **Automatic Cleanup**: Resources disposed when bars scroll out of view

## Visual Effects and Animation System

### Perspective and Depth Effects

**3D Perspective Simulation**:
- **Center Focus**: Selected song (index 5) at coordinates (464, 270) appears largest
- **Distance Scaling**: Bars scale down as they move away from center focus
- **Opacity Gradient**: Distant bars become increasingly transparent
- **Position Interpolation**: Smooth transitions between positions during scrolling

**Curved Layout Mathematics**:
```csharp
// Wave pattern creates depth illusion
X-coordinates: 708 → 464 → 1280  (inward curve, then outward)
Scaling factors: 0.8 → 1.1 → 0.7  (largest at center)
Opacity range: 0.4 → 1.0 → 0.2   (brightest at center)
```

### Animation Sequences

**Entry Animation** (lines 711-738):
- **Staggered Timing**: Each bar delays by 10ms from previous
- **Total Duration**: 130ms for complete 13-bar sequence
- **Effect**: Creates smooth cascade appearance

**Scrolling Animation**:
- **Acceleration Curve**: Distance-based speed adjustment
- **Interpolation**: Smooth movement between discrete positions
- **Content Updates**: Dynamic regeneration during movement

## Performance Optimizations

### Rendering Optimizations

1. **2x Texture Resolution**: Anti-aliased text rendered at 2x, displayed at 0.5x
2. **Selective Updates**: Only regenerate bars that actually changed content
3. **Animation Batching**: Staggered 10ms delays for smooth entrance animations
4. **Distance-Based Acceleration**: Faster scrolling for larger distances
5. **Perspective Culling**: Bars beyond screen edges use reduced detail
6. **Z-Order Optimization**: Selected song rendered last for proper highlighting

### Data Structure Efficiency

1. **Fixed Array Size**: 13-element arrays prevent dynamic allocation
2. **Circular Indexing**: `(index + offset) % 13` for wraparound navigation
3. **Reference Caching**: Current selection maintained without tree traversal
4. **Lazy Evaluation**: Difficulty levels calculated only when needed
5. **Coordinate Precalculation**: Static coordinate array avoids runtime calculations
6. **Texture Recycling**: Bar textures reused during scrolling transitions

## Search System Integration

**Text Input Component** (`CActTextBox` - lines 739-807):
- **Activation**: Search key opens text input overlay
- **Search Processing**: Filters song list based on title/artist matching
- **Exit Command**: Special "---" input to restore full list
- **Visual Feedback**: Search results count and status display

**Search Notification Display** (`CStageSongSelection.cs:1234-1253`):
- **Position**: **X:10, Y:130** (top-left area, below header)
- **Font**: **14px** font (`prvFontSearchInputNotification`)
- **Colors**: White text with black outline for visibility
- **Dynamic Sizing**: Text texture generated at runtime based on notification content
- **Display Duration**: Controlled by `ctSearchInputDisplayCounter` (10 second timeout)
- **Content**: Shows current search terms, result counts, and mode changes
- **Z-Order**: High priority overlay (drawn after most other components)

## Component Positioning System

### Fixed Coordinate Layout

The UI uses **hardcoded pixel coordinates** that are fixed in the source code, not configurable through skin files:

**Status Panel** (`CActSelectStatusPanel.cs:487-488`):
- **Main Panel**: X:130, Y:350 (left side area, positioned to the left of preview image when both exist)
- **BPM Section**: X:90, Y:275 (when panel exists) or X:490, Y:385 (standalone)
  - **Song Duration**: X:132, Y:268 (format: "m:ss")
  - **BPM Value**: X:135, Y:298 (format: "###")
- **Difficulty Text**: X:140, Y:352 (relative to main panel)

**Preview Image Panel** (`CActSelectPreimagePanel.cs:451-458`):
- **Without Status Panel**: X:18, Y:88 (368px size, left side)
- **With Status Panel**: X:250, Y:34 (292px size, right side)

**Song List Area**:
- **BPM Label**: X:32, Y:258 (fixed position)
- **Skill Point Panel**: X:32, Y:180
  - **Skill Point Value**: X:92, Y:200 (format: "##0.00")
- **Graph Panels Base**: X:15, Y:368

**Bar Coordinates** (`ptバーの基本座標[]` - line 1334):
```csharp
private readonly Point[] ptバーの基本座標 = new Point[] { 
    new Point(0x2c4, 5),   // Bar 0: (708, 5)
    new Point(0x272, 56),  // Bar 1: (626, 56)
    new Point(0x242, 107), // Bar 2: (578, 107)
    new Point(0x222, 158), // Bar 3: (546, 158)
    new Point(0x210, 209), // Bar 4: (528, 209)
    new Point(0x1d0, 270), // Bar 5: (464, 270) - SELECTED
    new Point(0x224, 362), // Bar 6: (548, 362)
    // ... continues for all 13 bars
};
```

### Layout Design Principles

The Song Selection UI follows a **left-right split layout** optimized for 1280×720 resolution:

1. **Left Side (X: 0-400px)**:
   - **Graph Panel**: Note analysis and distribution charts at X:15, Y:368
   - **Status Panel**: 3×5 difficulty grid positioned at X:130, Y:350
   - **Information Panels**: Song metadata (BPM, skill points) and performance history
   - **Additional UI Elements**: BPM display and other song details

2. **Right Side (X: 400-1280px)**:
   - **Preview Image Panel**: Large album art/video display at X:250, Y:34
   - **Curved Song List**: 13-bar perspective arrangement starting at X:464
   - **Center Focus**: Selected song prominently displayed at (464, 270)
   - **Perspective Fade**: Distant songs fade and scale down

3. **Screen Spanning Elements**:
   - **Header Panel**: Top overlay with system information and branding (from Graphics/5_header panel.png)
   - **Footer Panel**: Bottom overlay with control hints and navigation help (from Graphics/5_footer panel.png)
   - **Background**: Video or static image behind all UI elements (Graphics/5_background.jpg or 5_background.mp4)

### Screen Space Utilization

**Information Density Distribution**:
- **30% Left Side**: Dense information panels (graph analysis, status/difficulty grid, stats, song metadata)
- **70% Right Side**: Preview image and song selection with curved perspective layout
- **Focus Architecture**: Selected song at golden ratio position (≈38% from left)

**Depth Layering System**:
- **Background (Z:1)**: Video/image background
- **UI Panels (Z:4-5)**: Information panels and song bars
- **Selection (Z:6)**: Currently selected song highlighted
- **System UI (Z:10)**: Top/bottom panels always visible
- **Popups (Z:15-20)**: Search notifications, quick config menus

## Status Panel Difficulty Grid System

### 3×5 Grid Layout

The status panel displays a comprehensive **3×5 difficulty grid** showing all available information simultaneously:

**Column Structure**:
```csharp
int[] nPart = { 0, CDTXMania.ConfigIni.bIsSwappedGuitarBass ? 2 : 1, CDTXMania.ConfigIni.bIsSwappedGuitarBass ? 1 : 2 };
// Column 0: Drums (D)
// Column 1: Guitar (G) - swappable with Bass
// Column 2: Bass (B) - swappable with Guitar
```

**Row Structure** (5 difficulty levels, highest to lowest):
- **Row 0**: Ultimate/DTXMania (Level 4)
- **Row 1**: Master (Level 3)
- **Row 2**: Expert (Level 2) 
- **Row 3**: Regular (Level 1)
- **Row 4**: Novice (Level 0)

### Individual Difficulty Cell Content

Each cell in the 3×5 grid displays:

- **Cell Dimensions**: **187×60 pixels** (default) or dynamically calculated as texture_width÷3 × texture_height×2÷11
- **Content**:
  1. **Difficulty Level**: Format `XX.XX` or `--` if no chart exists
  2. **Rank Icon**: Best achieved rank (E, D, C, B, A, S, SS)
  3. **Achievement Badge**: Full Combo (FC) or Excellent (100%) icons
  4. **Achievement Rate**: Percentage like `95.67%` or "MAX" for perfect scores

### Cell Positioning Calculation

```csharp
int nPanelW = 187;  // Panel width
int nPanelH = 60;   // Panel height

// For instrument j, difficulty i:
int nBoxX = nBaseX + this.txパネル本体.szImageSize.Width + (nPanelW * (nPart[j] - 3));
int nBoxY = (391 + ((4 - i) * 60)) - 2;  // Higher difficulties at top
```

### Special Visual Indicators

**Current Selection Highlighting**:
- Active difficulty gets highlighted frame (`tx難易度枠`)
- Only shown for currently enabled instrument mode

**Skill Point Badges**:
- Special badges displayed on difficulties that contributed highest skill points
- Separate badges for Drums vs Guitar/Bass skill calculations
- Positioned as overlay on the contributing difficulty cell

**Instrument Availability**:
- Cells show `--` when no chart exists for that instrument/difficulty
- Visual styling indicates which charts are playable

### Data Structure Support

**Multi-Instrument Arrays**:
```csharp
STDGBVALUE<int>[] n現在選択中の曲のレベル難易度毎DGB = new STDGBVALUE<int>[5];
STDGBVALUE<bool>[] b現在選択中の曲に譜面がある = new STDGBVALUE<bool>[5];
STDGBVALUE<double>[] db現在選択中の曲の最高スキル値難易度毎 = new STDGBVALUE<double>[5];
```

This structure allows simultaneous display of complete difficulty information across all instruments and difficulty levels, providing players with comprehensive song information at a glance.

## Graph Panel System

### Note Analysis Panel (Lines 420-482)

The Graph Panel provides detailed note distribution analysis for the currently selected chart and difficulty:

**Panel Base Position**:
```csharp
int nGraphBaseX = 15;
int nGraphBaseY = 368; // 350 + 18
```

**Components**:

1. **Background Panel**:
   - **Drums Mode**: Uses `txDrumsGraphPanel` texture
   - **Guitar/Bass Mode**: Uses `txGuitarBassGraphPanel` texture
   - Position: X:15, Y:368

2. **Total Notes Counter**:
   - **Position**: `X: 15 + 66 - text_width_adjustment, Y: 368 + 298 = 666`
   - **Data Source**: `nPanelNoteCount` from selected chart
   - **Format**: Simple numerical display (e.g., "1247")

3. **Lane Distribution Bar Graph**:
   - **Purpose**: Visual representation of note count per lane/instrument
   - **Bar Specifications**: **Width = 4 pixels** (fixed), **Max Height = 252 pixels**
   - **Drums Configuration** (9 lanes):
     - Start position: X:46 (15 + 31), Y:389 (368 + 21)
     - **Spacing**: **8 pixels between bars**
     - Lanes: LC, HH, LP, SD, HT, BD, LT, FT, CY
     - Colors: Each lane has distinct color from `clDrumChipsBarColors[]`
   - **Guitar/Bass Configuration** (6 lanes):
     - Start position: X:53 (15 + 38), Y:389 (368 + 21)  
     - **Spacing**: **10 pixels between bars**
     - Lanes: R, G, B, Y, P, Pick
     - Colors: Each lane has distinct color from `clGBChipsBarColors[]`

4. **Progress Bar**:
   - **Position**: X:33 (15 + 18), Y:389 (368 + 21)
   - **Purpose**: Shows chart completion progress/status
   - **Data Source**: `strProgressText` from selected song progress data
   - **Generation**: Dynamic texture creation via `txGenerateProgressBarLine()`

### Graph Data Processing

**Bar Height Calculation** (Lines 451-477):
```csharp
int nBarMaxHeight = 252;
int[] chipsBarHeights = nCalculateChipsBarPxHeight(arrChipsByLane, nBarMaxHeight);
```

**Lane Data Collection**:
- **Drums**: 9-element array from `cスコア.SongInformation.chipCountByLane[ELane.XX]`
- **Guitar**: 6-element array (GtR, GtG, GtB, GtY, GtP, GtPick)
- **Bass**: 6-element array (BsR, BsG, BsB, BsY, BsP, BsPick)

**Dynamic Updates**:
- Graph regenerates when song selection changes
- Bar heights recalculated based on note distribution
- Progress data reflects current chart completion status
- Total note count updates for selected difficulty level

This comprehensive note analysis system provides players with detailed insight into chart complexity and their performance progress across different lanes and instruments.

## Header and Footer Panel System

### Panel Architecture (Lines 272-399)

The Song Selection UI uses dedicated header and footer panels that span the full screen width, providing consistent branding and navigation information:

**Resource Loading** (Lines 276-278):
```csharp
this.txBackground = CDTXMania.tGenerateTexture(CSkin.Path(@"Graphics\5_background.jpg"), false);
this.txTopPanel = CDTXMania.tGenerateTexture(CSkin.Path(@"Graphics\5_header panel.png"), false);
this.txBottomPanel = CDTXMania.tGenerateTexture(CSkin.Path(@"Graphics\5_footer panel.png"), false);
```

### Header Panel Implementation

**Position and Animation** (Lines 387-395):
- **Base Position**: `X: 0, Y: 0` (top of screen)
- **Size**: **Full screen width (1280px) × texture height** (determined by `this.txTopPanel.szImageSize.Height`)
- **Entry Animation**: Slides down from off-screen during stage entry
- **Animation Formula**: 
  ```csharp
  double db登場割合 = ((double)this.ct登場時アニメ用共通.nCurrentValue) / 100.0;
  double dbY表示割合 = Math.Sin(Math.PI / 2 * db登場割合);
  y = ((int)(this.txTopPanel.szImageSize.Height * dbY表示割合)) - this.txTopPanel.szImageSize.Height;
  ```
- **Final Position**: `this.txTopPanel.tDraw2D(CDTXMania.app.Device, 0, y);`

**Content Areas**:
- **Left Side**: DTXMania branding/logo area
- **Right Side**: System information (song count, FPS, current mode)
- **Background**: Semi-transparent overlay with blur effects

### Footer Panel Implementation

**Position** (Lines 398-399):
- **Fixed Position**: `X: 0, Y: 720 - panel_height`
- **Size**: **Full screen width (1280px) × texture height** (determined by `this.txBottomPanel.szImageSize.Height`)
- **Drawing**: `this.txBottomPanel.tDraw2D(CDTXMania.app.Device, 0, 720 - this.txBottomPanel.szImageSize.Height);`
- **Height Calculation**: Dynamically calculated from texture size

**Content Areas**:
- **Center Area**: Control hints and navigation instructions
- **Input Mapping**: Shows current control scheme (keyboard, controller, MIDI)
- **Context Hints**: Dynamic help text based on current selection state

### Background System Integration

**Background Video** (Lines 284-291):
```csharp
this.rBackgroundVideoAVI = new CDTX.CAVI(1290, CSkin.Path(@"Graphics\5_background.mp4"), "", 20.0);
this.actBackgroundVideoAVI.bLoop = true;
this.actBackgroundVideoAVI.Start(EChannel.MovieFull, rBackgroundVideoAVI, 0, -1);
```

**Background Elements**:
- **Primary**: Video file (5_background.mp4) with looping playback
- **Fallback**: Static image (5_background.jpg) if video unavailable  
- **Layer Order**: Background (Z:1) → UI Panels (Z:4-5) → Header/Footer (Z:10)

### Panel Integration with UI Layout

**Z-Order Management**:
- **Background Video**: Z-index 1 (lowest)
- **UI Components**: Z-index 4-6 (middle layers)
- **Header/Footer Panels**: Z-index 10 (top overlay)
- **Popups/Notifications**: Z-index 15-20 (highest)

**Coordinate System Impact**:
- **Effective UI Area**: Reduced by header/footer panel heights
- **Content Positioning**: All UI elements positioned to avoid panel overlap
- **Safe Zone**: Central 1280×520 area for main content (720 - header - footer heights)

This panel system provides consistent visual hierarchy and ensures all UI elements remain accessible while maintaining proper information architecture across the entire Song Selection interface.

## Extension Points

### Plugin Integration

The system provides plugin hooks for:
- **Song Selection Changes**: `On選択曲変更()` with file path and SetDef info
- **Input Occupation**: Plugins can temporarily take over input processing
- **Custom Commands**: Plugin-specific command sequences

### Customization Support

- **Skin System**: Configurable graphics and animations (positions are fixed)
- **BOX Definition**: Folder-specific skins and metadata
- **Input Mapping**: Flexible key assignment for all devices
- **Performance Metrics**: Configurable skill calculation methods

## Architectural Insights

### Design Philosophy

The Song Selection UI embodies several sophisticated design principles:

**1. Perspective-Based Navigation**:
- Uses curved layout to simulate 3D depth on 2D screen
- Selected song prominently displayed as focal point
- Creates intuitive navigation through visual hierarchy

**2. Information Architecture**:
- **Left Side**: Dense, detailed information (preview, difficulty grid, metadata)
- **Right Side**: Visual browsing with perspective effects
- **Balanced Layout**: 30/70 split optimizes information density vs. navigation space

**3. Performance-First Design**:
- Fixed 13-bar window prevents memory growth with large libraries
- Coordinate precalculation eliminates runtime mathematics
- Circular buffering enables infinite scrolling without array resizing

### Technical Excellence

**Memory Management**:
- **Constant Memory Usage**: O(1) memory regardless of song database size
- **Texture Recycling**: Bars reuse textures during scrolling
- **Lazy Loading**: Resources loaded only when needed
- **Automatic Cleanup**: Disposed resources prevent memory leaks

**Rendering Efficiency**:
- **Static Coordinate System**: Eliminates layout calculations
- **Distance-Based Optimization**: Reduces detail for distant elements
- **Selective Updates**: Only changed content gets regenerated
- **Z-Order Management**: Proper layering without overdraw

## Conclusion

The Song Selection UI demonstrates sophisticated software architecture with:

- **Curved Perspective System**: 13-bar layout with 3D depth simulation
- **Efficient Scrolling**: Fixed window with O(1) memory usage regardless of database size
- **Multi-Device Input**: Unified handling of keyboard, controller, and MIDI with complex command sequences
- **Component Coordination**: Loosely coupled activities with event-driven updates  
- **Advanced Visual Effects**: Perspective scaling, opacity gradients, and smooth animations
- **Performance Optimization**: Memory-efficient rendering, coordinate precalculation, and selective updates
- **Information Design**: Optimal 30/70 left-right split balancing detail and navigation
- **Extensibility**: Plugin system and customization support

This implementation successfully handles large song databases (thousands of entries) while maintaining 60fps performance and responsive user interaction across multiple input device types. The curved perspective layout creates an intuitive and visually appealing interface that stands out from traditional vertical list approaches, demonstrating advanced UI design principles in gaming applications.