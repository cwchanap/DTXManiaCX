# HPA-409 — Bravura Notation Glyph Font (Implementation Plan)

**Companion spec:** [HPA-409 design](../specs/2026-10-19-hpa-409-bravura-notation-font-design.md)  
**Issue:** https://linear.app/cwchanap/issue/HPA-409/tab-mode-bravura-notation-glyph-font  
**Delivery:** One implementation PR (reuse this draft docs PR after approval); <=3 engineer days. Documentation-only until implementation begins.

## What the implementing agent should know

The merged HPA-408 notation core already defines the three notehead shapes, entry durations and tuplet slot durations; its quantizer can emit whole/half and irregular short-duration entries. DTXWeb maps 192/96/48/24/12/6/3 ticks to whole/half/quarter/eighth/16th/32nd/64th, and computes a tuplet display base as `SlotTicks * 3 / 2`. CX does **not** currently have a generic SpriteFont loader: `ManagedFont.GetBestSpriteFontAssetName` chooses among fixed families and silently falls back to NotoSerifJP on asset load failures. The existing Windows/Mac game projects independently copy OFL licenses to `Licenses/`. The font cannot be considered working merely because `LoadFont` returned an object. Also, `ManagedFont.BuildCharacterCache` currently omits SMuFL's Private Use Area from its range scan; `SupportsCharacter` may report false for a glyph in the actual SpriteFont. `DrawString` attempts the original glyph first and sanitizes only on `ArgumentException`, so validate the **full public `IFont` behavior** rather than assuming every draw sanitizes.

Keep changes confined to the font assets/content manifest, font-family case/cache fix, **pure** notation glyph/duration contract, packaging entries and focused tests. Do not introduce `IFont` or other MonoGame dependencies into `Song/Notation`; pass `Func<char, bool>` when checking glyph availability. Do not create a separate branch/PR for a preliminary spike or add a new renderer abstraction.

## Task 1 — Proven glyph source and one-screen spike

- Pin a stable official Steinberg Bravura release. Download its unmodified regular OTF, OFL license and `redist/Bravura.json` metadata (same release). Record the release/tag, metadata font version and source URLs in the spec or source comments. `glyphBBoxes`, `glyphsWithAnchors` and relevant `engravingDefaults` are in staff spaces; use the first two for glyph-specific geometry.
- Preserve the spec's SMuFL table (cross-checked against W3C glyph metadata during design review). Recheck against the exact pinned Bravura/SMuFL source if needed, recording discrepancies rather than changing names/code points from memory.
- Add one `Bravura-Notation.spritefont` (**40 pt**, not 40 px), restricting regions to the intended glyphs, **including six additional whole/half heads**: `noteheadWhole` E0A2, `noteheadHalf` E0A3, `noteheadXWhole` E0A7, `noteheadXHalf` E0A8, `noteheadCircleXWhole` E0B1 and `noteheadCircleXHalf` E0B2. Add the explicit `Content.mgcb` build entry. Use no automatic default-replacement glyph.
- **Spike gate:** build the content pipeline on an available desktop platform and visually render **all required glyphs, including the six open heads**, through the loaded `IFont.DrawString` method that HPA-410 will call. Do not bypass `ManagedFont` by drawing directly with `SpriteBatch.DrawString(font.SpriteFont, ...)`. Use Bravura's pinned glyph bbox/stem anchor metadata for expected geometry instead of measuring one pixel offset per glyph by eye. Measure **effective pixels per staff space** and **SMuFL baseline/origin offset** relative to `IFont.DrawString` (SpriteFont `<Size>` is in points); verify no tofu/clipping and that heads, rests and flags align. Remove the temporary harness after collecting a screenshot/brief verification note; no permanent debug screen.
- If unusable, choose a **single** pre-rasterized atlas alternative instead of trying to keep both systems. Explain the evidence and adjust this plan/spec. Generated raster atlas is technical conversion of existing font glyphs, not original art generation.

**Exit:** one demonstrably working glyph source, with verified metadata and the two measured global font calibration values (pixels/staff-space and baseline offset). Do not proceed assuming MonoGame preserved glyphs merely because the asset compiled.

## Task 2 — Integrate with existing resource lifetime

- Add an exact Bravura-Notation family/size case in `ManagedFont.GetBestSpriteFontAssetName`, leaving existing font selection intact. Use the established `IResourceManager.LoadFont("Bravura-Notation", 40)` call contract.
- Fix the existing `ManagedFont.BuildCharacterCache`: **union `_spriteFont.Characters` into `_supportedCharacters`** after its range scan (only when `_spriteFont` is non-null). This is an additive correction to the existing `SupportsCharacter` contract, not Bravura-specific hard-coding. The direct `DrawString` path currently tries text first and calls `SanitizeText` only after `ArgumentException`; do not describe it as unconditional substitution.
- Add **GPU-free** `NotationGlyphs.cs` with an internal glyph enum, code-point mapping, existing `NoteheadShape` adapter, and a closed `NoteValue` enum with `FromTicks(int)`. Follow DTXWeb's descending duration match (192, 96, 48, 24, 12, 6, 3): choose the greatest supported tick value ≤ duration, or SixtyFourth below 3; reject non-positive input. Expose `Notehead(shape, value)`, `Rest(value)`, `Flag(value)` (null for Quarter and longer), and document that tuplet display ticks are `SlotTicks * 3 / 2` before conversion. Avoid changing HPA-408's quantizer.
- Copy only **glyph-specific** bbox width/origin and `stemUpSE`/`stemUpNW` attachment constants actually consumed by the upcoming renderer from pinned `Bravura.json`, expressed in staff spaces. Use metadata version/source comments and a small table, not a runtime JSON loader or per-glyph eyeballed pixel offsets. Keep staff-line/stem/beam thickness settings in HPA-410; they are renderer geometry rather than font glyph contracts.
- Provide `NotationGlyphs.IsComplete(Func<char, bool> hasGlyph)` over the exact required glyph set, keeping `Song/Notation` independent of `IFont`/MonoGame. The resource consumer calls `NotationGlyphs.IsComplete(font.SupportsCharacter)` after the cache fix; if `LoadFont` silently returned NotoSerifJP or a partial/incorrect font, reject it and release the acquired font reference. Any combined load/validate/release helper belongs in `Lib/Resources` **only if necessary**; do not add a font abstraction or duplicate glyph lookup by reading `SpriteFont.Characters` again.
- Keep the public surface as small as possible (internal notation types). No `PerformanceStage` modifications or new config field in this task; document the small load/validate usage for HPA-410/HPA-411.

**Exit:** normal resource loading yields Bravura; missing/partial font is detected rather than rendered incorrectly; no change to existing font consumers.

## Task 3 — Packaging and regression tests

- Ship the original license notice as `Content/Bravura-OFL.txt`. Add `Licenses/Bravura-OFL.txt` copy entries to both `DTXMania.Game.Windows.csproj` and `DTXMania.Game.Mac.csproj` using the existing Orbitron/ShareTechMono convention. **Extend existing `DTXMania.Test/Resources/FontLicenseFilesTests.cs`** with `[InlineData("Bravura-OFL.txt")]` and assertions for the Bravura filename and `Licenses\\Bravura-OFL.txt` copy target in both projects. No new packaging-test class. Ensure published installer/portable/app outputs carry it.
- Add pure tests (e.g. `DTXMania.Test/Song/Notation/NotationGlyphsTests.cs` and an existing `Resources/ManagedFontAssetMappingTests` case) to assert:
  - every required glyph/code point (including six whole/half heads); all three `NoteheadShape` values combined with Whole/Half/Quarter-or-shorter values;
  - duration mapping for 192/96/48/24/12/6/3 ticks, non-binary folded rests (5→SixtyFourth, 7→ThirtySecond) and 1–2 tick fallback, plus tuplet displayed-base conversion (`SlotTicks * 3 / 2`); no GPU dependency;
  - glyph-specific bbox/origin and attachment values copied from pinned Bravura metadata in staff spaces; verify correct constants rather than self-comparison when feasible;
  - exact Bravura family and asset selection, without changing existing family tests;
  - `NotationGlyphs.IsComplete(Func<char, bool>)` accepts the full required set and rejects any missing glyph, using a stub predicate in pure unit tests;
  - character-cache regression coverage for a PUA glyph present in `SpriteFont.Characters` but not in the old range scan, exercised through the existing `IFont.SupportsCharacter` seam where feasible. Avoid spinning up a real GPU just for the pure mapping/validation checks.
- Keep one manual/integration smoke **through `IFont.DrawString` on the real compiled SpriteFont**, measuring only pixels-per-staff-space and baseline offset and visually verifying the metadata-derived anchors; pure tests cannot prove MonoGame's PUA importer, glyph drawing or wrapper fallback worked.
- Run focused and full Mac test suites, build Mac game, and check Windows test/build CI. Existing current project target frameworks are net10.0 / net10.0-windows; do not retarget frameworks in this ticket.

**Exit:** tests plus observed render prove source, mapping and licensing are correct.

## Implementation PR checklist

- [ ] Bravura source release pinned; OFL notice stored and copied on Windows and Mac
- [ ] Required PUA glyphs include all nine normal/X/circled-X whole/half/black heads; no full-PUA texture atlas
- [ ] MonoGame glyph specimen rendered through `IFont.DrawString`; baseline and staff-space pixel scale measured, glyph anchors verified against pinned metadata
- [ ] Font-family mapping added; `ManagedFont` cache includes actual SpriteFont glyphs; `SupportsCharacter` validates presence after load (silent fallback covered)
- [ ] HPA-408 `NoteheadShape` reused; `NoteValue.FromTicks` and glyph selection tested for binary/tuplet/non-binary durations; no duplicate DTX channel mapping
- [ ] Existing `FontLicenseFilesTests` extended; focused tests and Mac/Windows builds/CI are green
- [ ] No renderer, gameplay, config, HUD, or timing logic included
- [ ] HPA-409 marked done only after implementation PR merges, not at docs approval

## Verification commands

Run from the repository root, using the installed SDK and project's documented restore/build setup:

```bash
dotnet tool restore
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~Notation|FullyQualifiedName~ManagedFontAssetMapping"
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --no-incremental
```

Also build the Windows game and execute `DTXMania.Test.csproj` on Windows CI. Inspect a built/published output for the Bravura content XNB and `Licenses/Bravura-OFL.txt`. Run a one-off graphics smoke to confirm actual glyphs; the tests above should discover at least one test rather than silently executing zero.

## Do not do

- No tab UI/config switch, lane fallback hookup or runtime telemetry (HPA-411).
- No scrolling, staff/beam/stem **drawing geometry or thickness**, beaming, culling, GPU drawing infrastructure or gameplay rendering (HPA-410). Glyph-specific SMuFL bboxes/anchors and pure duration→glyph conversion remain in HPA-409.
- No further chart parser, note model, quantizer or judgement changes (HPA-408 already landed).
- No separate proof-of-concept PR, custom font service, two parallel font backends, code-generator framework, layout cache or new dependencies unless the spike demonstrates they are indispensable.
