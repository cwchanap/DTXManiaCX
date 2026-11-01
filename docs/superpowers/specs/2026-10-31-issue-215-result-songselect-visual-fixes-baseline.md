# Issue #215 — Composition & Art Baseline (Task 0)

Recorded before any implementation changes. Worktree base `7e90517d`.
Method: real game captures via the JSON-RPC GameApi (`takeScreenshot`) with a
sandboxed `DTXMANIA_APPDATA_ROOT` (default NX skin from `System/`), plus numeric
pixel measurement of the shipped art (PIL). All values below were measured or
read from code in this tree — not assumed from the design doc.

## Screenshots (1280×720, default NX skin)

Evidence directory: `.superpowers/sdd/2026-10-31-issue-215-result-songselect-visual-fixes/evidence/task-0/`

| File | What it shows | Validity |
| --- | --- | --- |
| `nx-result-1280x720.png` (+`-late` variant) | Result, autoplay PERFECT-clear (score 999996, 6/6), save status **NotStarted** (HPA-18: assisted runs never persist) | Block-mean diff vs `System/Graphics/8_background.jpg` = 8.4 → real NX Result art |
| `nx-songselect-1280x720.png` + `nx-songselect-verify-…` | SongSelect, default profile | Two independent runs; mean abs diff 3.79 → composition reproducible |
| `nx-songselect-label-band-…-3x.png` | 3× zoom of X=30..720, Y=315..355 | Crop of capture |
| `nx_8bg_left/right_region_…png` | Regions of NX `8_background.jpg` | corr = 1.000 vs art |
| `{nx,cxneon}_5_preimage_{default,panel}.png` | Full art files, both skins | corr = 1.000 vs art |

Result playback-group text bands found in the capture (diff vs art):
Y 318..331 (profile), Y 343..357 (bucket), Y 378..391 (status), all at
X ≥ 830 — i.e. exactly over the right-side character, confirming defect 3.
CXNeon result screenshots were not captured (Task 4 sweep); CXNeon facts below
come from its shipped `Theme.ini` and art.

## Art metrics (natural sizes, both skins unless noted)

| Asset | NX | CXNeon | Drawn at | Notes |
| --- | --- | --- | --- | --- |
| `8_background.jpg` | 1280×720 | 1280×720 | (0,0) | NX bright at proposed group region X=30..450/Y=108..252: mean gray 193, 60% pixels ≥192, 0% dark → dark backdrop required. Pixel (30,108)=155. CXNeon same region mean 39 (100% dark) |
| `7_SkillPanel.png` | 257×439 | 257×439 | (180,260) | Panel has alpha; over NX bright art it reads light |
| `5_BPM.png` | 187×67 | 187×67 | (32,275), **natural size, no scaling** (`SongStatusPanel.DrawBPMBackground`) → occupies X=32..219, Y=275..342 | `SongSelectionUILayout.BPMSection.Height=50` is NOT the drawn height (spec calls for a 67px native-height contract) |
| `5_preimage panel.png` | 430×313 | 430×313 | (250,34) at natural size (`PreviewImagePanel`) → frame X=250..680, Y=34..347 | Matches spec's preview-frame bounds |
| `5_preimage default.png` | 400×400, mean gray 188 (bright, colorful, sat 66) | 400×400, mean gray 62 (dark navy) | not wired into song bars | NX intentionally bright, CXNeon dark — as spec states |
| `5_status panel.png` | 561×342, **100% transparent** | 561×342, **100% transparent** | (130,350) | The status panel background is fully invisible in both skins — the proposed label band crosses only empty top-left area |
| `5_difficulty panel.png` | 561×321 (53% opaque + 43% semi) | 561×321 (26% opaque + 60% semi) | texture top Y=368 (header = 321−5×60 = 21px above first cell row at Y=389) | Difficulty-header top Y=368 confirmed |
| `5_graph panel drums.png` | 110×321 | 110×321 | (15,368) | Starts below the proposed label band |

`SongSelectionUILayout.UILabels.PlaybackProfile` today: X=50, Y=326, W=640,
H=24 (band X=50..690, Y=326..350) — conflicts with CXNeon preview frame
(X≥250 overlap Y≤347). `SongSelectionUILayout.DifficultyLabelPosition=(160,352)`
is **dead code** (never drawn) — no conflict with the proposed Y=342..366 band.
In the capture the current label's wash-dimmed ink sits at X=53..280, Y=334..347:
the 70%-black `SongListDisplay` wash (`Color.Black * 0.7f` full-screen) leaves it
at ~(37,76,0) — effectively invisible. Defect 2 confirmed on-pixel.

## Result layout facts (code-authoritative)

- Defaults: level (198,550), playing skill (238,537) — ΔY=13 — game skill
  (268,623), score (30,58); `ResolveValueRightX` default 0 → left-aligned.
- Fonts: NX default has no `Result.FontValueFamily` → level/skill values use
  `_normalFont` = LoadFont("NotoSerifJP", 20) → baked **NotoSerifJP-24** asset
  (~23px glyphs per code comment, ~28px line box). A 13px offset cannot
  separate ~28px line boxes → glyph-bounds overlap. Defect 1 confirmed.
- Playback group: `ResultUILayout.PlaybackPresentation` (830,310)/(830,336)/
  (830,370)/guidance (830,394,420×60), fixed in `DrawModelText`, no theme keys.
- CXNeon `Theme.ini`: `Result.LevelY=525`, `Result.PlayingSkillY=555`,
  `Result.GameSkillY=632`, `Result.ValueRightX=421` (right-aligned),
  `Result.FontValueFamily=Orbitron`; no `Result.PlaybackInfoX/Y` keys yet.

## PlaybackProfileFormatter width vs 224px slot — DOES NOT FIT

- Formatter max-width string: `PLAY 1.50x · PITCH +12 st` (NotoSerifJP digits
  are tabular, so every speed form ties at the same width; "+12 st" is the
  widest pitch form — the plus sign is ~5px wider than the hyphen, so
  `1.50x/-12` measures 233.9px). All 525 speed×pitch combinations were
  enumerated (re-verified in fix round 1).
- SongSelect label font: `LoadFont("Arial", 16)` → **NotoSerifJP** baked 14pt
  asset (family fallback), i.e. the "16px UI font" renders ≈18.7px em.
- Ground truth: the default string `PLAY 1.00x · PITCH +0 st` measures **228px**
  of ink (X=53..280) in the captured frame. Calibrating `Content/NotoSerifJP.ttf`
  to that ink (em ≈19.1px; strict 14pt@96dpi = 18.67px gives 222px):
  default ≈ 228px, max ≈ **238px** (233.9px for `1.50x/-12`, 238.3px for `0.50x/+12`).
- Verdict: the full supported range **exceeds the proposed 224px slot** by
  ~10–14px; even the default string is ≥224px.
- **Decided correction (spec B, small local font adjustment): draw the label
  text at 0.9× scale of the baked 14pt NotoSerifJP asset; geometry unchanged
  from the spec** — slot X=20, Y=342, W=224, H=24 (band X=20..244, Y=342..366:
  left edge X=20 stays, top Y=342 sits on the 67px BPM plate bottom, bottom
  366 precedes difficulty-header top 368). Expected max text width under the
  correction: **≈215px** (238.3 × 0.9 = 214.5; default string 228 → ≈205px),
  i.e. ≈9px slot slack and ink right edge ≈X=235, ≈15px clear of the
  preview-frame left X=250 — still within the slot at the ±5px worst case
  (220px ≤ 224px). No truncation, no `PlaybackProfileFormatter` change.
  Why the alternatives were rejected: with the left edge pinned at X=20 and
  the frame at X=250 the widest possible slot is 229px < 238px max text (a
  wider slot would also have to move the left edge off X=20), while a plain
  `LoadFont` size change is a no-op here — `ManagedFont.GetBestSpriteFontAssetName`
  snaps every NotoSerifJP request to the baked {14, 24, 48} set (15–18 → the
  same 14pt asset; 24 would render ≈409px). The only small local font move is
  therefore a per-label draw scale (~0.9×), to be implemented in Task 2.
- Caveat: no headless SpriteFont/MeasureString facility exists in the tests
  (all mocks); widths are PIL advances on the shipped TTF calibrated to real
  rendered ink (±5px). MonoGame's own MeasureString may differ by a few px —
  Task 2's real-font test should pin the final value.

## Legacy NX comparison (defects 1 and 4)

- `DTXManiaNX/Code/Stage/08.Result/CActResultParameterPanel.cs`:
  level drawn as a **60×60 badge** (`tx難易度パネル` region at
  `14+n本体X, 266+n本体Y`) with per-digit 16px-advance bitmap digits at
  `(18|26)+n本体X, 290+n本体Y`; playing skill drawn as left-padded text
  `string.Format("{0,6:##0.00}", dbPerformanceSkill)` (a positive alignment
  width pads on the left, i.e. the value is right-aligned within 6 chars) at
  `58+n本体X, 277+n本体Y`
  (+ separate `%` bitmap at 217,287); game skill same format at (88,363).
  CX replaced badge+padded-bitmap with plain left-aligned text at nearby X —
  the badge/padding semantics were not retained.
- `DTXManiaNX/Code/Stage/05.SongSelection/CActSelectSongList.cs`
  `tGeneratePreviewImageTexture`: on `!File.Exists(previewPath)` it substitutes
  `Graphics\5_preimage default.png`, aspect-fits into 44×44 and centers.
  CX's `SongBarRenderer.GeneratePreviewImageTexture` returns `null` for
  missing/oversized/unresolved previews (fast-scroll and cache rules verified)
  and `SongListDisplay` draws nothing → white slot from score-bar skin shows.
  Defect 4 confirmed as a dropped NX fallback.

## Notes for implementation tasks

- No code, tests, or assets were changed in Task 0.
- The game was driven Title→SongSelect→Performance(auto)→Result over the real
  JSON-RPC API; the previously-aborted run's orphaned game process (holding
  port 8731 since Oct 31) was found and terminated before capture.
- Result capture shows the NotStarted save variant (autoplay never persists,
  HPA-18). A "Saved"/failed-save variant capture belongs to Task 1's exit gate.
