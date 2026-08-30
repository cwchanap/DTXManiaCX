# HPA-12 MonoGame Audio Validation and Stale Setting Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans`. Keep planning, implementation, validation evidence, and review fixes on this same PR.

**Goal:** Remove dead audio-buffer configuration/diagnostics, make fresh/default latency compensation neutral (`0 ms`), preserve the existing judgement-only offset seam, and validate the unchanged MonoGame transport on Windows and macOS hardware.

**Architecture:** No new audio abstraction. MonoGame `SoundEffectInstance` remains transport; `SongTimer`/`PlaybackClock` remains logical chart time; `PerformanceStage.GetPlayerJudgementTimeMs(...)` remains manual latency compensation. If a hardware run shows drift after a long hitch, investigate the fixed-step clock before blaming the audio backend.

**Tech Stack:** .NET 8, C#, MonoGame 3.8.x, xUnit, SQLite-backed `ConfigManager`.

**Spec:** `docs/superpowers/specs/2026-08-29-hpa-12-monogame-audio-validation-design.md`

## Global constraints

- One PR for HPA-12.
- No DirectSound/ASIO/WASAPI/BASS/NAudio or `IAudioEngine`.
- No device picker, buffer knob, auto-calibration, per-device profile, or benchmark harness.
- Remove `BufferSizeMs`; do not rename/replace it and do not replace its crash field with `AudioLatencyOffsetMs`.
- Fresh/default `AudioLatencyOffsetMs = 0`; existing persisted values are not migrated.
- Keep the current `0..500 ms` / `10 ms` manual control.
- Preserve `JudgementManager.HitDetectionWindowMs = 200.0`.
- Production clock/audio architecture remains unchanged in this ticket.
- Windows wired/default and macOS wired/default observations are required before the PR leaves Draft. USB is optional when available.

---

## Task 1: Cleanup dead buffer configuration and neutralize the default

**Production files:**

```text
DTXMania.Game/Lib/Config/ConfigData.cs
DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashContextPublisher.cs
DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashLogFieldPolicy.cs
DTXMania.Game/Lib/Stage/PerformanceStage.cs
```

**Test files:**

```text
DTXMania.Test/BaseGameTests.cs
DTXMania.Test/Config/ConfigDataTests.cs
DTXMania.Test/Config/ConfigDataApiSettingsTests.cs
DTXMania.Test/Config/ConfigManagerTests.cs
DTXMania.Test/CrashReporting/CrashContextPublisherTests.cs
DTXMania.Test/CrashReporting/CrashLogFieldPolicyTests.cs
DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs
```

### 1. Update the existing default contract

In `ConfigDataTests.ConfigData_DefaultValues_ShouldBeValid`:

```csharp
// remove
Assert.Equal(100, config.BufferSizeMs);

// change
Assert.Equal(0, config.AudioLatencyOffsetMs);
```

This is the existing direct default pin; do not add a duplicate default assertion elsewhere.

### 2. Delete the complete `BufferSizeMs` surface

Production:

- delete `ConfigData.BufferSizeMs`;
- delete `CrashContextPublisher`'s `BufferSizeMs` field;
- delete `CrashLogFieldPolicy.MaximumBufferMilliseconds`;
- delete the `BufferSizeMs` configuration normalization case.

Tests:

- remove `BufferSizeMs` object initializers/assertions from `BaseGameTests` and `CrashContextPublisherTests`;
- delete the entire `Sound Settings – Buffer Size` region in `ConfigDataApiSettingsTests`;
- delete the two `CrashLogFieldPolicyTests` buffer-size cases.

Do not add replacement telemetry.

### 3. Set fresh/default latency to zero and correct comments

In `ConfigData`:

```csharp
public int AudioLatencyOffsetMs { get; set; } = 0;
```

Replace comments that claim CX controls a 100 ms backend buffer or has a known universal 200 ms output delay. Keep wording behavior-only: this is optional manual judgement compensation, and `0` means no compensation.

In `PerformanceStage`, rewrite only the stale comment that says the default 200 ms equals the hit window. Preserve the real ±200 ms hit-detection rule.

In `PerformanceStageDeterministicTests`, fix the stale telemetry comment that says compensation occurs in `SongTimer.GetCurrentMs(...)`.

### 4. Pin zero-offset runtime semantics

Add one focused deterministic test near the existing `GetPlayerJudgementTimeMs` theory:

```csharp
[Fact]
public void GetPlayerJudgementTimeMs_WhenDefaultOffsetIsZero_ShouldReturnRawTime()
{
    var game = ReflectionHelpers.CreateGame();
    ReflectionHelpers.SetProperty(
        game,
        nameof(BaseGame.ConfigManager),
        CreateConfigManager(new ConfigData()));
    var stage = CreateStage(game);

    Assert.Equal(
        1000.0,
        ReflectionHelpers.InvokePrivateMethod<double>(
            stage,
            "GetPlayerJudgementTimeMs",
            1000.0));
}
```

This pins the runtime consequence of the new default. Existing chip tests already cover raw/unadjusted lookup and explicit non-zero compensation; do not duplicate their full fixture.

### 5. Keep negative-clamp coverage non-vacuous

Change the current clamp test so it cannot pass if the setter becomes a no-op:

```csharp
[Fact]
public void SetAudioLatency_Negative_ClampsToZero()
{
    var cm = new ConfigManager();
    cm.SetAudioLatency(120);

    cm.SetAudioLatency(-50);

    Assert.Equal(0, cm.Config.AudioLatencyOffsetMs);
}
```

Keep the existing positive mutation and persistence tests.

### 6. Update the fresh SQLite snapshot expectation

Change only the default snapshot expectation:

```csharp
Assert.Equal("0", rows["AudioLatencyOffsetMs"]);
```

Explicit stored values such as `350` remain unchanged.

### 7. Verify Task 1

Repository search:

```bash
git grep -n "BufferSizeMs" -- ':!docs/superpowers/**'
```

Expected: no output.

Focused macOS test gate:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~ConfigDataTests|FullyQualifiedName~ConfigDataApiSettingsTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~CrashContextPublisherTests|FullyQualifiedName~CrashLogFieldPolicyTests|FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~BaseGameTests"
```

Use the equivalent Windows test project on Windows.

Then run the normal full test/build gate for the available host.

**Task 1 gate:** code cleanup is independently correct and tested, but keep PR #159 Draft because HPA-12 is not complete until Task 2 hardware evidence exists.

---

## Task 2: Validate the unchanged MonoGame transport on real hardware

**Modify only:**

```text
docs/superpowers/specs/2026-08-29-hpa-12-monogame-audio-validation-design.md
```

No production instrumentation or playback change is expected in this task.

### 1. Build the platform under test

macOS:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --no-restore
```

Windows:

```powershell
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj --no-restore
```

### 2. Use the fixed validation profile

For every run:

```text
Play Speed           = 100%
Pitch                = 0 st
Audio Latency Offset = 0 ms initially
```

Use the same known-good long chart where practical.

### 3. Run the required wired smoke

Required environments:

```text
Windows wired/default output
macOS wired/default output
```

USB interface: repeat if readily available; otherwise record unavailability.

For each run, record:

- fixed opening audio-vs-chart offset;
- physical input -> audible chip response;
- manual judgement usability;
- any offset values tried in 10 ms steps;
- whether late-song offset changed relative to opening;
- pause/resume permanent shift, if any;
- playback failure/instability;
- obvious hitch/slow-frame episodes.

Avoid alt-tab/debugger interruption during the drift observation.

### 4. Reproduce drift before classifying it

Because CX uses MonoGame's default fixed-step loop and `PlaybackClock` is based on `GameTime.TotalGameTime`, a >500 ms real hitch can leave logical chart time behind audio time.

If progressive drift is noticed:

1. repeat from a clean start;
2. note whether an obvious long hitch occurred;
3. if the drift depends on that hitch, classify the follow-up as **clock/game-loop investigation**, not audio-backend replacement;
4. only stable/repeatable transport problems should become an audio-path follow-up.

Do not add temporary `SongTimer` duration/state logging in this ticket.

### 5. Record evidence

Use the design-spec table:

```text
Platform | Output/device | Chart | Offset tried | Fixed opening offset | Late-song change | Pause/resume | Chip/manual response | Hitch notes | Decision
```

Decision:

```text
keep MonoGame
follow-up required
```

A failure row must state the repeatable symptom. It does not prescribe a backend.

### 6. Final verification

Before moving PR #159 out of Draft:

```text
[ ] Windows wired/default row recorded
[ ] macOS wired/default row recorded
[ ] USB row recorded or unavailability stated
[ ] no BufferSizeMs live references
[ ] fresh/default AudioLatencyOffsetMs == 0
[ ] zero-offset runtime semantics test passes
[ ] explicit non-zero latency tests still pass
[ ] full macOS test/build gate passes
[ ] full Windows test/build gate passes
[ ] no audio/clock architecture expansion in HPA-12
```

If a required row is `follow-up required`, create one separate Linear task describing the observed defect and keep HPA-12's implementation scope unchanged.