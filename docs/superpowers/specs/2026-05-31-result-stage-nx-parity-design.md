# Result Stage NX Parity - Design

Date: 2026-05-31
Status: Approved design, pending implementation plan

## Goal

Make the CX result stage look and behave like DTXManiaNX as much as possible using the data and assets CX already has. This replaces the current centered text report with an NX-style result screen while avoiding parser and persistence expansion in this slice.

## Background

CX already loads `Graphics/8_background.jpg` for `ResultStage`, but the foreground is still a simple centered text list. NX's result screen is asset-driven: it layers a result background, rank badge, clear/full-combo/excellent plate, jacket/result image area, skill panel, score, judgement breakdown, song metadata, and delayed result reveal behavior.

The required structural assets already exist in `System/Graphics`, including `8_background.jpg`, `8_rank*.png`, `ScreenResult StageCleared.png`, `ScreenResult fullcombo.png`, `ScreenResult Excellent.png`, `7_JacketPanel.png`, `7_SkillPanel.png`, and `8_New Record.png`.

## Scope

In scope:

- Draw an NX-style 1280x720 result layout for drums.
- Use existing CX result data from `PerformanceSummary`, `selectedSong`, `selectedDifficulty`, and the selected chart.
- Use NX structural bitmap assets for the background, rank badge, result plate, jacket frame, skill panel frame, and new-record badge.
- Use MonoGame sprite fonts through CX's existing `IFont`/`ManagedFont` path for score, judgement counts, percentages, level, skill values, title, and artist.
- Add NX-style reveal timing.
- Make confirm/back complete the reveal first, then return to song select after the reveal is complete.
- Select result sounds based on the visible result category when CX has matching sound resources.
- Fall back cleanly when optional assets, selected song data, or chart data are missing.

Out of scope:

- Parsing or honoring `#RESULTIMAGE_*`, `#RESULTMOVIE_*`, and `#RESULTSOUND_*`.
- Result movie playback.
- Result screen capture and auto-save.
- Guitar/bass result panels.
- Ghost and progress bar compatibility.
- Broad result persistence changes beyond the existing `SongManager.UpdateScoreAsync` call.

## Decisions

| Area | Decision | Rationale |
|---|---|---|
| Implementation shape | Split into a result model and result renderer | Keeps `ResultStage` from becoming a dense drawing/data-conversion class. |
| Coordinate system | Draw in NX's native 1280x720 layout | Preserves NX placement and asset proportions. |
| Non-1280x720 viewport | Uniform scale from 1280x720 | Keeps the result screen faithful instead of reflowing. |
| Numeric rendering | Use `IFont`/`ManagedFont` | Simpler, more robust, and aligns with the user's preference over bitmap number sheets. |
| NX metadata support | Use existing CX chart metadata only | Avoids expanding the DTX parser and runtime contract in this slice. |
| New record | Infer from pre-save in-memory score/skill when available | Gives the NX cue without changing persistence semantics. |

## Architecture

`ResultStage` remains the stage lifecycle owner. It extracts shared data, persists the result, updates reveal state, handles input, and transitions back to song select.

Add `ResultScreenModel` as the testable display-data layer. It is built from:

- `PerformanceSummary`
- `SongListNode? selectedSong`
- selected difficulty index
- selected `SongChart?`
- previous `SongScore?` for new-record detection

The model computes:

- Rank index and rank label.
- Result plate category: Excellent, Full Combo, Stage Cleared, Failed.
- Formatted score, max combo, judgement counts, judgement percentages, playing skill, game skill, and chart level.
- Song title and artist fallbacks.
- Preview image path or default preview fallback.
- New-record flag.

Add a result-specific renderer, preferably `ResultScreenRenderer`, to own resource loading and NX-style drawing. The renderer depends on `IResourceManager`, `GraphicsDevice`, and the existing result font. It should expose a narrow surface such as:

```csharp
public sealed class ResultScreenRenderer : IDisposable
{
    public ResultScreenRenderer(IResourceManager resources, GraphicsDevice graphicsDevice, IFont font);
    public void Load(ResultScreenModel model);
    public void Draw(SpriteBatch spriteBatch, ResultScreenModel model, ResultRevealState reveal, Viewport viewport);
}
```

Move fixed layout coordinates into `ResultUILayout`. Move structural result asset paths into `TexturePath` constants.

## Data Flow

On activation:

1. Extract `performanceSummary`, `selectedSong`, and `selectedDifficulty`.
2. Resolve the selected chart via `selectedSong.GetCurrentDifficultyChart(selectedDifficulty)`.
3. Resolve the previous score for that chart/difficulty before persistence. Prefer a score whose `ChartId` matches the selected chart; otherwise use `selectedSong.GetScore(selectedDifficulty)` as the legacy UI fallback.
4. Build `ResultScreenModel`.
5. Persist the result with the existing `SongManager.Instance.UpdateScoreAsync(...)` path.
6. Initialize renderer resources and reveal state.
7. Clear pending input commands to avoid immediate transition.

Build the model before persistence so new-record detection compares against the old in-memory score/skill values.

## Rank And Plate Rules

Rank uses the NX modern rank thresholds over the current play's `PlayingSkill`:

| Rank | Threshold |
|---|---:|
| SS | `>= 95` |
| S | `>= 80` |
| A | `>= 73` |
| B | `>= 63` |
| C | `>= 53` |
| D | `>= 45` |
| E | below `45` |

The result plate is selected in priority order:

1. Excellent: `TotalNotes > 0` and `PerfectCount == TotalNotes`.
2. Full Combo: `ClearFlag == true`, `PoorCount == 0`, and `MissCount == 0`.
3. Stage Cleared: `ClearFlag == true`.
4. Failed: no NX failed plate exists in the current skin, so use the same layout with a clear text fallback instead of a missing texture.

## Rendering

The renderer draws in this layer order:

1. Result background, normally `TexturePath.ResultBackground`.
2. Rank-specific background if a matching NX skin asset exists, using paths such as `Graphics/8_background rankSS.png` through `Graphics/8_background rankE.png`; otherwise keep `TexturePath.ResultBackground`.
3. Rank badge, using `8_rankSS.png` through `8_rankE.png`.
4. Result plate: Excellent, Full Combo, or Stage Cleared.
5. Jacket frame: `7_JacketPanel.png`.
6. Preview/default image, scaled into the NX jacket area.
7. Skill panel frame: `7_SkillPanel.png`.
8. Font-rendered judgement counts and percentages.
9. Font-rendered playing skill, game skill, chart level, and score.
10. Font-rendered song title and artist.
11. New-record badge when applicable.

The renderer should use `IResourceManager.ResourceExists` for optional structural assets before loading, because `LoadTexture` returns fallback textures. Missing optional assets should disable that element, not draw a fallback placeholder that looks like a real result component.

## Reveal Behavior

Use elapsed-time based reveal state, independent of frame rate.

Recommended reveal phases:

- `RankReveal`: rank badge reveals with an NX-like vertical crop.
- `PanelReveal`: result plate, jacket, and skill panel become visible.
- `Complete`: all static elements visible and navigation is allowed.

Input behavior:

- Confirm/back during reveal completes all reveal phases immediately.
- Confirm/back after reveal returns to song select using the existing `DTXManiaFadeTransition`.
- Navigation still respects `BaseGame.CanPerformStageTransition()`.

## Sounds

When the result stage starts, choose a sound category from the same model state used for the plate:

- Excellent -> excellent sound if available.
- Full Combo -> full-combo sound if available.
- Otherwise cleared/failed -> stage-clear sound if available.

If CX does not expose one of these skin sounds through the current resource layer, skip that sound rather than introducing a broad skin-system change in this slice.

## Error Handling

- Missing `PerformanceSummary`: build the existing default zero summary.
- Missing selected song/chart: render "Unknown Song", "Unknown Artist", zero level, and default preview.
- Missing preview image: draw `Graphics/5_preimage default.png`.
- Missing structural asset: omit only that layer and keep the rest of the result screen usable.
- Missing font: keep the current rectangle fallback only for essential navigation/text placeholders.
- Persistence failure should not block showing the result screen; log and continue.

## Tests

Add or update unit tests for model behavior:

- Rank threshold boundaries.
- Plate selection for excellent, full combo, stage cleared, and failed.
- Judgement percentage formatting with non-zero and zero totals.
- Score, level, playing skill, and game skill formatting.
- Preview image fallback selection.
- New-record detection from previous score and skill data.

Add or update `ResultStage` tests:

- Activation builds the model before save.
- Confirm/back during reveal completes reveal without navigating.
- Confirm/back after reveal navigates to song select.
- Missing selected song/chart does not throw.
- Missing optional assets do not throw.

Focused verification:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ResultStage"
```

Final verification:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
```

## Implementation Notes

- Prefer `ResultScreenModel` tests over graphics-device tests for formatting and decision logic.
- Keep renderer seams narrow so tests can verify load/draw decisions without requiring real MonoGame rendering.
- Avoid changing DTX parser entities for `RESULTIMAGE`, `RESULTMOVIE`, or `RESULTSOUND` in this slice.
- Keep legacy `DTXManiaNX` as reference only.
