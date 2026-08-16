# HPA-13 Chart-Synchronized Gameplay Metronome Implementation Plan

> **For implementation agents:** Follow the tasks in order. Keep this as one feature ticket and one implementation PR. Use test-driven development for each task and do not add any of the explicitly deferred practice/audio features.

**Goal:** Add an optional gameplay metronome that accents measure starts, clicks quarter-note boundaries, follows the complete DTX timing map and Play Speed, and is enabled from the persisted Drums settings.

**Architecture:** `ChartTimingMap` resolves authored beat positions into immutable `BeatMarker.TimeMs` values during `ParsedChart.FinalizeChart()`. A small pure `MetronomePlayer` consumes those markers from the raw `SongTimer` logical clock. `PerformanceStage` owns optional sound resources and maps due markers to regular/accent one-shot playback.

**Tech stack:** .NET 8, C#, MonoGame audio, xUnit, existing `ConfigManager`, `ChartTimingMap`, `ParsedChart`, `SongTimer`, `IResourceManager`, and `PerformanceStage` seams.

**Scope guard:** Do not add count-in, subdivisions, visual beats, hotkeys, practice loops, custom sound selection, a separate volume, per-song settings, timers, threads, event buses, or sample-accurate scheduling.

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
- Modify: `DTXMania.Test/Stage/ConfigStageCoverageTests.cs`

### Step 1: Add failing configuration tests

Cover these contracts before production changes:

1. `new ConfigData().MetronomeEnabled` is `false`.
2. `LoadConfig` recognizes `MetronomeEnabled=true` and `false` case-insensitively through the existing boolean parser behavior.
3. `SaveConfig` writes exactly one `MetronomeEnabled=<value>` entry.
4. A save/load round trip preserves the value.
5. `SetMetronomeEnabled(bool)` updates the in-memory value and participates in the existing deferred-save behavior.

Do not add migration logic. A missing key must naturally retain the default Off value.

### Step 2: Add a failing ConfigStage inventory test

Use the existing `ConfigStageTestFactory` / reflection pattern already used by `ConfigStageCoverageTests` to call `SetupConfigItems` headlessly and inspect the Drums category.

Assert that:

- a `Metronome` toggle exists;
- it appears after `Pitch` and before `Auto Play`;
- its current-value callback reads `ConfigData.MetronomeEnabled`;
- changing it invokes the typed configuration setter.

Avoid rendering or keyboard simulation for this contract; this is an item-registration test.

### Step 3: Implement the setting

Add to `ConfigData`:

```csharp
public bool MetronomeEnabled { get; set; } = false;
```

Extend `IConfigManager` and `ConfigManager` following the existing `AutoPlay` / `NoFail` pattern:

- parse `MetronomeEnabled`;
- serialize it in `SaveConfig`;
- expose `SetMetronomeEnabled(bool enabled)`;
- mark the normal deferred save dirty rather than forcing a new immediate-write path.

Add a `ToggleConfigItem` to the Drums list:

```text
Name: Metronome
Description: Plays an accented click on each quarter-note beat during gameplay.
```

Do not add a separate settings category or overlay.

### Step 4: Run focused tests

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~ConfigDataTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigStageCoverageTests"
```

Expected: all selected tests pass.

---

## Task 2: Generate resolved quarter-note markers during chart finalization

**Estimated size:** 0.75 engineer-day

**Files:**

- Create: `DTXMania.Game/Lib/Song/Components/BeatMarker.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/ParsedChart.cs`
- Modify: `DTXMania.Test/Song/ChartTimingMapTests.cs`
- Modify: `DTXMania.Test/Song/ParsedChartTests.cs`

### Step 1: Add failing timing-map tests

Add focused `ChartTimingMapTests` for a marker-building seam. Use base BPM `120` so expected quarter-note times are easy to inspect.

Cover:

1. **Normal measure `1.0`** — marker times `0`, `500`, `1000`, `1500`; only the first is accented.
2. **Short measure `0.75`** — three markers at `0`, `500`, `1000`; next measure starts at `1500`.
3. **Long measure `1.5`** — six markers at `0`, `500`, `1000`, `1500`, `2000`, `2500`; next measure starts at `3000`.
4. **Non-integral musical length** — for multiplier `0.625`, emit offsets `0`, `1`, and `2` quarter notes without rounding the `2.5`-beat measure to two or three full beats.
5. **Boundary BPM change** — the new BPM applies to all markers after the boundary.
6. **Mid-measure BPM change** — later marker times integrate the new BPM rather than evenly dividing the measure.
7. **Two adjacent measures** — one accent at each bar start and no duplicate marker at the shared boundary.

Use precision-based assertions for `double` milliseconds.

### Step 2: Implement `BeatMarker`

Keep the runtime model minimal:

```csharp
public sealed class BeatMarker
{
    public double TimeMs { get; init; }
    public bool IsMeasureStart { get; init; }
}
```

Do not add BPM, bar, lane, display position, or scoring state.

### Step 3: Add fractional internal timing support

Extend `ChartTimingMap` with one internal method that builds markers through an inclusive occupied bar, for example:

```csharp
internal IReadOnlyList<BeatMarker> BuildBeatMarkers(int throughBar);
```

Implementation rules:

- Require the map to have already been rebuilt through the requested bar.
- Read the effective measure multiplier from the existing `_measureLengths` dictionary, defaulting to `1.0`.
- Compute `measureBeats = 4.0 * multiplier`.
- For integer `beatOffset = 0; beatOffset < measureBeats; beatOffset++`, compute:

```text
fractionalTick = beatOffset * 192.0 / measureBeats
```

- Resolve that fractional position using the last timing anchor at or before it.
- Mark only `beatOffset == 0` as a measure start.
- Never emit a marker at tick `192`; the next bar emits its own accent.

Generalize the private interval calculation from integer to `double tickDelta`. Keep `NormalizePosition` and the existing public authored-event lookup on integer bars/ticks. Fractional ticks are an internal metronome calculation only.

A narrow private helper such as `CalculateTimeMsWithinMeasure(int bar, double tick)` is enough. Do not introduce a generic fractional chart-position type.

### Step 4: Add failing ParsedChart finalization tests

Cover:

1. A chart with an event in bar zero publishes the expected bar-zero markers.
2. A chart whose highest event is in a later bar publishes markers only through that occupied bar.
3. The extra terminal `MeasureLine` does not create an extra metronome measure.
4. An empty chart has no markers.
5. Calling `FinalizeChart()` twice produces identical marker values and count.

### Step 5: Wire markers into `ParsedChart.FinalizeChart()`

Add a public read-only-by-convention collection matching the existing chart component style:

```csharp
public List<BeatMarker> BeatMarkers { get; } = new();
```

During finalization:

- clear `BeatMarkers` first;
- leave empty charts empty;
- after `TimingMap.Rebuild`, generate markers for bars `0..highestOccupiedBar`;
- keep measure-line generation through `highestOccupiedBar + 1` unchanged;
- do not change `ChartManager`.

### Step 6: Run focused tests

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~ChartTimingMapTests|FullyQualifiedName~ParsedChartTests"
```

Expected: all selected tests pass, including existing HPA-600 timing tests.

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
3. A due accented marker is forwarded once with `IsMeasureStart=true`.
4. The marker at time zero is played when the first update arrives within the late tolerance.
5. When several markers became due during one delayed frame, only the latest one is forwarded.
6. When the latest due marker is older than the late tolerance, it is skipped.
7. A second update at the same/later time does not replay an already consumed marker.
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

Do not add pause/resume methods, clock ownership, seeking, `IDisposable`, an interface, or an event. `PerformanceStage` already owns transport and resources.

### Step 3: Run focused tests

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~MetronomePlayerTests"
```

Expected: all scheduler tests pass without graphics or audio initialization.

---

## Task 4: Add click assets and integrate PerformanceStage

**Estimated size:** 0.75 engineer-day

**Files:**

- Modify: `DTXMania.Game/Lib/Resources/SoundPath.cs`
- Create binary asset: `System/CXNeon/Sounds/Metronome Beat.ogg`
- Create binary asset: `System/CXNeon/Sounds/Metronome Accent.ogg`
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Modify: `DTXMania.Test/Resources/ResourceManagerLogicTests.cs` or the existing sound-inventory test that owns `SoundPath.GetAllSoundPaths()` coverage
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageAdditionalCoverageTests.cs` only for lifecycle/error paths not suited to the deterministic test file

### Step 1: Add failing sound-inventory tests

Extend the existing `SoundPath` inventory assertion to require:

```text
Sounds/Metronome Beat.ogg
Sounds/Metronome Accent.ogg
```

Also verify that the bundled CXNeon files exist through the same repository/resource test pattern used for existing sounds. Do not add a new asset manifest.

### Step 2: Add the two sounds

Add constants and include them in `GetAllSoundPaths()`:

```csharp
public const string MetronomeBeat = "Sounds/Metronome Beat.ogg";
public const string MetronomeAccent = "Sounds/Metronome Accent.ogg";
```

Asset requirements:

- short mono or stereo OGG clips;
- dry transient with minimal tail;
- clearly distinguishable accent;
- normalized conservatively so repeated clicks do not dominate the song mix.

Do not reuse a drum chip or menu sound as the shipped metronome asset.

### Step 3: Add failing PerformanceStage tests

Use existing headless/reflection seams rather than constructing a real `GraphicsDevice`.

Cover these observable contracts:

1. With `MetronomeEnabled=false`, initialization does not request either click sound and creates no player.
2. With the setting enabled and a finalized chart, initialization creates a player using the chart markers.
3. The click callback selects accent versus regular sound and applies `SEVolume / 100f` clamped to `0..1`.
4. `UpdateGameplay` drives the player only from raw `currentTimeMs` while `_songTimer.IsPlaying`.
5. READY/loading/paused states do not advance the player.
6. Cleanup clears the player and balances references for both loaded sounds.
7. Failure to load one or both sounds leaves gameplay usable and the available click type still works.
8. Restart/re-activation begins from marker zero.

Prefer a small overridable sound-loading or one-shot-play seam on `PerformanceStage` if existing tests cannot observe calls. Do not introduce a project-wide audio service for this feature.

### Step 4: Implement stage-owned resources

Add nullable fields for:

- frozen `_metronomeEnabled`;
- frozen `_metronomeVolume`;
- regular `ISound`;
- accent `ISound`;
- `MetronomePlayer`.

Freeze the setting and volume in `OnActivate`:

```text
enabled = config.MetronomeEnabled
volume = clamp(config.SEVolume / 100f)
```

Create the player only after the chart is finalized and published by the existing asynchronous initialization path. Load sounds only when enabled.

Keep loading best-effort. Catch and log through the existing performance/resource diagnostics, leaving failed sounds null.

The callback should:

- choose accent or regular by `marker.IsMeasureStart`;
- no-op when that sound is unavailable;
- submit a fire-and-forget one-shot at the frozen volume;
- catch playback exceptions and log without changing stage state.

Use `SoundEffect.Play(volume, pitch: 0f, pan: 0f)` through the loaded `ISound.SoundEffect` for the short fire-and-forget click. This uses MonoGame's one-shot path and avoids accumulating caller-owned `SoundEffectInstance` objects. Do not add a second active-instance collection solely for metronome clicks.

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

Place it beside `ProcessBGMEvents(currentTimeMs)` and before creation/use of `playerJudgementTimeMs` so the raw-clock dependency is obvious.

Do not call it from the compensated judgement path, `SongTimer.Update`, draw methods, or a separate timer.

### Step 6: Complete cleanup

In the existing serialized audio cleanup path:

- clear `_metronomePlayer`;
- `RemoveReference()` on each non-null loaded click sound exactly once;
- null the fields;
- ensure cancellation/deactivation during asynchronous initialization cannot publish resources after cleanup, using the stage's existing activation-generation/audio-lifecycle gate.

Do not build a second cancellation mechanism.

### Step 7: Run focused tests

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~SoundPath|FullyQualifiedName~ResourceManagerLogicTests|FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~PerformanceStageAdditionalCoverageTests"
```

Expected: all selected tests pass.

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

Expected: successful build with no new warnings attributable to HPA-13.

### Step 3: Perform focused manual checks

Use small local DTX fixtures or existing timing-map fixtures for each case:

1. **Disabled:** no click assets are loaded/audible.
2. **Normal 4/4 at 120 BPM:** four clicks per measure, first accented.
3. **Channel `02` 0.75:** three evenly spaced quarter-note clicks before the next accent.
4. **Channel `02` 1.5:** six quarter-note clicks before the next accent.
5. **Channel `03` or `08` mid-measure tempo change:** later clicks change spacing without measure drift.
6. **Play Speed 0.50x and 2.00x:** clicks stay aligned with notes/BGM.
7. **Pause/resume:** silence while paused, then no duplicate or burst on resume.
8. **Restart/re-enter:** first downbeat accents again.
9. **Missing regular or accent asset:** gameplay continues with the remaining click type or silence.

### Step 4: Review the final diff against scope

Confirm the implementation contains only:

- one persisted toggle;
- one beat-marker model/list;
- one timing-map marker builder;
- one pure scheduler;
- two sound assets/constants;
- focused `PerformanceStage` integration and tests.

Remove any count-in, subdivision, visual, hotkey, practice-loop, new audio-service, or generalized transport work before requesting review.
