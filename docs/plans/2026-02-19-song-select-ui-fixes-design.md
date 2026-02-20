# Song Selection UI Fixes — Design Doc

**Date**: 2026-02-19
**Branch**: fix/song-selection-ui-authentic
**Approach**: Option B — Constants + targeted rendering fixes

## Scope

Fix discrepancies D2–D7 between DTXManiaNX and DTXManiaCX song selection UI.
D1 and D8 deferred to tracking docs. D9 confirmed correct. D10 skipped.

## Section 1: Layout constant fixes (`SongSelectionUILayout.cs`)

| Discrepancy | Constant | Current | Correct (NX source) |
|---|---|---|---|
| D3 | `DifficultyGrid.BaseX` | `150` | `130` |
| D4 | `DifficultyGrid.BaseY` | `400` | `391` |
| D5 | `BPMSection.X` | `32` | `90` |
| D5 | `BPMSection.Y` | `258` | `275` |
| D5 | `BPMSection.LengthTextPosition` | `(X+75, Y)` → `(107,258)` | `(X+42, Y-7)` → `(132,268)` |
| D5 | `BPMSection.BPMTextPosition` | `(X+75, Y+20)` → `(107,278)` | `(X+45, Y+23)` → `(135,298)` |
| D7 | `NoteDistributionBars.Drums.StartX` | `50` | `46` |
| D7 | `NoteDistributionBars.GuitarBass.BarSpacing` | `10` | `6` |

New constants to add under `SongBars`:

```csharp
public const int ArtistNameAbsoluteRightEdge = 1235; // = 1260 - 25 from NX
public const int ArtistNameAbsoluteY = 320;
```

NX source refs:
- `CActSelectStatusPanel.cs` lines 294–301, 399, 401, 487–488, 517, 537, 461–476
- `CActSelectArtistComment.cs` lines 190–192

## Section 2: Rendering code fixes (`SongListDisplay.cs`)

### D6 — Selected bar texture Y offset

In `DrawBarInfoWithPerspective()`, when `isCenter == true`:
- Draw skin bar texture at `itemBounds.Y - 30` (currently `itemBounds.Y`)
- Title, preview image, lamp stay at `itemBounds.Y`

NX source: `CActSelectSongList.cs` lines 1083–1091 draws bar texture at `(665, y選曲-30)` = `(665, 239)`.

### D2 — Artist name absolute positioning

In `DrawArtistName()` and `DrawArtistNameWithManagedFont()`:
- Replace `artistX = itemBounds.Right - 10 - textWidth` with `artistX = SongBars.ArtistNameAbsoluteRightEdge - textWidth`
- Replace `artistY = itemBounds.Bottom + 8` with `artistY = SongBars.ArtistNameAbsoluteY`

NX source: `CActSelectArtistComment.cs` lines 190–192 draws at `x = 1260 - 25 - textureWidth`, `y = 320`.

## Section 3: Tracking documents

Lightweight markdown files to record deferred discrepancies:

- `docs/tracking/D1-top-navigation-bar.md`
- `docs/tracking/D8-performance-history-panel.md`
