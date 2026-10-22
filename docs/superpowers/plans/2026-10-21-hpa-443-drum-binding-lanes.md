# HPA-443 — Align Drum Binding Lane Identities (Implementation Plan)

> Agent instruction: implement **in this existing PR branch**, not another PR. Read [the paired design](../specs/2026-10-21-hpa-443-drum-binding-lanes-design.md) first. One ticket, one PR, ~1–2 engineer days.

**Stack:** .NET 10, MonoGame, xUnit.  
**Linear:** https://linear.app/cwchanap/issue/HPA-443/input-align-drum-binding-lanes-with-gameplay-lane-identities

## Invariants / exclusions

Gameplay lanes are `0 LC, 1 HH, 2 LP, 3 SN, 4 HT, 5 DB, 6 LT, 7 FT, 8 CY, 9 RD`. `DTXChartParser.ChannelToLaneMap` is already correct and `InputRouter` forwards binding IDs unchanged. **Do not** change parser, judgement/scoring, AutoPlay execution, `InputRouter`, `Note.cs`, `PerformanceUILayout.LaneNames`, configuration persistence, device APIs, or Tab Mode (HPA-411/HPA-509). No translation, saved-binding migration, new art/SFX, or new input abstraction.

`KeyBindings.GetLaneName` becomes the single **friendly** UI label source reused by ConfigStage and DrumConfig. Keep short notation `Note.GetLaneName` separate: those are different presentation needs, not duplicate friendly-name tables.

## Task 1 — Friendly names, default keyboard bindings, and dependent tests

**Production:**
- `DTXMania.Game/Lib/Input/KeyBindings.cs`
- `DTXMania.Game/Lib/Stage/ConfigStage.cs`

**Tests to check/update:**
- `DTXMania.Test/Input/KeyBindingsTests.cs`
- `DTXMania.Test/Input/ModularInputManagerTests.cs`
- `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`
- `DTXMania.Test/Config/SystemKeyBindingsPersistenceTests.cs`
- `DTXMania.Test/Config/ConfigStageLogicTests.cs` (existing friendly-name assertions should pass without alteration)

- [ ] Move the canonical long-label array from `ConfigStage.cs` (around 759–763) into `KeyBindings`, replacing its wrong lane-name switch; keep invalid `Lane {n}` fallback. In ConfigStage, build AutoPlay labels from `Enumerable.Range(0, 10).Select(KeyBindings.GetLaneName).ToArray()`. No `Input → Song` dependency.
- [ ] Change only the default values in `KeyBindings.LoadDefaultBindings`: **A=0, J=1, G=2, S=3, K=4, Space=5, L=6, F=7, D=8, ;=9**. Remove stale DTX channel descriptions and the duplicated `0x1A` comment. Preserve IDs and events.
- [ ] In `KeyBindingsTests`, pin all ten default key→lane pairs in one parameterized test, and verify the friendly names on all ten canonical lanes.
- [ ] In `ModularInputManagerTests`, update the existing constructor/default mapping assertions near lines 116–139 and default mapping theory near 323–331. Remove the redundant `GetLaneName_ValidLanes_ReturnsCorrectNames` theory near 421–429; the canonical friendly-name test belongs in `KeyBindingsTests`. Check other constructor-default assumptions such as the `Key.Space` expectation.
- [ ] In `DrumConfigStageTests`, update default Snare/S lane assumptions (near 200–222 and 700–715), including popup clear/chip-remove and reset tests; preserve intentionally custom lane bindings.
- [ ] In `SystemKeyBindingsPersistenceTests`, update the reset assertion near 354 from S/4 to S/3. Leave the system-key conflict behavior intact.
- [ ] Confirm ConfigStage's existing AutoPlay menu tests still see `Left Cymbal, Hi-Hat, ..., Ride` (friendly labels) and retain their old behavior.

## Task 2 — Explicit parser-to-default-key contract regression

**Modify:** `DTXMania.Test/Song/DTXChartParserAdditionalTests.cs`.

- [ ] Reuse the authored all-channels DTX fixture from `ParseAsync_WithDrumNotes_ShouldParseAllMappedChannels` (around line 135), extracting a small shared local fixture constant/helper if needed, to avoid writing another large test chart.
- [ ] Add a focused test comparing each parsed note's actual `LaneIndex` to `new KeyBindings().GetLane(defaultKey)` for the instrument. Include all mapped channels, notably **0x12 ↔ Key.S**, **0x13 ↔ Key.Space**, **0x16 ↔ Key.D**, the paired Hi-Hat channels 0x11/0x18 ↔ Key.J, and LP channels 0x1B/0x1C ↔ Key.G. This would fail on the pre-fix bindings.
- [ ] Keep the existing parser-only channel table regression unchanged in meaning.
- [ ] **Do not** add an extra `InputRouter` event test: `DTXMania.Test/Input/InputRouterTests.cs` already directly verifies `LaneHitEventArgs.Lane` forwards a deliberately assigned lane without translation. Do not rewrite explicit `Key.S=4` assignments in unrelated unit tests.

## Task 3 — Correct DrumConfig kit, preserve hi-hat pedal

**Production:**
- `DTXMania.Game/Lib/Stage/DrumConfig/DrumKitLayout.cs`
- `DTXMania.Game/Lib/Stage/DrumConfig/DrumKitRenderer.cs`

**Tests:**
- `DTXMania.Test/Stage/DrumConfig/DrumKitLayoutTests.cs`
- Inspect `DTXMania.Test/Stage/DrumConfig/DrumKitRendererTests.cs` only if a changed signature requires updates; keep its `HiHatPedal` shape/texture assertions.

- [ ] Reassign the authored visual zones to the **actual instrument**: HH stand=1, left crash=0, ride=9, high tom=4, low tom=6, snare=3, floor tom=7, right kick pedal=5, **hi-hat foot pedal=2**. First focused zone remains HH and should now be lane 1.
- [ ] Replace the **mirrored left bass pedal** (old last zone, x≈560/y≈600) with a CY/8 cymbal zone in an open upper-right spot (roughly x=920–980/y=170–200). Reuse `DrumZoneShape.Cymbal` and the existing cymbal sprite. Verify overlap/hit-testing and adjust the proposed coordinate only if necessary.
- [ ] Keep `DrumZoneShape.HiHatPedal`, `_hihat` load/release, and its renderer test. Delete the now-unused `DrumZone.FlipHorizontal` property and constructor parameter, renderer flip conditional (draw unflipped), and flip-specific unit test. Retain `DrumZone.Name` as existing data, without additional unused-name tests.
- [ ] Preserve ten zones, their focus ordering/Reset index, and `Zones_CoverAllTenLanesExactlyOnce`. Add one parameterized/looping test verifying **`HitTest(zone.CenterX, zone.CenterY) == zone.Lane` for all ten zones**; refresh existing assertions for names/shape and corrected default snare/HH zone IDs. Do not create a GPU test harness.
- [ ] **Required visual verification before merge:** reset saved drum bindings, launch DrumConfig, take **one screenshot** using the existing DTXMania MCP `game_screenshot` or a manual screenshot if the MCP bridge is unavailable. Attach/link it in the PR, confirming the CY art, labels and no overlap. If the environment cannot launch a graphical game, report that limitation and leave visual acceptance pending rather than claiming it passed.

## Task 4 — Song Selection and E2E, then final verification

**Production:**
- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`

**Tests:**
- `DTXMania.Test/Stage/SongSelectionStageTabTests.cs`
- `DTXMania.Test/Stage/SongSelectionStageBookmarkToggleTests.cs`
- `DTXMania.E2E/DrumMappingStageSmokeTests.cs`

- [ ] Replace old constants `LowTomLaneIndex = 8` and `FloorTomLaneIndex = 1` with **`(int)PerformanceUILayout.LaneType.LT`** and **`(int)PerformanceUILayout.LaneType.FT`**. The layout namespace is already imported; remove old 'KeyBindings numbering' commentary.
- [ ] Update tab tests to assert LT/6 switches and CY/8 does not, while preserving search-modal behavior.
- [ ] Update bookmark shortcut test to assert FT/7 toggles while old HH/1 does not. Leave direct `ToggleBookmarkForSelectedSong` behavior untouched.
- [ ] Update E2E `DefaultFocusedLane` to HH/1 instead of HH/5. Existing bind/Reset persistence assertions still apply.

### Test commands (macOS)

```sh
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~KeyBindingsTests|FullyQualifiedName~ModularInputManagerTests|FullyQualifiedName~DrumConfigStageTests|FullyQualifiedName~SystemKeyBindingsPersistenceTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~DTXChartParserAdditionalTests|FullyQualifiedName~DrumKitLayoutTests|FullyQualifiedName~DrumKitRendererTests|FullyQualifiedName~SongSelectionStageTabTests|FullyQualifiedName~SongSelectionStageBookmarkToggleTests"
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

On Windows run the repository's matching supported test/CI command. DrumMapping E2E needs a real game process/display; follow its existing launch workflow, and do not treat a skipped graphics-only run as a pass.

## Final review checklist

- [ ] One friendly-name table in `KeyBindings`, reused by ConfigStage and DrumConfig. `Note.cs` untouched.
- [ ] All ten default keyboard→lane identities pinned; parsed notes independently agree with each instrument's default key.
- [ ] A kit zone exists per lane; the LP hi-hat pedal remains, redundant mirrored pedal is replaced by CY, flip-only code removed, all ten center hit tests pass.
- [ ] Song Selection uses canonical LT/FT enum values and tests pin the behavior.
- [ ] Dependent tests named above are updated **only where defaults/zone semantics changed**. Existing explicit custom bindings are untouched.
- [ ] Unit + CI gates pass; one reviewed DrumConfig screenshot attached before merge.
- [ ] All production/tests/docs stay in this one HPA-443 PR. No HPA-411/509 implementation, migration, translation layer, new assets or unrelated refactor.
