# HPA-407 DTX Measure-Length Carry-Forward Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make channel `02` measure lengths persist until changed for non-BMS-family charts (DTX plus any direct `ParseAsync` suffix) while preserving per-bar reset semantics for `.bms`/`.bme`/`.bml`. Real GDA/G2D charts are unaffected today — CX parses hex channel codes only — so the PR claims DTX compatibility only (GDA channel parsing is a follow-up recorded on HPA-407).

**Architecture:** Keep the existing `ChartTimingMap` as the only timing compiler. Add one internal carry-forward behavior flag, configure it once from the parser's source extension, and let `ParsedChart.FinalizeChart()` continue consuming the compiled map unchanged.

**Tech Stack:** C# / .NET 8, xUnit, existing DTXManiaCX song parser and timing-map tests.

**Spec:** `docs/superpowers/specs/2026-10-16-hpa-407-measure-length-carry-forward-design.md`

## Global Constraints

- One PR for HPA-407; do not split implementation into multiple PRs.
- Do not add a chart-format subsystem or public API for this one-bit behavior.
- Do not change channel `03`/`08`, note parsing, rendering, audio, judgement, or persistence.
- `.dtx`, `.gda`, `.g2d` carry the last valid measure multiplier forward.
- `.bms`, `.bme`, `.bml` reset missing-bar multipliers to `1.0` (case-insensitive extension match; `.bml` is a deliberate CX deviation — see spec).
- Only the BMS-family extensions reset: other suffixes passed directly to `ParseAsync` carry forward. Do not change the library scanner's supported-extension list.
- Invalid/non-positive/non-finite `02` values keep existing ignored-value behavior.
- Do not claim GDA/G2D compatibility in the PR: CX drops real GDA two-letter channel codes (`#000BL:` etc.) today; the `.gda`/`.g2d` tests pin the extension predicate only.

## Review Focus

- A DTX-family chart with one `02` row must keep that multiplier across multiple missing bars.
- A later valid `02` in a DTX-family chart must replace the carried value from that bar onward.
- BMS/BME/BML must not accidentally inherit a prior bar's multiplier.
- Direct `ParseAsync` calls for any other suffix use the DTX-style carry-forward fallback.
- Bars before the first DTX-family `02` remain at `1.0`.
- Existing tempo-change integration must continue composing with the effective measure multiplier.
- An uppercase extension (`.BMS`, `.DTX`) selects the same semantics as its lowercase form.

---

### Task 1: Add format-neutral carry-forward behavior to `ChartTimingMap`

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs`
- Modify: `DTXMania.Test/Song/ChartTimingMapTests.cs`

**Interfaces:**
- Produces: `internal bool CarryMeasureLengthsForward` on `ChartTimingMap`, defaulting to `true`, whose setter calls `InvalidateAnchors()` like `SetMeasureLength`/`SetTempoChange` do.
- Existing `SetMeasureLength(...)`, `Rebuild(...)`, `CalculateTimeMs(...)`, and `GetMeasureLengthMultiplier(...)` signatures remain unchanged.

- [ ] **Step 1: Add failing carry-forward coverage**

Add a focused test that applies `SetMeasureLength(0, 0.75)`, rebuilds through bar 3, and directly asserts:

```csharp
Assert.Equal(0.75, map.GetMeasureLengthMultiplier(1));
Assert.Equal(0.75, map.GetMeasureLengthMultiplier(2));
```

Rebuild through bar 3 (not 2) so the override test below stays queryable — `GetMeasureLengthMultiplier` throws for bars beyond the compiled horizon.

Also assert the corresponding `CalculateTimeMs(...)` result so both the compiled multiplier-anchor contract and elapsed timing are pinned.

Add a second assertion/test with a later explicit multiplier distinct from the reset value (`SetMeasureLength(2, 1.5)`): assert bar 2 resolves to `1.5` and bar 3 also carries `1.5`, proving the carried value stops at the override *and* the replacement itself carries on. An override of `1.0` cannot fail a broken implementation.

Update the existing default behavior test (`GetMeasureLengthMultiplier_ShouldReadCompiledBarStartAnchor`) to set `CarryMeasureLengthsForward = false` explicitly; it keeps asserting an unspecified next bar uses `1.0` and now pins the reset branch under the new carry-forward default.

- [ ] **Step 2: Run focused timing-map tests and verify the new carry-forward case fails**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter FullyQualifiedName~ChartTimingMapTests
```

Expected before implementation: the new/updated tests fail (they reference `CarryMeasureLengthsForward`, which does not exist yet — the compile failure is the red phase; after adding the property with the not-yet-carrying `Rebuild`, the carry assertions fail because bar 1 resolves to `1.0`).

- [ ] **Step 3: Implement minimal stateful multiplier selection in `Rebuild`**

Add the flag as a property whose setter invalidates compiled anchors, defaulting to carry-forward so the timing-map default agrees with the parser's default (BMS-family formats are the only ones that opt out):

```csharp
private bool _carryMeasureLengthsForward = true;

internal bool CarryMeasureLengthsForward
{
    get => _carryMeasureLengthsForward;
    set
    {
        _carryMeasureLengthsForward = value;
        InvalidateAnchors();
    }
}
```

The setter must invalidate like `SetMeasureLength`/`SetTempoChange`, so flipping the flag after a rebuild can never silently leave stale anchors.

Inside `Rebuild`, maintain one active measure multiplier initialized to `1.0`:

- explicit configured value -> replace active multiplier;
- missing value + carry disabled -> reset active multiplier to `1.0`;
- missing value + carry enabled -> retain active multiplier.

Do not alter tempo-change ordering or anchor lookup logic.

- [ ] **Step 4: Run focused timing-map tests**

Run the same filtered command.

Expected: all `ChartTimingMapTests` pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs DTXMania.Test/Song/ChartTimingMapTests.cs
git commit -m "fix: support persistent DTX measure lengths"
```

---

### Task 2: Configure measure-length semantics from the parser and pin all supported extensions

**Files:**
- Modify: `DTXMania.Game/Lib/Song/DTXChartParser.cs`
- Modify: `DTXMania.Test/Song/DTXChartParserTests.cs`

**Interfaces:**
- Consumes: `ChartTimingMap.CarryMeasureLengthsForward` from Task 1.
- Produces: parser selection where only `.bms`/`.bme`/`.bml` set the flag to `false`; `.dtx`/`.gda`/`.g2d` and other direct-parser paths set it to `true`.

- [ ] **Step 1: Generalize the timing fixture helper and add failing parser theories**

Change `ParseTimingFixtureAsync` so callers may provide an extension, defaulting to `.dtx`.

Add one theory for `.dtx`, `.gda`, `.g2d` using content equivalent to:

```text
#BPM:120
#00002:0.75
#00211:01
```

Assert the bar-2 note resolves to `3000 ms` and the bar-2 measure boundary matches it. Include one direct-parser fixture with an unlisted extension (e.g. `.txt`) asserting the same result; do not add it to `SupportedExtensions`.

The `.gda`/`.g2d` inline data pin the extension predicate only — real GDA charts use non-hex channel codes CX does not parse yet (follow-up on HPA-407; do not claim GDA compatibility in the PR).

Add one theory for `.bms`, `.bme`, `.bml` with the same content and assert the bar-2 note/boundary resolves to `3500 ms`.

Include at least one uppercase case in each family (e.g. `.BMS` resets, `.DTX` carries) so a future regression of the `ToLowerInvariant()` extension check fails a test.

Add one focused `.dtx` composition fixture proving a carried multiplier still applies when a later bar contains a mid-bar tempo change:

```text
#BPM:120
#00002:0.75
#00103:0060
#00211:01
```

Channel `03` value `60` is hexadecimal 96 BPM at the halfway tick of bar 1. Assert the bar-2 note resolves to `3187.5 ms` (`1500 + 750 + 937.5`), which pins both carry-forward and tempo integration together.

Name all new parser test methods with `Measure` so the focused filter in Step 2 discovers them.

- [ ] **Step 2: Run focused parser timing tests and verify DTX-family cases fail**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DTXChartParserTests&FullyQualifiedName~Measure"
```

The filter already matches three pre-existing tests (`ShortMeasureLength`, `ExtendedMeasureLength`, `...MeasureLines`), so a zero-test run is not a meaningful guard. Instead verify each new test method appears by name in the run output; a missing new test is a failure of this verification step. If test discovery differs by environment, run the whole `DTXChartParserTests` class instead.

Expected before parser wiring: DTX-family held-length cases, including the carry-plus-tempo composition and unlisted-extension cases, fail; BMS-family reset cases remain green.

- [ ] **Step 3: Configure the timing map from the source extension**

In `DTXChartParser.ParseAsync`, compute the extension decision **once per call, before the encoding-retry loop** (the loop recreates `ParsedChart` per attempt and must not recompute the suffix check). Add a small static predicate next to `_supportedExtensions`/`IsSupportedFile`:

```csharp
private static bool ResetsMeasureLengthsPerBar(string filePath) =>
    Path.GetExtension(filePath).ToLowerInvariant() is ".bms" or ".bme" or ".bml";
```

Then, immediately after each fresh `ParsedChart(filePath)` is created inside the loop:

```csharp
chart.TimingMap.CarryMeasureLengthsForward = !ResetsMeasureLengthsPerBar(filePath);
```

This follows NX's default and handles direct `ParseAsync` calls on unlisted suffixes; it does **not** change the song library's supported extensions. Do not add a new enum or move extension parsing into `ParsedChart`.

- [ ] **Step 4: Run parser and timing-map tests**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DTXChartParserTests|FullyQualifiedName~ChartTimingMapTests"
```

Expected: all focused tests pass.

- [ ] **Step 5: Run the full test suite**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: PASS with no new failures. The full `DTXMania.Test` suite (Windows-only tests) runs on Windows CI; the implementation PR must be green there before merge — local Mac cannot run it.

- [ ] **Step 6: Check the diff**

Run:

```bash
git diff --check
git diff --stat main...
```

Confirm production changes are limited to `ChartTimingMap.cs` and `DTXChartParser.cs`, plus focused tests. No tab-mode or unrelated timing changes belong in this PR.

- [ ] **Step 7: Commit**

```bash
git add DTXMania.Game/Lib/Song/DTXChartParser.cs DTXMania.Test/Song/DTXChartParserTests.cs
git commit -m "fix: carry DTX measure lengths across bars"
```

## PR / Release Note

The implementation PR body must call out the user-visible correction:

> DTX charts now keep `#xxx02` measure lengths active until the next `02` row, matching DTXManiaNX/DTXCreator behavior. Timing may shift for charts that previously relied on CX's incorrect one-bar reset. BMS/BME/BML keep their existing per-bar reset behavior. Real GDA/G2D charts are unaffected (CX does not parse their channel codes yet; follow-up recorded on HPA-407).

No separate changelog file is required for HPA-407 unless the repository's release process introduces one before implementation.