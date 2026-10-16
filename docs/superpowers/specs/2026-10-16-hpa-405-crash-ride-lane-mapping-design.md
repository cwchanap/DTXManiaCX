# HPA-405 Crash/Ride Lane Mapping Design

## Summary

HPA-405 fixes a narrow semantic mismatch in CX's 10-lane drum mapping. `DTXChartParser` currently places DTX channel `0x16` in RD and `0x19` in CY, but the DTX contract used by DTXWeb is the opposite: `0x16` is crash/CY and `0x19` is ride/RD.

The renderer already contains the correct channel-specific art geometry: channel `0x16` selects the 74 px crash chip and `0x19` selects the 58 px ride chip. The bug is therefore limited to the parser's channel-to-lane assignment, the lane-keyed overlay fallback, and stale comments/tests that encode the swapped meaning.

This remains one small bug-fix PR and intentionally does not begin Tab Mode implementation.

## Canonical lane contract

CX keeps the existing gameplay lane order:

| Lane | Label | Primary DTX channel for this change |
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

The authoritative correction is therefore:

- `0x16 -> lane 8 (CY / crash)`
- `0x19 -> lane 9 (RD / ride)`

`Note.Channel` remains the authored DTX channel. Only `LaneIndex` changes for these two channels.

## Evidence from current code

Current `main` has the two parser entries reversed in `DTXChartParser.ChannelToLaneMap`.

DTXWeb's notation mapping independently defines:

- `16` as `CY` / crash;
- `19` as `RD` / ride.

CX's own note sprite geometry already agrees with that meaning:

- sprite column 6 is channel `0x16`, width 74, labelled CY/right crash;
- sprite column 1 is channel `0x19`, width 58, labelled RD/ride.

The channel-based note draw path therefore needs no numerical mapping change. Only its stale `RideCymbal` / `RightCymbal` comments should be corrected.

## Production changes

### Parser

In `DTXMania.Game/Lib/Song/DTXChartParser.cs`:

- change `0x16` from lane 9 to lane 8;
- change `0x19` from lane 8 to lane 9;
- correct the comments to `Cymbal/Right Crash -> CY` and `Ride -> RD`.

No other channel entry changes.

### Lane-keyed renderer fallback

`NoteRenderer.DrawOverlayEffect` resolves art by lane through `GetSpriteColumnForLane`. After the parser correction, its CY/RD entries must match the same contract:

- lane 8 (CY) -> sprite column 6 (`0x16`, crash);
- lane 9 (RD) -> sprite column 1 (`0x19`, ride).

`GetSpriteColumnForChannel` keeps its current numeric mapping (`0x16 -> 6`, `0x19 -> 1`) because that path is already correct. Rename only misleading comments/docs around those entries.

### Mapping documentation

Correct stale mapping comments in the directly affected files, including `Note.cs`. In particular, the `Note.LaneIndex` summary must say `0x17=7 (FT), 0x16=8 (CY), 0x19=9 (RD)`.

Do not opportunistically rewrite unrelated lane/input documentation.

## Test strategy

### Parser regression

Strengthen the existing self-contained `ParseAsync_WithDrumNotes_ShouldParseAllMappedChannels` test so it asserts the lane for each authored drum channel instead of only asserting that notes were parsed. The key red assertions on current `main` are:

- channel `0x16` has `LaneIndex == 8`;
- channel `0x19` has `LaneIndex == 9`.

Keep assertions for the other mapped channels in the same table so this test becomes the executable source of truth for `ChannelToLaneMap` without depending on an external sample chart.

Update the older sample-chart mapping assertion in `DTXChartParserTests` to the same canonical lane order so it no longer documents a contradictory mapping when that fixture is present.

`Note_GetLaneName_ReturnsCorrectNames` keeps the same lane-name behavior; only its channel examples for CY/RD change.

### Renderer regression

Update the existing `GetSpriteColumnForLane_ShouldReturnExpectedMapping` theory:

- lane 8 expects column 6;
- lane 9 expects column 1.

Preserve all other lane and invalid-lane cases.

Channel-based sprite mapping tests, if touched by comment cleanup, must continue to prove `0x16 -> 6` and `0x19 -> 1`.

## Out of scope

- `InputLaneExtensions.FromChannel` / `JudgementTypes` cleanup;
- key-binding or physical-pad remapping;
- lane enum/model refactors;
- changing `GetSpriteColumnForChannel` numeric behavior;
- DTXWeb changes;
- DTXManiaNX changes;
- Tab Mode notation, glyphs, rendering, config, or HUD work;
- compatibility shims for the previously incorrect mapping.

There are no released-user compatibility requirements for preserving the bug; saved scores/config do not own authored channel-to-lane parsing.

## Acceptance

HPA-405 is complete when:

- parsing channel `0x16` yields lane 8 / `CY`;
- parsing channel `0x19` yields lane 9 / `RD`;
- lane 8 overlay lookup uses the crash chip column 6;
- lane 9 overlay lookup uses the ride chip column 1;
- all other drum channel mappings remain unchanged;
- stale crash/ride mapping comments in the touched parser/note/renderer surface are corrected;
- focused parser/renderer tests and the normal game test suite pass;
- no Tab Mode or input-mapping refactor is included.