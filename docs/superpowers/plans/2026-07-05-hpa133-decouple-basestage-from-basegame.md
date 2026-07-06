# Decouple BaseStage From BaseGame Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Retype `BaseStage._game` from the concrete `BaseGame` to the `IStageGame` interface so stages depend only on the interface, by adding the three missing members (`MapMouseToVirtual`, `GetTextInputSource`, `RequestExit`) to `IStageGame`, deleting the ~15 redundant `is BaseGame` casts, and collapsing the dead render-target fallback.

**Architecture:** Interface-first. Add the three members to `IStageGame` and implement them on `BaseGame` (which currently only declares `: IGameContext`). Then migrate each stage subclass independently while `_game` is still typed `BaseGame` — the cast targets (`CanPerformStageTransition`, `MarkStageTransition`, `GraphicsManager`) and the new members (`RequestExit`, `GetTextInputSource`) are all already reachable on `BaseGame`, so each migration compiles and tests green on its own. Finally, retype the `BaseStage` field/constructor and all eight subclass constructors from `BaseGame` to `IStageGame` in one atomic task that compiles cleanly because every `_game.` access now goes through interface members.

**Tech Stack:** .NET 8, MonoGame 3.8, xUnit, Moq. Build: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`. Tests: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`.

**Spec:** `docs/superpowers/specs/2026-07-05-hpa133-decouple-basestage-from-basegame-design.md`

## Global Constraints

- Runtime behavior of stage input handling and coordinate mapping must not change.
- `IStageGame` stays `internal`; do not add `InternalsVisibleTo` (tests use the public `BaseGame` surface and reflection).
- `MapMouseToVirtual` stays `internal` on `BaseGame` (satisfies the `internal interface` implicitly). `RequestExit` and `GetTextInputSource` are `public` on `BaseGame` (useful seams, and directly testable on the uninitialized headless game).
- Do not touch the concrete `ConfigManager` cast at `SongTransitionStage.cs:543` (out of scope; separate follow-up).
- `IStageGame.InputManager` stays typed as the concrete `InputManagerCompat` (pre-existing decision; out of scope).
- The two Mac/Windows game csproj files share the same source; every code task must keep BOTH building. Build verification uses the Mac csproj (available on this host); Windows builds are verified in CI.
- Conventional Commits (`refactor:`, `test:`, `docs:`). One logical commit per task unless a task says otherwise.

## File Structure

- Modify `DTXMania.Game/Lib/Stage/IStageGame.cs`: add `MapMouseToVirtual`, `GetTextInputSource`, `RequestExit`.
- Modify `DTXMania.Game/Game1.cs`: declare `: IStageGame` on `BaseGame`; add `RequestExit()` and `GetTextInputSource()`; confirm `MapMouseToVirtual` implicitly implements the interface member.
- Modify `DTXMania.Game/Lib/Stage/BaseStage.cs`: retype `_game` field and constructor from `BaseGame` to `IStageGame`.
- Modify `DTXMania.Game/Lib/Stage/TitleStage.cs`: remove 5 casts, migrate `_game.Exit()` → `_game.RequestExit()` (×2), retype constructor.
- Modify `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`: remove 6 casts, collapse render-target fallback, remove `_isUnmanagedRenderTarget`, migrate `_game.Window` → `_game.GetTextInputSource()`, retype constructor.
- Modify `DTXMania.Game/Lib/Stage/SongTransitionStage.cs`: remove 3 casts, retype constructor.
- Modify `DTXMania.Game/Lib/Stage/PerformanceStage.cs`: remove 1 cast, retype constructor.
- Modify `DTXMania.Game/Lib/Stage/ResultStage.cs`: remove 1 cast, retype constructor.
- Modify `DTXMania.Game/Lib/Stage/ConfigStage.cs`, `DrumConfigStage.cs`, `StartupStage.cs`: retype constructors only (no cast removal needed).
- Create `DTXMania.Test/Stage/IStageGameContractTests.cs`: lock the `BaseGame : IStageGame` contract and the headless null-guard on `GetTextInputSource`.

---

## Task 1: Add the three members to `IStageGame` and implement on `BaseGame`

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/IStageGame.cs`
- Modify: `DTXMania.Game/Game1.cs:40` (class declaration), `:476` (existing `MapMouseToVirtual`)
- Create: `DTXMania.Test/Stage/IStageGameContractTests.cs`

**Interfaces:**
- Produces (used by all later tasks): `IStageGame.MapMouseToVirtual(Point) → Point?`, `IStageGame.GetTextInputSource() → ITextInputSource?`, `IStageGame.RequestExit() → void`, all implemented by `BaseGame`.

- [ ] **Step 1: Write the failing contract test**

Create `DTXMania.Test/Stage/IStageGameContractTests.cs`:

```csharp
using DTXMania.Game;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI.Components;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class IStageGameContractTests
    {
        private const string IStageGameFullName = "DTXMania.Game.Lib.Stage.IStageGame";

        [Fact]
        public void BaseGame_ShouldImplementIStageGame()
        {
            var iface = typeof(BaseGame).GetInterface(IStageGameFullName);
            Assert.NotNull(iface);
        }

        [Fact]
        public void IStageGame_ShouldDeclareMapMouseToVirtualAndGetTextInputSourceAndRequestExit()
        {
            var iface = typeof(BaseGame).GetInterface(IStageGameFullName)!;
            Assert.NotNull(iface.GetMethod("MapMouseToVirtual"));
            Assert.NotNull(iface.GetMethod("GetTextInputSource"));
            Assert.NotNull(iface.GetMethod("RequestExit"));
        }

        [Fact]
        public void GetTextInputSource_ShouldReturnNull_WhenWindowIsUnavailable()
        {
            // ReflectionHelpers.CreateGame builds an uninitialized BaseGame (Window is null),
            // which models the headless/test environment the search modal must tolerate.
            var game = ReflectionHelpers.CreateGame();
            Assert.Null(game.GetTextInputSource());
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~IStageGameContractTests"`
Expected: FAIL — `BaseGame_ShouldImplementIStageGame` fails because `BaseGame` does not yet declare `: IStageGame` (the `GetInterface` lookup returns null).

- [ ] **Step 3: Add the three members to `IStageGame`**

In `DTXMania.Game/Lib/Stage/IStageGame.cs`, add a `using DTXMania.Game.Lib.UI.Components;` and `using Microsoft.Xna.Framework;` at the top, and append the three members to the interface body (after `MarkStageTransition();`):

```csharp
        /// <summary>
        /// Maps raw window mouse coordinates into the fixed 1280x720 virtual render target.
        /// Returns null when the point lands outside the letterboxed virtual area.
        /// </summary>
        Point? MapMouseToVirtual(Point windowPoint);

        /// <summary>
        /// Builds a text-input source for OS text events (used by the song search modal).
        /// Returns null in headless/test environments where no OS window is available.
        /// </summary>
        ITextInputSource? GetTextInputSource();

        /// <summary>
        /// Requests game process termination.
        /// </summary>
        void RequestExit();
```

- [ ] **Step 4: Declare `IStageGame` on `BaseGame` and add the two new methods**

In `DTXMania.Game/Game1.cs`:

4a. Update the class declaration at line 40 to also implement `IStageGame`:

```csharp
public class BaseGame : Microsoft.Xna.Framework.Game, IGameContext, IStageGame
```

(Add `using DTXMania.Game.Lib.Stage;` to the usings at the top of the file if not already present — it is required for the `IStageGame` reference.)

4b. The existing `internal Point? MapMouseToVirtual(Point windowPoint)` at line 476 already matches the interface signature; leave it as-is (it implicitly implements the `internal interface` member — no visibility change needed).

4c. Add the two new public methods. Place them immediately after the existing `MarkStageTransition()` method (after line 100):

```csharp
        /// <summary>
        /// Requests game process termination. Implements <see cref="IStageGame.RequestExit"/>
        /// so stages can exit through the interface without depending on the concrete type.
        /// </summary>
        public void RequestExit()
        {
            Exit();
        }

        /// <summary>
        /// Builds a <see cref="WindowTextInputSource"/> from the OS window for text input,
        /// or returns null when no window is available (headless/test environments).
        /// Implements <see cref="IStageGame.GetTextInputSource"/>.
        /// </summary>
        public ITextInputSource? GetTextInputSource()
        {
            return Window != null ? new WindowTextInputSource(Window) : null;
        }
```

(Add `using DTXMania.Game.Lib.UI.Components;` to the usings at the top of the file — required for `ITextInputSource` and `WindowTextInputSource`.)

- [ ] **Step 5: Build the Mac game project**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeded. (`MapMouseToVirtual` implicitly implements the interface member; `RequestExit`/`GetTextInputSource` are the new public implementations.)

- [ ] **Step 6: Run the contract test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~IStageGameContractTests"`
Expected: PASS (all three tests).

- [ ] **Step 7: Run the full Mac test suite to confirm no regressions**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: All tests pass (no behavior changed; additive interface members only).

- [ ] **Step 8: Commit**

```bash
git add DTXMania.Game/Lib/Stage/IStageGame.cs DTXMania.Game/Game1.cs DTXMania.Test/Stage/IStageGameContractTests.cs
git commit -m "refactor: add MapMouseToVirtual/GetTextInputSource/RequestExit to IStageGame

BaseGame now implements IStageGame. RequestExit wraps Exit() and
GetTextInputSource returns a WindowTextInputSource (or null in
headless). MapMouseToVirtual already matched the interface."
```

---

## Task 2: Migrate `TitleStage` (remove 5 casts, `Exit` → `RequestExit`)

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/TitleStage.cs:71` (constructor), `:356-363`, `:444`, `:447-474`

**Interfaces:**
- Consumes: `IStageGame.CanPerformStageTransition()`, `MarkStageTransition()`, `RequestExit()`, `MapMouseToVirtual(Point)` (all reachable on `BaseGame` today; `_game` is still typed `BaseGame` until Task 5).

- [ ] **Step 1: Migrate the ESC/back handler (lines 356-363)**

Replace:

```csharp
            if (_game.InputManager?.IsBackActionTriggered() == true)
            {
                if (_game is BaseGame baseGame && baseGame.CanPerformStageTransition())
                {
                    baseGame.MarkStageTransition();
                    _game.Exit();
                }
                return;
            }
```

with:

```csharp
            if (_game.InputManager?.IsBackActionTriggered() == true)
            {
                if (_game.CanPerformStageTransition())
                {
                    _game.MarkStageTransition();
                    _game.RequestExit();
                }
                return;
            }
```

- [ ] **Step 2: Migrate the menu-select debounce guard (line 444)**

Replace:

```csharp
            if (_game is BaseGame baseGame && !baseGame.CanPerformStageTransition())
                return;
```

with:

```csharp
            if (!_game.CanPerformStageTransition())
                return;
```

- [ ] **Step 3: Migrate the three menu-item casts (lines 447-474)**

Replace the whole `switch` body's three `case` arms that cast `BaseGame`:

```csharp
            switch (_currentMenuIndex)
            {
                case 0: // GAME START
                    if (_game is BaseGame baseGameStart)
                        baseGameStart.MarkStageTransition();
                    PlayGameStartSound();
                    System.Diagnostics.Debug.WriteLine("Starting game - transitioning to Song Selection Stage");
                    // Use DTXMania-style fade transition for game start
                    ChangeStage(StageType.SongSelect, new DTXManiaFadeTransition(0.7));
                    break;

                case 1: // CONFIG
                    if (_game is BaseGame baseGameConfig)
                        baseGameConfig.MarkStageTransition();
                    PlaySelectSound();
                    System.Diagnostics.Debug.WriteLine("Opening config - transitioning to Config Stage");
                    // Use crossfade transition for config
                    ChangeStage(StageType.Config, new CrossfadeTransition(0.5));
                    break;

                case 2: // EXIT
                    if (_game is BaseGame baseGameExit)
                        baseGameExit.MarkStageTransition();
                    PlaySelectSound();
                    System.Diagnostics.Debug.WriteLine("Exiting game");
                    _game.Exit();
                    break;
            }
```

with:

```csharp
            switch (_currentMenuIndex)
            {
                case 0: // GAME START
                    _game.MarkStageTransition();
                    PlayGameStartSound();
                    System.Diagnostics.Debug.WriteLine("Starting game - transitioning to Song Selection Stage");
                    // Use DTXMania-style fade transition for game start
                    ChangeStage(StageType.SongSelect, new DTXManiaFadeTransition(0.7));
                    break;

                case 1: // CONFIG
                    _game.MarkStageTransition();
                    PlaySelectSound();
                    System.Diagnostics.Debug.WriteLine("Opening config - transitioning to Config Stage");
                    // Use crossfade transition for config
                    ChangeStage(StageType.Config, new CrossfadeTransition(0.5));
                    break;

                case 2: // EXIT
                    _game.MarkStageTransition();
                    PlaySelectSound();
                    System.Diagnostics.Debug.WriteLine("Exiting game");
                    _game.RequestExit();
                    break;
            }
```

- [ ] **Step 4: Build the Mac game project**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeded. (`_game` is still `BaseGame`; all members are reachable on it.)

- [ ] **Step 5: Run TitleStage tests**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~TitleStage"`
Expected: All TitleStage tests pass.

- [ ] **Step 6: Commit**

```bash
git add DTXMania.Game/Lib/Stage/TitleStage.cs
git commit -m "refactor: remove redundant BaseGame casts in TitleStage

Drops the 'is BaseGame' guards around CanPerformStageTransition/
MarkStageTransition (already reachable on BaseGame) and routes exit
through _game.RequestExit()."
```

---

## Task 3: Migrate `SongSelectionStage` (remove 6 casts, collapse render-target fallback, `Window` → `GetTextInputSource`)

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs:209` (constructor), `:225`, `:230-233`, `:625-639`, `:956-959`, `:1519-1524`, `:1686`, `:1705-1727`, `:1732-1754`

**Interfaces:**
- Consumes: `IStageGame.InputManager`, `ConfigManager`, `GraphicsManager`, `CanPerformStageTransition()`, `MarkStageTransition()`, `MapMouseToVirtual(Point)`, `GetTextInputSource()` (all reachable on `BaseGame` today).

- [ ] **Step 1: Migrate the InputManager/ConfigManager casts (lines 224-233)**

Replace:

```csharp
            // Use game's shared InputManager (supports MCP key injection)
            AssignInputManager((_game as BaseGame)?.InputManager);
            _inputManager?.ClearPendingCommands();
            _cancellationTokenSource = new CancellationTokenSource();

            // Get config manager from game
            if (_game is BaseGame baseGame)
            {
                _configManager = baseGame.ConfigManager;
            }
```

with:

```csharp
            // Use game's shared InputManager (supports MCP key injection)
            AssignInputManager(_game.InputManager);
            _inputManager?.ClearPendingCommands();
            _cancellationTokenSource = new CancellationTokenSource();

            // Get config manager from game
            _configManager = _game.ConfigManager;
```

- [ ] **Step 2: Migrate the `Window` → `GetTextInputSource` seam (lines 625-639)**

Replace:

```csharp
            // Search/filter modal (guarded: Window is unavailable in headless/test environments)
            try
            {
                var window = _game.Window;
                if (window != null)
                {
                    _textInputSource = new WindowTextInputSource(window);
                    _searchFilterModal = new SongSearchFilterModal(_textInputSource);
                    // Inject graphics resources
                    _searchFilterModal.WhitePixel = _whitePixel;
                    _searchFilterModal.Font = _statusPanel?.Font;
                    _searchFilterModal.FilterApplied += OnFilterApplied;
                    _searchFilterModal.FilterReset   += OnFilterReset;
                    _searchFilterModal.Cancelled     += OnFilterCancelled;
                    _mainPanel.AddChild(_searchFilterModal);
```

(closing braces of the `try`/`if` stay as they are further down) with:

```csharp
            // Search/filter modal (guarded: text input is unavailable in headless/test environments)
            try
            {
                var textInputSource = _game.GetTextInputSource();
                if (textInputSource != null)
                {
                    _textInputSource = textInputSource;
                    _searchFilterModal = new SongSearchFilterModal(_textInputSource);
                    // Inject graphics resources
                    _searchFilterModal.WhitePixel = _whitePixel;
                    _searchFilterModal.Font = _statusPanel?.Font;
                    _searchFilterModal.FilterApplied += OnFilterApplied;
                    _searchFilterModal.FilterReset   += OnFilterReset;
                    _searchFilterModal.Cancelled     += OnFilterCancelled;
                    _mainPanel.AddChild(_searchFilterModal);
```

- [ ] **Step 3: Migrate the SelectSong debounce cast (lines 955-959)**

Replace:

```csharp
            // Debounce stage transitions to prevent accidental double selections
            if (!(_game is BaseGame baseGame) || !baseGame.CanPerformStageTransition())
                return;
            
            baseGame.MarkStageTransition();
```

with:

```csharp
            // Debounce stage transitions to prevent accidental double selections
            if (!_game.CanPerformStageTransition())
                return;

            _game.MarkStageTransition();
```

- [ ] **Step 4: Migrate the Back-to-title debounce cast (lines 1519-1524)**

Replace:

```csharp
                        if (_game is BaseGame baseGame && baseGame.CanPerformStageTransition())
                        {
                            baseGame.MarkStageTransition();
                            StageManager?.ChangeStage(StageType.Title, new DTXManiaFadeTransition(SongSelectionUILayout.Timing.TransitionDuration));
                        }
```

with:

```csharp
                        if (_game.CanPerformStageTransition())
                        {
                            _game.MarkStageTransition();
                            StageManager?.ChangeStage(StageType.Title, new DTXManiaFadeTransition(SongSelectionUILayout.Timing.TransitionDuration));
                        }
```

- [ ] **Step 5: Collapse the render-target creation fallback (lines 1705-1727)**

Replace the whole `InitializeStageRenderTargets` method body:

```csharp
        protected virtual void InitializeStageRenderTargets()
        {
            // Create a single RenderTarget for all stage operations using RenderTargetManager
            // Size should be large enough to accommodate all UI components
            if (_game is BaseGame baseGame)
            {
                _stageRenderTarget = baseGame.GraphicsManager.RenderTargetManager
                    .GetOrCreateRenderTarget("SongSelectionStage_Main", 1024, 1024);
                _isUnmanagedRenderTarget = false;
            }
            else
            {
                // Fallback for non-BaseGame instances (shouldn't happen in normal operation)
                // Log warning that we're creating an unmanaged render target
                System.Diagnostics.Debug.WriteLine(
                    "WARNING: SongSelectionStage.InitializeStageRenderTargets() - " +
                    "Creating unmanaged RenderTarget2D fallback (1024x1024). " +
                    "This resource will need manual disposal.");
                
                _stageRenderTarget = new RenderTarget2D(_game.GraphicsDevice, 1024, 1024);
                _isUnmanagedRenderTarget = true;
            }
        }
```

with:

```csharp
        protected virtual void InitializeStageRenderTargets()
        {
            // Create a single RenderTarget for all stage operations using RenderTargetManager.
            // Size should be large enough to accommodate all UI components.
            _stageRenderTarget = _game.GraphicsManager.RenderTargetManager
                .GetOrCreateRenderTarget("SongSelectionStage_Main", 1024, 1024);
        }
```

- [ ] **Step 6: Collapse the render-target cleanup fallback (lines 1732-1754)**

Replace the whole `CleanupStageRenderTargets` method body:

```csharp
        private void CleanupStageRenderTargets()
        {
            if (_stageRenderTarget == null)
            {
                return;
            }

            if (_game is BaseGame baseGame && !_isUnmanagedRenderTarget)
            {
                // Use RenderTargetManager to properly dispose the RenderTarget
                baseGame.GraphicsManager.RenderTargetManager.RemoveRenderTarget("SongSelectionStage_Main");
            }
            else
            {
                // Fallback cleanup for non-BaseGame instances or unmanaged render targets
                System.Diagnostics.Debug.WriteLine(
                    "SongSelectionStage.CleanupStageRenderTargets() - " +
                    "Disposing unmanaged RenderTarget2D.");
                _stageRenderTarget.Dispose();
            }
            _stageRenderTarget = null;
            _isUnmanagedRenderTarget = false;
        }
```

with:

```csharp
        private void CleanupStageRenderTargets()
        {
            if (_stageRenderTarget == null)
            {
                return;
            }

            // Use RenderTargetManager to properly dispose the RenderTarget
            _game.GraphicsManager.RenderTargetManager.RemoveRenderTarget("SongSelectionStage_Main");
            _stageRenderTarget = null;
        }
```

- [ ] **Step 7: Remove the now-unused `_isUnmanagedRenderTarget` field (line 1686)**

Delete the field declaration:

```csharp
        private bool _isUnmanagedRenderTarget = false;
```

Then verify there are no remaining references:

Run: `rg -n "_isUnmanagedRenderTarget" DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
Expected: **No matches** (all five prior references were removed in Steps 5 and 6).

- [ ] **Step 8: Build the Mac game project**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeded.

- [ ] **Step 9: Run SongSelectionStage tests**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStage"`
Expected: All SongSelectionStage tests pass.

- [ ] **Step 10: Commit**

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs
git commit -m "refactor: remove BaseGame casts and collapse render-target fallback in SongSelectionStage

Drops six 'is BaseGame' guards, routes the search modal's text input
through _game.GetTextInputSource(), and removes the unreachable
unmanaged-render-target fallback plus its _isUnmanagedRenderTarget field."
```

---

## Task 4: Remove the casts in `SongTransitionStage`, `PerformanceStage`, `ResultStage`

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/SongTransitionStage.cs:570-572`, `:831-833`, `:839-841`
- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs:508-511`
- Modify: `DTXMania.Game/Lib/Stage/ResultStage.cs:368-371`

**Interfaces:**
- Consumes: `IStageGame.CanPerformStageTransition()`, `MarkStageTransition()` (reachable on `BaseGame` today).

- [ ] **Step 1: Migrate `SongTransitionStage` auto-transition cast (lines 570-572)**

Replace:

```csharp
                if (_game is BaseGame baseGame && baseGame.CanPerformStageTransition())
                {
                    baseGame.MarkStageTransition();
                    TransitionToPerformance();
```

with:

```csharp
                if (_game.CanPerformStageTransition())
                {
                    _game.MarkStageTransition();
                    TransitionToPerformance();
```

- [ ] **Step 2: Migrate `SongTransitionStage` Activate command cast (lines 831-833)**

Replace:

```csharp
                case InputCommandType.Activate:
                    if (_game is BaseGame baseGame && baseGame.CanPerformStageTransition())
                    {
                        baseGame.MarkStageTransition();
                        TransitionToPerformance();
                    }
                    break;
```

with:

```csharp
                case InputCommandType.Activate:
                    if (_game.CanPerformStageTransition())
                    {
                        _game.MarkStageTransition();
                        TransitionToPerformance();
                    }
                    break;
```

- [ ] **Step 3: Migrate `SongTransitionStage` Back command cast (lines 839-841)**

Replace:

```csharp
                case InputCommandType.Back:
                    if (_game is BaseGame bg && bg.CanPerformStageTransition())
                    {
                        bg.MarkStageTransition();
                        TransitionBackToSongSelect();
                    }
                    break;
```

with:

```csharp
                case InputCommandType.Back:
                    if (_game.CanPerformStageTransition())
                    {
                        _game.MarkStageTransition();
                        TransitionBackToSongSelect();
                    }
                    break;
```

- [ ] **Step 4: Migrate `PerformanceStage` back-action cast (lines 508-511)**

Replace:

```csharp
                if (_game is BaseGame baseGame && baseGame.CanPerformStageTransition())
                {
                    baseGame.MarkStageTransition();
                    ReturnToSongSelect();
                }
```

with:

```csharp
                if (_game.CanPerformStageTransition())
                {
                    _game.MarkStageTransition();
                    ReturnToSongSelect();
                }
```

- [ ] **Step 5: Migrate `ResultStage` continue cast (lines 368-371)**

Replace:

```csharp
                    if (_game is BaseGame baseGame && baseGame.CanPerformStageTransition())
                    {
                        baseGame.MarkStageTransition();
                        ReturnToSongSelect();
                    }
```

with:

```csharp
                    if (_game.CanPerformStageTransition())
                    {
                        _game.MarkStageTransition();
                        ReturnToSongSelect();
                    }
```

- [ ] **Step 6: Build the Mac game project**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeded.

- [ ] **Step 7: Run the affected stage tests**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongTransitionStage|FullyQualifiedName~PerformanceStage|FullyQualifiedName~ResultStage"`
Expected: All matching tests pass.

- [ ] **Step 8: Verify zero `is BaseGame`/`as BaseGame` casts remain in stages**

Run: `rg -n "is BaseGame|as BaseGame|\(BaseGame\)" DTXMania.Game/Lib/Stage/`
Expected: **No matches.** (All 15 casts across the five stages are now gone. The `MapMouseToVirtual` call sites remain and are valid because that member is now on `IStageGame`.) If any match remains, remove it before committing.

- [ ] **Step 9: Commit**

```bash
git add DTXMania.Game/Lib/Stage/SongTransitionStage.cs DTXMania.Game/Lib/Stage/PerformanceStage.cs DTXMania.Game/Lib/Stage/ResultStage.cs
git commit -m "refactor: remove remaining BaseGame casts in transition/performance/result stages

Drops five 'is BaseGame' guards around CanPerformStageTransition/
MarkStageTransition. No stage references the concrete BaseGame type
for service access anymore."
```

---

## Task 5: Retype `BaseStage._game` and all stage constructors from `BaseGame` to `IStageGame`

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/BaseStage.cs:18`, `:45`
- Modify (constructor signature only): `TitleStage.cs:71`, `SongSelectionStage.cs:209`, `SongTransitionStage.cs:81`, `PerformanceStage.cs:173`, `ResultStage.cs:72`, `ConfigStage.cs:130`, `DrumConfigStage.cs:69`, `StartupStage.cs:83`

**Interfaces:**
- Produces: `BaseStage._game` typed as `IStageGame`; every stage constructor accepts `IStageGame`. This is the decoupling deliverable.

**Why this compiles cleanly now:** Tasks 1–4 already routed every `_game.<member>` access through interface-reachable members and removed every `is BaseGame` cast. The only remaining `_game.` accesses are to the nine `IStageGame` members (six pre-existing + three added in Task 1). After retyping, `BaseGame` callers still satisfy `IStageGame` because `BaseGame : IStageGame` (Task 1).

- [ ] **Step 1: Retype the `BaseStage` field and constructor**

In `DTXMania.Game/Lib/Stage/BaseStage.cs`:

1a. Line 18 — change the field type:

```csharp
        protected readonly IStageGame _game;
```

1b. Lines 45–48 — change the constructor parameter type:

```csharp
        protected BaseStage(IStageGame game)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
        }
```

- [ ] **Step 2: Retype each subclass constructor parameter**

For each of the eight files, change `public ClassName(BaseGame game) : base(game)` to `public ClassName(IStageGame game) : base(game)`. The exact lines:

- `TitleStage.cs:71`: `public TitleStage(IStageGame game) : base(game)`
- `SongSelectionStage.cs:209`: `public SongSelectionStage(IStageGame game) : base(game)`
- `SongTransitionStage.cs:81`: `public SongTransitionStage(IStageGame game) : base(game)`
- `PerformanceStage.cs:173`: `public PerformanceStage(IStageGame game) : base(game)`
- `ResultStage.cs:72`: `public ResultStage(IStageGame game) : base(game)`
- `ConfigStage.cs:130`: `public ConfigStage(IStageGame game) : base(game)`
- `DrumConfigStage.cs:69`: `public DrumConfigStage(IStageGame game) : base(game)`
- `StartupStage.cs:83`: `public StartupStage(IStageGame game) : base(game)`

(`IStageGame` is in the same namespace `DTXMania.Game.Lib.Stage`, so no new `using` is required in these files.)

- [ ] **Step 3: Build the Mac game project**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeded. If a compile error names a `_game.` member not on `IStageGame`, that access was missed by Tasks 1–4 — add it to `IStageGame` + `BaseGame` (following the Task 1 pattern) or re-migrate the call site before continuing.

- [ ] **Step 4: Run the full Mac test suite**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: All tests pass. (Test factories like `SongSelectionStageTestFactory.CreateStage(BaseGame?)` still compile because `BaseGame` is implicitly convertible to the new `IStageGame` constructor parameter — no test-side change required.)

- [ ] **Step 5: Run the grep guardrail**

Run: `rg -n "is BaseGame|as BaseGame|\(BaseGame\)|BaseGame _game|BaseGame game" DTXMania.Game/Lib/Stage/`
Expected: **No matches.**

- [ ] **Step 6: Commit**

```bash
git add DTXMania.Game/Lib/Stage/BaseStage.cs DTXMania.Game/Lib/Stage/TitleStage.cs DTXMania.Game/Lib/Stage/SongSelectionStage.cs DTXMania.Game/Lib/Stage/SongTransitionStage.cs DTXMania.Game/Lib/Stage/PerformanceStage.cs DTXMania.Game/Lib/Stage/ResultStage.cs DTXMania.Game/Lib/Stage/ConfigStage.cs DTXMania.Game/Lib/Stage/DrumConfigStage.cs DTXMania.Game/Lib/Stage/StartupStage.cs
git commit -m "refactor: retype BaseStage._game and stage constructors to IStageGame

BaseStage._game is now IStageGame, and all eight stage subclass
constructors accept IStageGame. Stages no longer reference the
concrete BaseGame type for service access."
```

---

## Task 6: Full verification (build both targets, test suite, E2E smoke)

**Files:** None modified.

- [ ] **Step 1: Build the Mac game project clean**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Build the Windows game project (compile check)**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj`
Expected: Build succeeded. (If the host OS rejects the Windows target framework, this step is deferred to CI — note it in the task report. The shared source is identical, so a Mac build passing is strong evidence the Windows build will pass.)

- [ ] **Step 3: Run the full Mac test suite**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: All tests pass, no regressions versus baseline.

- [ ] **Step 4: Run the E2E gameplay smoke**

Run:
```bash
DTXMANIA_E2E_GAME_PROJECT=DTXMania.Game/DTXMania.Game.Mac.csproj \
  dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E"
```
Expected: The smoke test launches the game, navigates Title → SongSelect → Performance via mouse/keyboard injection (exercising `_game.MapMouseToVirtual` through the new `IStageGame` member and `GetTextInputSource` in `SongSelectionStage`), and asserts the Result stage reports a cleared run. PASS.

- [ ] **Step 5: Final grep guardrail**

Run: `rg -n "is BaseGame|as BaseGame|\(BaseGame\)|BaseGame _game|BaseGame game" DTXMania.Game/Lib/Stage/`
Expected: **No matches.** (Acceptance criterion: no stage subclass references the concrete `BaseGame` type.)

- [ ] **Step 6: Confirm the `BaseStage._game` type**

Run: `rg -n "readonly IStageGame _game|protected BaseStage\(IStageGame" DTXMania.Game/Lib/Stage/BaseStage.cs`
Expected: Two matches (the field at line 18 and the constructor at line 45), confirming `_game` is typed `IStageGame`.

- [ ] **Step 7: No commit (verification-only task)**

If all steps pass, the refactor is complete. If any step fails, stop and debug (use superpowers:systematic-debugging) — do not mark the task complete until every check is green.

---

## Notes for the implementer

- The `is BaseGame`/`as BaseGame` casts all compile fine *before* Task 5 because `_game` is `BaseGame` and the cast targets are public on `BaseGame`. That is why Tasks 2–4 can remove them incrementally while the build stays green. Task 5 (the retyping) only compiles because Tasks 2–4 already removed every cast.
- If Task 5 Step 3 reveals a `_game.` access to a member not on `IStageGame`, do not retype `_game` back to `BaseGame`. Instead extend `IStageGame` + `BaseGame` (Task 1 pattern) or re-migrate the call site — the audit found exactly nine members, but verify against the compiler's actual errors.
- `SongSelectionStageTestFactory.CreateStage(BaseGame? game)` and `ReflectionHelpers.CreateGame() → BaseGame` are intentionally left returning/accepting `BaseGame`. They still compile after Task 5 because `BaseGame : IStageGame`. Do not "clean them up" to `IStageGame` — it is out of scope and unnecessary.
- Out of scope (do not touch): `SongTransitionStage.cs:543` casts `IConfigManager` to concrete `ConfigManager`; `IStageGame.InputManager` is the concrete `InputManagerCompat`. Both are flagged as follow-ups in the spec.
