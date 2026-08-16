# HPA-13 Chart-Synchronized Gameplay Metronome Implementation Plan

> **For implementation agents:** keep HPA-13 as one feature ticket and one implementation PR. Follow the tasks in order, use TDD, and do not add deferred practice/audio features.

**Goal:** Add an optional gameplay metronome that accents measure starts, clicks later quarter-note boundaries, follows the complete DTX timing map and Play Speed, and is enabled from persisted Drums settings.

**Architecture:** `ChartTimingMap` remains a position→time calculator. `ParsedChart.FinalizeChart()` owns quarter-note grid policy and publishes immutable `BeatMarker` values. A pure `MetronomePlayer` consumes those markers using raw logical song time. `PerformanceStage` owns click resources and converts a real-time stale-click tolerance into logical chart time. Click assets are deterministic ffmpeg-generated OGGs managed through the existing `tools/sfxgen` inventory.

**Scope guard:** no count-in, subdivisions, channel `51`, visual beats, hotkeys, practice loops, per-song setting, volume routing, runtime click synthesis, audio service, worker thread, event bus, or sample-accurate scheduler.

---

## Task 1: Persist and expose the Metronome toggle

**Estimate:** 0.5 engineer-day

**Files:**

- Modify: `DTXMania.Game/Lib/Config/ConfigData.cs`
- Modify: `DTXMania.Game/Lib/Config/IConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- Modify: `DTXMania.Test/Config/ConfigDataTests.cs`
- Modify: `DTXMania.Test/Config/ConfigManagerTests.cs`
- Modify: `DTXMania.Test/Config/ConfigStageLogicTests.cs`
- Modify: `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`

### 1.1 Add failing config tests

Cover:

1. `new ConfigData().Metronome == false`.
2. `LoadConfig` accepts `Metronome=true`, `1`, and `on` through existing `TryParseBool` behavior.
3. `SaveConfig` writes exactly one `Metronome=<value>` under `[Game]`.
4. Save/load round trip preserves the value.
5. `SetMetronome(bool)` mutates only on change and uses the existing deferred-save path.

No migration alias. Missing key means default Off.

### 1.2 Extend existing ConfigStage tests

Do not add a second inventory-style test.

Update `ConfigStageLogicTests`:

- add `Metronome` between `Pitch` and `Auto Play` in `SetupConfigItems_ShouldBuildSystemDrumsExitCategories`;
- add `[InlineData("Metronome", nameof(ConfigData.Metronome))]` to `ActivatePressedOnToggle_ShouldMutateConfigViaSetter`.

Update `DrumConfigStageTests.StubConfigManager` with `SetMetronome(bool)` in the same change so the `IConfigManager` edit does not break test compilation.

### 1.3 Implement

Add:

```csharp
public bool Metronome { get; set; } = false;
```

Follow `AutoPlay` / `NoFail` for parse, save, setter, and deferred dirty state.

Add the Drums toggle after Pitch:

```text
Name: Metronome
Description: Accents each measure start and clicks later quarter-note beats during gameplay.
```

### 1.4 Verify

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~ConfigDataTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~DrumConfigStageTests"
```

---

## Task 2: Generalize timing lookup and publish beat markers

**Estimate:** 0.75 engineer-day

**Files:**

- Create: `DTXMania.Game/Lib/Song/Components/BeatMarker.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/ParsedChart.cs`
- Modify: `DTXMania.Test/Song/ChartTimingMapTests.cs`
- Modify: `DTXMania.Test/Song/ParsedChartTests.cs`

### 2.1 Add failing `ChartTimingMapTests`

The map owns timing resolution only. Cover:

1. Existing integer positions still resolve correctly through `CalculateTimeMs(int, double)`.
2. Fractional ticks resolve correctly at base BPM.
3. Fractional ticks before/at/after an integer in-measure BPM anchor choose the correct anchor.
4. Negative and non-finite ticks are rejected.
5. `tick >= 192` carries into later bars while preserving the fractional remainder.
6. Out-of-compiled-horizon positions still fail.
7. `GetMeasureLengthMultiplier(bar)` returns the multiplier from the compiled bar-start anchor and defaults to `1.0` for an unconfigured compiled bar.

### 2.2 Implement the marker model

```csharp
public sealed class BeatMarker
{
    public double TimeMs { get; init; }
    public bool IsMeasureStart { get; init; }
}
```

No bar/BPM/lane/render/scoring metadata.

### 2.3 Replace the integer-only timing method with one double method

Change the internal timing API to:

```csharp
internal double CalculateTimeMs(int bar, double tick);
```

Do **not** keep a second `int` overload with different normalization rules.

Implementation rules:

- existing integer callers rely on implicit numeric conversion and preserve behavior;
- keep current `NormalizePosition(int, int)` for authored integer parser/tempo-map paths;
- add a private fractional normalizer used only by `CalculateTimeMs`;
- fractional normalization rejects negative/non-finite ticks, carries whole 192-tick measures, and preserves the remainder;
- keep `_tempoChanges` and `TimingAnchor.Tick` integer-based;
- use `Math.Floor(normalizedTick)` when calling `FindAnchorIndex`, because all anchors are integer ticks;
- widen private `CalculateIntervalMs` to `double tickDelta`.

Add:

```csharp
internal double GetMeasureLengthMultiplier(int bar);
```

Read the multiplier from the compiled bar-start `TimingAnchor`, not directly from `_measureLengths`, so compiled timing state remains the source of truth.

### 2.4 Add failing `ParsedChartTests` for musical grid policy

Use base BPM `120` where convenient. Cover:

1. Multiplier `1.0`: four markers at `0`, `500`, `1000`, `1500`; first accented.
2. `0.75`: three quarter-note markers before next accent.
3. `1.5`: six markers before next accent.
4. `0.625`: 2.5-beat measure emits offsets `0`, `1`, `2` without rounding.
5. Boundary BPM change.
6. In-measure BPM change.
7. Adjacent measures have exactly one shared-boundary accent.
8. Terminal `MeasureLine` does not create an extra metronome measure.
9. Empty chart has no markers.
10. Repeated `FinalizeChart()` is idempotent.
11. A multiplier whose `4 * m` is only marginally above an integer does **not** emit a near-tick-192 extra marker.
12. A very short positive measure still emits its measure-start accent.

### 2.5 Enumerate markers in `ParsedChart.FinalizeChart()`

Add:

```csharp
public List<BeatMarker> BeatMarkers { get; } = new();
```

Clear it with `MeasureLines` at the start of finalization.

Keep the existing occupied-horizon and timing-map rebuild flow. After timing rebuild, enumerate bars `0..highestOccupiedBar` using:

```text
BeatGridEpsilon = 1e-6
measureBeats = 4.0 * TimingMap.GetMeasureLengthMultiplier(bar)

for integer beatOffset = 0;
    beatOffset == 0 || beatOffset < measureBeats - BeatGridEpsilon;
    beatOffset++:

    tick = beatOffset * 192.0 / measureBeats
    BeatMarkers.Add(
        TimeMs = TimingMap.CalculateTimeMs(bar, tick),
        IsMeasureStart = beatOffset == 0)
```

Never emit tick `192` from the current bar. Leave `ChartManager` unchanged.

### 2.6 Verify

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~ChartTimingMapTests|FullyQualifiedName~ParsedChartTests"
```

---

## Task 3: Add the pure stale-click scheduler

**Estimate:** 0.4 engineer-day

**Files:**

- Create: `DTXMania.Game/Lib/Stage/Performance/MetronomePlayer.cs`
- Create: `DTXMania.Test/Stage/Performance/MetronomePlayerTests.cs`

### 3.1 Write failing scheduler tests

Construct `BeatMarker` lists directly. Cover:

1. No callback before a marker is due.
2. Due regular/accent markers are forwarded correctly.
3. Marker zero plays when current time is within the supplied tolerance.
4. Several overdue markers consume all but forward only the latest.
5. Latest marker older than the supplied tolerance is dropped.
6. Consumed markers never replay.
7. Empty list is a no-op.

Do **not** add a reset test.

### 3.2 Implement

```csharp
internal sealed class MetronomePlayer
{
    internal MetronomePlayer(
        IReadOnlyList<BeatMarker> markers,
        double maxLateChartMs,
        Action<BeatMarker> playClick);

    internal void Update(double currentChartTimeMs);
}
```

`maxLateChartMs` is already converted from a real-time threshold by the stage.

On update:

- consume all due markers;
- retain only the latest consumed marker;
- callback once when `currentChartTimeMs - marker.TimeMs <= maxLateChartMs`;
- otherwise drop it.

No `Reset`, pause/resume, seek, clock, interface, event, or disposal API. A fresh player is constructed per performance initialization.

### 3.3 Verify

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~MetronomePlayerTests"
```

---

## Task 4: Add deterministic click assets and integrate `PerformanceStage`

**Estimate:** 1.0 engineer-day

**Files:**

- Modify: `DTXMania.Game/Lib/Resources/SoundPath.cs`
- Modify: `tools/sfxgen/manifest.json`
- Modify: `tools/sfxgen/sfxgen.py`
- Modify: `tools/sfxgen/test_sfxgen.py`
- Create binary asset: `System/CXNeon/Sounds/Metronome Beat.ogg`
- Create binary asset: `System/CXNeon/Sounds/Metronome Accent.ogg`
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Modify: `DTXMania.Test/Resources/CxNeonPackTests.cs` only if explicit coverage beyond existing inventory gating is useful
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageAdditionalCoverageTests.cs` only for lifecycle/error branches that do not fit the deterministic file

### 4.1 Add deterministic generator tests first

Extend `test_sfxgen.py` so the manifest supports two generator types:

- omitted/`elevenlabs` — existing behavior;
- `ffmpeg_sine` — deterministic metronome click.

Cover:

1. Existing manifest entries remain valid without adding a `generator` field.
2. `ffmpeg_sine` entries require frequency + positive short duration, not prompt/prompt-influence.
3. Generating a selected `ffmpeg_sine` entry does not require `ELEVENLABS_API_KEY`.
4. The ffmpeg command uses the configured frequency, ~30 ms duration, supported sample rate, and Ogg/Vorbis output.
5. Validation rejects a deterministic click whose actual duration materially exceeds its manifest duration.

Keep this a discriminator inside the existing SFX tool; do not build a generic DSP framework.

### 4.2 Extend `SoundPath` + manifest atomically

Add:

```csharp
public const string MetronomeBeat = "Sounds/Metronome Beat.ogg";
public const string MetronomeAccent = "Sounds/Metronome Accent.ogg";
```

Include both in `GetAllSoundPaths()`.

Add matching manifest entries, for example:

```json
{
  "file": "Metronome Beat.ogg",
  "generator": "ffmpeg_sine",
  "duration_seconds": 0.03,
  "frequency_hz": 1000
}
```

Use a distinct accent frequency, e.g. `1600 Hz`, with the same short duration.

The exact pitch can be tuned during manual verification; onset and duration are the hard requirements.

### 4.3 Extend `sfxgen.py` minimally

For a selected manifest entry:

- default missing `generator` to `elevenlabs`;
- only require `ELEVENLABS_API_KEY` when the selected entry actually uses ElevenLabs;
- dispatch `ffmpeg_sine` to ffmpeg directly;
- generate a ~30 ms sine burst beginning at sample zero;
- apply a fast fade-out/no tail;
- encode Vorbis at a MonoGame-supported sample rate directly into the manifest output directory.

Do not route deterministic clicks through ElevenLabs and do not hand-edit them after generation.

Generate and commit both files in the same change that adds their `SoundPath`/manifest entries:

```bash
python tools/sfxgen/sfxgen.py generate --only "Metronome Beat.ogg"
python tools/sfxgen/sfxgen.py generate --only "Metronome Accent.ogg"
python tools/sfxgen/sfxgen.py validate
```

This keeps `CxNeonPackTests` green throughout the finished commit and makes regeneration key-free for the two timing-critical assets.

### 4.4 Add failing `PerformanceStage` tests

Use the existing inspectable/headless stage pattern. Cover:

1. `Metronome=false` does not request click sounds or create a player.
2. `Metronome=true` requests both `SoundPath` values and creates a player from finalized markers.
3. The callback reaches an overridable `PlayMetronomeClick(BeatMarker)` seam without constructing real `SoundEffect` objects.
4. `UpdateGameplayManagers(raw, compensated)` advances metronome with **raw** time even when the two arguments differ.
5. Real stale tolerance is converted to logical chart time using the frozen Play Speed:
   - `0.50x` → `50` logical ms for a `100` ms real threshold;
   - `1.00x` → `100` logical ms;
   - `2.00x` → `200` logical ms.
6. Cleanup clears the player and balances click sound references.
7. Re-activation constructs a fresh player starting at marker zero.
8. Resource fallback/missing files do not fail gameplay.

Do not make these tests drive `UpdateGameplay` with a live `SongTimer`; reuse `UpdateGameplayManagers`, which already exists specifically around the raw vs compensated clocks.

### 4.5 Implement stage-owned resources

Add fields for:

- frozen `_metronomeEnabled`;
- regular/accent `ISound`;
- `MetronomePlayer`.

Freeze only the setting in `OnActivate`:

```text
_metronomeEnabled = config.Metronome
```

After chart/audio initialization has safely published the finalized chart and only when enabled:

```csharp
_metronomeBeatSound = _resourceManager.LoadSound(SoundPath.MetronomeBeat);
_metronomeAccentSound = _resourceManager.LoadSound(SoundPath.MetronomeAccent);

var maxLateChartMs = 100.0 * _playbackModifiers.Speed;
_metronomePlayer = new MetronomePlayer(
    _parsedChart.BeatMarkers,
    maxLateChartMs,
    PlayMetronomeClick);
```

Use existing activation-generation/audio-lifecycle synchronization. Do not add another cancellation path.

### 4.6 Add the narrow one-shot playback seam

```csharp
protected virtual void PlayMetronomeClick(BeatMarker marker)
{
    var sound = marker.IsMeasureStart
        ? _metronomeAccentSound
        : _metronomeBeatSound;

    sound?.SoundEffect?.Play(1.0f, 0.0f, 0.0f);
}
```

Catch unexpected playback exceptions locally and log without changing gameplay state.

Direct `SoundEffect.Play` is intentional: `ISound.Play(...)` creates caller-owned `SoundEffectInstance` objects, so using it for every click would require another active-instance cleanup collection.

Do not read `SEVolume`.

### 4.7 Drive metronome from the existing dual-clock manager seam

Update:

```csharp
private void UpdateGameplayManagers(
    double logicalSongTimeMs,
    double pendingHitTimeMs)
```

with:

```csharp
_metronomePlayer?.Update(logicalSongTimeMs);
```

Place it beside the raw-clock Auto Play work. Judgement continues to use `pendingHitTimeMs`.

Do not put the scheduler directly beside `ProcessBGMEvents` in `UpdateGameplay`; the outer `_songTimer.IsPlaying` block already gates `UpdateGameplayManagers` during real playback, while the manager method provides the deterministic raw/compensated test seam.

### 4.8 Cleanup

In the existing serialized audio cleanup path:

- set `_metronomePlayer = null`;
- `RemoveReference()` each loaded click sound exactly once;
- null both sound fields.

Do not call `Reset()`; no such API exists. Re-entry rebuilds the player during initialization.

### 4.9 Focused verification

```bash
python tools/sfxgen/test_sfxgen.py
python tools/sfxgen/sfxgen.py validate

dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~CxNeonPackTests|FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~PerformanceStageAdditionalCoverageTests"
```

---

## Task 5: Full verification and manual timing check

**Estimate:** 0.25 engineer-day

### 5.1 Full unit suite

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore
```

Expected: zero failures.

### 5.2 Build

```bash
dotnet build DTXMania.sln --no-restore
```

Expected: successful build with no new HPA-13 warnings.

### 5.3 Establish the asset-resolution precondition

Before evaluating timing, make the two committed click files reachable through the active runtime skin resolution chain.

For a development run, either:

- point the active skin at the repository `System/CXNeon` pack; or
- install/copy the generated sounds into the active/fallback System skin using the normal local development setup.

Then enable Metronome and confirm **one audible click**. If the game silently falls back because the active skin cannot resolve the files, fix asset reachability before debugging timing.

### 5.4 Manual timing matrix

After the audible-click precondition passes, verify:

1. **Disabled:** no clicks.
2. **Normal 4/4 at 120 BPM:** four clicks per measure, first accented.
3. **Channel `02` 0.75:** three clicks before next accent.
4. **Channel `02` 1.5:** six clicks before next accent.
5. **In-measure channel `03`/`08` BPM change:** later spacing changes without measure drift.
6. **Play Speed 0.50x and 2.00x:** click timing remains aligned and stale-drop behavior feels equivalent in real time.
7. **Pause/resume:** silent while paused; no duplicate/catch-up burst on resume.
8. **Re-enter performance:** first downbeat accents again from a fresh player.
9. **Missing click asset:** gameplay continues through silent fallback.
10. **Asset quality:** regular/accent onsets are immediate and do not audibly differ in leading delay; clips do not overlap at representative fast tempos.

### 5.5 Final scope review

The implementation PR should contain only:

- one persisted `Metronome` toggle;
- one `BeatMarker` model/list;
- one generalized `CalculateTimeMs(int, double)` implementation plus compiled multiplier query;
- quarter-note enumeration in `ParsedChart.FinalizeChart()` with epsilon guard;
- one pure `MetronomePlayer` with constructor-supplied logical late tolerance and no reset API;
- two deterministic SFX manifest entries + minimal `ffmpeg_sine` generator support + committed OGGs;
- focused `PerformanceStage` integration through `UpdateGameplayManagers`;
- tests for the above.

Remove any second timing overload with different normalization rules, dead `Reset()`, chart-ms perceptual constant, channel `51`, `SEVolume` wiring, runtime synthesis, count-in, subdivision, visual beat, hotkey, practice-loop, new audio service, or generalized transport work before requesting review.

**Total estimate:** ~2.9 engineer-days.
