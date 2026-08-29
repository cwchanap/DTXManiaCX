# HPA-12 MonoGame Audio Validation and Stale Setting Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Keep planning, implementation, validation notes, and review fixes on this same PR.

**Goal:** Keep the existing MonoGame audio playback path, remove the dead `BufferSizeMs` configuration, make `AudioLatencyOffsetMs=0` the neutral fresh/default behavior, correct stale latency comments, and record real-device evidence before the PR becomes ready for review.

**Architecture:** Do not add an audio abstraction or backend. `ManagedSound` / MonoGame `SoundEffectInstance` remains the playback transport, `SongTimer` / `PlaybackClock` remains the raw logical chart clock, and `PerformanceStage.GetPlayerJudgementTimeMs(...)` remains the only output-latency compensation seam for manual play.

**Tech Stack:** .NET 8, C#, MonoGame 3.8.x (`WindowsDX` on Windows, `DesktopGL` on macOS), xUnit, SQLite-backed `ConfigManager`.

**Spec:** `docs/superpowers/specs/2026-08-29-hpa-12-monogame-audio-validation-design.md`

## Global Constraints

- One PR for HPA-12 planning, implementation, validation evidence, and review fixes.
- Keep MonoGame playback unless real-device validation exposes a repeatable blocking defect.
- Do not add DirectSound, ASIO, WASAPI, BASS, NAudio, or another playback dependency.
- Do not add `IAudioEngine`, backend selection, device selection, buffer configuration, auto-calibration, per-device profiles, or a latency benchmark harness.
- Remove `BufferSizeMs`; do not rename or replace it.
- Fresh/default `AudioLatencyOffsetMs` is exactly `0` ms.
- Keep the existing manual `Audio Latency Offset` range `0..500` ms with `10` ms steps.
- Keep latency compensation scoped to manual judgement/chip/miss timing. Do not move it into `SongTimer`, `PlaybackClock`, `ManagedSound`, chart parsing, BGM/video scheduling, autoplay, visuals, progress, or completion.
- Do not add a migration for the old `200` default. Existing persisted values continue to load normally; fresh config and reset-to-defaults use `0`.
- Real-device evidence must be recorded from actual hardware. An agent must never fabricate or infer validation results.
- If validation shows a backend problem, document it and create a separate follow-up rather than widening HPA-12.

---

### Task 1: Remove dead buffer configuration and neutralize the latency default

**Files:**
- Modify: `DTXMania.Game/Lib/Config/ConfigData.cs`
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Modify: `DTXMania.Test/Config/ConfigManagerTests.cs`
- Modify: `DTXMania.Test/BaseGameTests.cs`
- Inspect only unless a stale default assertion is found: `DTXMania.Test/Config/ConfigStageLogicTests.cs`
- Inspect only unless a stale default assertion is found: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`

**Interfaces:**
- Consumes: existing `ConfigData.AudioLatencyOffsetMs`, `ConfigManager.SetAudioLatency(int)`, and `PerformanceStage.GetPlayerJudgementTimeMs(double)` semantics.
- Produces: `ConfigData` with no `BufferSizeMs`; fresh/default `AudioLatencyOffsetMs == 0`; unchanged explicit latency-offset behavior and persistence.

- [ ] **Step 1: Add a failing assertion for the neutral default**

Extend the existing `ConfigManager_Constructor_ShouldInitializeWithDefaultConfig` test in `DTXMania.Test/Config/ConfigManagerTests.cs`:

```csharp
[Fact]
public void ConfigManager_Constructor_ShouldInitializeWithDefaultConfig()
{
    var manager = new ConfigManager();

    Assert.NotNull(manager.Config);
    Assert.Equal("NX1.5.0-MG", manager.Config.DTXManiaVersion);
    Assert.Equal(1280, manager.Config.ScreenWidth);
    Assert.Equal(720, manager.Config.ScreenHeight);
    Assert.Equal(0, manager.Config.AudioLatencyOffsetMs);
}
```

Do not add a reflection test for the absence of `BufferSizeMs`; repository search plus compilation is sufficient coverage for deleting a dead property.

- [ ] **Step 2: Run the focused test and confirm current `main` behavior is red**

macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~ConfigManagerTests.ConfigManager_Constructor_ShouldInitializeWithDefaultConfig"
```

Windows:

```powershell
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore `
  --filter "FullyQualifiedName~ConfigManagerTests.ConfigManager_Constructor_ShouldInitializeWithDefaultConfig"
```

Expected on the pre-change code: FAIL because the actual default is `200`.

- [ ] **Step 3: Remove `BufferSizeMs` and set the neutral latency default**

In `DTXMania.Game/Lib/Config/ConfigData.cs`, remove this property entirely:

```csharp
public int BufferSizeMs { get; set; } = 100;
```

Change the latency property default to:

```csharp
public int AudioLatencyOffsetMs { get; set; } = 0;
```

Replace the existing XML documentation with behavior-only wording equivalent to:

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

Do not mention a presumed OpenAL/XAudio/driver latency range because CX does not measure it.

- [ ] **Step 4: Remove the stale test initializer and update fresh-persistence expectation**

In `DTXMania.Test/BaseGameTests.cs`, remove only the dead initializer entry:

```csharp
BufferSizeMs = 80,
```

Do not add it to crash diagnostics under another name.

In `DTXMania.Test/Config/ConfigManagerTests.cs`, change the fresh/default snapshot assertion from:

```csharp
Assert.Equal("200", rows["AudioLatencyOffsetMs"]);
```

to:

```csharp
Assert.Equal("0", rows["AudioLatencyOffsetMs"]);
```

Keep explicit-value coverage such as `350` unchanged. Keep the negative-value clamp test unchanged.

- [ ] **Step 5: Remove stale `200 ms` default commentary from `PerformanceStage` without changing code behavior**

Find comments around compensated judgement/miss timing that describe `200 ms` as the default or equate it with a hit window. Reword them to describe the invariant instead:

```text
Pending hits and timeout misses share the latency-compensated logical time so
manual judgement does not lose part of its hit window when the user has
configured an output-latency correction.
```

Do not change the implementation of:

```csharp
GetPlayerJudgementTimeMs(...)
UpdateGameplayManagers(...)
FindNearestNoteForChip(...)
JudgementManager.Update(...)
```

The existing explicit-offset tests are the regression guard.

- [ ] **Step 6: Search for stale buffer/default assumptions**

Run:

```bash
git grep -n "BufferSizeMs"
```

Expected: no matches.

Then inspect all latency references:

```bash
git grep -n -E "AudioLatencyOffsetMs|200 ms|200ms" -- \
  DTXMania.Game DTXMania.Test
```

Expected:

- no comment claims `200 ms` is the product default or known backend latency;
- explicit test fixtures may still use `200` or `250` to exercise non-zero compensation;
- `ConfigManager` still parses, persists, and clamps `AudioLatencyOffsetMs`;
- `ConfigStage` still exposes the existing manual control.

- [ ] **Step 7: Run focused automated regression tests**

macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~BaseGameTests"
```

Windows:

```powershell
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore `
  --filter "FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~BaseGameTests"
```

Expected: PASS on the available host project. Do not weaken existing explicit-offset tests to accommodate the new default.

- [ ] **Step 8: Commit the code cleanup on the existing HPA-12 branch**

```bash
git add \
  DTXMania.Game/Lib/Config/ConfigData.cs \
  DTXMania.Game/Lib/Stage/PerformanceStage.cs \
  DTXMania.Test/Config/ConfigManagerTests.cs \
  DTXMania.Test/BaseGameTests.cs

git commit -m "fix: remove stale audio buffer config default"
```

If inspection found a genuinely stale default assertion in one of the two inspect-only test files, include that test file in the same commit. Do not otherwise touch it.

**Task 1 gate:** the codebase compiles with no `BufferSizeMs` reference; fresh/default latency is `0`; explicit manual latency behavior remains green.

---

### Task 2: Validate the unchanged MonoGame transport and record release evidence

**Files:**
- Modify: `docs/superpowers/specs/2026-08-29-hpa-12-monogame-audio-validation-design.md`
- No production file is expected to change in this task.

**Interfaces:**
- Consumes: the built HPA-12 branch, existing Config-stage `Audio Latency Offset`, and a known-good local DTX chart.
- Produces: concrete platform/device observations in the spec and a go/no-go decision for the existing MonoGame playback path.

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

- [ ] **Step 2: Run the wired/default-output smoke with latency compensation at zero**

For both required platform environments, configure:

```text
Audio Latency Offset = 0 ms
```

Use one known-good chart with clear chip attacks, recognizable BGM alignment, enough duration to notice drift, and a pause/resume opportunity.

For each run, explicitly observe all five behaviors:

```text
1. opening BGM/chart alignment
2. manual hit/chip response
3. late-song BGM/chart alignment / accumulating drift
4. pause for several seconds -> resume alignment
5. ending/late-song stability
```

Do not infer success from unit tests or a screenshot. This step requires listening/playing on the named hardware.

- [ ] **Step 3: If a wired path is materially late, exercise only the existing manual offset**

Adjust the existing setting in its current `10 ms` increments until the path is usable or until it is clear that a fixed offset cannot correct the problem.

Record the actual values tried. Do not implement calibration code and do not convert one device's preferred value into the default.

If a fixed manual offset cannot make the path usable, record the failure as a potential follow-up backend/timing defect. Do not modify the playback architecture in HPA-12.

- [ ] **Step 4: Run one USB-interface observation when hardware is readily available**

Repeat Steps 2-3 for one USB audio interface if available.

If no USB interface is available to the person performing validation, record exactly that fact in the spec. Do not fabricate a result and do not block code cleanup on acquiring new hardware.

- [ ] **Step 5: Replace the validation checklist with concrete evidence**

Under `## Validation Results` in the spec, append one Markdown row per actual environment using this exact column layout:

```markdown
| Platform | Output path/device | Chart | Offset tried | Start alignment | Late-song drift | Pause/resume | Chip/manual response | Decision |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
```

Use concise concrete observations, for example the level of detail expected is:

```text
Windows 11 | wired motherboard output | <actual chart name> | 0, 30 ms | opening visually/audibly aligned | no accumulating drift noticed through full song | resumed without new shift | 0 felt late; 30 ms comfortable | keep MonoGame
```

That sentence is an **example of evidence granularity**, not a result to copy. Every value entered in the PR must come from the actual run.

Set the decision for each row to one of:

```text
keep MonoGame
follow-up required
```

Then mark the corresponding validation checkboxes in the spec based only on completed observations.

- [ ] **Step 6: Apply the follow-up rule if validation finds a blocking defect**

If any required wired environment needs `follow-up required`, keep HPA-12's code scope unchanged and capture these facts in the same spec row/adjacent paragraph:

```text
platform + OS version
physical output path/device
chart used
configured offset values tried
whether the issue is fixed offset, accumulating drift, pause/resume shift, playback failure, or instability
repeatability
```

Create one separate Linear follow-up only when the evidence points to a concrete missing capability. The follow-up should state the observed defect; it should not prescribe DirectSound/ASIO/WASAPI unless the evidence specifically requires one of them.

If all required wired environments say `keep MonoGame`, no follow-up ticket is needed.

- [ ] **Step 7: Run the final repository gates on the available development hosts**

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

- [ ] **Step 8: Commit validation evidence and prepare the same PR for review**

```bash
git add docs/superpowers/specs/2026-08-29-hpa-12-monogame-audio-validation-design.md
git commit -m "docs: record HPA-12 audio validation evidence"
```

Before switching the PR out of Draft, verify:

```bash
git grep -n "BufferSizeMs"        # expected: no output
git status --short                 # expected: clean
```

PR readiness checklist:

```text
[ ] Windows wired/default observation recorded
[ ] macOS wired/default observation recorded
[ ] USB observation recorded, or unavailability explicitly recorded
[ ] no BufferSizeMs references
[ ] AudioLatencyOffsetMs fresh/default == 0
[ ] manual latency UI/persistence preserved
[ ] focused tests pass
[ ] full available-host tests/build pass
[ ] no backend/dependency/architecture expansion
```

**Task 2 gate:** real-device evidence is present and the PR either confirms MonoGame is acceptable or contains enough concrete failure evidence to justify a separately scoped follow-up.
