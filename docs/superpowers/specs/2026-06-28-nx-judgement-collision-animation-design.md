# NX Judgement Collision Animation Design

> **STATUS NOTE (2026-06-30):** The design below originally called for slicing the
> combined `ScreenPlayDrums chip fire.png` sheet into per-lane animation frames.
> That approach was abandoned in commits `de9323f` and `3a9ac4c` in favor of
> porting NX's actual per-lane fire model (loading individual
> `ScreenPlayDrums chip fire_*.png` assets directly). The combined sheet asset and
> its `ChipFireCombined` texture path constant have been removed. References to
> the combined-sheet approach below are retained for historical context.

## Context

DTXManiaCX currently shows performance-stage judgement feedback through two small systems:

- `JudgementTextPopupManager` renders font text per lane and fades it upward.
- `EffectsManager` renders `Graphics/hit_fx.png`, which is currently a tiny 8x32 single-frame bundled asset.

DTXManiaNX has a richer judgement-collision presentation. The relevant NX references are:

- `DTXManiaNX/DTXMania/Code/Stage/07.Performance/DrumsScreen/CActPerfDrumsChipFireD.cs`
- `DTXManiaNX/DTXMania/Code/Stage/07.Performance/CActPerfCommonJudgementString.cs`
- `DTXManiaNX/DTXMania/Code/Stage/07.Performance/DrumsScreen/CActPerfDrumsJudgementString.cs`
- `DTXManiaNX/DTXMania/Code/App/CSkin.cs`

The bundled `System/Graphics` skin already includes NX-style assets such as `ScreenPlayDrums chip fire.png`, per-lane chip-fire images, chip-wave images, chip-star images, and `7_JudgeStrings_XG.png`.

## Goal

Implement a bundled, NX-style judgement collision animation for the gameplay stage and align judgement text styling with bundled sprite art.

The feature should make successful note collisions feel like NX: a visible lane-local burst at the judgement line, secondary particles, waves, and sprite-based judgement words.

## Non-Goals

- Do not add config UI or user-facing settings in this pass.
- Do not port every NX config option such as `JudgeAnimeType`, `ExplosionFrames`, `ExplosionInterval`, `AttackEffect`, or custom effect dimensions.
- Do not change judgement timing, scoring, combo, gauge, MIDI input, autoplay semantics, or chart parsing.
- Do not modify the legacy `DTXManiaNX` source tree.

## Approved Approach

Use fixed bundled defaults and build the renderer so future config wiring can be added without rewriting the rendering model.

This is intentionally narrower than a complete NX config port, but broader than a simple spark replacement. It includes spark, stars, chip fragments, waves, and sprite judgement text.

## Architecture

Keep the existing event-driven gameplay flow. `PerformanceStage.OnJudgementMade` remains the only place that turns judgement results into visual feedback.

The new rendering pieces are isolated:

- `NxAttackEffectManager`
  - Owns active spark, star, chip-fragment, and wave instances.
  - Loads bundled NX-style textures through `IResourceManager`.
  - Updates animation state using `deltaTime`.
  - Draws additive effects near the judgement line.
- `NxAttackEffectSettings`
  - Internal settings object for fixed defaults such as frame dimensions, frame interval, particle counts, and effect lifetime.
  - Exists so future config wiring can pass values into the manager without changing the manager's public shape.
- `NxAttackEffectInstance` model types
  - Store deterministic per-instance state: lane, source frame, position, velocity, scale, alpha, rotation, elapsed time, and expiry.
- `SpriteJudgementTextPopupManager`
  - Replaces font-first judgement display with sprite-first rendering from `7_JudgeStrings_XG.png`.
  - Keeps the current per-lane spawn/update/draw lifecycle.
  - Falls back to the existing font popup behavior if sprite art is unavailable.
- `TexturePath` and `PerformanceUILayout`
  - Add explicit bundled asset paths and source-rectangle/frame constants.
  - Avoid hardcoded texture names and magic frame geometry inside renderer logic.

## Data Flow

Judgement data flow remains:

```text
JudgementManager.JudgementMade
  -> ScoreManager.ProcessJudgement
  -> ComboManager.ProcessJudgement
  -> GaugeManager.ProcessJudgement
  -> SkillManager.ProcessJudgement
  -> SkillPanelDisplay.ProcessJudgement
  -> if e.IsHit(): NxAttackEffectManager.Spawn(e.Lane, e.Type)
  -> SpriteJudgementTextPopupManager.SpawnPopup(e)
```

`Miss` does not spawn the attack effect. It still spawns judgement text.

## Attack Effect Behavior

Successful judgements (`Perfect`, `Great`, `Good`, `Poor`) spawn the full bundled attack animation.

Primary spark behavior:

- Use `Graphics/ScreenPlayDrums chip fire.png` as the preferred combined spark sheet.
- Slice it by lane row and frame column using fixed bundled defaults.
- Draw at the lane center and judgement-line Y position.
- Restart the active primary spark for the same lane when a new hit lands on that lane, matching the NX behavior of replacing lane-local fire.

Fallback spark behavior:

- If the combined sheet is missing or cannot be sliced safely, use per-lane images such as `Graphics/ScreenPlayDrums chip fire_LC.png`.
- If a per-lane image is missing, skip that lane's spark without breaking gameplay.

Secondary particle behavior:

- Spawn star particles from `Graphics/ScreenPlayDrums chip star_<lane>.png` when present.
- Spawn small chip fragments from `Graphics/7_Chips_drums.png` when present.
- Spawn wave pulses from `Graphics/ScreenPlayDrums chip wave.png` and, if available, `Graphics/ScreenPlayDrums chip wave2.png`.
- Secondary particles may overlap briefly across repeated hits.
- Motion is deterministic enough for unit testing: fixed counts and bounded pseudo-random ranges from an injectable random source or deterministic default.

Draw order:

- Attack effects draw in an additive pass near the judgement line.
- Effects should remain visually tied to the lanes and should not cover the score, gauge, progress, or skill panels.
- If necessary, clamp particle bounds or alpha so the UI remains readable.

## Judgement Text Behavior

Replace font-rendered judgement text with bundled sprite art by default.

Sprite source:

- Preferred asset: `Graphics/7_JudgeStrings_XG.png`.
- `Perfect`, `Great`, `Good`, `Poor`, and `Miss` map to fixed source rectangles.
- `Poor` displays the bundled `OK` art, preserving the current display label behavior.

Placement and animation:

- Keep judgement text per lane, centered near the judgement line.
- Use the current lane-center calculation from `PerformanceUILayout`.
- Animate with a short scale pop and fade.
- Avoid the current text-only rise behavior as the primary presentation.

Fallback:

- If `7_JudgeStrings_XG.png` is missing or invalid, use the current font popup behavior.
- Font fallback keeps custom skins without the bundled sprite sheet functional.

## Asset Ownership

All loaded textures must follow the existing resource-manager reference-counting rules:

- `IResourceManager.LoadTexture` adds a reference.
- Managers release their references with `RemoveReference()` on dispose.
- Sprite views must not dispose shared `Texture2D` instances from the cache.

Optional assets are best-effort. Missing optional particles should produce no gameplay error.

## Testing

Add logic-focused tests that are safe for the Mac test project.

Attack effect tests:

- Combined spark sheet frame mapping.
- Lane-to-row mapping and fallback to per-lane textures.
- Spawn on `Perfect`, `Great`, `Good`, and `Poor`; no attack effect on `Miss`.
- Same-lane primary spark restart.
- Secondary particle expiry.
- Missing optional assets are skipped.
- Missing primary combined sheet uses fallback textures.

Judgement text tests:

- Sprite source rectangles for `Perfect`, `Great`, `Good`, `Poor/OK`, and `Miss`.
- Per-lane placement uses `PerformanceUILayout.GetLaneX`.
- Scale/fade lifecycle expires predictably.
- Missing sprite sheet uses font fallback.
- Existing `JudgementTextPopupTests` are updated rather than discarded where the lifecycle assertions still apply.

Integration tests:

- `PerformanceStage.OnJudgementMade` still forwards judgement data to scoring, combo, gauge, skill, attack effects, pad feedback, and judgement text.
- Tests should use seams or test factories instead of requiring a live graphics device where possible.

Verification:

- Run `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`.
- If feasible, launch the Mac game with autoplay enabled and visually inspect a deterministic chart for sparks, particles, waves, and sprite judgement text.

## Risks

- The NX combined spark sheet is large and easy to slice incorrectly. Mitigate with explicit frame constants and mapping tests.
- Additive blending can obscure UI if particles travel too far. Keep particle motion bounded near lanes.
- Resource ownership bugs can poison cached textures between stage activations. Preserve the existing `RemoveReference()` pattern and add disposal tests.
- Sprite judgement text could be unreadable if placed too low or if scale is too large. Keep per-lane text near the existing popup location and test destination geometry.

## Implementation Boundary

This design is one implementation plan. It touches performance visual feedback only:

- `DTXMania.Game/Lib/Stage/Performance/`
- `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- `DTXMania.Game/Lib/Resources/TexturePath.cs`
- `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs`
- Focused tests under `DTXMania.Test/Stage/Performance/`, `DTXMania.Test/Resources/`, and `DTXMania.Test/UI/`.

No gameplay rules or input routing should change.
