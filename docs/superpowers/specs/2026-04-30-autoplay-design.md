# AutoPlay: Bug Fix + Per-Note Chip Playback

**Status:** Design approved, ready for implementation planning
**Date:** 2026-04-30
**Scope:** Make AutoPlay actually fire, add drum chip-sound playback for both autoplay and player input, gate player input while autoplay is on.

## Problem

`ConfigData.AutoPlay` is wired through Config UI and read by `PerformanceStage.InitializeAutoPlay`. `ProcessAutoPlay` exists and looks correct on paper, but at runtime nothing happens when autoplay is on: notes scroll past as MISS, no pad lights, no judgement, no score. There is no per-note drum chip-sound playback anywhere — neither for player hits nor for autoplay. Drum sounds today come exclusively from BGM-track events on DTX channel `0x01`; visible drum-lane chips reference `#WAVxx` defs that the parser discards after populating BGM events.

## Goals

1. AutoPlay reliably auto-hits notes during gameplay: judgement registers, score/combo build, pads light up.
2. Drum chip sounds play on auto-hit (per-note WAV lookup), giving autoplay a "ghost player" feel.
3. Player input also produces chip sounds on lane press (parity, single shared cache).
4. While AutoPlay is on, player input is gated — no judgement, no pad press, no chip from player presses. (Differs from DTXManiaNX, which lets them coexist.)
5. Regression coverage: tests prevent autoplay from silently breaking again.

## Non-Goals

- Per-lane autoplay, partial autoplay, demo-mode loop, AI-style imperfect play. Out of scope.
- Refactoring autoplay into its own component (`AutoPlayManager`). Considered (Approach 3) and rejected — too large for a fix.
- Toggling autoplay mid-song. Stage activation remains the source of truth (matches existing behavior).
- Default-thump fallback when player presses a lane with no note in window. Silent on whiff.

## Architecture

Three layers change, in order from chart-load time to runtime:

### 1) Chart parser layer

`DTXChartParser` already builds a local `wavDefinitions` (WAV id → file path) and resolves absolute paths for BGM events. Today it discards that table after parse. Change: surface it on `ParsedChart` as a frozen read-only view.

`ParsedChart` (additive):
```csharp
public IReadOnlyDictionary<string, string> WavDefinitions { get; }
```

- Populated once during parse with fully resolved absolute paths.
- Empty dictionary when no `#WAVxx` lines are present. Never null.
- `Note` schema is unchanged; notes already carry their `Value` (the WAV id string).

### 2) Audio caching layer

New file `Lib/Stage/Performance/ChipSoundCache.cs` (~80 lines), mirrors the existing `_bgmSounds` pattern:

```csharp
public class ChipSoundCache : IDisposable
{
    public Task PreloadAsync(IReadOnlyDictionary<string, string> wavDefs);
    public void Play(string wavId);     // no-op on unknown id
    public bool Contains(string wavId);
    public int Count { get; }
    public void Dispose();
}
```

- Internally `Dictionary<string, ISound> _sounds`.
- `PreloadAsync` swallows per-file failures with a logged warning — one bad WAV doesn't kill preload.
- `Play(wavId)` does `TryGetValue` and plays. Unknown id is silent (charts have stray refs).
- One instance per `PerformanceStage` activation; disposed in `CleanupGameplayManagers`.

A dedicated cache (rather than reusing `_bgmSounds`) keeps lifetime clean: BGM events are scheduled and fire from `TriggerBGMEvent`; chip sounds are demand-played from input/autoplay paths. Separate caches avoid accidental key collisions if BGM and chip ids overlap.

### 3) PerformanceStage trigger + gating

**Field:** `private ChipSoundCache _chipSoundCache = null!;`

**Preload** — added to chart-load flow alongside the existing `LoadBGMSounds()` call:
```csharp
_chipSoundCache = new ChipSoundCache();
await _chipSoundCache.PreloadAsync(_parsedChart.WavDefinitions);
```

**Helper:**
```csharp
private void PlayChipForNote(Note note)
{
    if (note != null && !string.IsNullOrEmpty(note.Value))
        _chipSoundCache.Play(note.Value);
}

private Note? FindNearestNoteForChip(int laneIndex)
{
    // Scan ChartManager.AllNotes from BinarySearchStartIndex(currentTime - 200ms),
    // return nearest unhit note in this lane within ±200ms (HitDetectionWindowMs).
    // Logic mirrors JudgementManager.FindNearestUnhitNote but returns Note rather
    // than runtime data, so judgement state stays private.
}
```

**Autoplay path** — in `ProcessAutoPlay`'s "within window" branch, after `TestTriggerLaneHit`:
```csharp
PlayChipForNote(note);
```

**Player-input path** — extend `OnLaneHitForPadFeedback`:
```csharp
private void OnLaneHitForPadFeedback(object? sender, LaneHitEventArgs e)
{
    if (_autoPlayEnabled) return;
    _padRenderer?.TriggerPadPress(e.Lane, false);
    var nearestNote = FindNearestNoteForChip(e.Lane);
    if (nearestNote != null) PlayChipForNote(nearestNote);
}
```

**Player-input gate on JudgementManager:**
```csharp
public bool IgnorePlayerInput { get; set; } = false;
```

- `OnLaneHit` (private event handler) early-returns when `IgnorePlayerInput || !IsActive || _disposed`.
- `TestTriggerLaneHit` is refactored to enqueue directly into `_pendingLaneHits` instead of calling `OnLaneHit`, bypassing the gate. Still respects `IsActive` and `_disposed`.
- Set in `PerformanceStage` after constructing `_judgementManager`: `_judgementManager.IgnorePlayerInput = _autoPlayEnabled;`

**Disposal** — in `CleanupGameplayManagers`:
```csharp
_chipSoundCache?.Dispose();
_chipSoundCache = null;
```

## Data Flow

**Chart load:**
```text
DTXChartParser.ParseAsync(file)
  → builds wavDefinitions locally
  → resolves each WAV path (relative → absolute)
  → ParsedChart.WavDefinitions = frozen dict
  → ParsedChart.BGMEvents have AudioFilePath (unchanged)
```

**Stage activation:**
```text
ExtractSharedData()
InitializeAutoPlay()                  // reads Config.AutoPlay
InitializeComponents()
  ...async chart load...
  LoadBGMSounds()                     // existing
  _chipSoundCache.PreloadAsync(_parsedChart.WavDefinitions)   // NEW
  _judgementManager.IgnorePlayerInput = _autoPlayEnabled      // NEW
  _isReady = true
```

**Per-frame (active gameplay, song playing):**
```csharp
UpdateGameplay(dt)
  if (_songTimer.IsPlaying) {
    UpdateGameplayManagers(currentTimeMs)
      if (_autoPlayEnabled) ProcessAutoPlay(t)
        for each note in window:
          _judgementManager.TestTriggerLaneHit(lane)   // enqueue, bypasses gate
          _padRenderer.TriggerPadPress(lane, true)
          PlayChipForNote(note)                        // NEW
      _judgementManager.Update(t)        // processes queue, emits JudgementMade
  }
```

**Player-input path (autoplay OFF):**
```text
ModularInputManager.OnLaneHit
  ├── JudgementManager.OnLaneHit              → enqueues
  └── PerformanceStage.OnLaneHitForPadFeedback
        _padRenderer.TriggerPadPress(lane, false)
        FindNearestNoteForChip(lane)           // NEW
        PlayChipForNote(note)                  // NEW
```

**Player-input path (autoplay ON):**
```text
ModularInputManager.OnLaneHit
  ├── JudgementManager.OnLaneHit          → returns (IgnorePlayerInput=true)
  └── PerformanceStage.OnLaneHitForPadFeedback → returns (_autoPlayEnabled=true)
                                                ▲
                                                └─ no judgement, no pad, no chip
```

**Cleanup:**
```text
CleanupGameplayManagers()
  ...existing manager disposes...
  _chipSoundCache.Dispose()      // NEW: releases all loaded ISounds
```

## Diagnostic Step 0 (first task in implementation)

Without a runtime repro the exact root cause of the trigger bug is uncertain. Ranked hypotheses:

| # | Hypothesis | How to confirm | Resulting fix |
|---|---|---|---|
| H1 | Config toggle isn't persisting `AutoPlay=true` to in-memory `Config` (e.g., user exits without saving) | One-shot log at `InitializeAutoPlay`; inspect Config.ini after toggle + restart | Fix save path or document save UX |
| H2 | `_autoPlayEnabled=true` and `ProcessAutoPlay` runs, but ±50ms window is missed because `currentSongTimeMs` advances unevenly | Log first entry to `ProcessAutoPlay`: currentSongTimeMs, first pending note's TimeMs | Widen window OR change loop to "hit any pending note ≤ currentSongTimeMs" |
| H3 | `ProcessAutoPlay` triggers but queued hit is dropped — `IsActive=false` at trigger frame from ordering bug between `_songTimer.IsPlaying` and `_judgementManager.IsActive=true` | Same one-shot log including `_judgementManager.IsActive` | Move `IsActive=true` flip ahead of `UpdateGameplayManagers` |
| H4 | `_chartManager.AllNotes` is empty at autoplay-tick time (chart-load race) | Log `allNotes.Count` in `ProcessAutoPlay` | Defer autoplay until `_chartManager` is populated |

**Diagnostic plan (Step 0):**

1. Add minimal one-shot diagnostic logging:
   - One line at `InitializeAutoPlay` (`_autoPlayEnabled`, `_chartManager?.AllNotes.Count`).
   - One line on **first entry** to `ProcessAutoPlay` per stage activation (gated by `_autoPlayDiagnosticLogged` bool): `currentSongTimeMs`, first pending note's `TimeMs`, `_judgementManager.IsActive`.
   - Total log lines per session: ≤ 2. No per-frame logging.
2. Run the app via the dtxmania MCP (`game_launch` + `game_get_state`). Toggle AutoPlay on, save config, enter Performance stage, inspect state.
3. Match the observed result against H1–H4 and apply that fix.
4. Remove the `ProcessAutoPlay` first-entry log before commit. Keep the `InitializeAutoPlay` line as a permanent breadcrumb.

If none of H1–H4 match, the implementation plan needs a small revision — bounded to ~30 minutes of investigation, not a stalled implementation.

## Touch List

- `DTXMania.Game/Lib/Song/Components/ParsedChart.cs` — add `WavDefinitions` property
- `DTXMania.Game/Lib/Song/DTXChartParser.cs` — populate `WavDefinitions` (resolve paths once)
- `DTXMania.Game/Lib/Stage/Performance/ChipSoundCache.cs` — new file
- `DTXMania.Game/Lib/Stage/Performance/JudgementManager.cs` — add `IgnorePlayerInput`, refactor `TestTriggerLaneHit` to bypass gate
- `DTXMania.Game/Lib/Stage/PerformanceStage.cs` — instantiate cache, preload, fix trigger (per Step 0 result), `PlayChipForNote`, `FindNearestNoteForChip`, gate `OnLaneHitForPadFeedback`, set `IgnorePlayerInput`, dispose
- Tests (see Testing Strategy)

## Testing Strategy

All new tests are pure logic (mocked sounds via `ISound`, no `GraphicsDevice`), so they go in both `DTXMania.Test.csproj` and `DTXMania.Test.Mac.csproj`. No additions to the Mac exclusion list.

**`DTXChartParserTests` (extend):**
- `ParseChart_PopulatesWavDefinitions` — chart with `#WAV01 snare.wav` produces `WavDefinitions["01"]` ending in `snare.wav` (resolved absolute path).
- `ParseChart_NoWavLines_WavDefinitionsEmpty` — empty dict, not null.

**`ChipSoundCacheTests` (new):**
- `Play_UnknownWavId_DoesNotThrow` — silent no-op.
- `PreloadAsync_BadFilePath_LogsAndContinues` — one bad entry doesn't abort the rest.
- `Dispose_ReleasesAllSounds` — verifies `ISound.Dispose` called for each.
- `Contains_AfterPreload_ReturnsTrueForLoaded` / false for unknown.

**`PerformanceStageDeterministicTests` (extend):**
- `ProcessAutoPlay_NoteInWindow_TriggersHitAndChipAndPad` — autoplay enabled, mocks injected, advance one tick, assert all three calls fire with correct lane/wavId.
- `ProcessAutoPlay_NoteOutsideWindow_DoesNotTrigger` — past-window and future-window cases.
- `ProcessAutoPlay_AlreadyHitNote_DoesNotRetrigger` — runtime data status = Hit, skipped.
- **Regression test for whichever H1–H4 root cause is confirmed.**

**`JudgementManagerTests` (extend):**
- `OnLaneHit_WhenIgnorePlayerInputTrue_DropsEvent` — player events don't queue.
- `TestTriggerLaneHit_WhenIgnorePlayerInputTrue_StillEnqueues` — autoplay path bypasses.

**Out of scope:** end-to-end MCP flow (covered by manual run during Step 0), actual audio device emission (mocked).

## Risks

- **Memory:** every chart preloads N chip sounds. Most DTX charts have <50 unique WAVs, each <100KB. Acceptable. If a future chart trips this, lazy-load on first `Play()` is a small follow-up.
- **Disposal ordering:** `ChipSoundCache.Dispose()` must run before `_audioLoader.Dispose()` if there's any shared audio backend. Verified by adding cache dispose to existing `CleanupGameplayManagers` flow before audio loader disposal.
- **WAV path resolution:** parser already does this for BGM. Reusing the same `ResolveBGMPath`-style logic for all entries — no new path-resolution risk.
- **Hypothesis miss:** Step 0 may reveal a fifth root cause. Bounded to ~30min investigation; spec gets a revision then continues.

## Out-of-Scope Follow-ups

- Per-lane autoplay (some lanes auto, others manual).
- AI-style imperfect autoplay (intentional misses, varied timing).
- Refactor autoplay into a dedicated `AutoPlayManager` component.
- Default-thump sound on player whiff in empty section.
- Lazy chip loading.
- Toggling autoplay mid-song.
