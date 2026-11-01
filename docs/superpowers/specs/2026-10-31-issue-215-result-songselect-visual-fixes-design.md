# Issue #215 — Result / SongSelect Visual Regression Fixes (Design)

**Issue:** https://github.com/cwchanap/DTXManiaCX/issues/215  
**Source:** 2026-10-28/29 headless playtest, consolidated from #211–#214  
**Baseline inspected:** `main` at `752a4fb3fd8a599ed3efd896a6fc624c9ec16201` (2026-10-31)  
**Delivery:** one PR, starting as this docs-only draft and implementing in the same PR after review; target <=3 engineer days.

## Goal and scope

Make four existing UI elements readable under both the unthemed/default `System/` (NX) skin and bundled `System/CXNeon/`, without touching chart parsing, scoring, score-save behavior, audio, input, stage navigation, or gameplay. Preserve current theme overrides. Use the existing MonoGame/UI primitives; no new art, shaders, asset pipeline, or skin subsystem.

This is a **presentation defect**, not an issue with the values or their underlying calculation.

## Findings verified in current code

1. **Result fallback level/play overlap:** `ResultUILayout.SkillPanel.LevelPosition=(198,550)` and `PlayingSkillPosition=(238,537)`; only 13px between baselines. `ResultScreenRenderer.ResolveLevelPosition/ResolvePlayingSkillPosition` already support `Result.LevelX/Y` and `Result.PlayingSkillX/Y`. CXNeon fixes it using `Result.LevelY=525` and `Result.PlayingSkillY=555`. Its panel art also labels these as distinct rows.
2. **SongSelect profile obscured:** `SongSelectionStage.InitializeUI` adds `_playbackProfileLabel` **before** full-screen `_songListDisplay`, `_statusPanel` and `_previewImagePanel`, so later children can paint over it. The label spans X=50..690, Y=326..350. NX's preview frame spans X=250..542/Y=34..326; CXNeon's 430x313 frame spans X=250..680/Y=34..347. Independently, the current difficulty panel header top computes to **Y=368** (first cell Y=389 minus 21px), not inside the reported 326..350 band; do not change difficulty-grid geometry based on that diagnosis alone.
3. **Result playback text over character:** `ResultScreenRenderer.DrawModelText` hardcodes profile/bucket/save text at X=830 and guidance at X=830. The NX `8_background.jpg` has prominent character art in the right region; CXNeon does not. Other Result positions already use `ISkinTheme.GetInt`, but these four do not.
4. **Missing preview is a baked white slot:** `SongBarRenderer.GeneratePreviewImageTexture` returns null when `#PREIMAGE` is absent, and `SongListDisplay.DrawBarInfoWithPerspective` simply skips drawing the preview. Both NX `5_bar score selected.png` and `5_bar score.png` contain a white thumbnail inset; CXNeon's equivalent bars are dark. This is **not** a failed texture load or a request to replace the game's general/default jacket artwork.

## Design decisions

### A. Result: separate level and playing-skill rows

Change **only the built-in fallback** Y positions in `ResultUILayout.SkillPanel`:

| Element | Current default | New default | CXNeon existing override |
| --- | --- | --- | --- |
| Chart level | (198, 550) | **(198, 525)** | Y=525 |
| Playing skill | (238, 537) | **(238, 555)** | Y=555 |
| Game skill | (268, 623) | unchanged | Y=632 |

Keep both X values, fonts, number formatting and the existing independent `Result.LevelX/Y` / `Result.PlayingSkillX/Y` keys. The new default spacing is 30px; verify against the **real NX panel graphic and rendered font**, not merely the coordinates.

### B. SongSelect: profile always foreground and in the free left band

- Set `SongSelectionUILayout.UILabels.PlaybackProfile` to **X=20, Y=326, Width=224, Height=24** (initial 1280x720 virtual-space defaults). Its right edge at 244 leaves a 6px gap before the preview-frame X=250 for both bundled skins; its bottom is at the status-panel Y=350 and above the difficulty header at Y=368.
- Add `_playbackProfileLabel` to `_mainPanel` **after** song list/status/history/preview children, but **before** the search modal, so the label wins normal panel draw order while the modal remains topmost.
- Keep `PlaybackProfileFormatter.Format` and the synchronized play-speed/pitch state unchanged. Continue restoring its authored `Size` after a text update (current `SynchronizeActivePlaySpeed` contract). Retain the existing 16px UI font and shadow unless the actual longest supported speed/pitch text does not fit the 224px slot; in that case choose the smallest localized font/position adjustment, not truncation or a second display-format contract.
- Do not relocate the difficulty grid or resize the preview art. Do not introduce theme keys for this one label unless real skin screenshots demonstrate a need; the shared free band should handle both bundled skins.

### C. Result: move the playback/status group, not each label independently

Add **two optional theme keys**, `Result.PlaybackInfoX` and `Result.PlaybackInfoY`, resolving one group origin and deriving all existing positions from it:

| Element | Offset from group origin | Proposed NX origin (30,108) | CXNeon origin (830,310) |
| --- | --- | --- | --- |
| Playback profile | (0, 0) | (30,108) | (830,310) |
| Score bucket | (0, 26) | (30,134) | (830,336) |
| Score save status | (0, 60) | (30,168) | (830,370) |
| Failure guidance rect | (0, 84), 420x60 | (30,192,420,60) | (830,394,420,60) |

- NX's top-left group sits beneath the existing score (30,58), ends before the skill panel begins at Y=260, and avoids the right-side character. The clear-plate artwork is transparent around this left area; verify with full screenshots.
- CXNeon should set only `Result.PlaybackInfoX=830` / `Result.PlaybackInfoY=310` in `System/CXNeon/Theme.ini` to keep its established layout **unchanged**.
- Implement one small, pure layout resolver using `ISkinTheme.GetInt` and fixed relative offsets. Reuse `ResultUILayout.PlaybackPresentation` colors and status mapping. For contrast on NX's bright background, use a small dark text shadow/outline for this group (including wrapped guidance), with no new texture, panel asset, or render target. Validate visually; adjust **this group** if real measurements show a collision.
- Do not add 8–10 per-line position keys, a global layout engine, or new persisted configuration. Longer save errors continue to wrap in the existing 420x60 guidance rectangle.

### D. Song bars: dark placeholder overlay for missing jacket

- In `SongListDisplay`'s existing score-bar draw paths, when **no preview texture is being drawn**, fill the baked thumbnail slot with a simple dark rectangle via the already-owned `_whitePixel` and existing `SpriteBatch`. Reuse a small helper for selected/unselected preview-slot bounds derived from `SongSelectionUILayout.SongBars` offsets and the actual NX art dimensions (slight padding is allowed to cover the bright rim without reaching the title).
- Render order: **skin bar background → missing-jacket fill (if needed) → clear lamp → actual jacket (when present) → title**. This preserves the lamp overlay and normal preview rendering.
- Score bars only; folder, back-box and random-bar artwork stays untouched. In the basic/fallback song-item path, apply the same missing-preview treatment if it draws a score skin bar without a preview.
- Use a dark fallback color (e.g. `#202C40`) with existing opacity behavior; one optional `SongSelect.MissingPreviewColor` theme override is acceptable if helpful, but no new bitmap, per-song texture, cache entry, image I/O, or font. The placeholder also covers intentionally skipped image loads while fast-scrolling.
- Do **not** change `SongBarRenderer.GeneratePreviewImageTexture` null/file-size/cache/ref-count semantics, `PreviewImagePanel`'s larger default jacket, or the Result screen's default preview.

## Validation contract (actual screenshots are required)

Check the **whole 1280x720 frame**, not isolated rectangles, for both `System/` and CXNeon:

1. Result with normal saved score: level and play values occupy distinct readable rows; group is readable, no character interference, CXNeon's existing positions remain unchanged.
2. Result with **failed save**: status + potentially multi-line retry guidance visible, wrapped and clear of score/panel/rank art. Check also non-failed status.
3. SongSelect with default and high/low supported speed/pitch values: entire profile visible, neither behind a panel nor over the preview art; search modal still renders in front.
4. Song wheel with no `#PREIMAGE` and with a valid preview, both selected and unselected, with fast-scroll and the basic fallback path where feasible: no stark white square, genuine images and clear lamps intact, no title overlap.
5. Repeat at a second window size to confirm the game's existing virtual-resolution scaling remains correct.

Add focused pure layout/rectangle and existing renderer mock tests where useful. A headless test verifying numeric positions **does not prove** the composition: retain before/after screenshot evidence on the PR. There is no requirement to build a new screenshot harness if the existing GameApi playtest workflow is usable.

## Non-goals and delivery guardrails

No changes to HPA-16 screenshots, playback profile values, score buckets, score saving, skins' source art, DTXCreator, game loop, database, audio, or chart format. No new art/SFX task is required because the missing jacket uses a procedural overlay. Keep one implementation PR for this issue; this branch begins docs-only, then the implementing agent extends **the same PR** after design approval. If visual verification reveals a genuinely separate artwork request, track that separately rather than mixing asset generation into this fix.
