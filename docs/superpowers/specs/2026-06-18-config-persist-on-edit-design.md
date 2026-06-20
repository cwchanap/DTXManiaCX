# Config: Persist-on-Edit (Single Source of Truth)

**Date:** 2026-06-18
**Status:** Approved (design)
**Sibling plan:** `docs/superpowers/plans/2026-06-18-config-persist-on-edit.md` (to be generated)
**Precedent:** `2026-05-04-scroll-speed-control-design.md` (the in-repo blueprint this extends)

## Problem

`ConfigStage` and `DrumConfigStage` each hold **private working copies** of settings
(`_workingConfig`, `_workingDrumBindings`, `_workingSystemBindings`) and commit them only on
exit. Two independent pending copies can drift, which is the root cause of a class of bugs
exemplified by the drum-mapping review finding:

> User reassigns a required system key (e.g. `MoveUp → Z`) in the System Key Mapping panel
> (pending in `ConfigStage._workingSystemBindings`, live `InputManager` untouched). They then
> open Drum Key Mapping: `DrumConfigStage.OnActivate` clones the **live** snapshot, not the
> pending map, so the popup doesn't reject `Z`. `Z` gets bound to a drum lane. On return,
> `ConfigStage.ReloadWorkingDrumBindings` evicts `Z` from the pending system map, and the later
> Config save silently drops the system edit.

A targeted `sharedData` fix was shipped as a stopgap (forwarding the pending map for popup
conflict detection only), but the correct long-term shape is to remove the staging model
entirely so the bug class cannot exist.

## Goals

- `ConfigManager.Config` is the **single source of truth** for every setting. No stage holds a
  private working copy.
- Edits apply **immediately** to live state and are persisted to disk via the existing deferred
  flush (`FlushPendingSave`), exactly as scroll speed already behaves.
- Delete the pending-state machinery (working copies, dirty-flag, discard, snapshot/rollback,
  deferred eviction) that exists only to manage staged commits.
- Make the Comment-1 round-trip bug structurally impossible.

## Non-Goals (YAGNI)

- Generic property-bag / `Set<T>(key, value)` abstraction. We extend the proven typed-setter
  pattern (`SetScrollSpeed`) instead.
- A revert/undo affordance on Back. Back becomes "exit" (see Decisions).
- A user-facing "save failed" banner for flushed settings. Scroll speed surfaces none; we stay
  consistent (failures are logged + retried).
- Auto-restore of a system shortcut when a drum binding is removed (see Eviction decision).

## Decisions

1. **Scope = C.** Bindings + "hot" scalars (scroll speed, AutoPlay, NoFail, audio latency,
   master/BGM/SE volume) live-apply on edit. **Device-affecting** settings
   (resolution/fullscreen/vsync) mutate `Config` + mark dirty on edit but fire **no** live-apply
   event; `GraphicsManager` applies them on stage exit as today (avoids resetting the
   `GraphicsDevice` on every nudge).
2. **Back = exit only.** Edits are already live + dirty-on-disk by the time Back is pressed, so
   Back just leaves the stage. `_hasUnsavedChanges` / `DiscardPendingChanges` delete. (No
   per-stage entry snapshot, no revert.)
3. **Drum eviction = immediate, no auto-restore.** The moment a drum lane claims a non-required
   system key, it is removed from the system map. Removing the drum binding later does **not**
   restore the shortcut (reassign it in the System panel). The deferred-eviction design and its
   "undo restores" behavior were artifacts of the staging model, not designed features.

## Architecture

### `ConfigManager` — typed setters, dirty flag, change events

Extend the `SetScrollSpeed` shape to every setting. Each setter: mutate `Config` → mark
`_pendingSavePath` dirty → fire a typed change event.

| Setting | Setter | Live-apply event? | Subscribers |
|---|---|---|---|
| ScrollSpeed | `SetScrollSpeed` (exists) | yes | `NoteRenderer` (exists) |
| KeyBindings | `SetKeyBindings` | yes | `InputManager` → `ReloadKeyBindings()` |
| SystemKeyBindings | `SetSystemKeyBindings` | yes | `InputManager` → rebuild system map |
| AutoPlay, NoFail, AudioLatency | `SetAutoPlay` / `SetNoFail` / `SetAudioLatency` | yes (fire; read on next song start) | — |
| Master/BGM/SE volume | `SetMasterVolume` / `SetBGMVolume` / `SetSEVolume` | yes (if audio supports live change — *open item*) | audio system |
| Screen width/height, FullScreen, VSync | `SetResolution` / `SetFullscreen` / `SetVSync` | **no** | `GraphicsManager` applies on exit |

`SaveKeyBindings` / `SaveSystemKeyBindings` (which today write to `Config` in-memory without
dirty/event) are refitted into the dirty+firing setters above.

### Disk persistence (unchanged mechanism)

`FlushPendingSave()` writes the whole file atomically (`SaveConfig` already does temp-file +
`File.Move`) and is called on stage deactivation. **New requirement:** `ConfigStage.OnDeactivate`
and `DrumConfigStage.OnDeactivate` must call `FlushPendingSave()` (today only Song/Performance
stages and `Game1` do). On flush failure: log, keep the dirty flag, retry next flush; in-memory
`Config` stays correct.

### Stages read truth, call setters

- `OnActivate`: read `ConfigManager.Config` for display. No working copies, no preservation
  branch, no `LoadWorkingInputBindings` / `ReloadWorkingDrumBindings`.
- Edits: call the matching `ConfigManager` setter.
- `Back`: `ChangeStage(...)` (exit). `OnDeactivate`: `FlushPendingSave()`.

## Bindings flow (the crux)

**Ownership: popup = intent, stage = mutation, `ConfigManager` = truth.**

`DrumCapturePopup` stops mutating a `KeyBindings`. `TryCapture(button)` reads the system map via
its provider, shows the `ShowingConflict` message if the key is **required** (rejects), otherwise
returns `Captured` with the button — mutating nothing. `RemoveBinding` / `ClearLane` / reset
become requests the stage carries out. The system-map provider collapses from `pending ?? live`
to a single `() => ConfigManager.Config.SystemKeyBindings` — always current, no divergence.

The stage carries out edits immediately via `ConfigManager` setters. On capture:
1. Clone `Config.KeyBindings`, add `button → lane`, call `SetKeyBindings`.
2. If the claimed key is a keyboard key currently in the system map (non-required — required was
   already rejected by the popup): clone `Config.SystemKeyBindings`, remove it, call
   `SetSystemKeyBindings`.

That **is** the eviction (Decision 3) — moved out of the deferred `Save()` and into the capture
handler. `EvictSystemKeysClaimedByDrumLanes` and all of `DrumConfigStage.Save()` delete.

**System Key Mapping panel:** `OnPanelSaved` calls `ConfigManager.SetSystemKeyBindings(snapshot)`
immediately (today it writes `_workingSystemBindings`, committed at Save).

**Round-trip (the Comment-1 scenario) needs no special handling:** Config edits system keys →
`SetSystemKeyBindings` (live + dirty-disk) → DrumConfig reads `ConfigManager.Config`
(authoritative) → captures a key, evicts immediately → returns to Config, re-reads
`ConfigManager.Config`. No working copies to drift, no `sharedData`. The bug is gone by
construction.

## Error handling & atomicity

- **No snapshot/rollback.** That machinery existed only because the old `Save` mutated `Config`
  *before* the disk write, so a disk failure needed rollback to keep memory consistent with disk.
  Here `Config` is the truth and is mutated on every edit; disk just catches up at flush. A flush
  failure means disk lags memory until the next flush — identical to today's scroll-speed
  behavior.
- **Atomicity is free.** A capture does `SetKeyBindings` then `SetSystemKeyBindings`
  synchronously in one call stack (no `await` between them). Disk writes only at flush,
  whole-file. A crash mid-edit either completes both in-memory mutations or, on restart, reloads
  the not-yet-flushed pre-edit state wholesale — never a half-evicted mapping on disk.
- **Live-apply failures are best-effort + logged.** `ConfigManager` fires events synchronously;
  per-subscriber try/catch ensures one bad listener cannot break the edit or roll back `Config`.
  `Config` stays the truth; live state catches up on the next reload.

## What gets deleted

- `ConfigStage`: `_workingConfig`, `_workingDrumBindings`, `_workingSystemBindings`,
  `_navigationBindings`, `_hasUnsavedChanges`, `DiscardPendingChanges`, `LoadWorkingInputBindings`,
  `ReloadWorkingDrumBindings`, the `OnActivate` preservation branch, the snapshot/rollback inside
  `ApplyConfiguration`, `_saveError`.
- `DrumConfigStage`: `_workingBindings`, `_workingSystemBindings`, all of `Save()`
  (eviction/rollback/`_saveError`/`EvictSystemKeysClaimedByDrumLanes`), the `PendingSystemBindingsKey`
  const + `ResolvePopupSystemMapping` + the `sharedData` wiring from the stopgap fix.
- `DrumCapturePopup`: direct mutation of the passed `KeyBindings` (becomes intent-only).

## Testing

- **ConfigManager:** each new setter mutates + marks dirty + fires its event; `FlushPendingSave`
  persists, and on failure keeps dirty + logs (mirror the existing scroll-speed flush test).
- **InputManager:** subscribes to `KeyBindingsChanged` / `SystemKeyBindingsChanged` → reloads
  keymap + system map.
- **DrumCapturePopup (rewritten):** `TryCapture` returns `Captured` (intent, no mutation) /
  `Rejected` (required-key conflict message intact).
- **DrumConfigStage:** eviction now happens **at capture** (replaces
  `Save_EvictsSystemKeysClaimedByDrumLanes`); `Back` = exit; no `Save_*` tests.
- **ConfigStage:** toggles call setters; `OnPanelSaved` → `SetSystemKeyBindings`; `Back` = exit;
  `OnDeactivate` flushes.
- **Comment-1 regression test (the proof):** system edit in Config → DrumConfig round-trip →
  assert the system edit survives, because there is no pending state to lose. Successor to the
  `sharedData` tests being deleted.
- **Deleted:** `PendingSystemBindingsKey` / `sharedData` tests, `Save_*` eviction/rollback tests,
  `_hasUnsavedChanges` / `DiscardPendingChanges` tests.
- **Verification gate:** Mac build + full `DTXMania.Test.Mac.csproj` green before done.

## Open items to confirm during planning

- **Audio volume live-apply:** does the audio system support changing master/BGM/SE volume without
  re-init? If not, volume setters degrade to dirty+flush-only (applies next audio init) and are not
  truly "hot." Verify before finalizing the volume subscriber wiring; downgrade scope if needed.
- **`SystemKeyBindings` in-memory representation:** `ConfigData` stores it as
  `Dictionary<string,string>` (`"SystemKey.<Command>" → "<Keys>"`). The stages currently work in
  `Dictionary<Keys,InputCommandType>`. Decide whether the setter accepts the `Keys`-keyed form
  (and does the conversion internally) or whether `ConfigData` is refactored — prefer the former to
  keep the on-disk format stable.

## Rollout note

The stopgap Comment-1 fix (sharedData + `ResolvePopupSystemMapping`) is correct and shippable on
its own; it is deleted by this refactor. If the refactor is deferred, the stopgap remains a valid
safety improvement.
