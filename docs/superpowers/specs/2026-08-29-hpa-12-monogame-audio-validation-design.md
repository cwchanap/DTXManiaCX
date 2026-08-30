# HPA-12 MonoGame Audio Validation and Stale Setting Cleanup Design

## Summary

HPA-12 is a validation-and-cleanup ticket, not an audio-backend project.

CX already has one cross-platform playback path built on MonoGame `SoundEffect` / `SoundEffectInstance`. Windows uses the WindowsDX MonoGame target and macOS uses DesktopGL. `SongTimer` starts, pauses, resumes, and stops the `SoundEffectInstance` while `PlaybackClock` owns the logical chart clock. `PerformanceStage` applies `AudioLatencyOffsetMs` only to manual player judgement/chip lookup/miss timing; autoplay, BGM scheduling, chart visuals, progress, video, and completion stay on the raw logical chart clock.

The current configuration still contains one stale NX-era-looking property:

```csharp
public int BufferSizeMs { get; set; } = 100;
```

`BufferSizeMs` is not consumed by playback and is not persisted by `ConfigManager`, but it is still copied into crash-report configuration context and accepted by `CrashLogFieldPolicy`. Several tests also pin those stale crash/config behaviors. Removing the field therefore requires deleting the whole dead diagnostic surface in the same change; it does **not** require a replacement setting or persistence migration.

`AudioLatencyOffsetMs` is different: it is persisted, exposed in the config UI, and used by `PerformanceStage`. Keep it, but remove the unsupported assumption that a hard-coded `200 ms` compensation is a defensible cross-platform default. Fresh/default configuration should use `0 ms`; players can opt into compensation when their actual output path needs it.

## Goals

1. Validate the existing MonoGame playback path on representative real devices before declaring it acceptable.
2. Keep the current MonoGame playback implementation unless validation exposes a concrete blocking defect.
3. Remove `BufferSizeMs` completely from live config, crash diagnostics, field policy, and tests because CX cannot configure or observe an audio buffer through its current playback path.
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

If real-device validation finds a concrete problem that cannot be solved by the existing judgement offset or current MonoGame transport, record the evidence and create a separate follow-up. Do not widen HPA-12 to solve it.

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

On `Play`, `Pause`, `Resume`, and `Stop`, it updates both the audio instance and the logical clock. `GetCurrentMs(...)` returns the logical clock position; it does not query an audio-device playback cursor. `SetPosition(...)` changes logical chart time only because `SoundEffectInstance` does not expose seeking.

This means HPA-12's validation question is whether the existing transport stays usable and stable enough for CX. It is **not** an invitation to derive a second clock from the audio backend.

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

Important limitation for validation: the setting does **not** delay chart visuals or reduce physical chip-sound output latency. It only corrects the manual judgement timeline. Therefore a small fixed output delay at `0 ms` is not automatically a MonoGame transport failure, but an audio/visual or chip-response delay that remains materially unplayable is still valid evidence for a follow-up.

### `BufferSizeMs` is dead playback configuration but still live diagnostic data

Current `main` references `BufferSizeMs` in the following live surfaces:

```text
Production
  DTXMania.Game/Lib/Config/ConfigData.cs
  DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashContextPublisher.cs
  DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashLogFieldPolicy.cs

Tests
  DTXMania.Test/BaseGameTests.cs
  DTXMania.Test/Config/ConfigDataTests.cs
  DTXMania.Test/Config/ConfigDataApiSettingsTests.cs
  DTXMania.Test/CrashReporting/CrashContextPublisherTests.cs
  DTXMania.Test/CrashReporting/CrashLogFieldPolicyTests.cs
```

It is absent from `ConfigManager` parsing/snapshot persistence and absent from the playback path. The Mac test project explicitly compiles `**/*.cs` apart from a narrow exclusion list, so the config/crash tests above are part of that suite too.

Removal therefore means deleting the diagnostic field/policy cases and their tests together with the `ConfigData` property. Do not replace the removed crash field with `AudioLatencyOffsetMs`; HPA-12 is deleting unsupported buffer telemetry, not expanding crash diagnostics.

### Existing default pins already cover `ConfigData`

`ConfigDataTests.ConfigData_DefaultValues_ShouldBeValid` currently asserts both:

```text
BufferSizeMs == 100
AudioLatencyOffsetMs == 200
```

That existing test is the direct red/green pin for the default change. Do not add a duplicate default assertion to `ConfigManager_Constructor_ShouldInitializeWithDefaultConfig` just for HPA-12.

`ConfigManagerTests` separately pins the first-created SQLite snapshot and should change its persisted default expectation from `200` to `0`.

The E2E fixture already writes `AudioLatencyOffsetMs=0`, so this change does not require an E2E fixture update.

## Design decisions

### 1. Keep the MonoGame playback path

The default outcome is no playback architecture change.

Real-device validation is a gate that can falsify this decision. A follow-up is justified only if validation produces a repeatable problem such as:

- audible/visible drift that grows during a normal song;
- pause/resume producing a repeatable permanent alignment shift;
- player-triggered chip response latency severe enough to make rhythm input unusable;
- a fixed audio/visual delay large enough that manual play remains unusable even after the existing judgement offset is tuned;
- platform-specific playback failure or instability.

A normal small fixed device/output delay is not by itself evidence that MonoGame must be replaced. The validation should distinguish fixed output latency from accumulating transport drift.

Subjective preference for a different backend or theoretical lower latency is not sufficient evidence for HPA-12 to replace MonoGame.

### 2. Delete `BufferSizeMs`; do not replace it

Remove the property from `ConfigData`, then remove the stale crash-context publication, `CrashLogFieldPolicy` normalization case/maximum constant, and every test that specifically exists to pin the removed field.

In particular, delete the whole `Sound Settings – Buffer Size` region from `ConfigDataApiSettingsTests`; getter/setter tests for a deleted property provide no value.

Do not add:

- `AudioBufferSizeMs` under another name;
- a backend-specific buffer option;
- `AudioLatencyOffsetMs` as replacement crash telemetry;
- an ignored compatibility row in SQLite;
- a migration for a key that current `ConfigManager` never persisted.

There is no runtime playback behavior to preserve.

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

`ConfigManager.SetAudioLatency(...)`, SQLite parsing/snapshot persistence, and the current config-stage interaction remain unchanged except for tests that explicitly assert the fresh/default value.

Do not add negative values or split input/output offsets in this ticket. Current runtime semantics only model delayed audible output for manual judgement, and HPA-12 is not redesigning calibration.

### 5. Correct only latency-default/judgement comments

Update comments that currently present `200 ms`, `BufferSizeMs=100`, or a presumed OpenAL buffering model as factual CX behavior.

Also fix the stale `PerformanceStageDeterministicTests` comment that says compensation happens inside `SongTimer.GetCurrentMs(...)`; compensation is applied later by `PerformanceStage.GetPlayerJudgementTimeMs(...)`.

The replacement documentation should say only:

- `AudioLatencyOffsetMs` is optional manual output-latency compensation;
- `0` means no compensation;
- it affects manual player judgement/chip lookup/miss timing only;
- autoplay, BGM/video scheduling, visuals, progress, and stage completion use the raw logical chart clock;
- Play Speed conversion remains handled by the existing `PerformanceStage` helper.

Do **not** rewrite unrelated `200 ms` comments/constants. `JudgementManager.HitDetectionWindowMs = 200` and comments describing the ±200 ms hit-detection window are independent timing rules and must remain unchanged.

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

For consistency, validate at:

```text
Play Speed = 100%
Pitch      = 0 st
```

HPA-12 is evaluating the normal playback path; Play Speed/pitch processing should not add noise to the result.

### Procedure

For each environment:

1. Set `Play Speed = 100%`, `Pitch = 0 st`, and `Audio Latency Offset = 0 ms`.
2. Play the opening and record whether there is a noticeable **fixed** audio-vs-chart offset. A small fixed output delay is expected on real hardware and is not an automatic failure.
3. Hit several clear notes and assess both:
   - physical input -> audible chip-response latency;
   - whether manual judgement is usable at the current offset.
4. If manual judgement feels systematically early/late, try the existing `Audio Latency Offset` control in 10 ms steps and record the values tried. This changes judgement timing only; it does not shift visuals or eliminate physical chip-output latency.
5. Continue far enough into the song to detect **accumulating** BGM/chart drift rather than confusing it with the fixed startup/output offset.
6. Pause for several seconds, resume, and verify there is no new permanent alignment shift relative to the pre-pause state.
7. Check late-song/ending alignment and playback stability.

### Evidence to record in this spec during implementation

Append one completed row per environment under `Validation Results` before marking the PR ready for review. Each row must name the actual output path used and record concrete observations rather than only `pass`/`fail`.

Use this column shape:

```text
Platform | Output path/device | Chart | Offset tried | Fixed start/output offset | Late-song drift | Pause/resume | Chip/manual response | Decision
```

### Acceptance interpretation

MonoGame is accepted for HPA-12 when required wired environments show:

- no obvious accumulating BGM/chart drift during normal playback;
- no repeatable permanent shift after pause/resume;
- no playback failure/instability;
- player-triggered chip response remains usable for rhythm play;
- manual judgement is usable with a fixed `0..500 ms` correction when needed;
- any remaining fixed audio/visual offset is small enough that gameplay is still practical.

A fixed offset at `0 ms` does **not** by itself fail MonoGame. The existing judgement control is specifically available for fixed output-latency correction, although it does not move chart visuals or reduce physical chip-output delay.

A required wired row needs `follow-up required` when the problem is repeatable and materially affects play, for example:

- accumulating drift;
- a permanent pause/resume shift;
- playback failure/instability;
- unacceptable physical chip-response latency;
- or a fixed audio/visual/judgement delay that remains unplayable within the existing adjustment range.

Such evidence does not automatically imply “replace MonoGame.” The follow-up should identify the smallest missing capability first (for example visual timing calibration vs. transport/backend latency) before prescribing an audio engine.

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

DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashContextPublisher.cs
  - stop publishing BufferSizeMs

DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashLogFieldPolicy.cs
  - remove MaximumBufferMilliseconds
  - remove BufferSizeMs normalization/allowlist behavior

DTXMania.Game/Lib/Stage/PerformanceStage.cs
  - remove comments that describe 200 ms as the default/known physical latency
  - preserve unrelated 200 ms hit-window documentation
  - no timing behavior change
```

`ConfigManager`, `ConfigStage`, `ManagedSound`, `SongTimer`, `PlaybackClock`, and `JudgementManager` should remain behaviorally unchanged unless implementation discovers a concrete contradiction to this verified design.

## Test changes

Expected focused test changes:

```text
DTXMania.Test/BaseGameTests.cs
  - remove the dead BufferSizeMs initializer

DTXMania.Test/Config/ConfigDataTests.cs
  - remove BufferSizeMs default assertion
  - change AudioLatencyOffsetMs default assertion from 200 to 0

DTXMania.Test/Config/ConfigDataApiSettingsTests.cs
  - delete the entire Sound Settings – Buffer Size region

DTXMania.Test/Config/ConfigManagerTests.cs
  - update fresh/default persisted AudioLatencyOffsetMs expectation from 200 to 0
  - preserve explicit-value parsing/persistence and negative clamp coverage

DTXMania.Test/CrashReporting/CrashContextPublisherTests.cs
  - remove BufferSizeMs initializer/assertion

DTXMania.Test/CrashReporting/CrashLogFieldPolicyTests.cs
  - remove BufferSizeMs normalization/range tests

DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs
  - correct the stale comment that assigns compensation to SongTimer.GetCurrentMs
```

Existing `ConfigStageLogicTests` already pin that the manual offset can be increased, cannot go below `0`, and is capped at `500`. A test fixture that explicitly starts from `200` remains valid because it is testing increment behavior, not the product default.

Existing `PerformanceStageDeterministicTests` use explicit non-zero offsets to pin compensation behavior across Play Speed profiles. Keep those behavior tests unchanged.

No new integration harness is required.

## Verification

Implementation should run targeted cross-platform tests first:

```bash
# macOS
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~ConfigData|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~CrashContextPublisherTests|FullyQualifiedName~CrashLogFieldPolicyTests|FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~BaseGameTests"

# Windows
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~ConfigData|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~CrashContextPublisherTests|FullyQualifiedName~CrashLogFieldPolicyTests|FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~BaseGameTests"
```

Then run the repository's normal full test/build gates for the available host platform before ready-for-review.

Repository search after cleanup must return no live-code/test references:

```bash
git grep -n "BufferSizeMs" -- DTXMania.Game DTXMania.Test DTXMania.E2E
```

Expected: no output. Historical planning notes under `docs/superpowers` may still mention the removed name as rationale and are intentionally excluded from this gate.

## Follow-up rule

HPA-12 must not speculate a replacement backend into existence.

A later ticket is justified only when the validation notes contain a repeatable concrete defect and enough environment detail to state what capability CX lacks. The follow-up should evaluate the smallest remedy for that defect rather than resurrecting the old NX backend matrix wholesale.
