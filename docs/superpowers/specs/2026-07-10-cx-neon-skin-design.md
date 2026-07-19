# CX Neon Skin — Alternative Skin/Style/Theming System

**Date:** 2026-07-10
**Status:** Approved design, pending implementation plan

## Problem

DTXManiaCX's default UI assets (`System/Graphics/`, 247 tracked files) are derived from
Konami's GITADORA (via DTXManiaNX). Publishing the game — or even keeping the repo public —
carries copyright risk. The game needs an original default skin. The existing NX look should
remain usable as a user-supplied alternative, not a shipped one.

## Goals

1. Ship an original, complete skin (**CX Neon**) as the default in release builds.
2. Keep the NX assets in the repo for development, but exclude them from release artifacts.
3. Let skins make *small* layout/color/font-metric adjustments without forking layout code.
4. Preserve the existing skin system (fallback chain, `System/<name>/` discovery,
   box.def skins) unchanged, so NX-format skins users already own keep working.
5. Release builds must never render missing-asset white boxes: the pack is complete and is
   itself the final fallback.

## Non-Goals

- A general-purpose layout engine or per-skin layout classes. Overrides are a whitelist
  that grows only on concrete need.
- Changes to box.def/song-level skin behavior.
- Guitar-mode assets beyond what `TexturePath` already requires.

## Decisions Made

| Decision | Choice |
|---|---|
| NX assets | Keep in repo for dev; exclude from release builds |
| Layout scope | NX skeleton + light per-skin theming layer (whitelisted overrides) |
| Art production | AI-generated image pack, assembled/validated by a script pipeline |
| Visual direction | Neon dark / synthwave |
| Delivery scope | Complete pack, all 7 stages, before release |
| Architecture | Per-skin `Theme.ini` with whitelisted overrides (Approach A) |
| SFX | Original sound pack generated with ElevenLabs, shipped with CX Neon |

## Architecture

### SkinTheme

New type `SkinTheme` (namespace `DTXMania.Game.Lib.Resources`), parsed from an optional
`Theme.ini` at the skin root, using the same INI conventions as `ConfigManager`.

- `SkinManager` loads `Theme.ini` from the **effective skin root** — whatever directory
  the fallback chain currently treats as the skin root, including the base `System/`
  root itself (this is how the release build, where CX Neon *is* `System/`, picks up its
  theme). Absent file → empty theme where every getter returns the caller's fallback.
- Exposed as `IResourceManager.CurrentTheme` (`ISkinTheme`). Every stage and performance
  component already holds an `IResourceManager`, so no new plumbing through `IStageGame`.
- Skin switching already tears down and reloads resources; the theme reloads at the same
  point. No hot-reload beyond that.

```csharp
public interface ISkinTheme
{
    Color  GetColor(string key, Color fallback);
    int    GetInt(string key, int fallback);
    float  GetFloat(string key, float fallback);
    Point  GetPoint(string key, Point fallback);   // "x,y"
    string GetString(string key, string fallback);
}
```

> **Amendment (2026-07-19):** `GetString` was added to the interface after the
> original four-method surface proved insufficient for the CX Neon font
> theming layer. Draw sites across Startup, SongSelect, SongTransition,
> Performance, and Result stages read font-family overrides (e.g.
> `Startup.TextFontFamily`, `SongSelect.TabFontFamily`,
> `Transition.LevelFontFamily`, `Result.FontValueFamily`) and one boolean
> toggle (`SongSelect.HideEmptyHistoryPanel`) from `[Fonts]` / `[Layout]` via
> `GetString`, falling back to the NX default family or `string.Empty` (which
> the font resolver treats as "use the built-in default"). The getter follows
> the same contract as the other four: absent or malformed keys return the
> caller's fallback so skins without a `Theme.ini` behave identically to NX.

### Theme.ini format

Three sections, all keys optional:

```ini
[Palette]
; semantic colors for code-drawn elements
Lane.HH=#22D3EE
Judgement.Just=#E879F9
Gauge.Fill=#22D3EE
UI.TextPrimary=#F1F5F9

[Layout]
; whitelisted overrides; defaults remain the NX constants in layout classes
SongSelect.StatusPanel.Position=580,130
Result.RankBadge.Scale=1.15

[Fonts]
; CX Neon ships bitmap number fonts at NX cell metrics (12x20 BPM, 20x28 level,
; 12x20 skill). Crispness at scaled draw sizes comes from 2x authoring — the
; BPM sheet is 264x40 (2x of NX 132x20) — handled by the existing
; sourceFactor = texture.Height / NxCellHeight mechanism in SongStatusPanel.
; A [Fonts] CellSize override key is therefore not required; the section is
; reserved for a future skin that authors at non-NX cell metrics.
```

### Integration rule (keeps blast radius small)

Layout classes (`SongSelectionUILayout`, `PerformanceUILayout`, …) and
`DTXManiaVisualTheme` keep their current constants as **defaults**. A draw site becomes
theme-aware only when the CX Neon skin concretely needs it:

```csharp
var color = theme.GetColor("Judgement.Just", DTXManiaVisualTheme.JustColor);
```

With no `Theme.ini`, every call returns its fallback, so the NX skin's behavior is
byte-identical and existing tests are unaffected.

### Error handling

- Malformed `Theme.ini` never crashes: log a warning, continue with defaults.
- Malformed individual values (bad hex, bad point) → that key falls back, warning logged.
- Unknown keys are ignored (forward compatibility as the whitelist grows).
- Missing textures in any skin → existing fallback chain, unchanged.

## CX Neon Asset Pack

**Location:** `System/CXNeon/` (Graphics + Theme.ini), tracked in git like the NX graphics.

### Art direction (style guide for every AI prompt)

- Base `#0F172A` (dark slate); panel surface `#1E293B` with 1px neon edge strokes
- Primary accent cyan `#22D3EE`; secondary magenta `#E879F9`; success green `#22C55E`;
  danger red `#EF4444`
- Motifs: thin grid/scanlines, chamfered panel corners, soft outer glow on highlighted
  elements
- Display type: geometric/Orbitron-like letterforms for headings and bitmap fonts

### Production tiers

| Tier | Assets | Method |
|---|---|---|
| 1. Backgrounds | 7 stage backgrounds, rank result backgrounds | Direct AI generation at 1280×720 |
| 2. Panels & bars | headers, footers, status/menu panels, song bars, item boxes | AI generation oversized → script crops/resizes to exact dimensions |
| 3. Metric sprite sheets | bitmap number fonts, judge strings, rank icons, difficulty sprites, chips, pads | AI generates individual glyphs/elements; compositing script assembles sheets at declared cell metrics (`[Fonts]` may differ from NX) |
| 4. Effects | hit sparks, lane flushes, per-lane fire/star variants, explosion | One AI master per effect; script derives per-lane variants by hue-shift |

### Pipeline tool: `tools/skingen/` (Python + Pillow)

- A manifest maps every `TexturePath` entry → recipe (source image(s), transform, target
  size, sheet layout).
- One command regenerates `System/CXNeon/Graphics/` deterministically from checked-in
  source art.
- **Validation:** asserts every path in `TexturePath.GetAllTexturePaths()` exists in the
  pack with expected dimensions. Runnable locally and in CI.
- `tools/skingen/STYLE.md` holds the prompt/style guide so regenerated art stays
  consistent.

## CX Neon Sound Pack (ElevenLabs)

**Required inventory (8 files):** the game code references exactly eight sound paths, all
short one-shot SFX/jingles — `Decide`, `Move`, `Game start`, `Now loading`, `Stage Clear`,
`Excellent`, `Full Combo`, `New Record` (all `.ogg`). The other files present in NX's
`System/Sounds/` (announcer voices, `Title.ogg`, `1Config BGM.ogg`, …) are never played by
CX code and are not part of the pack.

**Code change — `SoundPath` constants class:** the eight paths are currently hardcoded
strings scattered across stages. Add `SoundPath` (mirroring `TexturePath`) with a
`GetAllSoundPaths()` helper, and replace the string literals. This centralizes the
inventory and gives the completeness test its source of truth.

**Generation pipeline:** `tools/sfxgen/` (sibling of `skingen`, shares its manifest
conventions):

- Each sound has a recipe: ElevenLabs sound-effects API prompt + duration + post-processing
  (loudness normalization, trim, fade-out).
- ElevenLabs outputs MP3; the pipeline converts to OGG/Vorbis via ffmpeg (`AudioLoader`
  plays OGG natively through NVorbis).
- Requires `ELEVENLABS_API_KEY` locally; **generated OGGs are committed** under
  `System/CXNeon/Sounds/`, so builds, CI, and regeneration of other assets never need the
  API. Prompts live in the manifest so the sound identity is reproducible/tweakable.
- Style guide: SFX follow the synthwave direction — synthetic, clean transients, subtle
  neon "zap/shimmer" character; jingles (`Stage Clear`, `Excellent`, `Full Combo`,
  `New Record`) short and melodic.

**Gitignore fix (also needed for graphics):** `.gitignore` currently ignores `System/*`
except `System/Graphics/`, so `System/CXNeon/` would be silently untracked. Add
`!System/CXNeon/` so the full pack (Graphics, Sounds, Theme.ini) is committed.

## Packaging & Distribution

The default-skin swap happens in packaging, not code:

- **Dev (repo as-is):** `System/Graphics/` stays NX and remains the default fallback
  root — current behavior is untouched. CX Neon is previewed via `Config.ini`
  (`SkinPath=System/CXNeon/`).
- **Release:** the artifacts job copies `System/CXNeon/*` (Graphics + Sounds + Theme.ini)
  to `<release>/System/` as the base system skin and never packages the NX graphics or
  the NX sounds. The
  fallback chain is untouched; in release the final fallback *is* CX Neon, so a missing
  file in a user's custom skin falls back to neon art, never a white box.
- Users who own NX-format skins drop them into `System/<name>/` and select them — the
  "NX as alternative" path.

### CI changes (`.github/workflows/build-and-test.yml`)

- Artifacts job (manual dispatch): bundle CX Neon as `System/`, exclude NX graphics.
- New pack-completeness test runs on both Windows and macOS jobs (pure file I/O,
  Mac-safe): every `TexturePath.GetAllTexturePaths()` and `SoundPath.GetAllSoundPaths()`
  entry **exists** under `System/CXNeon/`. Dimension/cell-metric correctness is the
  skingen validator's job (single source of truth: the manifest), run alongside the test
  suite in CI.

## Testing

- `SkinThemeTests` (unit): hex color / point / int / float parsing; missing file →
  fallbacks; malformed values → fallback + warning; unknown keys ignored.
- Theme-aware draw sites: unit tests that an overridden key actually moves/recolors the
  element, following existing stage-logic test patterns.
- `SoundPath` refactor: existing stage tests keep passing (paths are unchanged strings,
  only centralized into constants).
- Pack-completeness test as described under CI.
- E2E smoke: unaffected (uses generated fixtures). During art development, stages are
  verified visually via the dtxmania MCP screenshot flow; SFX are auditioned in-game.

## Implementation Phases (for the plan)

1. **Theming layer:** `ISkinTheme`/`SkinTheme` + parsing + `IResourceManager.CurrentTheme`
   + SkinManager loading + tests. Includes the `SoundPath` constants refactor and the
   `.gitignore` fix for `System/CXNeon/`.
2. **Pipeline scaffolding:** `tools/skingen/` (images) and `tools/sfxgen/` (ElevenLabs
   audio) — manifests, compositor, validator, STYLE.md; pack-completeness test wired
   into CI.
3. **Prompt manifest & delegation docs (assets deferred):** asset generation will not
   happen immediately — the deliverable is a complete, ready-to-delegate manifest: one
   entry per asset with the exact image-gen prompt (style-guide baked in), target
   size/sheet metrics, and post-processing recipe; likewise ElevenLabs prompts per sound.
   A HOWTO doc explains the workflow (generate → drop into source dir → run skingen/sfxgen
   → validate). The pack is produced later, tier-by-tier (backgrounds → panels → sprite
   sheets → effects → SFX), verified on-screen/in-game; theme overrides are added to draw
   sites as concrete needs appear then.
4. **Packaging:** artifacts-job changes (Graphics + Sounds + Theme.ini); release smoke
   check that no NX file ships.

## Open Follow-Ups (not part of this work)

- Whether the public repo should eventually purge NX graphics from history (the user
  accepted keeping them tracked for now).
