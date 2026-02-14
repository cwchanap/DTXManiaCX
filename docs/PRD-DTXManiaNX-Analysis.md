# DTXManiaNX Feature Analysis & DTXManiaCX Gap Assessment

## Part 1: DTXManiaNX High-Level PRD

### 1. Product Overview

DTXManiaNX is a rhythm game simulator for Konami's GITADORA (Drummania/GuitarFreaks) series. It reads DTX chart files and provides a full gameplay experience across three instruments: Drums, Guitar, and Bass. Players use keyboards, MIDI controllers, or game controllers to hit notes in sync with music.

---

### 2. Core Game Modes

#### 2.1 Drums Mode
- 12 drum pads: LC, HH, LP, SN, HT, BD, LT, FT, CY, HHO, RD, LBD
- Lane configurations: Default 10-lane, XG 9-lane, Classic 6-lane
- Hi-hat open/close distinction (same lane, different graphics)
- Fill-in marker detection with audience cheer on completion
- Pad configurable lane types (A/B/C/D) for different physical arrangements
- Configurable dkdk (double kick) display modes
- Drum grouping modes: HH group (4 modes), FT group (2), CY group (2), BD group (4)
- Cymbal Free mode: any cymbal input accepted for any cymbal note
- Hit sound priority per group (Chip > Pad or Pad > Chip)

#### 2.2 Guitar Mode
- 5 fret buttons: R(ed), G(reen), B(lue), Y(ellow), P(urple)
- Pick (strum) input
- Wailing (sustain) sections with bonus scoring
- Open note support
- All 32 chord combinations (single notes through 5-button chords)
- Long note support with accumulated bonus

#### 2.3 Bass Mode
- Same 5-fret+Pick layout as Guitar
- Independent chart, scoring, and auto-play settings
- FLIP mode: swap Guitar and Bass charts (activated via pad sequence)

#### 2.4 Shared Gameplay Features
- **Judgement system**: Perfect, Great, Good, Poor, Miss, Bad (6 tiers)
- **Customizable hit timing windows** per judgement tier, per instrument
- **Scoring**: Configurable skill modes (Old DTXMania vs XG specification)
- **Dual skill ratings**: Game Skill + Performance Skill
- **Combo system**: Configurable minimum display threshold per instrument
- **Life gauge**: Gauge delta matrix (positive for good hits, negative for misses), configurable damage level (Small/Normal/High)
- **RISKY mode**: Fixed miss count → instant failure (overrides normal gauge)
- **Stage failure/clear**: Gauge-based with failure animation
- **Auto-play**: Per-lane toggle (can auto-play specific drums while playing others manually)
  - Drums: 11 independent lane toggles (LC, HH, SD, BD, HT, LT, FT, CY, RD, LP, LBD)
  - Guitar: 7 independent toggles (R, G, B, Y, P, Pick, Wail)
  - Bass: 7 independent toggles (same as guitar)
  - Quick presets: All Auto, Auto LP, Auto BD, 2PedalAuto, XGLaneAuto, Custom, OFF
- **Play speed**: 0.5x–4.0x with both pitch-shift and time-stretch modes
- **Configurable hit ranges**: Per-tier (Perfect/Great/Good/Poor), per-instrument, per-BOX folder override
- **Ghost data system**: Auto ghost types (Perfect, Last Play, Hi Skill, Hi Score, Online) + target ghost comparison

---

### 3. Gameplay Modifiers

Per-instrument configurable modifiers:

| Modifier | Description |
|----------|-------------|
| Scroll Speed | Note approach speed (adjustable during play) |
| Hidden | Notes disappear before reaching judgement line |
| Sudden | Notes only appear near the judgement line |
| Dark | Lane visibility (OFF, Half-transparent, Full dark) |
| Reverse | Notes scroll downward instead of upward |
| Random | Randomize note lane assignments |
| Mirror | Mirror lane positions |
| Super Random | Fully randomize all notes |
| Hyper Random | (Drums) Even more aggressive randomization |
| Master Random | (Drums) Maximum randomization |
| Another Random | (Drums) Alternative randomization algorithm |
| Random Pedal | (Drums) Separate pedal randomization |
| Light | (Guitar/Bass) Missed notes don't break combo |
| Specialist | (Guitar/Bass) Stricter playing mode |
| Tight | Strict timing (empties count as misses) |
| Hazard | One miss = instant failure |
| NoFail | Continue playing after gauge depletion |

---

### 4. Song Selection System

#### 4.1 Song List Display
- Hierarchical tree navigation with BOX (folder) support
- Back navigation items in subfolder views
- Scrollable list with center-focus selection
- Random song selection node
- SET.def support: groups multiple difficulty files under one song entry
- BOX.def support: folder configuration (colors, skin, hit ranges)

#### 4.2 Song Information Panel
- Preview image display (PREIMAGE)
- Preview movie display (PREMOVIE)
- Preview sound playback with configurable delay
- Artist/Comment display
- Difficulty level display (up to 5 levels per song)
- BPM display
- Performance history panel (past play records)
- Current position indicator (scrollbar)

#### 4.3 In-Selection Features
- **Quick Config popup**: Target instrument, auto mode presets, scroll speed, dark mode, risky, play speed, hidden/sudden
- **Song sort**: By title, level, rank, play count, artist, skill point, date (ascending/descending)
- **Song search**: Full-text search across title/artist/comment with switches (/t, /a, /c, /s for case-sensitive)
- **Background AVI**: Animated selection screen background
- **Difficulty cycling**: Left/Right changes difficulty slot
- **Scroll speed adjustment** during selection

---

### 5. DTX Chart Format Support

#### 5.1 Supported File Formats
- DTX (native), GDA, G2D, BMS, BME, SMF (MIDI)

#### 5.2 Header Metadata
- TITLE, ARTIST, COMMENT, GENRE
- DLEVEL/GLEVEL/BLEVEL (difficulty per instrument)
- BPM + mid-song BPM changes
- PREVIEW (audio), PREIMAGE (image), PREMOVIE (video)
- STAGEFILE (loading screen), BACKGROUND/WALL (background art)
- RESULTIMAGE/RESULTMOVIE/RESULTSOUND per rank (SS through E)
- Random branching (#RANDOM / #IF / #ENDIF)

#### 5.3 Resource Definitions
- WAVzz: Sound files with volume and pan control
- BMPzz: Image/BMP textures with size control
- AVIzz: Video files
- Custom paths (#PATH_WAV, #PATH)

#### 5.4 Channel System
- System: BGM, bar length, BPM, BPM-extended
- Drums: 12 channels (HH close/open, SN, BD, HT, LT, FT, CY, RD, LC, LP, LBD)
- Hidden drum channels (visible, no sound) and NoChip channels (sound, not visible)
- Guitar: 32 chord combinations + wailing + long notes
- Bass: 32 chord combinations + wailing + long notes
- BGA: 8 layers + 8 swap layers
- Movie: Normal + fullscreen
- SE: 30 sound effect channels
- Control: Bar lines, beat lines, fill-in markers, bonus effects
- Mixer: Dynamic sound mixer management

---

### 6. Audio System

#### 6.1 Output Devices
- DirectSound (fallback)
- WASAPI Exclusive and Shared (Windows low-latency)
- ASIO (professional audio interfaces)
- Event-driven WASAPI mode

#### 6.2 Audio Features
- Polyphonic playback per lane (1–8 simultaneous sounds)
- Dynamic mixer management (add/remove during playback)
- Play speed modification (pitch-shift and time-stretch)
- Per-channel volume and pan control
- Supported formats: WAV, MP3, OGG, XA (PlayStation ADPCM via libbjxa)
- Sound monitoring per instrument (emphasize player's sound)
- Configurable BGM/chip/audience volumes
- Metronome (embedded click track)

#### 6.3 Timing/Calibration
- Input delay adjustment per instrument (-99 to +99ms)
- BGM delay adjustment (-99 to +99ms)
- Judgement line position offset per instrument
- Sound-synchronized timer for drift correction
- Wave position auto-adjustment

---

### 7. Input System

#### 7.1 Supported Devices
- Keyboard (DirectInput)
- Joystick/Gamepad (DirectInput, axis + 128 buttons)
- MIDI input (multiple devices, velocity support)
- Mouse

#### 7.2 Key Assignment
- 4 assignment groups: Drums, Guitar, Bass, System
- Up to 16 bindings per pad (multi-device)
- Format: Device type (K/N/M/J) + device ID + key code
- Velocity minimum filtering per drum pad (ghost note rejection, 0–127)
- Buffered input mode toggle

#### 7.3 System Keys
- Capture (screenshot), Search, Help, Pause
- Loop create/delete (practice region)
- Skip forward/backward (configurable 100–10000ms)
- Increase/decrease play speed during gameplay
- Restart from beginning

---

### 8. Visual/Graphics System

#### 8.1 Rendering
- Direct3D9 via SharpDX
- Configurable resolution, fullscreen (exclusive + maximized window)
- VSync toggle, background transparency
- Device loss recovery (managed/unmanaged resource split)

#### 8.2 Visual Effects
- Chip fire effects (per-instrument)
- Lane flush effects on hit
- Judgement string animations (3 types)
- Explosion/attack effects (configurable frame-based animation)
- Fill-in drum effects
- Wailing fire effects (guitar/bass)
- BGA layers (8 layers + 8 swap layers)
- AVI/video playback during performance (4 movie modes)

#### 8.3 Display Options
- Lane display modes (All ON, Lane OFF, Line OFF, All OFF)
- Judgement display toggle per instrument
- Lag time display (OFF, ON, GREAT-POOR only) with configurable color scheme
- Play speed indicator
- BPM bar display (4 modes)
- Moving drum set animation
- Nameplate types (XG2, XG style)
- Movie alpha/lane transparency

---

### 9. Skin/Theme System
- Skin directories under System/
- Per-BOX skin switching (automatic on folder enter)
- Dedicated ChangeSkin stage for transitions
- System sound definitions (BGM per stage, SFX for navigation/events)
- Difficulty announcement sounds (NOVICE through EXTREME)
- Custom fonts for song list

---

### 10. Result Screen
- Per-instrument score, combo, and judgement breakdown display
- Rank system: SS, S, A, B, C, D, E (two calculation modes)
- New record detection (Skill, Score, Rank) with indicators
- Full combo / Excellent tracking (persistent)
- Dual skill display (Game Skill + Performance Skill)
- Rank-specific result images, movies, and sounds
- .score.ini export with MD5 hash verification
- Auto screenshot capture on new records
- Ghost data saving for future comparison
- Play count and clear count tracking

---

### 11. Training/Practice Mode
- Loop region create/delete for section practice
- Skip forward/backward (configurable time)
- Increase/decrease play speed during gameplay
- Restart from beginning
- Pause functionality
- Results NOT saved in training mode

---

### 12. Special Features
- **Discord Rich Presence**: Stage-aware presence (song title, difficulty, time remaining)
- **Plugin system**: IPluginActivity/IPluginHost with lifecycle hooks and stage change notifications
- **Compact mode**: Direct song launch from command-line (no menu)
- **DTXV mode**: Real-time preview integration with DTXCreator
- **DTX2WAV mode**: WAV export from DTX files
- **Multiple instance prevention**: Mutex + Windows messaging to forward arguments
- **Stoic mode**: Strip all visual distractions (no BGA/AVI/images)
- **IME hook**: Japanese text input support
- **Command parsing**: Sequential pad input recognition (e.g., FLIP activation)
- **Screen capture**: Configurable key binding + auto-capture
- **Ghost data**: Track and compare with previous best performances

---

### 13. Configuration Persistence
- Config.ini with sections: System, Log, PlayOption, AutoPlay, HitRange, DiscordRichPresence, GUID, KeyAssign (Drums/Guitar/Bass/System)
- .score.ini per song with 9 record sections and hash verification
- SongsDB caching for song metadata
- Player profile (card name, group name, name color per instrument)

---
---

## Part 2: DTXManiaCX Current Implementation Status

### What DTXManiaCX Has Implemented

#### Framework & Infrastructure
- .NET 8.0 + MonoGame 3.8 (cross-platform: Windows + macOS)
- Stage management system: 7 stages (Startup, Title, Config, SongSelect, SongTransition, Performance, Result)
- 6 transition types (Fade, DTXManiaFade, Crossfade, Instant, StartupToTitle, generic)
- Component-based UI system (UIManager, UIElement, UIContainer, UILabel, UIButton, UIImage, UIPanel, UIList)
- Interface-driven design (IStage, IResourceManager, IConfigManager, IGraphicsManager, IInputManager)
- xUnit test suite with Moq
- CI: GitHub Actions (Windows + macOS builds)
- MCP integration for AI copilot (unique to CX)

#### Song System
- DTXChartParser: Parses .dtx, .gda, .g2d, .bms, .bme, .bml
- 10 NX drum lane mapping (LC, HH, LP, SN, HT, BD, LT, FT, CY, RD)
  - Note: LBD (Left Bass Drum) is intentionally excluded; channel 0x1C maps to LP lane
  - BD abbreviation standardized (code uses "DB" internally but "BD" is the standard notation)
- SQLite song database (via EF Core)
- SongManager singleton, SongListNode tree, SET.def parsing
- Song hierarchy with BOX folder support, BackBox, Random nodes

#### Performance Stage (Drums Only)
- 18 specialized components (AudioLoader, BackgroundRenderer, ComboDisplay, ComboManager, EffectsManager, GaugeDisplay, GaugeManager, JudgementLineRenderer, JudgementManager, JudgementTextPopup, LaneBackgroundRenderer, NoteRenderer, PadRenderer, PerformanceSummary, PooledEffectsManager, ScoreDisplay, ScoreManager, SongTimer)
- Event-driven judgement system (JudgementManager → ScoreManager, ComboManager, GaugeManager)
- Hit detection with timing windows (Just, Great, Good, Poor, Miss)
- Note scrolling and rendering
- BGM event scheduling and playback
- Auto-play functionality
- Stage completion with transition to Result
- NoFail mode
- Gauge with danger state
- Audio: WAV (native), MP3 (FFMpegCore), OGG (NVorbis)

#### Song Selection
- Song list display with BOX folder navigation
- Breadcrumb navigation
- Difficulty cycling (Left/Right)
- Song status panel with difficulty info
- Preview image panel
- Preview sound playback with delay timer + BGM fade
- Navigation sounds
- Random song selection
- Async song initialization

#### Result Screen
- Score, max combo, accuracy display
- Judgement breakdown (Just/Great/Good/Poor/Miss)
- Clear/Failed status
- Return to song selection

#### Configuration
- INI-based config (ConfigManager/ConfigData)
- Options: Resolution, Fullscreen, VSync, ScrollSpeed, AutoPlay, NoFail
- Key bindings system (KeyBindings + InputRouter)
- ConfigStage with menu UI

#### Input System
- ModularInputManager with KeyboardInputSource
- InputRouter with lane-hit events
- Key bindings (hot-swappable, config-persistent)
- Back action debouncing

#### Resource Management
- Reference-counted caching with statistics
- Skin system with fallback chain (current → fallback → system)
- TexturePath constants
- BitmapFont rendering

---
---

## Part 3: Gap Analysis — What DTXManiaCX Is Missing

### CRITICAL GAPS (Core Gameplay)

#### G1. Guitar & Bass Gameplay — NOT IMPLEMENTED
- No guitar/bass performance screen (CX only has drums)
- No 5-fret (R/G/B/Y/P) + Pick + Wailing input handling
- No 32-chord combination parsing or rendering
- No long note support
- No wailing bonus system
- No FLIP mode (guitar/bass swap)
- **Impact**: Eliminates 2 of 3 instrument modes entirely

#### G2. Per-Lane Auto-Play — NOT IMPLEMENTED
- CX has global auto-play toggle only (on/off for all lanes)
- NX allows per-lane auto-play (e.g., auto bass drum while manually playing other pads)
- **Impact**: Removes a major practice/accessibility feature

#### G3. Gameplay Modifiers — NOT IMPLEMENTED
- No Hidden, Sudden, Dark, Reverse, Mirror, Random, Super Random, Tight, Hazard modes
- CX only has AutoPlay and NoFail
- **Impact**: Removes variety and difficulty customization

#### G4. Play Speed Control — PARTIAL
- CX has scroll speed in config but no runtime adjustment
- NX supports 0.5x–4.0x play speed with pitch-shift and time-stretch
- No increase/decrease during gameplay
- **Impact**: Removes practice and challenge features

#### G5. RISKY Mode — NOT IMPLEMENTED
- Fixed miss count → failure mode absent
- **Impact**: Missing competitive/challenge mode

#### G6. Training/Practice Mode — NOT IMPLEMENTED
- No loop region create/delete
- No skip forward/backward
- No play speed change during gameplay
- No pause functionality during performance
- No restart from beginning
- **Impact**: Major missing feature for practice and learning

---

### SIGNIFICANT GAPS (Song Selection & UI)

#### G7. Song Sort & Search — NOT IMPLEMENTED
- No sort options (by title, level, rank, play count, etc.)
- No full-text search with switches
- **Impact**: Usability regression for large song libraries

#### G8. Quick Config During Selection — NOT IMPLEMENTED
- NX has popup config for modifiers during song selection
- CX requires going to separate Config stage
- **Impact**: Workflow friction

#### G9. Performance History Panel — NOT IMPLEMENTED
- No display of past play records on song selection
- **Impact**: Missing context for skill tracking

#### G10. Song Selection Background AVI/Movie — NOT IMPLEMENTED
- No animated backgrounds in song selection
- **Impact**: Visual polish

#### G11. Artist/Comment/Information Bar — NOT IMPLEMENTED
- No artist or comment display in song selection
- Basic song metadata display only

---

### SIGNIFICANT GAPS (Audio)

#### G12. MIDI Input — NOT IMPLEMENTED
- CX only supports keyboard input
- No MIDI device scanning, velocity support, or multi-device input
- **Impact**: Blocks real drum controller gameplay

#### G13. Joystick/Gamepad Input — NOT IMPLEMENTED
- No gamepad support
- **Impact**: Blocks guitar controller gameplay

#### G14. Sound Device Selection — NOT IMPLEMENTED
- CX uses MonoGame's default audio (SoundEffect)
- No WASAPI/ASIO/DirectSound device selection
- No configurable buffer size for low latency
- **Impact**: Higher audio latency, no professional audio support

#### G15. Polyphonic Sound Management — NOT IMPLEMENTED
- No configurable polyphonic sounds per lane
- No dynamic mixer management
- **Impact**: Audio quality degradation in dense charts

#### G16. XA Audio Format — NOT IMPLEMENTED
- No PlayStation ADPCM support (libbjxa equivalent)
- **Impact**: Cannot play some legacy DTX files

---

### SIGNIFICANT GAPS (Visuals & Effects)

#### G17. BGA/AVI Video Playback — NOT IMPLEMENTED
- No background animation layers during gameplay
- No video playback (AVI/DirectShow equivalent)
- No movie modes (fullscreen, window, both)
- **Impact**: Missing major visual element of rhythm game experience

#### G18. Fill-In Effects — NOT IMPLEMENTED
- No fill-in marker detection or drum fill effects
- No audience cheer on fill completion
- **Impact**: Missing drum-specific visual feedback

#### G19. Judgement Animation Variants — PARTIAL
- CX has basic judgement text popup
- NX has 3 animation types (old DTXMania, frame-based, pseudo-XG) with configurable parameters
- **Impact**: Reduced visual polish

#### G20. Chip Fire / Wailing Fire Effects — NOT IMPLEMENTED
- No fire/explosion effects on note hits
- Basic hit effects only in CX
- **Impact**: Reduced visual feedback

---

### MODERATE GAPS (Result & Scoring)

#### G21. Rank System — NOT IMPLEMENTED
- No SS/S/A/B/C/D/E rank calculation
- CX shows raw score, combo, accuracy only
- No dual rank modes (Old vs XG)
- **Impact**: Missing standard rhythm game progression metric

#### G22. Score Persistence & Records — NOT IMPLEMENTED
- No .score.ini saving per song
- No high score tracking across sessions
- No best rank / full combo / clear count persistence
- No ghost data comparison
- **Impact**: No long-term progression tracking

#### G23. Skill Rating System — NOT IMPLEMENTED
- No Game Skill or Performance Skill calculation
- No skill meter during gameplay
- **Impact**: Missing competitive progression metric

#### G24. Result Screen Detail — PARTIAL
- No rank-specific result images/movies/sounds
- No new record detection indicators
- No performance history comparison
- Basic text-only display in CX

---

### MODERATE GAPS (Configuration)

#### G25. Timing Calibration — NOT IMPLEMENTED
- No input delay adjustment per instrument
- No BGM delay adjustment
- No judgement line position offset
- **Impact**: Cannot compensate for hardware latency

#### G26. Visual Customization — NOT IMPLEMENTED
- No Dark/Hidden/Sudden lane display modes
- No per-instrument lane display options
- No lag time display
- No configurable judgement animation types
- No movie alpha/transparency settings

#### G27. Audio Fine-Tuning — NOT IMPLEMENTED
- No per-channel volume (chip, auto-chip, monitor)
- No hit sound / BGM sound / audience sound toggles
- No metronome support

#### G28. Player Profile — NOT IMPLEMENTED
- No card name / group name / name color per instrument
- No nameplate display

---

### LOWER PRIORITY GAPS

#### G29. Discord Rich Presence — NOT IMPLEMENTED
- NX has full Discord integration (song info, time remaining)

#### G30. Plugin System — NOT IMPLEMENTED
- NX has IPluginActivity/IPluginHost extensibility

#### G31. Compact Mode / DTXV / DTX2WAV — NOT IMPLEMENTED
- No command-line direct song launch
- No DTXCreator preview integration
- No WAV export

#### G32. Stoic Mode — NOT IMPLEMENTED
- No distraction-free visual mode

#### G33. DTXCreator (Chart Editor) — NOT PORTED
- NX includes a full chart editor application
- Not in scope for CX game rewrite

#### G34. IME / Japanese Input — NOT IMPLEMENTED
- No Japanese text input for search

#### G35. Screen Capture — NOT IMPLEMENTED
- No screenshot key or auto-capture on records

#### G36. Multiple Instance Prevention — NOT IMPLEMENTED
- No mutex-based duplicate detection

#### G37. BOX.def Features — PARTIAL
- Basic BOX support exists
- No per-box skin switching, custom hit ranges, or color customization

#### G38. Hidden/NoChip Drum Channels — NOT IMPLEMENTED
- No channels 31-3C (visible, no sound) or B1-BE (sound, no visible chip)

#### G39. Bonus Effect Channels — NOT IMPLEMENTED
- No channels 4C-4F for bonus effects

#### G40. SE Channels — NOT IMPLEMENTED
- No sound effect channels SE01-SE32

---

### Summary Priority Matrix

| Priority | Category | Gaps |
|----------|----------|------|
| **P0 - Critical** | Core gameplay | G1 (Guitar/Bass), G2 (Per-lane auto), G12 (MIDI), G13 (Gamepad) |
| **P1 - High** | Gameplay depth | G3 (Modifiers), G4 (Play speed), G6 (Training), G17 (BGA/Video), G21 (Ranks), G22 (Score saving) |
| **P2 - Medium** | Usability | G7 (Sort/Search), G8 (Quick config), G14 (Sound devices), G23 (Skill), G25 (Calibration) |
| **P3 - Low** | Polish | G9-G11 (Selection UI), G15-G16 (Audio), G18-G20 (Effects), G24 (Result), G26-G28 (Config) |
| **P4 - Deferred** | Extras | G29-G40 (Discord, plugins, compact mode, DTXCreator, etc.) |
