# Unit Test Coverage to 90% — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raise line coverage on `DTXMania.Game.Mac` / `DTXMania.Game` from 81.92% to ≥ 90% via a single sweeping PR.

**Architecture:** Three-pillar strategy — (1) direct xUnit + Moq tests for testable logic, (2) `[ExcludeFromCodeCoverage]` on genuinely graphics-bound methods, (3) targeted extraction of testable logic out of the four worst offenders (`SongStatusPanel`, `SongBarRenderer`, `PerformanceStage`, `SongSelectionStage`). The Windows full suite is the gating coverage metric.

**Tech Stack:** .NET 8, MonoGame 3.8, xUnit, Moq, Coverlet, ReportGenerator (optional, for local viewing).

**Spec:** [`docs/superpowers/specs/2026-05-14-unit-test-coverage-90-design.md`](../specs/2026-05-14-unit-test-coverage-90-design.md)

---

## Conventions Used Throughout This Plan

- **Test files** live under `DTXMania.Test/<MirrorOfSourceDir>/<ClassName>CoverageTests.cs` (or `<ClassName>Tests.cs` when there is no existing file). Mirror the source directory layout.
- **Test method naming:** `Scenario_ShouldExpect` (e.g., `GetAppDataRoot_OnLinux_ShouldReturnXdgConfigDir`).
- **Tests requiring `GraphicsDevice`** must be excluded from `DTXMania.Test/DTXMania.Test.Mac.csproj` by adding the file path to the `<Compile Include>` exclusion block. See CLAUDE.md for the existing list.
- **`[ExcludeFromCodeCoverage]`** uses `using System.Diagnostics.CodeAnalysis;` and is always applied at the **method level**, with a one-line XML comment such as `/// <summary>Pure draw method; no logic to assert.</summary>`. Never apply at the class level.
- **Commit style:** Conventional Commits (`test:`, `refactor:`, `chore:`), subject under 72 chars, imperative mood. Frequent commits — one per task minimum.
- **Verification command (Mac):**
  ```bash
  dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
    --collect:"XPlat Code Coverage" \
    --settings coverlet.runsettings \
    --results-directory ./TestResults
  ```
- **Coverage rate command (Mac):** after running tests, parse the freshest `coverage.cobertura.xml`:
  ```bash
  python3 -c "import xml.etree.ElementTree as ET, glob, os; \
    f=max(glob.glob('TestResults/*/coverage.cobertura.xml'), key=os.path.getmtime); \
    r=ET.parse(f).getroot(); \
    print(f'{f}: line={float(r.get(\"line-rate\"))*100:.2f}% branch={float(r.get(\"branch-rate\"))*100:.2f}%')"
  ```

---

## Phase 0 — Baseline & Tooling

### Task 1: Capture Mac coverage baseline

**Files:**
- No file changes; this records the starting point.

- [ ] **Step 1: Run the Mac test suite with coverage**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --collect:"XPlat Code Coverage" \
  --settings coverlet.runsettings \
  --results-directory ./TestResults
```

Expected: all tests pass.

- [ ] **Step 2: Record the baseline**

```bash
python3 -c "import xml.etree.ElementTree as ET, glob, os; \
  f=max(glob.glob('TestResults/*/coverage.cobertura.xml'), key=os.path.getmtime); \
  r=ET.parse(f).getroot(); \
  print(f'BASELINE-MAC: line={float(r.get(\"line-rate\"))*100:.2f}% branch={float(r.get(\"branch-rate\"))*100:.2f}% lines={r.get(\"lines-covered\")}/{r.get(\"lines-valid\")}')"
```

Expected output similar to: `BASELINE-MAC: line=81.92% branch=78.16% lines=18285/22320`.

Save this output to a scratch note (do not commit). It is the reference point for verifying progress.

- [ ] **Step 3: Identify per-file gaps**

```bash
python3 <<'EOF'
import xml.etree.ElementTree as ET, glob, os
f=max(glob.glob('TestResults/*/coverage.cobertura.xml'), key=os.path.getmtime)
root=ET.parse(f).getroot()
fd={}
for cls in root.iter('class'):
    fn=cls.get('filename')
    lv=lc=0
    for ln in cls.iter('line'):
        lv+=1
        if int(ln.get('hits','0'))>0: lc+=1
    if fn not in fd: fd[fn]=[0,0]
    fd[fn][0]+=lc; fd[fn][1]+=lv
for fn,(lc,lv) in sorted(fd.items(), key=lambda x:-(x[1][1]-x[1][0]))[:30]:
    if lv: print(f'{fn:<80} {lc/lv*100:6.1f}% missing={lv-lc:>5}')
EOF
```

Save this list to a scratch note. It tells you which files to target during Phase 4.

### Task 2: Capture Windows coverage baseline

**Files:**
- No file changes; this confirms the gating-metric starting point.

- [ ] **Step 1: Locate the latest Windows CI coverage**

Open GitHub Actions, find the most recent successful run of `.github/workflows/build-and-test.yml` on `main`. The Windows job uploads coverage to codecov; record the codecov line-rate for `DTXMania.Game` on the gating metric.

- [ ] **Step 2: If codecov is unavailable, run locally on a Windows machine**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura \
  /p:CoverletOutput=./TestResults/windows-baseline.cobertura.xml
```

- [ ] **Step 3: Decide scope adjustment**

- If Windows ≥ 90% already, narrow this plan: skip exclusions/tests for files where Windows alone is sufficient, and focus on what is needed for Mac.
- If Windows < 82%, expand the plan as needed — note any Windows-only files that need extra tests.
- Save the Windows baseline number alongside the Mac baseline.

- [ ] **Step 4: No commit**

This task produces a note only; nothing is committed.

---

## Phase 1 — Pillar 1: Low-risk tests for testable logic

Each task in this phase follows the same recipe:
1. Inspect the target file to find uncovered behaviors (use the per-file gap list from Task 1 Step 3 + `grep -n` to locate methods).
2. Write `Scenario_ShouldExpect` tests covering specific uncovered behaviors.
3. Run tests; verify pass.
4. Re-run coverage; confirm rate increased for that file.
5. Commit.

### Task 3: Cover `AppPaths` cross-platform branches

**Files:**
- Modify: `DTXMania.Test/Utilities/AppPathsTests.cs` (create if missing)
- Source under test: `DTXMania.Game/Lib/Utilities/AppPaths.cs` (currently 74.2%, missing 50)

- [ ] **Step 1: Write failing tests for OS-branch fallback paths**

Create `DTXMania.Test/Utilities/AppPathsTests.cs`. Cover at least these cases. If a directory already exists, add only the missing scenarios.

```csharp
using System;
using System.IO;
using DTXMania.Game.Lib.Utilities;
using Xunit;

namespace DTXMania.Test.Utilities
{
    public class AppPathsTests
    {
        [Fact]
        public void GetAppDataRoot_ShouldReturnAbsolutePath()
        {
            var root = AppPaths.GetAppDataRoot();
            Assert.False(string.IsNullOrWhiteSpace(root));
            Assert.True(Path.IsPathRooted(root));
            Assert.Contains("DTXManiaCX", root);
        }

        [Fact]
        public void GetAppDataRoot_OnSecondCall_ShouldReturnSameValue()
        {
            var a = AppPaths.GetAppDataRoot();
            var b = AppPaths.GetAppDataRoot();
            Assert.Equal(a, b);
        }

        [Fact]
        public void GetAppDataRoot_ShouldEnsureDirectoryCreatable()
        {
            var root = AppPaths.GetAppDataRoot();
            Assert.True(Directory.Exists(root) || Directory.CreateDirectory(root).Exists);
        }
    }
}
```

Also add tests for any other public methods on `AppPaths` (e.g., subdirectory helpers, environment-variable handling). Inspect the file with `grep -n "public " DTXMania.Game/Lib/Utilities/AppPaths.cs` to enumerate the public surface.

- [ ] **Step 2: Run the new tests**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~AppPathsTests"
```

Expected: all new tests pass.

- [ ] **Step 3: Re-run coverage**

Use the verification + rate commands from "Conventions Used Throughout This Plan". `AppPaths.cs` should be at ≥ 90%.

- [ ] **Step 4: Commit**

```bash
git add DTXMania.Test/Utilities/AppPathsTests.cs
git commit -m "test: cover AppPaths cross-platform branches"
```

### Task 4: Cover `ResourceManager` edge cases

**Files:**
- Modify: `DTXMania.Test/Resources/ResourceManagerCoverageTests.cs` (create if missing)
- Source under test: `DTXMania.Game/Lib/Resources/ResourceManager.cs` (currently 86.8%, missing 132)

- [ ] **Step 1: Identify uncovered branches**

Inspect `ResourceManager.cs` for these behaviors that are commonly under-tested: cache-eviction, AddReference/RemoveReference symmetry, dispose on shutdown, missing-resource error paths, statistics counter updates, skin-fallback chain miss. Use `git blame` and `grep -n` to find them.

- [ ] **Step 2: Add tests**

Write tests targeting each uncovered behavior. Use Moq to inject `IGraphicsDevice` and skin discovery stubs where needed. Example pattern (adapt to actual constructor signature):

```csharp
[Fact]
public void RemoveReference_WhenCountReachesZero_ShouldDisposeUnderlyingResource()
{
    var mockDevice = new Mock<IGraphicsDevice>();
    var mgr = new ResourceManager(mockDevice.Object, /* skinManager */ ...);
    var key = "test.png";
    var first = mgr.LoadTexture(key);
    mgr.RemoveReference(key);
    Assert.False(mgr.TryGetTexture(key, out _));
}

[Fact]
public void LoadTexture_WhenFileMissing_ShouldReturnFallbackOrThrow()
{
    var mgr = MakeResourceManager();
    Action act = () => mgr.LoadTexture("does_not_exist.png");
    Assert.Throws<FileNotFoundException>(act);  // or assert fallback texture, depending on actual behavior
}
```

The exact assertions follow the file's existing behavior. Read the source first; do **not** invent behavior. If a test seems to encode a new behavior, stop and re-check the source.

- [ ] **Step 3: Run & verify**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ResourceManagerCoverageTests"
```

- [ ] **Step 4: Re-run coverage; `ResourceManager.cs` should be ≥ 94%.**

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Test/Resources/ResourceManagerCoverageTests.cs
git commit -m "test: cover ResourceManager reference-count and miss paths"
```

### Task 5: Cover `JsonRpcServer` error paths

**Files:**
- Modify: `DTXMania.Test/JsonRpc/JsonRpcServerCoverageTests.cs` (create if missing)
- Source under test: `DTXMania.Game/Lib/JsonRpc/JsonRpcServer.cs` (currently 91.9%, missing 84)

- [ ] **Step 1: Inspect the file for uncovered branches**

Run `grep -n "catch\|throw\|if (" DTXMania.Game/Lib/JsonRpc/JsonRpcServer.cs` and cross-reference with the cobertura per-line data to find the gaps. Typical untested paths: malformed JSON, missing `method` field, unknown method, request without `id` (notifications), oversized payloads, server-shutdown mid-request.

- [ ] **Step 2: Add tests using `JsonRpcServer` in-process**

```csharp
[Fact]
public async Task HandleRequest_WhenJsonMalformed_ShouldReturnParseError()
{
    var server = new JsonRpcServer(/* args */);
    var response = await server.HandleRequestAsync("{not valid json");
    var doc = JsonDocument.Parse(response);
    Assert.Equal(-32700, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
}

[Fact]
public async Task HandleRequest_WhenMethodMissing_ShouldReturnInvalidRequest()
{
    var server = new JsonRpcServer(/* args */);
    var response = await server.HandleRequestAsync("""{"jsonrpc":"2.0","id":1}""");
    var doc = JsonDocument.Parse(response);
    Assert.Equal(-32600, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
}
```

Adjust to actual public API. If the parse path is internal, expose via `InternalsVisibleTo` or test through `HandleRequestAsync` exclusively.

- [ ] **Step 3: Run & verify**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~JsonRpcServerCoverageTests"
```

- [ ] **Step 4: Re-run coverage; `JsonRpcServer.cs` should be ≥ 96%.**

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Test/JsonRpc/JsonRpcServerCoverageTests.cs
git commit -m "test: cover JsonRpcServer error and edge paths"
```

### Task 6: Cover `ConfigStage` non-Draw logic

**Files:**
- Modify: `DTXMania.Test/Stage/ConfigStageCoverageTests.cs` (already exists)
- Source under test: `DTXMania.Game/Lib/Stage/ConfigStage.cs` (currently 86.1%, missing 132)

- [ ] **Step 1: Identify uncovered branches**

`ConfigStage` already has 4 `[ExcludeFromCodeCoverage]` attributes. Find the remaining gaps in **non-Draw** methods: menu navigation (`Update` branch on key inputs), config save/load handlers, validation logic.

- [ ] **Step 2: Add tests using existing test patterns**

Reuse the patterns already in `ConfigStageCoverageTests.cs`. Add new test methods covering the gaps; do not refactor existing tests.

- [ ] **Step 3: Run & verify**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigStageCoverageTests"
```

- [ ] **Step 4: Re-run coverage; `ConfigStage.cs` should be ≥ 92%.**

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Test/Stage/ConfigStageCoverageTests.cs
git commit -m "test: cover ConfigStage navigation and validation"
```

### Task 7: Cover `SkinManager` fallback chain

**Files:**
- Modify: `DTXMania.Test/Resources/SkinManagerCoverageTests.cs` (create if missing)
- Source under test: `DTXMania.Game/Lib/Resources/SkinManager.cs` (currently 78.6%, missing 60)

- [ ] **Step 1: Add tests for fallback resolution**

```csharp
[Fact]
public void ResolveTexturePath_WhenInCurrentSkin_ShouldReturnCurrentSkinPath()
{
    var fs = MakeFakeFs(new[] { "/skins/MySkin/Graphics/foo.png" });
    var mgr = new SkinManager(fs, currentSkin: "MySkin");
    var resolved = mgr.ResolveTexturePath("Graphics/foo.png");
    Assert.EndsWith("/skins/MySkin/Graphics/foo.png", resolved);
}

[Fact]
public void ResolveTexturePath_WhenMissingFromCurrent_ShouldFallBackToSystem()
{
    var fs = MakeFakeFs(new[] { "/System/Graphics/foo.png" });
    var mgr = new SkinManager(fs, currentSkin: "MySkin");
    var resolved = mgr.ResolveTexturePath("Graphics/foo.png");
    Assert.EndsWith("/System/Graphics/foo.png", resolved);
}

[Fact]
public void ResolveTexturePath_WhenMissingEverywhere_ShouldReturnNullOrEmpty()
{
    var fs = MakeFakeFs(System.Array.Empty<string>());
    var mgr = new SkinManager(fs, currentSkin: "MySkin");
    var resolved = mgr.ResolveTexturePath("Graphics/nope.png");
    Assert.True(string.IsNullOrEmpty(resolved));
}
```

`MakeFakeFs` is a local helper using `Moq<IFileSystem>` (or whatever filesystem abstraction `SkinManager` accepts). If it accepts no abstraction, use `Path.GetTempPath()` + directory setup/teardown.

- [ ] **Step 2: Run & verify**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SkinManagerCoverageTests"
```

- [ ] **Step 3: Re-run coverage; `SkinManager.cs` should be ≥ 92%.**

- [ ] **Step 4: Commit**

```bash
git add DTXMania.Test/Resources/SkinManagerCoverageTests.cs
git commit -m "test: cover SkinManager fallback chain"
```

---

## Phase 2 — Pillar 3: Extract testable logic from worst offenders

Each extraction follows this strict process:

1. **Write characterization tests against the new class** — encoding the existing behavior. The new class doesn't exist yet; tests fail to compile. That's the failing-test state.
2. **Create the new class** — moving the relevant methods/state out of the original file. Match signatures exactly so the original site can call into it.
3. **Re-point the original site** — the original methods become thin shims that delegate, or are deleted entirely and call sites updated.
4. **Run the full test suite** — every existing test must still pass. If anything fails, the extraction is not pure — fix and re-verify.
5. **Commit refactor + tests together.**

Pure-extraction rule: no behavior changes. If a method has a bug, leave it. The extracted class should produce identical output to the original code for identical inputs.

### Task 8: Extract `SongStatusPanelLogic`

**Files:**
- Create: `DTXMania.Game/Lib/Song/Components/SongStatusPanelLogic.cs`
- Create: `DTXMania.Test/Song/Components/SongStatusPanelLogicTests.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/SongStatusPanel.cs`

**What moves** (read `SongStatusPanel.cs` to confirm before moving):

- Per-difficulty selection state (`_currentDifficulty`, `UpdateSongInfo`, `GetCurrentScore`, `GetInstrumentFromDifficulty`).
- Layout math (computed positions inside `DrawDifficultyGrid`, `DrawDifficultyCell` for cell coords).
- Score-display formatting (computed strings inside `DrawSongInfo`, `DrawDTXManiaNXLayout`, `DrawSimplifiedInfo`, `DrawSongTypeInfo`, `DrawNotesCounter`).
- BPM origin calculation (`GetBpmBackgroundOrigin`).
- Decisions inside `DrawNoteDistributionBars`, `DrawProgressBar` (the arithmetic that produces bar widths/positions).

**What stays in `SongStatusPanel.cs`:**

- The actual `SpriteBatch.Draw` / `_renderer.Draw` calls.
- `OnDraw`, all `DrawXxx` methods reduced to: call `SongStatusPanelLogic.ComputeXxx()`, then draw using the result.

- [ ] **Step 1: Sketch the new class API**

In the new file, define the public surface based on what the original code computes. Approximate shape:

```csharp
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Xna.Framework;

namespace DTXMania.Game.Lib.Song.Components
{
    /// <summary>
    /// Pure logic for SongStatusPanel — layout calculations, score formatting,
    /// difficulty selection, and value derivation. No graphics device required.
    /// </summary>
    public class SongStatusPanelLogic
    {
        public int CurrentDifficulty { get; private set; }
        public SongListNode CurrentSong { get; private set; }

        public void UpdateSongInfo(SongListNode song, int difficulty) { /* ... */ }
        public SongScore GetCurrentScore(SongListNode song, int difficulty) { /* ... */ }
        public string GetInstrumentFromDifficulty(int difficulty) { /* ... */ }
        public Vector2 GetBpmBackgroundOrigin(bool useStandalone) { /* ... */ }
        public Rectangle GetDifficultyCellRect(Rectangle bounds, int gridRow, int instrumentColumn) { /* ... */ }
        public string FormatBpm(double bpm) { /* ... */ }
        public string FormatNotesCount(SongChart chart) { /* ... */ }
        // ... add others as you identify them in the source
    }
}
```

- [ ] **Step 2: Write failing characterization tests**

```csharp
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song.Components
{
    public class SongStatusPanelLogicTests
    {
        [Fact]
        public void UpdateSongInfo_WithValidInputs_ShouldSetCurrentSongAndDifficulty()
        {
            var logic = new SongStatusPanelLogic();
            var song = new SongListNode { Title = "Test" };
            logic.UpdateSongInfo(song, 2);
            Assert.Same(song, logic.CurrentSong);
            Assert.Equal(2, logic.CurrentDifficulty);
        }

        [Fact]
        public void GetInstrumentFromDifficulty_ShouldMapDifficultyToInstrumentName()
        {
            var logic = new SongStatusPanelLogic();
            // Encode actual behavior from current implementation in SongStatusPanel.cs:1268
            Assert.Equal(/* expected */, logic.GetInstrumentFromDifficulty(0));
            // ... one test per difficulty value handled by the switch/branch
        }

        // Add at least one test for each public method on SongStatusPanelLogic.
        // Each test must encode behavior already present in SongStatusPanel.cs.
    }
}
```

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongStatusPanelLogicTests"`. Expected: build fails (class doesn't exist yet).

- [ ] **Step 3: Implement `SongStatusPanelLogic`**

Move the logic from `SongStatusPanel.cs` into the new file. For each method:
- Open the original method in `SongStatusPanel.cs`.
- Extract the pure-logic portion (e.g., the math that computes a `Rectangle`, the formatting that produces a `string`).
- Place it in `SongStatusPanelLogic` with the same input types.
- The original drawing code calls the new method, then passes the result to `SpriteBatch.Draw`.

- [ ] **Step 4: Re-point `SongStatusPanel.cs`**

Each affected method shrinks. Example shape for `DrawDifficultyCell`:

```csharp
private void DrawDifficultyCell(SpriteBatch spriteBatch, int x, int y, int gridRow, int instrument, ChartLevelInfo chartInfo)
{
    var bounds = GetCachedBounds();
    var cellRect = _logic.GetDifficultyCellRect(bounds, gridRow, instrument);
    spriteBatch.Draw(_difficultyFrameTexture.Texture, cellRect, Color.White);
    DrawDifficultyCellContent(spriteBatch, cellRect.X, cellRect.Y, cellRect.Width, cellRect.Height, gridRow, instrument, chartInfo);
}
```

Add a `private readonly SongStatusPanelLogic _logic = new();` field on `SongStatusPanel`. Where the panel used to read its own `_currentDifficulty`, it now reads `_logic.CurrentDifficulty`.

- [ ] **Step 5: Run the full Mac suite**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: ALL existing tests pass + new `SongStatusPanelLogicTests` pass. If any existing test fails, the extraction is not pure. Diff what changed and fix.

- [ ] **Step 6: Re-run coverage**

`SongStatusPanel.cs` should drop in absolute line count (logic moved out). `SongStatusPanelLogic.cs` is added with ≥ 95% coverage. Overall coverage rate should rise by 2–4 percentage points.

- [ ] **Step 7: Commit**

```bash
git add DTXMania.Game/Lib/Song/Components/SongStatusPanelLogic.cs \
        DTXMania.Game/Lib/Song/Components/SongStatusPanel.cs \
        DTXMania.Test/Song/Components/SongStatusPanelLogicTests.cs
git commit -m "refactor: extract SongStatusPanelLogic for testability"
```

### Task 9: Extract `SongBarRenderLogic`

**Files:**
- Create: `DTXMania.Game/Lib/Song/Components/SongBarRenderLogic.cs`
- Create: `DTXMania.Test/Song/Components/SongBarRenderLogicTests.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/SongBarRenderer.cs`

**What moves:** bar layout (Y coordinate per index, fade-in/scroll math), label truncation (string length given a max-pixel width), color selection (per song type / difficulty / state), and any decision logic embedded in current rendering methods.

**What stays:** the `SpriteBatch.Draw` calls and font draws themselves.

- [ ] **Step 1: Sketch the new class API**

```csharp
namespace DTXMania.Game.Lib.Song.Components
{
    public class SongBarRenderLogic
    {
        public int GetBarY(int barIndex, int selectedIndex, int barHeight, float scrollOffset) { /* ... */ }
        public string TruncateLabel(string label, int maxPixelWidth, int charWidthEstimate) { /* ... */ }
        public Color GetBarColor(SongListNode node, bool isSelected) { /* ... */ }
        // Add methods to mirror each logic block in SongBarRenderer.cs
    }
}
```

- [ ] **Step 2: Write failing characterization tests**

```csharp
public class SongBarRenderLogicTests
{
    [Fact]
    public void GetBarY_AtSelectedIndex_ShouldReturnSelectedRowCoordinate()
    {
        var logic = new SongBarRenderLogic();
        var y = logic.GetBarY(barIndex: 5, selectedIndex: 5, barHeight: 40, scrollOffset: 0f);
        Assert.Equal(/* expected from current code */, y);
    }

    [Fact]
    public void TruncateLabel_WhenFitsInWidth_ShouldReturnInputUnchanged()
    {
        var logic = new SongBarRenderLogic();
        Assert.Equal("hello", logic.TruncateLabel("hello", maxPixelWidth: 200, charWidthEstimate: 10));
    }

    [Fact]
    public void TruncateLabel_WhenExceedsWidth_ShouldAppendEllipsisOrTruncate()
    {
        var logic = new SongBarRenderLogic();
        var result = logic.TruncateLabel("hello world", maxPixelWidth: 30, charWidthEstimate: 10);
        Assert.True(result.Length <= "hello".Length);
    }

    // Add one test per behavior identified in the source.
}
```

Run; expect compile failure.

- [ ] **Step 3: Implement `SongBarRenderLogic`**

Move logic out of `SongBarRenderer.cs`. The renderer keeps its field for the logic instance and calls into it before drawing.

- [ ] **Step 4: Re-point `SongBarRenderer.cs`**

- [ ] **Step 5: Run full suite**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: all pass.

- [ ] **Step 6: Re-run coverage; rate should rise 1–2 percentage points.**

- [ ] **Step 7: Commit**

```bash
git add DTXMania.Game/Lib/Song/Components/SongBarRenderLogic.cs \
        DTXMania.Game/Lib/Song/Components/SongBarRenderer.cs \
        DTXMania.Test/Song/Components/SongBarRenderLogicTests.cs
git commit -m "refactor: extract SongBarRenderLogic for testability"
```

### Task 10: Extract `PerformanceStageLoader`

**Files:**
- Create: `DTXMania.Game/Lib/Stage/Performance/PerformanceStageLoader.cs`
- Create: `DTXMania.Test/Stage/Performance/PerformanceStageLoaderTests.cs`
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`

**What moves:** the song-load pipeline — chart parsing kickoff, BGM file resolution (path lookup, fallback rules), audio-pre-load scheduling. This is the discrete chunk of `PerformanceStage` that runs on activation and produces a `LoadedSong` result.

**What stays:** `OnActivate` itself still orchestrates, but delegates to `PerformanceStageLoader.LoadAsync(...)`.

- [ ] **Step 1: Identify the load pipeline in `PerformanceStage.cs`**

Run `grep -n "OnActivate\|LoadChart\|LoadBgm\|LoadAudio" DTXMania.Game/Lib/Stage/PerformanceStage.cs`. The contiguous block(s) of code that perform loading are what moves. If loading is mixed with state assignment on `PerformanceStage` fields, the loader returns a `LoadResult` record and the stage applies the result.

- [ ] **Step 2: Sketch the API**

```csharp
namespace DTXMania.Game.Lib.Stage.Performance
{
    public class PerformanceStageLoader
    {
        private readonly IResourceManager _resources;
        private readonly DTXChartParser _parser;

        public PerformanceStageLoader(IResourceManager resources, DTXChartParser parser)
        { _resources = resources; _parser = parser; }

        public LoadResult Load(string chartPath) { /* ... */ }
    }

    public record LoadResult(ParsedChart Chart, string BgmPath, /* ... */);
}
```

Exact types depend on the source; do not invent fields.

- [ ] **Step 3: Write failing characterization tests using mocks**

```csharp
public class PerformanceStageLoaderTests
{
    [Fact]
    public void Load_WithValidChartPath_ShouldReturnParsedChartAndResolvedBgmPath()
    {
        var resources = new Mock<IResourceManager>();
        var parser = new DTXChartParser(/* args */);
        var loader = new PerformanceStageLoader(resources.Object, parser);

        var result = loader.Load("TestData/sample.dtx");

        Assert.NotNull(result.Chart);
        Assert.False(string.IsNullOrEmpty(result.BgmPath));
    }

    [Fact]
    public void Load_WhenBgmMissing_ShouldFallBackOrReturnEmptyBgmPath()
    {
        // Encode actual behavior from PerformanceStage.cs current code.
    }
}
```

- [ ] **Step 4: Implement the loader**

- [ ] **Step 5: Re-point `PerformanceStage.OnActivate`** to call `_loader.Load(chartPath)` and consume `LoadResult`.

- [ ] **Step 6: Run full suite**

Expected: all pass.

- [ ] **Step 7: Re-run coverage; `PerformanceStage.cs` shrinks; `PerformanceStageLoader.cs` covered.**

- [ ] **Step 8: Commit**

```bash
git add DTXMania.Game/Lib/Stage/Performance/PerformanceStageLoader.cs \
        DTXMania.Game/Lib/Stage/PerformanceStage.cs \
        DTXMania.Test/Stage/Performance/PerformanceStageLoaderTests.cs
git commit -m "refactor: extract PerformanceStageLoader for testability"
```

If the loader turns out to be too entangled to extract cleanly, **stop after one good-faith attempt**, revert, and document the obstacle in the commit message of a `chore:` commit. Then skip to Task 11 (end flow) which is more decoupled. The plan's risk-mitigation budget permits this.

### Task 11: Extract `PerformanceStageEndFlow`

**Files:**
- Create: `DTXMania.Game/Lib/Stage/Performance/PerformanceStageEndFlow.cs`
- Create: `DTXMania.Test/Stage/Performance/PerformanceStageEndFlowTests.cs`
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`

**What moves:** the end-of-song flow — deciding when the song is over (final note + tail time + audio finished), assembling the `ResultStage`'s input, and the transition trigger logic. State-machine logic, no graphics.

- [ ] **Step 1: Identify the end-flow code in `PerformanceStage.cs`**

`grep -n "Result\|EndOfSong\|FinalNote\|TransitionTo" DTXMania.Game/Lib/Stage/PerformanceStage.cs`. Contiguous logic is what moves.

- [ ] **Step 2: Sketch the API**

```csharp
public class PerformanceStageEndFlow
{
    public EndState Step(double currentSongTimeMs, bool audioStillPlaying, int notesRemaining) { /* ... */ }
    public ResultStageInput BuildResult(ScoreSnapshot score, ComboSnapshot combo, /* ... */) { /* ... */ }
}

public enum EndState { Playing, Tailing, ReadyForResult }
```

- [ ] **Step 3: Write failing tests for state transitions**

```csharp
[Fact]
public void Step_WhenNotesRemainAndAudioPlaying_ShouldReturnPlaying()
[Fact]
public void Step_WhenAllNotesDoneButAudioPlaying_ShouldReturnTailing()
[Fact]
public void Step_WhenAllNotesDoneAndAudioFinished_ShouldReturnReadyForResult()
[Fact]
public void BuildResult_ShouldPopulateResultStageInputFromSnapshots()
```

- [ ] **Step 4: Implement `PerformanceStageEndFlow`**

- [ ] **Step 5: Re-point `PerformanceStage.Update`** to consult the new state machine.

- [ ] **Step 6: Run full suite; verify pass.**

- [ ] **Step 7: Re-run coverage.**

- [ ] **Step 8: Commit**

```bash
git add DTXMania.Game/Lib/Stage/Performance/PerformanceStageEndFlow.cs \
        DTXMania.Game/Lib/Stage/PerformanceStage.cs \
        DTXMania.Test/Stage/Performance/PerformanceStageEndFlowTests.cs
git commit -m "refactor: extract PerformanceStageEndFlow state machine"
```

---

## Phase 3 — Pillar 1 continued: remaining test gaps

### Task 12: Cover `SongManager` edge cases

**Files:**
- Modify: `DTXMania.Test/Song/SongManagerCoverageTests.cs` (already exists)
- Source under test: `DTXMania.Game/Lib/Song/SongManager.cs` (currently 94.8%, missing 152)

- [ ] **Step 1: Inspect uncovered branches**

Use the per-file gap data from Task 1 Step 3. Common gaps in singletons: locking paths under contention, re-initialization, edge cases in collection mutation, error paths in DB load.

- [ ] **Step 2: Add tests**

Add tests targeting each uncovered behavior. Use the existing `SongManagerCollectionDefinition` xUnit collection if singleton isolation matters.

- [ ] **Step 3: Run, verify, commit**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongManager"
git add DTXMania.Test/Song/SongManagerCoverageTests.cs
git commit -m "test: cover SongManager edge cases"
```

`SongManager.cs` target: ≥ 98%.

### Task 13: Cover `ModularInputManager` paths

**Files:**
- Modify: `DTXMania.Test/Input/ModularInputManagerTests.cs` (create if missing)
- Source under test: `DTXMania.Game/Lib/Input/ModularInputManager.cs` (currently 86.1%, missing 68)

- [ ] **Step 1: Identify uncovered branches**

Likely candidates: source-priority switching, source-disable handling, duplicate-event suppression, repeat-key timing.

- [ ] **Step 2: Add tests using `Mock<IInputSource>`**

```csharp
[Fact]
public void Update_WithMultipleSources_ShouldMergeStatesInDeclaredOrder()
{
    var src1 = new Mock<IInputSource>();
    var src2 = new Mock<IInputSource>();
    src1.Setup(x => x.IsKeyPressed(Keys.A)).Returns(false);
    src2.Setup(x => x.IsKeyPressed(Keys.A)).Returns(true);
    var mgr = new ModularInputManager(new[] { src1.Object, src2.Object });
    mgr.Update();
    Assert.True(mgr.IsKeyPressed(Keys.A));
}
```

- [ ] **Step 3: Run, verify, commit**

```bash
git add DTXMania.Test/Input/ModularInputManagerTests.cs
git commit -m "test: cover ModularInputManager source merging and edge cases"
```

`ModularInputManager.cs` target: ≥ 94%.

### Task 14: Cover `DTXChartParser` and supporting code

**Files:**
- Modify: `DTXMania.Test/Song/DTXChartParserAdditionalTests.cs` (already exists)
- Source under test: `DTXMania.Game/Lib/Song/DTXChartParser.cs` (currently 90.0%, missing 128)

- [ ] **Step 1: Identify uncovered branches**

The 128 missing lines likely live in: rare channel handlers, malformed-line tolerance paths, encoding-detection branches, BPM-change handling at unusual positions. Cross-reference cobertura per-line data.

- [ ] **Step 2: Add tests with crafted `.dtx` snippets**

Place fixtures under `DTXMania.Test/TestData/` (existing pattern). One fixture per behavior.

```csharp
[Fact]
public void Parse_WithMalformedHeader_ShouldSkipLineAndContinue()
{
    var raw = "#TITLE Test\n#BPMnot-a-number\n#WAV01 hi.wav\n#00111: 01";
    var chart = DTXChartParser.Parse(raw);
    Assert.Equal("Test", chart.Title);
    Assert.Single(chart.Notes);
}

[Fact]
public void Parse_WithBpmChangeMidSong_ShouldRecordBpmChange()
{
    // Encode actual current behavior using a fixture
}
```

- [ ] **Step 3: Run, verify, commit**

```bash
git add DTXMania.Test/Song/DTXChartParserAdditionalTests.cs DTXMania.Test/TestData/*
git commit -m "test: cover DTXChartParser tolerance and BPM change paths"
```

`DTXChartParser.cs` target: ≥ 95%.

### Task 15: Cover remaining Pillar-1 stragglers

**Files:**
- Modify or create:
  - `DTXMania.Test/Song/SongDatabaseServiceCoverageTests.cs` (already exists)
  - `DTXMania.Test/UI/DefaultGraphicsGeneratorTests.cs` (create if missing)
  - `DTXMania.Test/Song/Components/PreviewImagePanelTests.cs` (create if missing)

Source targets:
- `Lib/Song/Entities/SongDatabaseService.cs` (84.1%, missing 150) — DB upsert paths, error handling, schema migration edges.
- `Lib/UI/DefaultGraphicsGenerator.cs` (88.8%, missing 72) — non-graphics methods only; the `GraphicsDevice` paths are Pillar 2 territory.
- `Lib/Song/Components/PreviewImagePanel.cs` (90.5%, missing 74) — state transitions, fade math, file-missing fallbacks.

- [ ] **Step 1: For each file, identify gaps and add `Scenario_ShouldExpect` tests**

Use the patterns established in Tasks 3–7.

- [ ] **Step 2: Run, verify, commit one file at a time**

```bash
git add DTXMania.Test/Song/SongDatabaseServiceCoverageTests.cs
git commit -m "test: cover SongDatabaseService upsert and migration paths"

git add DTXMania.Test/UI/DefaultGraphicsGeneratorTests.cs
git commit -m "test: cover DefaultGraphicsGenerator non-graphics methods"

git add DTXMania.Test/Song/Components/PreviewImagePanelTests.cs
git commit -m "test: cover PreviewImagePanel state and fade math"
```

---

## Phase 4 — Pillar 2: apply exclusions

Each task in this phase modifies one source file (or a small group). For each method targeted:
1. Confirm the method is **pure graphics** (only `SpriteBatch.Draw` / `_renderer.Draw` / `font.DrawText` calls; no conditionals, loops, or state mutation beyond looping over a collection to draw each element).
2. Add `[ExcludeFromCodeCoverage]` with a one-line XML summary.
3. Run the test suite to confirm nothing broke (it shouldn't — attribute-only change).
4. Re-run coverage to confirm rate rises.
5. Commit.

If a method has conditionals or state mutation, **do not exclude it**. Either leave it (it must be covered by a test) or extract its logic (would be a back-port of Pillar 3 — note it as a follow-up, do not do it in this PR).

### Task 16: Exclude Draw methods on stages

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Modify: `DTXMania.Game/Lib/Stage/TitleStage.cs`
- Modify: `DTXMania.Game/Lib/Stage/SongTransitionStage.cs`
- Modify: `DTXMania.Game/Lib/Stage/ResultStage.cs`

**Target methods** (verified to be pure draw paths by inspection; double-check each before applying):

| File | Method (line) |
|---|---|
| `PerformanceStage.cs` | `OnDraw` (236), `DrawNotes` (725), `DrawNoteOverlays` (745), `DrawCenteredText` (937), `DrawBackground` (1427), `DrawLaneBackgrounds` (1444), `DrawJudgementLine` (1486), `DrawPads` (1502), `DrawHitEffects` (1510), `DrawJudgementTexts` (1516), `DrawUIElements` (1522), `DrawShutters` (1551), `DrawGaugeElements` (1567) |
| `TitleStage.cs` | `OnDraw` (141), `DrawVersionInfo` (570), `DrawMenu` (582), `DrawMenuWithTexture` (595), `DrawMenuItemFromTexture` (625), `DrawMenuCursor` (638), `DrawMenuWithRectangles` (670), `DrawTextRect` (720) |
| `SongTransitionStage.cs` | `OnDraw` (582), `DrawBackground` (613), `DrawText` (643), `DrawDifficultyBackground` (678), `DrawDifficultySprite` (699), `DrawDifficultyLevelNumber` (719), `DrawPreviewImage` (747) |
| `ResultStage.cs` | `OnDraw` (102), `DrawBackground` (226), `DrawResults` (263), `DrawResultLine` (300) |

`DrawGameplayState` in `PerformanceStage.cs` (line 767) is **probably not pure** (likely contains state branching). Inspect before deciding; default to NOT excluding unless verified pure.

- [ ] **Step 1: For each file in the list, open and verify each method is pure draw**

A method is pure draw iff its body consists only of: variable declarations, calls to `spriteBatch.Draw`, `_renderer.Draw`, `font.DrawText`, simple `for`/`foreach` loops over collections where each iteration only draws, and trivial null-checks against draw dependencies (texture, font). Anything else → **do not exclude**.

- [ ] **Step 2: Apply attributes**

Add `using System.Diagnostics.CodeAnalysis;` if missing. Above each verified method:

```csharp
/// <summary>Pure draw method; no logic to assert.</summary>
[ExcludeFromCodeCoverage]
private void DrawBackground()
{
    // ... existing body unchanged
}
```

- [ ] **Step 3: Run the full suite**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: all pass (attribute-only change).

- [ ] **Step 4: Re-run coverage**

Expected: rate jumps by 2–4 percentage points; valid-line count drops.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Stage/PerformanceStage.cs \
        DTXMania.Game/Lib/Stage/TitleStage.cs \
        DTXMania.Game/Lib/Stage/SongTransitionStage.cs \
        DTXMania.Game/Lib/Stage/ResultStage.cs
git commit -m "test: exclude pure draw methods on stages from coverage"
```

### Task 17: Exclude Draw methods on Performance components

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/Performance/NoteRenderer.cs`
- Modify: `DTXMania.Game/Lib/Stage/Performance/PadRenderer.cs`
- Modify: `DTXMania.Game/Lib/Stage/Performance/GaugeDisplay.cs`
- Modify: `DTXMania.Game/Lib/Stage/Performance/ComboDisplay.cs`
- Modify: `DTXMania.Game/Lib/Stage/Performance/EffectsManager.cs`

**Target methods** (verify each before applying):

| File | Method (line) |
|---|---|
| `NoteRenderer.cs` | `DrawNotes` (199), `DrawNote` (226), `DrawNoteOverlays` (330), `DrawNoteOverlay` (347), `DrawOverlayEffect` (487), `DrawLaneFlashEffects` (530) |
| `PadRenderer.cs` | `Draw` (163), `DrawPadSpriteCore` (178), `DrawFallbackTextureCore` (198), `DrawPadForLane` (277), `TryDrawSpriteSheetPad` (330), `DrawFallbackPad` (361) |
| `GaugeDisplay.cs` | `Draw` (126), `DrawFrame` (199) |
| `ComboDisplay.cs` | `Draw` (146) |
| `EffectsManager.cs` | `Draw` (125) |

- [ ] **Step 1: Verify each method is pure draw** (same rule as Task 16 Step 1).
- [ ] **Step 2: Apply `[ExcludeFromCodeCoverage]` with summary comments.**
- [ ] **Step 3: Run full suite; expect pass.**
- [ ] **Step 4: Re-run coverage.**
- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Stage/Performance/NoteRenderer.cs \
        DTXMania.Game/Lib/Stage/Performance/PadRenderer.cs \
        DTXMania.Game/Lib/Stage/Performance/GaugeDisplay.cs \
        DTXMania.Game/Lib/Stage/Performance/ComboDisplay.cs \
        DTXMania.Game/Lib/Stage/Performance/EffectsManager.cs
git commit -m "test: exclude pure draw methods on Performance components"
```

### Task 18: Exclude graphics-bound resource methods

**Files:**
- Modify: `DTXMania.Game/Lib/Resources/ManagedFont.cs`
- Modify: `DTXMania.Game/Lib/Resources/BitmapFont.cs`
- Modify: `DTXMania.Game/Lib/Resources/ManagedSpriteTexture.cs`

**Target methods** (verify each):

| File | Method (line) |
|---|---|
| `ManagedFont.cs` | `DrawString` (341, 369), `DrawStringWithOutline` (388), `DrawStringWithGradient` (410), `DrawStringWithShadow` (432), `DrawStringWrapped` (445), `DrawStringCustom` (921) |
| `BitmapFont.cs` | `DrawText` (99), `DrawCharacter` (269) |
| `ManagedSpriteTexture.cs` | `DrawSprite` (108, 121, 137, 154, 167, 180) |

`TryDrawCharacter` in `ManagedFont.cs` (line 752) is **probably not pure** (returns bool, may have decision logic). Inspect before applying.

- [ ] **Step 1: Verify each method is pure draw.**
- [ ] **Step 2: Apply `[ExcludeFromCodeCoverage]`.**
- [ ] **Step 3: Run full suite; expect pass.**
- [ ] **Step 4: Re-run coverage.**
- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Resources/ManagedFont.cs \
        DTXMania.Game/Lib/Resources/BitmapFont.cs \
        DTXMania.Game/Lib/Resources/ManagedSpriteTexture.cs
git commit -m "test: exclude pure draw methods in resource wrappers"
```

### Task 19: Exclude any remaining pure-draw helpers in `SongStatusPanel` / `SongBarRenderer` / `SongListDisplay`

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Components/SongStatusPanel.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/SongBarRenderer.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/SongListDisplay.cs`

After Tasks 8 and 9, the renderers in `SongStatusPanel.cs` and `SongBarRenderer.cs` are now thin shims around `SongStatusPanelLogic` / `SongBarRenderLogic`. They should qualify as pure draw.

**Target methods in `SongStatusPanel.cs`** (post-extraction):

`OnDraw` (496), `DrawBackground` (540), `DrawSongInfo` (575), `DrawDTXManiaNXLayout` (604), `DrawSimplifiedInfo` (619), `DrawSongTypeInfo` (629), `DrawBPMSection` (654), `DrawBPMBackground` (693), `DrawSimpleBPMBackground` (746), `DrawGraphPanelBackground` (771), `DrawFallbackGraphPanelBackground` (808), `DrawSkillPointSection` (831), `DrawDifficultyGrid` (861), `DrawDifficultyCell` (933), `DrawDifficultyFrame` (944), `DrawDifficultyCellContent` (974), `DrawRankSymbol` (993), `DrawDifficultyText` (1029), `DrawGraphPanel` (1055), `DrawNoteDistributionBars` (1094), `DrawProgressBar` (1128), `DrawNotesCounter` (1153), `DrawNoSongMessage` (1207), `DrawTextWithShadow` (1233).

Note: line numbers shift after Task 8. Re-run `grep -n "private void Draw\|protected override void OnDraw" DTXMania.Game/Lib/Song/Components/SongStatusPanel.cs` to find current locations.

**Target methods in `SongBarRenderer.cs`** (post-extraction): all `Draw*` methods that remain after Task 9. Re-list with `grep -n "Draw" DTXMania.Game/Lib/Song/Components/SongBarRenderer.cs`.

**Target methods in `SongListDisplay.cs`**: inspect with `grep -n "Draw" DTXMania.Game/Lib/Song/Components/SongListDisplay.cs`, then apply the same rule: pure draws get the attribute; anything with conditional logic does not.

- [ ] **Step 1: For each file, list `Draw*` methods and verify each is pure draw.**
- [ ] **Step 2: Apply `[ExcludeFromCodeCoverage]` to verified ones.**
- [ ] **Step 3: Run full suite; expect pass.**
- [ ] **Step 4: Re-run coverage.**
- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/Components/SongStatusPanel.cs \
        DTXMania.Game/Lib/Song/Components/SongBarRenderer.cs \
        DTXMania.Game/Lib/Song/Components/SongListDisplay.cs
git commit -m "test: exclude pure draw methods on Song components"
```

---

## Phase 5 — Final Verification

### Task 20: Verify Mac coverage ≥ 90%

**Files:**
- No file changes.

- [ ] **Step 1: Run the Mac suite with coverage**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --collect:"XPlat Code Coverage" \
  --settings coverlet.runsettings \
  --results-directory ./TestResults
```

Expected: all tests pass.

- [ ] **Step 2: Print the new rate**

```bash
python3 -c "import xml.etree.ElementTree as ET, glob, os; \
  f=max(glob.glob('TestResults/*/coverage.cobertura.xml'), key=os.path.getmtime); \
  r=ET.parse(f).getroot(); \
  print(f'FINAL-MAC: line={float(r.get(\"line-rate\"))*100:.2f}% branch={float(r.get(\"branch-rate\"))*100:.2f}% lines={r.get(\"lines-covered\")}/{r.get(\"lines-valid\")}')"
```

Expected: `line >= 90.00%`. If not, return to whichever phase has obvious gaps remaining (re-run Task 1 Step 3 to see which file is now the biggest contributor).

- [ ] **Step 3: No commit**

This task produces a verification number only.

### Task 21: Push branch and verify Windows CI

**Files:**
- No file changes.

- [ ] **Step 1: Push the branch**

```bash
git push -u origin <branch-name>
```

- [ ] **Step 2: Open the GitHub Actions run for the PR**

Wait for both Mac and Windows jobs to complete. Confirm both pass.

- [ ] **Step 3: Check codecov status on the PR**

Codecov must show the Windows full suite at ≥ 90%. If Windows is below 90% despite Mac being above:
- Inspect the Windows-only files (e.g., `SongStatusPanel.cs` tests that ran on Windows but not Mac). Apply Pillar 1 or Pillar 2 to them.
- The most likely culprits are graphics-dependent test files that exist but didn't get attention in this PR.
- Add additional commits to address the gap; do not merge until both metrics clear.

- [ ] **Step 4: Open PR**

Once codecov is green, open the PR with a description that lists the three pillars, the before/after coverage numbers (Mac and Windows), and any deviations from this plan.

---

## Self-Review (post-write)

- **Spec coverage:**
  - "Three-pillar strategy" → Phases 1, 2, 4 cover Pillars 1, 3, 2 respectively. ✓
  - "Windows full suite is the gating metric" → Task 2 + Task 21. ✓
  - "Single sweeping PR" → all tasks form a single branch; PR opens at Task 21. ✓
  - "Pure extraction, no behavior changes" → Phase 2 mandates characterization tests + full-suite verification at each step. ✓
  - "Estimated +3,800 lines" → verification at Task 20 confirms ≥ 90%; explicit fallback if short. ✓
  - "Risk mitigations" → Task 10 carries the explicit "stop after good-faith attempt" fallback for `PerformanceStageLoader`. ✓
- **Placeholder scan:** No `TBD`/`TODO` / "fill in details". Every step has either a code block or an exact command. Where a test body needs to encode existing behavior, the plan explicitly instructs the engineer to read the source first.
- **Type consistency:** `SongStatusPanelLogic` / `SongBarRenderLogic` / `PerformanceStageLoader` / `PerformanceStageEndFlow` referenced consistently. `[ExcludeFromCodeCoverage]` always method-level. Test file naming consistent (`<Class>CoverageTests.cs` or `<Class>Tests.cs`).
