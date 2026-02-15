# Song Selection Stage — UI Layout Discrepancy Analysis

**DTXmaniaNX (Original) vs DTXManiaCX (Rewrite)**

Screen resolution: **1280x720** for both projects.

---

## 1. Draw Order / Layer Stack

| Layer | NX (Original) | CX (Rewrite) | Discrepancy |
|-------|---------------|--------------|-------------|
| 1 | Background Video (`5_background.mp4`) | Background texture or gradient fallback | **MISSING: CX has no background video (AVI/MP4) support** |
| 2 | Background Image (`5_background.jpg`) at (0,0) — only if no video | Background image (`5_background.jpg`) at (0,0) with gradient fallback | Equivalent |
| 3 | BPM Label texture (`5_BPM.png`) at (32, 258) | BPM section drawn inside Status Panel | Different: NX draws BPM label independently on screen; CX only inside status panel |
| 4 | Preview Image Panel | Preview Image Panel | Equivalent (see Section 7) |
| 5 | Artist/Comment Bar | Comment Bar (partial) | **PARTIAL: CX missing scrolling artist display** (see Section 8) |
| 6 | Song List (13 bars) | Song List (13 bars) | Differences in bar content (see Section 3) |
| 7 | Status Panel | Status Panel | Differences in content (see Section 6) |
| 8 | Performance History Panel | — | **MISSING: CX has no play history panel** |
| 9 | Top Header Panel (`5_header panel.png`) with slide-in animation | Header Panel at (0,0) — static | **MISSING: CX has no entrance slide-in animation for header** |
| 10 | Information Ticker | — | **MISSING: CX has no rotating information ticker** |
| 11 | Bottom Footer Panel | Footer Panel at (0, 720-h) | Equivalent |
| 12 | Preview Sound (audio) | Preview Sound (audio) | Equivalent (see Section 9) |
| 13 | Scrollbar | — | **MISSING: CX has no scrollbar indicator** |
| 14 | Sort Menu popup | — | **MISSING: CX has no sort menu** |
| 15 | Quick Config popup | — | **MISSING: CX has no quick config popup** |
| 16 | Search Text Box popup | — | **MISSING: CX has no search/filter feature** |
| 17 | Search Notification | — | **MISSING: CX has no search notification** |
| — | — | Title Label at (50, 100) | **CX-ONLY: Static "Song Selection" title label not in NX** |
| — | — | Breadcrumb Label at (50, 150) | **CX-ONLY: Breadcrumb path label not in NX** |

**Summary:** CX is missing 8 UI elements/layers that NX has: Background Video, Information Ticker, Performance History Panel, Scrollbar, Sort Menu, Quick Config, Search TextBox, and the animated header entrance. CX adds 2 elements not in NX: Title Label and Breadcrumb Label.

---

## 2. Song Bar Positioning (13-Bar Layout)

Both use 13 visible bars with the selected item at index 5. The Y-coordinates match exactly between NX and CX:

| Bar | Y (NX) | Y (CX) | Match? |
|-----|--------|--------|--------|
| 0 | 5 | 5 | Yes |
| 1 | 56 | 56 | Yes |
| 2 | 107 | 107 | Yes |
| 3 | 158 | 158 | Yes |
| 4 | 209 | 209 | Yes |
| **5 (selected)** | **270** | **270** | **Yes** |
| 6 | 362 | 362 | Yes |
| 7 | 413 | 413 | Yes |
| 8 | 464 | 464 | Yes |
| 9 | 515 | 515 | Yes |
| 10 | 566 | 566 | Yes |
| 11 | 617 | 617 | Yes |
| 12 | 668 | 668 | Yes |

### X-Coordinate Discrepancies

| Property | NX | CX | Discrepancy |
|----------|----|----|-------------|
| Unselected Bar X | 673 | 723 | **CX bars are 50px further right** |
| Selected Bar X | 665 | 715 | **CX selected bar is 50px further right** |
| Selected-to-Unselected offset | 8px left indent | 8px left indent | Same relative offset |
| Bar Width (max) | 510px | 510px | Same |

### Entrance Animation

| Feature | NX | CX |
|---------|----|----|
| Slide-in from right | Yes — sinusoidal easing using curved X coordinates from `ptバーの基本座標` | **NO — bars appear immediately at fixed X positions** |
| Staggered animation | Yes — each bar offset by -10 counter units | **NO** |
| Curved layout during animation | Yes — bars trace a diamond/chevron path from right edge | **NO** |

**Impact:** CX loses the distinctive slide-in animation that gives the song list a dynamic, polished feel when the stage activates.

---

## 3. Individual Song Bar Content

### Bar Size

| Property | NX | CX | Discrepancy |
|----------|----|----|-------------|
| Bar height (approx) | ~48px (based on textures) | 30px | **CX bars are significantly shorter (30px vs ~48px)** |
| Bar background | Skin texture (`5_bar score.png` etc.) | Programmatically generated (solid color + indicators) | **CX has no skin-based bar textures** |

### Bar Visual Elements

| Element | NX | CX | Discrepancy |
|---------|----|----|-------------|
| Bar background texture | 3 types x 2 states = 6 skin textures | Solid color fills (3 types: Song/Folder/Special) | **MISSING: CX has no skin-based bar textures** |
| Preview thumbnail | 44x44px at (barX+31, barY+2) | 24x24px after clear lamp | **CX thumbnail is smaller (24px vs 44px)** |
| Clear lamp indicator | 7x41px generated texture with 5 colored slots stacked vertically (per difficulty) | 8x24px single color strip (left edge) | **SIMPLIFIED: CX shows one lamp color instead of 5-difficulty stack** |
| Song title text | 2x rendered, 0.5x scaled (anti-aliased), configurable font, max 510px, compression for long titles | BitmapFont (8x16 console font), 1.0 scale, with 1px shadow | **DOWNGRADED: CX uses console font instead of configurable high-quality font** |
| Title max width | 510px with horizontal compression | Bar width | Similar |
| Skill value | Exists (commented out in code) | Not implemented | N/A (disabled in NX too) |
| Node type indicator | Implied by bar texture type | 4px colored strip at right edge (White/Cyan/Orange/Magenta) | CX-only visual |
| Selection border | Implied by selected texture variant | Yellow border (center) / White border (selected) | Different approach |

### Bar Color Scheme

| Bar Type | NX (texture-based) | CX (programmatic) |
|----------|--------------------|--------------------|
| Score normal | `5_bar score.png` | RGB(40, 40, 60) dark blue-gray |
| Score selected | `5_bar score selected.png` | RGB(80, 120, 200) medium blue |
| Score center | (same as selected) | RGB(120, 160, 255) bright blue |
| Box/Folder | `5_bar box.png` | RGB(40, 60, 40) dark green |
| Other/Random | `5_bar other.png` | RGB(60, 40, 60) dark purple |

### Text Colors

| Node Type | NX | CX |
|-----------|----|----|
| Score normal | White | White |
| Score selected | (texture highlight) | Yellow |
| Box/Folder | (texture-based) | RGB(150, 255, 150) green |
| BackBox | (texture-based) | RGB(255, 200, 150) orange |
| Random | (texture-based) | Magenta |

### Display Text Formatting

| Node Type | NX | CX | Discrepancy |
|-----------|----|----|-------------|
| BackBox | (uses bar texture) | `".. (Back)"` | CX adds explicit text |
| Box | (song title from data) | `"[FolderName]"` with brackets | CX adds brackets |
| Random | (from data) | `"*** RANDOM SELECT ***"` | CX adds decorative text |

---

## 4. Selected Song Extra Info (Below Song List)

| Element | NX | CX | Discrepancy |
|---------|----|----|-------------|
| Large song title | At (60, 490), GITADORA gradient (cyan-to-yellow), 30px font, max 600px | Not present | **MISSING in CX** |
| Artist name (large) | At (60, 545), GITADORA gradient, 15px font, max 600px | Small text below selected bar, right-aligned, LightGray, 200px max | **DOWNGRADED: CX shows artist small and right-aligned instead of large and below the list** |
| Item counter | At (1260, 620), right-aligned, digit sprites 16x16 | Not present | **MISSING in CX: No "current/total" position indicator** |

---

## 5. Artist and Comment Bar

| Element | NX | CX | Discrepancy |
|---------|----|----|-------------|
| Comment bar texture | `5_comment bar.png` at (560, 257) | `5_comment bar.png` at (560, 257) | Same position |
| Comment bar fallback | None (requires texture) | Blue rectangle 510x80px | CX has fallback |
| Artist name | Right-aligned at (1260-25-textW, 320), MS PGothic 40px, max 510px, 2x render 0.5x display | Not shown on comment bar | **MISSING: CX has no artist name on the comment bar** |
| Comment text | At (683, 339), horizontally scrolling if long, 510px display width, 750px clip during scroll | At (683, 339), max 510px, 0.5x font scale | **MISSING: CX has no horizontal scroll animation for long comments** |

---

## 6. Status Panel

### Panel Background

| Property | NX | CX | Discrepancy |
|----------|----|----|-------------|
| Texture | `5_status panel.png` at (130, 350) | `5_status panel.png` at (130, 350) | Same position |
| Size | Determined by texture | 580x320 | CX has explicit size |
| Fallback | None | Generated panel (Black 0.8f + blue border) | CX has fallback |

### BPM Section

| Property | NX | CX | Discrepancy |
|----------|----|----|-------------|
| BPM label texture | `5_BPM.png` at (32, 258) — drawn by main stage | `5_BPM.png` at (32, 258) — drawn inside status panel | Different owner |
| Song duration | At (BPM_X+42, BPM_Y-7) using `5_bpm font.png` sprite digits | At (107, 258) as text "M:SS" | **CX uses text instead of sprite font for duration** |
| BPM value | At (BPM_X+45, BPM_Y+23) using `5_bpm font.png` sprite digits | At (107, 278) as text | **CX uses text instead of sprite font for BPM** |
| Standalone position | (490, 385) when no status panel | (490, 385) same | Same |

### Skill Point Panel

| Property | NX | CX | Discrepancy |
|----------|----|----|-------------|
| Background | `5_skill point panel.png` at (32, 180) | DarkBlue 0.7f rectangle at (32, 180) | **CX has no skin texture for skill point panel** |
| Value position | (92, 200) using difficulty number font (28x42 per char) | (92, 200) in Yellow text | **CX uses plain text instead of sprite font** |

### Difficulty Grid (3x5 Grid)

| Property | NX | CX | Discrepancy |
|----------|----|----|-------------|
| Layout | 3 columns (D/G/B) x 5 rows, 60px row spacing | 3 columns x 5 rows, 60px row spacing, cell 187x60 | Same structure |
| Background | `5_difficulty panel.png` divided into 3 columns | `5_difficulty panel.png` | Same |
| Selection frame | `5_difficulty frame.png` on active instrument+difficulty | `5_difficulty frame.png` on selected cell | Same |
| Rank icons | `5_skill icon.png` spritesheet (10 icons, 35px each) — shows rank, FC, Excellent | Not present | **MISSING: CX has no rank/FC/Excellent icons in the grid** |
| Level number | `5_level number.png` sprite digits (20x28), format "0.00" (Gitadora) or "00" (Classic) | `6_LevelNumber.png` bitmap font or SpriteFont fallback, format "F2" (e.g., "3.80") | Similar but different font approach |
| Achievement rate | `5_skill number.png` digits (12x20), format "##0.00%" | Not present | **MISSING: CX has no achievement rate display per difficulty** |
| Achievement MAX | `5_skill max.png` when 100% | Not present | **MISSING** |
| Skill badge | Drawn at (boxX+75, boxY+5) for highest-SP difficulty | Not present | **MISSING** |
| Level display modes | Classic ("00") and Gitadora ("0.00") | Single mode ("0.00") | **MISSING: CX has no Classic integer mode** |
| "No data" display | "--" or "-.--" when no score | Cells skipped if no chart | Different approach |

### Graph Panel (Note Distribution)

| Property | NX | CX | Discrepancy |
|----------|----|----|-------------|
| Base position | (15, 368) | (15, 368) | Same |
| Drums background | `5_graph panel drums.png` — 9-lane chart | `5_graph panel drums.png` — 10-lane chart | CX has 10 lanes vs NX 9 |
| Guitar/Bass background | `5_graph panel guitar bass.png` — 6-lane chart | `5_graph panel guitar bass.png` — 6-lane chart | Same |
| Drum bar colors | PaleVioletRed, DeepSkyBlue, HotPink, Yellow, Green, MediumPurple, Red, Orange, DeepSkyBlue | Purple(LC), Yellow(HH), Purple(LP), Red(SD), Blue(HT), Orange(BD), Blue(LT), Green(FT), Cyan(CY) | Different color scheme |
| Guitar/Bass bar colors | Red, Green, DeepSkyBlue, Yellow, HotPink, White | Red, Green, Blue, Yellow, Purple, Orange | Different color scheme (especially P and Pick) |
| Drum bar spacing | 8px spacing, 4px width | 4px spacing, 4px width | **CX has tighter bar spacing** |
| Guitar bar spacing | 10px spacing, 4px width | 10px spacing, 4px width | Same |
| Max bar height | 252px | 252px | Same |
| Total notes counter | At (graphBase+66, graphBase+298) using BPM font sprites | At (81, 666) in text | Different position and rendering |
| Progress bar | (graphBase+18, graphBase+21), 4px wide, 294px tall | (33, 389), 5x120px, Green fill based on playCount/10 | **Different size and meaning** — NX shows some progress, CX shows play count ratio |

### Difficulty Labels

| Property | NX | CX | Discrepancy |
|----------|----|----|-------------|
| Label display | 5 labels across top, X = base + (i * 110), Red=selected, White=others | Not visible as separate labels | **MISSING: CX has no visible difficulty name labels across the top** |
| Label rendering | `CCharacterConsole` text | N/A | |

---

## 7. Preview Image Panel

| Property | NX | CX | Discrepancy |
|----------|----|----|-------------|
| With status panel — position | (250, 34), image offset +8,+8 | (250, 34), image offset +8,+8 | Same |
| With status panel — size | 292x292 | 292x292 | Same |
| Without status panel — position | (18, 88), image offset +37,+24 | (18, 88), image offset +37,+24 | Same |
| Without status panel — size | 368x368 | 368x368 | Same |
| Panel frame texture | `5_preimage panel.png` | None (uses border) | **MISSING: CX has no frame texture, uses 2px white border** |
| Default image | `5_preimage default.png` | `5_default_preview.png` | Different filename |
| Fade-in animation | Scale 0.9x→1.0x with opacity fade | No animation (instant display) | **MISSING: CX has no fade-in animation** |
| Display delay | Configurable ms from config | 500ms hardcoded | Similar concept, different implementation |
| Video preview | Supports #PREMOVIE | Not supported | **MISSING: CX has no video preview** |
| Fallback chain | #PREIMAGE → #PREMOVIE → background crop | Search: preview.jpg/png, jacket.*, banner.* | Different fallback strategy |
| Placeholder | (none visible) | Dark gray cross pattern | CX has placeholder |

---

## 8. Missing Components in CX

### 8a. Performance History Panel

NX has `CActSelectPerfHistoryPanel`:
- Texture: `5_play history panel.png`
- Position: (700, 570) with status panel, (210, 570) without
- Shows last 5 play records
- Font: Meiryo 26px Bold, Yellow, 2x render

**CX:** Completely absent. No play history shown on song selection screen.

### 8b. Information Ticker

NX has `CActSelectInformation`:
- Texture: `5_information.png` (Japanese) / `5_informatione.png` (English)
- Position: (4, 0)
- 8 frames of 240x42px, 6-second cycle, 250ms vertical slide transition

**CX:** Completely absent. No rotating information banner.

### 8c. Scrollbar Position Indicator

NX has `CActSelectShowCurrentPosition`:
- Texture: `5_scrollbar.png`
- Position: X = 1280-24+50 = 1306, Y = 120
- Track: 12x492px, Indicator: 12x12px

**CX:** Completely absent. No visual indication of position within the song list.

### 8d. Sort Menu

NX has `CActSortSongs`:
- Sort by: Title, Level, Best Rank, PlayCount, Author, SkillPoint, Date
- Each option has ascending/descending toggle
- Background: `ScreenSelect sort menu background.png` at (460, 150)

**CX:** Completely absent. No sorting capability.

### 8e. Quick Config Popup

NX has `CActSelectQuickConfig`:
- Options: Target (D/G/B), Auto Mode, ScrollSpeed, Dark, Risky, PlaySpeed, HID/SUD, AUTO Ghost, Target Ghost, More...
- Auto settings panel at (486, 320)

**CX:** Completely absent. No quick config from song selection.

### 8f. Search Text Box

NX has `CActTextBox`:
- Position: (390, 200), 500x40px
- Supports IME input, clipboard, history navigation
- Search help panel at (390, 260), 500x350px

**CX:** Completely absent. No search/filter functionality.

---

## 9. Preview Sound (Audio)

| Property | NX | CX | Discrepancy |
|----------|----|----|-------------|
| Preview sound support | Yes | Yes | Both supported |
| Play delay | Configurable from config | 1.0 second hardcoded | |
| Looping | Yes | Yes | Same |
| BGM interaction | Not detailed | BGM fades out to 10% over 0.5s, back in over 1.0s | CX has explicit BGM fade logic |

---

## 10. Input Handling

| Input | NX | CX | Discrepancy |
|-------|----|----|-------------|
| Up/Down | Scroll list (keyboard + drums HT/LT + guitar R/G) | Scroll list (keyboard only) | **MISSING: CX has no drum/guitar pad navigation** |
| Enter | Select/enter BOX (keyboard + CY/RD drums + Decide) | Select/enter BOX (keyboard only) | **MISSING: CX has no drum/guitar pad selection** |
| ESC | Exit BOX or title (keyboard + LC drums + Cancel) | Exit BOX or title (keyboard only) | **MISSING: CX has no drum/guitar pad cancel** |
| Difficulty cycle | HHx2 (drums) / Bx2 (guitar/bass) | Left/Right arrows (keyboard) | Different mechanism |
| Quick Config | BDx2 (drums) / Px2 (guitar/bass) | Not available | **MISSING** |
| Sort Menu | FTx2 (drums) / Y+P (guitar/bass) | Not available | **MISSING** |
| Swap G/B keys | Yx2 (guitar/bass) | Not available | **MISSING** |
| Search | Search key trigger | Not available | **MISSING** |
| Config shortcut | Shift+F1 | Not available | **MISSING** |
| Status panel toggle | N/A (always visible) | Enter on Score toggles status panel | CX has a two-step selection flow |

---

## 11. Difficulty System

| Property | NX | CX | Discrepancy |
|----------|----|----|-------------|
| Number of difficulties | 5 slots (indices 0-4) | 5 slots (indices 0-4) | Same |
| Difficulty labels | 12 named labels (DTXMANIA, DEBUT, NOVICE, REGULAR, EXPERT, MASTER, BASIC, ADVANCED, EXTREME, RAW, RWS, REAL) | None visible | **MISSING: CX has no difficulty label names** |
| Sound effects | Per-difficulty SFX (Novice.ogg, Regular.ogg, etc.) | No per-difficulty sound | **MISSING** |
| Anchor difficulty | Yes — remembers preferred level, finds nearest available | Not implemented | **MISSING: CX has no difficulty preference memory** |
| Instrument switching | Via configured keybinds (drum pads, guitar buttons) | Not exposed in UI | **MISSING: CX has no in-stage instrument switching** |

---

## 12. Skin/Texture Resource Comparison

### Textures Used by NX (Song Selection)

| Texture | Used in CX? | Notes |
|---------|-------------|-------|
| `5_background.jpg` | Yes | Same |
| `5_background.mp4` | **No** | Background video not supported |
| `5_header panel.png` | Yes | Same |
| `5_footer panel.png` | Yes | Same |
| `5_BPM.png` | Yes | Same |
| `5_bar score.png` | **No** | CX uses generated colors |
| `5_bar box.png` | **No** | CX uses generated colors |
| `5_bar other.png` | **No** | CX uses generated colors |
| `5_bar score selected.png` | **No** | CX uses generated colors |
| `5_bar box selected.png` | **No** | CX uses generated colors |
| `5_bar other selected.png` | **No** | CX uses generated colors |
| `5_header song list.png` | **No** | Not used |
| `5_footer song list.png` | **No** | Not used |
| `5_preimage default.png` | Renamed | CX: `5_default_preview.png` |
| `5_preimage panel.png` | **No** | CX uses border instead |
| `5_status panel.png` | Yes | Same |
| `5_difficulty panel.png` | Yes | Same |
| `5_difficulty frame.png` | Yes | Same |
| `5_skill icon.png` | **No** | Rank icons not implemented |
| `5_skill max.png` | **No** | Achievement MAX not implemented |
| `5_skill number.png` | **No** | Achievement rate not implemented |
| `5_level number.png` | Renamed | CX: `6_LevelNumber.png` |
| `5_bpm font.png` | **No** | CX uses text rendering |
| `5_skill point panel.png` | **No** | CX uses generated rectangle |
| `5_graph panel drums.png` | Yes | Same |
| `5_graph panel guitar bass.png` | Yes | Same |
| `5_skill number on gauge etc.png` | **No** | Item counter not implemented |
| `5_comment bar.png` | Yes | Same |
| `5_play history panel.png` | **No** | History panel not implemented |
| `5_information.png` | **No** | Info ticker not implemented |
| `5_informatione.png` | **No** | Info ticker not implemented |
| `5_scrollbar.png` | **No** | Scrollbar not implemented |
| `ScreenSelect sort menu background.png` | **No** | Sort menu not implemented |
| `ScreenConfig menu cursor.png` | **No** | Popup menu not implemented |
| `ScreenSelect popup auto settings.png` | **No** | Quick config not implemented |

**Summary:** Of 34 NX textures, CX uses 9 directly, 2 renamed, and **23 are unused/unsupported**.

---

## 13. Visual Quality Comparison

| Aspect | NX | CX | Assessment |
|--------|----|----|------------|
| Text rendering | 2x render, 0.5x display (anti-aliased), configurable fonts (MS PGothic, etc.) | BitmapFont (8x16 console font) or SpriteFont | **NX significantly higher quality** — CX console font looks retro/programmer-ish |
| Bar textures | Skin-based PNG textures with gradient/shadow effects | Solid color rectangles with simple borders | **NX more polished** — CX looks utilitarian |
| Animations | Entrance slide-in, preview fade-in, info ticker cycling, scrollbar tracking | None (static positioning) | **NX much more dynamic** |
| Color theming | Defined by skin textures | Hardcoded in DTXManiaVisualTheme | CX has centralized theme system (advantage for customization) |
| Layout responsiveness | Fixed 1280x720 | Fixed 1280x720 | Same |
| Fallback handling | Requires skin textures present | Generated fallback textures for all elements | **CX more robust** — works without skin files |

---

## 14. Summary of Priorities

### Critical Missing Features (Gameplay Impact)
1. **Sort Menu** — Users cannot organize large song collections
2. **Quick Config Popup** — Must exit to config for any setting change
3. **Search/Filter** — No way to find songs in large libraries
4. **Rank/Achievement Display** — No visible progress tracking per difficulty
5. **Instrument Switching** — Cannot change D/G/B target from song selection

### Important Visual Gaps
6. **Bar Textures** — Skin-based bars vs programmatic (affects skinning community)
7. **Text Quality** — Console font vs anti-aliased configurable font
8. **Entrance Animation** — Static vs dynamic bar slide-in
9. **Performance History** — No way to see recent play records
10. **Item Counter / Scrollbar** — No position awareness in large lists

### Nice-to-Have
11. Background video playback
12. Information ticker
13. Preview image fade-in animation
14. GITADORA-style gradient text for selected song title
15. Per-difficulty sound effects
16. Anchor difficulty memory
17. Preview frame texture (`5_preimage panel.png`)
18. Difficulty label names (BASIC/ADVANCED/EXTREME etc.)
