# D8 — Performance History Panel (Deferred)

## Status: Not implemented — tracked for future sprint

## Description

DTXManiaNX renders a performance history panel in the status panel area showing past skill/rank results per difficulty level. DTXManiaCX does not render this panel.

## NX Reference

Source: `DTXManiaNX/DTXMania/Code/Stage/05.SongSelection/CActSelectStatusPanel.cs`

- Panel shows historical best rank symbols (SS/S/A/B/C) per difficulty
- Drawn within the status panel bounds (nBaseX=130, nBaseY=350)
- Uses `txランク` rank textures and historical score data from the song database

## What needs to be implemented in CX

1. Load historical performance data (best rank per difficulty) from `SongScore`/`PerformanceHistory` entities
2. Add rank texture rendering to `SongStatusPanel`
3. Position rank symbols within the existing difficulty grid area, matching NX layout
4. Handle missing data gracefully (no history = no symbols)

## Notes

- Score data model (`SongScore`, `PerformanceHistory`) already exists in CX
- This is a display feature gap, not a layout bug
- Medium priority: useful feedback for players choosing difficulty
