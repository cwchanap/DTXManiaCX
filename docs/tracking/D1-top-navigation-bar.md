# D1 — Top Navigation Bar (Deferred)

## Status: Not implemented — tracked for future sprint

## Description

DTXManiaNX renders a top navigation bar in the song selection stage showing instrument tabs (Drums / Guitar / Bass) and navigation controls. DTXManiaCX does not render this bar.

## NX Reference

Source: `DTXManiaNX/DTXMania/Code/Stage/05.SongSelection/CActSelectSongList.cs`

- Navigation bar drawn via `txカーソル` and instrument tab textures
- Tab positions are instrument-dependent (Drums/Guitar/Bass)
- Positioned at top of screen (~Y=0–40 region)

## What needs to be implemented in CX

1. Add navigation bar texture rendering to `SongListDisplay` or a new `SongNavBarRenderer` component
2. Render instrument tabs at correct positions matching NX
3. Highlight active instrument tab
4. Wire to instrument-switching input

## Notes

- This is a UI feature gap, not a layout bug
- Low priority: the game is playable without it
- Consider implementing alongside instrument-switching logic
