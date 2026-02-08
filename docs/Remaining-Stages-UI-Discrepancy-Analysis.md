# Remaining Stages — UI Layout Discrepancy Analysis

**DTXmaniaNX (Original) vs DTXManiaCX (Rewrite)**

Screen resolution: **1280x720** for both projects.

Stages covered: Startup, Title, Config/Option, Song Loading/Transition, Performance, Result, End/ChangeSkin.

---

## 1. STARTUP STAGE

### NX Layout
- Background: `1_background.jpg` at (0,0)
- Progress text lines at (0, y) incrementing 14px per line, white console font
- Shows: version, loading phases (system sounds, songlist.db, songs.db, song enumeration, score cache, file reading, post-processing, saving)
- 8 loading phases with status text
- No input handling — auto-transitions to Title

### CX Layout
- Background: `Graphics/1_background.jpg` via BaseStage (with fallback gradient/solid color)
- No visible progress text or loading phases in UI
- Minimal startup — transitions to Title via `StartupToTitleTransition(1.0)`

### Discrepancies

| Element | NX | CX | Status |
|---------|----|----|--------|
| Background texture | `1_background.jpg` | `Graphics/1_background.jpg` | Same (path convention differs) |
| Loading progress text | 8 phases with console text at 14px line spacing | None visible | **MISSING: CX has no loading progress display** |
| Version string | Shown in progress text | Not shown | **MISSING** |
| Song enumeration | Full multi-phase (DB load, file scan, cache) | Deferred to SongManager initialization | Different architecture |
| Transition | `CActFIFOWhite` (white tile fade) | `StartupToTitleTransition` (1.0s, delayed fade-in) | Similar concept, different implementation |

**Impact:** Low — startup is brief. The lack of progress display in CX means users see a blank screen during any initialization, but CX's initialization is faster due to deferred song loading.

---

## 2. TITLE STAGE

### NX Layout
```
+--------------------------------------------------+
| 2_background.jpg                                  |
| vX.X.X  (2,2)                                    |
|                                                   |
|                   [GAME START]  (506,513)          |
|                   [CONFIG    ]  (506,552)          |
|                   [EXIT      ]  (506,591)          |
|                   ^cursor highlight^               |
+--------------------------------------------------+
```

### CX Layout
```
+--------------------------------------------------+
| Graphics/2_background.jpg (or fallback dark navy)  |
| DTXManiaCX v1.0.0  (2,2)                         |
|                                                   |
|                   [GAME START]  (506,513)          |
|                   [CONFIG    ]  (506,552)          |
|                   [EXIT      ]  (506,591)          |
|                   ^cursor highlight^               |
+--------------------------------------------------+
```

### Discrepancies

| Element | NX | CX | Status |
|---------|----|----|--------|
| Background | `2_background.jpg` at (0,0) | `Graphics/2_background.jpg` with fallback `Color(8,8,16)` | CX has fallback |
| Version string | `(2, 2)`, white text | `(2, 2)`, rectangle fallback (textLen*8, 16, White) | **DOWNGRADED: CX uses rectangle placeholder instead of text** |
| Menu texture | `2_menu.png` sprite sheet (227x, 6 rows of 39px) | `Graphics/2_menu.png` same layout | Same |
| Menu position | X=506, Y=513, W=227, H=39 | X=506, Y=513, W=227, H=39 | **Exact match** |
| Menu items | GAME START (row0), CONFIG (row2-3), EXIT (row3+) | GAME START (row0), CONFIG (row2), EXIT (row3) | Same (OPTION removed in both) |
| Cursor flash | Row 5, scales 1.0→1.5x, fades 255→0 alpha, 700ms cycle | Row 4 flash + Row 5 normal, same scaling/fade, 700ms | Same behavior |
| Cursor movement | Cosine interpolation over 100 ticks | Cosine interpolation over 100ms | Same (different time unit but equivalent) |
| Song enum indicator | `ScreenTitle NowEnumeratingSongs.png` at (18,7) pulsing | Not present | **MISSING: CX has no background enumeration indicator** |
| Input: Keyboard | Up/Down, Enter, Escape | Up/Down, Enter/Space, Escape | CX adds Space as alternative |
| Input: Drums | HH/HT up, SD/LT down, CY/RD select | Not supported | **MISSING: CX has no drum pad input** |
| Input: Mouse | Not supported | Hover detection + click on menu items | **CX-ONLY: Mouse support** |
| Sound: title | `soundTitle` on fade-in | Not mentioned | Unknown |
| Sound: cursor | `soundCursorMovement` | `Sounds/Move.ogg` at 70% | Equivalent |
| Sound: decide | `soundDecide` / `soundGameStart` | `Sounds/Decide.ogg` 80% / `Sounds/Game start.ogg` 90% | Same concept |
| Transition out | `CActFIFOWhite` | `DTXManiaFadeTransition(0.7)` to SongSelect, `CrossfadeTransition(0.5)` to Config | Different transition types |

**Impact:** Low-Medium. The Title stage is nearly identical in layout. Key gaps: no drum pad input, no background song enumeration indicator.

---

## 3. CONFIG / OPTION STAGE

### NX Layout
```
+--------------------------------------------------+
| [4_header panel.png]                              |
|   +--------+     +--------------------------+     |
|   | Menu   |     | Item List (scrolling)    |     |
|   | Panel  |     | (400,0) item bar bg      |     |
|   | (245,  |     |                          |     |
|   |  140)  |     | [item1] [value1]         |     |
|   |>Drums< |     | [item2] [value2]  <--sel |     |
|   |[Guitar]|     | [item3] [value3]         |     |
|   |[Bass  ]|     |    Description (800,270) |     |
|   |[Exit  ]|     +--------------------------+     |
| [4_footer panel.png]                              |
+--------------------------------------------------+
```

**NX features:**
- 5 category menu (System, Drums, Guitar, Bass, Exit) with cursor at (250, 146 + menu*32)
- Scrolling item list on right side with 10 visible panels
- Description panel at (800, 270) when focused on items
- Key assignment sub-screen with 16 key slots per pad
- Full skin textures: `4_background.png`, `4_header panel.png`, `4_footer panel.png`, `4_menu cursor.png`, `4_menu panel.png`, `4_item bar.png`
- Smooth scrolling between items
- Category-specific settings: autoplay per-pad, scroll speed, combo position, sudden/hidden/reverse, tight mode, input adjust, graph

### CX Layout
```
+--------------------------------------------------+
| Background: Color(16,16,32) solid                  |
|                                                   |
|            CONFIGURATION  (centered, Y=50)        |
|                                                   |
| Screen Resolution: 1280x720  (100, 110)          |
| Fullscreen: OFF              (100, 150)           |
| VSync Wait: ON               (100, 190)           |
| No Fail: OFF                 (100, 230)           |
| Auto Play: OFF               (100, 270)           |
|                                                   |
| [BACK]      [SAVE & EXIT]   (100, 320)           |
|                                                   |
| Instructions                 (10, 690)            |
+--------------------------------------------------+
```

### Discrepancies

| Element | NX | CX | Status |
|---------|----|----|--------|
| Background | `4_background.png` skin texture | Solid `Color(16,16,32)` | **MISSING: CX has no skin-based background** |
| Header/Footer panels | `4_header panel.png`, `4_footer panel.png` | None | **MISSING** |
| Menu categories | 5 categories (System/Drums/Guitar/Bass/Exit) with panel at (245,140) | None — flat list | **MISSING: CX has no category navigation** |
| Menu cursor texture | `4_menu cursor.png` with 3-segment stretch | Selection highlight rectangle `Color(64,64,128,150)` | **DOWNGRADED** |
| Item list | Scrolling 10-panel list with `4_item bar.png` background | 5 items in flat list | **SEVERELY REDUCED** |
| Item count | 50+ config items across 4 categories | 5 items total (Resolution, Fullscreen, VSync, NoFail, AutoPlay) | **MISSING: ~90% of config items** |
| Description panel | At (800, 270) with bitmap description | None | **MISSING** |
| Key assignment | Full sub-screen: pad name, 16 key slots, reset, return | Not in Config stage (separate KeyBindings system) | **MISSING from Config UI** |
| Item types | CItemInteger, CItemList, CItemToggle, CItemThreeState | DropdownConfigItem, ToggleConfigItem | Simplified |
| Scroll arrows | `4_triangle arrow.png` (64x24) | None | **MISSING** |
| Input: Drums | HH/HT navigate, CY/RD select, LC cancel | Keyboard only | **MISSING** |

### Missing Config Categories (NX has, CX lacks)

**System settings missing from CX:**
- AVI/BGA display toggle
- Sound output mode (DirectSound/WASAPI/ASIO)
- WASAPI buffer size, ASIO device selection
- Master/BGM/SE/Guitar/Bass/Drums volume sliders
- BGM preview on/off
- Fullscreen anti-alias
- AdjustTime (audio sync offset)
- SongList.db update on/off
- Import/Export config
- Reload Songs

**Per-instrument settings missing from CX (Drums/Guitar/Bass):**
- AutoPlay per-pad (25 individual toggles in NX)
- Scroll Speed (per instrument)
- Combo display position
- Sudden/Hidden/Stealth modes
- Reverse scroll direction
- Judgement position adjustment
- Tight mode (strict timing)
- Input timing adjust
- Dark mode (hide lane backgrounds)
- Random/Mirror modes
- Light mode (guitar/bass)

**Impact:** Very High. The Config stage is the most severely reduced stage in CX. Users cannot configure the vast majority of gameplay settings.

---

## 4. SONG LOADING / SONG TRANSITION STAGE

### NX Layout
```
+--------------------------------------------------+
| 6_background.jpg                                  |
|      [Part: DRUMS]     (191, 52)  262x50          |
|      [EXPERT     ]     (191, 102) 262x50          |
|      [ 8 . 50    ]     (187, 152) 100x130/digit   |
|          /-------\                                 |
|         /  Jacket \ (206,66) rotated 0.28 rad      |
|         \ 384x384 /                                |
|          \-------/                                 |
|    Song Title Text  (190, 285) 40px               |
|    Artist Name      (190, 360) 30px               |
+--------------------------------------------------+
```

### CX Layout
```
+--------------------------------------------------+
| Graphics/6_background.jpg (or gradient fallback)   |
| [Difficulty Sprite]  (191, 80)  262x50            |
| [Grey BG: 262x120]                                |
| [Level Number]       (191, 130) BitmapFont        |
|                                                   |
| Song Title           (190, 285) FontSize=50       |
|                          [Preview Image]          |
| Artist Name          (190, 450) FontSize=30       |
|                          at (640, 120)            |
|                          384x384, rot -0.28rad    |
+--------------------------------------------------+
```

### Discrepancies

| Element | NX | CX | Status |
|---------|----|----|--------|
| Background | `6_background.jpg` | `Graphics/6_background.jpg` with gradient fallback | CX has fallback |
| Part label | `6_Part.png` at (191, 52), 262x50 sprite | Not present | **MISSING: CX has no instrument part label** |
| Difficulty sprite | `6_Difficulty.png` at (191, 102), 262x50 | `Graphics/6_Difficulty.png` at (191, 80), 262x50 | **Y offset differs: NX=102, CX=80** |
| Level number | `6_LevelNumber.png` at (187, 152), 100x130/digit sprite | BitmapFont from `6_LevelNumber.png` at (191, 130), Yellow | **Different rendering: NX uses large sprites, CX uses bitmap font** |
| Decimal point | Custom sprite at (282, 243) | Part of bitmap font text | Different approach |
| Jacket image | 3D transform: translate(206,66), rotate 0.28 rad, scale 384x384 | At (640, 120), 384x384, rotate -0.28 rad | **Different position: NX left-side (206,66), CX right-side (640,120)** |
| Song title | (190, 285), CPrivateFastFont 40px, max 625px | (190, 285), NotoSerifJP 50px | Same position, different font/size |
| Artist name | (190, 360), CPrivateFastFont 30px, max 625px | (190, 450), NotoSerifJP 30px | **Y differs: NX=360, CX=450** |
| Loading phases | 5 phases (DTX read, WAV load, BMP load, wait, fadeout) | No visible loading phases | **MISSING: CX has no progressive loading indication** |
| Fade-out effect | `CActFIFOBlackStart` with jacket overlay at (620,40) | Auto-transition after 3.0s | **Different: NX has custom fade with jacket, CX has timed auto-advance** |
| Cancel input | ESC cancels loading | ESC returns to SongSelect | Same |
| Transition to Performance | After loading complete | After 3.0s or Enter key | Different trigger |

**Impact:** Medium. The layout is similar in concept but differs in positioning and visual quality. The lack of a part label means users don't see which instrument they're about to play.

---

## 5. PERFORMANCE STAGE (Most Complex)

### NX Drums Layout (approximate)
```
+-------------------------------------------------------------+
| [BGA/AVI area]                                               |
|                                                              |
| [Score]  |LC |HH|LP|SN|HT|BD |LT|FT |CY |RD| [Status]     |
|          |295|367|416|467|524|573|642|691|745|815             |
|          |   |   |   |   |   |   |   |   |   |              |
|          |   | Notes scroll down (or up if reverse)          |
|          |   |   |   |   |   |   |   |   |   |              |
|          |===+===+===+===+===+===+===+===+===+===| Judge Line|
|          |   Chip Fire effects   |                           |
| [Gauge]  [Combo]  [Judgement Text]  [Progress Bar]           |
| [Skill Meter / Graph]                                        |
+-------------------------------------------------------------+
```

### CX Drums Layout
```
+-------------------------------------------------------------+
| [Score: (40,41)]                              [Progress Bar] |
|  7 digits                                      (853,0,60,540)|
|                                                              |
| [Skill  |LC |HH|  |SN|HT|BD |LT|FT |CY |RD|              |
|  Panel] |295|367|416|467|524|573|642|691|745|815|             |
| (22,250)|72 |49 |51 |57 |49 |69 |49 |54 |70 |38 |           |
|         |   |   |   |   |   |   |   |   |   |   |           |
|         |   | Notes scroll down                  |           |
|         |   |   |   |   |   |   |   |   |   |   |           |
|         |===+===+===+===+===+===+===+===+===+===| Y=600     |
|         [Gauge Frame (294,626)]                              |
|         [Gauge Fill  (314,635) h=31]                         |
|         [Pad indicators Y=670, h=60]                         |
| [Combo display at (1245,60)]                                 |
+-------------------------------------------------------------+
```

### Drum Lane Position Comparison

| Lane | NX Left X | CX Left X | NX Width | CX Width | Match? |
|------|-----------|-----------|----------|----------|--------|
| LC | 295 | 295 | ~72 | 72 | Yes |
| HH | 367 | 367 | ~49 | 49 | Yes |
| LP | 416 | 416 | ~51 | 51 | Yes |
| SN | 467 | 467 | ~57 | 57 | Yes |
| HT | 524 | 524 | ~49 | 49 | Yes |
| BD | 573 | 573 | ~69 | 69 | Yes |
| LT | 642 | 642 | ~49 | 49 | Yes |
| FT | 691 | 691 | ~54 | 54 | Yes |
| CY | 745 | 745 | ~70 | 70 | Yes |
| RD | 815 | 815 | ~38 | 38 | Yes |

**Lane positions match exactly between NX and CX.**

### Performance Sub-Component Comparison

| Component | NX | CX | Status |
|-----------|----|----|--------|
| Background | `7_background.jpg` or BGA video | `Graphics/7_background.jpg` | CX has no BGA video |
| BGA/AVI video | `CActPerfAVI` — full background video playback | Not implemented | **MISSING: CX has no BGA/AVI support** |
| Lane strips | Skin textures | `Graphics/7_Paret.png` sprite sheet | Equivalent |
| Lane flush (hit glow) | `CActPerfDrumsLaneFlushD` — skin-based lane flash | White overlay with exponential decay | **SIMPLIFIED: CX uses simple white flash** |
| Note rendering | `7_chips_drums.png` sprite sheet, animated | `Graphics/7_chips_drums.png`, 12-col x 11-row, animated (8 base frames + 3 overlay) | Equivalent |
| Note animation | Chip pattern cycling | 100ms per frame, base + overlay passes | Similar |
| Judgement line | `ScreenPlayDrums hit-bar.png` at configurable Y | `Graphics/ScreenPlayDrums hit-bar.png` at Y=600 | **CX lacks configurable judge line position** |
| Judge line Y (normal) | 561 - nJudgeLine.Drums (configurable offset) | 600 (fixed) | **MISSING: CX has no configurable judge line offset** |
| Judge line Y (reverse) | 159 + nJudgeLine.Drums | Not implemented | **MISSING: CX has no reverse scroll** |
| Drum pad effects | `CActPerfDrumsPad` | PadRenderer: `7_pads.png` 4x3 sprite, Y=670, h=60 | Different implementation |
| Score display | `CActPerfDrumsScore` with skin textures | ManagedFont at (40, 41), 7-digit "0000000" | **DIFFERENT: NX uses sprite digits, CX uses font** |
| Combo display | `CActPerfDrumsComboDGB` | ManagedFont at (1245, 60) with spring animation 1.0→1.5x | **DIFFERENT position and rendering** |
| Gauge | `CActPerfDrumsGauge` | Gauge frame at (294, 626), fill at (314, 635), h=31 | Similar concept |
| Skill meter | `CActPerfSkillMeter` graph display | Skill panel at (22, 250) with judgement counts | Different display |
| Judgement text | `CActPerfJudgementString` | BitmapFont popup, fade over 0.6s, rise 30px | Similar |
| Chip fire | `CActPerfDrumsChipFireD` at Y=judgeLine-186 | `Graphics/hit_fx.png` animated sprites, additive blending | Similar |
| Progress bar | `CActPerfProgressBar` | Frame at (853, 0, 60, 540), fill bottom-up LightBlue | Similar |
| Shutter/lane cover | `7_shutter.png` + `7_lanes_Cover_cls.png` for CLASSIC mode | `Graphics/7_shutter.png` at (295, 0) during non-Normal phase | Partial |
| Bar lines | Measure/beat lines rendered | Not mentioned in detail | Unknown |
| Loop markers | Training loop markers | Not implemented | **MISSING: CX has no training/loop mode** |
| Danger indicator | Stage failed overlay | `Graphics/7_Danger.png` pulsing at life < 20% | Similar |
| Play speed overlay | Displayed during modified speed | Not visible | **MISSING** |
| Pause overlay | Not detailed | `Graphics/7_pause_overlay.png` fullscreen | CX has explicit pause |
| MIDI BGM | Audio playback | BGM event scheduling | Different architecture |

### Guitar/Bass Screen — NX vs CX

| Feature | NX | CX | Status |
|---------|----|----|--------|
| Guitar screen | `CStagePerfGuitarScreen` — full implementation | Not implemented | **MISSING: CX has no guitar/bass gameplay** |
| Guitar lane texture | `7_Chips_Guitar.png`, `7_lanes_Guitar.png` | TexturePath constants exist but unused | Referenced but not implemented |
| Guitar judge line | Y=154+offset (top-to-bottom) | N/A | **MISSING** |
| Bass judge line | Y=154+offset | N/A | **MISSING** |
| Guitar RGB buttons | `CActPerfGuitarRGB` display | N/A | **MISSING** |
| Wailing bonus | `CActPerfGuitarWailingBonus` | N/A | **MISSING** |
| Guitar combo | `CActPerfGuitarCombo` | N/A | **MISSING** |
| Guitar gauge | `CActPerfGuitarGauge` | N/A | **MISSING** |
| Guitar score | `CActPerfGuitarScore` | N/A | **MISSING** |

### Performance Input Comparison

| Input | NX | CX | Status |
|-------|----|----|--------|
| Escape | Pause/quit | Stops song, returns to SongSelect | Similar |
| F1/F2 | Speed up/down scroll | Not implemented | **MISSING** |
| F5 | Toggle movie mode | Not implemented | **MISSING** |
| Delete | Toggle debug info | Not implemented | **MISSING** |
| Drum pads | Full MIDI + keyboard input | Keyboard only (InputManagerCompat) | **MISSING: No MIDI input** |
| Guitar buttons | 5-fret + pick + wailing | Not implemented | **MISSING** |
| Backspace | Training loop points | Not implemented | **MISSING** |
| Page Up/Down | Jump in song | Not implemented | **MISSING** |

### Performance Textures Comparison

| NX Texture | CX Equivalent | Status |
|-----------|---------------|--------|
| `7_chips_drums.png` | `Graphics/7_chips_drums.png` | Same |
| `7_Chips_Guitar.png` | Referenced in TexturePath but unused | **Guitar not implemented** |
| `ScreenPlayDrums hit-bar.png` | `Graphics/ScreenPlayDrums hit-bar.png` | Same |
| `7_shutter.png` | `Graphics/7_shutter.png` | Same |
| `7_lanes_Cover_cls.png` | `Graphics/7_lanes_Cover_cls.png` | Same |
| (various lane flush textures) | `Graphics/ScreenPlayDrums lane flush *.png` | Defined in TexturePath |
| `7_Gauge.png` | `Graphics/7_Gauge.png` | Same |
| (gauge bar) | `Graphics/7_gauge_bar.png` | Same |
| (progress bg) | `Graphics/7_Drum_Progress_bg.png` | Same |
| (pads) | `Graphics/7_pads.png` | Same |
| (various BGA textures) | Not supported | **MISSING** |
| (score number sprites) | `Graphics/7_score numbersGD.png` (defined but may use font) | Different rendering approach |

### Gauge Life System Comparison

| Property | NX | CX |
|----------|----|----|
| Starting life | Not detailed (likely 100%) | 50% |
| Danger threshold | Stage failed mechanic | 20% (pulsing danger overlay) |
| Failure threshold | Gauge reaches 0 | 2% (unless NoFail) |
| Perfect/Just | Not detailed | +2.0 |
| Great | Not detailed | +1.5 |
| Good | Not detailed | +1.0 |
| Poor | Not detailed | -1.5 |
| Miss | Not detailed | -3.0 |

### CX Skill Panel Details (NX equivalent is Status Panel + Skill Meter)

CX has a dedicated skill panel at (22, 250) showing:
- Difficulty icon: (36, 516, 60, 60)
- Level number: (40, 540)
- Skill percent: (80, 527)
- Judgement counts: Perfect(102,322), Great(102,352), Good(102,382), Poor(102,412), Miss(102,442)
- Max Combo: (102, 472)
- Timing: Early(192,585), Late(267,585)

**Impact:** Very High. Performance is the core gameplay stage. CX has a solid drums implementation but is missing: guitar/bass mode entirely, BGA video, reverse scroll, training mode, MIDI input, scroll speed adjustment during play, and many visual effects.

---

## 6. RESULT STAGE

### NX Layout
```
+--------------------------------------------------+
| [8_header panel.png] (slides in from top)         |
|                                                   |
|              Rank: [SS]  (480,0 center)           |
|              [Stage Cleared/FullCombo/Excellent]   |
|                                                   |
|    Play Speed warning       (840,360)             |
|            +----------+                           |
|            | Jacket   | (467,287)                 |
|            | 245x245  | rotated 0.3 rad           |
|            +----------+                           |
| Song bar (slides in, Y=395)                       |
|  Song Title (0,415)                               |
|                                                   |
| Parameter Panel (score, skill, combo numbers)     |
|  Song Name (500,630)   Artist (500,665)           |
| [8_footer panel.png]                              |
+--------------------------------------------------+
```

### CX Layout
```
+--------------------------------------------------+
| Graphics/8_background.jpg (or DarkBlue fallback)   |
|                                                   |
|         PERFORMANCE RESULTS    (center, Y=100)    |
|                                                   |
|         CLEARED / FAILED       (center, Y=140)    |
|         Score: 1,234,567       (center, Y=180)    |
|         Max Combo: 256         (center, Y=220)    |
|         Accuracy: 95.3%        (center, Y=260)    |
|                                                   |
|         JUDGEMENT BREAKDOWN    (center, Y=300)     |
|         Just: 120              (center, Y=340)     |
|         Great: 80              (center, Y=380)     |
|         Good: 15               (center, Y=420)     |
|         Poor: 5                (center, Y=460)     |
|         Miss: 3                (center, Y=500)     |
|                                                   |
|   Press ESC or ENTER to continue (center, Y=540)  |
+--------------------------------------------------+
```

### Discrepancies

| Element | NX | CX | Status |
|---------|----|----|--------|
| Background | `8_background.jpg` or rank-specific variants (SS/S/A/.../E) | `Graphics/8_background.jpg` with DarkBlue fallback | **MISSING: CX has no rank-specific backgrounds** |
| Background video | `8_background.mp4` via AVI player | Not supported | **MISSING** |
| Header panel | `8_header panel.png` slides in via sine animation | None | **MISSING** |
| Footer panel | `8_footer panel.png` | None | **MISSING** |
| Rank display | `8_rankSS.png` through `8_rankE.png` — large rank letter images with vertical wipe reveal | Text "CLEARED"/"FAILED" in Green/Red | **SEVERELY DOWNGRADED: CX has no rank letters (SS/S/A/B/C/D/E)** |
| Rank calculation | Full rank system (SS/S/A/B/C/D/E based on achievement) | Binary: Cleared/Failed only | **MISSING: CX has no grade ranking system** |
| Achievement overlays | `ScreenResult StageCleared.png`, `fullcombo.png`, `Excellent.png` | None | **MISSING: CX has no achievement overlay graphics** |
| Jacket art | `7_JacketPanel.png` frame at (467,287), jacket 3D rotated 0.3 rad, 245x245 | None | **MISSING: CX has no jacket display on results** |
| Song bar | Slides in from left with bounce, final Y=395, title at (0,415) | None | **MISSING: CX has no animated song bar** |
| Score display | `CActResultParameterPanel` with sprite-based numbers | Text "Score: X,XXX,XXX" centered | **DOWNGRADED: CX uses plain text** |
| Skill value | Displayed in parameter panel | Not shown | **MISSING** |
| Song name/artist | At (500,630) and (500,665) | Not shown separately | **MISSING** |
| Speed warning | At (840, 360) for modified play speed | Not shown | **MISSING** |
| Training warning | At (840, 385) | Not applicable | N/A |
| Score persistence | Saves to songs.db with rank/FC/Excellent | No persistence | **MISSING: CX doesn't save scores** |
| Screenshot | PrintScreen saves result image | Not implemented | **MISSING** |
| Drum pad sounds | Can play drum sounds on result screen | Not implemented | **MISSING** |
| Text rendering | Sprite-based number fonts | BitmapFont console font | **DOWNGRADED** |
| Animations | Header slide-in, song bar bounce, rank reveal wipe | None — static text display | **MISSING: CX has no animations** |
| Input | Enter/CY/RD: advance (or skip anim); ESC: back | Enter or ESC: return to SongSelect | Similar |
| Transition | `CActFIFOWhiteClear` with achievement overlays + star particles | `DTXManiaFadeTransition(0.5)` | **MISSING: CX has no special result transition** |

### NX Result Textures Not in CX

| Texture | Purpose |
|---------|---------|
| `8_background rankSS.png` through `8_background rankE.png` | Rank-specific backgrounds |
| `8_background.mp4` | Background video |
| `8_header panel.png` | Header overlay |
| `8_footer panel.png` | Footer overlay |
| `8_rankSS.png` through `8_rankE.png` | Rank letter graphics |
| `ScreenResult StageCleared.png` | Clear overlay |
| `ScreenResult fullcombo.png` | Full combo overlay |
| `ScreenResult Excellent.png` | Excellent overlay |
| `7_JacketPanel.png` | Jacket frame |
| `7_FullCombo.png`, `7_Excellent.png` | Performance-to-result transition overlays |
| `7_Drums_black.png` | Transition overlay |
| `ScreenPlayDrums chip star.png` | Star particle effect |

**Impact:** High. The Result stage is the most visually reduced stage in CX. It shows only plain text with no visual flair, no rank system, no score persistence, and no animations. This significantly diminishes the sense of achievement after playing.

---

## 7. END STAGE / CHANGE SKIN STAGE

### NX
- **End Stage (`CStageEnd`):** `9_background.jpg` at (0,0), plays `soundGameEnd`, waits ~1 second, exits
- **ChangeSkin Stage (`CStageChangeSkin`):** No visuals, calls `Skin.ReloadSkin()` then immediately returns

### CX
- **No End Stage:** Game exits directly from Title menu
- **No ChangeSkin Stage:** Skin changes not supported as a stage transition

### Discrepancies

| Element | NX | CX | Status |
|---------|----|----|--------|
| Exit screen | Background + game end sound + timed exit | Direct application exit | **MISSING: CX has no graceful exit screen** |
| Skin reload | Dedicated stage with resource reload | Not implemented | **MISSING: CX has no runtime skin switching** |

**Impact:** Low. These are minor quality-of-life features.

---

## 8. STAGE TRANSITIONS COMPARISON

| Transition | NX | CX | Match? |
|-----------|----|----|--------|
| Startup → Title | `CActFIFOWhite` (white tile fade, 500ms) | `StartupToTitleTransition` (1.0s, delayed fade-in) | Different |
| Title → SongSelect | `CActFIFOWhite` fade-out | `DTXManiaFadeTransition(0.7)` cosine-eased | Different style |
| Title → Config | `CActFIFOWhite` | `CrossfadeTransition(0.5)` | Different |
| Config → Title | `CActFIFOWhite` | `CrossfadeTransition(0.3)` | Different |
| SongSelect → Loading | `CActFIFOBlackStart` (750ms, with jacket overlay at (620,40)) | `InstantTransition` | **MISSING: CX has no loading fade with jacket** |
| Loading → Performance | Automatic after loading | `InstantTransition` after 3.0s | Different trigger |
| Performance → Result | `CActFIFOWhiteClear` (2000ms, with achievement overlays + star particles) | `InstantTransition` | **MISSING: CX has no performance-to-result celebration** |
| Result → SongSelect | `CActFIFOWhite` | `DTXManiaFadeTransition(0.5)` | Different style |

**NX transition textures not in CX:**
- `Tile black 64x64.png`, `Tile white 64x64.png` (tile-based fades)
- `6_FadeOut.jpg` (loading fade overlay)
- `7_FullCombo.png`, `7_Excellent.png` (result celebration)
- `7_Drums_black.png`, `ScreenPlayDrums chip star.png` (star particle effects)

**Impact:** Medium. CX has a clean transition system but misses the dramatic performance-to-result celebration transition that NX uses to highlight Full Combo and Excellent achievements.

---

## 9. SUMMARY — ALL STAGES PRIORITY MATRIX

### By Stage Severity

| Stage | CX Completeness | Priority |
|-------|----------------|----------|
| **Title** | ~85% — layout matches, missing drum input + enum indicator | Low |
| **Startup** | ~60% — functional but no progress display | Low |
| **Config** | **~10%** — only 5 of 50+ settings, no categories, no key assignment UI | **Critical** |
| **Song Loading** | ~65% — similar layout, missing part label, different jacket position | Medium |
| **Performance (Drums)** | ~70% — lane positions exact match, core gameplay works, missing BGA/reverse/training/MIDI | **High** |
| **Performance (Guitar/Bass)** | **0%** — not implemented | **Critical** |
| **Result** | **~15%** — plain text only, no ranks, no score save, no animations | **High** |
| **End/ChangeSkin** | 0% — not implemented | Low |

### Top 15 Cross-Stage Gaps (Ordered by Impact)

1. **Guitar/Bass gameplay mode** — entire instrument modes missing
2. **Config categories + 45+ missing settings** — users cannot configure gameplay
3. **Rank system (SS/S/A/B/C/D/E)** — no grade feedback on results
4. **Score persistence** — scores not saved to database
5. **MIDI input support** — drum pads only via keyboard
6. **BGA/AVI video playback** — no background animations during gameplay
7. **Key assignment UI** — no way to configure input mappings in-game
8. **Result animations + overlays** — no visual reward for achievement
9. **Reverse scroll** — important accessibility feature
10. **Training/loop mode** — practice feature
11. **Per-instrument autoplay toggles** — 25 individual pad controls
12. **Sudden/Hidden/Stealth modes** — gameplay modifiers
13. **Configurable judge line position** — accessibility
14. **Performance-to-result transition** — celebration effect
15. **Sort/Search/Quick Config popups** — song management (from Song Selection analysis)
