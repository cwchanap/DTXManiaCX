# DTXMania Title/Menu Structure Analysis

## Overview

This document provides a comprehensive analysis of DTXMania's title and menu structure based on examination of the original DTXManiaNX source code. This analysis covers startup sequences, resource loading, menu navigation patterns, transition effects, input handling, and background animations.

## 📋 Analysis Summary

### Key Files Examined
- **CStageタイトル.cs** - Main title stage implementation
- **CEnumSongs.cs** - Song enumeration and database management
- **CActEnumSongs.cs** - Loading screen with progress indication
- **CActSelectPopupMenu.cs** - Popup menu framework
- **CActSelectQuickConfig.cs** - Quick configuration menu
- **CStageSongSelection.cs** - Song selection stage patterns

## 🚀 Startup Sequence & Resource Loading

### Phase-Based Startup Process
DTXMania uses a sophisticated multi-phase startup system:

1. **Phase 0: System Sound Construction** (`起動0_システムサウンドを構築`)
   - Loads non-BGM system sounds first
   - Plays startup BGM (`bgm起動画面.tPlay()`)
   - Handles compact mode exclusions

2. **Phase 1: songlist.db Loading** (`起動00_songlistから曲リストを作成する`)
   - Deserializes cached song list from binary format
   - Skips on first run or version changes
   - Background thread operation with progress tracking

3. **Phase 2: songs.db Loading** (`起動1_SongsDBからスコアキャッシュを構築`)
   - Loads score cache database
   - Handles file corruption gracefully
   - Version-aware loading logic

4. **Phase 3-7: Song Enumeration Process**
   - **Phase 3**: Disk-based song searching (`曲を検索してリストを作成する`)
   - **Phase 4**: Score cache reflection (`スコアキャッシュをリストに反映する`)
   - **Phase 5**: File-based song data loading (`ファイルから読み込んで反映する`)
   - **Phase 6**: Post-processing (`曲リストへ後処理を適用する`)
   - **Phase 7**: Database saving (`スコアキャッシュをSongsDBに出力する`)

### Resource Loading Patterns
```csharp
// Background texture loading with fallback
this.tx背景 = CDTXMania.tGenerateTexture(CSkin.Path(@"Graphics\2_background.jpg"), false);

// Menu texture loading
this.txメニュー = CDTXMania.tGenerateTexture(CSkin.Path(@"Graphics\2_menu.png"), false);

// Version display
CDTXMania.actDisplayString.tPrint(2, 2, CCharacterConsole.EFontType.White, CDTXMania.VERSION_DISPLAY);
```

## 🎮 Menu Item Structure & Navigation

### Title Screen Menu Layout
DTXMania's title screen uses a vertical menu with these standard items:
1. **GAME START** - Transitions to song selection
2. **CONFIG** - Opens configuration menu
3. **EXIT** - Exits the application

### Menu Navigation Patterns
```csharp
// Menu constants from CStageタイトル.cs
private const int MENU_H = 0x27;    // Menu item height (39px)
private const int MENU_W = 0xe3;    // Menu item width (227px)
private const int MENU_X = 0x1fa;   // Menu X position (506px)
private const int MENU_Y = 0x201;   // Menu Y position (513px)

// Cursor movement with animation
private void tカーソルを上へ移動する()
{
    if (this.n現在のカーソル行 != (int)E戻り値.GAMESTART - 1)
    {
        CDTXMania.Skin.soundCursorMovement.tPlay();
        this.n現在のカーソル行--;
        this.ct上移動用.tStart(0, 100, 1, CDTXMania.Timer);
    }
}
```

### Input Handling System
DTXMania uses a sophisticated input system supporting multiple input methods:

**Keyboard Controls:**
- Arrow Keys: Menu navigation
- Enter: Selection confirmation
- Escape: Exit/Cancel

**Drum Pad Controls:**
- HT (Hi-Tom): Move up
- LT (Low-Tom): Move down
- CY/RD (Cymbal/Ride): Confirm selection
- LC (Left Crash): Cancel

**Guitar/Bass Controls:**
- R/G buttons: Navigation
- Pick button: Confirm

## 🎨 Transition Effects & Animations

### Fade System
DTXMania implements a comprehensive fade system using `CActFIFO` classes:

```csharp
// Fade phases in title stage
public enum EPhase
{
    Common_FadeIn,
    Common_DefaultState,
    Common_FadeOut,
    タイトル_起動画面からのフェードイン  // Special fade from startup
}

// Fade transition logic
if (CDTXMania.rPreviousStage == CDTXMania.stageStartup)
{
    this.actFIfromSetup.tフェードイン開始();
    base.ePhaseID = CStage.EPhase.タイトル_起動画面からのフェードイン;
}
```

### Cursor Animation Effects
The title screen features animated cursor effects:

1. **Flash Animation**: Cursor pulses with sine wave intensity
2. **Movement Animation**: Smooth transitions between menu items
3. **Scale Effects**: Cursor scaling during selection

```csharp
// Cursor flash calculation
float flashIntensity = (float)(0.5 + 0.5 * Math.Sin(_cursorFlashTimer * Math.PI * 2 / 0.7));
Color cursorColor = Color.White * flashIntensity;

// Menu item scaling during selection
float nMag = (float)(1.0 + (((double)this.ctカーソルフラッシュ用.nCurrentValue) / 100.0) * 0.5);
this.txメニュー.vcScaleRatio.X = nMag;
this.txメニュー.vcScaleRatio.Y = nMag;
```

## 📱 "Press Any Key" Detection

### Input Detection System
DTXMania implements comprehensive input detection across all supported devices:

```csharp
// Multi-device input checking
if ((CDTXMania.Pad.bPressedDGB(EPad.CY) ||
     CDTXMania.Pad.bPressed(EInstrumentPart.DRUMS, EPad.RD)) ||
    (CDTXMania.ConfigIni.bEnterがキー割り当てのどこにも使用されていない &&
     CDTXMania.InputManager.Keyboard.bKeyPressed((int)SlimDXKey.Return)))
{
    // Handle selection
    if ((this.n現在のカーソル行 == (int)E戻り値.GAMESTART - 1) &&
        CDTXMania.Skin.soundGameStart.b読み込み成功)
    {
        CDTXMania.Skin.soundGameStart.tPlay();
    }
    else
    {
        CDTXMania.Skin.soundDecide.tPlay();
    }
}
```

### Key Repeat System
DTXMania uses a sophisticated key repeat system for smooth navigation:

```csharp
// Key repeat structure
[StructLayout(LayoutKind.Sequential)]
private struct STキー反復用カウンタ
{
    public CCounter Up;
    public CCounter Down;
    public CCounter R;
    public CCounter B;
}

// Repeat key implementation
this.ctキー反復用.Up.tRepeatKey(
    CDTXMania.InputManager.Keyboard.bKeyPressing((int)SlimDXKey.UpArrow),
    new CCounter.DGキー処理(this.tカーソルを上へ移動する)
);
## 🎵 Background Animations & Effects

### Loading Screen Animation
The song enumeration loading screen (`CActEnumSongs`) features:

```csharp
// Animated transparency effect during song loading
this.txNowEnumeratingSongs.nTransparency = (int)(176.0 + 80.0 *
    Math.Sin((double)(2 * Math.PI * this.ctNowEnumeratingSongs.nCurrentValue * 2 / 100.0)));

// Localized loading messages
string[] strMessage = {
    "     曲データの一覧を\n       取得しています。\n   しばらくお待ちください。",
    " Now enumerating songs.\n         Please wait..."
};
```

### Background Texture System
DTXMania supports dynamic background loading with fallbacks:

```csharp
// Background loading with fallback to solid color
if (this.tx背景 != null)
{
    this.tx背景.tDraw2D(CDTXMania.app.Device, 0, 0);
}
else if (_whitePixel != null)
{
    // Fallback: draw solid dark background
    var viewport = _game.GraphicsDevice.Viewport;
    _spriteBatch.Draw(_whitePixel,
        new Rectangle(0, 0, viewport.Width, viewport.Height),
        new Color(0, 0, 32));
}
```

## 🔧 Popup Menu Framework

### CActSelectPopupMenu Architecture
DTXMania uses a sophisticated popup menu system for configuration:

```csharp
// Popup menu activation
public virtual void tActivatePopupMenu(EInstrumentPart einst)
{
    nItemSelecting = -1;
    this.eInst = einst;
    this.bIsActivePopupMenu = true;
    this.bIsSelectingIntItem = false;
    this.bGotoDetailConfig = false;
}

// Menu item types supported
public enum EType
{
    List,                    // Dropdown selection
    ONorOFFToggle,          // Boolean toggle
    ONorOFForUndefined3State, // Three-state toggle
    Integer,                // Numeric input
    切替リスト              // Switch list
}
```

### Quick Config Menu Structure
The quick config menu (`CActSelectQuickConfig`) provides rapid access to common settings:

**Menu Items:**
1. **Target**: Drums/Guitar/Bass instrument selection
2. **Auto Mode**: Automated play settings
3. **Scroll Speed**: Note scroll velocity
4. **Dark Mode**: Visual reduction settings (OFF/HALF/FULL)
5. **Risky**: Failure threshold settings
6. **Play Speed**: Song tempo adjustment
7. **HID/SUD**: Hidden/Sudden note visibility
8. **Ghost Settings**: Auto and target ghost data
9. **More...**: Link to detailed configuration
10. **Return**: Exit menu

## 🎯 Song Enumeration During Startup

### Multi-threaded Song Loading
DTXMania implements sophisticated background song enumeration:

```csharp
// Thread management for song enumeration
public void StartEnumFromDisk(bool readCache)
{
    if (state == DTXEnumState.None || state == DTXEnumState.CompletelyDone)
    {
        this.thDTXFileEnumerate = new Thread(new ParameterizedThreadStart(this.t曲リストの構築2));
        this.thDTXFileEnumerate.Name = "曲リストの構築";
        this.thDTXFileEnumerate.IsBackground = true;
        this.thDTXFileEnumerate.Start(readCache);
    }
}

// Enumeration states
private enum DTXEnumState
{
    None,           // Not started
    Ongoing,        // In progress
    Suspended,      // Temporarily paused
    Enumeratad,     // Complete but not reflected
    CompletelyDone  // Complete and reflected
}
```

### Database Management
DTXMania maintains two database files:
- **songs.db**: Score cache and metadata
- **songlist.db**: Serialized song list for fast loading

```csharp
// Database serialization
private static void SerializeSongList(CSongManager cs, string strPathSongList)
{
    using (Stream output = File.Create(strPathSongList))
    {
        BinaryFormatter formatter = new BinaryFormatter();
        formatter.Serialize(output, cs);
    }
}

// Asynchronous deserialization
private async Task<CSongManager> Deserialize(string strPathSongList)
{
    return await Task.Run(() => {
        using (Stream input = File.OpenRead(strPathSongList))
        {
            BinaryFormatter formatter = new BinaryFormatter();
            return (CSongManager)formatter.Deserialize(input);
        }
    });
}
```

## 🎮 Implementation Patterns for DTXManiaCX

### Key Architectural Patterns Identified

1. **Stage-Based Architecture**: Each screen is a separate stage with lifecycle methods
2. **Activity Pattern**: UI components inherit from `CActivity` with standard lifecycle
3. **Counter-Based Animation**: `CCounter` class manages all timing and animation
4. **Multi-Input Support**: Unified input handling across keyboard, drums, guitar, bass
5. **Resource Management**: Centralized texture and sound loading with fallbacks
6. **Phase-Based Loading**: Complex operations broken into trackable phases
7. **Background Threading**: Heavy operations (song enumeration) run on background threads
8. **Caching Strategy**: Multiple levels of caching for performance optimization

### Recommended Implementation Approach for DTXManiaCX

1. **Adopt Stage Pattern**: Implement similar stage-based architecture in MonoGame
2. **Implement Counter System**: Create equivalent timing/animation system
3. **Multi-Input Framework**: Support keyboard, gamepad, and MIDI input simultaneously
4. **Progressive Loading**: Implement phase-based loading with progress indication
5. **Background Processing**: Use async/await for song enumeration and database operations
6. **Fallback Graphics**: Always provide fallback rendering when textures fail to load
7. **Sound Integration**: Integrate sound effects for all UI interactions
8. **Localization Support**: Prepare for Japanese and English text rendering

This analysis provides the foundation for implementing an authentic DTXMania experience in DTXManiaCX while leveraging modern MonoGame capabilities.

## 🚧 Implementation Status

### ✅ Completed Features
- **Stage Architecture**: Implemented IStage pattern with proper lifecycle management
- **Resource Loading**: Menu texture (2_menu.png) and background texture (2_background.jpg) loading via ResourceManager
- **Input Handling**: Keyboard navigation (Up/Down arrows, Enter, Escape) with proper key repeat
- **Menu Structure**: Three-item menu (GAME START, CONFIG, EXIT) with correct positioning
- **Animation System**: Cursor flash animation and menu movement transitions
- **Texture-Based Rendering**: Authentic DTXMania menu rendering using sprite sheet texture

### 🎯 Menu Rendering Implementation
The menu rendering now follows the authentic DTXMania pattern:

**Texture Layout (2_menu.png):**
- Row 0 (0, 0): "GAME START" menu item
- Row 1 (0, 39): "OPTION" menu item (skipped for compatibility)
- Row 2 (0, 78): "CONFIG" menu item
- Row 3 (0, 117): "EXIT" menu item
- Row 4 (0, 156): Cursor background effect
- Row 5 (0, 195): Cursor highlight overlay

**Rendering Features:**
- Individual menu items drawn from specific texture regions
- DTXMania-style cursor effects with scaling and transparency animation
- Smooth movement animations between menu items
- Fallback to rectangle-based rendering when texture unavailable

### 🔄 Next Steps
- Test menu texture rendering with actual DTXMania graphics
- Add fade transitions between stages
- Implement song enumeration loading screen

### ✅ Recently Completed - Main Menu Enhancements
- **Sound Effects System**: Implemented complete sound loading and playback infrastructure
- **Menu Sound Effects**: Added cursor movement, selection, and game start sounds
- **Mouse Support**: Full mouse navigation with hover effects and click support
- **Menu Wrapping**: Added circular navigation (up from first item goes to last, down from last goes to first)
- **Enhanced Input**: Integrated keyboard and mouse input with proper state management

### ✅ Recently Completed - Configuration Screen (Task 5)
- **Basic Config Screen**: Implemented full configuration stage with UI components
- **Config Item System**: Created IConfigItem interface with dropdown, toggle, and integer implementations
- **Value Editing**: Left/Right arrows change values, Enter toggles boolean options
- **Save Functionality**: Apply changes immediately, save to the config database (config.db) on exit, cancel option
- **DTXMania Integration**: Follows DTXMania patterns with proper stage lifecycle management
- **Unit Testing**: 11 unit tests covering config item functionality

### 🎵 Sound System Implementation Details

**Sound Infrastructure:**
- `ISound` interface for sound abstraction with reference counting
- `ManagedSound` implementation wrapping MonoGame SoundEffect
- `IResourceManager.LoadSound()` method for centralized sound loading
- Fallback silent sound creation for missing audio files

**Menu Sound Effects:**
- **Cursor Movement**: `Sounds/Move.ogg` - Plays when navigating between menu items
- **Selection**: `Sounds/Decide.ogg` - Plays for CONFIG and EXIT selections
- **Game Start**: `Sounds/Game start.ogg` - Special sound for GAME START selection (fallback to Decide.ogg)
- Volume control: Cursor (70%), Select (80%), Game Start (90%)
- **Format**: OGG Vorbis (authentic DTXManiaNX format)

**Mouse Integration:**
- Hover detection over menu items with proper bounds checking
- Automatic cursor movement when mouse hovers over different menu item
- Left-click support for menu selection
- Seamless integration with keyboard navigation

## 🚧 Startup Stage Text Rendering Implementation

### ✅ Recently Completed
- **BitmapFont System**: Implemented DTXMania-style bitmap font rendering using console font textures
- **Authentic Text Display**: Startup stage now displays proper text instead of placeholder rectangles
- **Font Texture Loading**: Loads `Console font 8x16.png` and `Console font 2 8x16.png` from DTXMania graphics
- **Character Mapping**: Follows DTXMania's character layout and positioning system
- **Fallback Rendering**: Gracefully falls back to rectangles if font textures unavailable

### 🎯 BitmapFont Implementation Details
The new BitmapFont system provides authentic DTXMania text rendering:

**Font Texture Structure:**
- Uses DTXMania's console font textures (8x16 pixel characters)
- Supports 3 font types: Normal, Thin, WhiteThin
- Character set: ` !"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\]^_`abcdefghijklmnopqrstuvwxyz{|}~ `

**Rendering Features:**
- Character-by-character rendering from texture atlas
- Proper newline handling and text positioning
- Multiple font styles matching DTXMania's EFontType enum
- Efficient texture-based rendering with MonoGame SpriteBatch

**Startup Stage Integration:**
- Progress messages now display as authentic DTXMania text
- Current progress uses thin font style for visual distinction
- Progress percentage rendered with bitmap font
- Maintains fallback to rectangle rendering for compatibility

The startup stage now provides the authentic DTXMania loading experience with proper text display during resource loading phases.
