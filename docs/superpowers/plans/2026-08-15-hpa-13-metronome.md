# HPA-13 Chart-Synchronized Gameplay Metronome Implementation Plan

> **For implementation agents:** Follow the tasks in order. Keep this as one feature ticket and one implementation PR. Use test-driven development for each task and do not add any deferred practice/audio features.

**Goal:** Add an optional gameplay metronome that accents measure starts, clicks later quarter-note boundaries, follows the complete DTX timing map and Play Speed, and is enabled from persisted Drums settings.

**Architecture:** `ParsedChart.FinalizeChart()` enumerates quarter-note markers and asks `ChartTimingMap` to resolve fractional positions into immutable `BeatMarker.TimeMs` values. A small pure `MetronomePlayer` consumes those markers from the raw `SongTimer` clock. `PerformanceStage` owns optional click resources and a narrow fire-and-forget playback seam.

**Scope guard:** Do not add count-in, subdivisions, channel `51` parsing, visual beats, hotkeys, practice loops, custom sound selection, volume routing, per-song settings, timers, threads, event buses, or sample-accurate scheduling.

---

## Task 1: Persist and expose the Metronome toggle

**Estimated size:** 0.5 engineer-day

**Files:**

- Modify: `DTXMania.Game/Lib/Config/ConfigData.cs`
- Modify: `DTXMania.Game/Lib/Config/IConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- Modify: `DTXMania.Test/Config/ConfigDataTests.cs`
- Modify: `DTXMania.Test/Config/ConfigManagerTests.cs`
- Modify: `DTXMania.Test/Config/ConfigStageLogicTests.cs`
- Modify: `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`

### Step 1: Add failing configuration tests

Cover these contracts before production changes:

1. `new ConfigData().Metronome` is `false`.
2. `LoadConfig` recognizes `Metronome=true`, `1`, and `on` through the existing `TryParseBool` behavior.
3. `SaveConfig` writes exactly one `Metronome=<value>` entry under `[Game]`.
4. A save/load round trip preserves the value.
5. `SetMetronome(bool)` updates the in-memory value and participates in the existing deferred-save behavior.

Do not add migration aliases. A missing key naturally retains the default Off value.

### Step 2: Extend the existing ConfigStage logic tests

Do not create a second inventory-style test file for this toggle. Extend the existing tests that already own these contracts:

- add `Metronome` between `Pitch` and `Auto Play` in `SetupConfigItems_ShouldBuildSystemDrumsExitCategories`;
- add `[InlineData("Metronome", nameof(ConfigData.Metronome))]` to `ActivatePressedOnToggle_ShouldMutateConfigViaSetter`.

This verifies both list order and the setter path using the existing headless input seam.

### Step 3: Update concrete `IConfigManager` test doubles

Adding `SetMetronome(bool)` to `IConfigManager` is a compile-time contract change. Update `DrumConfigStageTests.StubConfigManager` in the same task so the suite still compiles after the interface edit.

Do not add a default interface implementation solely to avoid updating the test double.

### Step 4: Implement the setting

Add to `ConfigData`:

```csharp
public bool Metronome { get; set; } = false;
```

Extend `IConfigManager` and `ConfigManager` following the existing `AutoPlay` / `NoFail` pattern:

- parse `Metronome` with `TryParseBool`;
- serialize `Metronome=<value>` in `[Game]`;
- expose `SetMetronome(bool value)`;
- mark the existing deferred save dirty only when the value changes.

Add a `ToggleConfigItem` to Drums after Pitch and before Auto Play:

```text
Name: Metronome
Description: Accents each measure start and clicks later quarter-note beats during gameplay.
```

### Step 5: Run focused tests

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~ConfigDataTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~DrumConfigStageTests"
```

Expected: selected tests compile and pass.

---

## Task 2: Add fractional timing lookup and publish beat markers

**Estimated size:** 0.75 engineer-day

**Files:**

- Create: `DTXMania.Game/Lib/Song/Components/BeatMarker.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/ParsedChart.cs`
- Modify: `DTXMania.Test/Song/ChartTimingMapTests.cs`
- Modify: `DTXMania.Test/Song/ParsedChartTests.cs`

### Step 1: Add focused `ChartTimingMapTests`

The timing map should not own musical-grid policy. Test only the new timing/resolution seams:

1. `CalculateTimeMs(bar, double tick)` resolves a fractional position at base BPM.
2. A fractional position before an in-measure BPM anchor uses the old BPM.
3. A fractional position at/after the anchor uses the new BPM.
4. `GetMeasureLengthMultiplier(bar)` returns `1.0` when absent and the configured channel `02` multiplier when present.

A useful fractional case is multiplier `0.625` with an in-measure tick such as `76.8`.

### Step 2: Implement `BeatMarker`

Keep the model minimal:

```csharp
public sealed class BeatMarker
{
    public double TimeMs { get; init; }
    public bool IsMeasureStart { get; init; }
}
```

Do not add BPM, bar, lane, rendered position, or scoring state.

### Step 3: Extend `ChartTimingMap` only as a timing resolver

Add:

```csharp
internal double CalculateTimeMs(int bar, double tick);
internal double GetMeasureLengthMultiplier(int bar);
```

Implementation rules:

- retain the existing integer `CalculateTimeMs(int bar, int tick)` for authored events;
- generalize private `CalculateIntervalMs` from `int tickDelta` to `double tickDelta`;
- keep `NormalizePosition` integer-only;
- the new fractional overload accepts an in-measure tick in `[0, 192)`;
- select the last timing anchor at or before the fractional position using the integer floor for `FindAnchorIndex`;
- integrate the remaining fractional delta with that anchor's BPM and measure multiplier;
- `GetMeasureLengthMultiplier` reads the existing `_measureLengths` dictionary and defaults to `1.0`.

Do not add `BuildBeatMarkers`, a new timing table, or a generic fractional chart-position type.

### Step 4: Add musical-grid tests to `ParsedChartTests`

Use base BPM `120` where convenient and cover:

1. **Normal multiplier `1.0`** — times `0`, `500`, `1000`, `1500`; only the first is accented.
2. **Multiplier `0.75`** — three markers before the next measure accent.
3. **Multiplier `1.5`** — six markers before the next measure accent.
4. **Multiplier `0.625`** — emit offsets `0`, `1`, and `2` without rounding the 2.5-beat measure.
5. **Boundary BPM change** — new BPM applies to later markers.
6. **Mid-measure BPM change** — later markers integrate the new BPM rather than dividing the measure evenly.
7. **Adjacent measures** — exactly one accent at the shared boundary.
8. **Terminal measure line** — `highestOccupiedBar + 1` does not create an extra metronome measure.
9. **Empty chart** — no markers.
10. **Repeated finalization** — same count, order, times, and accents.

Use precision-based assertions for `double` milliseconds.

### Step 5: Enumerate markers in `ParsedChart.FinalizeChart()`

Add:

```csharp
public List<BeatMarker> BeatMarkers { get; } = new();
```

During finalization:

- clear `BeatMarkers` with `MeasureLines` before determining the occupied horizon;
- leave empty charts empty;
- rebuild `TimingMap` exactly as today;
- resolve note and BGM times exactly as today;
- for bars `0..highestOccupiedBar`, read the effective multiplier and enumerate the quarter-note grid;
- keep measure-line generation through `highestOccupiedBar + 1` unchanged;
- leave `ChartManager` unchanged.

Grid rule:

```text
measureBeats = 4.0 * multiplier
for beatOffset = 0 while beatOffset < measureBeats:
    tick = beatOffset * 192.0 / measureBeats
    add BeatMarker(
        TimingMap.CalculateTimeMs(bar, tick),
        IsMeasureStart = beatOffset == 0)
```

Never emit tick `192` from the current measure.

### Step 6: Run focused tests

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~ChartTimingMapTests|FullyQualifiedName~ParsedChartTests"
```

Expected: all selected tests pass, including existing HPA-600 timing coverage.

---

## Task 3: Add the pure metronome scheduler

**Estimated size:** 0.5 engineer-day

**Files:**

- Create: `DTXMania.Game/Lib/Stage/Performance/MetronomePlayer.cs`
- Create: `DTXMania.Test/Stage/Performance/MetronomePlayerTests.cs`

### Step 1: Write failing scheduler tests

Construct marker lists directly; do not initialize MonoGame audio.

Cover:

1. Updating before a marker produces no callback.
2. A due regular marker is forwarded once.
3. A due accented marker is forwarded once.
4. Marker zero is played when the first update arrives within the late tolerance.
5. When several markers became due during one delayed frame, only the latest one is forwarded.
6. When the latest due marker is older than the tolerance, it is skipped.
7. Already-consumed markers never replay.
8. `Reset()` returns to marker zero.
9. An empty list is a no-op.

### Step 2: Implement the scheduler

Use this narrow responsibility:

```csharp
internal sealed class MetronomePlayer
{
    internal const double MaxLatePlaybackMs = 100.0;

    internal MetronomePlayer(
        IReadOnlyList<BeatMarker> markers,
        Action<BeatMarker> playClick);

    internal void Reset();
    internal void Update(double currentChartTimeMs);
}
```

On `Update`:

- consume all markers with `TimeMs <= currentChartTimeMs`;
- retain only the last consumed marker;
- invoke once only when `currentChartTimeMs - marker.TimeMs <= MaxLatePlaybackMs`.

Do not add pause/resume methods, clock ownership, seeking, `IDisposable`, an interface, or an event.

### Step 3: Run focused tests

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~MetronomePlayerTests"
```

Expected: scheduler tests pass without graphics or audio initialization.

---

## Task 4: Add click assets and integrate `PerformanceStage`

**Estimated size:** 0.75 engineer-day

**Files:**

- Modify: `DTXMania.Game/Lib/Resources/SoundPath.cs`
- Modify: `tools/sfxgen/manifest.json`
- Create binary asset: `System/CXNeon/Sounds/Metronome Beat.ogg`
- Create binary asset: `System/CXNeon/Sounds/Metronome Accent.ogg`
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Modify: `DTXMania.Test/Resources/CxNeonPackTests.cs`
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageAdditionalCoverageTests.cs` only when a lifecycle branch does not fit the deterministic file

### Step 1: Extend the sound inventory and generation manifest

Add to `SoundPath` and `GetAllSoundPaths()`:

```csharp
public const string MetronomeBeat = "Sounds/Metronome Beat.ogg";
public const string MetronomeAccent = "Sounds/Metronome Accent.ogg";
```

Add matching entries to `tools/sfxgen/manifest.json`. Keep them short, dry, and clearly distinguishable; the accent should have a stronger/higher transient than the regular beat.

Generate and commit both OGGs through the existing pipeline, for example:

```bash
python tools/sfxgen/sfxgen.py generate --only "Metronome Beat.ogg"
python tools/sfxgen/sfxgen.py generate --only "Metronome Accent.ogg"
python tools/sfxgen/sfxgen.py validate
```

Generation requires the existing `ELEVENLABS_API_KEY` + ffmpeg setup. Do not hand-author a parallel asset manifest.

`CxNeonPackTests` already gates every `SoundPath.GetAllSoundPaths()` entry once the sound tree exists; extend/adjust it only if an explicit assertion is needed for the two new paths.

### Step 2: Add failing `PerformanceStage` tests

Use existing headless/test-subclass seams. Cover these contracts:

1. With `Metronome=false`, initialization does not request either click sound and creates no player.
2. With `Metronome=true` and a finalized chart, initialization requests both sound paths and creates a player from `BeatMarkers`.
3. The player callback reaches a narrow overridable `PlayMetronomeClick(BeatMarker)` hook.
4. `UpdateGameplay` drives the player only from raw `currentTimeMs` while `_songTimer.IsPlaying`.
5. READY/loading/paused states do not advance the player.
6. Cleanup clears the player and balances references for both loaded sounds.
7. Resource fallback/missing assets do not fail gameplay.
8. Restart/re-activation begins again from marker zero.

Do not construct a real `SoundEffect` merely to test callback wiring.

### Step 3: Implement stage-owned resources

Add nullable fields for:

- frozen `_metronomeEnabled`;
- regular `ISound`;
- accent `ISound`;
- `MetronomePlayer`.

Freeze only the toggle in `OnActivate`:

```text
enabled = config.Metronome
```

Do **not** read `SEVolume`. Volume routing is not currently persisted/applied consistently across gameplay audio and is outside HPA-13.

Create the player only after the chart is finalized. Load sounds only when enabled:

```csharp
_metronomeBeatSound = _resourceManager.LoadSound(SoundPath.MetronomeBeat);
_metronomeAccentSound = _resourceManager.LoadSound(SoundPath.MetronomeAccent);
_metronomePlayer = new MetronomePlayer(
    _parsedChart.BeatMarkers,
    PlayMetronomeClick);
```

Use the resource manager's existing missing-file behavior: it reports the failure and normally returns a silent fallback. Keep defensive null handling only for the rare fallback-creation failure path; do not invent a separate null-on-missing contract.

### Step 4: Add the narrow fire-and-forget playback seam

Add a small overridable method on `PerformanceStage`:

```csharp
protected virtual void PlayMetronomeClick(BeatMarker marker)
{
    var sound = marker.IsMeasureStart
        ? _metronomeAccentSound
        : _metronomeBeatSound;

    sound?.SoundEffect?.Play(1.0f, 0.0f, 0.0f);
}
```

Catch unexpected playback exceptions locally and log without changing stage state.

The direct `SoundEffect.Play` overload is intentional for short one-shots: it avoids creating caller-owned `SoundEffectInstance` objects and therefore avoids a new active-instance collection. The virtual method is the test hook; do not add a project-wide audio service/interface for this feature.

### Step 5: Integrate the raw clock

Inside the existing block:

```csharp
if (_songTimer != null && _songTimer.IsPlaying)
{
    var currentTimeMs = _songTimer.GetCurrentMs(_currentGameTime);
    ...
}
```

call:

```csharp
_metronomePlayer?.Update(currentTimeMs);
```

Place it beside `ProcessBGMEvents(currentTimeMs)` and before latency-compensated player judgement timing so the raw-clock dependency is explicit.

Do not call it from `GetPlayerJudgementTimeMs`, `SongTimer.Update`, draw methods, or a separate timer.

### Step 6: Complete cleanup

In the existing serialized audio cleanup path:

- clear `_metronomePlayer`;
- call `RemoveReference()` exactly once for each non-null loaded click sound;
- null the sound fields;
- use the existing activation-generation/audio-lifecycle gate so cancelled initialization cannot publish resources after teardown.

Do not add another cancellation mechanism.

### Step 7: Run focused tests

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~CxNeonPackTests|FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~PerformanceStageAdditionalCoverageTests"
```

Also validate the sound pack:

```bash
python tools/sfxgen/sfxgen.py validate
```

Expected: selected tests and asset validation pass.

---

## Task 5: Full verification and manual timing check

**Estimated size:** 0.25 engineer-day

### Step 1: Run the complete unit suite

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore
```

Expected: zero failures.

### Step 2: Build the solution

```bash
dotnet build DTXMania.sln --no-restore
```

Expected: successful build with no new HPA-13 warnings.

### Step 3: Perform focused manual checks

Use small local DTX fixtures or existing timing-map fixtures for each case:

1. **Disabled:** no metronome click sounds are requested/audible.
2. **Normal 4/4 at 120 BPM:** four clicks per measure, first accented.
3. **Channel `02` 0.75:** three quarter-note clicks before the next accent.
4. **Channel `02` 1.5:** six quarter-note clicks before the next accent.
5. **Channel `03` or `08` mid-measure tempo change:** later click spacing changes without measure drift.
6. **Play Speed 0.50x and 2.00x:** clicks remain aligned with notes/BGM.
7. **Pause/resume:** silence while paused, then no duplicate or burst on resume.
8. **Restart/re-enter:** first downbeat accents again.
9. **Missing click asset:** gameplay continues using the resource manager's silent fallback behavior.

### Step 4: Review the final diff against scope

Confirm the implementation contains only:

- one persisted `Metronome` toggle;
- one `BeatMarker` model/list;
- two narrow timing-map helpers (`double` time lookup + measure multiplier lookup);
- beat-grid enumeration in `ParsedChart.FinalizeChart()`;
- one pure `MetronomePlayer` scheduler;
- two `SoundPath` entries + SFX manifest entries + committed OGGs;
- focused `PerformanceStage` integration and tests.

Remove any `BuildBeatMarkers` timing-map policy, `SEVolume` wiring, channel `51` parsing, count-in, subdivision, visual, hotkey, practice-loop, new audio-service, or generalized transport work before requesting review.