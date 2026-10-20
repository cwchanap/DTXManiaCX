# HPA-409 — Bravura Notation Glyph Font (Implementation Plan)

**Companion spec:** [HPA-409 design](../specs/2026-10-19-hpa-409-bravura-notation-font-design.md)  
**Issue:** https://linear.app/cwchanap/issue/HPA-409/tab-mode-bravura-notation-glyph-font  
**Delivery:** One implementation PR (reuse this draft docs PR after approval); <=3 engineer days. Documentation-only until implementation begins.

## What the implementing agent should know

The merged HPA-408 notation core already defines the three notehead shapes. CX does **not** currently have a generic SpriteFont loader: `ManagedFont.GetBestSpriteFontAssetName` chooses among fixed families and silently falls back to NotoSerifJP on asset load failures. The existing Windows/Mac game projects independently copy OFL licenses to `Licenses/`. The font cannot be considered working merely because `LoadFont` returned an object. Also, `ManagedFont.BuildCharacterCache` currently omits SMuFL's Private Use Area from its range scan; `SupportsCharacter` may report false for a glyph in the actual SpriteFont. `DrawString` attempts the original glyph first and sanitizes only on `ArgumentException`, so validate the **full public `IFont` behavior** rather than assuming every draw sanitizes.

Keep changes confined to the font assets/content manifest, font-family case, notation glyph contract, packaging entries and focused tests. Do not create a separate branch/PR for a preliminary spike or add a new renderer abstraction.

## Task 1 — Proven glyph source and one-screen spike

- Pin a stable official Steinberg Bravura release. Download its unmodified regular OTF, OFL license and glyph metadata for verification. Record the release/tag and the origin of the binaries in the spec or source comments.
- Preserve the spec's SMuFL table (cross-checked against W3C glyph metadata during design review). Recheck against the exact pinned Bravura/SMuFL source if needed, recording discrepancies rather than changing names/code points from memory.
- Add one `Bravura-Notation.spritefont` (nominal 40 px), restricting regions to the intended glyphs. Add the explicit `Content.mgcb` build entry. Use no automatic default-replacement glyph.
- **Spike gate:** build the content pipeline on an available desktop platform and visually render a specimen with one symbol per kind **through the loaded `IFont.DrawString` method that HPA-410 will call**. Do not bypass `ManagedFont` by drawing directly with `SpriteBatch.DrawString(font.SpriteFont, ...)`; that would miss wrapper and fallback behavior. Check PUA glyph retention, no tofu, baseline/cropping, notehead center and font-scale adequacy. Remove temporary spike-only harness after collecting a screenshot/short verification note. No new permanent debug screen.
- If unusable, choose a **single** pre-rasterized atlas alternative instead of trying to keep both systems. Explain the evidence and adjust this plan/spec. Generated raster atlas is technical conversion of existing font glyphs, not original art generation.

**Exit:** one demonstrably working glyph source, with verified metadata and anchor measurements. Do not proceed assuming MonoGame preserved glyphs merely because the asset compiled.

## Task 2 — Integrate with existing resource lifetime

- Add an exact Bravura-Notation family/size case in `ManagedFont.GetBestSpriteFontAssetName`, leaving existing font selection intact. Use the established `IResourceManager.LoadFont("Bravura-Notation", 40)` call contract.
- Fix the existing `ManagedFont.BuildCharacterCache`: **union `_spriteFont.Characters` into `_supportedCharacters`** after its range scan (only when `_spriteFont` is non-null). This is an additive correction to the existing `SupportsCharacter` contract, not Bravura-specific hard-coding. The direct `DrawString` path currently tries text first and calls `SanitizeText` only after `ArgumentException`; do not describe it as unconditional substitution.
- Add `NotationGlyphs.cs` with small internal enum, code-point mapping, `NoteheadShape` adapter, measured anchor offsets, and required glyph-character set.
- Provide a narrow validation helper that checks all required glyphs via **existing `IFont.SupportsCharacter(char)`**, after the cache fix. Do not duplicate its glyph lookup by reading `SpriteFont.Characters` again. If `LoadFont` silently returned NotoSerifJP or a partial/incorrect font, reject it and release the acquired font reference. Do not globally disable normal fallback for other UI fonts.
- Keep the public surface as small as possible (internal notation types). No `PerformanceStage` modifications or new config field in this task; document the small load/validate usage for HPA-410/HPA-411.

**Exit:** normal resource loading yields Bravura; missing/partial font is detected rather than rendered incorrectly; no change to existing font consumers.

## Task 3 — Packaging and regression tests

- Ship the original license notice as `Content/Bravura-OFL.txt`. Add `Licenses/Bravura-OFL.txt` copy entries to both `DTXMania.Game.Windows.csproj` and `DTXMania.Game.Mac.csproj` using the existing Orbitron/ShareTechMono convention. **Extend existing `DTXMania.Test/Resources/FontLicenseFilesTests.cs`** with `[InlineData("Bravura-OFL.txt")]` and assertions for the Bravura filename and `Licenses\\Bravura-OFL.txt` copy target in both projects. No new packaging-test class. Ensure published installer/portable/app outputs carry it.
- Add pure tests (e.g. `DTXMania.Test/Song/Notation/NotationGlyphsTests.cs` and an existing `Resources/ManagedFontAssetMappingTests` case) to assert:
  - all required glyphs and code points; all three notehead shapes;
  - a deterministic, documented anchor for each glyph used to attach to the staff;
  - exact Bravura family and asset selection, without changing existing family tests;
  - `SupportsCharacter`-based validation accepts a complete character set and rejects a set missing one critical glyph;
  - character-cache regression coverage for a PUA glyph present in `SpriteFont.Characters` but not in the old range scan, exercised through the existing `IFont.SupportsCharacter` seam where feasible. Avoid spinning up a real GPU just for the pure mapping/validation checks.
- Keep one manual/integration smoke **through `IFont.DrawString` on the real compiled SpriteFont** and measured offsets: the pure tests cannot prove MonoGame's PUA importer, glyph drawing or wrapper fallback worked.
- Run focused and full Mac test suites, build Mac game, and check Windows test/build CI. Existing current project target frameworks are net10.0 / net10.0-windows; do not retarget frameworks in this ticket.

**Exit:** tests plus observed render prove source, mapping and licensing are correct.

## Implementation PR checklist

- [ ] Bravura source release pinned; OFL notice stored and copied on Windows and Mac
- [ ] Only the required PUA glyphs included; no full-PUA texture atlas
- [ ] MonoGame glyph specimen rendered through `IFont.DrawString`; alignment/anchors measured
- [ ] Font-family mapping added; `ManagedFont` cache includes actual SpriteFont glyphs; `SupportsCharacter` validates presence after load (silent fallback covered)
- [ ] HPA-408 `NoteheadShape` reused; no duplicate DTX channel mapping
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
- No scrolling, staff geometry, beaming, culling, GPU drawing infrastructure or gameplay rendering (HPA-410).
- No further chart parser, note model, quantizer or judgement changes (HPA-408 already landed).
- No separate proof-of-concept PR, custom font service, two parallel font backends, code-generator framework, layout cache or new dependencies unless the spike demonstrates they are indispensable.
