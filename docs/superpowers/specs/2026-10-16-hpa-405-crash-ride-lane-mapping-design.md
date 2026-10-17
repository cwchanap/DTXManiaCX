# HPA-405 Crash/Ride Lane Mapping Design

## Summary

HPA-405 fixes a narrow semantic mismatch in CX's 10-lane drum mapping. `DTXChartParser` currently places DTX channel `0x16` in RD and `0x19` in CY, while the legacy DTX contract defines `0x16` as Cymbal/crash and `0x19` as RideCymbal.

The correction is still a two-entry parser swap, but review found two obsolete secondary mapping paths that would otherwise continue to contradict the parser. This PR should delete those dead paths instead of repairing them. It also removes one vacuous sample-file mapping test and updates two direct-`Note` fixtures that still encode the reversed crash/ride pairing.

This remains one bounded bug-fix/cleanup PR and intentionally does not begin Tab Mode implementation or repair the separate drum-binding UI mismatch.

## Canonical lane contract

CX keeps the existing gameplay lane order:

| Lane | Label | DTX channel(s) |
| ---: | --- | --- |
| 0 | LC | `0x1A` |
| 1 | HH | `0x11` / `0x18` |
| 2 | LP | `0x1B` / `0x1C` |
| 3 | SN | `0x12` |
| 4 | HT | `0x14` |
| 5 | DB | `0x13` |
| 6 | LT | `0x15` |
| 7 | FT | `0x17` |
| 8 | CY | `0x16` |
| 9 | RD | `0x19` |

The authoritative correction is:

- `0x16 -> lane 8 (CY / crash)`
- `0x19 -> lane 9 (RD / ride)`

`Note.Channel` remains the authored DTX channel. Only `LaneIndex` changes for these two channels.

## Evidence

The legacy `DTXManiaNX` `EChannel` enum defines `Cymbal = 22` (`0x16`) and `RideCymbal = 25` (`0x19`). CX's own gameplay geometry agrees: `PerformanceUILayout` gives lane 8/CY the wide 70 px RC lane and lane 9/RD the narrow 38 px ride lane.

CX's channel-based chip lookup also already has the correct numeric art mapping:

- `0x16 -> sprite column 6`, the 74 px crash chip;
- `0x19 -> sprite column 1`, the 58 px ride chip.

Today the parser sends those channels to the opposite lanes, so the crash chip can be drawn in the narrow ride lane and the ride chip in the crash lane. The user-visible fix is that crash/ride chips fit the intended lane and the existing per-pad AutoPlay `Cymbal`/`Ride` lane toggles control the intended notes.

## Production changes

### Parser

In `DTXMania.Game/Lib/Song/DTXChartParser.cs`:

- change `0x16` from lane 9 to lane 8;
- change `0x19` from lane 8 to lane 9;
- correct the comments to crash/CY and ride/RD;
- leave every other channel entry unchanged.

### Channel-based renderer path

`NoteRenderer.GetSpriteColumnForChannel` keeps its numeric behavior because it is already correct. Only stale `RightCymbal` / `RideCymbal` naming around `0x19` / `0x16` is corrected.

### Remove dead lane-keyed overlay mapping

`NoteRenderer.DrawOverlayEffect` has no production caller, and its only mapping dependency is `GetSpriteColumnForLane`. Keeping that lane-to-art table creates a second mapping that must be synchronized with the parser for no runtime benefit.

Delete:

- `NoteRenderer.DrawOverlayEffect`;
- `NoteRenderer.GetSpriteColumnForLane`;
- their dedicated tests in `NoteRendererLogicTests`.

Do not replace them with another lane-to-channel table.

### Remove dead `InputLane` mapping table

`InputLane` / `InputLaneExtensions` is not used by the gameplay input path. `FromChannel` and `ToLaneIndex` are test-only; `FromLaneIndex` is used only by `JudgementEvent.ToString()` to produce a debug label. The table contradicts the parser (including `0x16 -> lane 7`).

Delete the enum/extensions and their tests. Change `JudgementEvent.ToString()` to print the already-owned integer as `Lane {Lane}` instead of consulting another semantic map.

This deletion is intentionally in HPA-405 because leaving the table would make the test suite assert two incompatible channel-to-lane contracts immediately after this fix.

### Mapping documentation and stale fixtures

Correct `Note.LaneIndex` documentation so the tail reads `0x17=7 (FT), 0x16=8 (CY), 0x19=9 (RD)`.

Update direct-note fixtures that still pair lane 8 with `0x19`:

- `JudgementManagerTests`: lane 8 uses `0x16`;
- `ChartManagerTests`: lane 8 uses `0x16`.

These fixtures do not parse channels, so they would keep passing while documenting the old semantic pairing unless corrected explicitly.

## Test strategy

### Parser regression

Strengthen `ParseAsync_WithDrumNotes_ShouldParseAllMappedChannels` into a self-contained expected channel-to-lane table covering every mapped drum channel. The red assertions on current `main` are:

- `0x16` has `LaneIndex == 8`;
- `0x19` has `LaneIndex == 9`.

This becomes the executable source of truth for `DTXChartParser.ChannelToLaneMap`.

Keep `Note_GetLaneName_ReturnsCorrectNames`; swap only its CY/RD channel examples so lane 8 uses `0x16` and lane 9 uses `0x19`.

Delete `ParseAsync_ValidDtxFile_SetsCorrectChannelMapping`: `sample.dtx` is not copied by either active test project, so the test returns before asserting anything, and its mapping table is independently stale. The new temp-file table test replaces it.

The other `_sampleDtxPath` tests are a separate test-harness cleanup and remain out of scope.

### Dead-code deletion regression

After deleting `InputLane` / `InputLaneExtensions`, keep the existing `JudgementEvent.ToString()` behavior tests and pin that the output includes `Lane <n>` for a normal lane as well as an unknown lane.

After deleting the unused renderer overlay API, remove only tests whose sole purpose is `DrawOverlayEffect` or `GetSpriteColumnForLane`; keep channel-based sprite mapping coverage.

## Binding-lane mismatch discovered during review

The drum binding/config UI uses a different semantic lane table from gameplay. `InputRouter` forwards a binding's integer lane directly to `JudgementManager`, so there is no real second coordinate system that translates those names at runtime.

That bug predates HPA-405 and is not made worse by this parser swap. Fixing it here would mix user-input/config behavior into a chart parser correction, so it remains separate. Reclaimed ticket **HPA-443 — [Input] Align drum binding lanes with gameplay lane identities** owns that repair and blocks HPA-411 Tab Mode gameplay integration.

HPA-443 covers `KeyBindings` names/defaults, `DrumKitLayout` zone lane IDs, and the false "KeyBindings numbering" assumptions in `SongSelectionStage`.

## Out of scope

- HPA-443 drum-binding/config lane repair;
- changing `GetSpriteColumnForChannel` numeric behavior;
- lane model/enum redesign beyond deleting the unused contradictory `InputLane` table;
- DTXWeb changes;
- DTXManiaNX changes;
- Tab Mode notation, glyphs, rendering, config, or HUD work;
- compatibility shims for the previously incorrect parser mapping;
- broad cleanup of the remaining vacuous `_sampleDtxPath` tests.

There are no released-user compatibility requirements for preserving the parser bug; saved scores/config do not own authored channel-to-lane parsing.

## Acceptance

HPA-405 is complete when:

- parsing `0x16` yields lane 8 / CY;
- parsing `0x19` yields lane 9 / RD;
- all other parser channel mappings remain unchanged;
- channel-based chip mapping remains `0x16 -> column 6` and `0x19 -> column 1`;
- unused `DrawOverlayEffect` / `GetSpriteColumnForLane` and the contradictory `InputLane` mapping table are removed rather than repaired;
- the vacuous sample mapping test is removed and the self-contained parser table covers the mapping;
- direct lane-8 fixtures use `0x16`, not `0x19`;
- stale crash/ride comments in the touched parser/note/renderer surface are corrected;
- focused tests and the full Mac-safe game test suite pass;
- HPA-443 remains separate and blocks HPA-411.