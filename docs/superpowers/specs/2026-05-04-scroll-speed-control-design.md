# Scroll Speed Control — Design

**Date:** 2026-05-04
**Status:** Draft for review
**Scope:** Visual scroll-speed control for the Performance stage. Audio playback rate (true PlaySpeed) is **out of scope** and explicitly deferred.

## 1. Summary

Let the player control how fast notes visually fall down the lane. Adjustable from the Config menu, song-select screen, and during play, with `PageUp` / `PageDown` hotkeys. Range x0.5 to x4.0, step x0.5. Every change writes through to `Config.ini` immediately and updates listeners via an event on `IConfigManager`.

The underlying field (`ConfigData.ScrollSpeed`, integer percentage, default 100) and the renderer hook (`NoteRenderer.SetScrollSpeed(int)`) already exist. This design adds the user-facing controls, the in-game indicator, and the central mutation path that ties them together.

## 2. User-facing behavior

| Property | Value |
| --- | --- |
| Range | x0.5 – x4.0 |
| Step | x0.5 (8 discrete values) |
| Storage | integer percentage (`50, 100, 150, …, 400`), step 50 |
| Display format | `x1.0`, `x1.5`, etc. (one decimal) |
| Persistence | write-through to `Config.ini` on every change |
| Affects audio? | No |
| Affects judgement timing? | No |

**Adjustable from:**
- **Config menu** — scroll-speed item, left/right adjusts.
- **Song-select** — `PageUp` / `PageDown` adjusts; current value visible on the panel.
- **In-game (Performance)** — `PageUp` / `PageDown` adjusts mid-song; brief toast indicator shows the new value.

**Boundary behavior:** at `x4.0`, `PageUp` is a no-op; at `x0.5`, `PageDown` is a no-op. No wrap-around.

## 3. Architecture

### 3.1 Single mutation point on `IConfigManager`

Add to `IConfigManager` (and implement on `ConfigManager`):

```csharp
event EventHandler<ScrollSpeedChangedEventArgs>? ScrollSpeedChanged;

void SetScrollSpeed(int percent);       // snaps & clamps, persists, raises event
void AdjustScrollSpeed(int stepDelta);  // +1 / -1 step; calls SetScrollSpeed
```

`ScrollSpeedChangedEventArgs` carries `OldPercent` and `NewPercent`.

`SetScrollSpeed(int percent)` semantics:
1. Snap `percent` to nearest allowed value (multiple of 50).
2. Clamp to `[ScrollSpeedRange.Min, ScrollSpeedRange.Max]`.
3. If equal to current `Config.ScrollSpeed` → no-op (no event, no save).
4. Otherwise: assign, call `SaveConfig()`, raise `ScrollSpeedChanged`.

`AdjustScrollSpeed(int stepDelta)` computes `newPercent = current + stepDelta * Step` and delegates to `SetScrollSpeed`.

### 3.2 `ScrollSpeedRange` helper

New file `DTXMania.Game/Lib/Config/ScrollSpeedRange.cs`:

```csharp
public static class ScrollSpeedRange
{
    public const int Min = 50;
    public const int Max = 400;
    public const int Step = 50;
    public const int Default = 100;

    public static int SnapAndClamp(int percent);     // nearest multiple of Step, clamped
    public static string Format(int percent);        // 50 -> "x0.5", 100 -> "x1.0"
}
```

Single source of truth for range and formatting. Used by ConfigManager, ConfigStage, SongSelectStage, and the in-game indicator.

### 3.3 Input layer

Extend `InputCommandType` (in `DTXMania.Game/Lib/Input/InputManager.cs`):

```csharp
public enum InputCommandType
{
    MoveUp, MoveDown, MoveLeft, MoveRight, Activate, Back,
    IncreaseScrollSpeed,
    DecreaseScrollSpeed
}
```

Add to `InputManager.InitializeDefaultMappings()`:
```csharp
_keyMapping[Keys.PageUp]   = InputCommandType.IncreaseScrollSpeed;
_keyMapping[Keys.PageDown] = InputCommandType.DecreaseScrollSpeed;
```

Hotkeys are edge-triggered via `IsCommandPressed`, so holding the key does **not** repeat. Hold-to-repeat is a deliberate non-goal here; revisit if requested.

### 3.4 Consumers

All three stages subscribe to `ConfigManager.ScrollSpeedChanged` on activation, unsubscribe on deactivation.

**`PerformanceStage`:**
- On `ScrollSpeedChanged` → `_noteRenderer.SetScrollSpeed(e.NewPercent)` and `_scrollSpeedIndicator.Show(e.NewPercent)`.
- Each frame in `OnUpdate`: if `IsCommandPressed(IncreaseScrollSpeed)` → `ConfigManager.AdjustScrollSpeed(+1)`; same for decrease.
- Owns the `ScrollSpeedIndicator` (see 3.5).

**`SongSelectStage`:**
- Same input polling and `AdjustScrollSpeed` calls.
- Renders current value (formatted via `ScrollSpeedRange.Format`) on the song-select status panel area, beside other session-mode displays. Exact pixel position is a layout-class constant added to `SongSelectionUILayout`; choose a free slot near existing indicators rather than introducing a new region.
- On `ScrollSpeedChanged` → mark UI dirty / refresh label text.

**`ConfigStage`:**
- Adds a config item "Scroll Speed: x1.5" using the existing item-adjust mechanism. Left/right calls `ConfigManager.AdjustScrollSpeed(-1)` / `(+1)`. Already copies `originalConfig.ScrollSpeed` (line 214); this stays.
- On `ScrollSpeedChanged` → refresh the displayed value.

### 3.5 `ScrollSpeedIndicator` (new)

`DTXMania.Game/Lib/Stage/Performance/ScrollSpeedIndicator.cs`. Small fade-in/fade-out label drawn over the lane area.

```csharp
public class ScrollSpeedIndicator
{
    public void Show(int scrollSpeedPercent);   // resets timer, sets label text
    public void Update(GameTime gameTime);
    public void Draw(SpriteBatch spriteBatch);
}
```

- Visible duration: 1500 ms (constant in `PerformanceUILayout`).
- Position: top-center of the lane area (constant in `PerformanceUILayout`).
- Renders nothing until first `Show` call.

## 4. Data flow

**In-game `PageUp` press:**
```
Keys.PageUp
  -> InputManager           : edge-detected -> InputCommandType.IncreaseScrollSpeed
  -> PerformanceStage.OnUpdate: IsCommandPressed -> ConfigManager.AdjustScrollSpeed(+1)
  -> ConfigManager           : snap+clamp, assign, SaveConfig(), raise ScrollSpeedChanged
  -> PerformanceStage handler: NoteRenderer.SetScrollSpeed, Indicator.Show
```

**Song-select adjust:** identical path; consumer is `SongSelectStage` (panel label refresh) instead.

**Config menu adjust:** identical path; consumer is `ConfigStage` (item label refresh).

**Game launch:** `ConfigManager.LoadConfig()` populates `Config.ScrollSpeed` (existing behavior). `PerformanceStage` calls `_noteRenderer.SetScrollSpeed(scrollSpeedSetting)` at startup (existing line 541). No change.

## 5. Mid-song correctness

`NoteRenderer` recomputes `_scrollPixelsPerMs` and `EffectiveLookAheadMs` inside `SetScrollSpeed`. Note positions are computed each frame as `JudgementY - _scrollPixelsPerMs * (note.TimeMs - currentSongTimeMs)`, and the active-note window in `PerformanceStage` (lines 694, 716) reads `_noteRenderer.EffectiveLookAheadMs` per frame. Therefore a mid-song speed change:
- Repositions all visible notes instantly (they snap to the new pixels-per-ms).
- Adjusts which notes are "active" (the look-ahead window grows/shrinks) on the next frame.
- Does **not** affect `currentSongTimeMs`, audio, or judgement (those use real time).

The visual snap is a known and acceptable artifact — it's the expected behavior for scroll-speed changes in DTXMania.

## 6. Error handling

- `SetScrollSpeed` clamps and snaps silently. No exceptions for out-of-range input.
- `SaveConfig()` failures are logged via the existing logger; the in-memory value still updates and the event still fires. Config saving is best-effort elsewhere in the codebase — match that.
- `NoteRenderer.SetScrollSpeed` already throws on `<= 0` input; clamp upstream means it never sees an invalid value.
- Stage handlers must unsubscribe from `ScrollSpeedChanged` on deactivation to prevent leaks.

## 7. Testing

### 7.1 Unit tests (Mac-safe)

In `DTXMania.Test/Config/`:
- `ConfigManager_SetScrollSpeed_SnapsToNearestStep` — 117→100, 130→150, 425→400, 30→50.
- `ConfigManager_SetScrollSpeed_ClampsToRange` — 0, -50, 9999 all land inside [50, 400].
- `ConfigManager_SetScrollSpeed_RaisesChangedEventWithOldAndNew`.
- `ConfigManager_SetScrollSpeed_NoOpWhenUnchanged` — no event, no save.
- `ConfigManager_SetScrollSpeed_PersistsToConfigIni` — round-trip via `SaveConfig` / `LoadConfig`.
- `ConfigManager_AdjustScrollSpeed_StepsUpAndDown` — +1 from 100→150; -1 from 50→50 (floor); +1 from 400→400 (ceiling).
- `ScrollSpeedRange_Format_FormatsAsXMultiplier` — 50→"x0.5", 100→"x1.0", 400→"x4.0".
- `ScrollSpeedRange_SnapAndClamp` — boundary and snap cases.

### 7.2 Input tests (Mac-safe)

In `DTXMania.Test/Input/`:
- `InputManager_PageUp_MapsToIncreaseScrollSpeed`.
- `InputManager_PageDown_MapsToDecreaseScrollSpeed`.

### 7.3 Out of automated coverage

The Mac test project excludes graphics/UI tests, so the on-screen indicator, ConfigStage rendering, and SongSelectStage panel rendering rely on manual verification. Existing `NoteRenderer` tests already cover `SetScrollSpeed` math.

### 7.4 Manual verification checklist

1. Launch, open Config menu, change scroll speed to x2.5, exit, restart app → Config.ini and Config menu both show x2.5.
2. In song-select, press `PageUp` / `PageDown` → panel label updates; values clamp at x0.5 and x4.0.
3. Start a song, press `PageUp` mid-song → toast appears, notes visibly fall faster, judgement still feels correct (note that hits register at the same time as before relative to the audio).
4. Press `PageDown` repeatedly to floor → indicator stops updating once at x0.5.
5. Open Config.ini after each change → `ScrollSpeed=...` reflects the latest value.

## 8. Files touched

**New files:**
- `DTXMania.Game/Lib/Config/ScrollSpeedRange.cs`
- `DTXMania.Game/Lib/Config/ScrollSpeedChangedEventArgs.cs`
- `DTXMania.Game/Lib/Stage/Performance/ScrollSpeedIndicator.cs`

**Modified:**
- `DTXMania.Game/Lib/Config/IConfigManager.cs` — add event and methods.
- `DTXMania.Game/Lib/Config/ConfigManager.cs` — implement event and methods.
- `DTXMania.Game/Lib/Input/InputManager.cs` — add enum values and PageUp/PageDown bindings.
- `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs` — add indicator position + duration constants.
- `DTXMania.Game/Lib/Stage/PerformanceStage.cs` — subscribe, poll input, own indicator.
- `DTXMania.Game/Lib/Stage/SongSelectStage.cs` (or its panel component) — subscribe, poll input, render label.
- `DTXMania.Game/Lib/Stage/ConfigStage.cs` — add scroll-speed config item.

**New tests:**
- `DTXMania.Test/Config/ConfigManagerScrollSpeedTests.cs`
- `DTXMania.Test/Config/ScrollSpeedRangeTests.cs`
- `DTXMania.Test/Input/InputManagerScrollSpeedKeysTests.cs`

(All new test files are Mac-safe — no `GraphicsDevice` dependency — so they go in both `DTXMania.Test.csproj` and `DTXMania.Test.Mac.csproj` via the explicit include list.)

## 9. Out of scope (deferred)

- True PlaySpeed (audio + chart playback rate).
- Hold-to-repeat for `PageUp` / `PageDown`.
- Per-song scroll-speed memory.
- Time-stretch vs pitch-shift toggle.
- Score-saving policy for non-default scroll speed (DTXManiaNX legacy `bSaveScoreIfModifiedPlaySpeed` — only meaningful for true PlaySpeed).
