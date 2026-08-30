# HPA-12 MonoGame Audio Validation and Stale Setting Cleanup Design

## Summary

HPA-12 is a validation-and-cleanup ticket, not an audio-backend project.

CX already has one cross-platform playback path built on MonoGame `SoundEffect` / `SoundEffectInstance`. Windows uses `MonoGame.Framework.WindowsDX`; macOS uses `MonoGame.Framework.DesktopGL`. `SongTimer` starts, pauses, resumes, and stops the audio instance while `PlaybackClock` owns logical chart time. `PerformanceStage.GetPlayerJudgementTimeMs(...)` applies the user-configured `AudioLatencyOffsetMs` only to manual judgement/chip lookup/miss timing.

The ticket should therefore do three things only:

1. remove the dead `BufferSizeMs` config/diagnostic surface;
2. use a neutral fresh/default `AudioLatencyOffsetMs = 0` while preserving the existing manual adjustment;
3. validate the existing MonoGame transport on real Windows/macOS hardware before closing the ticket.

No replacement audio engine is introduced unless later evidence justifies a separately scoped task.

## Goals

- Keep the existing MonoGame playback path unless a required hardware run exposes a repeatable product problem.
- Remove `BufferSizeMs` from config, crash diagnostics/policy, and tests; do not rename or replace it.
- Change fresh/default `AudioLatencyOffsetMs` from `200` to `0` without migrating existing persisted values.
- Preserve the existing `0..500 ms`, `10 ms` step latency control.
- Keep compensation in `PerformanceStage.GetPlayerJudgementTimeMs(...)`.
- Keep all planning, cleanup, and review fixes in one HPA-12 PR.
- Defer the real-device Windows/macOS playback observations to a later follow-up run by owner decision (see `Real-device validation gate` below); they are no longer a Draft gate for PR #159.

## Non-goals

Do **not** add or prototype:

- DirectSound, ASIO, WASAPI, BASS, NAudio, or another playback dependency;
- `IAudioEngine`, backend/device registries, output-device selection, or plugin APIs;
- audio-buffer configuration;
- automatic calibration or per-device latency profiles;
- a latency benchmark/instrumentation harness;
- negative latency offsets or separate input/output offsets;
- a new timer/clock source in this ticket;
- changes to Play Speed, pitch processing, chart parsing, judgement windows, or MIDI/input timing.

A later task may investigate the clock or playback backend if HPA-12 produces evidence for it. Do not expand this PR to implement that follow-up.

## Verified current architecture

### Playback is MonoGame-owned

`ManagedSound` produces MonoGame `SoundEffect` objects and playback uses `SoundEffectInstance`. FFmpeg/NVorbis are decode/conversion dependencies, not alternate runtime playback backends.

Platform selection already happens at the project level:

```text
Windows -> MonoGame.Framework.WindowsDX 3.8.*
macOS   -> MonoGame.Framework.DesktopGL 3.8.*
```

No HPA-12 production code should bypass those platform choices.

### Logical chart time is not an audio cursor

`SongTimer` owns an optional `SoundEffectInstance` plus a `PlaybackClock`.

- `Play`, `Pause`, `Resume`, and `Stop` coordinate the transport and logical clock.
- `GetCurrentMs(GameTime)` returns `PlaybackClock` time, not audio-device playback position.
- `SetPosition(...)` changes logical chart time only; `SoundEffectInstance` has no seek path here.

This separation is existing behavior and remains unchanged.

### Latency compensation stays judgement-only

The runtime has two timing views:

```text
raw logical chart time
  -> autoplay
  -> BGM/video events
  -> note visuals
  -> progress/completion

latency-compensated manual time
  -> player judgement
  -> player chip lookup
  -> pending-hit/miss timing
```

`AudioLatencyOffsetMs` is expressed in real milliseconds. `GetPlayerJudgementTimeMs(...)` converts it for the frozen Play Speed profile before subtracting it.

The setting does **not** move chart visuals and does **not** reduce physical input-to-speaker chip latency. Validation must keep those observations separate.

### `BufferSizeMs` is dead playback config but still live diagnostic data

Current `main` references `BufferSizeMs` in:

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

It is absent from `ConfigManager` parsing/snapshot persistence and absent from playback. Remove the entire stale surface together. Delete `MaximumBufferMilliseconds` with the policy case and delete the `ConfigDataApiSettingsTests` buffer-size region.

Do not replace the removed crash field with `AudioLatencyOffsetMs`; useful crash telemetry can be considered in a crash-reporting task when a concrete diagnostic need exists.

### Existing tests already pin the defaults

`ConfigDataTests.ConfigData_DefaultValues_ShouldBeValid` currently pins both `BufferSizeMs == 100` and `AudioLatencyOffsetMs == 200`. Use that existing test for the default change rather than adding another default assertion elsewhere.

`ConfigManagerTests` separately pins the first-created SQLite snapshot and explicit latency parsing/persistence. The E2E fixture already writes `AudioLatencyOffsetMs=0`.

## Fixed-timestep clock caveat

This is important for interpreting any observed drift.

CX does not assign `Game.IsFixedTimeStep`, so MonoGame's default fixed-step loop applies. `PlaybackClock` derives elapsed logical time from `GameTime.TotalGameTime`. MonoGame's fixed-step loop caps accumulated elapsed time at `MaxElapsedTime` (default `500 ms`) before advancing `TotalGameTime`.

Therefore a real hitch longer than that cap can permanently leave the logical chart clock behind wall/audio time even when the audio transport itself is healthy. A visible late-song offset after a long GC pause, suspension, debugger stop, or similar stall must not automatically become an "audio backend" finding.

HPA-12 does **not** change the clock. Instead:

- validation runs should avoid intentional alt-tab/debugger pauses outside the explicit pause/resume test;
- if accumulating drift is observed, repeat the run from a clean start;
- note any obvious long hitch or slow-frame episode;
- drift that correlates with hitches should first become a clock/game-loop follow-up, not a backend replacement task;
- only stable, repeatable transport problems should justify investigating the playback backend.

Do not add temporary `SongTimer` end-of-song instrumentation in HPA-12. `SongTimer` does not own `ISound.Duration`, and observing a real `SoundEffectInstance.State` is itself a real-audio transport check rather than a headless measurement. A benchmark seam would add production/test plumbing for a one-time acceptance exercise that the ticket explicitly does not require.

## Design decisions

### 1. Keep MonoGame unless evidence says otherwise

A follow-up is justified when a required wired run repeatedly shows a material problem such as:

- progressive BGM/chart drift on a clean run;
- a permanent pause/resume alignment shift;
- playback failure or instability;
- physical player-triggered chip latency that makes rhythm play impractical;
- a fixed audio/visual/judgement delay that remains impractical within the current adjustment model.

Evidence determines the **problem**, not the solution. Do not prescribe ASIO/WASAPI/etc. unless the later investigation demonstrates that need.

### 2. Delete `BufferSizeMs`; do not replace it

Remove the property, crash publication, field-policy normalization/limit, and tests. There is no runtime behavior or persisted value to preserve.

### 3. Use `0 ms` as the fresh/default compensation

Fresh/default config becomes:

```csharp
public int AudioLatencyOffsetMs { get; set; } = 0;
```

Rationale: output latency is hardware/driver/path dependent and CX has no measurement supporting one universal non-zero default. Existing persisted values continue to load unchanged; `ResetToDefaults()` receives the new `0` through `new ConfigData()`.

### 4. Preserve manual UI/persistence

Keep:

```text
range: 0..500 ms
step:  10 ms
```

Keep `ConfigManager.SetAudioLatency(...)`, parsing, snapshot persistence, and Config-stage behavior. The negative clamp test must first seed a non-zero value before applying a negative value so the test cannot pass when the setter is accidentally a no-op.

### 5. Pin zero-offset runtime semantics

Besides changing the default-value assertion, add one deterministic `PerformanceStageDeterministicTests` case using a real default `ConfigData` through the test config manager:

```text
AudioLatencyOffsetMs == 0
raw logical time = 1000 ms
GetPlayerJudgementTimeMs(...) = 1000 ms
```

The existing manual chip-lookup tests already cover unadjusted/raw lookup and explicit non-zero compensation. Do not duplicate the whole chip fixture solely for the default change.

### 6. Correct only stale latency comments

Fix comments that incorrectly claim:

- `BufferSizeMs` configures MonoGame latency;
- `200 ms` is a known/default physical latency;
- compensation occurs inside `SongTimer.GetCurrentMs(...)`.

Do **not** alter `JudgementManager.HitDetectionWindowMs = 200.0` or its ±200 ms hit-window documentation.

## Real-device validation gate

This remains a practical acceptance check, not a benchmark framework. It is deliberately partly human because physical audible offset and chip response are the product experience being validated.

> **Status (owner decision, 2026-08-30):** The real-device validation rows below are **deferred** and are no longer a Draft gate for PR #159. The PR may be reviewed and merged without the Windows/macOS/USB wired/default observations. The procedure, environments, and result table below are retained as the acceptance checklist for the deferred follow-up run; the `Validation Results` checkboxes stay unchecked until that run happens. The cleanup and default-neutralization work in this PR does not depend on the deferred observations.

### Required environments

For the deferred follow-up run, cover:

1. Windows, normal/default **wired** output;
2. macOS, normal/default **wired** output;
3. one USB audio interface if readily available; otherwise record that it was unavailable.

Windows and macOS are the required supported-platform evidence for the follow-up. Do not replace the Windows row with an "unavailable" escape hatch; that would weaken the follow-up's acceptance criteria.

Bluetooth is not a gate.

### Test profile

Use the same known-good long chart where practical and fix:

```text
Play Speed             = 100%
Pitch                   = 0 st
Audio Latency Offset    = 0 ms initially
```

The chart should have clear chip attacks, recognizable BGM/visual alignment, enough duration to expose progressive drift, and a pause/resume opportunity.

### Procedure

For each environment:

1. Start a clean run at the profile above. Avoid alt-tab/debugger interruption during the drift observation.
2. Record whether a noticeable **fixed** audio-vs-chart offset exists near the opening. A small constant output delay is not automatically a failure.
3. Hit clear notes and separately assess:
   - physical input -> audible chip response;
   - manual judgement usability.
4. If judgement is systematically early/late, try the existing offset in `10 ms` steps and record the values tried. Remember: this changes judgement only, not visuals or physical speaker latency.
5. Continue through a substantial/late portion of the chart and compare the late offset with the opening offset.
6. Pause for several seconds, resume, and check whether a **new permanent** shift appears.
7. Record any obvious hitch/slow-frame episode. If progressive drift appeared, repeat once from a clean run before classifying it.

### Result table

Use:

```text
Platform | Output/device | Chart | Offset tried | Fixed opening offset | Late-song change | Pause/resume | Chip/manual response | Hitch notes | Decision
```

Decisions are:

```text
keep MonoGame
follow-up required
```

A `follow-up required` row must state the observed defect and repeatability. If drift followed a long hitch, classify the follow-up as clock/game-loop investigation first.

## Validation Results

> Deferred by owner decision (2026-08-30). These rows are not a Draft gate for PR #159 and will be filled in during the deferred follow-up run.

- [ ] Windows wired/default output observation recorded.
- [ ] macOS wired/default output observation recorded.
- [ ] USB interface observation recorded, or unavailability explicitly recorded.

## Expected implementation surface

Production:

```text
DTXMania.Game/Lib/Config/ConfigData.cs
DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashContextPublisher.cs
DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashLogFieldPolicy.cs
DTXMania.Game/Lib/Stage/PerformanceStage.cs   # comments only besides config-read behavior remaining unchanged
```

Tests:

```text
DTXMania.Test/BaseGameTests.cs
DTXMania.Test/Config/ConfigDataTests.cs
DTXMania.Test/Config/ConfigDataApiSettingsTests.cs
DTXMania.Test/Config/ConfigManagerTests.cs
DTXMania.Test/CrashReporting/CrashContextPublisherTests.cs
DTXMania.Test/CrashReporting/CrashLogFieldPolicyTests.cs
DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs
```

`ConfigManager`, `ConfigStage`, `ManagedSound`, `AudioLoader`, `SongTimer`, `PlaybackClock`, and `JudgementManager` remain behaviorally unchanged in HPA-12.

## Verification

After implementation:

```bash
git grep -n "BufferSizeMs" -- ':!docs/superpowers/**'
# expected: no output
```

Run focused config/crash/timing tests on the available host, then the normal full test/build gate for that host. The deferred real-device validation run (see `Real-device validation gate`) is no longer a merge gate for PR #159; it is performed in a follow-up and records its own platform results.

## Follow-up rule

HPA-12 never converts an observation directly into "replace MonoGame."

- Hitch-correlated drift -> investigate the fixed-step clock/game loop first.
- Stable physical chip latency -> investigate the smallest playback-latency remedy.
- Fixed visual/audio mismatch -> investigate timing/calibration ownership before backend replacement.
- Playback failure/instability -> investigate the platform playback path.

Only create a follow-up when the real-device evidence is concrete and repeatable.