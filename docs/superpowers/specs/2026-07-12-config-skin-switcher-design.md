# Config-Stage Skin Switcher

**Date:** 2026-07-12
**Status:** Approved design, pending implementation plan

## Problem

Switching skins today means hand-editing `SkinPath` in `Config.ini` and restarting. With the
CX Neon pack arriving as an alternative to the NX look, the player needs an in-game way to
pick a skin. The building blocks already exist — `SkinManager.SwitchToSystemSkin()`,
`SkinDiscoveryService`, and skin-path plumbing in `ResourceManager` — but nothing in the
game invokes them at runtime.

## Goals

1. A **"Skin" dropdown in the Config stage's System category** listing discovered skins.
2. Switching applies **live in the Config stage** (its own textures reload immediately);
   every other stage picks the skin up on its next activation.
3. The choice **persists to `Config.ini`** and survives restart (round-trips through the
   existing startup path `ResourceManager.SetSkinPath(config.SkinPath)`).
4. Untouched dropdown ⇒ zero behavior change; no existing test modified.

## Non-Goals

- A skin-browser panel (completeness %, author metadata). The dropdown is enough.
- Hot-reloading other stages via `SkinChanged` subscriptions.
- Box.def/song-skin behavior changes (`UseBoxDefSkin` and `LastUsedSkin` stay untouched;
  `LastUsedSkin` remains a dead key).
- Theming (`Theme.ini`) changes — the existing `CurrentTheme` invalidation on
  `SetSkinPath` already covers the switcher.

## Decisions Made

| Decision | Choice |
|---|---|
| Apply timing | Live in Config stage; other stages on next `OnActivate` |
| Logic placement | Local to `ConfigStage` (Approach A) — no `IStageGame`/`BaseGame` changes |
| Persistence | New `IConfigManager` setter following the `SetScrollSpeed` persist-on-edit pattern |
| Persisted value shape | The effective absolute skin path (`Config.SkinPath` is absolute after `NormalizeConfigPaths`; the identical value round-trips through startup) |

## Design

### Discovery and the dropdown

`ConfigStage.SetupConfigItems()` (already re-runs on every activation) creates a
`SkinManager` rooted at `Config.SystemSkinRoot`. Dropdown options =
`AvailableSystemSkins` mapped through `SkinManager.GetSkinName` (base root → `"Default"`,
listed first — existing ordering). Only directories passing the existing validation
(`Graphics/1_background.jpg` + `2_background.jpg`) qualify, so CX Neon appears
automatically once its pack is generated and not before.

The dropdown's `getValue` re-reads the actual current skin name from the resource
manager's current skin path on every frame, so the displayed value always reflects
reality — including after a failed switch (self-correcting UI).

### Switch flow (dropdown setter)

1. `SkinManager.SwitchToSystemSkin(name)` — existing method: clears any box.def
   override, calls `ResourceManager.SetSkinPath(discoveredAbsolutePath)`. Returns false
   if the skin vanished since discovery.
2. On success: persist via new `IConfigManager.SetSkinPath(...)`, mirroring the
   `SetScrollSpeed(configFilePath, value)` persist-on-edit mechanism (deferred write,
   flushed on exit like other config edits). Stored value: the effective absolute skin path the switch produced —
   `Config.ini` already stores absolute paths after `NormalizeConfigPaths`
   resolves them at load, so this matches existing config behavior exactly.
3. On success: ConfigStage releases its stage-local textures and re-runs its graphics
   initialization so the new skin is visible immediately.
4. On failure: log, write nothing, reload nothing — `getValue` snaps the dropdown back.

**Round-trip requirement:** after a restart, startup
(`Game1.cs` `LoadContent` → `ResourceManager.SetSkinPath(config.SkinPath)`) must resolve
to the same effective skin the in-game switch produced. The implementation plan must
verify the persisted relative form against `ConfigManager.NormalizeConfigPaths` and the
skin-root resolution (`AppPaths`) rather than assuming path shapes.

### Live reload scope

Only the Config stage reloads in place (factor its texture loading so
activation and post-switch reuse the same path, releasing previously held references
first — the reference-counted `ResourceManager` cache handles the rest). All other
stages already load art in `OnActivate`, which is when they naturally adopt the new skin.

## Error Handling

- Switch failure (deleted/invalid skin): current skin stays active, nothing persisted,
  dropdown self-corrects. Log via the stage's `ILogger`.
- Zero discovered skins: dropdown lists nothing but displays the current name; cycling
  is a no-op. Harmless degenerate case.
- Discovery I/O errors: `SkinManager.RefreshAvailableSkins` already swallows and logs,
  yielding an empty list (degenerate case above).

## Testing

Mac-safe xUnit (no GraphicsDevice), following the existing reflection-based
`ConfigStageLogicTests` pattern and `MockResourceManager`:

- System category contains a "Skin" dropdown whose options come from discovery.
- Successful switch: resource manager skin path updated **and** `Config.SkinPath`
  persisted in relative form.
- Failed switch: config untouched, current skin unchanged.
- `getValue` reflects the effective skin name (including "Default" for the base root).
- Existing `SkinManager`/`SkinDiscoveryService` tests already cover discovery/validation;
  no existing test is modified.

## Implementation Notes

- Branch: continues on `feat/cx-neon-skin` alongside the CX Neon foundation work.
- Touch points: `ConfigStage.SetupConfigItems` (+ a texture-reload helper),
  `IConfigManager`/`ConfigManager` (one new setter), tests. No `IStageGame`, no
  `BaseGame`, no new types beyond the setter.
