# CX Neon asset pipelines — delegation HOWTO

`System/` is the canonical CX Neon skin: `Theme.ini`, `Graphics/` and `Sounds/`
are committed there and shipped with release builds. The pack is produced by
two pipelines. Generation is intentionally decoupled: you can produce assets
whenever you like (in any order, in batches) and the validators tell you
exactly what is still missing.

## Images (`tools/skingen/`)

### Fast path (procedural baseline + optional AI polish)

A complete original pack can be produced without an external image API:

```bash
pip install "Pillow>=10,<12"               # required by generate_source.py and skingen.py
python3 tools/skingen/generate_source.py   # writes source/ + wires copy recipes
python3 tools/skingen/skingen.py compose   # builds System/Graphics/
python3 tools/skingen/skingen.py validate
```

`generate_source.py` draws neon UI chrome, bitmap fonts, chips, ranks, and
effects to STYLE.md tokens and NX sheet metrics. Stage backgrounds can be
replaced later by dropping AI art over the matching files in
`tools/skingen/source/` (e.g. `2_background.jpg`) and re-running `compose`.

### Delegated AI path

1. Open `tools/skingen/PROMPTS.md`. Each entry has the target file, the target
   size, and a complete prompt for your image-generation AI.
2. Generate each image (at 2x target size or larger) and save it under
   `tools/skingen/source/<path referenced by the recipe in manifest.json>`.
   For an asset whose manifest `recipe` is still `null`, add a recipe first —
   usually `{"type": "copy", "source": "<where you saved it>"}` for whole
   images, or a `sheet` recipe for glyph/sprite sheets (see existing entries).
3. Build and check the pack:

   ```bash
   python3 tools/skingen/skingen.py compose
   python3 tools/skingen/skingen.py validate
   ```

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
   `System/Graphics/` files together.

Regenerating `PROMPTS.md` after editing `STYLE.md`/`descriptors.json`:

```bash
python3 tools/skingen/skingen.py prompts
```

`skingen.py compose --only <pattern>` pulls in hueshift base assets automatically
when the pattern matches a derived asset, so a fresh/empty pack can build a
single derived file (e.g. `fire_HH.png`) without a prior full compose.

## Sounds (`tools/sfxgen/`)

1. Set your API key: `export ELEVENLABS_API_KEY=...` (ffmpeg must be on PATH).
2. Generate everything, or one file at a time while auditioning:

   ```bash
   python3 tools/sfxgen/sfxgen.py generate
   python3 tools/sfxgen/sfxgen.py generate --only "Decide.ogg"
   ```

   Prompts live in `tools/sfxgen/manifest.json` — tweak and re-run per file
   until it sounds right in-game.
3. Check completeness:

   ```bash
   python3 tools/sfxgen/sfxgen.py validate
   ```
4. Commit `System/Sounds/*.ogg` (raw MP3s in `tools/sfxgen/raw/` are
   gitignored intermediates).

## Previewing the pack and custom skins

`dotnet run`/`dotnet build` copy `System/` next to the executable (the macOS
`.app` keeps it at `Contents/Resources/System`), so the committed pack is the
`Default` skin with no local install step. The config **Skin** dropdown also
discovers skins under `SystemSkinRoot` (default: app-data `System/`); subfolders
that pass validation (`Graphics/1_background.jpg` or `2_background.jpg`) appear
as named skins and take precedence while selected. Rebuilding the pack updates
`Default` directly.

Developer machines that used the old nested pack or NX-era app-data art may
still have a stale `<appdata>/System/CXNeon` symlink, or NX files under
`<appdata>/System/{Graphics,Sounds,Script}`, that shadow the new default. Check
your own customizations, remove those old local items manually, then reselect
`Default`. The game never deletes app-data skins automatically.

`Config.ini` is bootstrap/import input only (read once, when `config.db` does
not exist yet); editing it after the first launch has no effect.

Stages can be screenshotted through the MCP server's `game_*` tools
(`game_click`, `game_drag`, `game_get_state`, `game_send_key`) for visual review.
