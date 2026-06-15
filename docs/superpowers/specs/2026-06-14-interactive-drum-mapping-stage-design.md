# Interactive Drum Mapping Stage — Design

**Date:** 2026-06-14
**Status:** Approved (brainstorming), ready for implementation planning

## Summary

Add a new standalone stage that presents the drum kit visually and lets the
user remap drum inputs by clicking (or keyboard-focusing) a drum piece, then
hitting the input they want bound to it. It replaces the existing keyboard-only
list panel (`DrumKeyAssignPanel`) with a graphical, device-agnostic equivalent.

The user's original phrasing ("hit the HDMI") was clarified to mean "hit
whatever input device fires" — keyboard now, MIDI/gamepad later.

## Goals

- A visual drum-set screen where each of the 10 drum lanes is a clickable zone.
- Click or keyboard-focus a piece → popup that captures the next input from any
  device and binds it to that piece's lane.
- Each piece shows its current binding(s) at a glance (keyboard keys plus any
  captured MIDI/pad bindings).
- Device-agnostic capture: works with the keyboard today and accepts MIDI/gamepad
  automatically once those input sources exist, with no UI changes.

## Non-Goals (explicitly out of scope)

- **Implementing MIDI or gamepad input drivers.** Only the keyboard `IInputSource`
  exists today; MIDI/gamepad are separate future sub-projects. This project adds
  the device-agnostic *capture seam* so they slot in later, but does not build the
  device drivers themselves.
- Preprinting "expected General-MIDI note" hints on pieces. Chips show only real,
  currently-bound inputs; MIDI appears only once a note is actually captured.
- Changes to the System Key Mapping panel or to `KeyConflictChecker`.

## Decisions (from brainstorming)

| Topic | Decision |
| --- | --- |
| Input source | Capture whatever device is hit (device-agnostic). |
| Scope split | UI + capture seam now; MIDI/gamepad drivers later, separately. |
| Placement | New standalone `StageType.DrumConfig`; retire old `DrumKeyAssignPanel`. |
| Selection | Mouse click **and** keyboard fallback (arrows/Tab focus + Enter). |
| Graphic fidelity | Drawn shapes (primitives) in a realistic kit arrangement. |
| Binding display | Each zone shows current bindings; MIDI shown only once bound. |
| Capture model | Hitting an input **appends** a binding (multiple per piece). |
| Save model | Edit a working copy; commit on Save, discard on Cancel/Back. |

## Architecture

A new stage, `DrumConfigStage` (`StageType.DrumConfig`), reached from the existing
**ConfigStage → "Drum Key Mapping"** menu item. It renders a drawn drum kit of 10
zones (1:1 with the 10 drum lanes), supports mouse and keyboard selection, opens a
capture popup, and commits bindings on Save.

### New components

Each unit has a single purpose and a clear rendering/logic split so the logic is
unit-testable without a `GraphicsDevice`.

1. **`Lib/Stage/DrumConfigStage.cs`** — orchestrator. Owns the working
   `KeyBindings` clone, the current selection/focus index, the popup instance, and
   the Save/Cancel flow returning to `StageType.Config`. Reads mouse via
   `Mouse.GetState()` (with left-button edge detection, matching
   `SongSelectionStage`); reads keyboard for navigation.

2. **`Lib/Stage/DrumConfig/DrumKitLayout.cs`** — pure data + geometry, no drawing:
   - The 10 zone definitions: lane index, display name (from
     `KeyBindings.GetLaneName`), shape kind (cymbal / drum / pedal / kick), center
     position, and size.
   - `HitTest(Point) -> int?` returning the lane under a screen point.
   - Keyboard focus order (a stable sequence over the 10 zones plus the
     Save / Cancel / Reset actions).
   - Unit-testable, Mac-safe.

3. **`Lib/Stage/DrumConfig/DrumKitRenderer.cs`** — draws the zones (primitives via
   a white-pixel texture), the per-zone binding chips (text from
   `KeyBindings.GetLaneDescription` / `FormatButtonId`), and hover / focus /
   selected highlights. Pure render-from-data. Excluded from
   `DTXMania.Test.Mac.csproj` like the other renderers.

4. **`Lib/Stage/DrumConfig/DrumCapturePopup.cs`** — modal state machine with states
   `Listening` and `ShowingConflict`. Displays current bindings (each with a ✕
   remove affordance) and a "Clear all" action; on a captured input runs the
   conflict check then appends the binding. Raises events when bindings change and
   when it closes. Drawing is separated from state so the state machine is
   unit-testable.

### Device-agnostic capture seam

Add a minimal "buttons pressed this frame, from any device" feed to
`ModularInputManager`: a poll method `IReadOnlyList<ButtonState>
ConsumePressedButtons()` that returns every input-source button — and keyboard
key — that transitioned to pressed this frame, then clears its internal buffer.
A poll method (rather than an always-on event) keeps the feed inert outside of
capture and is trivial to drive in tests.

- Keyboard flows through it today.
- MIDI/gamepad sources (future sub-projects) flow through it automatically with no
  popup changes, because each source already produces a generic `ButtonState`
  whose `Id` is a `Key.*` / `MIDI.*` / `Pad.*` string.
- Reuses existing `ButtonState` and `InjectButton` plumbing, so it is testable via
  `ModularInputManager.InjectButton`.

The popup consumes this feed and binds the first **non-navigation** button.

## Data flow

1. **Enter** from ConfigStage "Drum Key Mapping" → `ChangeStage(StageType.DrumConfig, ...)`.
   On activate, clone the live `KeyBindings` into a working copy.
2. **Select** a zone: hover/focus via mouse move or arrows/Tab; click or Enter opens
   the popup for that lane.
3. **Capture**: the popup listens; the first captured input is checked with
   `KeyConflictChecker.GetRequiredSystemConflict`:
   - Reserved navigation/core keys are **rejected** with a transient notice.
   - A non-required system key binding (e.g. an action key) is **auto-evicted**
     (deferred until Save), mirroring `DrumKeyAssignPanel`'s existing behavior.
   - Otherwise the `buttonId` is **appended** to the lane's bindings.
   - ✕ removes a single binding; "Clear all" unbinds the lane.
4. **Save** → replace the live `KeyBindings` contents with the working copy, apply
   the deferred system-key evictions, call `ModularInputManager.SaveKeyBindings()`
   (persists to `Config.ini`), then return to `StageType.Config`.
   **Cancel / Back** (Esc at stage level with no popup open) → discard the working
   copy and return to Config.
5. **Reset to defaults** → reset the working copy via the existing
   `ResetKeyBindingsToDefaults` path (still requires Save to persist).

## Integration & cleanup

- Add `StageType.DrumConfig` to the `StageType` enum (`IStageManager.cs`) and the
  corresponding case in `StageManager`'s stage factory.
- ConfigStage: the "Drum Key Mapping" `NavigationConfigItem` transitions to
  `StageType.DrumConfig` instead of `OpenPanel(_drumPanel)`. Remove the `_drumPanel`
  field, the `DrumKeyAssignPanel` wiring, and ConfigStage's ownership/commit of
  `_workingDrumBindings` — the new stage now owns drum-binding commit, so drum
  changes persist on the new stage's Save (independent of ConfigStage's own
  Save/Cancel).
- Keep the System Key Mapping panel (`SystemKeyAssignPanel`), `IKeyAssignPanel`, and
  `KeyConflictChecker`.
- Retire `DrumKeyAssignPanel.cs` and its tests.

## Drum-kit layout & lane mapping

Ten zones arranged like a kit; mapping uses `KeyBindings.GetLaneName()` as the
source of truth:

| Zone (visual) | Lane | Lane name | Default key |
| --- | --- | --- | --- |
| Crash | 0 | Splash/Crash | A |
| Floor Tom | 1 | Floor Tom/Left Cymbal | F |
| Hi-Hat Foot (pedal) | 2 | Hi-Hat Foot/Left Crash | D |
| Left Pedal | 3 | Left Pedal | G |
| Snare | 4 | Snare Drum | S |
| Hi-Hat | 5 | Hi-Hat | J |
| Kick | 6 | Bass Drum | Space |
| Hi Tom | 7 | High Tom | K |
| Low Tom | 8 | Low Tom/Right Cymbal | L |
| Ride | 9 | Ride | ; |

The picture shows the primary piece for combined-name lanes; the label shows the
full lane name. A lane may hold multiple bindings; the chip lists all of them.

## Testing strategy

- **`DrumKitLayout`** — hit-test and focus order: unit tests, Mac-safe.
- **`DrumCapturePopup`** — state machine: append, remove single, clear all, reject a
  navigation key, deferred system-key eviction, cancel-vs-commit. Driven with a
  real/working `KeyBindings`, a stub system mapping, and injected captured buttons.
  Mac-safe.
- **`ModularInputManager`** capture feed — unit test via `InjectButton` /
  `ConsumePressedButtons`. Mac-safe.
- **`DrumKitRenderer.Draw`** and anything touching `GraphicsDevice` — Windows-only;
  exclude new renderer tests from `DTXMania.Test.Mac.csproj` (as with
  `SongBarRendererTests`, etc.).

## UX details

- No capture timeout: listening continues until an input is hit or the popup is
  cancelled (matches the current panel).
- Esc / right-click closes the popup; Esc at stage level (no popup) cancels back to
  Config.
- Selection works by mouse click and by keyboard focus (arrows/Tab + Enter); the
  focused zone is highlighted for mouse-less use.

## Open follow-ups (separate projects)

- MIDI input source (`IInputSource`) so e-drum kits register through the capture
  seam end-to-end.
- Gamepad input source.
