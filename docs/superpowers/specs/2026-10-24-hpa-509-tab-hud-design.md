# HPA-509 — Tab-mode HUD design

**Linear:** https://linear.app/cwchanap/issue/HPA-509/tab-mode-tab-mode-hud-layout  
**Parent:** HPA-394 · **Dependency:** HPA-411 (merged as #202)  
**Delivery:** one implementation PR, <=3 engineer days. This document is design-only pending review.

## Goal

Make opt-in Tab mode usable with its own 1280×720 HUD, matching approved mockup C while keeping the existing scrolling notation, judgement, scoring, audio and Lanes presentation untouched.

The approved behavior is: title/artist top-left, score top-right, combo and judgement/fast-slow above the fixed x=220 hit line, horizontal LIFE just above the notation, 10 key labels with hit flash, progress with elapsed/total directly below notation, and a bottom row of judgement/skill stats. No pads, vertical progress meter or skill graph in Tab.

## Verified implementation baseline (main after #202)

- `PerformanceStage.OnDraw` already switches on effective Tab via the retained, ready `_notationStrip != null`; Lanes are the fallback. The switch also suppresses falling-note overlays and lane hit effects.
- `DrawUIElements` currently suppresses the four obstructing legacy draws in Tab: `DrawSkillPanel`, `DrawProgressBar`, `_skillPanelDisplay.Draw`, `_skillMeterDisplay.Draw`. It **still** draws the old gauge, score and combo at Lanes positions.
- `PerformanceUILayout.NotationStrip` fixes strip x=0..1280, y=290..580, key mask x=0..195, hit line x=220, and the BackToFront layer depths. These are **not** to be moved by this ticket.
- `SkillPanelDisplay` already maintains Perfect/Great/Good/Poor/Miss/MaxCombo counts and the current achievement/skill display value via existing manager events; `ScoreDisplay`, `ComboDisplay`, `_currentGaugeValue` and `_currentProgressValue` already have the live values. `Poor` should be presented as `OK` in the mockup row, with no judgement rule change.
- `OnJudgementMade` owns the single event fan-out to existing managers and popup spawns. `OnLaneHitForPadFeedback` already receives manual key hits. `NotationStripLayout.GetKeyColumnLabels` and `DrumStaffMapping` own the displayed label positions. Reuse existing input lane identities from HPA-443.

## Layout contract

All Tab-specific bounds, font sizes, colors, paddings, text anchors and depth values belong in a nested `PerformanceUILayout.TabHud` class; Lanes constants must not be modified. Initial 1280×720 safe regions:

| Region | Bounds / placement | Contents |
| --- | --- | --- |
| Header | x=20..1260, y=8..90 | Clamped/ellipsized title + artist left, score digits right |
| Hit feedback | around x=220, y=125..250 | Combo above the hit marker; transient judgement plus fast/slow. Keep out of score/header |
| LIFE | x=195..1260, y=260..283 | LIFE label in left column and horizontal gauge; below feedback and above strip |
| Notation | existing x=0..1280, y=290..580 | **Unchanged** strip, marker and mask; left key labels flash on matching hit |
| Progress | x=195..1260, y=586..617 | Thin horizontal progress fill and elapsed/total at left |
| Stats row | x=16..1264, y=628..704 | Compact tiles: Perfect, Great, Good, OK (Poor), Miss, Max Combo, Fast/Slow, Achievement %, Skill |

These are starting regions rather than a pixel-perfect assertion for dynamic font metrics. Use fixed virtual-space coordinates, legible 1280×720 rendering and bounded text; do not introduce resolution-specific UI frameworks, new navigation or responsive modes. Do not cover hit marker, approaching notes, key column or BGA with an old Lanes overlay.

## Design decision: one presentation owner, existing gameplay owners

Implement a small `TabHudDisplay` (or equivalent single renderer) for Tab drawing/short-lived feedback only. It takes a read-only snapshot of values from the existing stage/managers; it must not own or recalculate the score, combo, life, overall skill, hit windows, note judgements or logical song clock. Build fonts/pixel primitive resources once, not per frame. The stage decides which UI to draw using **only** `_notationStrip != null` and disposes the Tab HUD with other resources.

- Preserve existing score/combo **updates** and their Lanes sprite rendering paths; suppress their legacy position draws in Tab, then present their current values in the Tab header/feedback region. Reuse existing digit sprites/font when relocation is practical; a managed-font fallback is acceptable.
- Keep `SkillPanelDisplay.ProcessJudgement` as owner of cumulative judgement counts. If Fast/Slow cumulative counts do not already exist, extend that one existing stats owner using the sign conventions of `PerformanceUILayout.HitTimingFeedback.ProjectDelta` for successful timed hits; do **not** create a second per-note judgement map. Do not change judgement outcomes, scoring or skill calculations.
- Use the existing song metadata and `_songTimer.GetCurrentMs(_currentGameTime)` plus `_parsedChart.DurationMs` / existing `_currentProgressValue` for progress. Do not introduce an independent timer, duration parser, or speed multiplier.
- Hit feedback: the strip masks x<195. At 100% a missed note is already behind the mask when Miss is registered (>200 ms late), so **do not promise red Miss noteheads, source-note anchors or notehead fades in this PR**. Surface all judgements, including Miss, through clear transient feedback near x=220, and use a brief colored key-label flash for actual lane input/hits. Reuse the existing judgement palette and popup presentation/fallback where feasible. Only visual effects may keep short-lived local state; no duplicate judgement-results data structure. Avoid overlapping duplicate old-lane and Tab popups.
- For label flash, use the notation's existing label y positions and canonical channel/lane mapping; do not reverse-engineer labels from on-screen pixel order or hardcode a second lane-index table. Include manual input even if no chart note matches, and show AutoPlay judged hits without a second autoplay tracker.
- The life and progress fills are simple textured/drawn rectangles using existing white-pixel/fallback conventions. The skill-meter graph stays suppressed. Pause/danger and gameplay state overlays retain existing semantics; placement conflicts should be checked on a real screenshot.

## Render and ownership boundaries

- Keep `PerformanceStage.OnDraw`'s existing base, overlay, UI and additive passes. Tab HUD draws only in the UI pass; key flashes render in front of the strip labels without obscuring them. Respect explicit BackToFront depths and font draw overloads (never silently use depth 0 on a mask-sensitive element).
- Lanes code path and positioning remain **bit-for-bit equivalent in intent**. Tab renderer/font acquisition failures should not break chart playback or silently rewrite the saved `NoteDisplayStyle`; fall back to a readable minimal HUD if optional assets are unavailable. No new scene, config switch, saved-state schema or telemetric mode field.
- Retain existing `_notationStrip` readiness and fallback policy, stage lifecycle/resource cleanup and shared managers. No production changes to `NotationChart`, `NotationStripRenderer` timing/engraving, `JudgementManager`, parser, audio, or note input routing.

## Acceptance and evidence

1. Tab at actual 1280×720: all regions fit, no clipped title/stats, no overlap with the hit marker or notation, BGA remains behind the strip; capture a **real GPU screenshot** on the PR.
2. All ten key labels stay visible; manual input highlights the correct label; an AutoPlay run shows expected label feedback and reaches Result. Miss remains visibly reported even though its notehead is hidden.
3. Counts, score, skill, LIFE, combo, progress and elapsed/total reflect existing manager values; unstarted/zero-duration charts render safely. Fast/Slow follows the existing signed delta convention.
4. Switching back to Lanes is visually unchanged, including legacy score/combo/gauge, skill panel/meter, progress and pad feedback. Invalid Tab renderer still falls back to Lanes.
5. Add focused pure geometry/formatting/state tests and existing-category Tab AutoPlay verification where practical. GPU pixels must be checked by a real screenshot, not claimed from headless tests.

## Explicit exclusions / follow-up gate

No notehead tint/fade, moving the key mask/marker, new custom art/SFX, new chart feedback store, editor work, UI framework migration, arbitrary screen-size redesign, CI pipeline changes, or refactoring the Lanes HUD. If subsequent playtesting justifies visible-on-note resolution effects, raise **a separate follow-up ticket** after the HUD ships; don't expand HPA-509 beyond three engineer days.
