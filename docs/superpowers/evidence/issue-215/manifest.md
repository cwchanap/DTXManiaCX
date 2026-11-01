# Issue #215 — committed visual evidence

This directory contains **eight historical before/after pair PNGs**, plus **three final-fix-wave full-resolution PNGs and their telemetry JSONs** listed below. Only these images/JSONs and this manifest are committed here. Pair frames are reduced to 640×360 each; the new single-frame exports are 1280×720 virtual-target captures. Resizing/annotation is evidence assembly, not production artwork.

## Historical matched-fixture pairs (Tasks 1–4)

Before: Mac game rebuilt from `a8de75f8` in an archived source copy inside this worktree. After: `b0825054`, **before the final review fixes**. Both sides use matching charts/profile, unchanged shipped artwork, 1280×720 configuration and the API capture route. CXNeon's playback group remains at (830,310).

| Committed pair | Observed change at that revision |
| --- | --- |
| [NX normal Result](nx-result-normal-pair.png) | Overlapping white level/skill values become separated charcoal rows; playback group moves onto the top-left plate. This AutoPlay run reports `NotStarted`, not `Saved`. |
| [NX failed-save Result](nx-result-failedsave-pair.png) | Extended plate improves retry guidance contrast. **Historical defect:** this version dims the left edge of FAILED; the final-fix-wave image below supersedes it. |
| [NX missing jacket](nx-missing-jacket-pair.png) | Empty score-bar slots acquire the colorful skin default; profile becomes foreground below BPM/left of preview. Large missing-preview white panel is unchanged. |
| [NX real jacket](nx-real-jacket-pair.png) | Selected real dark jacket remains intact; adjacent missing previews acquire the colorful NX default. |
| [CXNeon normal Result](cxneon-result-normal-pair.png) | Group retains (830,310), with a translucent dark plate; skin value styling is unchanged. AutoPlay save status is `NotStarted`. |
| [CXNeon failed-save Result](cxneon-result-failedsave-pair.png) | Extended plate improves orange retry/leave guidance contrast while retaining the background beam/particles. |
| [CXNeon missing jacket](cxneon-missing-jacket-pair.png) | Empty score-bar slots acquire the dark gridded-disc default; profile clears BPM/preview. |
| [CXNeon real jacket](cxneon-real-jacket-pair.png) | Selected bright real jacket remains intact; adjacent missing previews acquire the distinct dark default. |

The A/B/C fixtures have no PREIMAGE, a valid `jacket.png`, and a nonexistent `absent.png`, respectively. Real jackets copy the other skin's shipped default, so priority over the fallback is visually distinguishable. Failed-save fixtures make the sandbox `songs.db` read-only **before launch** and verify `Failed` / SQLite error 8 in telemetry. These pairs cover the enhanced song-bar path; the basic-path title/thumbnail fix is covered by focused draw-geometry tests, not a separate rendered screenshot.

## Final fix wave — genuine Saved status and FAILED clearance

These captures use the reviewed branch plus the final geometry fixes. Opened all three PNGs as image attachments for visual inspection.

| Committed full-resolution capture | Telemetry | Inspection |
| --- | --- | --- |
| [NX Saved Result](nx-result-saved-1280x720.png) | [state](nx-result-saved-1280x720-state.json) | `SongComplete`, `scoreSaveStatus=Saved`, no error. Green `SCORE SAVE: SAVED` is readable on the short 60%-black plate; level/play/skill rows remain separate. NX plate now ends at X=420. |
| [CXNeon Saved Result](cxneon-result-saved-1280x720.png) | [state](cxneon-result-saved-1280x720-state.json) | `SongComplete`, `scoreSaveStatus=Saved`, no error. Green Saved row is readable; group remains at (830,310) on its unchanged 432px-wide plate, with no jacket/value-panel collision. |
| [NX failed-save Result after fix](nx-result-failedsave-1280x720.png) | [state](nx-result-failedsave-1280x720-state.json) | `PlayerFailed`, `scoreSaveStatus=Failed`, SQLite error 8. Red FAILED is no longer darkened by the plate; retry/leave and WITHOUT SAVING remain readable. The existing long diagnostic still truncates in its two-line bounds. |

**AutoPlay cannot produce Saved in this codebase.** `PerformanceSummary.IsSavable` rejects every run with `UsedAutoPlay=true`, before database saving is attempted. The NX fixture first seeds the played chart's writable speed-100/drums score row (`PlayCount=1`, `BestScore=1000`), then completes a genuine **non-AutoPlay / NoFail** run (no input, 32 misses). That run saves successfully. Replaying the same writable, previously-scored chart with all ten AutoPlay lanes enabled yields 32 perfects and **`NotStarted`**, confirming that a prior score does not bypass the guard. CXNeon's Saved capture likewise uses non-AutoPlay / NoFail. No production save behavior or telemetry was altered to manufacture Saved evidence.

## Second-size native-window attempt — blocked by macOS capture access

A fresh CXNeon run requested **1600×900** in config. The API exports remain **1280×720 virtual-target images**, not physical-window captures. They are local evidence only and do not establish final-blit scaling/filtering.

Attempted native capture in **both SongSelect and Result**:

- Swift `CGPreflightScreenCaptureAccess()` returned **false**. On-screen-only enumeration did not expose the game; `CGWindowListCopyWindowInfo([.optionAll, .excludeDesktopElements], ...)` found the owned `dotnet` window, PID **4287**, window ID **82371**, layer 0, sharing state 0.
- CGWindow bounds were **1568×910 at (16,42)** (outer window bounds, not proof of a 1600×900 drawable). Thus the requested size must not be presented as a measured physical size.
- `/usr/sbin/screencapture -x -l 82371 <sandbox-evidence-output>.png` returned **exit 1** in both stages, with stderr **`could not create image from window`**. Neither native PNG was created.
- macOS screen-recording access is unavailable to this agent process. No permission/settings change was attempted. **Physical scaling remains unverified** and needs a permitted desktop session/controller adjudication; there is no native PNG to inspect.

## Uncommitted originals, scripts and logs

Untouched before/after originals (16 images), three historical Task 0/1 images, profile-extreme/search/missing-file captures, configured-1600×900 virtual exports, reproduction/assembly scripts and build provenance are **not committed**. They remain available on request in the git-ignored repository-root directory:

`.superpowers/sdd/2026-10-31-issue-215-result-and-songselect-visual-fixes/evidence/task-4/`

Its `before-after/` directory holds the full-size pair originals and `historical/` images. `capture.py`, `assemble.py`, `capture-metadata.json` and `build-provenance.txt` are at task-4 root. Original Tasks 0–3 evidence uses the sibling `2026-10-31-issue-215-result-songselect-visual-fixes/evidence/task-{0..3}/` directory (without “and”).

New originals, seeded-fixture reproduction script, Saved/AutoPlay state proof, native window enumeration and exact capture failures are in task-4's **`fix-wave/`** subdirectory. In particular: `capture.py`, `seed-score.json`, `nx-result-autoplay-prior-score-1280x720-state.json`, `cxneon-{songselect,result}-1600x900-windows.log`, and `cxneon-{songselect,result}-1600x900-native-capture.json`. These are local paths, not links to nonexistent committed files.

Local final-wave verification: **285 focused tests passed** (ResultScreenRenderer **108**, SongListDisplay **177**), **9,307 full Mac tests passed**, and the Mac game build succeeded. Logs/TRX are in the same local `fix-wave/` directory. Windows runtime/CI and the permitted physical-window check are not claimed here.
