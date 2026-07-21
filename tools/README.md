# CX Neon asset pipelines — delegation HOWTO

The CX Neon pack (`System/CXNeon/`) is produced by two pipelines. Generation
is intentionally decoupled: you can produce assets whenever you like (in any
order, in batches) and the validators tell you exactly what is still missing.

## Images (`tools/skingen/`)

### Fast path (procedural baseline + optional AI polish)

A complete original pack can be produced without an external image API:

```bash
pip install "Pillow>=10,<12"               # required by generate_source.py and skingen.py
python3 tools/skingen/generate_source.py   # writes source/ + wires copy recipes
python3 tools/skingen/skingen.py compose   # builds System/CXNeon/Graphics/
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
   `System/CXNeon/Graphics/` files together.

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
4. Commit `System/CXNeon/Sounds/*.ogg` (raw MP3s in `tools/sfxgen/raw/` are
   gitignored intermediates).

## In-game preview / skin switcher

The config **Skin** dropdown discovers skins under `SystemSkinRoot` (default:
app-data `System/`). Subfolders that pass validation (`Graphics/1_background.jpg`
or `2_background.jpg`) appear as named skins (e.g. `CXNeon`); the root itself
is `Default`.

Recommended local install (symlink so pack regenerations are live):

```bash
# macOS / Linux
just install-cx-neon

# or manually:
APP_SYSTEM="$HOME/Library/Application Support/DTXManiaCX/System"   # macOS
# APP_SYSTEM="$LOCALAPPDATA/DTXManiaCX/System"                     # Windows
ln -sfn "$(pwd)/System/CXNeon" "$APP_SYSTEM/CXNeon"
```

Then either:

- Launch the game and switch **System → Skin → CXNeon** in the config menu
  (persists `SkinPath` on change), or
- Set `SkinPath` in `Config.ini` to the installed folder:

  ```ini
  SkinPath=/Users/you/Library/Application Support/DTXManiaCX/System/CXNeon/
  ```

`ConfigManager` resolves a *relative* `SkinPath` under the app-data root — not
the repository checkout — so a bare `System/CXNeon/` only works after the pack
is installed under app-data as above. An absolute path to the worktree
`System/CXNeon/` also works for a one-off preview, but that skin only stays in
the dropdown while it is the active external skin.

Stages can be screenshotted through the MCP server's `game_*` tools
(`game_click`, `game_drag`, `game_get_state`, `game_send_key`) for visual review.
