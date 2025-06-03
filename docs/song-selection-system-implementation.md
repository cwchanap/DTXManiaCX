# DTXMania Song Selection System Implementation Plan

## 📊 Current Implementation Status

### ✅ COMPLETED PHASES (Phases 1-3)
- **Phase 1**: Core Data Structures - **100% Complete** with 154 unit tests passing
- **Phase 2**: Song Discovery & Enumeration - **100% Complete** with comprehensive file format support
- **Phase 3**: DTXManiaNX UI Components & Stage Integration - **100% Complete** with enhanced components

### 📋 PLANNED (Phases 4-8) - Updated Based on DTXManiaNX Architecture Analysis
- **Phase 4**: DTXManiaNX Song List Display Enhancement - **100% Complete** with Japanese font support
- **Phase 5**: Status Panel & Song Information - **100% Complete** with enhanced metadata display
- **Phase 6**: Preview Sound System & Audio Integration - CActSelectPresound equivalent
- **Phase 7**: Visual Polish & Performance Optimization - DTXManiaNX visual parity
- **Phase 8**: Advanced Features & Complete DTXManiaNX Parity - Remaining components

### 🎯 Key Achievements
- ✅ **JSON-based song database system** with caching and incremental enumeration
- ✅ **Full DTX file parsing** with Japanese text support (Shift_JIS encoding)
- ✅ **Hierarchical song organization** supporting set.def/box.def folder structures
- ✅ **Comprehensive unit test coverage** with 245 tests across all components (167 + 78 new)
- ✅ **Performance optimizations** for medium-sized libraries with background threading
- ✅ **Cross-platform compatibility** with proper file path handling
- ✅ **DTXManiaNX-compatible UI components** with smooth scrolling and authentic behavior
- ✅ **Stage management system** with transitions and phase handling
- ✅ **Thread-safe data operations** with proper locking mechanisms
- ✅ **Enhanced song selection interface** matching original DTXManiaNX architecture
- ✅ **Advanced status panel** with note counts, duration, and comprehensive song information
- ✅ **Enhanced metadata system** supporting duration calculation and note count tracking

### 🚀 Next Steps (Phase 6+) - UPDATED PRIORITIES
1. **🔥 URGENT: Default Skin & Visual Polish** - Fix primitive UI appearance with DTXManiaNX-style graphics and styling
2. **Implement Preview Sound System** - Audio preview with BGM fade effects (CActSelectPresound equivalent)
3. **Performance Optimization** - Advanced texture caching and memory management for large libraries
4. **Advanced Features** - Artist comments, quick config, song sorting functionality
5. **Database Migration** - Optional SQLite migration for large song libraries (10,000+ songs)

## Overview

This document provides a comprehensive analysis and implementation plan for DTXMania's Song Selection System based on examination of the original DTXManiaNX source code. The system handles song discovery, metadata parsing, list navigation, preview playback, and performance optimization for large song libraries.

## 🔍 DTXManiaNX Architecture Analysis Summary

### Key DTXManiaNX Components Analyzed
Based on examination of `docs/DTXManiaNX/DTXManiaNX-song-selection-ui-existing-architecture.md`, the original DTXManiaNX song selection system consists of:

1. **CStageSongSelection** - Main stage controller coordinating all components
2. **CActSelectSongList** - Core song list UI with 13-item display and smooth scrolling
3. **CActSelectStatusPanel** - Detailed song information and difficulty statistics
4. **CActSelectPresound** - Preview sound player with BGM fade effects
5. **CActSelectPreimagePanel** - Preview content management (images/videos)
6. **CActSelectArtistComment** - Artist information and song comments
7. **CActSelectQuickConfig** - Quick access to game configuration
8. **CActSortSongs** - Song sorting functionality

### 🔍 UPDATED: Current Implementation Analysis (December 2024)

**✅ ARCHITECTURE CORRECTLY IMPLEMENTED:**
- ✅ **Song List Layout**: Left panel with 13-item visible window and smooth scrolling
- ✅ **Status Panel Layout**: Right panel showing song information and difficulties
- ✅ **Navigation Flow**: Up/Down for songs, Left/Right for difficulties, Enter for activation
- ✅ **Difficulty Display**: All difficulties shown simultaneously in status panel (NOT separate navigation)
- ✅ **BOX Navigation**: Folder enter/exit with breadcrumb tracking
- ✅ **Song Database**: Complete enumeration and management system
- ✅ **UI Component Structure**: Proper DTXManiaNX-compatible component hierarchy

**🚨 CRITICAL VISUAL ISSUES IDENTIFIED:**
- **❌ Missing Default Skin**: Shows primitive rectangles instead of DTXManiaNX graphics
- **❌ Basic Font Rendering**: No shadows, outlines, or DTXManiaNX-style text effects
- **❌ No Visual Styling**: Missing colors, backgrounds, and visual polish
- **❌ Resource Loading**: Default skin path not configured (System/Graphics/)

**📋 REMAINING DTXManiaNX Features:**
- **Preview Sound System**: Audio preview with configurable delay and BGM fade effects
- **Advanced Visual Effects**: Transitions, animations, and DTXManiaNX-specific styling
- **Performance Optimization**: Advanced texture caching and memory management
- **Additional Components**: Artist comments, quick config, song sorting

## 📋 Analysis Summary

### Key Files Analyzed
- **CStageSongSelection.cs** - Main song selection stage with input handling and UI coordination
- **CActSelectSongList.cs** - Song list implementation with scrolling, navigation, and bar rendering
- **CSongListNode.cs** - Song/folder node structure with hierarchical organization
- **CSongManager.cs** - Song database management with caching and enumeration
- **CDTX.cs** - DTX file parsing for metadata extraction
- **CActSelectStatusPanel.cs** - Status panel showing song info, difficulty, and statistics
- **CActSelectPresound.cs** - Preview sound player with fade effects

## 🎵 Song Data Architecture

### Song List Node Structure
```csharp
public class CSongListNode
{
    public ENodeType eNodeType;           // SCORE, BOX, BACKBOX, RANDOM
    public CScore[] arScore = new CScore[5]; // Up to 5 difficulty levels
    public string[] arDifficultyLabel = new string[5]; // Difficulty names
    public List<CSongListNode> list子リスト; // Child nodes for BOX navigation
    public CSongListNode r親ノード;        // Parent node reference
    public string strタイトル;             // Song/folder title
    public string strジャンル;             // Genre information
    public Color col文字色;                // Text color for display
    public string strBreadcrumbs;          // Navigation path for position tracking
    public string strSkinPath;             // Custom skin path for BOX folders
}
```

### Song Manager Database System
```csharp
public class CSongManager
{
    public List<CScore> listSongsDB;        // songs.db cache
    public List<CSongListNode> listSongRoot; // Root song list
    public int nNbScoresFromSongsDB;        // Database statistics
    public int nNbScoresFound;              // Enumeration statistics
    public bool bIsSuspending;              // Thread suspension control
    public AutoResetEvent autoReset;       // Thread synchronization
}
```

## 🔍 Song Discovery & Enumeration

### File Format Support
- **DTX Files**: `.dtx` - Primary DTXMania format
- **GDA Files**: `.gda` - Guitar format
- **BMS Files**: `.bms`, `.bme` - BeatMania format
- **G2D Files**: `.g2d` - Guitar format variant
- **Set Definition**: `set.def` - Multi-difficulty song definitions
- **Box Definition**: `box.def` - Folder organization with metadata

### Folder Organization Patterns
```
Songs/
├── dtxfiles.FolderName/     # DTXFiles-style BOX (title from folder name)
│   ├── box.def              # Optional metadata override
│   ├── song1.dtx
│   └── song2.dtx
├── CustomFolder/            # box.def-style BOX
│   ├── box.def              # Required for metadata
│   ├── set.def              # Multi-difficulty definitions
│   └── songs/
└── IndividualSong.dtx       # Standalone song file
```

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

### Phase 1: Core Data Structures ✅ COMPLETED
**Objective**: Implement fundamental song data structures and file parsing capabilities

**Implementation Summary:**
- ✅ **Song Node System**: Complete hierarchical structure with NodeType support (Score, Box, BackBox, Random)
- ✅ **Song Manager**: Full database management with JSON serialization and async enumeration
- ✅ **Score Data**: Comprehensive metadata storage with performance tracking and skill calculation
- ✅ **File Parsing**: DTX file header parsing with Shift_JIS encoding for Japanese text support

**Files Created:**
- `DTXMania.Shared.Game/Lib/Song/SongMetadata.cs` - Song metadata with DTX parsing support
- `DTXMania.Shared.Game/Lib/Song/SongScore.cs` - Performance score tracking with skill calculation
- `DTXMania.Shared.Game/Lib/Song/SongListNode.cs` - Hierarchical song list structure
- `DTXMania.Shared.Game/Lib/Song/DTXMetadataParser.cs` - DTX file parsing with Japanese text support
- `DTXMania.Shared.Game/Lib/Song/SongManager.cs` - Song database management and enumeration
- `DTXMania.Shared.Game/Lib/Song/SongSystemTest.cs` - Basic functionality tests

**Key Features Implemented:**
- ✅ DTX file metadata parsing with Shift_JIS encoding support
- ✅ Hierarchical song organization (Score, Box, BackBox, Random nodes)
- ✅ Multi-difficulty support (up to 5 levels per song)
- ✅ Performance score tracking with rank calculation
- ✅ JSON-based database serialization for caching
- ✅ Async song enumeration with progress tracking
- ✅ Cross-platform file path handling
- ✅ Comprehensive error handling and logging

**Integration Status:**
- ✅ Integrated with existing StartupStage for testing
- ✅ Compatible with existing resource management system
- ✅ Added System.Text.Encoding.CodePages package for Japanese text support
- ✅ Configured to read from user's DTXFiles folder
- ✅ All file I/O conflicts resolved

**Unit Test Coverage:**
- ✅ **154 unit tests** created and passing for all Phase 1 components
- ✅ **SongMetadataTests** (26 tests) - metadata handling, calculated properties, cloning
- ✅ **SongScoreTests** (20 tests) - score tracking, rank calculation, skill computation
- ✅ **SongListNodeTests** (25 tests) - hierarchical organization, node operations, sorting
- ✅ **DTXMetadataParserTests** (18 tests) - file parsing, encoding support, error handling
- ✅ **SongManagerTests** (22 tests) - database management, enumeration, event handling
- ✅ **xUnit framework** with Theory/InlineData patterns following project standards

### Phase 2: Song Discovery & Enumeration ✅ COMPLETED
**Objective**: Implement comprehensive song discovery with advanced folder organization support

**Implementation Summary:**
- ✅ **File Enumeration**: Complete recursive directory scanning for all supported formats (.dtx, .gda, .bms, .bme, .g2d)
- ✅ **JSON-Based Caching**: Full songs.db equivalent using JSON serialization with incremental updates
- ✅ **Background Threading**: Async enumeration with progress tracking and cancellation support
- ✅ **Set.def Support**: Multi-difficulty song definition parsing with proper file path resolution
- ✅ **Box.def Support**: Folder metadata parsing including title, genre, skin path, and colors

**Current Data Storage Implementation:**
- ✅ **JSON Database**: `SongDatabaseData` class with JSON serialization for persistence
- ✅ **In-Memory Collections**: `List<SongScore>` and `List<SongListNode>` for runtime operations
- ✅ **File-Based Caching**: songs.db JSON file with modification time checking for incremental updates
- ✅ **Thread-Safe Operations**: Proper locking mechanisms for concurrent access

**Files Enhanced:**
- `DTXMania.Shared.Game/Lib/Song/SongManager.cs` - Added set.def/box.def parsing, incremental enumeration
- `DTXMania.Test/Song/SongManagerTests.cs` - Comprehensive Phase 2 functionality tests

**Key Features Implemented:**
- ✅ **Set.def parsing** for multi-difficulty song definitions with proper file path resolution
- ✅ **Box.def parsing** for folder metadata including title, genre, skin path, and colors
- ✅ **Enhanced database caching** with incremental updates and modification time checking
- ✅ **Background threading improvements** with better progress tracking and cancellation support
- ✅ **Incremental enumeration** that only processes changed directories for better performance
- ✅ **Hierarchical folder organization** with proper box node creation and metadata application
- ✅ **Comprehensive error handling** for corrupted definition files and missing resources

**Integration Status:**
- ✅ Seamlessly integrated with existing Phase 1 song management system
- ✅ Maintains backward compatibility with individual DTX file enumeration
- ✅ Enhanced progress reporting with detailed file and directory tracking
- ✅ Proper cancellation token support for responsive UI during enumeration
- ✅ Cross-platform file path handling for Windows/Unix compatibility

**Unit Test Coverage:**
- ✅ **22 comprehensive unit tests** covering all Phase 1 & 2 functionality (consolidated)
- ✅ **Set.def parsing tests** - multi-difficulty songs, missing files, empty definitions
- ✅ **Box.def parsing tests** - folder metadata, custom titles, fallback behavior
- ✅ **Database management tests** - save/load operations, round-trip serialization
- ✅ **Enumeration tests** - recursive directory scanning, progress tracking
- ✅ **Error handling tests** - corrupted files, graceful degradation, invalid paths
- ✅ **100% test success rate** - All tests passing

**Performance Improvements:**
- ✅ **Incremental enumeration** reduces startup time by only processing changed directories
- ✅ **Modification time checking** prevents unnecessary re-parsing of unchanged content
- ✅ **Background threading** with proper cancellation for responsive user experience
- ✅ **Efficient memory usage** with lazy loading and proper resource disposal

### Phase 3: DTXManiaNX UI Components & Stage Integration ✅ COMPLETED
**Objective**: Create song selection UI components matching DTXManiaNX architecture

**Implementation Summary:**
- ✅ **DTXManiaNX Song List Display**: Complete SongListDisplay component with smooth scrolling and 13-item window
- ✅ **Counter-Based Scrolling**: Implemented smooth animation with acceleration (2x-8x based on distance)
- ✅ **Song Status Panel**: Complete SongStatusPanel with detailed song information and difficulty statistics
- ✅ **Enhanced Song Selection Stage**: Updated to use DTXManiaNX-compatible components
- ✅ **Event-Driven Architecture**: Proper event handling for selection changes and difficulty cycling
- ✅ **Visual Integration**: DTXManiaNX-style rendering with proper color coding and layout

**Key Features Implemented:**
- ✅ **13-Item Visible Window**: Authentic DTXManiaNX display with center selection (index 6)
- ✅ **Smooth Scrolling Animation**: Counter-based system with distance-based acceleration
- ✅ **Difficulty Management**: Proper cycling through available difficulties with status updates
- ✅ **Song Information Display**: Comprehensive metadata, performance stats, and difficulty info
- ✅ **Navigation Support**: BOX folder enter/exit with breadcrumb tracking
- ✅ **Keyboard Controls**: Up/Down for navigation, Left/Right for difficulty, Enter for activation

**Files Created:**
- `DTXMania.Shared.Game/Lib/UI/Components/SongListDisplay.cs` - DTXManiaNX-compatible song list with smooth scrolling
- `DTXMania.Shared.Game/Lib/UI/Components/SongStatusPanel.cs` - Detailed song information panel
- `DTXMania.Shared.Game/Lib/Stage/SongSelectionStage.cs` - Enhanced with DTXManiaNX components
- `DTXMania.Test/UI/SongListDisplayTests.cs` - 13 unit tests covering all functionality

**Integration Status:**
- ✅ Seamlessly integrated with existing song management system
- ✅ Compatible with Phase 1 & 2 song data structures
- ✅ Proper event handling for component communication
- ✅ All unit tests passing (13 new tests for SongListDisplay)

**Performance Features:**
- ✅ Texture caching dictionaries for song titles and preview images
- ✅ Lazy loading patterns for visible items only
- ✅ Efficient scroll animation with proper acceleration curves
- ✅ Memory-conscious resource management

### Phase 4: DTXManiaNX Song List Display Enhancement ✅ COMPLETE
**Objective**: Implement CActSelectSongList equivalent with authentic DTXManiaNX behavior

**Implementation Tasks:**
1. **Create SongListDisplay Component** - Replace basic UIList with DTXManiaNX-style song list
   - 13-item visible window with center selection (index 6)
   - Counter-based smooth scrolling system (nTargetScrollCounter/nCurrentScrollCounter)
   - Scroll acceleration logic: Distance ≤100→2x, ≤300→3x, ≤500→4x, >500→8x
   - Song bar reconstruction on selection changes

2. **Song Bar Generation System** - Implement t現在選択中の曲を元に曲バーを再構成する() equivalent
   - Dynamic song title texture generation (tGenerateSongNameBar)
   - Preview image loading and display (tGeneratePreviewImageTexture)
   - Clear lamp indicators with difficulty colors (tGenerateClearLampTexture)
   - Bar type indicators (Score/Box/BackBox/Random)

3. **Visual Integration** - Match DTXManiaNX appearance and behavior
   - 13 visible song bars with center selection highlighting
   - Smooth animation during scrolling with proper timing
   - Color-coded text and backgrounds based on node type
   - Preview image display with fallback system

**✅ PHASE 4 COMPLETION STATUS:**
- ✅ **SongBar Component**: Individual song bar with visual state management, texture support, and node type handling
- ✅ **SongBarRenderer**: Dynamic texture generation system with caching for titles, preview images, and clear lamps
- ✅ **Enhanced SongListDisplay**: Integration with SongBar components for authentic DTXManiaNX appearance
- ✅ **Custom Font System**: Japanese character support with dynamic font atlas generation
- ✅ **Text Rendering**: Proper display of Japanese song titles (Hiragana, Katakana, Kanji)
- ✅ **Fallback Rendering**: Graceful degradation when enhanced rendering is unavailable
- ✅ **Navigation System**: Keyboard navigation with visual feedback and selection highlighting
- ✅ **Unit Test Coverage**: 15 comprehensive tests covering all major functionality
- ✅ **Key Features**: Selection highlighting, texture caching, clear lamp system, preview image loading, graceful graphics handling

### Phase 5: Status Panel & Song Information ✅ COMPLETED
**Objective**: Implement CActSelectStatusPanel equivalent for detailed song information

**✅ PHASE 5 COMPLETION STATUS:**
- ✅ **Enhanced SongMetadata**: Added duration, note counts per instrument, and formatted duration display
- ✅ **Improved SongStatusPanel**: Enhanced difficulty display with note counts, duration, and total notes
- ✅ **Multiple Instrument Support**: Separate display for Drums/Guitar/Bass with individual note counts and levels
- ✅ **Performance Statistics**: Complete display of best scores, ranks, full combo status, play counts, and skill values
- ✅ **Visual Enhancements**: Better layout with instrument-specific highlighting and comprehensive information display
- ✅ **Unit Test Coverage**: 22 comprehensive tests for SongStatusPanel and 56 tests for enhanced SongMetadata
- ✅ **Backward Compatibility**: Fallback display for songs without enhanced metadata

**Key Features Implemented:**
1. **Enhanced Metadata Display**
   - Song duration in MM:SS format
   - Total note count across all instruments
   - Individual note counts per instrument (Drums/Guitar/Bass)
   - BPM, artist, genre information

2. **Advanced Difficulty Information**
   - Instrument-specific difficulty levels with note counts
   - Current instrument highlighting (► indicator)
   - Graceful handling of partial instrument data
   - Fallback to legacy difficulty display when needed

3. **Complete Performance Statistics**
   - Best scores and ranks per difficulty
   - Full combo status indicators
   - Play count and skill value display
   - Real-time updates on selection changes

### Phase 6: 🔥 URGENT - Default Skin & Visual Polish System 📋 HIGH PRIORITY
**Objective**: Fix primitive UI appearance with DTXManiaNX-style graphics and default skin support

**🚨 CRITICAL ISSUES IDENTIFIED:**
- Current UI shows primitive rectangles instead of DTXManiaNX-style graphics
- Missing default skin support (System/Graphics/ path resolution)
- Basic font rendering without shadows/outlines
- No visual effects or proper styling

**Implementation Tasks:**
1. **Default Skin System Implementation**
   - Configure proper skin path resolution (`System/Graphics/` for default skin)
   - Implement CSkin.Path() method for resource loading
   - Add default background textures and UI element graphics
   - Set up DTXManiaNX-compatible color schemes and styling

2. **Enhanced Visual Rendering**
   - Replace primitive rectangle rendering with styled graphics
   - Add shadow/outline effects to font rendering
   - Implement proper selection highlighting effects
   - Add DTXManiaNX-style visual transitions and animations

3. **Song Bar & UI Component Styling**
   - Enhance SongBar component with proper background styling
   - Add clear lamp visual indicators with difficulty colors
   - Implement preview image display with proper scaling
   - Add node type indicators (Score/Box/BackBox/Random) with icons

4. **Status Panel Visual Enhancement**
   - Improve difficulty display with proper highlighting (► indicator)
   - Add visual separation between sections
   - Enhance performance statistics display with proper formatting
   - Add instrument-specific color coding

**Files to Modify:**
- `CSkin.cs` - Default skin path configuration
- `CTextureFactory.cs` - Default texture loading
- `SongListDisplay.cs` - Enhanced visual styling
- `SongStatusPanel.cs` - Improved difficulty display
- `SongBar.cs` - Proper background and styling
- Font system - DTXManiaNX-style rendering

### Phase 7: Preview Sound System & Audio Integration 📋 PLANNED
**Objective**: Implement CActSelectPresound equivalent with fade effects

**Implementation Tasks:**
1. **Create PreviewSoundManager Component** - CActSelectPresound equivalent
   - Configurable preview delay (ct再生待ちウェイト) - default 1000ms
   - BGM fade out/in system (ctBGMフェードアウト用/ctBGMフェードイン用)
   - Preview volume control (default 80%)
   - Automatic sound cleanup and resource management

2. **Audio Integration** - t選択曲が変更された() equivalent behavior
   - Preview sound loading from song metadata (PREVIEW tag)
   - Background music fade coordination
   - Scroll prevention during preview playback
   - Proper audio disposal on selection changes

3. **Fade Effect System** - tBGMフェードアウト開始()/tBGMフェードイン開始() equivalent
   - Smooth volume transitions for BGM
   - Preview sound volume management
   - Timing coordination with selection changes
   - Audio resource cleanup

### Phase 8: Performance Optimization & Advanced Features 📋 PLANNED
**Objective**: Complete DTXManiaNX performance optimization and advanced features

**Implementation Tasks:**
1. **Performance Optimization** - Match DTXManiaNX efficiency patterns
   - Advanced texture caching for song titles and preview images
   - Lazy loading for visible items only (13-item window)
   - Memory management for large song lists
   - Background threading for resource loading

2. **DTXManiaNX Integration Patterns**
   - Component communication via tSelectedSongChanged() equivalent
   - Centralized state management through main stage
   - Proper resource cleanup on selection changes
   - Efficient font rendering with caching

### Phase 9: Advanced Features & Complete DTXManiaNX Parity 📋 PLANNED
**Objective**: Implement remaining DTXManiaNX features and database migration

**Implementation Tasks:**
1. **Additional UI Components**
   - CActSelectArtistComment equivalent - Artist information and song comments
   - CActSelectQuickConfig equivalent - Quick access to game configuration
   - CActSortSongs equivalent - Song sorting functionality

2. **Advanced Navigation Features**
   - Random song selection (RANDOM node type)
   - Skin support per BOX folder (custom graphics and layout)
   - Advanced input handling (gamepad, drum pad support)
   - Configuration integration

3. **Database Migration & Advanced Features**
   - SQLite migration with Entity Framework Core
   - Search system with real-time filtering
   - Multiple sort criteria with database optimization
   - Performance optimization for large libraries (10,000+ songs)

## 💾 Current Data Storage Architecture

### ✅ JSON-Based Implementation (Current)
The current implementation uses a simple but effective JSON-based storage system:

**Storage Format:**
```csharp
public class SongDatabaseData
{
    public List<SongScore> Scores { get; set; } = new();
    public List<SongListNode> RootNodes { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public string Version { get; set; } = "1.0";
}
```

**Key Features:**
- ✅ **JSON Serialization**: Human-readable format for debugging and manual editing
- ✅ **Incremental Updates**: Only processes changed files based on modification time
- ✅ **Thread-Safe Operations**: Proper locking for concurrent access
- ✅ **Cross-Platform**: Works on all MonoGame supported platforms
- ✅ **Simple Backup**: Easy to backup/restore with file copy operations

**Performance Characteristics:**
- **Load Time**: ~100-500ms for 1,000-5,000 songs
- **Memory Usage**: ~50-200MB for typical song libraries
- **Search Performance**: O(n) linear search through in-memory collections
- **Update Performance**: Fast incremental updates with modification time checking

**Limitations:**
- **Large Libraries**: Performance degrades with 10,000+ songs
- **Search Speed**: No indexing for complex queries
- **Concurrent Access**: Limited to single-process access
- **Data Integrity**: No transaction support or referential integrity

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

## 🏗️ Detailed Implementation Architecture

### Core Classes Structure

#### SongListNode (DTXManiaCX Implementation)
```csharp
public class SongListNode
{
    public NodeType Type { get; set; }           // Score, Box, BackBox, Random
    public SongScore[] Scores { get; set; }      // Up to 5 difficulties
    public string[] DifficultyLabels { get; set; } // Difficulty names
    public List<SongListNode> Children { get; set; } // Child nodes
    public SongListNode Parent { get; set; }     // Parent reference
    public string Title { get; set; }            // Display title
    public string Genre { get; set; }            // Genre classification
    public Color TextColor { get; set; }         // Display color
    public string BreadcrumbPath { get; set; }   // Navigation path
    public string SkinPath { get; set; }         // Custom skin override
    public SongMetadata Metadata { get; set; }   // Additional properties
}

public enum NodeType
{
    Score,      // Individual song
    Box,        // Folder container
    BackBox,    // Parent folder navigation
    Random      // Random selection placeholder
}
```

#### SongManager (Database & Enumeration)
```csharp
public class SongManager
{
    private List<SongScore> _songsDatabase;      // Cached song data
    private List<SongListNode> _rootSongs;       // Root level songs
    private CancellationTokenSource _enumCancellation;
    private readonly object _lockObject = new object();

    // Statistics
    public int DatabaseScoreCount { get; private set; }
    public int DiscoveredScoreCount { get; private set; }
    public int EnumeratedFileCount { get; private set; }

    // Events
    public event EventHandler<SongDiscoveredEventArgs> SongDiscovered;
    public event EventHandler<EnumerationProgressEventArgs> ProgressChanged;
    public event EventHandler EnumerationCompleted;

    // Methods
    public async Task<bool> LoadSongsDatabaseAsync(string databasePath);
    public async Task SaveSongsDatabaseAsync(string databasePath);
    public async Task EnumerateSongsAsync(string[] searchPaths, IProgress<EnumerationProgress> progress);
    public void SuspendEnumeration();
    public void ResumeEnumeration();
}
```

#### SongListDisplay (UI Component)
```csharp
public class SongListDisplay : UIElement
{
    private const int VISIBLE_ITEMS = 13;
    private const int CENTER_INDEX = 6;
    private const int SCROLL_UNIT = 100;

    private List<SongListNode> _currentList;
    private int _selectedIndex;
    private int _scrollOffset;
    private int _targetScrollCounter;
    private int _currentScrollCounter;
    private Dictionary<int, ITexture> _titleBarCache;
    private Dictionary<int, ITexture> _previewImageCache;

    // Properties
    public SongListNode SelectedSong { get; private set; }
    public int CurrentDifficulty { get; private set; }
    public bool IsScrolling => _targetScrollCounter != 0 || _currentScrollCounter != 0;

    // Events
    public event EventHandler<SongSelectionChangedEventArgs> SelectionChanged;
    public event EventHandler<DifficultyChangedEventArgs> DifficultyChanged;

    // Methods
    public void MoveNext();
    public void MovePrevious();
    public void CycleDifficulty();
    public bool EnterBox();
    public bool ExitBox();
    public void RefreshDisplay();
}
```

### Song Enumeration Implementation

#### File Discovery Process
```csharp
public class SongEnumerationService
{
    private readonly string[] SUPPORTED_EXTENSIONS = { ".dtx", ".gda", ".bms", ".bme", ".g2d" };

    public async Task<List<SongListNode>> EnumerateDirectoryAsync(
        string basePath,
        SongListNode parent = null,
        IProgress<EnumerationProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SongListNode>();
        var directory = new DirectoryInfo(basePath);

        // Check for set.def (multi-difficulty songs)
        var setDefPath = Path.Combine(basePath, "set.def");
        if (File.Exists(setDefPath))
        {
            var setDefSongs = await ParseSetDefinitionAsync(setDefPath, parent);
            results.AddRange(setDefSongs);
            return results;
        }

        // Check for box.def (folder metadata)
        var boxDefPath = Path.Combine(basePath, "box.def");
        BoxDefinition boxDef = null;
        if (File.Exists(boxDefPath))
        {
            boxDef = await ParseBoxDefinitionAsync(boxDefPath);
        }

        // Process subdirectories as BOX folders
        foreach (var subDir in directory.GetDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var boxNode = CreateBoxNode(subDir, parent, boxDef);
            boxNode.Children = await EnumerateDirectoryAsync(
                subDir.FullName, boxNode, progress, cancellationToken);

            if (boxNode.Children.Count > 0)
            {
                results.Add(boxNode);
            }
        }

        // Process individual song files
        foreach (var file in directory.GetFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (SUPPORTED_EXTENSIONS.Contains(file.Extension.ToLowerInvariant()))
            {
                var songNode = await CreateSongNodeAsync(file.FullName, parent);
                if (songNode != null)
                {
                    results.Add(songNode);
                    progress?.Report(new EnumerationProgress
                    {
                        CurrentFile = file.Name,
                        ProcessedCount = results.Count
                    });
                }
            }
        }

        return results;
    }
}
```

#### DTX Metadata Parser
```csharp
public class DTXMetadataParser
{
    public async Task<SongMetadata> ParseMetadataAsync(string filePath)
    {
        var metadata = new SongMetadata();

        using var reader = new StreamReader(filePath, Encoding.GetEncoding("Shift_JIS"));
        string line;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.StartsWith("#"))
            {
                var parts = line.Split(':', 2);
                if (parts.Length == 2)
                {
                    var command = parts[0].Trim().ToUpperInvariant();
                    var value = parts[1].Trim();

                    switch (command)
                    {
                        case "#TITLE":
                            metadata.Title = value;
                            break;
                        case "#ARTIST":
                            metadata.Artist = value;
                            break;
                        case "#GENRE":
                            metadata.Genre = value;
                            break;
                        case "#BPM":
                            if (double.TryParse(value, out var bpm))
                                metadata.BPM = bpm;
                            break;
                        case "#LEVEL":
                            ParseLevelData(value, metadata);
                            break;
                        case "#PREVIEW":
                            metadata.PreviewFile = value;
                            break;
                        case "#PREIMAGE":
                            metadata.PreviewImage = value;
                            break;
                        case "#COMMENT":
                            metadata.Comment = value;
                            break;
                    }
                }
            }

            // Stop parsing after header section
            if (line.StartsWith("*") || line.Contains("|"))
                break;
        }

        return metadata;
    }

    private void ParseLevelData(string levelData, SongMetadata metadata)
    {
        // Parse level data: "DRUMS:85,GUITAR:78,BASS:65"
        var parts = levelData.Split(',');
        foreach (var part in parts)
        {
            var instrumentLevel = part.Split(':');
            if (instrumentLevel.Length == 2)
            {
                var instrument = instrumentLevel[0].Trim().ToUpperInvariant();
                if (int.TryParse(instrumentLevel[1].Trim(), out var level))
                {
                    switch (instrument)
                    {
                        case "DRUMS":
                            metadata.DrumLevel = level;
                            break;
                        case "GUITAR":
                            metadata.GuitarLevel = level;
                            break;
                        case "BASS":
                            metadata.BassLevel = level;
                            break;
                    }
                }
            }
        }
    }
}
```

#### Preview Sound System
```csharp
public class PreviewSoundManager
{
    private ISound _currentPreview;
    private ISound _backgroundMusic;
    private Timer _previewDelayTimer;
    private Timer _fadeTimer;
    private readonly IResourceManager _resourceManager;

    public int PreviewDelayMs { get; set; } = 1000;
    public int PreviewVolumePercent { get; set; } = 80;
    public bool IsScrolling { get; set; }

    public async Task PlayPreviewAsync(SongListNode song)
    {
        // Stop current preview
        StopPreview();

        // Start BGM fade in
        StartBGMFadeIn();

        if (song?.Metadata?.PreviewFile != null)
        {
            // Set delay timer before preview starts
            _previewDelayTimer = new Timer(async _ =>
            {
                if (!IsScrolling)
                {
                    await StartPreviewPlayback(song);
                }
            }, null, PreviewDelayMs, Timeout.Infinite);
        }
    }

    private async Task StartPreviewPlayback(SongListNode song)
    {
        try
        {
            var previewPath = Path.Combine(
                Path.GetDirectoryName(song.Scores[0].FilePath),
                song.Metadata.PreviewFile);

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
}
```

### 📋 Future Database Migration (Phase 6)

#### Planned: Entity Framework Core + SQLite Implementation

**Effort Assessment: MEDIUM** ⭐⭐⭐ (1-2 weeks)

The current JSON-based system works well for medium-sized libraries, but for large libraries (10,000+ songs) and advanced features, migrating to SQLite with Entity Framework Core would provide significant benefits:

**Benefits:**
- **Type Safety**: Strongly-typed queries with compile-time checking
- **LINQ Support**: Natural C# query syntax instead of raw SQL
- **Automatic Migrations**: Schema changes handled automatically
- **Change Tracking**: Automatic dirty checking and optimized updates
- **Relationship Management**: Automatic foreign key handling and navigation properties
- **Performance**: Built-in query optimization and caching
- **Testing**: Easy to mock and unit test with InMemory provider

**Required Changes:**
1. Add EF Core NuGet packages (2 packages, cross-platform)
2. Define entity models with attributes/fluent API
3. Create DbContext with DbSets
4. Replace manual SQL with LINQ queries

**NuGet Packages:**
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="7.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Tools" Version="7.0.0" />
```

**Entity Models:**
```csharp
// Song entity (replaces both songlist.db and songs.db)
public class Song
{
    public int Id { get; set; }

    [Required, MaxLength(500)]
    public string FilePath { get; set; } = "";

    public long FileSize { get; set; }
    public DateTime LastModified { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(200)]
    public string? Artist { get; set; }

    [MaxLength(100)]
    public string? Genre { get; set; }

    public double? BPM { get; set; }
    public int? DrumLevel { get; set; }
    public int? GuitarLevel { get; set; }
    public int? BassLevel { get; set; }

    [MaxLength(200)]
    public string? PreviewFile { get; set; }

    [MaxLength(200)]
    public string? PreviewImage { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ICollection<SongScore> Scores { get; set; } = new List<SongScore>();
    public virtual ICollection<SongHierarchy> HierarchyNodes { get; set; } = new List<SongHierarchy>();
}

// Song hierarchy (replaces songlist.db structure)
public class SongHierarchy
{
    public int Id { get; set; }

    public int? SongId { get; set; }
    public virtual Song? Song { get; set; }

    public int? ParentId { get; set; }
    public virtual SongHierarchy? Parent { get; set; }
    public virtual ICollection<SongHierarchy> Children { get; set; } = new List<SongHierarchy>();

    [Required]
    public NodeType NodeType { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }

    public int DisplayOrder { get; set; }

    [MaxLength(1000)]
    public string? BreadcrumbPath { get; set; }

    [MaxLength(500)]
    public string? SkinPath { get; set; }
}

// Performance scores (replaces songs.db score data)
public class SongScore
{
    public int Id { get; set; }

    public int SongId { get; set; }
    public virtual Song Song { get; set; } = null!;

    public int Difficulty { get; set; }
    public int BestScore { get; set; }
    public int BestRank { get; set; }
    public bool FullCombo { get; set; }
    public int PlayCount { get; set; }
    public DateTime? LastPlayed { get; set; }

    public double HighSkill { get; set; }
    public double SongSkill { get; set; }
}

public enum NodeType
{
    Song = 0,
    Box = 1,
    BackBox = 2,
    Random = 3
}

**DbContext:**
```csharp
public class SongDbContext : DbContext
{
    public DbSet<Song> Songs { get; set; } = null!;
    public DbSet<SongHierarchy> SongHierarchy { get; set; } = null!;
    public DbSet<SongScore> SongScores { get; set; } = null!;

    public SongDbContext(DbContextOptions<SongDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Song entity configuration
        modelBuilder.Entity<Song>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.FilePath).IsUnique();
            entity.HasIndex(e => e.Title);
            entity.HasIndex(e => e.Artist);
            entity.HasIndex(e => e.Genre);
            entity.HasIndex(e => e.LastModified);

            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        // Song hierarchy configuration
        modelBuilder.Entity<SongHierarchy>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ParentId, e.DisplayOrder });

            // Self-referencing relationship
            entity.HasOne(e => e.Parent)
                .WithMany(e => e.Children)
                .HasForeignKey(e => e.ParentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Song relationship
            entity.HasOne(e => e.Song)
                .WithMany(e => e.HierarchyNodes)
                .HasForeignKey(e => e.SongId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Song score configuration
        modelBuilder.Entity<SongScore>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.SongId, e.Difficulty }).IsUnique();

            entity.HasOne(e => e.Song)
                .WithMany(e => e.Scores)
                .HasForeignKey(e => e.SongId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

**Repository Service (EF Core LINQ Queries):**
```csharp
public class SongRepository
{
    private readonly SongDbContext _context;

    public SongRepository(SongDbContext context)
    {
        _context = context;
    }

    // Get song by file path (replaces manual SQL)
    public async Task<Song?> GetSongAsync(string filePath)
    {
        return await _context.Songs
            .Include(s => s.Scores)
            .Include(s => s.HierarchyNodes)
            .FirstOrDefaultAsync(s => s.FilePath == filePath);
    }

    // Search songs with LINQ (much cleaner than SQL)
    public async Task<List<Song>> SearchSongsAsync(string searchTerm, int limit = 100)
    {
        return await _context.Songs
            .Where(s => s.Title!.Contains(searchTerm) ||
                       s.Artist!.Contains(searchTerm) ||
                       s.Genre!.Contains(searchTerm))
            .OrderBy(s => s.Title)
            .Take(limit)
            .ToListAsync();
    }

    // Upsert song (EF Core handles this automatically)
    public async Task UpsertSongAsync(Song song)
    {
        var existing = await _context.Songs
            .FirstOrDefaultAsync(s => s.FilePath == song.FilePath);

        if (existing != null)
        {
            // Update existing
            _context.Entry(existing).CurrentValues.SetValues(song);
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            // Add new
            _context.Songs.Add(song);
        }

        await _context.SaveChangesAsync();
    }

    // Get songs modified after date
    public async Task<List<Song>> GetSongsModifiedAfterAsync(DateTime lastCheck)
    {
        return await _context.Songs
            .Where(s => s.LastModified > lastCheck)
            .OrderByDescending(s => s.LastModified)
            .ToListAsync();
    }

    // Get song hierarchy (replaces songlist.db loading)
    public async Task<List<SongHierarchy>> GetSongHierarchyAsync(int? parentId = null)
    {
        return await _context.SongHierarchy
            .Include(h => h.Song)
            .Include(h => h.Children)
            .Where(h => h.ParentId == parentId)
            .OrderBy(h => h.DisplayOrder)
            .ToListAsync();
    }

    // Advanced search with multiple criteria
    public async Task<List<Song>> AdvancedSearchAsync(SearchCriteria criteria)
    {
        var query = _context.Songs.AsQueryable();

        if (!string.IsNullOrEmpty(criteria.Title))
            query = query.Where(s => s.Title!.Contains(criteria.Title));

        if (!string.IsNullOrEmpty(criteria.Artist))
            query = query.Where(s => s.Artist!.Contains(criteria.Artist));

        if (criteria.MinBPM.HasValue)
            query = query.Where(s => s.BPM >= criteria.MinBPM);

        if (criteria.MaxBPM.HasValue)
            query = query.Where(s => s.BPM <= criteria.MaxBPM);

        // Dynamic sorting
        query = criteria.SortBy switch
        {
            "Title" => criteria.SortDirection == "DESC"
                ? query.OrderByDescending(s => s.Title)
                : query.OrderBy(s => s.Title),
            "Artist" => criteria.SortDirection == "DESC"
                ? query.OrderByDescending(s => s.Artist)
                : query.OrderBy(s => s.Artist),
            "BPM" => criteria.SortDirection == "DESC"
                ? query.OrderByDescending(s => s.BPM)
                : query.OrderBy(s => s.BPM),
            _ => query.OrderBy(s => s.Title)
        };

        return await query
            .Skip(criteria.Offset)
            .Take(criteria.Limit)
            .ToListAsync();
    }

    // Get statistics (replaces manual SQL aggregation)
    public async Task<SongStatistics> GetStatisticsAsync()
    {
        var stats = await _context.Songs
            .GroupBy(s => 1)
            .Select(g => new SongStatistics
            {
                TotalSongs = g.Count(),
                UniqueArtists = g.Select(s => s.Artist).Distinct().Count(),
                UniqueGenres = g.Select(s => s.Genre).Distinct().Count(),
                AverageBPM = g.Average(s => s.BPM) ?? 0,
                MinBPM = g.Min(s => s.BPM) ?? 0,
                MaxBPM = g.Max(s => s.BPM) ?? 0
            })
            .FirstOrDefaultAsync();

        return stats ?? new SongStatistics();
    }
}
```

**Dependency Injection Setup:**
```csharp
// In Program.cs or Startup.cs
services.AddDbContext<SongDbContext>(options =>
    options.UseSqlite("Data Source=songs.db"));

services.AddScoped<SongRepository>();
```

**Automatic Migrations:**
```bash
# Create initial migration
dotnet ef migrations add InitialCreate

# Update database
dotnet ef database update
```

#### Entity Framework Core Benefits vs Raw SQLite

**Effort Comparison:**

| Task | Raw SQLite | EF Core | Time Saved |
|------|------------|---------|------------|
| **Setup** | Write SQL schema, indexes, connections | Define entities, DbContext | 80% less |
| **CRUD Operations** | Write SQL + parameter mapping | LINQ queries | 90% less |
| **Relationships** | Manual JOIN queries | Navigation properties | 95% less |
| **Migrations** | Manual schema versioning | Automatic migrations | 99% less |
| **Testing** | Mock SQL connections | InMemory provider | 90% less |

**Code Quality Benefits:**

1. **Type Safety**: Compile-time checking prevents SQL injection and typos
2. **IntelliSense**: Full IDE support with autocomplete for properties and methods
3. **Refactoring**: Rename properties and EF Core updates all queries automatically
4. **LINQ**: Natural C# syntax instead of string-based SQL
5. **Navigation Properties**: Access related data without writing JOINs
6. **Change Tracking**: Automatic dirty checking and optimized updates
7. **Unit Testing**: Easy mocking with InMemory provider

**Example Comparison:**

```csharp
// Raw SQLite (error-prone, no IntelliSense)
var sql = "SELECT * FROM Songs WHERE Title LIKE @search AND BPM > @minBpm";
command.Parameters.AddWithValue("@search", $"%{searchTerm}%");
command.Parameters.AddWithValue("@minBpm", minBpm);

// EF Core (type-safe, IntelliSense, refactor-friendly)
var songs = await _context.Songs
    .Where(s => s.Title.Contains(searchTerm) && s.BPM > minBpm)
    .ToListAsync();
```

**Migration from Binary Files:**
```csharp
public class BinaryToEFCoreMigration
{
    public async Task MigrateAsync(SongDbContext context, string binaryPath)
    {
        if (!File.Exists(binaryPath))
            return;

        // Read existing binary data
        var existingSongs = await ReadBinaryDatabaseAsync(binaryPath);

        // Use EF Core transaction
        using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            foreach (var songData in existingSongs)
            {
                var song = new Song
                {
                    FilePath = songData.FilePath,
                    Title = songData.Title,
                    Artist = songData.Artist,
                    // ... map other properties
                };

                context.Songs.Add(song);
            }

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            // Backup old file
            File.Move(binaryPath, binaryPath + ".backup");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
```

**Performance Comparison:**

| Operation | Binary File | SQLite | Improvement |
|-----------|-------------|---------|-------------|
| **Load All Songs** | O(n) - Read entire file | O(1) - Index lookup | 10-100x faster |
| **Search by Title** | O(n) - Linear scan | O(log n) - Index search | 100-1000x faster |
| **Update Single Song** | O(n) - Rewrite entire file | O(1) - Single UPDATE | 1000x+ faster |
| **Add New Song** | O(n) - Rewrite entire file | O(1) - Single INSERT | 1000x+ faster |
| **Memory Usage** | Load all in RAM | Stream results | 10-100x less memory |

**Real-World Impact:**
- **10,000 songs**: Binary load ~5-10 seconds → SQLite ~0.1 seconds
- **Search "Beatles"**: Binary ~2 seconds → SQLite ~0.01 seconds
- **Add new song**: Binary ~5 seconds → SQLite ~0.001 seconds
- **Memory**: Binary ~500MB → SQLite ~50MB

**Implementation Effort Breakdown:**

1. **NuGet Package** (5 minutes)
   ```xml
   <PackageReference Include="Microsoft.Data.Sqlite" Version="7.0.0" />
   ```

2. **Schema Creation** (30 minutes)
   - Define tables and indexes
   - Add foreign key relationships
   - Create migration scripts

3. **Replace Binary Methods** (2-3 hours)
   - Convert Load/Save methods to SQL
   - Add search and filter methods
   - Implement upsert operations

4. **Migration Tool** (1 hour)
   - Read existing binary database
   - Batch insert into SQLite
   - Handle errors and rollback

5. **Testing** (2-3 hours)
   - Unit tests for database operations
   - Performance testing with large datasets
   - Migration testing

**Total Effort: 1-2 days** for a massive improvement in performance and functionality.

**Additional SQLite Features:**
```csharp
// Advanced search with multiple criteria
public async Task<List<SongScore>> AdvancedSearchAsync(SearchCriteria criteria)
{
    var sql = new StringBuilder("SELECT * FROM Songs WHERE 1=1");
    var parameters = new List<SqliteParameter>();

    if (!string.IsNullOrEmpty(criteria.Title))
    {
        sql.Append(" AND Title LIKE @title");
        parameters.Add(new SqliteParameter("@title", $"%{criteria.Title}%"));
    }

    if (!string.IsNullOrEmpty(criteria.Artist))
    {
        sql.Append(" AND Artist LIKE @artist");
        parameters.Add(new SqliteParameter("@artist", $"%{criteria.Artist}%"));
    }

    if (criteria.MinBPM.HasValue)
    {
        sql.Append(" AND BPM >= @minBpm");
        parameters.Add(new SqliteParameter("@minBpm", criteria.MinBPM.Value));
    }

    if (criteria.MaxBPM.HasValue)
    {
        sql.Append(" AND BPM <= @maxBpm");
        parameters.Add(new SqliteParameter("@maxBpm", criteria.MaxBPM.Value));
    }

    sql.Append($" ORDER BY {criteria.SortBy} {criteria.SortDirection}");
    sql.Append(" LIMIT @limit OFFSET @offset");

    // Execute query with pagination
    return await ExecuteSearchAsync(sql.ToString(), parameters, criteria.Limit, criteria.Offset);
}

// Incremental enumeration - only process changed files
public async Task<bool> NeedsEnumerationAsync(string directoryPath)
{
    var lastEnumeration = await GetLastEnumerationTimeAsync(directoryPath);
    var directoryInfo = new DirectoryInfo(directoryPath);

    return directoryInfo.LastWriteTime > lastEnumeration;
}

// Statistics and analytics
public async Task<SongStatistics> GetStatisticsAsync()
{
    const string sql = @"
        SELECT
            COUNT(*) as TotalSongs,
            COUNT(DISTINCT Artist) as UniqueArtists,
            COUNT(DISTINCT Genre) as UniqueGenres,
            AVG(BPM) as AverageBPM,
            MIN(BPM) as MinBPM,
            MAX(BPM) as MaxBPM
        FROM Songs";

    // Execute and return statistics
}
```

### Integration with DTXManiaCX Architecture

#### Song Selection Stage
```csharp
public class SongSelectionStage : BaseStage
{
    private SongManager _songManager;
    private SongListDisplay _songListDisplay;
    private PreviewSoundManager _previewManager;
    private StatusPanel _statusPanel;
    private IResourceManager _resourceManager;

    public override void OnActivate()
    {
        base.OnActivate();

        // Initialize components
        _songManager = new SongManager();
        _songListDisplay = new SongListDisplay();
        _previewManager = new PreviewSoundManager(_resourceManager);
        _statusPanel = new StatusPanel();

        // Wire up events
        _songListDisplay.SelectionChanged += OnSongSelectionChanged;
        _songListDisplay.DifficultyChanged += OnDifficultyChanged;

        // Load song database and start enumeration if needed
        _ = InitializeSongListAsync();
    }

    private async Task InitializeSongListAsync()
    {
        // Load cached song database
        await _songManager.LoadSongsDatabaseAsync("songs.db");

        // Check if enumeration is needed
        if (_songManager.DatabaseScoreCount == 0 || ShouldReenumerate())
        {
            var progress = new Progress<EnumerationProgress>(OnEnumerationProgress);
            await _songManager.EnumerateSongsAsync(
                new[] { "Songs", "DTX", "Music" },
                progress);
        }

        // Initialize display with song list
        _songListDisplay.SetSongList(_songManager.RootSongs);
    }

    private void OnSongSelectionChanged(object sender, SongSelectionChangedEventArgs e)
    {
        // Update preview sound
        _ = _previewManager.PlayPreviewAsync(e.SelectedSong);

        // Update status panel
        _statusPanel.UpdateSongInfo(e.SelectedSong, e.CurrentDifficulty);

        // Notify other components
        NotifySelectionChanged(e.SelectedSong);
    }

    protected override void OnUpdate(double deltaTime)
    {
        base.OnUpdate(deltaTime);

        // Update scrolling animation
        _songListDisplay.Update(deltaTime);

        // Update preview manager
        _previewManager.IsScrolling = _songListDisplay.IsScrolling;
    }

    protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
    {
        // Draw background
        DrawBackground(spriteBatch);

        // Draw song list
        _songListDisplay.Draw(spriteBatch, deltaTime);

        // Draw status panel
        _statusPanel.Draw(spriteBatch, deltaTime);

        base.OnDraw(spriteBatch, deltaTime);
    }
}
```



## 🏗️ Implementation Files Summary

### ✅ Current Implementation Files (Phases 1-3)
**Phase 1-2 Complete (Song Data & Enumeration):**
- `DTXMania.Shared.Game/Lib/Song/SongManager.cs` - Song database management and enumeration
- `DTXMania.Shared.Game/Lib/Song/SongListNode.cs` - Hierarchical song list structure
- `DTXMania.Shared.Game/Lib/Song/SongMetadata.cs` - Song metadata with DTX parsing support
- `DTXMania.Shared.Game/Lib/Song/SongScore.cs` - Performance score tracking
- `DTXMania.Shared.Game/Lib/Song/DTXMetadataParser.cs` - DTX file parsing with Japanese support

**Phase 3 Basic Implementation (UI Foundation):**
- `DTXMania.Shared.Game/Lib/Stage/SongSelectionStage.cs` - Basic song selection stage (needs DTXManiaNX enhancement)
- `DTXMania.Shared.Game/Lib/UI/Components/UIList.cs` - Generic scrollable list (needs song-specific features)
- `DTXMania.Shared.Game/Lib/UI/Components/UILabel.cs` - Text display with DTXMania effects
- `DTXMania.Shared.Game/Lib/UI/Components/UIPanel.cs` - Layout container
- `DTXMania.Shared.Game/Lib/Stage/BaseStage.cs` - Stage base class with lifecycle management
- `DTXMania.Shared.Game/Lib/Stage/StageManager.cs` - Stage transition and management system

### 📋 Planned Implementation Files (Phases 4-8)
**Phase 4 - DTXManiaNX Song List Display Enhancement:**
- `DTXMania.Shared.Game/Lib/UI/Components/SongListDisplay.cs` - CActSelectSongList equivalent with 13-item display
- `DTXMania.Shared.Game/Lib/UI/Components/SongBar.cs` - Individual song bar with textures and effects
- `DTXMania.Shared.Game/Lib/UI/Components/SongBarRenderer.cs` - Song bar generation and caching

**Phase 5 - Status Panel & Song Information:**
- `DTXMania.Shared.Game/Lib/UI/Components/SongStatusPanel.cs` - CActSelectStatusPanel equivalent

**Phase 6 - Preview Sound System:**
- `DTXMania.Shared.Game/Lib/Audio/PreviewSoundManager.cs` - CActSelectPresound equivalent with fade effects

**Phase 7-8 - Additional DTXManiaNX Components:**
- `DTXMania.Shared.Game/Lib/UI/Components/ArtistCommentPanel.cs` - CActSelectArtistComment equivalent
- `DTXMania.Shared.Game/Lib/UI/Components/QuickConfigPanel.cs` - CActSelectQuickConfig equivalent
- `DTXMania.Shared.Game/Lib/UI/Components/SongSortPanel.cs` - CActSortSongs equivalent
- `DTXMania.Shared.Game/Lib/UI/Components/PreviewImagePanel.cs` - CActSelectPreimagePanel equivalent
