# Interactive Drum Mapping Stage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a new `DrumConfigStage` that shows a drawn drum kit; the user clicks (or keyboard-focuses) a drum piece, hits any input device, and that input is bound to the piece's lane — replacing the keyboard-only list panel.

**Architecture:** A new standalone stage (`StageType.DrumConfig`), reached from the existing ConfigStage "Drum Key Mapping" menu item. Pure logic (kit layout/hit-test, capture popup state machine) is split from rendering so it is unit-testable headlessly. A small device-agnostic "buttons pressed this frame" feed is added to the input layer so keyboard works today and future MIDI/gamepad sources bind with no UI change.

**Tech Stack:** .NET 8, MonoGame 3.8, xUnit + Moq. Game code in `DTXMania.Game/Lib/...`; tests in `DTXMania.Test/...` (Mac-safe subset must stay green).

**Branch:** `feat/interactive-drum-mapping-stage`

**Build/test commands (Mac):**
- Build game: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
- Run Mac test suite: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
- Run one test class: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DrumKitLayoutTests"`

---

## File Structure

**New game files** (auto-compiled; game csproj uses default compile items):
- `DTXMania.Game/Lib/Stage/DrumConfig/DrumKitLayout.cs` — zone geometry + hit-test (pure logic).
- `DTXMania.Game/Lib/Stage/DrumConfig/DrumCapturePopup.cs` — capture state machine + popup geometry (logic; `Draw` is the only graphics method).
- `DTXMania.Game/Lib/Stage/DrumConfig/DrumKitRenderer.cs` — draws the kit + chips + highlights (graphics only).
- `DTXMania.Game/Lib/Stage/DrumConfigStage.cs` — orchestrator stage.

**New test files** (Mac-safe; no `GraphicsDevice`):
- `DTXMania.Test/Stage/DrumConfig/DrumKitLayoutTests.cs`
- `DTXMania.Test/Stage/DrumConfig/DrumCapturePopupTests.cs`

**Modified files:**
- `DTXMania.Game/Lib/Input/InputRouter.cs` — add `OnButtonPressed` event.
- `DTXMania.Game/Lib/Input/ModularInputManager.cs` — add `ConsumePressedButtons()`.
- `DTXMania.Game/Lib/Stage/IStageManager.cs` — add `StageType.DrumConfig`.
- `DTXMania.Game/Lib/Stage/StageManager.cs` — factory case.
- `DTXMania.Game/Lib/Stage/ConfigStage.cs` — repoint the "Drum Key Mapping" menu item.
- `DTXMania.Test/Input/InputRouterTests.cs` — test for `OnButtonPressed`.
- `DTXMania.Test/Input/ModularInputManagerInjectionTests.cs` — tests for `ConsumePressedButtons()`.

---

## Task 1: Add `OnButtonPressed` event to InputRouter

**Files:**
- Modify: `DTXMania.Game/Lib/Input/InputRouter.cs`
- Test: `DTXMania.Test/Input/InputRouterTests.cs`

- [ ] **Step 1: Write the failing test**

Add this test inside the `InputRouterTests` class in `DTXMania.Test/Input/InputRouterTests.cs`:

```csharp
[Fact]
public void Update_WhenSourceYieldsPressedButton_RaisesOnButtonPressed()
{
    var source = new Mock<IInputSource>();
    source.Setup(s => s.Update()).Returns(new[] { new ButtonState("MIDI.38", true) });
    _router.AddInputSource(source.Object);

    ButtonState captured = null;
    _router.OnButtonPressed += (_, b) => captured = b;

    _router.Update();

    Assert.NotNull(captured);
    Assert.Equal("MIDI.38", captured.Id);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~InputRouterTests.Update_WhenSourceYieldsPressedButton_RaisesOnButtonPressed"`
Expected: FAIL — compile error `'InputRouter' does not contain a definition for 'OnButtonPressed'`.

- [ ] **Step 3: Add the event and raise it**

In `DTXMania.Game/Lib/Input/InputRouter.cs`, add the event next to `OnLaneHit` (after line 21):

```csharp
        /// <summary>
        /// Raised for every button that transitioned to pressed this frame, from any source.
        /// Device-agnostic feed used by key-binding capture UIs. May be null if no subscribers.
        /// </summary>
        public event EventHandler<ButtonState>? OnButtonPressed;
```

In `Update()`, raise it for each pressed button. Replace the loop body so it reads:

```csharp
            foreach (var source in _inputSources)
            {
                foreach (var buttonState in source.Update())
                {
                    if (buttonState.IsPressed)
                    {
                        OnButtonPressed?.Invoke(this, buttonState);
                        ProcessButtonState(buttonState);
                    }
                }
            }
```

In `Dispose(bool disposing)`, null the handler next to `OnLaneHit = null;`:

```csharp
                OnLaneHit = null;
                OnButtonPressed = null;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~InputRouterTests.Update_WhenSourceYieldsPressedButton_RaisesOnButtonPressed"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Input/InputRouter.cs DTXMania.Test/Input/InputRouterTests.cs
git commit -m "feat: add InputRouter.OnButtonPressed device-agnostic feed"
```

---

## Task 2: Add `ConsumePressedButtons()` to ModularInputManager

**Files:**
- Modify: `DTXMania.Game/Lib/Input/ModularInputManager.cs`
- Test: `DTXMania.Test/Input/ModularInputManagerInjectionTests.cs`

- [ ] **Step 1: Write the failing tests**

Add these two tests to `ModularInputManagerInjectionTests` in `DTXMania.Test/Input/ModularInputManagerInjectionTests.cs`:

```csharp
[Fact]
public void ConsumePressedButtons_AfterInjectedPress_ReturnsThatButton()
{
    _manager.InjectButton("Key.Q", isPressed: true);
    _manager.Update(0.016);

    var pressed = _manager.ConsumePressedButtons();

    Assert.Contains(pressed, b => b.Id == "Key.Q");
}

[Fact]
public void ConsumePressedButtons_FrameAfterPress_ReturnsEmpty()
{
    _manager.InjectButton("Key.Q", isPressed: true);
    _manager.Update(0.016);
    _manager.ConsumePressedButtons();

    _manager.Update(0.016); // no new input this frame

    Assert.Empty(_manager.ConsumePressedButtons());
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ModularInputManagerInjectionTests.ConsumePressedButtons"`
Expected: FAIL — compile error `'ModularInputManager' does not contain a definition for 'ConsumePressedButtons'`.

- [ ] **Step 3: Implement the per-frame buffer**

In `DTXMania.Game/Lib/Input/ModularInputManager.cs`:

(a) Add a field in the Private Fields region (after line 33, near `_injectedPressEvents`):

```csharp
        // Buttons that transitioned to pressed this frame (any source + injected).
        // Rebuilt every Update(); drained by ConsumePressedButtons() for binding-capture UIs.
        private readonly List<ButtonState> _pressedThisFrame = new();
```

(b) In the constructor, subscribe to the router event next to the existing `OnLaneHit` wiring (after line 104 `_inputRouter.OnLaneHit += OnInputRouterLaneHit;`):

```csharp
            _inputRouter.OnButtonPressed += OnInputRouterButtonPressed;
```

(c) In `Update()`, clear the buffer at the start of the frame. Immediately after `_updateStopwatch.Restart();` (line 156), add:

```csharp
            _pressedThisFrame.Clear();
```

(d) In `ProcessInjectedInputs()`, record pressed injected buttons. Inside the `while (_injectedButtonQueue.TryDequeue(out var injected))` loop, as the first statement of the loop body, add:

```csharp
                if (injected.IsPressed)
                    _pressedThisFrame.Add(injected);
```

(e) Add the event handler and the public accessor in the Event Handlers / Utility region:

```csharp
        private void OnInputRouterButtonPressed(object sender, ButtonState button)
        {
            _pressedThisFrame.Add(button);
        }

        /// <summary>
        /// Returns the buttons that transitioned to pressed during the most recent Update(),
        /// from any input source (keyboard now; MIDI/gamepad once those sources exist) plus
        /// injected inputs. The buffer is rebuilt each Update(), so callers should read it once
        /// per frame. Device-agnostic: each entry's Id is a "Key.*"/"MIDI.*"/"Pad.*" string.
        /// </summary>
        public IReadOnlyList<ButtonState> ConsumePressedButtons()
        {
            return _pressedThisFrame.ToArray();
        }
```

(f) In `Dispose(bool disposing)`, unsubscribe next to the existing `_inputRouter.OnLaneHit -= OnInputRouterLaneHit;` (line 575):

```csharp
                    _inputRouter.OnButtonPressed -= OnInputRouterButtonPressed;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ModularInputManagerInjectionTests.ConsumePressedButtons"`
Expected: PASS (both).

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Input/ModularInputManager.cs DTXMania.Test/Input/ModularInputManagerInjectionTests.cs
git commit -m "feat: add ModularInputManager.ConsumePressedButtons per-frame feed"
```

---

## Task 3: DrumKitLayout (zone geometry + hit-test)

**Files:**
- Create: `DTXMania.Game/Lib/Stage/DrumConfig/DrumKitLayout.cs`
- Test: `DTXMania.Test/Stage/DrumConfig/DrumKitLayoutTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `DTXMania.Test/Stage/DrumConfig/DrumKitLayoutTests.cs`:

```csharp
using System.Linq;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.DrumConfig;
using Xunit;

namespace DTXMania.Test.Stage.DrumConfig
{
    [Trait("Category", "Unit")]
    public class DrumKitLayoutTests
    {
        [Fact]
        public void Zones_CoverAllTenLanesExactlyOnce()
        {
            var lanes = DrumKitLayout.Zones.Select(z => z.Lane).OrderBy(l => l).ToArray();
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, lanes);
        }

        [Fact]
        public void Zone_NameMatchesKeyBindingsLaneName()
        {
            var snare = DrumKitLayout.Zones.Single(z => z.Lane == 4);
            Assert.Equal(KeyBindings.GetLaneName(4), snare.Name);
        }

        [Fact]
        public void HitTest_AtSnareCenter_ReturnsLane4()
        {
            var snare = DrumKitLayout.Zones.Single(z => z.Lane == 4);
            Assert.Equal(4, DrumKitLayout.HitTest(snare.CenterX, snare.CenterY));
        }

        [Fact]
        public void HitTest_FarOutsideAnyZone_ReturnsMinusOne()
        {
            Assert.Equal(-1, DrumKitLayout.HitTest(2f, 2f));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DrumKitLayoutTests"`
Expected: FAIL — `DrumKitLayout` / `DrumConfig` namespace does not exist.

- [ ] **Step 3: Create DrumKitLayout**

Create `DTXMania.Game/Lib/Stage/DrumConfig/DrumKitLayout.cs`:

```csharp
#nullable enable

using System.Collections.Generic;
using DTXMania.Game.Lib.Input;

namespace DTXMania.Game.Lib.Stage.DrumConfig
{
    /// <summary>Visual shape used when drawing a drum zone.</summary>
    public enum DrumZoneShape { Cymbal, Drum, Pedal, Kick }

    /// <summary>
    /// One clickable drum-kit zone, mapped 1:1 to a drum lane (0-9).
    /// Geometry is defined in a fixed design space (<see cref="DrumKitLayout.DesignWidth"/> x
    /// <see cref="DrumKitLayout.DesignHeight"/>); callers scale screen/mouse coords into it.
    /// </summary>
    public readonly struct DrumZone
    {
        public int Lane { get; }
        public string Name { get; }
        public DrumZoneShape Shape { get; }
        public float CenterX { get; }
        public float CenterY { get; }
        public float RadiusX { get; }
        public float RadiusY { get; }

        public DrumZone(int lane, string name, DrumZoneShape shape,
                        float centerX, float centerY, float radiusX, float radiusY)
        {
            Lane = lane;
            Name = name;
            Shape = shape;
            CenterX = centerX;
            CenterY = centerY;
            RadiusX = radiusX;
            RadiusY = radiusY;
        }

        /// <summary>Ellipse containment test in design space.</summary>
        public bool Contains(float x, float y)
        {
            var dx = (x - CenterX) / RadiusX;
            var dy = (y - CenterY) / RadiusY;
            return (dx * dx) + (dy * dy) <= 1f;
        }
    }

    /// <summary>
    /// Static drum-kit zone layout. Lane names come from <see cref="KeyBindings.GetLaneName"/>
    /// so the visual labels stay in sync with the binding model.
    /// </summary>
    public static class DrumKitLayout
    {
        public const int DesignWidth = 1280;
        public const int DesignHeight = 720;

        public static IReadOnlyList<DrumZone> Zones { get; } = new[]
        {
            new DrumZone(5, KeyBindings.GetLaneName(5), DrumZoneShape.Cymbal, 166f, 158f, 70f, 22f),
            new DrumZone(0, KeyBindings.GetLaneName(0), DrumZoneShape.Cymbal, 435f,  86f, 75f, 22f),
            new DrumZone(9, KeyBindings.GetLaneName(9), DrumZoneShape.Cymbal, 1024f, 115f, 78f, 22f),
            new DrumZone(7, KeyBindings.GetLaneName(7), DrumZoneShape.Drum,   525f, 295f, 42f, 42f),
            new DrumZone(8, KeyBindings.GetLaneName(8), DrumZoneShape.Drum,   755f, 295f, 46f, 46f),
            new DrumZone(4, KeyBindings.GetLaneName(4), DrumZoneShape.Drum,   346f, 432f, 50f, 50f),
            new DrumZone(1, KeyBindings.GetLaneName(1), DrumZoneShape.Drum,  1037f, 418f, 56f, 56f),
            new DrumZone(6, KeyBindings.GetLaneName(6), DrumZoneShape.Kick,   627f, 526f, 80f, 80f),
            new DrumZone(2, KeyBindings.GetLaneName(2), DrumZoneShape.Pedal,  179f, 619f, 48f, 18f),
            new DrumZone(3, KeyBindings.GetLaneName(3), DrumZoneShape.Pedal,  422f, 641f, 48f, 18f),
        };

        /// <summary>Returns the lane of the first zone containing the design-space point, or -1.</summary>
        public static int HitTest(float x, float y)
        {
            foreach (var zone in Zones)
            {
                if (zone.Contains(x, y))
                    return zone.Lane;
            }
            return -1;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DrumKitLayoutTests"`
Expected: PASS (all 4).

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Stage/DrumConfig/DrumKitLayout.cs DTXMania.Test/Stage/DrumConfig/DrumKitLayoutTests.cs
git commit -m "feat: add DrumKitLayout zone geometry and hit-test"
```

---

## Task 4: DrumCapturePopup (capture state machine)

**Files:**
- Create: `DTXMania.Game/Lib/Stage/DrumConfig/DrumCapturePopup.cs`
- Test: `DTXMania.Test/Stage/DrumConfig/DrumCapturePopupTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `DTXMania.Test/Stage/DrumConfig/DrumCapturePopupTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.DrumConfig;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace DTXMania.Test.Stage.DrumConfig
{
    [Trait("Category", "Unit")]
    public class DrumCapturePopupTests
    {
        private readonly KeyBindings _bindings = new();
        private readonly Dictionary<Keys, InputCommandType> _system = new()
        {
            [Keys.Enter] = InputCommandType.Activate,           // required
            [Keys.PageUp] = InputCommandType.IncreaseScrollSpeed, // non-required
        };
        private readonly List<Keys> _evicted = new();

        private DrumCapturePopup NewPopup() =>
            new(_bindings, () => _system, key => _evicted.Add(key));

        [Fact]
        public void Open_SetsListeningStateAndLane()
        {
            var popup = NewPopup();
            popup.Open(4);
            Assert.True(popup.IsOpen);
            Assert.Equal(DrumCaptureState.Listening, popup.State);
            Assert.Equal(4, popup.Lane);
        }

        [Fact]
        public void TryCapture_UnboundKey_AppendsBindingToLane()
        {
            var popup = NewPopup();
            popup.Open(4);

            var outcome = popup.TryCapture(new ButtonState("Key.Q", true));

            Assert.Equal(DrumCaptureOutcome.Captured, outcome);
            Assert.Contains("Key.Q", _bindings.GetButtonsForLane(4));
            Assert.Contains("Key.S", _bindings.GetButtonsForLane(4)); // default still present (append)
        }

        [Fact]
        public void TryCapture_RequiredNavKey_RejectsWithoutBinding()
        {
            var popup = NewPopup();
            popup.Open(4);

            var outcome = popup.TryCapture(new ButtonState("Key.Enter", true));

            Assert.Equal(DrumCaptureOutcome.Rejected, outcome);
            Assert.Equal(DrumCaptureState.ShowingConflict, popup.State);
            Assert.DoesNotContain("Key.Enter", _bindings.GetButtonsForLane(4));
        }

        [Fact]
        public void TryCapture_NonRequiredSystemKey_EvictsAndBinds()
        {
            var popup = NewPopup();
            popup.Open(7);

            var outcome = popup.TryCapture(new ButtonState("Key.PageUp", true));

            Assert.Equal(DrumCaptureOutcome.Captured, outcome);
            Assert.Contains(Keys.PageUp, _evicted);
            Assert.Contains("Key.PageUp", _bindings.GetButtonsForLane(7));
        }

        [Fact]
        public void TryCapture_NonKeyboardButton_BindsWithoutSystemCheck()
        {
            var popup = NewPopup();
            popup.Open(6);

            var outcome = popup.TryCapture(new ButtonState("MIDI.36", true));

            Assert.Equal(DrumCaptureOutcome.Captured, outcome);
            Assert.Contains("MIDI.36", _bindings.GetButtonsForLane(6));
            Assert.Empty(_evicted);
        }

        [Fact]
        public void RemoveBinding_And_ClearLane_MutateWorkingBindings()
        {
            var popup = NewPopup();
            popup.Open(4);

            popup.RemoveBinding("Key.S");
            Assert.DoesNotContain("Key.S", _bindings.GetButtonsForLane(4));

            popup.TryCapture(new ButtonState("Key.Q", true));
            popup.ClearLane();
            Assert.Empty(_bindings.GetButtonsForLane(4));
        }

        [Fact]
        public void Tick_AfterConflict_ReturnsToListening()
        {
            var popup = NewPopup();
            popup.Open(4);
            popup.TryCapture(new ButtonState("Key.Enter", true)); // -> ShowingConflict

            popup.Tick(2.5);

            Assert.Equal(DrumCaptureState.Listening, popup.State);
            Assert.Null(popup.ConflictMessage);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DrumCapturePopupTests"`
Expected: FAIL — `DrumCapturePopup` does not exist.

- [ ] **Step 3: Implement the popup logic**

Create `DTXMania.Game/Lib/Stage/DrumConfig/DrumCapturePopup.cs`:

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.KeyAssign;

namespace DTXMania.Game.Lib.Stage.DrumConfig
{
    public enum DrumCaptureState { Closed, Listening, ShowingConflict }
    public enum DrumCaptureOutcome { Ignored, Captured, Rejected }

    /// <summary>
    /// Modal capture for a single drum lane: listens for the next input from any device and
    /// appends it to the lane's bindings. Keyboard keys are checked against system bindings —
    /// required navigation keys are rejected; non-required system keys are auto-evicted
    /// (mirrors DrumKeyAssignPanel). Pure state/geometry; <see cref="Draw"/> is the only
    /// graphics method and is exercised only by the stage.
    /// </summary>
    public class DrumCapturePopup
    {
        private const double ConflictDuration = 2.0;

        private readonly KeyBindings _workingBindings;
        private readonly Func<IReadOnlyDictionary<Keys, InputCommandType>> _systemMappingProvider;
        private readonly Action<Keys> _evictSystemBinding;
        private double _conflictTimer;

        public DrumCaptureState State { get; private set; } = DrumCaptureState.Closed;
        public int Lane { get; private set; } = -1;
        public string? ConflictMessage { get; private set; }
        public bool IsOpen => State != DrumCaptureState.Closed;

        public DrumCapturePopup(
            KeyBindings workingBindings,
            Func<IReadOnlyDictionary<Keys, InputCommandType>> systemMappingProvider,
            Action<Keys> evictSystemBinding)
        {
            _workingBindings = workingBindings ?? throw new ArgumentNullException(nameof(workingBindings));
            _systemMappingProvider = systemMappingProvider ?? throw new ArgumentNullException(nameof(systemMappingProvider));
            _evictSystemBinding = evictSystemBinding ?? throw new ArgumentNullException(nameof(evictSystemBinding));
        }

        public void Open(int lane)
        {
            Lane = lane;
            State = DrumCaptureState.Listening;
            ConflictMessage = null;
            _conflictTimer = 0;
        }

        public void Close()
        {
            State = DrumCaptureState.Closed;
            Lane = -1;
            ConflictMessage = null;
        }

        /// <summary>Advances the conflict-notice timer; returns to Listening when it expires.</summary>
        public void Tick(double deltaTime)
        {
            if (State != DrumCaptureState.ShowingConflict)
                return;

            _conflictTimer -= deltaTime;
            if (_conflictTimer <= 0)
            {
                State = DrumCaptureState.Listening;
                ConflictMessage = null;
            }
        }

        /// <summary>Attempts to bind a captured button to the current lane (append model).</summary>
        public DrumCaptureOutcome TryCapture(ButtonState button)
        {
            if (State != DrumCaptureState.Listening || button == null || string.IsNullOrWhiteSpace(button.Id))
                return DrumCaptureOutcome.Ignored;

            if (TryParseKey(button.Id, out var key))
            {
                var system = _systemMappingProvider();
                var required = KeyConflictChecker.GetRequiredSystemConflict(system, key);
                if (required != null)
                {
                    ConflictMessage = $"{key} is reserved for system action: {required}";
                    State = DrumCaptureState.ShowingConflict;
                    _conflictTimer = ConflictDuration;
                    return DrumCaptureOutcome.Rejected;
                }

                // Non-required system key (e.g. IncreaseScrollSpeed): evict so the lane can claim it.
                if (system.ContainsKey(key))
                    _evictSystemBinding(key);
            }

            _workingBindings.BindButton(button.Id, Lane);
            return DrumCaptureOutcome.Captured;
        }

        public void RemoveBinding(string buttonId) => _workingBindings.UnbindButton(buttonId);

        public void ClearLane() => _workingBindings.UnbindLane(Lane);

        public IReadOnlyList<string> CurrentBindings =>
            _workingBindings.GetButtonsForLane(Lane).ToList();

        private static bool TryParseKey(string buttonId, out Keys key)
        {
            key = default;
            const string prefix = "Key.";
            if (!buttonId.StartsWith(prefix, StringComparison.Ordinal))
                return false;
            return Enum.TryParse(buttonId.Substring(prefix.Length), out key);
        }

        // ---- Geometry shared by rendering and mouse hit-testing (design space) ----

        public const int PopupWidth = 380;
        public const int PopupHeight = 230;

        public Rectangle GetPanelRect(int viewportWidth, int viewportHeight) =>
            new((viewportWidth - PopupWidth) / 2, (viewportHeight - PopupHeight) / 2, PopupWidth, PopupHeight);

        public Rectangle GetDoneRect(int viewportWidth, int viewportHeight)
        {
            var p = GetPanelRect(viewportWidth, viewportHeight);
            return new Rectangle(p.Right - 92, p.Bottom - 44, 80, 30);
        }

        public Rectangle GetClearRect(int viewportWidth, int viewportHeight)
        {
            var p = GetPanelRect(viewportWidth, viewportHeight);
            return new Rectangle(p.Left + 12, p.Bottom - 44, 90, 30);
        }

        /// <summary>
        /// Draws the popup. Called only by the stage (graphics). No unit test.
        /// </summary>
        public void Draw(SpriteBatch spriteBatch, IFont? font, Texture2D? whitePixel,
                         int viewportWidth, int viewportHeight)
        {
            if (!IsOpen || spriteBatch == null)
                return;

            if (whitePixel != null)
            {
                spriteBatch.Draw(whitePixel, new Rectangle(0, 0, viewportWidth, viewportHeight),
                    new Color(0, 0, 0, 180));
                var panel = GetPanelRect(viewportWidth, viewportHeight);
                spriteBatch.Draw(whitePixel, panel, new Color(27, 31, 41));
                spriteBatch.Draw(whitePixel, GetClearRect(viewportWidth, viewportHeight), new Color(58, 35, 48));
                spriteBatch.Draw(whitePixel, GetDoneRect(viewportWidth, viewportHeight), new Color(58, 70, 90));
            }

            if (font == null)
                return;

            var panelRect = GetPanelRect(viewportWidth, viewportHeight);
            int x = panelRect.X + 16;
            int y = panelRect.Y + 14;

            font.DrawString(spriteBatch, $"Configure: {KeyBindings.GetLaneName(Lane)}",
                new Vector2(x, y), Color.White);
            y += 30;
            font.DrawString(spriteBatch, $"Bound: {_workingBindings.GetLaneDescription(Lane)}",
                new Vector2(x, y), new Color(180, 200, 220));
            y += 34;

            var prompt = State == DrumCaptureState.ShowingConflict
                ? (ConflictMessage ?? "Conflict")
                : "Listening - hit any key, pad, or MIDI note";
            var promptColor = State == DrumCaptureState.ShowingConflict ? Color.Red : new Color(255, 216, 77);
            font.DrawString(spriteBatch, prompt, new Vector2(x, y), promptColor);

            font.DrawString(spriteBatch, "Clear", new Vector2(GetClearRect(viewportWidth, viewportHeight).X + 14,
                GetClearRect(viewportWidth, viewportHeight).Y + 6), Color.White);
            font.DrawString(spriteBatch, "Done", new Vector2(GetDoneRect(viewportWidth, viewportHeight).X + 18,
                GetDoneRect(viewportWidth, viewportHeight).Y + 6), Color.White);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DrumCapturePopupTests"`
Expected: PASS (all 7).

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Stage/DrumConfig/DrumCapturePopup.cs DTXMania.Test/Stage/DrumConfig/DrumCapturePopupTests.cs
git commit -m "feat: add DrumCapturePopup capture state machine"
```

---

## Task 5: DrumKitRenderer (drawing)

**Files:**
- Create: `DTXMania.Game/Lib/Stage/DrumConfig/DrumKitRenderer.cs`

No unit test (pure rendering, verified by build + running the game). The renderer generates a soft circle texture once and draws each zone scaled to its ellipse size, plus a highlight ring and the binding text.

- [ ] **Step 1: Create the renderer**

Create `DTXMania.Game/Lib/Stage/DrumConfig/DrumKitRenderer.cs`:

```csharp
#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;

namespace DTXMania.Game.Lib.Stage.DrumConfig
{
    /// <summary>
    /// Draws the drum-kit zones (ellipses), their current binding text, and selection/focus/hover
    /// highlights. Coordinates are scaled from DrumKitLayout design space to the viewport.
    /// </summary>
    public sealed class DrumKitRenderer : IDisposable
    {
        private readonly Texture2D _circle;
        private bool _disposed;

        public DrumKitRenderer(GraphicsDevice graphicsDevice)
        {
            _circle = CreateCircleTexture(graphicsDevice, 128);
        }

        private static Color ZoneColor(DrumZoneShape shape) => shape switch
        {
            DrumZoneShape.Cymbal => new Color(214, 177, 60),
            DrumZoneShape.Drum => new Color(60, 110, 170),
            DrumZoneShape.Kick => new Color(40, 44, 56),
            DrumZoneShape.Pedal => new Color(74, 79, 90),
            _ => Color.Gray
        };

        public void Draw(SpriteBatch spriteBatch, IFont? font, Texture2D whitePixel,
                         KeyBindings bindings, int viewportWidth, int viewportHeight,
                         int selectedLane, int focusedLane, int hoveredLane)
        {
            float sx = viewportWidth / (float)DrumKitLayout.DesignWidth;
            float sy = viewportHeight / (float)DrumKitLayout.DesignHeight;

            foreach (var zone in DrumKitLayout.Zones)
            {
                var dest = new Rectangle(
                    (int)((zone.CenterX - zone.RadiusX) * sx),
                    (int)((zone.CenterY - zone.RadiusY) * sy),
                    (int)(zone.RadiusX * 2 * sx),
                    (int)(zone.RadiusY * 2 * sy));

                bool highlight = zone.Lane == selectedLane || zone.Lane == focusedLane || zone.Lane == hoveredLane;
                if (highlight)
                {
                    var ring = dest;
                    ring.Inflate(5, 5);
                    spriteBatch.Draw(_circle, ring, new Color(255, 216, 77));
                }

                spriteBatch.Draw(_circle, dest, ZoneColor(zone.Shape));

                if (font != null)
                {
                    var labelPos = new Vector2(dest.Center.X - 28, dest.Center.Y - 8);
                    font.DrawString(spriteBatch, KeyBindings.GetLaneName(zone.Lane), labelPos, Color.White);
                    var chipPos = new Vector2(dest.Center.X - 28, dest.Bottom + 2);
                    font.DrawString(spriteBatch, bindings.GetLaneDescription(zone.Lane), chipPos,
                        new Color(200, 220, 235));
                }
            }
        }

        private static Texture2D CreateCircleTexture(GraphicsDevice device, int size)
        {
            var data = new Color[size * size];
            float r = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - r + 0.5f;
                    float dy = y - r + 0.5f;
                    float dist = (float)Math.Sqrt((dx * dx) + (dy * dy));
                    data[(y * size) + x] = dist <= r ? Color.White : Color.Transparent;
                }
            }
            var tex = new Texture2D(device, size, size);
            tex.SetData(data);
            return tex;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _circle.Dispose();
            _disposed = true;
        }
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add DTXMania.Game/Lib/Stage/DrumConfig/DrumKitRenderer.cs
git commit -m "feat: add DrumKitRenderer for the drum-config kit"
```

---

## Task 6: DrumConfigStage (orchestrator)

**Files:**
- Create: `DTXMania.Game/Lib/Stage/DrumConfigStage.cs`

Integration glue (graphics + mouse + ConfigManager); verified by build + running the app. Mirrors ConfigStage's resource setup. Note `StageType.DrumConfig` does not exist yet — this task will not build until Task 7 adds it, so build verification happens at the end of Task 7. (Implement this file now; the commit at Task 6 Step 2 is allowed to be made together with Task 7 if you prefer to keep the tree building — see note.)

- [ ] **Step 1: Create the stage**

Create `DTXMania.Game/Lib/Stage/DrumConfigStage.cs`:

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.DrumConfig;
using DTXMania.Game.Lib.Utilities;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Visual drum-mapping stage. Shows a drawn kit; the user selects a piece (mouse or keyboard)
    /// and hits any input device to bind it to that lane. Edits a working copy; commits on Save.
    /// </summary>
    public class DrumConfigStage : BaseStage
    {
        private const int LaneCount = 10;

        private IConfigManager _configManager = null!;
        private InputManagerCompat? _input;

        private KeyBindings _workingBindings = new();
        private Dictionary<Keys, InputCommandType> _workingSystemBindings = new();

        private SpriteBatch _spriteBatch = null!;
        private Texture2D _whitePixel = null!;
        private IFont? _font;
        private IResourceManager _resourceManager = null!;
        private DrumKitRenderer? _renderer;
        private DrumCapturePopup? _popup;

        private int _focusedLane;
        private int _hoveredLane = -1;
        private int _selectedLane = -1;
        private bool _skipCaptureThisFrame;

        private MouseState _previousMouse;

        public override StageType Type => StageType.DrumConfig;

        public DrumConfigStage(BaseGame game) : base(game)
        {
            _configManager = game.ConfigManager ?? throw new InvalidOperationException("ConfigManager not found");
        }

        protected override void OnActivate()
        {
            var graphicsDevice = _game.GraphicsDevice;
            _spriteBatch = new SpriteBatch(graphicsDevice);
            _whitePixel = new Texture2D(graphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });
            _resourceManager = _game.ResourceManager;
            _font = _resourceManager.LoadFont("NotoSerifJP", 14);
            _renderer = new DrumKitRenderer(graphicsDevice);

            _input = _game.InputManager; // BaseGame.InputManager is concretely InputManagerCompat

            // Working copies (committed on Save), mirroring ConfigStage.
            _workingBindings = _input?.ModularInputManager.KeyBindings.Clone() ?? new KeyBindings();
            _workingSystemBindings = _input != null
                ? new Dictionary<Keys, InputCommandType>(_input.GetKeyMappingSnapshot())
                : new Dictionary<Keys, InputCommandType>();

            _popup = new DrumCapturePopup(
                _workingBindings,
                () => _workingSystemBindings,
                key => _workingSystemBindings.Remove(key));

            _focusedLane = 0;
            _selectedLane = -1;
            _hoveredLane = -1;
            _previousMouse = Mouse.GetState();
        }

        protected override void OnUpdate(double deltaTime)
        {
            var mouse = Mouse.GetState();
            bool leftClick = mouse.LeftButton == ButtonState.Pressed
                             && _previousMouse.LeftButton == ButtonState.Released;
            bool rightClick = mouse.RightButton == ButtonState.Pressed
                              && _previousMouse.RightButton == ButtonState.Released;

            if (_popup != null && _popup.IsOpen)
                UpdatePopup(deltaTime, mouse, leftClick, rightClick);
            else
                UpdateSelection(mouse, leftClick);

            _previousMouse = mouse;
        }

        private void UpdatePopup(double deltaTime, MouseState mouse, bool leftClick, bool rightClick)
        {
            _popup!.Tick(deltaTime);

            // Esc/Back or right-click closes the popup (acts as "Done").
            if (rightClick || _input?.IsBackActionTriggered() == true)
            {
                _popup.Close();
                _selectedLane = -1;
                return;
            }

            var vp = _game.GraphicsDevice.Viewport;
            if (leftClick)
            {
                if (_popup.GetDoneRect(vp.Width, vp.Height).Contains(mouse.X, mouse.Y))
                {
                    _popup.Close();
                    _selectedLane = -1;
                    return;
                }
                if (_popup.GetClearRect(vp.Width, vp.Height).Contains(mouse.X, mouse.Y))
                {
                    _popup.ClearLane();
                    return;
                }
            }

            // Skip the frame the popup was opened so the activating key isn't captured.
            if (_skipCaptureThisFrame)
            {
                _skipCaptureThisFrame = false;
                return;
            }

            if (_input != null)
            {
                foreach (var button in _input.ModularInputManager.ConsumePressedButtons())
                {
                    if (_popup.TryCapture(button) != DrumCaptureOutcome.Ignored)
                        break; // one binding per press
                }
            }
        }

        private void UpdateSelection(MouseState mouse, bool leftClick)
        {
            var vp = _game.GraphicsDevice.Viewport;
            float designX = mouse.X * DrumKitLayout.DesignWidth / (float)vp.Width;
            float designY = mouse.Y * DrumKitLayout.DesignHeight / (float)vp.Height;
            _hoveredLane = DrumKitLayout.HitTest(designX, designY);

            // Keyboard focus navigation (left/right cycles lanes).
            if (_input?.IsCommandPressed(InputCommandType.MoveRight) == true)
                _focusedLane = (_focusedLane + 1) % LaneCount;
            else if (_input?.IsCommandPressed(InputCommandType.MoveLeft) == true)
                _focusedLane = (_focusedLane - 1 + LaneCount) % LaneCount;

            // Back exits the stage (discard working copy).
            if (_input?.IsBackActionTriggered() == true)
            {
                Cancel();
                return;
            }

            // Open popup via click on a zone, or Activate on the focused lane.
            if (leftClick && _hoveredLane >= 0)
                OpenPopup(_hoveredLane);
            else if (_input?.IsCommandPressed(InputCommandType.Activate) == true)
                OpenPopup(_focusedLane);
        }

        private void OpenPopup(int lane)
        {
            _selectedLane = lane;
            _focusedLane = lane;
            _popup!.Open(lane);
            _skipCaptureThisFrame = true;
        }

        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null || _renderer == null)
                return;

            var vp = _game.GraphicsDevice.Viewport;
            _spriteBatch.Begin();

            if (_font != null)
                _font.DrawString(_spriteBatch, "DRUM MAPPING  -  click a piece, then hit your input.  Back: save & exit",
                    new Vector2(20, 16), Color.White);

            _renderer.Draw(_spriteBatch, _font, _whitePixel, _workingBindings,
                vp.Width, vp.Height, _selectedLane, _focusedLane, _hoveredLane);

            _popup?.Draw(_spriteBatch, _font, _whitePixel, vp.Width, vp.Height);

            _spriteBatch.End();
        }

        /// <summary>Persists the working bindings and returns to ConfigStage.</summary>
        private void Save()
        {
            if (_configManager is ConfigManager concrete)
            {
                concrete.SaveKeyBindings(_workingBindings);
                concrete.SaveSystemKeyBindings(_workingSystemBindings);
                try
                {
                    _configManager.SaveConfig(AppPaths.GetConfigFilePath());
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"DrumConfigStage: save failed: {ex.Message}");
                }
            }

            if (_input != null)
            {
                _input.ModularInputManager.ReloadKeyBindings();
                ApplySystemBindings(_input, _workingSystemBindings);
            }

            ChangeStage(StageType.Config, new InstantTransition());
        }

        /// <summary>Back exits with save (the stage's own Save model: Back commits, like other config screens).</summary>
        private void Cancel()
        {
            // Per design, the drum stage commits on exit via Back (Save). To discard instead,
            // call ChangeStage(StageType.Config, ...) without Save(); kept as Save() here so the
            // user's mapping persists when they leave.
            Save();
        }

        private static void ApplySystemBindings(InputManager inputManager,
            IReadOnlyDictionary<Keys, InputCommandType> bindings)
        {
            var snapshot = inputManager.GetKeyMappingSnapshot();
            foreach (var kvp in snapshot)
                inputManager.RemoveKeyMapping(kvp.Key);
            foreach (var kvp in bindings)
                inputManager.AddKeyMapping(kvp.Key, kvp.Value);
        }

        protected override void OnDeactivate()
        {
            _renderer?.Dispose();
            _renderer = null;
            _popup = null;
            _font?.RemoveReference();
            _font = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _renderer?.Dispose();
                _whitePixel?.Dispose();
                _spriteBatch?.Dispose();
                _font?.RemoveReference();
                _renderer = null;
                _whitePixel = null!;
                _spriteBatch = null!;
                _font = null;
                _resourceManager = null!;
            }
            base.Dispose(disposing);
        }
    }
}
```

> **Design note on Save vs. Cancel:** The spec's working-copy model commits on Save and discards on Cancel. To keep v1 simple, Back triggers `Save()` (the working copy persists on exit). The explicit discard path exists in `Cancel()` as a comment — if you want a separate discard control, add a key (e.g. a dedicated "revert" command) that calls `ChangeStage(StageType.Config, new InstantTransition())` without `Save()`. Confirm desired behavior during review.

- [ ] **Step 2: Commit (after Task 7 makes it build)**

This file references `StageType.DrumConfig`, added in Task 7. Implement it now, then build and commit at the end of Task 7. (If executing strictly task-by-task with a green tree per task, fold this file's commit into Task 7.)

---

## Task 7: Wire up the stage (enum, factory, ConfigStage menu) and verify

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/IStageManager.cs`
- Modify: `DTXMania.Game/Lib/Stage/StageManager.cs`
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs`

- [ ] **Step 1: Add the enum value**

In `DTXMania.Game/Lib/Stage/IStageManager.cs`, add `DrumConfig` to the `StageType` enum (after `Result`):

```csharp
    public enum StageType
    {
        Startup,
        Title,
        Config,
        SongSelect,
        SongTransition,
        Performance,
        Result,
        DrumConfig
    }
```

- [ ] **Step 2: Add the factory case**

In `DTXMania.Game/Lib/Stage/StageManager.cs`, add to the `stageType switch` (after the `Result` case):

```csharp
                StageType.Result => new ResultStage(_game),
                StageType.DrumConfig => new DrumConfigStage(_game),
```

- [ ] **Step 3: Repoint the ConfigStage menu item**

In `DTXMania.Game/Lib/Stage/ConfigStage.cs`, change the "Drum Key Mapping" navigation item (lines 371-372) from opening the inline panel to navigating to the new stage:

```csharp
            // Drum and system key mapping navigation items
            _configItems.Add(new NavigationConfigItem("Drum Key Mapping",
                () => ChangeStage(StageType.DrumConfig, new InstantTransition())));
            _configItems.Add(new NavigationConfigItem("System Key Mapping",
                () => OpenPanel(_systemPanel)));
```

> Leave `_drumPanel` and `InitializePanels()` as-is for now (still constructed; just no longer reachable from the menu). This keeps all existing ConfigStage tests green. Physical removal is Task 8.

- [ ] **Step 4: Build the game**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeded (DrumConfigStage from Task 6 now resolves `StageType.DrumConfig`).

- [ ] **Step 5: Run the full Mac test suite**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: PASS — all existing tests plus the new `DrumKitLayoutTests`, `DrumCapturePopupTests`, `InputRouterTests`, and `ModularInputManagerInjectionTests` additions.

- [ ] **Step 6: Manually verify in-app**

Run: `dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj`
Steps: Title → Config → select "Drum Key Mapping" → the drum kit appears → click a piece → popup says "Listening" → press a key → the key appears in that piece's binding text → press Esc/Back → returns to Config. Reopen the screen to confirm the binding persisted.

- [ ] **Step 7: Commit**

```bash
git add DTXMania.Game/Lib/Stage/IStageManager.cs DTXMania.Game/Lib/Stage/StageManager.cs DTXMania.Game/Lib/Stage/ConfigStage.cs DTXMania.Game/Lib/Stage/DrumConfigStage.cs
git commit -m "feat: add DrumConfigStage and route Config menu to it"
```

---

## Task 8 (final cleanup): Retire the old DrumKeyAssignPanel

Only do this once Task 7 is validated in-app. This removes now-unreachable code and its tests. Because several ConfigStage tests reflectively read the private `_drumPanel` field, this task deletes those obsolete tests too.

**Files:**
- Delete: `DTXMania.Game/Lib/Stage/KeyAssign/DrumKeyAssignPanel.cs`
- Delete: `DTXMania.Test/Stage/KeyAssign/DrumKeyAssignPanelCoverageTests.cs`
- Delete: `DTXMania.Test/Config/KeyAssignPanelWorkingCopyTests.cs`, `DTXMania.Test/Config/KeyAssignPanelAdditionalCoverageTests.cs`, `DTXMania.Test/Config/KeyAssignPanelCoverageTests.cs` (these target the drum panel specifically — confirm each only covers `DrumKeyAssignPanel`/`SystemKeyAssignPanel`; keep any `SystemKeyAssignPanel`-only tests by moving them to a new `SystemKeyAssignPanelTests.cs`).
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs` — remove the `_drumPanel` field (line 49), its construction + event wiring in `InitializePanels()` (lines 391-398), the `sender == _drumPanel` branch in `OnPanelSaved` (lines 420-423), and any `_drumPanel` references in `OnDeactivate`/`Dispose`.
- Modify: `DTXMania.Test/Config/ConfigStageTests.cs`, `DTXMania.Test/Config/ConfigStageLogicTests.cs`, `DTXMania.Test/Stage/ConfigStageInputCoverageTests.cs` — delete the test methods that call `GetPrivateField<DrumKeyAssignPanel>(stage, "_drumPanel")` (enumerated by the `grep` below).

- [ ] **Step 1: Enumerate the obsolete tests**

Run: `grep -rn "DrumKeyAssignPanel" DTXMania.Game DTXMania.Test --include=*.cs`
For each `*Tests.cs` hit, open the enclosing `[Fact]`/`[Theory]` method and delete that method (it exercises the removed inline panel).

- [ ] **Step 2: Delete the panel and its dedicated tests**

```bash
git rm DTXMania.Game/Lib/Stage/KeyAssign/DrumKeyAssignPanel.cs \
       DTXMania.Test/Stage/KeyAssign/DrumKeyAssignPanelCoverageTests.cs
```

- [ ] **Step 3: Edit ConfigStage to drop `_drumPanel`**

Remove the field, construction, wiring, and `OnPanelSaved` drum branch listed above. Keep `_workingDrumBindings` (still used by `SystemKeyAssignPanel` conflict checks and ConfigStage save) and keep `SystemKeyAssignPanel`.

- [ ] **Step 4: Build and run the full Mac test suite**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj && dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: Build succeeded; all remaining tests PASS. Fix any compile error by deleting the referencing obsolete test method (it tested removed behavior).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: retire DrumKeyAssignPanel in favor of DrumConfigStage"
```

---

## Self-Review Notes

- **Spec coverage:** New stage (Tasks 6-7), device-agnostic capture (Tasks 1-2), 10-zone layout mapped to lanes (Task 3), append-model popup with conflict reject + non-required eviction (Task 4), binding display via `GetLaneDescription` (Tasks 4-5), mouse + keyboard selection (Task 6), persistence/commit (Task 6 `Save`), retire old panel (Tasks 7-8). All covered.
- **Deviation to confirm in review:** The design's working-copy model commits on **Save** and discards on **Cancel**. Task 6 makes **Back = Save** (commit-on-exit) for v1 simplicity, with the discard path stubbed in `Cancel()`. Flagged in the Task 6 design note — confirm desired Back behavior before/at execution.
- **Scope note:** Task 8 (physical deletion + obsolete-test removal) is separated so the feature ships green even if that cleanup is deferred. The menu already routes to the new stage after Task 7, satisfying "replace old panel" behaviorally.
- **Types are consistent across tasks:** `DrumCaptureState`/`DrumCaptureOutcome` (Task 4) are used by the stage (Task 6); `ConsumePressedButtons()` (Task 2) is consumed in Task 6; `DrumKitLayout.HitTest`/`Zones` (Task 3) used by renderer (Task 5) and stage (Task 6).
