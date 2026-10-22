# HPA-443 — Drum Binding Lane Identity Alignment (Implementation Plan)

> For the implementation agent: make code changes on **this PR branch**; do not open a second PR. Read the paired design spec before coding.

**Issue:** https://linear.app/cwchanap/issue/HPA-443/input-align-drum-binding-lanes-with-gameplay-lane-identities  
**Spec:** `docs/superpowers/specs/2026-10-21-hpa-443-drum-binding-lanes-design.md`  
**Target:** .NET 8 / MonoGame / xUnit; one bounded PR, 1–2 engineer days.

## Existing code to reuse
- `DTXMania.Game/Lib/Song/Components/Note.cs` — canonical lane-to-short-label switch; `DTXChartParser.cs` channel-to-lane mapping (already correct).
- `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs` — existing `LaneType` / `LaneNames` values.
- `DTXMania.Game/Lib/Input/KeyBindings.cs` and `InputRouter.cs` — bindings and lane forwarding (no translation).
- `DTXMania.Game/Lib/Stage/DrumConfig/DrumKitLayout.cs` — ten clickable zones, pure geometry.
- `DTXMania.Game/Lib/Stage/DrumConfig/DrumKitRenderer.cs` — existing cymbal/drum/pedal textures; no new assets.
- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs` — LowTom/ FloorTom shortcuts.

## Invariants
- Lane IDs remain `0 LC, 1 HH, 2 LP, 3 SN, 4 HT, 5 DB, 6 LT, 7 FT, 8 CY, 9 RD`.
- Parser mapping, judgement, timing, AutoPlay, scorer and visual lane renderer remain untouched.
- Preserve button IDs (`Key.*`, `MIDI.*`, `Pad.*`) and `InputRouter` contract; no extra translation layer or config migration.
- Keep focus navigation, popup flow and Reset behavior. One zone per lane, including a CY cymbal.
- HPA-411 and HPA-509 remain completely out of scope.

## Task 1 — Canonical names and default keyboard mappings
**Modify:** `DTXMania.Game/Lib/Song/Components/Note.cs`, `DTXMania.Game/Lib/Input/KeyBindings.cs`, `DTXMania.Test/Input/KeyBindingsTests.cs`.

- [ ] Add a static lane-name access path to the existing `Note` switch (or one very small shared non-UI helper) and make the existing instance method reuse it; let `KeyBindings.GetLaneName` reuse that path. Preserve its invalid-index `Lane N` fallback.
- [ ] Update `LoadDefaultBindings` to **A=LC/0, J=HH/1, G=LP/2, S=SN/3, K=HT/4, Space=DB/5, L=LT/6, F=FT/7, D=CY/8, ;=RD/9**. Repair all false comments, particularly the `0x1A` duplicate and old lane numbering.
- [ ] Update old `GetLaneName` and default/reset assertions that currently expect Snare=4 or Bass=6. Add a `[Theory]` over all ten key/lane/name pairs, not only a count-of-lanes test.
- [ ] Add one direct agreement assertion against the existing gameplay short names (`Note.GetLaneName` / `PerformanceUILayout.LaneNames`) for all 0..9.
- [ ] Keep existing `KeyBindings` events, binding data shape and MIDI parsing unchanged.

## Task 2 — Make visual drum-kit zones physically truthful
**Modify:** `DTXMania.Game/Lib/Stage/DrumConfig/DrumKitLayout.cs`, `DTXMania.Test/Stage/DrumConfig/DrumKitLayoutTests.cs`.

- [ ] Reassign old zone lanes by the **visual instruments**, not by translating the old integer mechanically: HH=1, LC=0, RD=9, HT=4, LT=6, SN=3, FT=7, right bass pedal=5, left mirrored bass pedal=2.
- [ ] Replace the extra `HiHatPedal` interactive zone with a CY/8 cymbal at a non-overlapping upper-right coordinate (see spec); reuse `DrumZoneShape.Cymbal` and current renderer texture. Avoid new art, renderer logic and complete layout redesign.
- [ ] Keep `Zones` in the same focus order where practical. Preserve zone count=10, Reset focus index=10, `AdvanceFocus` semantics and 1:1 mapping.
- [ ] Update the test using `GetLaneForZoneIndex(0)`: first zone HH becomes 1 (formerly 5). Update Snare center to lane 3, bass pedal to lane 5, mirrored left pedal to lane 2; assert all ten shape/name/lane identities and center hit tests.
- [ ] Add/retain checks that all zone lanes are distinct, cover 0..9, and have no accidental center overlap. Review screenshot only if manual game graphics are available. Do not create new graphics test harness.

## Task 3 — Make Song Selection shortcuts use canonical IDs
**Modify:** `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`, `DTXMania.Test/Stage/SongSelectionStageTabTests.cs`; locate and update any existing bookmark-focused tests that assert old values.

- [ ] Change `LowTomLaneIndex` from 8 to 6; change `FloorTomLaneIndex` from 1 to 7. If the project already exposes canonical enum values cleanly, use those; otherwise local constants with canonical comments are sufficient. Remove the false 'KeyBindings numbering' explanation.
- [ ] Update tab-switch and bookmark tests to use lanes 6 and 7; assert 8 does not switch tabs, and 1 does not toggle bookmarks. Keep search-modal suppression and tab order unchanged.
- [ ] Confirm the pipeline still delivers the raw lane integer via `InputRouter`; do **not** introduce a runtime conversion from `KeyBindings` to parser lanes.

## Task 4 — Regression and integration verification
**Modify, only as needed:** `DTXMania.E2E/DrumMappingStageSmokeTests.cs`, `DTXMania.Test/Input/*` and affected focused tests.

- [ ] Change existing E2E first-zone assumption from HH/5 to HH/1. It should still bind `Key.Z`, exit and verify the SQLite-persisted lane, plus confirm Reset removes it.
- [ ] Add a small `InputRouter` or comparable existing test proving an instrument's key emits its canonical lane without translation; a representative Snare/S=3, Bass/Space=5 and CY/D=8 is enough.
- [ ] Search existing tests/comments for hardcoded old assumptions (Snare=4, Bass=6, LowTom=8, FloorTom=1, HH=5). Update **only** assertions about user-configured input, zone identification or song shortcuts; leave deliberate parser/gameplay lane uses unchanged.
- [ ] Verify code does not silently rewrite old persisted user bindings. The existing UI Reset-to-defaults behavior suffices; no migration/back-compat work.

### Test commands
Use the relevant test project for the host OS (on macOS, the repository has `DTXMania.Test/DTXMania.Test.Mac.csproj`):

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~KeyBindingsTests|FullyQualifiedName~DrumKitLayoutTests|FullyQualifiedName~SongSelectionStageTabTests|FullyQualifiedName~InputRouter"
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

If a Windows CI environment is available, use its existing solution/test commands instead of inventing new build infrastructure. The existing `DrumMappingStageSmokeTests` are graphics/process E2E; use the repository's normal E2E launch path in a suitable runner. Do not require E2E to run headlessly on a machine without a graphics device.

## Review / done checklist
- [ ] One canonical lane identity throughout parser, names, keyboard, visual zones and song shortcuts.
- [ ] Defaults include **all ten** drum lanes, and full mapping is asserted by tests.
- [ ] Drum kit's named, drawn and clicked pieces match the binding lane; CY zone exists and LP is not duplicated.
- [ ] A physical MIDI/pad mapping still forwards its stored lane integer unchanged.
- [ ] Focus, capture, config persistence, conflict messages and Reset are not regressed.
- [ ] Song selection responds to LT/6 and FT/7, not the legacy IDs.
- [ ] CI/focused tests pass. Review any unrelated test hardcodes before changing them.
- [ ] PR touches only the listed files (plus directly relevant focused tests), with no Tab Mode, parser, score or input architecture refactor.

**Commit/PR approach:** one implementation branch/PR for HPA-443, including this spec/plan and production/test work. Use small commits as desired; do not fragment into separate PRs.
