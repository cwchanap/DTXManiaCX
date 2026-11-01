# Issue #215 — Result / SongSelect Visual Regression Fixes (Design)

**Issue:** https://github.com/cwchanap/DTXManiaCX/issues/215  
**Source:** 2026-10-28/29 headless playtest, consolidated from #211–#214  
**Baseline inspected:** `main` at `752a4fb3fd8a599ed3efd896a6fc624c9ec16201` (2026-10-31)  
**Delivery:** one PR, starting as this docs-only draft and implementing in the same PR after review; target <=3 engineer days.

## Goal and scope

Make four existing UI elements readable under both the unthemed/default `System/` (NX) skin and bundled `System/CXNeon/`, without touching chart parsing, scoring, score-save behavior, audio, input, stage navigation, or gameplay. Preserve current theme overrides. Use the existing MonoGame/UI primitives; no new art, shaders, asset pipeline, or skin subsystem.

This is a **presentation defect**, not an issue with the values or their underlying calculation.

## Findings verified in current code

1. **Result level/play overlap:** `ResultUILayout.SkillPanel` defaults put the level at (198,550) and playing skill at (238,537). This is primarily **horizontal text overlap**: the NX original drew a compact level bitmap inside a 60×60 difficulty badge and the playing skill as right-padded text beside it (`DTXManiaNX/.../08.Result/CActResultParameterPanel.cs`). CX replaced them with ordinary left-aligned text at nearby X values, without retaining that badge/padding. The 13px Y difference does not separate the glyph bounds. CXNeon already sets `Result.LevelY=525` / `Result.PlayingSkillY=555`.
2. **SongSelect profile compositing and geometry:** `SongSelectionStage.InitializeUI` adds `_playbackProfileLabel` before `SongListDisplay`, whose 1280×720 background draws a 70%-black wash over the entire label. The original label band X=50..690, Y=326..350 also conflicts with CXNeon's preview frame X=250..680, Y=34..347. Both bundled skins render the BPM plate `5_BPM.png` at natural **187×67** from (32,275), occupying X=32..219, Y=275..342; `BPMSection.Height=50` is **not** its real drawn height. The difficulty panel header begins at Y=368, not inside the original 326..350 band. Do not move the grid.
3. **Result playback text over character:** `ResultScreenRenderer.DrawModelText` fixes profile/bucket/status/guidance at X=830, over the right-side character in NX `8_background.jpg`. The proposed left alternative (X=30,Y=108) lies over pale truss/lighting; its light text needs an actual dark backdrop, not a 1px shadow.
4. **Missing jacket is a dropped NX fallback:** `SongBarRenderer.GeneratePreviewImageTexture` returns null for absent/invalid/skipped `#PREIMAGE` and `SongListDisplay` leaves the white slot baked in NX score-bar PNGs visible. Legacy NX's `tGeneratePreviewImageTexture` substitutes `Graphics/5_preimage default.png` when the per-song file is absent. The equivalent is already `TexturePath.ResultDefaultPreview` and exists in both skins: NX uses a recognizable colorful default jacket, CXNeon a dark navy record tile. Restore that display fallback **without changing image-loading or cache semantics**.

## Design decisions

### A. Result: separate level and playing-skill rows

Final fallback level Y=509 (playing skill Y=555) follows the plan's exit-gate clause: the real compiled font has 46px line spacing.
Evidence also justifies changing the existing `Result.ValueText` default White→#303030; CXNeon's explicit override is unaffected, while dark third-party skins without the key inherit charcoal (accepted risk).

| Element | Current default | New default | CXNeon existing override |
| --- | --- | --- | --- |
| Chart level | (198, 550) | **(198, 509)** | Y=525 |
| Playing skill | (238, 537) | **(238, 555)** | Y=555 |
| Game skill | (268, 623) | unchanged | Y=632 |

Keep both X values, fonts, number formatting and the existing independent `Result.LevelX/Y` / `Result.PlayingSkillX/Y` keys. The final default separation is 46px; test **non-overlapping rendered text bounds (using the actual value-font line height)**, not just `ΔY==46`. The stacked presentation intentionally differs from NX's original side-by-side badge layout and must be verified against the bundled art.

### B. SongSelect: profile always foreground in a clear left band

- Start with `SongSelectionUILayout.UILabels.PlaybackProfile` at **X=20, Y=342, Width=224, Height=24** (1280×720 virtual-space defaults). Right edge X=244 clears the preview-frame left X=250 by 6px on both skins; top Y=342 starts at the **actual** bottom of the natural 187×67 BPM texture (origin 32,275); bottom Y=366 precedes the difficulty-header top Y=368.
- The status panel starts at (130,350) and its bundled background is near-transparent. The new label therefore crosses only its unused top-left band, but **verify the real pixels** under both skins before locking the location; its text must not cover status content. Do not derive the BPM collision check from the existing `BPMSection.Height=50`. Capture the true drawn height in a clearly named native texture-height contract/test (67px).
- Add `_playbackProfileLabel` **after** song list/status/history/preview children and **before** the search modal. Besides panel overlaps, this fixes the 70%-black `SongListDisplay` overlay obscuring the label. The search modal remains topmost.
- Keep `PlaybackProfileFormatter.Format`, play-speed/pitch synchronization, and the label's size reassertion after text updates. Retain the existing 16px font unless the full supported text range exceeds 224px **in the actual rendered font**; then make a small local font/geometry adjustment, not truncation or a new formatter.
- Do not relocate the difficulty grid or resize preview art. No label-specific theme keys are needed unless screenshots prove the shared band unusable.

### C. Result: one theme-adjustable playback/status group on a legible backdrop

Add **two optional theme keys**, `Result.PlaybackInfoX` and `Result.PlaybackInfoY`, resolving one group origin and deriving all existing positions via independent `ISkinTheme.GetInt` lookups:

| Element | Offset from group origin | Proposed NX origin (30,108) | CXNeon origin (830,310) |
| --- | --- | --- | --- |
| Playback profile | (0, 0) | (30,108) | (830,310) |
| Score bucket | (0, 26) | (30,134) | (830,336) |
| Score save status | (0, 60) | (30,168) | (830,370) |
| Failure guidance rect | (0, 84), 420x60 | (30,192,420,60) | (830,394,420,60) |

- The NX group is below the existing score (30,58), above the skill-panel start Y=260 and clear of right-side character art. However, `8_background.jpg` is **bright at X=30..450, Y=108..252**; `LightSteelBlue`/`LightGray` text and status colours need sufficient contrast against the white truss and pale yellow beam.
- Draw **one translucent dark rectangle behind the group** within the Result renderer, using the existing stage-owned 1×1 `ResultStage._whitePixel` passed as a *borrowed* optional texture to `ResultScreenRenderer`. Do not allocate/own another texture or separate renderer. Use a modest inset/margin and size the backdrop to the normal three rows, extending vertically for the 420×60 guidance bounds **only when guidance is present**. The NX fallback plate ends at X=420 to clear FAILED; CXNeon's theme-derived origin retains the full width. Keep its bottom clear of the skill panel at Y=260, honor existing result reveal behaviour, and draw it before group text. One backdrop supports wrapped guidance without repeated text-shadow draws.
- CXNeon sets `Result.PlaybackInfoX=830` / `Result.PlaybackInfoY=310` in `System/CXNeon/Theme.ini`, retaining the existing group location. Check whether the common translucent backdrop is acceptable on its dark background; introduce a theme-colour override **only if screenshots show it is necessary**.
- Keep the existing `ResultUILayout.PlaybackPresentation` colours, save-state mapping, score and per-row offsets; use the same `GetInt` coordinate convention as other Result resolvers, not a new layout/parser API. Do not introduce independent keys for each line or new persisted settings. Longer error text still wraps within the existing guidance rectangle.

### D. Song bars: restore bundled default preview art

- Load **one optional default bar preview** (`TexturePath.ResultDefaultPreview` / `Graphics/5_preimage default.png`) alongside existing skin bar textures in `SongListDisplay`. Use the established `ResourceExists` / `LoadTexture` pattern; release its resource-manager reference on reinitialization, skin reload and disposal. Both bundled skins already ship it; missing custom-skin asset should safely retain existing no-image behavior.
- Reuse the **existing 44×44 preview destination rectangle and draw call**: for a score bar, draw `barInfo.PreviewImage ?? _defaultBarPreview`; for box/other bars, no fallback. A genuine preview always wins. Apply the same default-art behaviour to the basic/fallback score-bar path where it draws skin artwork without a generated preview.
- No new slot-bounds helper, dark fill, colour constant, theme key, per-song texture generation, image I/O or ref-count change. Keep current lamp/title draw order, opacity and fast-scroll behaviour; when scrolling suppresses a real jacket, the shared default may be displayed temporarily.
- The NX default jacket is **colourful, not dark**; the CXNeon default is dark. Accept this as intentional skin artwork only if the 44×44 screenshots make a no-jacket state clearly recognizable and remove the glitch-like empty-white slot. If NX still looks broken at thumbnail size, report evidence rather than silently adding new art/overlays.
- Do **not** change `SongBarRenderer.GeneratePreviewImageTexture` null/file-size/cache/ref-count semantics, the larger `PreviewImagePanel` fallback, or Result's default-preview loading.

## Validation contract (real screenshots are required per step)

Use the existing GameApi/desktop screenshot path; no new harness. For each implementation task (Result, SongSelect label, song-bar fallback), capture and inspect a **1280×720 default-NX** screenshot **before proceeding to the next task**. The final sweep adds CXNeon, a second window size and links/attaches evidence to the PR. Check full-frame composition rather than isolated coordinates.

1. Result with a normal saved score: level and play values occupy independent glyph bounds at Y=509/555; group and backing are readable on pale NX artwork and do not collide with rank, plate or skill panel. CXNeon's original group coordinates remain unchanged.
2. Result with **failed save**: backdrop extends around wrapped retry guidance only when needed, status colours remain meaningful, no text/box overlaps the skill panel; non-failed status has the shorter backing.
3. SongSelect with default and extreme supported speed/pitch values: full label is visible despite `SongListDisplay`'s background, no overlap with the **native 67px** BPM texture, preview frame, status values or difficulty header. Search modal remains in front.
4. Song wheel without `#PREIMAGE`, with missing file and with genuine preview: selected/unselected score bars display the correct bundled fallback or actual jacket (including fast scroll and basic path where feasible); lamps/title unaffected and folders/other bars unchanged. Check NX's bright default art still reads as intentional at 44×44.
5. Repeat at another window size to verify the game's virtual scaling, and on CXNeon verify backing and default tile aesthetics.

Add focused **real font-height/layout**, resolver, resource-lifetime and existing renderer-mock tests as appropriate. A pure bounds test **cannot prove** the final rendering; evidence is required.

## Non-goals and delivery guardrails

No changes to HPA-16 screenshots, playback profile values, score buckets, score saving, skins' source art, DTXCreator, game loop, database, audio, or chart format. No new art/SFX is needed: both skins already contain the default preview image, and the Result backdrop reuses an existing pixel texture. Keep **one implementation PR** for this issue: extend this docs draft after review. If screenshots identify an actual new art request, track it separately instead of adding art generation to this fix.
