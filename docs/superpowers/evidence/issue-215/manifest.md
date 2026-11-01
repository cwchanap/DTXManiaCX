# Issue #215 — compact before/after evidence

Each `*-pair.png` is an annotated **before-left / after-right** overview (full frames reduced to 640×360 each). The linked `*-before.png` and `*-after.png` are untouched 1280×720 API exports for close inspection. Annotation/resizing is evidence assembly only, not new game art.

## Matched-fixture pairs

Before: Mac game rebuilt from `a8de75f8` (immediately before Tasks 1–3) in an archived source copy **inside this worktree**. After: current `b0825054`. Both use the same fixtures, current unchanged artwork, 1280×720 configuration, default profile, and the same API capture route. The only current CXNeon Theme additions are the playback-origin keys; the baseline renderer ignores them and uses its original identical (830,310) origin.

| Pair | Originals | Observed difference |
| --- | --- | --- |
| [NX normal Result](nx-result-normal-pair.png) | [before](nx-result-normal-before.png) / [after](nx-result-normal-after.png) | Previously faint/overlapping white level/skill values become separated charcoal rows. Playback group moves off character art to the top-left translucent plate. Normal `NOT STARTED` status remains gray; no unnecessary guidance-height plate. |
| [NX failed-save Result](nx-result-failedsave-pair.png) | [before](nx-result-failedsave-before.png) / [after](nx-result-failedsave-after.png) | Actionable retry/leave guidance moves from pale character art to a readable extended plate above the skill panel. Values become separated/readable. Long SQLite diagnostic still truncates in its existing two-line bounds. |
| [NX missing jacket](nx-missing-jacket-pair.png) | [before](nx-missing-jacket-before.png) / [after](nx-missing-jacket-after.png) | Empty score-bar squares become the colorful skin default; the profile becomes a clear foreground line below the BPM plate and left of the preview. The unchanged large missing-preview panel is still white. |
| [NX real jacket](nx-real-jacket-pair.png) | [before](nx-real-jacket-before.png) / [after](nx-real-jacket-after.png) | Selected real dark/cyan/magenta jacket remains intact; adjacent missing-jacket bars now get the colorful NX default. Profile no longer disappears under BPM/frame/wash. |
| [CXNeon normal Result](cxneon-result-normal-pair.png) | [before](cxneon-result-normal-before.png) / [after](cxneon-result-normal-after.png) | Group stays at **830,310**, now on one subtle dark plate matching the surrounding panels. Level/play/skill typography, colors and placement remain unchanged. |
| [CXNeon failed-save Result](cxneon-result-failedsave-pair.png) | [before](cxneon-result-failedsave-before.png) / [after](cxneon-result-failedsave-after.png) | Same group origin; extended dark plate contains both readable orange instruction lines. The cyan beam/pink particles remain visible through the plate. No skin override warranted. |
| [CXNeon missing jacket](cxneon-missing-jacket-pair.png) | [before](cxneon-missing-jacket-before.png) / [after](cxneon-missing-jacket-after.png) | Empty bar slots become the skin's dark gridded disc thumbnail, recognizable at 44×44. Profile clears the BPM plate/preview and stays above the graph/status headings. |
| [CXNeon real jacket](cxneon-real-jacket-pair.png) | [before](cxneon-real-jacket-before.png) / [after](cxneon-real-jacket-after.png) | Selected bright real jacket stays intact; adjacent missing previews now use the distinct dark skin default. No new image on folder/RANDOM rows. |

A/B/C song fixtures are missing PREIMAGE, valid `jacket.png`, and nonexistent `absent.png`. The real image copies the **other skin's existing** default artwork to make real-preview priority visibly distinct: dark CXNeon art for NX runs, bright NX art for CXNeon runs. No generated/new production artwork. Normal Results use full AutoPlay (`NotStarted` save); failed-save Results use manual failure (`PlayerFailed`) with the seeded database made read-only **before process launch** (`Failed`, SQLite error 8). JSON state companions verify both cases.

## Historical Task 0/1 before captures, copied verbatim

These also pair the original investigation captures with current after evidence. Their charts/profile/note counts differ, so use the matched-fixture pairs above for controlled comparisons.

- [Task 0 normal NX Result](historical/nx-result-normal-task0.png) → [current normal NX Result](nx-result-normal-after.png): original overlapping white values and character-art playback group versus corrected rows/plate.
- [Task 0 NX SongSelect](historical/nx-songselect-task0.png) → [current missing-jacket NX SongSelect](nx-missing-jacket-after.png): old obscured profile/empty thumbnail versus foreground profile/default thumbnail.
- [Task 1 pre-contrast failed-save NX Result](historical/nx-result-failedsave-task1-precontrast.png) → [current failed-save NX Result](nx-result-failedsave-after.png): **intermediate before**, already has relocated plate but still faint white values. It is not a claim that the plate was present before Task 1.

## Additional final sweep (one level up)

- CXNeon extremes: `../cxneon-songselect-{slow-low,fast-high}-1280x720.png` and corresponding `-search-` captures. Whole profile text fits; modal dims it correctly and stays foreground.
- CXNeon missing file: `../cxneon-songselect-missingfile-1280x720.png`; selected nonexistent-file preview gets the same dark default.
- Second configuration: `../cxneon-songselect-{default,real,missingfile}-1600x900.png`, `../cxneon-songselect-default-search-1600x900.png`, `../cxneon-result-{default,failedsave}-1600x900.png`.

**Second-size limitation:** filenames describe the **configured client size**, not PNG pixel dimensions. `takeScreenshot` captures the fixed virtual render target before the backbuffer blit (`BaseGame.CapturePendingScreenshot`); all PNGs are 1280×720. Companion states report configured 1600×900. Both screens' virtual composition remained correct at that launch size, but these are **not physical 1600×900 backbuffer/window captures**. No native-window image was obtained; final-blit scaling/filtering remains a controller/human visual check. The full Mac suite includes the existing `CalculateLetterboxDestination` tests, but those are not a substitute for a physical-window screenshot.

## Reproduction/provenance

- `../capture.py`: fresh isolated Config.ini/database fixtures, JSON-RPC drive, strict real failed-save assertion, owned process cleanup.
- `../capture-continuation.log`: completed remaining matrix after correcting the SQLite setup. `../capture-nx-distinct-jacket.log`: final NX recapture with a visibly distinct valid jacket. `../capture-before.log`: matched baseline captures.
- `../baseline-build.log`: source archive rebuild. From the repository root, recreate `evidence/task-4/baseline/` with `git archive a8de75f8 DTXMania.Game .config Directory.Build.props tools/ffmpeg | tar -x -C <task-4-evidence-dir>/baseline`, build that copy's Mac project, then run `python3 <task-4-evidence-dir>/capture.py before` with fresh sandbox directories. The script expects the archived binary at that exact `baseline/` location.
- `../assemble.py`: copies originals, creates annotated pairs, validates stage/save states and records actual PNG/configured dimensions in `../capture-metadata.json`.
- `../build-provenance.txt`: source revisions and assembly checksums captured before removing temporary build trees.

All evidence remains local/git-ignored under `.superpowers`, as in Tasks 0–3. Controller must upload/link this bundle in PR #217 and gather authoritative Windows CI after pushing. This task did not push or modify PR metadata.
