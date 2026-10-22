# HPA-443 — Align Drum Binding Lane Identities (Design)

## Summary

Fix the concrete mismatch between default/user-facing drum bindings and parsed gameplay notes. `InputRouter` forwards `KeyBindings.GetLane` unchanged to gameplay, which compares it to `Note.LaneIndex`; the old binding/config numbering is therefore incorrect, not a cosmetic problem. HPA-443 unblocks HPA-411 (Tab Mode gameplay integration). This is a single small implementation PR, not a new input subsystem.

Verified against `main` at `10fe31e6` and the HPA-443 review discussion.

## Canonical gameplay lane contract

| Lane | Name | Drum | DTX channel(s) |
| ---: | --- | --- | --- |
| 0 | LC | Left Cymbal | `0x1A` |
| 1 | HH | Hi-Hat | `0x11`, `0x18` |
| 2 | LP | Left Pedal / left bass | `0x1B`, `0x1C` |
| 3 | SN | Snare | `0x12` |
| 4 | HT | High Tom | `0x14` |
| 5 | DB | Bass Drum | `0x13` |
| 6 | LT | Low Tom | `0x15` |
| 7 | FT | Floor Tom | `0x17` |
| 8 | CY | Cymbal (right crash) | `0x16` |
| 9 | RD | Ride | `0x19` |

`DTXChartParser.ChannelToLaneMap` already implements this mapping. `Note.GetLaneName` supplies short notation names; `PerformanceUILayout.LaneType` supplies lane IDs to stage code. Do not change the parser, judgement or AutoPlay engine.

## Decisions

### 1. Preserve friendly names with one source

`ConfigStage.cs` already contains the canonical **friendly-name** list for AutoPlay lanes (currently around lines 759–763): `Left Cymbal, Hi-Hat, Left Pedal, Snare, High Tom, Bass Drum, Low Tom, Floor Tom, Cymbal, Ride`.

Move that list into `KeyBindings` to replace the **wrong** `KeyBindings.GetLaneName` switch. `GetLaneName(int)` returns the corresponding friendly name for lanes 0–9 and retains its `Lane {n}` fallback for invalid lanes. In `ConfigStage`, replace the duplicated array by enumerating `KeyBindings.GetLaneName` for lanes 0–9. `DrumCapturePopup`, `KeyConflictChecker`, and the kit zones already obtain labels through `KeyBindings.GetLaneName`.

The UI keeps readable labels such as `Configure: Hi-Hat`, not `Configure: HH`. **Do not** modify `Note.cs` or introduce an `Input → Song.Components` dependency; short notation labels and friendly UI labels serve different purposes. Leave `PerformanceUILayout.LaneNames` untouched; it is not read by production consumers, so no new agreement test is warranted.

### 2. Align keyboard defaults by instrument

Preserve existing keys and reassign only their lane values:

| Key | Lane | Instrument |
| --- | ---: | --- |
| A | 0 | LC |
| J | 1 | HH |
| G | 2 | LP |
| S | 3 | SN |
| K | 4 | HT |
| Space | 5 | DB |
| L | 6 | LT |
| F | 7 | FT |
| D | 8 | CY |
| ; | 9 | RD |

Nine keys keep their intended physical instrument; `D` takes the missing CY slot instead of the former phantom second pedal. Fix misleading channel/name comments including the duplicate `0x1A`. Keep existing button IDs, events, and MIDI APIs unchanged.

### 3. DrumConfig kit: keep the hi-hat pedal, remove the duplicated bass pedal

The hi-hat pedal (**`DrumZoneShape.HiHatPedal`**, currently near x=175/y=598) is a sensible LP/2 instrument: `0x1B` is the hi-hat pedal; CX also places `0x1C` left bass on LP/2. The **mirrored left bass pedal** near x=560/y=600 is redundant and should be replaced by the missing cymbal CY/8 zone. This corrects the previous draft's mistaken claim that the hi-hat pedal had no distinct purpose.

Keep ten clickable zones (first zone remains HH/1) and preserve the current kit positions except for CY at an available upper-right location (approximately x=920–980/y=170–200, adjusted after checking overlap). Map the visual instruments to canonical lanes:

| Zone in existing focus sequence | Lane |
| --- | --- |
| Hi-hat stand | HH/1 |
| Left crash | LC/0 |
| Ride | RD/9 |
| High tom | HT/4 |
| Low tom | LT/6 |
| Snare | SN/3 |
| Floor tom | FT/7 |
| Right bass pedal / kick | DB/5 |
| Hi-hat foot pedal | LP/2 |
| Replaces mirrored left pedal: right crash | CY/8 |

Keep the existing `DrumZoneShape.HiHatPedal` enum, `_hihat` renderer texture lifecycle and associated renderer tests. After the mirrored zone disappears, remove only the dead `DrumZone.FlipHorizontal` property, constructor parameter, renderer conditional sprite effect and its dedicated tests. Retain the zone names/geometry structure and other artwork. Optionally move the right kick pedal towards x=640 if visual evidence shows a misalignment with the decorative kick body; otherwise don't move it.

### 4. Song Selection references gameplay enum

Change LT (switch tab) to `(int)PerformanceUILayout.LaneType.LT` and FT (bookmark) to `(int)PerformanceUILayout.LaneType.FT` rather than hardcoding 6/7. `SongSelectionStage` already imports `DTXMania.Game.Lib.UI.Layout`. Do not add another lane table. Preserve modal suppression, tab order, and bookmarking logic.

### 5. No compatibility or new architecture

There is no legacy binding remap, SQLite schema migration, or runtime translation. Saved old bindings may override the new defaults; **use Drum Key Mapping → Reset to defaults before manual verification**. Leave the parser, judgement/scoring, AutoPlay, MIDI, input events, gameplay rendering, HPA-411 and HPA-509 untouched.

## Verification and acceptance

1. `KeyBindings` and ConfigStage share one friendly-name source; capture/conflict and AutoPlay menus remain descriptive. No dependency from Input to Song is introduced.
2. Every default key maps to its intended parsed **DTX note's lane**, not merely to a name or another hardcoded table. Reuse the existing all-channel parsing fixture in `DTXChartParserAdditionalTests` to compare `Note.LaneIndex` against `new KeyBindings().GetLane(key)` (including SN/S, DB/Space and CY/D). A ten-key default mapping test also pins the defaults.
3. Each visual zone has the correct lane/name/shape; all ten lanes appear once, the first zone is HH/1, `HitTest(zone.Center) == zone.Lane` for **every** zone, and focus, capture, Reset and E2E persistence still work.
4. The new CY zone does not visibly overlap other kit pieces. **Before merge**, capture one real DrumConfig screenshot using the existing DTXMania MCP `game_screenshot` tool, or a manual game screenshot if MCP is unavailable, and attach/link it as review evidence. A headless unit test cannot replace this visual check.
5. Song Selection drum actions use LT/6 and FT/7; former lane 8 and 1 shortcuts no longer invoke those actions.

Avoid new tests on unused `PerformanceUILayout.LaneNames` or `DrumZone.Name`, duplicated `InputRouter` forwarding tests, new GPU harnesses, or unrelated cleanup. Existing `InputRouterTests` already prove direct lane forwarding. Existing `Zones_CoverAllTenLanesExactlyOnce` remains valuable and is retained.

## Scope and delivery

One PR for HPA-443, estimated 1–2 engineering days; same branch for implementation after review. Only binding names/defaults, AutoPlay label reuse, drum-kit zones and their renderer dead-code cleanup, Song Selection shortcuts, and affected targeted tests. No new art or SFX required.

Related: [HPA-405](https://linear.app/cwchanap/issue/HPA-405), [HPA-410](https://linear.app/cwchanap/issue/HPA-410), [HPA-411](https://linear.app/cwchanap/issue/HPA-411).
