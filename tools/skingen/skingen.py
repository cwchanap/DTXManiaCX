#!/usr/bin/env python3
"""skingen - CX Neon skin image pipeline.

Commands:
  bootstrap   Build/refresh manifest.json from TexturePath.cs constants,
              reading reference dimensions from the NX pack (System/Graphics).
              Authored fields (recipe, optional overrides) are preserved.
  validate    Check a pack directory against manifest.json: every non-optional
              asset exists, and matches declared dimensions when set.
  compose     (Task 6) Build the pack from source art per manifest recipes.
  prompts     (Task 7) Render PROMPTS.md for delegated AI image generation.

Python 3.9+. Third-party dependency: Pillow.
"""
import argparse
import json
import os
import re
import sys
import tempfile

from PIL import Image, ImageDraw, ImageFilter, ImageFont

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TEXTUREPATH_CS = os.path.join(REPO_ROOT, "DTXMania.Game", "Lib", "Resources", "TexturePath.cs")
NX_PACK_ROOT = os.path.join(REPO_ROOT, "System")
DEFAULT_TARGET_PACK = os.path.join(REPO_ROOT, "System", "CXNeon")
MANIFEST_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "manifest.json")
STYLE_MD_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "STYLE.md")
DESCRIPTORS_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "descriptors.json")
PROMPTS_MD_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "PROMPTS.md")

LANE_CODES = ["LC", "HH", "LP", "SD", "HT", "BD", "LT", "FT", "CY", "RD"]
IMAGE_EXTS = (".png", ".jpg", ".jpeg")
KNOWN_EXTS = (*IMAGE_EXTS, ".mp4")
# Assets that exist in TexturePath but are not required in a shipping pack.
ALWAYS_OPTIONAL = {
    "Graphics/7_background.mp4",  # background video: renderer falls back to 7_background.jpg
    # Rank-specific result backgrounds are documented optional skin assets
    # (TexturePath.GetOptionalResultTexturePaths).
    "Graphics/8_background rankSS.png",
    "Graphics/8_background rankS.png",
    "Graphics/8_background rankA.png",
    "Graphics/8_background rankB.png",
    "Graphics/8_background rankC.png",
    "Graphics/8_background rankD.png",
    "Graphics/8_background rankE.png",
}


def scan_texture_paths():
    """All Graphics/* literals in TexturePath.cs, with lane templates expanded."""
    with open(TEXTUREPATH_CS, encoding="utf-8") as f:
        source = f.read()

    raw = set(re.findall(r'"(Graphics/[^"]+)"', source))
    paths = set()
    for path in raw:
        if "{" in path:
            # f-string lane template, e.g. Graphics/ScreenPlayDrums chip fire_{...}.png
            prefix = path.split("{", 1)[0]
            suffix = path.rsplit("}", 1)[1]
            for code in LANE_CODES:
                paths.add(prefix + code + suffix)
        elif path.endswith(KNOWN_EXTS):
            paths.add(path)
        # else: bare prefixes like "Graphics/ScreenPlayDrums lane flush " are
        # runtime-composed and validated through the manifest only if authored.
    return sorted(paths)


def load_manifest(manifest_path):
    if not os.path.exists(manifest_path):
        return {"assets": {}}
    with open(manifest_path, encoding="utf-8") as f:
        return json.load(f)


def _atomic_write_json(path, data):
    """Write JSON atomically: serialize to a temp file in the same directory,
    then os.replace so an interrupted write never leaves a truncated file."""
    dir_path = os.path.dirname(os.path.abspath(path))
    fd, tmp = tempfile.mkstemp(dir=dir_path, suffix=".tmp")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2, sort_keys=True)
            f.write("\n")
        os.replace(tmp, path)
    except BaseException:
        if os.path.exists(tmp):
            os.remove(tmp)
        raise


def read_dims(file_path):
    if not file_path.lower().endswith(IMAGE_EXTS):
        return None, None
    try:
        with Image.open(file_path) as img:
            # Force a full pixel decode. Image.open is lazy: it reads only the
            # header, so img.size can be correct even when the pixel payload is
            # truncated. Texture2D.FromStream decodes the whole image at load
            # time and would then fail at runtime, falling back to a white box.
            # img.load() raises on truncated/corrupt payloads.
            img.load()
            return img.size
    except Exception:
        return None, None


def bootstrap(manifest_path):
    manifest = load_manifest(manifest_path)
    assets = manifest.setdefault("assets", {})
    added, missing_reference = 0, []

    for rel in scan_texture_paths():
        entry = assets.setdefault(rel, {"width": None, "height": None, "optional": False, "recipe": None})
        if rel in ALWAYS_OPTIONAL:
            entry["optional"] = True
        reference = os.path.join(NX_PACK_ROOT, rel.replace("/", os.sep))
        if os.path.exists(reference):
            if entry.get("width") is None:
                entry["width"], entry["height"] = read_dims(reference)
        elif entry.get("width") is None and not entry.get("optional"):
            # No NX reference to measure: keep required, author must fill dims.
            entry["note"] = "missing from NX reference pack; set target dimensions manually"
            missing_reference.append(rel)
        added += 1

    # Atomic write: serialize to a temp file in the same directory, then
    # os.replace so an interrupted bootstrap never leaves a truncated manifest.
    _atomic_write_json(manifest_path, manifest)

    print("bootstrap: %d assets in manifest" % added)
    for rel in missing_reference:
        print("  ! no NX reference for %s (dims left null)" % rel)
    return 0


def validate_pack(manifest_path, pack_root):
    manifest = load_manifest(manifest_path)
    errors = []
    for rel, entry in sorted(manifest.get("assets", {}).items()):
        target = os.path.join(pack_root, rel.replace("/", os.sep))
        if not os.path.exists(target):
            if not entry.get("optional"):
                errors.append("MISSING  %s" % rel)
            continue
        if os.path.isdir(target):
            errors.append("NOTAFILE %s: expected a file, found a directory" % rel)
            continue
        # Only image assets can be decoded by Pillow; the .mp4 background is
        # validated by existence only (the renderer falls back to a still).
        if not target.lower().endswith(IMAGE_EXTS):
            continue
        # Decode every image even when the manifest has no target dimensions
        # (width/height: null). A corrupt file with a valid header used to slip
        # through because the dimension comparison was the only decode path,
        # and the C# release gate checks existence only.
        width, height = read_dims(target)
        if width is None:
            # The file exists but read_dims could not decode it (corrupt,
            # truncated, or not actually an image). Without this guard the
            # CI validator passes while the C# gate only checks file
            # existence, letting a broken asset ship and fall back at
            # runtime.
            errors.append("UNREADABLE %s: exists but could not be decoded as an image" % rel)
            continue
        if entry.get("width") is not None and (width, height) != (entry["width"], entry["height"]):
            errors.append("DIMS     %s: expected %dx%d, found %dx%d"
                          % (rel, entry["width"], entry["height"], width, height))
    return errors


def _save(img, target, width, height):
    if width is not None and img.size != (width, height):
        img = img.resize((width, height), Image.LANCZOS)
    os.makedirs(os.path.dirname(target), exist_ok=True)
    if target.lower().endswith((".jpg", ".jpeg")):
        img.convert("RGB").save(target, quality=90)
    else:
        img.save(target)


# Procedural cell rendering (STYLE.md tokens). Text can never come from the AI
# generator (every prompt says "no text"), so text-bearing sheet cells — menu
# labels, judge strings, digits — are rendered here with the bundled Orbitron.
LABEL_FONT_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fonts", "Orbitron.ttf")
TEXT_FILL = (0xF1, 0xF5, 0xF9)       # UI.TextPrimary
ACCENT_CYAN = "#22D3EE"              # UI.Accent
_CELL_SUPERSAMPLE = 4


def _hex_rgb(value):
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))


def _load_label_font(pixel_size):
    font = ImageFont.truetype(LABEL_FONT_PATH, pixel_size)
    try:
        font.set_variation_by_name("Bold")
    except OSError:
        pass  # static font or FreeType without variation support
    return font


def _render_text_cell(w, h, text, accent_hex):
    """Neon label: near-white glyph core over a soft accent-colored halo,
    centered in a transparent cell. Rendered supersampled, then downsized."""
    s = _CELL_SUPERSAMPLE
    big_w, big_h = w * s, h * s
    img = Image.new("RGBA", (big_w, big_h), (0, 0, 0, 0))
    measure = ImageDraw.Draw(img)

    pad = max(2 * s, big_w // 24)
    size = max(5, int(big_h * 0.60))
    font = _load_label_font(size)
    while size > 5:
        left, top, right, bottom = measure.textbbox((0, 0), text, font=font)
        if right - left <= big_w - 2 * pad and bottom - top <= int(big_h * 0.9):
            break
        size = int(size * 0.9)
        font = _load_label_font(size)

    left, top, right, bottom = measure.textbbox((0, 0), text, font=font)
    tx = (big_w - (right - left)) // 2 - left
    ty = (big_h - (bottom - top)) // 2 - top

    glow = Image.new("RGBA", (big_w, big_h), (0, 0, 0, 0))
    ImageDraw.Draw(glow).text((tx, ty), text, font=font, fill=_hex_rgb(accent_hex) + (255,))
    glow = glow.filter(ImageFilter.GaussianBlur(radius=2 * s))
    img.alpha_composite(glow)
    img.alpha_composite(glow)  # second pass strengthens the halo
    ImageDraw.Draw(img).text((tx, ty), text, font=font, fill=TEXT_FILL + (255,))
    return img.resize((w, h), Image.LANCZOS)


def _render_cursor_cell(w, h, accent_hex):
    """Selection bar: glowing accent frame around a translucent tint. The
    cursor row is drawn over the label row in-game, so the fill must stay
    see-through for the label to remain readable."""
    s = _CELL_SUPERSAMPLE
    big_w, big_h = w * s, h * s
    accent = _hex_rgb(accent_hex)
    inset = 2 * s
    radius = 5 * s
    box = [inset, inset, big_w - 1 - inset, big_h - 1 - inset]

    frame = Image.new("RGBA", (big_w, big_h), (0, 0, 0, 0))
    ImageDraw.Draw(frame).rounded_rectangle(box, radius=radius, outline=accent + (255,), width=2 * s)
    img = frame.filter(ImageFilter.GaussianBlur(radius=2 * s))  # halo
    ImageDraw.Draw(img).rounded_rectangle(box, radius=radius, fill=accent + (56,))
    img.alpha_composite(frame)  # crisp frame back on top of fill + halo
    return img.resize((w, h), Image.LANCZOS)


def _build_sheet_cell(cell, source_root):
    if "source" in cell:
        with Image.open(os.path.join(source_root, cell["source"])) as glyph:
            return glyph.convert("RGBA").resize((cell["w"], cell["h"]), Image.LANCZOS)
    if "text" in cell:
        return _render_text_cell(cell["w"], cell["h"], cell["text"], cell.get("color", ACCENT_CYAN))
    if cell.get("cursor"):
        return _render_cursor_cell(cell["w"], cell["h"], cell.get("color", ACCENT_CYAN))
    raise ValueError(f"sheet cell needs 'source', 'text' or 'cursor': {cell}")


def _hueshift(img, degrees):
    offset = int(degrees / 360.0 * 255) % 256
    rgba = img.convert("RGBA")
    alpha = rgba.getchannel("A")
    h, s, v = rgba.convert("RGB").convert("HSV").split()
    h = h.point(lambda value: (value + offset) % 256)
    shifted = Image.merge("HSV", (h, s, v)).convert("RGBA")
    shifted.putalpha(alpha)
    return shifted


def compose(manifest_path, source_root, pack_root, only=None):
    """Builds recipe-bearing assets; returns the list of recipe-less (skipped) ones."""
    manifest = load_manifest(manifest_path)
    assets = manifest.get("assets", {})
    skipped = []

    def matches(rel):
        return only is None or only in rel

    # Pass 1: everything except hueshift (hueshift reads composed outputs).
    for rel, entry in sorted(assets.items()):
        if not matches(rel):
            continue
        recipe = entry.get("recipe")
        if recipe is None:
            if not entry.get("optional"):
                skipped.append(rel)
            continue
        target = os.path.join(pack_root, rel.replace("/", os.sep))
        if recipe["type"] == "copy":
            with Image.open(os.path.join(source_root, recipe["source"])) as img:
                _save(img.convert("RGBA"), target, entry.get("width"), entry.get("height"))
        elif recipe["type"] == "sheet":
            canvas = Image.new("RGBA", (entry["width"], entry["height"]), (0, 0, 0, 0))
            for cell in recipe["cells"]:
                glyph = _build_sheet_cell(cell, source_root)
                canvas.paste(glyph, (cell["x"], cell["y"]), glyph)
            _save(canvas, target, entry.get("width"), entry.get("height"))

    # Pass 2: hueshift derivations.
    for rel, entry in sorted(assets.items()):
        if not matches(rel):
            continue
        recipe = entry.get("recipe")
        if recipe is None or recipe["type"] != "hueshift":
            continue
        base_path = os.path.join(pack_root, recipe["base"].replace("/", os.sep))
        target = os.path.join(pack_root, rel.replace("/", os.sep))
        with Image.open(base_path) as base:
            _save(_hueshift(base, recipe["degrees"]), target, entry.get("width"), entry.get("height"))

    return skipped


def read_base_style(style_md_path):
    """Extracts the blockquote under '## Base style prompt' from STYLE.md."""
    lines = []
    in_section = False
    with open(style_md_path, encoding="utf-8") as f:
        for line in f:
            if line.startswith("## "):
                in_section = line.strip().lower().startswith("## base style prompt")
                continue
            if in_section and line.startswith(">"):
                lines.append(line.lstrip("> ").strip())
    return " ".join(lines)


def render_prompts(manifest_path, descriptors_path, base_style, out_path):
    manifest = load_manifest(manifest_path)
    with open(descriptors_path, encoding="utf-8") as f:
        descriptors = json.load(f)
    families = descriptors.get("families", {})
    asset_descs = descriptors.get("assets", {})

    missing = []
    sections = []
    for rel, entry in sorted(manifest.get("assets", {}).items()):
        recipe = entry.get("recipe") or {}
        if recipe.get("type") == "hueshift":
            continue  # derived, no prompt needed
        desc = asset_descs.get(rel)
        if desc is None:
            if not entry.get("optional"):
                missing.append(rel)
            continue
        family_prompt = families.get(desc.get("family", ""), "")
        dims = ("%dx%d" % (entry["width"], entry["height"])) if entry.get("width") else "TBD by author"
        prompt = " ".join(part for part in [base_style, family_prompt, desc.get("desc", "")] if part)
        sections.append(
            "## `%s`\n\n- **Target size:** %s (generate at 2x or larger)\n- **Prompt:**\n\n> %s\n"
            % (rel, dims, prompt))

    with open(out_path, "w", encoding="utf-8") as f:
        f.write("# CX Neon — Image Generation Prompts\n\n")
        f.write("<!-- GENERATED FILE — do not edit by hand. Regenerate with "
                "`python tools/skingen/skingen.py prompts`.\n")
        f.write("     Source of truth: tools/skingen/manifest.json + "
                "tools/skingen/descriptors.json + tools/skingen/STYLE.md.\n")
        f.write("     If those change, rerun the command and commit the result; "
                "otherwise this file drifts. -->\n\n")
        f.write("Generated by `skingen.py prompts` from manifest.json + descriptors.json.\n")
        f.write("Workflow: generate each image with your image AI, save into "
                "`tools/skingen/source/` under the recipe's source path, then run "
                "`skingen.py compose` and `skingen.py validate`.\n\n")
        f.write("\n".join(sections))
        f.write("\n")
    return missing


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--manifest", default=MANIFEST_PATH)
    sub = parser.add_subparsers(dest="command", required=True)

    sub.add_parser("bootstrap")

    validate = sub.add_parser("validate")
    validate.add_argument("--pack", default=DEFAULT_TARGET_PACK,
                          help="pack root containing Graphics/ (default: System/CXNeon)")

    compose_cmd = sub.add_parser("compose")
    compose_cmd.add_argument("--source", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "source"))
    compose_cmd.add_argument("--pack", default=DEFAULT_TARGET_PACK)
    compose_cmd.add_argument("--only", default=None, help="substring filter on asset paths")

    prompts_cmd = sub.add_parser("prompts")
    prompts_cmd.add_argument("--descriptors", default=DESCRIPTORS_PATH)
    prompts_cmd.add_argument("--style", default=STYLE_MD_PATH)
    prompts_cmd.add_argument("--out", default=PROMPTS_MD_PATH)

    args = parser.parse_args(argv)

    if args.command == "bootstrap":
        return bootstrap(args.manifest)

    if args.command == "validate":
        errors = validate_pack(args.manifest, args.pack)
        for error in errors:
            print(error)
        print("validate: %d problem(s) in %s" % (len(errors), args.pack))
        return 1 if errors else 0

    if args.command == "compose":
        skipped = compose(args.manifest, args.source, args.pack, args.only)
        for rel in skipped:
            print("no recipe yet: %s" % rel)
        print("compose: done (%d assets still without recipes)" % len(skipped))
        return 0

    if args.command == "prompts":
        base_style = read_base_style(args.style)
        missing = render_prompts(args.manifest, args.descriptors, base_style, args.out)
        for rel in missing:
            print("no descriptor: %s" % rel)
        print("prompts: wrote %s (%d assets without descriptors)" % (args.out, len(missing)))
        return 1 if missing else 0

    return 2


if __name__ == "__main__":
    sys.exit(main())
