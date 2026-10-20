# HPA-409 — Bravura Notation Glyph Font (Design Spec)

**Issue:** https://linear.app/cwchanap/issue/HPA-409/tab-mode-bravura-notation-glyph-font  
**Parent:** HPA-394 · **Depends on:** none · **Unblocks:** HPA-410  
**Status:** Draft for review (documentation only, 2026-10-19)

## Goal and boundary

Provide a single, tested, redistributable notation glyph source for CX's future scrolling drum staff: duration-appropriate whole/half/black, X and circled-X noteheads, percussion clef, rests, upward flags, triplet 3 and time-signature digits. Keep this to **one implementation PR / at most three engineer days**. HPA-410 owns drawing the strip; HPA-411 owns the config choice and graceful runtime fallback to lanes. Do not wire a new gameplay mode here.

The existing notation model already provides notehead shapes in `DTXMania.Game/Lib/Song/Notation/DrumStaffMapping.cs`; preserve those three shapes rather than adding another channel-to-glyph mapping.

## Verified existing seams

- `DTXMania.Game/Content/Content.mgcb` explicitly enumerates `.spritefont` assets. Existing examples: `Orbitron-24.spritefont` and `NotoSerifJP-Bold.spritefont`; the latter demonstrates an OTF source.
- `ResourceManager.LoadFont(path, size)` calls `ManagedFont.CreateFont`. `ManagedFont.GetBestSpriteFontAssetName` currently only recognizes Orbitron and ShareTechMono; all other names resolve to NotoSerifJP. Thus simply adding Bravura files would NOT load Bravura.
- `ManagedFont.GetBestSizeSpriteFont` silently substitutes NotoSerifJP if an asset fails to load; `ResourceManager.LoadFont` also has a fallback. A successful non-null `IFont` does NOT prove the Bravura glyphs loaded.
- `IFont.SpriteFont` exposes MonoGame's glyph list (`SpriteFont.Characters`), `IFont.SupportsCharacter(char)` is the established character-coverage API, and `IFont.RemoveReference()` owns reference release.
- `ManagedFont.BuildCharacterCache` currently scans selected Unicode ranges but **not the SMuFL Private Use Area**. Thus `SupportsCharacter` can return false even when the loaded SpriteFont contains a Bravura glyph. `DrawString` first attempts the original text and only calls `SanitizeText` after an `ArgumentException`; this is a **coverage API inconsistency**, not unconditional glyph replacement. Correct the shared cache before using `SupportsCharacter` for notation validation.
- The Windows and Mac game csproj files each explicitly copy existing OFL notices into output `Licenses/`; both must include Bravura's notice. Existing `DTXMania.Test/Resources/FontLicenseFilesTests.cs` verifies notice files and project copy declarations; extend it rather than creating another packaging test.
- `Song/Notation` currently contains GPU-free data/pure logic. Keep `NotationGlyphs` entirely independent of `IFont`, `Lib.Resources`, `Microsoft.Xna.Framework` and graphics types; the UI/resource integration passes a glyph predicate into its validation method.
- The merged HPA-408 core has `NoteheadShape.Normal`, `X`, `CircledX` and `NotationEntry.DurationTicks`/`NotationTuplet.SlotTicks`; HPA-409 must not touch quantization, chart timing, parser or note identity.
- DTXWeb's `packages/dtx-web/src/lib/components/preview/NotationView.svelte` maps notation durations `192, 96, 48, 24, 12, 6, 3` ticks to whole, half, quarter, eighth, 16th, 32nd and 64th, respectively; a non-exact duration takes the largest supported value no greater than its ticks, or 64th if below 3. Tuplet entries use the displayed base duration `SlotTicks * 3 / 2`. The existing plan's black-only noteheads would incorrectly represent sustained half/whole entries.

## Proposed solution

### 1. One fixed Bravura SpriteFont asset

Bundle the unmodified official `Bravura.otf` and its accompanying SIL OFL notice from the same **pinned upstream release** (https://github.com/steinbergmedia/bravura/releases), at:

- `DTXMania.Game/Content/Bravura.otf`
- `DTXMania.Game/Content/Bravura-OFL.txt`
- `DTXMania.Game/Content/Bravura-Notation.spritefont`
- One matching MGCB build entry in `Content.mgcb`.

Start with one **40-point** regular SpriteFont, no kerning. MonoGame `.spritefont` `<Size>` is in points, not physical pixels; the compiled glyph height, baseline and pixels-per-staff-space must be measured on the actual rendered font. The later renderer can scale it; do not produce multiple font-size variants before there is a demonstrated need. Encode **individual private-use code points** as single-character regions, plus the two small contiguous digit/rest regions; do not rasterize the whole PUA or set a default replacement character that hides missing glyphs.

The planned name/code-point table below was cross-checked during design review against the W3C SMuFL `glyphnames.json`. The implementing agent should preserve it and pin the source/version used for the bundled font rather than modifying values from memory:

| Purpose | SMuFL name(s) | Code point(s) |
| --- | --- | --- |
| Normal noteheads | noteheadWhole, noteheadHalf, noteheadBlack | E0A2, E0A3, E0A4 |
| X noteheads | noteheadXWhole, noteheadXHalf, noteheadXBlack | E0A7, E0A8, E0A9 |
| Circled X noteheads | noteheadCircleXWhole, noteheadCircleXHalf, noteheadCircleX | E0B1, E0B2, E0B3 |
| Clef | unpitchedPercussionClef1 | E069 |
| Rests | restWhole … rest64th | E4E3 … E4E9 |
| Stem-up flags | flag8thUp, flag16thUp, flag32ndUp, flag64thUp | E240, E242, E244, E246 |
| Triplet | tuplet3 | E883 |
| Time signature | timeSig0 … timeSig9 | E080 … E089 |

These are glyph names/code points, **not** font-size or staff-position data. The six additional open noteheads are needed for half/whole durations (including tuplets) and are part of the initial glyph-coverage gate, not a later enhancement. Keep staff lines, stems, beams, barlines and the hit line as rectangles in HPA-410; no extra SMuFL outline assets.

### 2. Pure glyph contract, duration rules and positioning

Add `DTXMania.Game/Lib/Song/Notation/NotationGlyphs.cs` with **no graphics/resource references**:

- A small internal glyph enum covering the listed characters; exhaustive glyph → PUA `char` mapping. Reuse HPA-408's `NoteheadShape`; do **not** add a second DTX channel table.
- A closed `NoteValue` enum (Whole, Half, Quarter, Eighth, Sixteenth, ThirtySecond, SixtyFourth) and pure `FromTicks(int)` following DTXWeb's exact/descending floor rule for 192, 96, 48, 24, 12, 6, 3 ticks. Values 1–2 map to SixtyFourth, as does a 5-tick folded rest; invalid/non-positive ticks should be rejected explicitly. Avoid changing `NotationQuantizer` or normalizing its original `DurationTicks`.
- `Notehead(NoteheadShape, NoteValue)`, `Rest(NoteValue)` and `Flag(NoteValue)`, where whole/half use the open noteheads appropriate to shape, quarter and shorter use the black variants, and flags exist only for eighth and shorter. For tuplets, the renderer will derive the **displayed base ticks** as `SlotTicks * 3 / 2` before calling `FromTicks`; this rule is documented and unit-tested here rather than reimplemented in the GPU draw loop.
- `IsComplete(Func<char, bool> hasGlyph)`, iterating the exact unique required glyph set, so the future resource-layer caller can pass `font.SupportsCharacter` without coupling notation to `IFont`.

**Metadata-first anchors:** Bravura's official `redist/Bravura.json` (from the same pinned font version) defines `glyphBBoxes` and `glyphsWithAnchors` in **staff spaces**, including `stemUpSE` on noteheads and `stemUpNW` on flag glyphs. Copy only the glyph-specific bbox widths/origins and attachment offsets the planned renderer will actually need into a small documented constant table, with exact metadata version/source noted. Do not load JSON at runtime, manually eyeball one pixel offset per glyph, or assume all head widths/optical centers match. Derive notehead musical-center and stem attachment from SMuFL origin/bbox and supplied anchor data, then prove visual alignment in the spike. Staff spacing, line/beam/stem thickness and drawing primitive choices belong in HPA-410's `PerformanceUILayout.NotationStrip` renderer/layout design, **not** in the glyph asset contract here.

The spike must measure only the compiled font's **pixels per staff space** and **SMuFL origin/baseline offset** relative to `IFont.DrawString` at this configured point size, then check alignment visually against the metadata anchors. Avoid new metric parsers, runtime JSON or a generalized font-layout framework.

### 3. Load via current resource manager, but fail closed

Add one exact `Bravura-Notation` family case to `ManagedFont.GetBestSpriteFontAssetName` so a caller can use `IResourceManager.LoadFont("Bravura-Notation", 40)`. Do **not** change other font-family fallback semantics.

Fix the root cause in `ManagedFont.BuildCharacterCache`: after the existing range scan, union `_spriteFont.Characters` into `_supportedCharacters` when the font exists. This retains the existing range checks and makes `IFont.SupportsCharacter` accurate for PUA code points actually present in the SpriteFont. Keep the change generic and additive; do not introduce Bravura-specific sanitization rules or change the normal font-family fallback.

Keep the glyph-coverage helper **pure** as `NotationGlyphs.IsComplete(Func<char, bool> hasGlyph)`; it does not reference `IFont`. The resource-layer consumer passes `font.SupportsCharacter` after the cache fix and rejects a silently substituted NotoSerifJP or incomplete Bravura font. Do not independently re-read `SpriteFont.Characters` in the glyph helper. On rejection, release the acquired font reference via the existing reference-counting API. If a combined load/validate/release helper is needed, put those few lines in `Lib/Resources`, not `Song/Notation`.

HPA-410 consumes this validated font; HPA-411 logs missing/invalid notation assets and runs lanes mode instead of tab. HPA-409 provides the validation contract and proves it works, but must not edit `PerformanceStage` to install a new mode prematurely.

### 4. Spike and conditional fallback

**Before committing to this rendering path**, build the spritefont and render a specimen containing **all required glyphs, including whole/half noteheads**, at the chosen point size in an existing graphics test/dev context on an available target platform. Draw it using **`IFont.DrawString` through the actual resource-manager-loaded font**, not a raw `SpriteBatch.DrawString` call on `IFont.SpriteFont`, so the smoke covers the same public wrapper and `SanitizeText` fallback that HPA-410 will use. Measure pixels-per-staff-space and the SMuFL baseline offset, then verify exact PUA preservation, clipping, alpha and metadata-derived anchor alignment, including flags, rests and all three notehead families.

If MonoGame's OTF/PUA import or baseline behavior makes this unusable, stop and **replace** the SpriteFont strategy with one pre-rendered PNG atlas plus an explicit glyph rectangle/anchor table, sourced from the same licensed Bravura. Use the existing texture-loading seam. Do not ship two glyph backends or a runtime renderer selector. Record evidence and the chosen deviation in this spec/PR; do not enlarge this ticket into HPA-410's renderer.

## Scope exclusions

No scrolling, measure placement, beaming, staff/beam/stem drawing thickness, real-time judgement tint, tab-mode config, HUD, note processing, changes to DTXWeb/NX, dynamic text rendering, or additional music fonts. Font licensing and source asset inclusion are part of this task, not a separate art-generation ticket.

## Acceptance / evidence

1. One source of truth maps every planned glyph, **including six whole/half heads**, to a verified code point; `NoteheadShape × NoteValue` chooses the correct notehead and rests/flags obey the DTXWeb tick-value rules (including 5-tick rests and tuplet display duration).
2. MGCB produces the asset on both Windows and macOS. A specimen through `IFont.DrawString` proves every glyph is present and visually aligned with **metadata-derived staff-space** metrics (not a default character), recording effective pixels/staff-space and baseline offset.
3. Bravura resolves through the existing font pipeline; `SupportsCharacter` recognizes actual PUA glyphs after cache construction, and a deliberately missing font or missing required glyph is **rejected** rather than accepted as NotoSerifJP.
4. Both desktop publish outputs include the OFL notice, with assertions extended in existing `FontLicenseFilesTests`.
5. Focused tests check code points, duration-to-value/glyph selection, exhaustive coverage using pure `IsComplete` via `SupportsCharacter`, font-family selection, and metadata-backed anchor contracts. The render specimen must pass through `IFont.DrawString`; Mac and Windows game builds / relevant tests pass.
6. No player-visible behavior change before HPA-410/HPA-411.

## Sources

- CX font pipeline: `DTXMania.Game/Lib/Resources/{ResourceManager.cs,ManagedFont.cs,IFont.cs}`
- CX content and packaging: `DTXMania.Game/Content/Content.mgcb`, `DTXMania.Game/DTXMania.Game.{Mac,Windows}.csproj`
- SMuFL official specification: https://smufl.formats.music/latest/
- Original Bravura and license: https://github.com/steinbergmedia/bravura
- Roadmap: HPA-394 → (HPA-408 + HPA-409) → HPA-410 → HPA-411 → HPA-509
