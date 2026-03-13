# D8 — Performance History Panel (Deferred)

## Status: Partially implemented — rank/FC icons in difficulty grid are done; dedicated history panel deferred

## Description

DTXManiaNX renders a performance history panel in the status panel area showing past skill/rank results per difficulty level.
DTXManiaCX now renders rank symbols (SS–F) and FC badges inside each difficulty grid cell via `DrawRankSymbol` in `SongStatusPanel`, matching the NX per-cell icon positions (rank at nBoxX+7, FC at nBoxX+42).
A dedicated full-height history panel in the status panel bounds (nBaseX=130, nBaseY=350) is not yet implemented.

## NX Reference

Source: `DTXManiaNX/DTXMania/Code/Stage/05.SongSelection/CActSelectStatusPanel.cs`

- Panel shows historical best rank symbols (SS/S/A/B/C/D/E/F) per difficulty
- Drawn within the status panel bounds (nBaseX=130, nBaseY=350)
- Uses `txランク` rank textures and historical score data from the song database

## What is implemented in CX

- Historical best rank/FC data loaded from `SongScore` entities (PlayCount, BestRank, FullCombo)
- Rank symbols (0–7: SS through F) and FC badge drawn per difficulty cell via `DrawRankSymbol`
- NX-authentic positioning: rank at cell+7, FC badge at cell+42
- Missing data handled gracefully (PlayCount == 0 → no icon)

## What still needs to be implemented

- Dedicated full-height performance history panel in the status panel area (nBaseX=130, nBaseY=350)
- Per-difficulty history panel that mirrors NX's `CActSelectStatusPanel` layout (rank symbols stacked by difficulty level outside the grid)

## Acceptance criteria

1. **Visual alignment**
   `SongStatusPanel` must render each historical rank symbol inside the NX difficulty-grid cell for the matching difficulty/instrument, with a maximum tolerance of `±2px` from the NX reference anchor in `DTXManiaNX`.
   Pass when the symbol, FC badge, and level text all fit without overlap or border clipping; fail if any icon drifts beyond that tolerance or collides with neighboring UI.

2. **Missing data behavior**
   When no matching `SongScore`/`PerformanceHistory` exists, or the score has no meaningful play history, `SongStatusPanel` must render no historical rank symbol and no placeholder/tooltip.
   Pass when the grid remains visually unchanged apart from the missing symbol; fail if stale icons, placeholder glyphs, or empty-state artifacts are rendered.

3. **Required validation**
   Coverage must include unit tests for `SongStatusPanel` rank-texture source-rect/position mapping, integration tests that load best-rank data per difficulty from `SongScore`/`PerformanceHistory`, and a visual regression check against the NX layout reference.
   Pass when all three validation layers succeed; fail if any layer is missing or the rendered output no longer matches the expected NX-aligned layout.

## Notes

- Score data model (`SongScore`, `PerformanceHistory`) already exists in CX
- This is a display feature gap, not a layout bug
- Medium priority: useful feedback for players choosing difficulty
