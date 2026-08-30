# HPA-12 MonoGame Audio Validation and Stale Setting Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Keep planning, implementation, validation notes, and review fixes on this same PR.

**Goal:** Keep the existing MonoGame audio playback path, remove the dead `BufferSizeMs` config/diagnostic surface, make `AudioLatencyOffsetMs=0` the neutral fresh/default behavior, correct stale latency comments, and record real-device evidence before the PR becomes ready for review.

**Architecture:** Do not add an audio abstraction or backend. `ManagedSound` / MonoGame `SoundEffectInstance` remains the playback transport, `SongTimer` / `PlaybackClock` remains the raw logical chart clock, and `PerformanceStage.GetPlayerJudgementTimeMs(...)` remains the only output-latency compensation seam for manual play. Fixed device latency, accumulating transport drift, and physical chip-response latency are separate observations in acceptance.

**Tech Stack:** .NET 8, C#, MonoGame 3.8.x (`WindowsDX` on Windows, `DesktopGL` on macOS), xUnit, SQLite-backed `ConfigManager`.

**Spec:** `docs/superpowers/specs/2026-08-29-hpa-12-monogame-audio-validation-design.md`

## Global Constraints

- One PR for HPA-12 planning, implementation, validation evidence, and review fixes.
- Keep MonoGame playback unless real-device validation exposes a repeatable blocking defect.
- Do not add DirectSound, ASIO, WASAPI, BASS, NAudio, or another playback dependency.
- Do not add `IAudioEngine`, backend selection, device selection, buffer configuration, auto-calibration, per-device profiles, or a latency benchmark harness.
- Remove `BufferSizeMs` from config, crash diagnostics/policy, and tests; do not rename or replace it.
- Do not replace the removed crash field with `AudioLatencyOffsetMs`.
- Fresh/default `AudioLatencyOffsetMs` is exactly `0` ms.
- Keep the existing manual `Audio Latency Offset` range `0..500` ms with `10` ms steps.
- Keep latency compensation scoped to manual judgement/chip lookup/miss timing. Do not move it into `SongTimer`, `PlaybackClock`, `ManagedSound`, chart parsing, BGM/video scheduling, autoplay, visuals, progress, or completion.
- Do not add a migration for the old `200` default. Existing persisted values continue to load normally; fresh config and reset-to-defaults use `0`.
- Preserve the unrelated `JudgementManager.HitDetectionWindowMs = 200.0` timing rule and its documentation.
- Real-device evidence must be recorded from actual hardware. An agent must never fabricate or infer validation results.
- If validation shows a concrete product problem, document it and create a separate follow-up rather than widening HPA-12.

---

### Task 1: Remove dead buffer config/diagnostics and neutralize the latency default

**Files:**
- Modify: `DTXMania.Game/Lib/Config/ConfigData.cs`
- Modify: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashContextPublisher.cs`
- Modify: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashLogFieldPolicy.cs`
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Modify: `DTXMania.Test/BaseGameTests.cs`
- Modify: `DTXMania.Test/Config/ConfigDataTests.cs`
- Modify: `DTXMania.Test/Config/ConfigDataApiSettingsTests.cs`
- Modify: `DTXMania.Test/Config/ConfigManagerTests.cs`
- Modify: `DTXMania.Test/CrashReporting/CrashContextPublisherTests.cs`
- Modify: `DTXMania.Test/CrashReporting/CrashLogFieldPolicyTests.cs`
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs` (comment only)
- Inspect only: `DTXMania.Test/Config/ConfigStageLogicTests.cs`
- Inspect only: `DTXMania.Game/Lib/Stage/Performance/JudgementManager.cs`

**Interfaces:**
- Consumes: existing `ConfigData.AudioLatencyOffsetMs`, `ConfigManager.SetAudioLatency(int)`, `PerformanceStage.GetPlayerJudgementTimeMs(double)`, and crash-report configuration allowlist semantics.
- Produces: no live `BufferSizeMs` config/diagnostic reference; fresh/default `AudioLatencyOffsetMs == 0`; unchanged explicit latency-offset behavior/persistence; unchanged ±200 ms hit-detection window.

- [ ] **Step 1: Turn the existing direct default test red**

Do **not** add a duplicate default assertion to `ConfigManager_Constructor_ShouldInitializeWithDefaultConfig`.

In `DTXMania.Test/Config/ConfigDataTests.cs`, change only the latency default expectation first:

```csharp
Assert.Equal(0, config.AudioLatencyOffsetMs);
```

Leave the existing `BufferSizeMs` assertion in place for this red run; it will be deleted when the property is removed.

- [ ] **Step 2: Run the direct default test and confirm current behavior is red**

macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~ConfigDataTests.ConfigData_DefaultValues_ShouldBeValid"
```

Windows:

```powershell
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore `
  --filter "FullyQualifiedName~ConfigDataTests.ConfigData_DefaultValues_ShouldBeValid"
```

Expected on the pre-change production code: FAIL because `AudioLatencyOffsetMs` is still `200`.

- [ ] **Step 3: Remove the dead config property and set the neutral latency default**

In `DTXMania.Game/Lib/Config/ConfigData.cs`, remove:

```csharp
public int BufferSizeMs { get; set; } = 100;
```

Change:

```csharp
public int AudioLatencyOffsetMs { get; set; } = 200;
```

to:

```csharp
public int AudioLatencyOffsetMs { get; set; } = 0;
```

Replace the latency XML documentation with behavior-only wording equivalent to:

```csharp
/// <summary>
/// Optional manual audio-output latency compensation in real milliseconds.
/// A value of 0 applies no compensation. PerformanceStage subtracts the
/// speed-adjusted equivalent from manual player judgement timing so players
/// can compensate for a fixed audible-output delay on their device.
///
/// This does not configure the MonoGame audio backend or an output buffer.
/// Autoplay, BGM/video scheduling, note visuals, progress, and stage
/// completion continue to use the raw logical chart clock.
/// </summary>
public int AudioLatencyOffsetMs { get; set; } = 0;
```

Do not mention `BufferSizeMs`, a presumed OpenAL buffer, or a universal driver-latency range.

- [ ] **Step 4: Remove `BufferSizeMs` from crash publication and normalization**

In `CrashContextPublisher.PublishConfiguration(...)`, delete only the stale field publication:

```csharp
["BufferSizeMs"] = config.BufferSizeMs,
```

Do not replace it with an audio-latency field.

In `CrashLogFieldPolicy.cs`, delete:

```csharp
private const int MaximumBufferMilliseconds = 60_000;
```

and delete the `TryNormalizeConfigurationProperty(...)` case:

```csharp
case "BufferSizeMs" when value is int bufferSizeMs
                         && bufferSizeMs >= 0
                         && bufferSizeMs <= MaximumBufferMilliseconds:
    normalizedValue = bufferSizeMs;
    return true;
```

No other crash-report allowlist behavior changes in HPA-12.

- [ ] **Step 5: Remove every test that pins the deleted buffer field**

Update these tests in the same change so both Windows and Mac test projects compile.

`DTXMania.Test/BaseGameTests.cs`:

```csharp
// Delete only this initializer entry.
BufferSizeMs = 80,
```

`DTXMania.Test/Config/ConfigDataTests.cs`:

```csharp
// Delete this assertion.
Assert.Equal(100, config.BufferSizeMs);
```

Keep the Step 1 latency assertion at `0`.

`DTXMania.Test/Config/ConfigDataApiSettingsTests.cs`:

Delete the entire region:

```text
#region Sound Settings – Buffer Size
...
#endregion
```

Do not keep getter/setter/range tests for a property that no longer exists.

`DTXMania.Test/CrashReporting/CrashContextPublisherTests.cs`:

Delete:

```csharp
BufferSizeMs = 80,
```

and:

```csharp
Assert.Equal(80, configuration.Fields["BufferSizeMs"]);
```

Do not add `AudioLatencyOffsetMs` to this crash snapshot in its place.

`DTXMania.Test/CrashReporting/CrashLogFieldPolicyTests.cs`:

Delete both buffer-specific tests:

```text
TryNormalizeContextProperty_ConfigurationBufferSizeMs_ShouldRetainInBounds
TryNormalizeContextProperty_ConfigurationBufferSizeMsOutOfRange_ShouldReturnFalse
```

- [ ] **Step 6: Update the persisted fresh-default pin, not explicit-value fixtures**

In `DTXMania.Test/Config/ConfigManagerTests.cs`, change the first-created snapshot assertion:

```csharp
Assert.Equal("200", rows["AudioLatencyOffsetMs"]);
```

to:

```csharp
Assert.Equal("0", rows["AudioLatencyOffsetMs"]);
```

Keep explicit-value tests such as `AudioLatencyOffsetMs=350`, `SetAudioLatency(350)`, and negative clamping unchanged.

Inspect `ConfigStageLogicTests`: fixtures that explicitly start at `200` to test `+10`/navigation behavior remain valid and should not be rewritten merely because the product default changed.

- [ ] **Step 7: Correct only stale latency-default comments**

In `ConfigData.cs`, remove the stale `BufferSizeMs=100` / presumed `100-200ms` explanation as covered by Step 3.

In `PerformanceStage.UpdateGameplayManagers(...)`, replace:

```text
(default 200 ms equals the hit window, leaving zero reaction time)
```

with invariant wording such as:

```text
Pending hits and timeout misses share the latency-compensated logical time so
manual judgement does not lose part of its hit window when the user has
configured an output-latency correction.
```

In `PerformanceStageDeterministicTests.cs`, fix the telemetry comment that currently says `SongTimer.GetCurrentMs` subtracts `AudioLatencyOffsetMs`. Replace it with wording equivalent to:

```text
SongTimer returns the raw logical chart time. AudioLatencyOffsetMs is applied
later only to manual judgement, so telemetry CurrentSongTimeMs remains raw.
```

Do **not** modify these legitimate 200 ms timing rules/comments:

```csharp
JudgementManager.HitDetectionWindowMs = 200.0;
```

and the `FindNearestNoteForChip` comment explaining that the 200 ms full window includes Miss-range hits.

Do not change the implementation of:

```csharp
GetPlayerJudgementTimeMs(...)
UpdateGameplayManagers(...)
FindNearestNoteForChip(...)
JudgementManager.Update(...)
SongTimer.GetCurrentMs(...)
```

- [ ] **Step 8: Run the buffer/default cleanup searches**

Search live production/test surfaces only; planning docs intentionally retain historical rationale:

```bash
git grep -n "BufferSizeMs" -- DTXMania.Game DTXMania.Test DTXMania.E2E
```

Expected: no output.

Inspect only latency-specific `200` assumptions instead of globally replacing every `200 ms` occurrence:

```bash
git grep -n -E "AudioLatencyOffsetMs.*200|200.*AudioLatencyOffsetMs|default 200" -- \
  DTXMania.Game DTXMania.Test DTXMania.E2E
```

Expected:

- no comment/assertion claims `200` is the fresh product latency default;
- explicit `200` fixtures may remain when they intentionally test a non-zero configured value.

Confirm the unrelated hit-window constant still exists:

```bash
git grep -n "HitDetectionWindowMs = 200.0" -- DTXMania.Game
```

Expected: the `JudgementManager` constant remains present.

- [ ] **Step 9: Run the focused automated regression suites**

macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~ConfigData|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~CrashContextPublisherTests|FullyQualifiedName~CrashLogFieldPolicyTests|FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~BaseGameTests"
```

Windows:

```powershell
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore `
  --filter "FullyQualifiedName~ConfigData|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~CrashContextPublisherTests|FullyQualifiedName~CrashLogFieldPolicyTests|FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~BaseGameTests"
```

Expected: PASS on each available host project.

The `ConfigData` filter intentionally includes both `ConfigDataTests` and `ConfigDataApiSettingsTests`; the crash suites are included explicitly because deleting the property otherwise leaves compile/test failures outside the original draft's filter.

- [ ] **Step 10: Commit the complete cleanup as one reviewable unit**

```bash
git add \
  DTXMania.Game/Lib/Config/ConfigData.cs \
  DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashContextPublisher.cs \
  DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashLogFieldPolicy.cs \
  DTXMania.Game/Lib/Stage/PerformanceStage.cs \
  DTXMania.Test/BaseGameTests.cs \
  DTXMania.Test/Config/ConfigDataTests.cs \
  DTXMania.Test/Config/ConfigDataApiSettingsTests.cs \
  DTXMania.Test/Config/ConfigManagerTests.cs \
  DTXMania.Test/CrashReporting/CrashContextPublisherTests.cs \
  DTXMania.Test/CrashReporting/CrashLogFieldPolicyTests.cs \
  DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs

git commit -m "fix: remove stale audio buffer config default"
```

**Task 1 gate:** both platform projects compile; no live `BufferSizeMs` references remain; fresh/default latency is `0`; crash/config tests reflect the removed field; explicit manual latency behavior and the independent 200 ms hit window remain intact.

---

### Task 2: Validate the unchanged MonoGame transport and record release evidence

**Files:**
- Modify: `docs/superpowers/specs/2026-08-29-hpa-12-monogame-audio-validation-design.md`
- No production file is expected to change in this task.

**Interfaces:**
- Consumes: the built HPA-12 branch, existing Config-stage `Audio Latency Offset`, and a known-good local DTX chart.
- Produces: concrete platform/device observations in the spec and a go/no-go decision for the existing MonoGame playback path or a separately scoped follow-up.

- [ ] **Step 1: Build the normal game target for the hardware being tested**

macOS:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --no-restore
```

Windows:

```powershell
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj --no-restore
```

Expected: PASS for each platform on which physical validation is performed.

- [ ] **Step 2: Normalize the validation profile**

For every hardware run configure:

```text
Play Speed           = 100%
Pitch                = 0 st
Audio Latency Offset = 0 ms
```

Use one known-good chart with clear chip attacks, recognizable BGM alignment, enough duration to notice drift, and a pause/resume opportunity.

Do not validate HPA-12 at altered Play Speed/pitch first; those paths add separate transformation behavior that obscures the normal transport question.

- [ ] **Step 3: Record fixed output latency separately from transport drift**

At the opening, observe whether audio is noticeably delayed relative to the raw chart/visual clock.

Do **not** require perceptual zero-latency at `0 ms`. `SongTimer.Play()` submits audio and starts `PlaybackClock` together, while the physical output path can add a fixed delay that CX does not directly control.

Record the fixed baseline qualitatively, then continue the chart long enough to distinguish:

```text
fixed offset that stays roughly constant
vs.
accumulating offset that grows through the song
```

Only the second pattern is direct evidence of transport/clock drift.

- [ ] **Step 4: Evaluate manual judgement and physical chip response separately**

Hit several clear notes and record two observations:

```text
A. physical input -> audible chip-sound response latency
B. manual judgement usability
```

If judgement is systematically early/late, exercise the existing `Audio Latency Offset` in its current `10 ms` increments and record the actual values tried.

Important: this setting changes manual judgement/chip lookup/miss timing only. It does **not** delay visuals and does **not** reduce the physical output latency of a player-triggered chip sound.

Therefore:

- a fixed baseline delay that becomes comfortable for judgement after offset tuning can still be `keep MonoGame` when gameplay remains practical;
- unacceptable physical chip-response latency remains a valid `follow-up required` signal even if judgement scoring can be shifted;
- a large fixed audio/visual mismatch that remains materially unplayable is also a follow-up signal, but it does not by itself prove that the remedy is a new backend.

Do not convert one device's preferred offset into the global default.

- [ ] **Step 5: Verify late-song and pause/resume stability**

For the same run:

```text
1. continue far enough to detect accumulating BGM/chart drift;
2. pause for several seconds;
3. resume and compare alignment with the pre-pause state;
4. check late-song/ending stability and playback failures/glitches.
```

A required wired run is a transport failure when it shows a repeatable accumulating drift, permanent pause/resume shift, playback failure/instability, or another materially unplayable behavior—not merely a small constant hardware-output delay.

- [ ] **Step 6: Run one USB-interface observation when hardware is readily available**

Repeat Steps 2-5 for one USB audio interface if available.

If no USB interface is available to the person performing validation, record exactly that fact in the spec. Do not fabricate a result and do not block code cleanup on acquiring new hardware.

Bluetooth remains outside the required gate.

- [ ] **Step 7: Replace the validation checklist with concrete evidence**

Under `## Validation Results` in the spec, append one Markdown row per actual environment using this exact column layout:

```markdown
| Platform | Output path/device | Chart | Offset tried | Fixed start/output offset | Late-song drift | Pause/resume | Chip/manual response | Decision |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
```

Use concise, concrete observations. The following row is fictional and demonstrates granularity only:

```text
Windows 11 | wired motherboard output | example-long-chart.dtx | 0, 30 ms | small fixed audible delay | no accumulating drift noticed through full song | resumed without new shift | chip response usable; judgement comfortable at 30 ms | keep MonoGame
```

Never copy the fictional values into validation results. Every value entered in the PR must come from the actual run.

Set each decision to exactly one of:

```text
keep MonoGame
follow-up required
```

Then mark the corresponding validation checkboxes in the spec based only on completed observations.

- [ ] **Step 8: Apply the follow-up rule only to concrete failures**

If any required wired environment needs `follow-up required`, keep HPA-12's production scope unchanged and capture these facts in the same spec row/adjacent paragraph:

```text
platform + OS version
physical output path/device
chart used
Play Speed / Pitch used (normally 100% / 0 st)
configured latency-offset values tried
whether the issue is fixed A/V mismatch, physical chip latency, accumulating drift,
pause/resume shift, playback failure, or instability
repeatability
```

Create one separate Linear follow-up only when the evidence points to a concrete missing capability.

The follow-up title/description should state the observed defect first. Do not prescribe DirectSound/ASIO/WASAPI unless evidence specifically shows that backend capability is the smallest remedy. A visual/calibration follow-up may be more appropriate for a fixed A/V offset; a backend follow-up is appropriate only when the transport/output path itself is the demonstrated limitation.

If all required wired environments say `keep MonoGame`, no follow-up ticket is needed.

- [ ] **Step 9: Run the final repository gates on the available development hosts**

At minimum run the full unit suite for each host available during implementation:

macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore
```

Windows:

```powershell
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore
```

Also build the corresponding game project after tests.

Expected: PASS. Existing explicit non-zero latency tests must continue to prove that the manual correction path works even though the product default is now `0`.

- [ ] **Step 10: Commit validation evidence and prepare the same PR for review**

```bash
git add docs/superpowers/specs/2026-08-29-hpa-12-monogame-audio-validation-design.md
git commit -m "docs: record HPA-12 audio validation evidence"
```

Before switching the PR out of Draft, verify live surfaces rather than historical planning text:

```bash
git grep -n "BufferSizeMs" -- DTXMania.Game DTXMania.Test DTXMania.E2E
# expected: no output

git status --short
# expected: clean
```

PR readiness checklist:

```text
[ ] Windows wired/default observation recorded at 100% / 0 st
[ ] macOS wired/default observation recorded at 100% / 0 st
[ ] USB observation recorded, or unavailability explicitly recorded
[ ] fixed output latency distinguished from accumulating drift
[ ] physical chip response and judgement usability recorded separately
[ ] no live BufferSizeMs references
[ ] AudioLatencyOffsetMs fresh/default == 0
[ ] manual latency UI/persistence preserved
[ ] 200 ms hit-detection window preserved
[ ] focused tests pass
[ ] full available-host tests/build pass
[ ] no backend/dependency/architecture expansion
```

**Task 2 gate:** real-device evidence is present and the PR either confirms MonoGame is acceptable for current CX gameplay or contains enough concrete failure evidence to justify one separately scoped follow-up.
