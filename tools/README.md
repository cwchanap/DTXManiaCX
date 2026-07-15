# CX Neon asset pipelines — delegation HOWTO

The CX Neon pack (`System/CXNeon/`) is produced by two pipelines. Generation
is intentionally decoupled: you can produce assets whenever you like (in any
order, in batches) and the validators tell you exactly what is still missing.

## Images (`tools/skingen/`)

1. Open `tools/skingen/PROMPTS.md`. Each entry has the target file, the target
   size, and a complete prompt for your image-generation AI.
2. Generate each image (at 2x target size or larger) and save it under
   `tools/skingen/source/<path referenced by the recipe in manifest.json>`.
   For an asset whose manifest `recipe` is still `null`, add a recipe first —
   usually `{"type": "copy", "source": "<where you saved it>"}` for whole
   images, or a `sheet` recipe for glyph/sprite sheets (see existing entries).
3. Build and check the pack:

       python3 tools/skingen/skingen.py compose
       python3 tools/skingen/skingen.py validate

   `validate` checks that every asset in `manifest.json` exists on disk with
   the right dimensions — it is not the final release-readiness check. Some
   assets the game code requires are marked `optional: true` in
   `manifest.json` (genuine gaps in the NX reference pack), so `validate` can
   exit 0 while the pack is still incomplete. The authoritative release gate
   is the C# test `CxNeonPackTests`, run via `dotnet test`, which requires
   every entry in `TexturePath.GetAllTexturePaths()` (except the background
   video) — including the manifest-optional-but-code-referenced assets listed
   in `PROMPTS.md`.
4. Commit `tools/skingen/source/`, `manifest.json`, and the generated
   `System/CXNeon/Graphics/` files together.

Regenerating `PROMPTS.md` after editing `STYLE.md`/`descriptors.json`:

       python3 tools/skingen/skingen.py prompts

Caveat: `skingen.py compose --only <pattern>` against a fresh/empty pack
fails with `FileNotFoundError` if the pattern matches a hueshift-derived
asset but not its base recipe (there's no dependency-aware filtering). Run a
full `compose` first, then use `--only` for incremental rebuilds.

## Sounds (`tools/sfxgen/`)

1. Set your API key: `export ELEVENLABS_API_KEY=...` (ffmpeg must be on PATH).
2. Generate everything, or one file at a time while auditioning:

       python3 tools/sfxgen/sfxgen.py generate
       python3 tools/sfxgen/sfxgen.py generate --only "Decide.ogg"

   Prompts live in `tools/sfxgen/manifest.json` — tweak and re-run per file
   until it sounds right in-game.
3. Check completeness: `python3 tools/sfxgen/sfxgen.py validate`
4. Commit `System/CXNeon/Sounds/*.ogg` (raw MP3s in `tools/sfxgen/raw/` are
   gitignored intermediates).

## In-game preview

Point the dev build at the pack in `Config.ini`. `ConfigManager` resolves a
relative `SkinPath` under the app-data root (`AppPaths.GetAppDataRoot()`, e.g.
`~/Library/Application Support/DTXManiaCX` on macOS or
`%LOCALAPPDATA%\DTXManiaCX` on Windows) — **not** the repository checkout — so a
relative value like `System/CXNeon/` would look in app data and miss the
checked-out pack. Use an absolute path to the checkout instead:

    SkinPath=/Users/you/repos/DTXmaniaCX/System/CXNeon/

(or copy `System/CXNeon/` into your app-data `System/` directory and keep the
relative `SkinPath=System/CXNeon/`).

Stages can be screenshotted through the dtxmania MCP flow for visual review.
