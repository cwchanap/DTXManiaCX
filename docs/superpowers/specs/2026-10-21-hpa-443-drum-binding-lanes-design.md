# HPA-443 — Drum Binding Lane Identity Alignment (Design)

## Goal and rationale
DTXManiaCX must use the same integer lane identity for a parsed drum note, a captured or default drum binding, drum-configuration visual zones, and song-selection drum shortcuts. This is a prerequisite for HPA-411 Tab Mode gameplay integration, **not** a Tab Mode implementation.

On current `main` (verified 2026-10-21), `InputRouter.ProcessButtonState` forwards `KeyBindings.GetLane` directly as `LaneHitEventArgs.Lane`; there is no translation. The 10 entries in `KeyBindings.GetLaneName` / `LoadDefaultBindings` are based on an older, contradictory order. `DrumKitLayout` embeds the same old IDs. `SongSelectionStage` treats 8 as Low Tom and 1 as Floor Tom even though gameplay uses 6 and 7. `DTXChartParser`, `Note.GetLaneName()` and `PerformanceUILayout.LaneType` already agree on the gameplay order.

## Canonical lane contract

| Lane | Name | Instrument | Parsed DTX channels |
| ---: | --- | --- | --- |
| 0 | LC | Left crash | `0x1A` |
| 1 | HH | Hi-hat | `0x11`, `0x18` |
| 2 | LP | Left pedal / left bass | `0x1B`, `0x1C` |
| 3 | SN | Snare | `0x12` |
| 4 | HT | High tom | `0x14` |
| 5 | DB | Bass drum | `0x13` |
| 6 | LT | Low tom | `0x15` |
| 7 | FT | Floor tom | `0x17` |
| 8 | CY | Right crash cymbal | `0x16` |
| 9 | RD | Ride | `0x19` |

Keep `DTXChartParser.ChannelToLaneMap` and note/judgement handling unchanged. HPA-405 already fixed crash/ride.

## Design decisions

### Names and shared contract
- Keep `Note.GetLaneName()` as the existing canonical short-name mapping. Prefer a minimal static `Note.GetLaneName(int lane)` overload with the instance member delegating to it (or an equivalent small shared non-UI helper).
- `KeyBindings.GetLaneName(int)` delegates to the canonical mapping for valid lanes; retain `Lane {n}` for invalid indices. `DrumKitLayout` and capture/conflict messages already consume `KeyBindings.GetLaneName`.
- Do **not** make the input layer depend on `PerformanceUILayout`; do not add a second independent lane-name table. Keep existing `PerformanceUILayout.LaneType` / `LaneNames` intact and cover their agreement with the canonical order in tests.

### Default keyboard bindings
Remap the existing ten keyboard keys by instrument rather than keeping their obsolete integer targets. One key per canonical lane:

| Key | Lane | Intent |
| --- | ---: | --- |
| A | 0 | LC (unchanged) |
| J | 1 | HH |
| G | 2 | LP |
| S | 3 | SN |
| K | 4 | HT |
| Space | 5 | DB |
| L | 6 | LT |
| F | 7 | FT |
| D | 8 | CY (previously labeled a crash/foot shared zone) |
| ; | 9 | RD (unchanged) |

This preserves established physical keys for named drums where possible; `D` becomes CY rather than keeping a contradictory duplicate pedal target. Keep existing button ID formats and assignment APIs; no translation, additional key, or config schema change.

### Drum-kit visual zones
Update `DrumKitLayout.Zones` to have the canonical lane at the **visually corresponding** instrument. Keep 10 focusable pieces and existing geometry for nine pieces where appropriate:

| Visual piece | Canonical lane |
| --- | ---: |
| Hi-hat stand (current zone 0) | HH / 1 |
| Left crash | LC / 0 |
| Ride | RD / 9 |
| High tom | HT / 4 |
| Low tom | LT / 6 |
| Snare | SN / 3 |
| Floor tom | FT / 7 |
| Right bass pedal | DB / 5 |
| Left bass pedal (mirrored) | LP / 2 |
| Right crash cymbal | CY / 8 |

The current extra `HiHatPedal` zone has no distinct gameplay lane. Replace that interactive zone with a cymbal-shaped CY zone in a free upper-right area (roughly x=920–980, y=170–200 in 1280x720 design coordinates); confirm the final location does not overlap Ride or the toms. Keep existing art reuse, hit radii, focus count/order and click/focus semantics. The decorative kit art need not be remodeled. Do not map two visual zones to LP or omit CY merely to avoid the one necessary geometry change.

### Song Selection
Switch the Low Tom tab shortcut from old lane 8 to canonical LT/6, and the Floor Tom bookmark shortcut from old lane 1 to canonical FT/7. Delete comments describing a separate `KeyBindings` numbering system. The existing event subscription, modal suppression, tab cycling and bookmark logic are otherwise unchanged.

### Existing saved bindings
No migration for pre-fix numeric lane values; this is an unreleased hobby migration with no backward-compatibility requirement. New/default mappings become canonical. Developers with an old persisted drum mapping can use the existing Reset-to-defaults UI to regenerate it. Do not silently remap arbitrary user-authored keys, add an auto-migration, or change SQLite persistence.

## Acceptance / proof
1. Each default keyboard binding resolves to exactly the expected lane; all ten canonical lanes are represented.
2. `KeyBindings.GetLaneName`, `Note.GetLaneName`, existing layout lane names, and parsed DTX notes agree on the ten integer identities.
3. Each kit zone's instrument, shape, lane/name, and center hit test agree; zones cover each gameplay lane once. Keyboard focus, hover, capture, and Reset still work.
4. An `InputRouter` key/pad press produces the same `LaneHitEventArgs.Lane` used by `Note.LaneIndex` / `JudgementManager` for that drum, with no runtime translation.
5. Low Tom switches song tabs and Floor Tom toggles bookmarks using lanes 6 and 7; the former 8 and 1 targets do not trigger those actions.
6. Unit tests work without a `GraphicsDevice`; existing drum-config E2E persists the corrected first-zone HH/1 binding.
7. No changes to chart parsing, scoring, AutoPlay, Tab Mode, performance rendering, MIDI infrastructure, device input APIs, or unrelated configuration.

## Risks and limits
- Changing a legacy test expectation from an old integer to another old integer is insufficient: assert identity **by instrument** and preferably by chart channel.
- `Note.LaneIndex` documentation is already slightly stale in nonessential prose; correct only any directly touched comment that would contradict the contract.
- Visual CY placement is the only intentional geometry adjustment; use a screenshot/manual hit check if available, not a broad visual redesign.
- This entire ticket ships in **one PR**. Keep it within approximately 1–2 engineer days and do not bundle HPA-411 or HPA-509.

## Related work
- [HPA-405](https://linear.app/cwchanap/issue/HPA-405) — completed parser crash/ride fix
- [HPA-410](https://linear.app/cwchanap/issue/HPA-410) — completed notation strip renderer
- [HPA-411](https://linear.app/cwchanap/issue/HPA-411) — blocked until this ticket is delivered
