# HPA-409 — Bravura Notation Glyph Font (Design Spec)

**Issue:** https://linear.app/cwchanap/issue/HPA-409/tab-mode-bravura-notation-glyph-font  
**Parent:** HPA-394 · **Depends on:** none · **Unblocks:** HPA-410  
**Status:** Draft for review (documentation only, 2026-10-19)

## Goal and boundary

Provide a single, tested, redistributable notation glyph source for CX's future scrolling drum staff: black/X/circled-X noteheads, percussion clef, rests, upward flags, triplet 3 and time-signature digits. Keep this to **one implementation PR / at most three engineer days**. HPA-410 owns drawing the strip; HPA-411 owns the config choice and graceful runtime fallback to lanes. Do not wire a new gameplay mode here.

The existing notation model already provides notehead shapes in `DTXMania.Game/Lib/Song/Notation/DrumStaffMapping.cs`; preserve those three shapes rather than adding another channel-to-glyph mapping.

## Verified existing seams

- `DTXMania.Game/Content/Content.mgcb` explicitly enumerates `.spritefont` assets. Existing examples: `Orbitron-24.spritefont` and `NotoSerifJP-Bold.spritefont`; the latter demonstrates an OTF source.
- `ResourceManager.LoadFont(path, size)` calls `ManagedFont.CreateFont`. `ManagedFont.GetBestSpriteFontAssetName` currently only recognizes Orbitron and ShareTechMono; all other names resolve to NotoSerifJP. Thus simply adding Bravura files would NOT load Bravura.
- `ManagedFont.GetBestSizeSpriteFont` silently substitutes NotoSerifJP if an asset fails to load; `ResourceManager.LoadFont` also has a fallback. A successful non-null `IFont` does NOT prove the Bravura glyphs loaded.
- `IFont.SpriteFont` exposes MonoGame's glyph list (`SpriteFont.Characters`) and `IFont.RemoveReference()` owns reference release.
- The Windows and Mac game csproj files each explicitly copy existing OFL notices into output `Licenses/`; both must include Bravura's notice.
- The merged HPA-408 core has `NoteheadShape.Normal`, `X`, `CircledX`; HPA-409 must not touch quantization, chart timing, parser or note identity.

## Proposed solution

### 1. One fixed Bravura SpriteFont asset

Bundle the unmodified official `Bravura.otf` and its accompanying SIL OFL notice from the same **pinned upstream release** (https://github.com/steinbergmedia/bravura/releases), at:

- `DTXMania.Game/Content/Bravura.otf`
- `DTXMania.Game/Content/Bravura-OFL.txt`
- `DTXMania.Game/Content/Bravura-Notation.spritefont`
- One matching MGCB build entry in `Content.mgcb`.

Start with one nominal 40 px regular SpriteFont, no kerning. The later renderer can scale it; do not produce multiple font-size variants before there is a demonstrated need. Encode **individual private-use code points** as single-character regions, plus the two small contiguous digit/rest regions; do not rasterize the whole PUA or set a default replacement character that hides missing glyphs.

Verify the required names/code points against authoritative SMuFL glyph metadata before implementation; the Linear ticket's list was assembled from memory. The planned set is:

| Purpose | SMuFL name(s) | Code point(s) |
| --- | --- | --- |
| Noteheads | noteheadBlack, noteheadXBlack, noteheadCircleX | E0A4, E0A9, E0B3 |
| Clef | unpitchedPercussionClef1 | E069 |
| Rests | restWhole … rest64th | E4E3 … E4E9 |
| Stem-up flags | flag8thUp, flag16thUp, flag32ndUp, flag64thUp | E240, E242, E244, E246 |
| Triplet | tuplet3 | E883 |
| Time signature | timeSig0 … timeSig9 | E080 … E089 |

These are glyph names/code points, **not** font-size or staff-position data. Keep staff lines, stems, beams, barlines and the hit line as rectangles in HPA-410; no extra SMuFL outline assets.

### 2. Minimal glyph contract and positioning

Add `DTXMania.Game/Lib/Song/Notation/NotationGlyphs.cs`:

- A small internal `NotationGlyph` enum for exactly the supported symbols.
- Exhaustive enum → PUA `char` resolution, plus `NoteheadShape` → `NotationGlyph` mapping that reuses the existing shapes (no DTX channel table).
- One documented anchor offset for the notehead/staff attachment point, expressed in nominal-font pixels (or an equivalent normalized unit used consistently by the renderer). The caller positions a notehead **by its musical center**, not the top-left text origin. Rest/flag/clef baselines follow the same documented contract where needed.
- Keep offsets together with glyph definitions; determine the actual values from the rasterized-font spike, not guessed font metrics. No layout engine, font service, or abstraction framework.

### 3. Load via current resource manager, but fail closed

Add one exact `Bravura-Notation` family case to `ManagedFont.GetBestSpriteFontAssetName` so a caller can use `IResourceManager.LoadFont("Bravura-Notation", 40)`. Do **not** change other font-family fallback semantics.

Expose a small glyph-coverage check alongside the glyph mapping. The consumer must accept the loaded `IFont` only if its underlying SpriteFont contains **every required code point**, preventing silent NotoSerifJP substitution and placeholder symbols. A rejected font is released via the existing reference-counting API.

HPA-410 consumes this validated font; HPA-411 logs missing/invalid notation assets and runs lanes mode instead of tab. HPA-409 provides the validation contract and proves it works, but must not edit `PerformanceStage` to install a new mode prematurely.

### 4. Spike and conditional fallback

**Before committing to this rendering path**, build the spritefont and render a specimen containing one of every required glyph at the chosen size in an existing graphics test/dev context on an available target platform. Check exact PUA preservation, clipping, baseline, alpha, and anchor alignment, including flags and rests.

If MonoGame's OTF/PUA import or baseline behavior makes this unusable, stop and **replace** the SpriteFont strategy with one pre-rendered PNG atlas plus an explicit glyph rectangle/anchor table, sourced from the same licensed Bravura. Use the existing texture-loading seam. Do not ship two glyph backends or a runtime renderer selector. Record evidence and the chosen deviation in this spec/PR; do not enlarge this ticket into HPA-410's renderer.

## Scope exclusions

No scrolling, measure placement, beaming, real-time judgement tint, tab-mode config, HUD, note processing, changes to DTXWeb/NX, dynamic text rendering, or additional music fonts. Font licensing and source asset inclusion are part of this task, not a separate art-generation ticket.

## Acceptance / evidence

1. One source of truth maps every planned glyph to a verified code point; a black/X/circled-X notehead matches HPA-408's `NoteheadShape`.
2. MGCB produces the asset on both Windows and macOS, and a captured specimen proves every glyph is present and visually aligned (not a default character).
3. Bravura resolves through the existing font pipeline; a deliberately missing font or missing required glyph is **rejected** rather than accepted as NotoSerifJP.
4. Both desktop publish outputs include the OFL notice.
5. Focused tests check code points, exhaustive glyph coverage validation, font-family selection, and pure anchor contracts. Mac and Windows game builds / relevant tests pass.
6. No player-visible behavior change before HPA-410/HPA-411.

## Sources

- CX font pipeline: `DTXMania.Game/Lib/Resources/{ResourceManager.cs,ManagedFont.cs,IFont.cs}`
- CX content and packaging: `DTXMania.Game/Content/Content.mgcb`, `DTXMania.Game/DTXMania.Game.{Mac,Windows}.csproj`
- SMuFL official specification: https://smufl.formats.music/latest/
- Original Bravura and license: https://github.com/steinbergmedia/bravura
- Roadmap: HPA-394 → (HPA-408 + HPA-409) → HPA-410 → HPA-411 → HPA-509
