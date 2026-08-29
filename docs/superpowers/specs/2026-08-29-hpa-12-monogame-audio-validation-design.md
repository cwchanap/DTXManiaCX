# HPA-12 MonoGame Audio Validation and Stale Setting Cleanup Design

## Summary

HPA-12 is a validation-and-cleanup ticket, not an audio-backend project.

CX already has one cross-platform playback path built on MonoGame `SoundEffect` / `SoundEffectInstance`. Windows uses the WindowsDX MonoGame target and macOS uses DesktopGL. `SongTimer` starts, pauses, resumes, and stops the `SoundEffectInstance` while `PlaybackClock` owns the logical chart clock. `PerformanceStage` applies `AudioLatencyOffsetMs` only to manual player judgement/chip lookup/miss timing; autoplay, BGM scheduling, chart visuals, progress, video, and completion stay on the raw logical chart clock.

The current configuration still contains one stale NX-era-looking property:

```csharp
public int BufferSizeMs { get; set; } = 100;
```

Current `main` does not read it in playback, persist it through `ConfigManager`, or expose it in the config UI. The only other reference is a test object initializer. Its presence is therefore misleading rather than functional.

`AudioLatencyOffsetMs` is different: it is persisted, exposed in the config UI, and used by `PerformanceStage`. Keep it, but remove the unsupported assumption that a hard-coded `200 ms` compensation is a defensible cross-platform default. Fresh/default configuration should use `0 ms`; players can opt into compensation when their actual output path needs it.

## Goals

1. Validate the existing MonoGame playback path on representative real devices before declaring it acceptable.
2. Keep the current MonoGame playback implementation unless validation exposes a concrete blocking defect.
3. Remove `BufferSizeMs` completely because CX cannot configure or observe that value through its current playback path.
4. Change the fresh/default `AudioLatencyOffsetMs` value from `200` to the neutral `0` and make comments describe only behavior the code actually owns.
5. Preserve the existing manual `Audio Latency Offset` setting and its judgement-only semantics.
6. Keep the implementation small enough for one focused PR and comfortably below three engineer days.

## Non-goals

HPA-12 does **not** add or prototype:

- DirectSound, ASIO, WASAPI, BASS, NAudio, or another native playback backend;
- an `IAudioEngine`, backend registry, device manager, or plugin system;
- buffer-size configuration;
- output-device selection;
- automatic latency measurement or calibration;
- per-device latency profiles;
- a latency benchmark harness;
- changes to Play Speed, pitch processing, chart timing, judgement windows, or MIDI/input timing;
- `UseOSTimer` or another clock source.

If real-device validation finds a backend-specific problem that cannot be fixed inside the existing MonoGame path, record the evidence and create a separate follow-up. Do not widen HPA-12 to solve it.

## Verified current architecture

### Playback is already MonoGame-owned

`DTXMania.Game/Lib/Resources/ManagedSound.cs` decodes supported source media and creates a MonoGame `SoundEffect`. Playback is performed by `SoundEffectInstance.Play()` / `Pause()` / `Resume()` / `Stop()`.

FFmpeg and NVorbis are decode/conversion dependencies here, not alternate runtime playback engines.

Platform projects select the MonoGame implementation:

```text
Windows: MonoGame.Framework.WindowsDX 3.8.*
macOS:   MonoGame.Framework.DesktopGL 3.8.*
```

No HPA-12 code should bypass these project-level platform choices.

### Logical chart time and audio transport are intentionally separate

`SongTimer` owns an optional `SoundEffectInstance` plus a `PlaybackClock`.

On `Play`, `Pause`, `Resume`, and `Stop`, it updates both the audio instance and the logical clock. `GetCurrentMs(...)` returns the logical clock position; it does not query an audio-device playback cursor.

This means HPA-12's validation question is whether the existing transport stays perceptually aligned well enough for CX. It is **not** an invitation to derive a second clock from the audio backend.

### `AudioLatencyOffsetMs` is a manual judgement correction

`PerformanceStage` already uses two timing views:

```text
raw logical chart time
  -> autoplay
  -> BGM/video events
  -> note visuals
  -> progress/completion

latency-compensated player judgement time
  -> manual judgement
  -> manual chip lookup
  -> pending-hit / miss timing
```

The offset remains expressed as real milliseconds. `PerformanceStage` converts it to logical chart time according to the frozen Play Speed profile before subtracting it from the raw clock.

This is the correct seam to keep. Do not move compensation into `SongTimer`, `PlaybackClock`, `ManagedSound`, or chart parsing.

### `BufferSizeMs` is dead configuration

Repository-wide search on current `main` finds `BufferSizeMs` only in:

```text
DTXMania.Game/Lib/Config/ConfigData.cs
DTXMania.Test/BaseGameTests.cs
```

It is absent from `ConfigManager` parsing/snapshot persistence and absent from the playback path. Therefore removal requires no replacement setting and no configuration migration.

## Design decisions

### 1. Keep the MonoGame playback path

The default outcome is no playback architecture change.

Real-device validation is a gate that can falsify this decision. A follow-up is justified only if validation produces a repeatable problem such as:

- audible/visible drift that grows during a normal song;
- pause/resume producing a repeatable permanent alignment shift;
- wired output latency severe enough that manual compensation cannot make normal play usable;
- platform-specific playback failure or instability.

Subjective preference for a different backend or theoretical lower latency is not sufficient evidence for HPA-12 to replace MonoGame.

### 2. Delete `BufferSizeMs`; do not replace it

Remove the property from `ConfigData` and remove the stale test initializer.

Do not add:

- `AudioBufferSizeMs` under another name;
- a backend-specific buffer option;
- an ignored compatibility row in SQLite;
- a migration for a key that current `ConfigManager` never persisted.

There is no runtime behavior to preserve.

### 3. Use `0 ms` as the fresh/default latency compensation

Change:

```csharp
public int AudioLatencyOffsetMs { get; set; } = 200;
```

to:

```csharp
public int AudioLatencyOffsetMs { get; set; } = 0;
```

Rationale:

- output latency is device/driver/OS-path dependent;
- current CX has no measurement proving one non-zero value applies across WindowsDX and DesktopGL;
- the current `200 ms` explanation depends on the dead `BufferSizeMs=100` setting and therefore overstates what CX controls;
- a non-zero universal default can systematically over-correct low-latency wired paths;
- `0 ms` has clear semantics: do not guess; let the user add compensation if their setup needs it.

No schema or one-time migration is needed. Existing persisted `AudioLatencyOffsetMs` values continue to load normally; fresh config and `ResetToDefaults()` receive `0`. HPA-12 does not try to infer whether an existing persisted `200` was user-selected or inherited from an old default.

### 4. Keep the existing manual UI and persistence

The `Audio Latency Offset` item remains:

```text
range: 0..500 ms
step:  10 ms
```

`ConfigManager.SetAudioLatency(...)`, SQLite parsing/snapshot persistence, and the current config-stage interaction remain unchanged except for tests that explicitly assert the default value.

Do not add negative values or split input/output offsets in this ticket. Current runtime semantics only model delayed audible output, and HPA-12 is not redesigning calibration.

### 5. Correct comments without changing timing behavior

Update comments that currently present `200 ms`, `BufferSizeMs=100`, or a presumed OpenAL buffering model as factual CX behavior.

The replacement documentation should say only:

- `AudioLatencyOffsetMs` is optional manual output-latency compensation;
- `0` means no compensation;
- it affects manual player judgement/chip/miss timing only;
- autoplay, BGM/video scheduling, visuals, progress, and stage completion use the raw logical chart clock;
- Play Speed conversion remains handled by the existing `PerformanceStage` helper.

Do not rewrite working timing code merely to make the comments easier to explain.

## Real-device validation gate

Validation is intentionally manual and lightweight. Do not build instrumentation solely for HPA-12.

### Required environments

Before the PR becomes ready for review, run the same small smoke on:

1. Windows with the normal/default **wired** output path;
2. macOS with the normal/default **wired** output path;
3. one USB audio interface, if one is readily available during implementation.

Bluetooth is not a required acceptance path because its transport latency is expected to be materially larger and device-specific; the manual offset remains available for such setups.

### Validation fixture

Use one known-good DTX chart already available to the implementer that has:

- clearly audible drum/chip attacks;
- continuous or easily recognizable BGM alignment;
- enough duration to notice accumulating drift;
- a normal pause/resume opportunity.

Use the same chart where practical across environments. Do not commit a new media corpus just for this ticket.

### Procedure

For each environment:

1. Start from `Audio Latency Offset = 0 ms`.
2. Play the opening and confirm BGM/chart visuals begin without an obvious fixed misalignment.
3. Hit several clear notes and assess whether chip response/manual judgement is playable without an obvious large systematic delay.
4. Continue far enough into the song to detect accumulating drift rather than only startup latency.
5. Pause for several seconds, resume, and verify BGM/chart alignment returns without a new permanent shift.
6. Check the ending/late-song alignment.
7. If the wired path feels materially late, try the existing offset control in 10 ms steps and record the approximate correction that makes the path usable. Do not convert that observation into a universal default automatically.

### Evidence to record in this spec during implementation

Append one completed row per environment under `Validation Results` before marking the PR ready for review. Each row must name the actual output path used and record concrete observations rather than only `pass`/`fail`.

Use this column shape:

```text
Platform | Output path/device | Chart | Offset tried | Start alignment | Late-song drift | Pause/resume | Chip/manual response | Decision
```

### Acceptance interpretation

MonoGame is accepted for HPA-12 when required wired environments show:

- no obvious accumulating BGM/chart drift during normal playback;
- no repeatable permanent shift after pause/resume;
- no playback failure/instability;
- manual play is usable, with the existing user offset available for fixed device latency.

The test does not need to prove zero physical latency. It only needs to establish that the current architecture is usable and that no blocking defect justifies an audio-backend project.

If a required environment fails these criteria, keep this PR focused on the cleanup changes, document the failure precisely, and create a separate follow-up before proposing backend replacement.

## Validation Results

This section is intentionally populated during implementation from real hardware; the planning PR does not fabricate measurements.

Required evidence before ready-for-review:

- [ ] Windows wired/default output observation recorded.
- [ ] macOS wired/default output observation recorded.
- [ ] USB interface observation recorded when readily available, or the PR states that no interface was available for this run.

## Production changes

Expected production changes are limited to:

```text
DTXMania.Game/Lib/Config/ConfigData.cs
  - remove BufferSizeMs
  - default AudioLatencyOffsetMs to 0
  - replace stale backend/buffer claims with behavior-only documentation

DTXMania.Game/Lib/Stage/PerformanceStage.cs
  - remove comments that describe 200 ms as the default/known physical latency
  - no timing behavior change
```

`ConfigManager`, `ConfigStage`, `ManagedSound`, `SongTimer`, and `PlaybackClock` should remain behaviorally unchanged unless implementation discovers a concrete contradiction to this verified design.

## Test changes

Expected focused test changes:

```text
DTXMania.Test/BaseGameTests.cs
  - remove the dead BufferSizeMs initializer

DTXMania.Test/Config/ConfigManagerTests.cs
  - update fresh/default persisted AudioLatencyOffsetMs expectation from 200 to 0
  - preserve explicit-value parsing/persistence and negative clamp coverage
```

Existing `ConfigStageLogicTests` already pin that the manual offset can be increased, cannot go below `0`, and is capped at `500`. Existing `PerformanceStageDeterministicTests` use explicit non-zero offsets to pin compensation behavior across Play Speed profiles. Those tests should remain unchanged unless a stale comment/assertion explicitly assumes the old default.

No new integration harness is required.

## Verification

Implementation should run targeted cross-platform tests first:

```bash
# macOS
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~BaseGameTests"

# Windows
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~BaseGameTests"
```

Then run the repository's normal full test/build gates for the available host platform before ready-for-review.

Repository search after cleanup must return no `BufferSizeMs` references.

## Follow-up rule

HPA-12 must not speculate a replacement backend into existence.

A later audio-backend ticket is justified only when the validation notes contain a repeatable concrete defect and enough environment detail to state what capability MonoGame lacks. That follow-up should evaluate the smallest remedy for that defect rather than resurrecting the old NX backend matrix wholesale.
