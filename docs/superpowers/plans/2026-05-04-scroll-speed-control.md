# Scroll Speed Control Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add user-facing controls to adjust visual scroll speed (x0.5–x4.0, step x0.5) from the Config menu, song-select screen, and during play, with write-through persistence to `Config.ini`.

**Architecture:** Centralize the mutation in `ConfigManager` (new `SetScrollSpeed` / `AdjustScrollSpeed` + `ScrollSpeedChanged` event). Add two new `InputCommandType` values bound to PageUp/PageDown. Stages subscribe to the event and forward hotkey input to the ConfigManager. A small `ScrollSpeedIndicator` shows a fade-out toast in-game.

**Tech Stack:** .NET 8, C#, MonoGame 3.8, xUnit, Moq.

**Spec:** `docs/superpowers/specs/2026-05-04-scroll-speed-control-design.md`

---

## File Structure

**New files:**
- `DTXMania.Game/Lib/Config/ScrollSpeedRange.cs` — constants + snap/clamp/format helpers.
- `DTXMania.Game/Lib/Config/ScrollSpeedChangedEventArgs.cs` — event args.
- `DTXMania.Game/Lib/Stage/Performance/ScrollSpeedIndicator.cs` — fade toast.
- `DTXMania.Test/Config/ScrollSpeedRangeTests.cs`
- `DTXMania.Test/Config/ConfigManagerScrollSpeedTests.cs`
- `DTXMania.Test/Input/InputManagerScrollSpeedKeysTests.cs`

**Modified:**
- `DTXMania.Game/Lib/Config/IConfigManager.cs` — add event + methods.
- `DTXMania.Game/Lib/Config/ConfigManager.cs` — implement event + methods.
- `DTXMania.Game/Lib/Config/ConfigItems.cs` — extend `IntegerConfigItem` with optional formatter.
- `DTXMania.Game/Lib/Input/InputManager.cs` — add enum values + PageUp/PageDown bindings.
- `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs` — indicator constants.
- `DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs` — scroll-speed label position constants.
- `DTXMania.Game/Lib/Stage/PerformanceStage.cs` — read config, subscribe, poll input, own indicator.
- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs` — subscribe, poll input, render label.
- `DTXMania.Game/Lib/Stage/ConfigStage.cs` — add scroll-speed config item; commit `ScrollSpeed` on save.
- `DTXMania.Test/DTXMania.Test.Mac.csproj` — add new test files to explicit `Compile Include`.

---

## Task 1: `ScrollSpeedRange` helper + tests

**Files:**
- Create: `DTXMania.Game/Lib/Config/ScrollSpeedRange.cs`
- Test: `DTXMania.Test/Config/ScrollSpeedRangeTests.cs`
- Modify: `DTXMania.Test/DTXMania.Test.Mac.csproj`

- [ ] **Step 1: Write the failing tests**

Create `DTXMania.Test/Config/ScrollSpeedRangeTests.cs`:

```csharp
using DTXMania.Game.Lib.Config;
using Xunit;

namespace DTXMania.Test.Config
{
    public class ScrollSpeedRangeTests
    {
        [Theory]
        [InlineData(50, 50)]
        [InlineData(100, 100)]
        [InlineData(400, 400)]
        [InlineData(117, 100)]
        [InlineData(130, 150)]
        [InlineData(124, 100)]
        [InlineData(125, 150)]
        [InlineData(425, 400)]
        [InlineData(0, 50)]
        [InlineData(-50, 50)]
        [InlineData(9999, 400)]
        public void SnapAndClamp_ReturnsExpected(int input, int expected)
        {
            Assert.Equal(expected, ScrollSpeedRange.SnapAndClamp(input));
        }

        [Theory]
        [InlineData(50, "x0.5")]
        [InlineData(100, "x1.0")]
        [InlineData(150, "x1.5")]
        [InlineData(400, "x4.0")]
        public void Format_ReturnsXMultiplier(int percent, string expected)
        {
            Assert.Equal(expected, ScrollSpeedRange.Format(percent));
        }

        [Fact]
        public void Constants_HaveExpectedValues()
        {
            Assert.Equal(50, ScrollSpeedRange.Min);
            Assert.Equal(400, ScrollSpeedRange.Max);
            Assert.Equal(50, ScrollSpeedRange.Step);
            Assert.Equal(100, ScrollSpeedRange.Default);
        }
    }
}
```

- [ ] **Step 2: Add the new test file to the Mac test project**

Open `DTXMania.Test/DTXMania.Test.Mac.csproj` and add `<Compile Include="Config\ScrollSpeedRangeTests.cs" />` next to the other `Config\*.cs` includes.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ScrollSpeedRangeTests"`
Expected: FAIL — `ScrollSpeedRange` does not exist (compile error).

- [ ] **Step 4: Create `ScrollSpeedRange.cs`**

```csharp
using System;
using System.Globalization;

namespace DTXMania.Game.Lib.Config
{
    /// <summary>
    /// Range, step, and formatting for the visual scroll-speed setting.
    /// Single source of truth used by ConfigManager, ConfigStage, SongSelectionStage,
    /// and the in-game ScrollSpeedIndicator.
    /// </summary>
    public static class ScrollSpeedRange
    {
        public const int Min = 50;
        public const int Max = 400;
        public const int Step = 50;
        public const int Default = 100;

        /// <summary>
        /// Snaps an arbitrary integer percent to the nearest multiple of Step,
        /// then clamps to [Min, Max].
        /// </summary>
        public static int SnapAndClamp(int percent)
        {
            var snapped = (int)Math.Round(percent / (double)Step) * Step;
            if (snapped < Min) return Min;
            if (snapped > Max) return Max;
            return snapped;
        }

        /// <summary>
        /// Formats a percent as "x1.0", "x1.5", etc. (one decimal).
        /// </summary>
        public static string Format(int percent)
        {
            var multiplier = percent / 100.0;
            return "x" + multiplier.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ScrollSpeedRangeTests"`
Expected: PASS (all 16 cases).

- [ ] **Step 6: Commit**

```bash
git add DTXMania.Game/Lib/Config/ScrollSpeedRange.cs DTXMania.Test/Config/ScrollSpeedRangeTests.cs DTXMania.Test/DTXMania.Test.Mac.csproj
git commit -m "feat: add ScrollSpeedRange helper with snap/clamp/format"
```

---

## Task 2: `ScrollSpeedChangedEventArgs`

**Files:**
- Create: `DTXMania.Game/Lib/Config/ScrollSpeedChangedEventArgs.cs`

- [ ] **Step 1: Create the event args type**

```csharp
using System;

namespace DTXMania.Game.Lib.Config
{
    public sealed class ScrollSpeedChangedEventArgs : EventArgs
    {
        public int OldPercent { get; }
        public int NewPercent { get; }

        public ScrollSpeedChangedEventArgs(int oldPercent, int newPercent)
        {
            OldPercent = oldPercent;
            NewPercent = newPercent;
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: SUCCESS.

- [ ] **Step 3: Commit**

```bash
git add DTXMania.Game/Lib/Config/ScrollSpeedChangedEventArgs.cs
git commit -m "feat: add ScrollSpeedChangedEventArgs"
```

---

## Task 3: `ConfigManager.SetScrollSpeed` / `AdjustScrollSpeed` + event

**Files:**
- Modify: `DTXMania.Game/Lib/Config/IConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigManager.cs`
- Test: `DTXMania.Test/Config/ConfigManagerScrollSpeedTests.cs`
- Modify: `DTXMania.Test/DTXMania.Test.Mac.csproj`

- [ ] **Step 1: Write the failing tests**

Create `DTXMania.Test/Config/ConfigManagerScrollSpeedTests.cs`:

```csharp
using System;
using System.IO;
using DTXMania.Game.Lib.Config;
using Xunit;

namespace DTXMania.Test.Config
{
    public class ConfigManagerScrollSpeedTests : IDisposable
    {
        private readonly string _tempPath;

        public ConfigManagerScrollSpeedTests()
        {
            _tempPath = Path.Combine(Path.GetTempPath(),
                "dtxmania-scrollspeed-" + Guid.NewGuid().ToString("N") + ".ini");
        }

        public void Dispose()
        {
            if (File.Exists(_tempPath))
                File.Delete(_tempPath);
        }

        [Theory]
        [InlineData(117, 100)]
        [InlineData(130, 150)]
        [InlineData(425, 400)]
        [InlineData(30, 50)]
        public void SetScrollSpeed_SnapsToNearestStep(int input, int expected)
        {
            var cm = new ConfigManager();
            cm.SetScrollSpeed(_tempPath, input);
            Assert.Equal(expected, cm.Config.ScrollSpeed);
        }

        [Theory]
        [InlineData(0, 50)]
        [InlineData(-100, 50)]
        [InlineData(9999, 400)]
        public void SetScrollSpeed_ClampsToRange(int input, int expected)
        {
            var cm = new ConfigManager();
            cm.SetScrollSpeed(_tempPath, input);
            Assert.Equal(expected, cm.Config.ScrollSpeed);
        }

        [Fact]
        public void SetScrollSpeed_RaisesChangedEventWithOldAndNew()
        {
            var cm = new ConfigManager();
            cm.Config.ScrollSpeed = 100;

            ScrollSpeedChangedEventArgs? captured = null;
            cm.ScrollSpeedChanged += (_, e) => captured = e;

            cm.SetScrollSpeed(_tempPath, 200);

            Assert.NotNull(captured);
            Assert.Equal(100, captured!.OldPercent);
            Assert.Equal(200, captured.NewPercent);
        }

        [Fact]
        public void SetScrollSpeed_NoOpWhenUnchanged_DoesNotRaiseEvent()
        {
            var cm = new ConfigManager();
            cm.Config.ScrollSpeed = 150;
            var raised = false;
            cm.ScrollSpeedChanged += (_, _) => raised = true;

            cm.SetScrollSpeed(_tempPath, 150);

            Assert.False(raised);
            Assert.False(File.Exists(_tempPath));
        }

        [Fact]
        public void SetScrollSpeed_PersistsToConfigIni()
        {
            var cm = new ConfigManager();
            cm.SetScrollSpeed(_tempPath, 250);

            var roundTrip = new ConfigManager();
            roundTrip.LoadConfig(_tempPath);
            Assert.Equal(250, roundTrip.Config.ScrollSpeed);
        }

        [Fact]
        public void AdjustScrollSpeed_StepsUp()
        {
            var cm = new ConfigManager();
            cm.Config.ScrollSpeed = 100;
            cm.AdjustScrollSpeed(_tempPath, +1);
            Assert.Equal(150, cm.Config.ScrollSpeed);
        }

        [Fact]
        public void AdjustScrollSpeed_StepsDown()
        {
            var cm = new ConfigManager();
            cm.Config.ScrollSpeed = 200;
            cm.AdjustScrollSpeed(_tempPath, -1);
            Assert.Equal(150, cm.Config.ScrollSpeed);
        }

        [Fact]
        public void AdjustScrollSpeed_FloorsAtMin()
        {
            var cm = new ConfigManager();
            cm.Config.ScrollSpeed = 50;
            cm.AdjustScrollSpeed(_tempPath, -1);
            Assert.Equal(50, cm.Config.ScrollSpeed);
        }

        [Fact]
        public void AdjustScrollSpeed_CeilingsAtMax()
        {
            var cm = new ConfigManager();
            cm.Config.ScrollSpeed = 400;
            cm.AdjustScrollSpeed(_tempPath, +1);
            Assert.Equal(400, cm.Config.ScrollSpeed);
        }
    }
}
```

- [ ] **Step 2: Add to Mac test project**

Edit `DTXMania.Test/DTXMania.Test.Mac.csproj`: add `<Compile Include="Config\ConfigManagerScrollSpeedTests.cs" />` next to the other `Config\*.cs` includes.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigManagerScrollSpeedTests"`
Expected: FAIL — `SetScrollSpeed`, `AdjustScrollSpeed`, `ScrollSpeedChanged` do not exist.

- [ ] **Step 4: Update `IConfigManager`**

Replace contents of `DTXMania.Game/Lib/Config/IConfigManager.cs`:

```csharp
using System;

namespace DTXMania.Game.Lib.Config
{
    public interface IConfigManager
    {
        ConfigData Config { get; }
        void LoadConfig(string filePath);
        void SaveConfig(string filePath);
        void ResetToDefaults();

        /// <summary>
        /// Raised when the scroll-speed setting changes via SetScrollSpeed or AdjustScrollSpeed.
        /// Not raised by direct mutation of Config.ScrollSpeed or by LoadConfig.
        /// </summary>
        event EventHandler<ScrollSpeedChangedEventArgs>? ScrollSpeedChanged;

        /// <summary>
        /// Sets the scroll speed (percent), snapping to the nearest allowed step and
        /// clamping to the allowed range. Persists to the given file path and raises
        /// ScrollSpeedChanged when the value actually changes.
        /// No-op (and no save) if the new value equals the current value.
        /// </summary>
        void SetScrollSpeed(string configFilePath, int percent);

        /// <summary>
        /// Adjusts scroll speed by stepDelta * Step. Equivalent to
        /// SetScrollSpeed(path, current + stepDelta * Step).
        /// </summary>
        void AdjustScrollSpeed(string configFilePath, int stepDelta);
    }
}
```

- [ ] **Step 5: Implement on `ConfigManager`**

In `DTXMania.Game/Lib/Config/ConfigManager.cs`, after the existing `ResetToDefaults` method (or near the end of the class), add:

```csharp
public event EventHandler<ScrollSpeedChangedEventArgs>? ScrollSpeedChanged;

public void SetScrollSpeed(string configFilePath, int percent)
{
    var snapped = ScrollSpeedRange.SnapAndClamp(percent);
    var old = Config.ScrollSpeed;
    if (snapped == old)
        return;

    Config.ScrollSpeed = snapped;

    try
    {
        SaveConfig(configFilePath);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to persist ScrollSpeed change to {Path}; in-memory value still updated.", configFilePath);
    }

    ScrollSpeedChanged?.Invoke(this, new ScrollSpeedChangedEventArgs(old, snapped));
}

public void AdjustScrollSpeed(string configFilePath, int stepDelta)
{
    SetScrollSpeed(configFilePath, Config.ScrollSpeed + stepDelta * ScrollSpeedRange.Step);
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigManagerScrollSpeedTests"`
Expected: PASS (all cases).

- [ ] **Step 7: Run full test suite to confirm no regressions**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: All tests pass (same baseline count + the new tests).

- [ ] **Step 8: Commit**

```bash
git add DTXMania.Game/Lib/Config/IConfigManager.cs DTXMania.Game/Lib/Config/ConfigManager.cs DTXMania.Test/Config/ConfigManagerScrollSpeedTests.cs DTXMania.Test/DTXMania.Test.Mac.csproj
git commit -m "feat: add SetScrollSpeed/AdjustScrollSpeed with change event on ConfigManager"
```

---

## Task 4: Add `IncreaseScrollSpeed` / `DecreaseScrollSpeed` to input system

**Files:**
- Modify: `DTXMania.Game/Lib/Input/InputManager.cs`
- Test: `DTXMania.Test/Input/InputManagerScrollSpeedKeysTests.cs`
- Modify: `DTXMania.Test/DTXMania.Test.Mac.csproj`

- [ ] **Step 1: Write the failing tests**

Create `DTXMania.Test/Input/InputManagerScrollSpeedKeysTests.cs`:

```csharp
using DTXMania.Game.Lib.Input;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace DTXMania.Test.Input
{
    public class InputManagerScrollSpeedKeysTests
    {
        [Fact]
        public void DefaultMapping_PageUp_MapsToIncreaseScrollSpeed()
        {
            var im = new InputManager();
            var snapshot = im.GetKeyMappingSnapshot();
            Assert.True(snapshot.TryGetValue(Keys.PageUp, out var cmd));
            Assert.Equal(InputCommandType.IncreaseScrollSpeed, cmd);
        }

        [Fact]
        public void DefaultMapping_PageDown_MapsToDecreaseScrollSpeed()
        {
            var im = new InputManager();
            var snapshot = im.GetKeyMappingSnapshot();
            Assert.True(snapshot.TryGetValue(Keys.PageDown, out var cmd));
            Assert.Equal(InputCommandType.DecreaseScrollSpeed, cmd);
        }

        [Fact]
        public void Enum_HasIncreaseScrollSpeed()
        {
            Assert.Contains(InputCommandType.IncreaseScrollSpeed, System.Enum.GetValues<InputCommandType>());
        }

        [Fact]
        public void Enum_HasDecreaseScrollSpeed()
        {
            Assert.Contains(InputCommandType.DecreaseScrollSpeed, System.Enum.GetValues<InputCommandType>());
        }
    }
}
```

- [ ] **Step 2: Add to Mac test project**

Edit `DTXMania.Test/DTXMania.Test.Mac.csproj`: add `<Compile Include="Input\InputManagerScrollSpeedKeysTests.cs" />` (create the `Input` group entry if not present, or place beside other input includes).

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~InputManagerScrollSpeedKeysTests"`
Expected: FAIL — enum values missing.

- [ ] **Step 4: Add enum values**

In `DTXMania.Game/Lib/Input/InputManager.cs`, replace the `InputCommandType` enum block:

```csharp
public enum InputCommandType
{
    MoveUp,
    MoveDown,
    MoveLeft,
    MoveRight,
    Activate,
    Back,
    IncreaseScrollSpeed,
    DecreaseScrollSpeed
}
```

- [ ] **Step 5: Add default key bindings**

In `DTXMania.Game/Lib/Input/InputManager.cs`, in `InitializeDefaultKeyMapping()`, append:

```csharp
_keyMapping[Keys.PageUp] = InputCommandType.IncreaseScrollSpeed;
_keyMapping[Keys.PageDown] = InputCommandType.DecreaseScrollSpeed;
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~InputManagerScrollSpeedKeysTests"`
Expected: PASS.

- [ ] **Step 7: Run full test suite**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: All previously passing tests still pass.

- [ ] **Step 8: Commit**

```bash
git add DTXMania.Game/Lib/Input/InputManager.cs DTXMania.Test/Input/InputManagerScrollSpeedKeysTests.cs DTXMania.Test/DTXMania.Test.Mac.csproj
git commit -m "feat: add IncreaseScrollSpeed/DecreaseScrollSpeed input commands on PageUp/PageDown"
```

---

## Task 5: Wire `PerformanceStage` to read config + react to event + poll hotkeys

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Modify: `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs` (constants)

This task does NOT yet add the visible toast indicator (that's Task 6). It wires the data flow first.

- [ ] **Step 1: Add layout constants for indicator**

In `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs`, near the existing `NoteDefaultScrollSpeed` constant, add:

```csharp
// Scroll-speed indicator (in-game toast shown when player adjusts scroll speed)
public const float ScrollSpeedIndicatorDurationSeconds = 1.5f;
public const float ScrollSpeedIndicatorFadeSeconds = 0.3f;
public const int ScrollSpeedIndicatorX = 640; // top-center of 1280-wide screen
public const int ScrollSpeedIndicatorY = 40;
```

- [ ] **Step 2: Replace the hardcoded scroll-speed read in PerformanceStage**

In `DTXMania.Game/Lib/Stage/PerformanceStage.cs`, find the block at lines 538-541:

```csharp
// Set scroll speed based on user preference
// TODO: Get scroll speed from user config
var scrollSpeedSetting = 100; // Default scroll speed
_noteRenderer?.SetScrollSpeed(scrollSpeedSetting);
```

Replace with:

```csharp
// Set scroll speed based on user preference (read from config, applied at chart load).
// In-game adjustments are handled via ConfigManager.ScrollSpeedChanged subscription.
var scrollSpeedSetting = _configManager?.Config?.ScrollSpeed ?? ScrollSpeedRange.Default;
_noteRenderer?.SetScrollSpeed(scrollSpeedSetting);
```

Add `using DTXMania.Game.Lib.Config;` to the file if not already present.

- [ ] **Step 3: Subscribe / unsubscribe to ScrollSpeedChanged**

In `PerformanceStage`, find `OnActivate` (lifecycle entry — locate via `protected override void OnActivate`). Append a subscription line:

```csharp
if (_configManager != null)
{
    _configManager.ScrollSpeedChanged += OnScrollSpeedChanged;
}
```

In `OnDeactivate` (matching lifecycle exit), append:

```csharp
if (_configManager != null)
{
    _configManager.ScrollSpeedChanged -= OnScrollSpeedChanged;
}
```

Add the handler method to `PerformanceStage`:

```csharp
private void OnScrollSpeedChanged(object? sender, ScrollSpeedChangedEventArgs e)
{
    _noteRenderer?.SetScrollSpeed(e.NewPercent);
    // Indicator hookup added in Task 6.
}
```

If `OnActivate` / `OnDeactivate` do not yet exist as overrides in `PerformanceStage`, locate the equivalent lifecycle hooks (`Activate` / `Deactivate` from `BaseStage`) and add the same subscribe/unsubscribe code. Verify by `grep -n "OnActivate\|OnDeactivate\|Activate(\|Deactivate(" DTXMania.Game/Lib/Stage/PerformanceStage.cs`.

- [ ] **Step 4: Poll hotkeys each frame**

Locate the per-frame update method in `PerformanceStage` (likely `OnUpdate` or `Update`). Find where input is already polled (e.g., escape key). Add:

```csharp
var input = (_game as BaseGame)?.InputManager?.ModularInputManager;
if (input != null)
{
    if (input.IsCommandPressed(InputCommandType.IncreaseScrollSpeed))
    {
        _configManager.AdjustScrollSpeed(AppPaths.GetConfigFilePath(), +1);
    }
    else if (input.IsCommandPressed(InputCommandType.DecreaseScrollSpeed))
    {
        _configManager.AdjustScrollSpeed(AppPaths.GetConfigFilePath(), -1);
    }
}
```

If the existing input access pattern differs (e.g., `_inputManager.IsCommandPressed`), match the surrounding style. Add `using DTXMania.Game.Lib.Input;` and `using DTXMania.Game.Lib.Utilities;` if not already present.

- [ ] **Step 5: Build**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: SUCCESS.

- [ ] **Step 6: Run full Mac test suite**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add DTXMania.Game/Lib/Stage/PerformanceStage.cs DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs
git commit -m "feat: PerformanceStage reads ScrollSpeed from config and reacts to PageUp/PageDown"
```

---

## Task 6: `ScrollSpeedIndicator` component + integrate into PerformanceStage

**Files:**
- Create: `DTXMania.Game/Lib/Stage/Performance/ScrollSpeedIndicator.cs`
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs`

The indicator depends on `BitmapFont` which is graphics-dependent and excluded from Mac tests; therefore no automated unit tests for the indicator itself. We rely on manual verification.

- [ ] **Step 1: Create the indicator class**

Create `DTXMania.Game/Lib/Stage/Performance/ScrollSpeedIndicator.cs`:

```csharp
#nullable enable
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Brief on-screen toast that displays the current scroll-speed value
    /// for a short duration after the player adjusts it.
    /// </summary>
    public class ScrollSpeedIndicator
    {
        private readonly BitmapFont? _font;
        private string _text = string.Empty;
        private float _remainingSeconds;
        private bool _hasShown;

        public ScrollSpeedIndicator(BitmapFont? font)
        {
            _font = font;
        }

        public void Show(int scrollSpeedPercent)
        {
            _text = "Scroll Speed " + ScrollSpeedRange.Format(scrollSpeedPercent);
            _remainingSeconds = PerformanceUILayout.ScrollSpeedIndicatorDurationSeconds;
            _hasShown = true;
        }

        public void Update(GameTime gameTime)
        {
            if (_remainingSeconds <= 0f)
                return;
            _remainingSeconds -= (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_remainingSeconds < 0f)
                _remainingSeconds = 0f;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (!_hasShown || _remainingSeconds <= 0f || _font == null)
                return;

            var alpha = ComputeAlpha();
            var color = Color.White * alpha;
            _font.DrawText(spriteBatch, _text,
                PerformanceUILayout.ScrollSpeedIndicatorX,
                PerformanceUILayout.ScrollSpeedIndicatorY,
                color);
        }

        private float ComputeAlpha()
        {
            var fade = PerformanceUILayout.ScrollSpeedIndicatorFadeSeconds;
            if (_remainingSeconds >= fade)
                return 1f;
            return _remainingSeconds / fade;
        }
    }
}
```

If `BitmapFont.DrawText` has a different signature, adjust the call in `Draw` accordingly. Verify by `grep -n "public.*DrawText" DTXMania.Game/Lib/Resources/BitmapFont.cs`.

- [ ] **Step 2: Add the indicator field + lifecycle to PerformanceStage**

In `DTXMania.Game/Lib/Stage/PerformanceStage.cs`:

(a) Add a private field near the other component fields:

```csharp
private ScrollSpeedIndicator? _scrollSpeedIndicator;
```

(b) In the same place where other Performance components are constructed (search `new NoteRenderer(` to find the construction block), construct the indicator using the existing bitmap font reference (look for a `_bitmapFont` or equivalent field used by other components like `ComboDisplay`):

```csharp
_scrollSpeedIndicator = new ScrollSpeedIndicator(_bitmapFont);
```

If the font field has a different name (e.g., `_font`, `_consoleFont`), use that name. Verify with `grep -n "BitmapFont\|_bitmapFont\|_font\b" DTXMania.Game/Lib/Stage/PerformanceStage.cs`.

(c) In the `OnScrollSpeedChanged` handler from Task 5, add the `Show` call:

```csharp
private void OnScrollSpeedChanged(object? sender, ScrollSpeedChangedEventArgs e)
{
    _noteRenderer?.SetScrollSpeed(e.NewPercent);
    _scrollSpeedIndicator?.Show(e.NewPercent);
}
```

(d) In the per-frame `Update`, after other component updates, add:

```csharp
_scrollSpeedIndicator?.Update(gameTime);
```

(e) In the per-frame `Draw` (find where `_comboDisplay?.Draw(...)` or similar is called — the indicator should draw on top of lane content, after notes), add:

```csharp
_scrollSpeedIndicator?.Draw(_spriteBatch);
```

Use the actual `SpriteBatch` variable name from surrounding code.

- [ ] **Step 3: Build**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: SUCCESS.

- [ ] **Step 4: Run full Mac test suite**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Stage/Performance/ScrollSpeedIndicator.cs DTXMania.Game/Lib/Stage/PerformanceStage.cs
git commit -m "feat: add ScrollSpeedIndicator toast shown when scroll speed changes mid-song"
```

---

## Task 7: Wire `SongSelectionStage` to poll hotkeys + render label

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- Modify: `DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs`

- [ ] **Step 1: Add label position constants**

In `DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs`, add (placement: near other status-area constants; if uncertain, place at bottom of class):

```csharp
// Scroll-speed display (e.g., "Scroll: x1.5") on song-select panel
public const int ScrollSpeedLabelX = 20;
public const int ScrollSpeedLabelY = 680; // bottom-left, above standard status bar
```

If the file already groups label constants, place these next to them and match the naming style.

- [ ] **Step 2: Subscribe / unsubscribe to ScrollSpeedChanged**

In `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`, find lifecycle hooks (search `protected override void OnActivate\|protected override void OnDeactivate\|Activate(\|Deactivate(`).

Add to the activation method:

```csharp
var configManager = (_game as BaseGame)?.ConfigManager;
if (configManager != null)
{
    configManager.ScrollSpeedChanged += OnScrollSpeedChanged;
}
```

Add to the deactivation method:

```csharp
var configManager = (_game as BaseGame)?.ConfigManager;
if (configManager != null)
{
    configManager.ScrollSpeedChanged -= OnScrollSpeedChanged;
}
```

Add the handler:

```csharp
private void OnScrollSpeedChanged(object? sender, DTXMania.Game.Lib.Config.ScrollSpeedChangedEventArgs e)
{
    // Label re-renders each frame from current config; nothing to do here today.
    // Hook kept for symmetry and future caching.
}
```

- [ ] **Step 3: Poll hotkeys in OnUpdate**

Locate `OnUpdate` (line ~794) in `SongSelectionStage`. After existing input processing, add:

```csharp
var configManager = (_game as BaseGame)?.ConfigManager;
if (_inputManager != null && configManager != null)
{
    if (_inputManager.IsCommandPressed(InputCommandType.IncreaseScrollSpeed))
    {
        configManager.AdjustScrollSpeed(AppPaths.GetConfigFilePath(), +1);
    }
    else if (_inputManager.IsCommandPressed(InputCommandType.DecreaseScrollSpeed))
    {
        configManager.AdjustScrollSpeed(AppPaths.GetConfigFilePath(), -1);
    }
}
```

Add `using DTXMania.Game.Lib.Input;`, `using DTXMania.Game.Lib.Config;`, `using DTXMania.Game.Lib.Utilities;` if not present.

- [ ] **Step 4: Render the label**

Locate the `Draw` (or `OnDraw`) method in `SongSelectionStage`. After existing UI draw calls, add:

```csharp
var bf = _bitmapFont; // or the actual field; verify with grep -n "BitmapFont" DTXMania.Game/Lib/Stage/SongSelectionStage.cs
var cm = (_game as BaseGame)?.ConfigManager;
if (bf != null && cm != null)
{
    var label = "Scroll " + DTXMania.Game.Lib.Config.ScrollSpeedRange.Format(cm.Config.ScrollSpeed);
    bf.DrawText(_spriteBatch, label,
        SongSelectionUILayout.ScrollSpeedLabelX,
        SongSelectionUILayout.ScrollSpeedLabelY,
        Microsoft.Xna.Framework.Color.White);
}
```

If `SongSelectionStage` does not use a `BitmapFont` directly today, follow whatever text-rendering pattern other status labels in the stage use (search for existing label rendering, e.g., `grep -n "DrawText\|DrawString" DTXMania.Game/Lib/Stage/SongSelectionStage.cs`). If no font exists at all in this stage, instantiate one the same way `PerformanceStage` does (look at how the font is constructed there) — this is a hard prerequisite for displaying the label.

- [ ] **Step 5: Build**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: SUCCESS.

- [ ] **Step 6: Run full Mac test suite**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs
git commit -m "feat: SongSelectionStage shows scroll-speed label and adjusts via PageUp/PageDown"
```

---

## Task 8: Extend `IntegerConfigItem` with optional value formatter

**Files:**
- Modify: `DTXMania.Game/Lib/Config/ConfigItems.cs`

The Config menu needs to display "Scroll Speed: x1.5" rather than "Scroll Speed: 150". Smallest change: add an optional `Func<int, string>?` formatter to `IntegerConfigItem`.

- [ ] **Step 1: Modify the constructor and `GetDisplayText`**

In `DTXMania.Game/Lib/Config/ConfigItems.cs`, replace the `IntegerConfigItem` class with:

```csharp
/// <summary>
/// Configuration item for integer values with min/max bounds.
/// Optional valueFormatter customizes the displayed value (default: stringified integer).
/// </summary>
public class IntegerConfigItem : BaseConfigItem
{
    private readonly Func<int> _getCurrentValue;
    private readonly Action<int> _setValue;
    private readonly int _minValue;
    private readonly int _maxValue;
    private readonly int _step;
    private readonly Func<int, string>? _valueFormatter;

    public IntegerConfigItem(string name, Func<int> getCurrentValue, Action<int> setValue,
        int minValue, int maxValue, int step = 1, Func<int, string>? valueFormatter = null)
        : base(name)
    {
        _getCurrentValue = getCurrentValue ?? throw new ArgumentNullException(nameof(getCurrentValue));
        _setValue = setValue ?? throw new ArgumentNullException(nameof(setValue));
        _minValue = minValue;
        _maxValue = maxValue;
        _step = step;
        _valueFormatter = valueFormatter;

        if (_minValue >= _maxValue)
            throw new ArgumentException("Min value must be less than max value");
        if (_step <= 0)
            throw new ArgumentException("Step must be positive");
    }

    public override string GetDisplayText()
    {
        var currentValue = _getCurrentValue();
        var formatted = _valueFormatter != null ? _valueFormatter(currentValue) : currentValue.ToString();
        return $"{Name}: {formatted}";
    }

    public override void PreviousValue()
    {
        var currentValue = _getCurrentValue();
        var newValue = Math.Max(_minValue, currentValue - _step);
        _setValue(newValue);
        OnValueChanged();
    }

    public override void NextValue()
    {
        var currentValue = _getCurrentValue();
        var newValue = Math.Min(_maxValue, currentValue + _step);
        _setValue(newValue);
        OnValueChanged();
    }

    public override void ToggleValue()
    {
        // For integer, toggle acts like next value
        NextValue();
    }
}
```

- [ ] **Step 2: Build and run all tests**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: All tests pass — formatter parameter is optional and defaults to existing behavior.

- [ ] **Step 3: Commit**

```bash
git add DTXMania.Game/Lib/Config/ConfigItems.cs
git commit -m "feat: IntegerConfigItem accepts optional value formatter"
```

---

## Task 9: Add Scroll Speed item to `ConfigStage` + commit on save

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs`

`ConfigStage` uses a working-copy + commit-on-save model. The existing `LoadConfiguration` (line ~194) already copies `ScrollSpeed` into `_workingConfig`. The save path (line ~501) does NOT currently write `ScrollSpeed` back — we add that.

- [ ] **Step 1: Add the Scroll Speed config item**

In `DTXMania.Game/Lib/Stage/ConfigStage.cs`, find `SetupConfigItems` (line ~232). After the `autoPlayItem` definition (line ~289 area), and before the `_configItems.Add(new NavigationConfigItem(...))` calls, add:

```csharp
var scrollSpeedItem = new IntegerConfigItem(
    "Scroll Speed",
    () => _workingConfig.ScrollSpeed,
    value =>
    {
        _workingConfig.ScrollSpeed = ScrollSpeedRange.SnapAndClamp(value);
        _hasUnsavedChanges = true;
    },
    minValue: ScrollSpeedRange.Min,
    maxValue: ScrollSpeedRange.Max,
    step: ScrollSpeedRange.Step,
    valueFormatter: ScrollSpeedRange.Format);
```

Then, where the other items are added to `_configItems` (search for `_configItems.Add(autoPlayItem);` or the surrounding `Add` block), add:

```csharp
_configItems.Add(scrollSpeedItem);
```

Add `using DTXMania.Game.Lib.Config;` if not already imported.

- [ ] **Step 2: Persist ScrollSpeed on save**

In the same file, find the save block at line ~500 (the "Stage 1: prepare in-memory config data from working copies" section). After the line `config.AutoPlay = _workingConfig.AutoPlay;`, add:

```csharp
config.ScrollSpeed = _workingConfig.ScrollSpeed;
```

Find the rollback block at ~line 524 and add a corresponding rollback:

(a) Before the assignment block, capture the previous value alongside the others — find `bool prevAutoPlay = config.AutoPlay;` (~line 494) and add directly after:

```csharp
int prevScrollSpeed = config.ScrollSpeed;
```

(b) In the catch block, after `config.AutoPlay = prevAutoPlay;` (~line 529), add:

```csharp
config.ScrollSpeed = prevScrollSpeed;
```

- [ ] **Step 3: Build**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: SUCCESS.

- [ ] **Step 4: Run full Mac test suite**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Stage/ConfigStage.cs
git commit -m "feat: ConfigStage exposes Scroll Speed item and persists on save"
```

---

## Task 10: Manual verification

**Files:** none (gameplay testing).

This task has no code. It validates the user-facing behavior the spec promises but cannot cover with automated tests on Mac.

- [ ] **Step 1: Run the game**

Run: `dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj`

- [ ] **Step 2: Config menu persistence**

1. Enter Config menu.
2. Navigate to "Scroll Speed". Default reads as `x1.0`.
3. Press right arrow several times to reach `x2.5`. Confirm display updates step-by-step.
4. Save and exit.
5. Open `~/Library/Application Support/DTXMania/Config.ini` (or wherever `AppPaths.GetConfigFilePath()` resolves on your machine) and confirm `ScrollSpeed=250`.
6. Restart the app, re-enter Config menu, confirm "Scroll Speed: x2.5" still shown.

- [ ] **Step 3: Song-select adjust**

1. From the title screen, enter Song Selection.
2. Confirm "Scroll x2.5" label visible at the configured position.
3. Press `PageUp` → label changes to `x3.0`. Press repeatedly → caps at `x4.0`.
4. Press `PageDown` repeatedly → drops to `x0.5` and stops.
5. Open `Config.ini` after each change to verify write-through.

- [ ] **Step 4: In-game adjust**

1. Set scroll speed to `x1.0` (via song-select PageDown).
2. Start a song.
3. Press `PageUp` mid-song → toast appears top-center reading "Scroll Speed x1.5"; notes visibly fall faster.
4. Press `PageUp` again → toast updates to `x2.0`. Wait for toast to fade out (~1.5s).
5. Press `PageDown` → toast shows `x1.5`, notes slow down.
6. Confirm: judgement timing still feels correct (hits register against the audio, not the visuals).
7. Confirm: hold `PageUp` does NOT continuously scroll through values — it must be pressed each time (edge-triggered).

- [ ] **Step 5: Boundary behavior**

1. Press `PageUp` until toast shows `x4.0`. Press `PageUp` again — no visible change, no new toast.
2. Press `PageDown` until `x0.5`. Press `PageDown` again — same.

- [ ] **Step 6: Cross-stage consistency**

1. After in-game adjust to `x3.0`, exit to song select. Confirm label reads `x3.0`.
2. Exit to Config menu. Confirm "Scroll Speed: x3.0".
3. Confirm `Config.ini` reads `ScrollSpeed=300`.

- [ ] **Step 7: Document any deviations**

If any step fails, add a TODO comment in this plan describing the failure (do NOT mark it complete) and report back before proceeding to merge.

---

## Self-Review

**Spec coverage check:**

| Spec section | Implemented in |
| --- | --- |
| §2 Range/step/storage/format | Task 1 (`ScrollSpeedRange`), Task 3 (snap on set) |
| §2 Adjustable from Config menu | Tasks 8 + 9 |
| §2 Adjustable from song-select | Task 7 |
| §2 Adjustable in-game | Tasks 4, 5 |
| §2 Persistence (write-through) | Task 3 (`SetScrollSpeed` calls `SaveConfig`) |
| §2 No audio/judgement effect | Inherited from existing NoteRenderer behavior; verified manually in Task 10 step 4 |
| §2 Boundaries (no wrap) | Task 1 (`SnapAndClamp`); Task 10 step 5 verifies |
| §3.1 `IConfigManager` event + methods | Task 3 |
| §3.2 `ScrollSpeedRange` helper | Task 1 |
| §3.3 Input enum + bindings | Task 4 |
| §3.4 PerformanceStage subscribe + poll | Task 5 |
| §3.4 SongSelectStage subscribe + poll + label | Task 7 |
| §3.4 ConfigStage item | Tasks 8, 9 |
| §3.5 ScrollSpeedIndicator | Task 6 |
| §4 Data flow | Tasks 3, 5, 6, 7 collectively |
| §5 Mid-song correctness | No code — relies on existing `NoteRenderer` per-frame behavior (verified in spec) |
| §6 Error handling (Save failure) | Task 3 step 5 (try/catch around `SaveConfig`) |
| §6 Edge-triggered (no hold-repeat) | Task 5 step 4, Task 7 step 3 (uses `IsCommandPressed`); Task 10 step 4 verifies |
| §6 Unsubscribe on deactivate | Task 5 step 3, Task 7 step 2 |
| §7.1 Unit tests (ConfigManager) | Task 3 step 1 |
| §7.1 Unit tests (ScrollSpeedRange) | Task 1 step 1 |
| §7.2 Input tests | Task 4 step 1 |
| §7.4 Manual checklist | Task 10 |

All spec items mapped to a task.

**Placeholder scan:** No "TBD"/"TODO"/"add appropriate" inside code blocks. The plan does say "if the field has a different name" in a couple of places — those are explicit verification instructions for the engineer with the exact `grep` command to run, not vague handwaving. Acceptable.

**Type consistency check:**
- `SetScrollSpeed(string configFilePath, int percent)` — same signature in IConfigManager, ConfigManager impl, and all callers (Tasks 5, 7).
- `AdjustScrollSpeed(string configFilePath, int stepDelta)` — same.
- `ScrollSpeedChanged` event — `EventHandler<ScrollSpeedChangedEventArgs>?` consistently.
- `ScrollSpeedRange` members `Min/Max/Step/Default/SnapAndClamp/Format` — same names everywhere referenced.
- `InputCommandType.IncreaseScrollSpeed` / `DecreaseScrollSpeed` — same in enum, bindings, polling.
- `ScrollSpeedIndicator.Show(int)` / `.Update(GameTime)` / `.Draw(SpriteBatch)` — consistent in Task 6.
- `PerformanceUILayout.ScrollSpeedIndicator{X,Y,DurationSeconds,FadeSeconds}` — consistent in Task 5 (defs) and Task 6 (uses).
- `SongSelectionUILayout.ScrollSpeedLabel{X,Y}` — consistent in Task 7.

No type drift detected.
