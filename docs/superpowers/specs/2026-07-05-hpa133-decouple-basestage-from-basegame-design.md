# Decouple BaseStage From Concrete BaseGame via IStageGame

**Status:** Design approved, awaiting implementation planning
**Date:** 2026-07-05
**Linear:** [HPA-133](https://linear.app/cwchanap/issue/HPA-133/refactor-decouple-basestage-from-concrete-basegame-via-istagegame)
**Scope:** Retype `BaseStage._game` from the concrete `BaseGame` to the `IStageGame` interface, add the three members stages need that are not yet on the interface, and delete the now-dead `is BaseGame` casts and fallback branches across the stage subclasses.

## Problem

`BaseStage._game` is typed as the concrete `BaseGame` (`DTXMania.Game/Lib/Stage/BaseStage.cs:18`), not `IStageGame`. As a result every stage subclass freely couples to the concrete type. The coupling shows up in two ways:

1. **Three service members are missing from `IStageGame`**, so stages can only reach them through the concrete type:
   - `MapMouseToVirtual(Point)` — used by `TitleStage`, `DrumConfigStage` (×2), `SongSelectionStage` for hit-testing in the 1280×720 virtual canvas.
   - `Exit()` — used by `TitleStage` on ESC and the "EXIT" menu item.
   - `Window` — used by `SongSelectionStage` only to construct a `WindowTextInputSource` for the search modal.

2. **~15 defensive `is BaseGame baseGame` casts** are scattered across seven stages. Almost all of them access members that are *already* on `IStageGame` (`CanPerformStageTransition`, `MarkStageTransition`, `GraphicsManager.RenderTargetManager`, `InputManager`, `ConfigManager`). They exist because `_game` was historically concrete and the casts predate (or ignore) the interface. They are dead-weight noise once the field is retyped.

Adding `MapMouseToVirtual` to `IStageGame` alone — the originating review suggestion — is cosmetic until `_game` is retyped. That retyping is a cross-stage refactor and is tracked here.

## Goals

1. `BaseStage._game` is typed as `IStageGame`, not `BaseGame`.
2. Every service a stage needs is reachable through `IStageGame`; no stage subclass references the concrete `BaseGame` type for service access.
3. The three missing members are added to `IStageGame` with seams that do not leak XNA platform types where avoidable.
4. Redundant `is BaseGame` casts and the now-unreachable fallback branches are deleted.
5. Runtime behavior of stage input handling and coordinate mapping is unchanged.

## Non-Goals

- Splitting `IStageGame` into many small interfaces beyond what this audit justifies.
- Changing the runtime behavior of stage input handling or coordinate mapping.
- Decoupling the concrete `ConfigManager` cast in `SongTransitionStage:543` (separate smell; flagged as follow-up).
- Re-typing `IStageGame.InputManager` away from the concrete `InputManagerCompat` (pre-existing decision; out of scope).
- Bundling this refactor with feature work.

## Chosen Approach

**Approach B — Decisive cleanup.** Add the three missing members to `IStageGame`, retype `BaseStage._game` to `IStageGame`, delete all ~15 redundant `is BaseGame` casts, and collapse the dead render-target fallback branches in `SongSelectionStage`. This is the only approach that fully satisfies the acceptance criterion "no stage subclass references the concrete `BaseGame` type for service access" without leaving a trail of dead guards.

Member-by-member decisions (confirmed with the maintainer):

- **`MapMouseToVirtual`** → added directly to `IStageGame`. Matches how the three stages already call it as `_game.MapMouseToVirtual(...)`. A dedicated `ICoordinateMapper` seam was rejected as over-splitting for three call sites (violates the non-goal caution).
- **`Window`** → exposed as `ITextInputSource? GetTextInputSource()` on `IStageGame`. `BaseGame` builds the `WindowTextInputSource` internally so the XNA `GameWindow` platform type does not leak through the interface. Nullable so headless/test environments return null, preserving the existing guard.
- **`Exit()`** → exposed as `void RequestExit()` on `IStageGame`. `BaseGame.RequestExit()` delegates to the existing `Exit()`. The name communicates intent better than a bare `Exit` on an interface contract.

## Alternatives Considered

### Alternative A — Surgical (minimal diff)

Add the three members, retype `_game`, and remove only the casts the compiler forces. Repurpose the render-target fallback branches around a `GraphicsManager != null` check instead of `is BaseGame`.

Rejected: it leaves the dual-branch structure and the dead "non-BaseGame instance" warnings in place, and it does not actually satisfy the acceptance criterion — the fallback branches still mention `BaseGame` by name in comments and intent.

### Alternative C — Maximalist

Approach B *plus* decoupling the concrete `ConfigManager` cast in `SongTransitionStage:543` and re-typing `IStageGame.InputManager` away from `InputManagerCompat`.

Rejected: explicitly out of scope per the non-goals. Flagged as a follow-up.

## Design

### 1. `IStageGame` interface (`Lib/Stage/IStageGame.cs`)

Add three members to the existing `internal interface IStageGame`:

```csharp
Point? MapMouseToVirtual(Point windowPoint);   // window→1280x720 virtual mapping; null never returned today
ITextInputSource? GetTextInputSource();          // platform-window seam; null in headless/test
void RequestExit();                              // terminate the game process
```

All existing members stay. Add `using DTXMania.Game.Lib.UI.Components;` for `ITextInputSource`. The interface remains `internal`.

### 2. `BaseGame` implementation (`Game1.cs`)

- Add `IStageGame` to `BaseGame`'s implemented interfaces. (`BaseGame` already declares `: IGameContext`; all `IStageGame` members are already present except the three new ones.)
- `MapMouseToVirtual` already exists as `internal Point? MapMouseToVirtual(Point)` at `Game1.cs:476`. Its signature already matches the interface; keep it as a normal `internal` method that implicitly implements the `IStageGame` member (both are `internal`, so no visibility change is needed). Do not add a separate explicit implementation.
- `RequestExit()` is a new `public` method that calls the inherited `Exit()`. It implicitly implements the `IStageGame.RequestExit()` member.
- `GetTextInputSource()` returns `Window != null ? new WindowTextInputSource(Window) : null`, preserving the existing headless null-guard that `SongSelectionStage` currently implements inline.

### 3. `BaseStage` retyping (`Lib/Stage/BaseStage.cs`)

- `protected readonly BaseGame _game;` → `protected readonly IStageGame _game;`
- Constructor `protected BaseStage(BaseGame game)` → `protected BaseStage(IStageGame game)`.
- The only `_game.` access in `BaseStage` is `_game.ResourceManager` (line 290), already on the interface. No other change required here.

### 4. Stage subclass migration

Replace each `is BaseGame baseGame` cast with a direct `_game.` call (every cast target is already on `IStageGame`), and migrate the three new members:

| Stage | Casts to delete (line → replacement) | New-member migration |
|---|---|---|
| **TitleStage** | 358, 444, 450, 459, 468 → `_game.CanPerformStageTransition()` / `_game.MarkStageTransition()` | `_game.Exit()` → `_game.RequestExit()` (361, 472); `_game.MapMouseToVirtual(...)` (482) unchanged |
| **SongTransitionStage** | 570, 831, 839 → `_game.CanPerformStageTransition()` | — |
| **SongSelectionStage** | 225 (`(_game as BaseGame)?.InputManager` → `_game.InputManager`), 230, 956, 1520, 1709, 1739 | `_game.MapMouseToVirtual(...)` (1403) unchanged; `_game.Window` block (628–631) → `var src = _game.GetTextInputSource(); if (src != null) { _textInputSource = src; ... }` |
| **PerformanceStage** | 508 | — |
| **ResultStage** | 368 | — |
| **ConfigStage** | none | — |
| **DrumConfigStage** | none | `_game.MapMouseToVirtual(...)` (154, 236) unchanged |
| **StartupStage** | none | — |

### 5. Render-target fallback collapse (`SongSelectionStage`)

`InitializeStageRenderTargets` (1705–1727) and `CleanupStageRenderTargets` (1732+) currently branch on `is BaseGame` with a "fallback for non-BaseGame instances" path that creates an unmanaged `RenderTarget2D` and disposes it manually. Collapse both methods to the single happy path using `_game.GraphicsManager.RenderTargetManager`.

Justification: `IStageGame.GraphicsManager` is non-nullable by interface contract, so the fallback is unreachable after retyping. The `_isUnmanagedRenderTarget` field and its dispose branch become dead code — remove them. This is behavior-neutral: the happy path is the only path ever taken in production today, and the removed branch only fired for "non-BaseGame instances" that cannot exist once `_game` is `IStageGame`.

### 6. Out-of-scope observations (no action this ticket)

- `SongTransitionStage:543` casts `IConfigManager` to concrete `ConfigManager` to call `CreateConfiguredInputManager()`. A separate concrete-dependency smell; flagged for a follow-up ticket, not touched here.
- `IStageGame.InputManager` is typed as the concrete `InputManagerCompat`, not an `IInputManager` interface. Pre-existing decision; out of scope per non-goals.

## Acceptance Criteria

- `BaseStage._game` is typed as `IStageGame`, not `BaseGame`.
- No stage subclass references the concrete `BaseGame` type for service access.
- `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj` and the Windows csproj both succeed.
- `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj` passes with no regressions.
- E2E smoke (`Category=E2E`) still reaches Result with a cleared run — verifies the mouse/virtual mapping path end-to-end through the new `IStageGame.MapMouseToVirtual`.
- Grep guardrail: `rg "is BaseGame|as BaseGame|\(BaseGame\)" Lib/Stage/` returns zero hits.

## Verification

- Build both game csproj files (Mac + Windows).
- Run the Mac test suite; confirm no regressions versus baseline.
- Run the E2E `Category=E2E` smoke; confirm it still reaches Result with a cleared run. This exercises the `_game.MapMouseToVirtual` hit-test path through the new interface member, and the `GetTextInputSource` seam in `SongSelectionStage`.
- Run the grep guardrail above and confirm zero hits in `Lib/Stage/`.

## Notes

- This is an architectural refactor; do not bundle it with feature work.
- The two nitpick fixes from the originating review (cached `vw`/`vh` in `DrumConfigStage.UpdatePopup`/`UpdateSelection`, and the stale comment in `ResultStage.DrawBackground`) are already applied on the working branch and are independent of this ticket.
- Coordinate mapping context: `BaseGame.MapMouseToVirtual` (`Game1.cs:476`) maps raw window mouse coords into the fixed 1280×720 virtual render target; stages author their hit-test rects in virtual space.
