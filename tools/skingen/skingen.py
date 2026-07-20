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
    if width is not None and height is not None and img.size != (width, height):
        iw, ih = img.size
        # Reject aspect-ratio changes — they usually mean a wrong source atlas
        # rather than an intentional scale (e.g. 268x8720 vs 2688x720).
        if iw > 0 and ih > 0 and width > 0 and height > 0 and iw * height != ih * width:
            raise ValueError(
                "aspect-ratio-changing resize refused for %s: source %dx%d -> target %dx%d"
                % (target, iw, ih, width, height))
        img = img.resize((width, height), Image.LANCZOS)
    elif width is not None and height is None and img.size[0] != width:
        img = img.resize((width, img.size[1]), Image.LANCZOS)
    elif height is not None and width is None and img.size[1] != height:
        img = img.resize((img.size[0], height), Image.LANCZOS)
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


def _render_text_cell(w, h, text, accent_hex, size_fraction=0.6, anchor="center", fit=None,
                      ink=False):
    """Neon label: near-white glyph core over a soft accent-colored halo in a
    transparent cell. size_fraction is the target glyph height as a fraction of
    the cell height (digits that must fill their cell use ~0.85); anchor places
    the glyph vertically ("center", "top" to leave the lower cell free, or
    "bottom" for baseline glyphs like '.'). fit gives reference characters the
    size is computed against, so every cell of a digit strip renders at ONE
    size — without it, wide digits shrink to the cell width while a narrow '1'
    keeps the full size fraction and looks oversized.
    Rendered supersampled, then downsized."""
    s = _CELL_SUPERSAMPLE
    big_w, big_h = w * s, h * s
    img = Image.new("RGBA", (big_w, big_h), (0, 0, 0, 0))
    measure = ImageDraw.Draw(img)

    pad = max(2 * s, big_w // 24)

    def fits(f):
        # With fit: every reference glyph must fit the cell individually.
        # Without: the actual text (possibly multi-glyph) must fit as one line.
        for probe in (fit or [text]):
            left, top, right, bottom = measure.textbbox((0, 0), probe, font=f)
            if right - left > big_w - 2 * pad or bottom - top > int(big_h * 0.92):
                return False
        return True

    size = max(5, int(big_h * size_fraction))
    font = _load_label_font(size)
    while size > 5 and not fits(font):
        size = int(size * 0.9)
        font = _load_label_font(size)

    left, top, right, bottom = measure.textbbox((0, 0), text, font=font)
    tx = (big_w - (right - left)) // 2 - left
    if anchor == "top":
        ty = pad - top
    elif anchor == "bottom":
        ty = big_h - pad - bottom
    else:
        ty = (big_h - (bottom - top)) // 2 - top

    glow = Image.new("RGBA", (big_w, big_h), (0, 0, 0, 0))
    ImageDraw.Draw(glow).text((tx, ty), text, font=font, fill=_hex_rgb(accent_hex) + (255,))
    glow = glow.filter(ImageFilter.GaussianBlur(radius=2 * s))
    img.alpha_composite(glow)
    img.alpha_composite(glow)  # second pass strengthens the halo
    # ink=True tints the glyph core toward the accent (for color-coded digit
    # strips); the default near-white core reads as plain white at small sizes.
    if ink:
        accent = _hex_rgb(accent_hex)
        core = tuple((a * 3 + t) // 4 for a, t in zip(accent, TEXT_FILL))
    else:
        core = TEXT_FILL
    ImageDraw.Draw(img).text((tx, ty), text, font=font, fill=core + (255,))
    return img.resize((w, h), Image.LANCZOS)


PANEL_FILL = (0x0B, 0x12, 0x20)      # dark slate panel body


def _render_panel_cell(w, h, accent_hex, fill_alpha=216, border=True):
    """Solid panel body: near-opaque dark fill, optionally framed by a glowing
    accent border. Unlike cursor cells (translucent tint drawn OVER labels),
    panels sit UNDER white text — bar bodies, grid cells — so the fill must be
    dark and near-opaque. border=False yields a plain dark number well."""
    s = _CELL_SUPERSAMPLE
    big_w, big_h = w * s, h * s
    accent = _hex_rgb(accent_hex)
    inset = 2 * s
    radius = 5 * s
    box = [inset, inset, big_w - 1 - inset, big_h - 1 - inset]

    img = Image.new("RGBA", (big_w, big_h), (0, 0, 0, 0))
    if border:
        frame = Image.new("RGBA", (big_w, big_h), (0, 0, 0, 0))
        ImageDraw.Draw(frame).rounded_rectangle(box, radius=radius, outline=accent + (255,), width=2 * s)
        img = frame.filter(ImageFilter.GaussianBlur(radius=2 * s))  # halo
    ImageDraw.Draw(img).rounded_rectangle(box, radius=radius, fill=PANEL_FILL + (fill_alpha,))
    if border:
        frame = Image.new("RGBA", (big_w, big_h), (0, 0, 0, 0))
        ImageDraw.Draw(frame).rounded_rectangle(box, radius=radius, outline=accent + (255,), width=2 * s)
        img.alpha_composite(frame)  # crisp frame back on top of the fill
    return img.resize((w, h), Image.LANCZOS)


def _render_cursor_cell(w, h, accent_hex, inset_fraction=None):
    """Selection bar / cap panel: glowing accent frame around a translucent
    tint. The cursor row is drawn over the label row in-game, so the fill must
    stay see-through for the label to remain readable. inset_fraction shrinks
    the panel inside the cell (transparent margin) for cells whose in-game
    destination rects overlap their neighbours (pad caps)."""
    s = _CELL_SUPERSAMPLE
    big_w, big_h = w * s, h * s
    accent = _hex_rgb(accent_hex)
    if inset_fraction is None:
        inset = 2 * s
    else:
        inset = int(min(big_w, big_h) * inset_fraction)
    radius = 5 * s
    box = [inset, inset, big_w - 1 - inset, big_h - 1 - inset]

    frame = Image.new("RGBA", (big_w, big_h), (0, 0, 0, 0))
    ImageDraw.Draw(frame).rounded_rectangle(box, radius=radius, outline=accent + (255,), width=2 * s)
    img = frame.filter(ImageFilter.GaussianBlur(radius=2 * s))  # halo
    ImageDraw.Draw(img).rounded_rectangle(box, radius=radius, fill=accent + (56,))
    img.alpha_composite(frame)  # crisp frame back on top of fill + halo
    return img.resize((w, h), Image.LANCZOS)


def _render_glyph_cell(w, h, kind, accent_hex):
    """Neon drum icon for pad indicators (per design: icons, not lane text).
    Shapes are sized ~55% of the cell so the overlapping in-game destination
    rects keep transparent margins: "cymbal"/"ride" are hollow rings, "hihat"
    is two stacked flattened rings, "drum" is a filled head with a rim, and
    "pedal" is an upright pedal plate."""
    s = _CELL_SUPERSAMPLE
    big_w, big_h = w * s, h * s
    accent = _hex_rgb(accent_hex)
    cx, cy = big_w // 2, big_h // 2
    stroke = 2 * s

    shape = Image.new("RGBA", (big_w, big_h), (0, 0, 0, 0))
    d = ImageDraw.Draw(shape)
    if kind in ("cymbal", "ride"):
        rx = int(big_w * (0.30 if kind == "cymbal" else 0.24))
        ry = int(rx * 0.55)
        d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], outline=accent + (255,), width=stroke)
        d.ellipse([cx - stroke, cy - stroke, cx + stroke, cy + stroke], fill=accent + (255,))
    elif kind == "hihat":
        rx, ry = int(big_w * 0.26), int(big_w * 0.10)
        for dy in (-int(ry * 0.9), int(ry * 0.9)):
            d.ellipse([cx - rx, cy + dy - ry, cx + rx, cy + dy + ry],
                      outline=accent + (255,), width=stroke)
    elif kind == "drum":
        rx, ry = int(big_w * 0.26), int(big_w * 0.20)
        d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=accent + (96,))
        d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], outline=accent + (255,), width=stroke)
    elif kind == "pedal":
        pw, ph = int(big_w * 0.16), int(big_h * 0.30)
        box = [cx - pw, cy - ph, cx + pw, cy + ph]
        d.rounded_rectangle(box, radius=3 * s, fill=accent + (96,))
        d.rounded_rectangle(box, radius=3 * s, outline=accent + (255,), width=stroke)
    else:
        raise ValueError(f"unknown glyph kind: {kind}")

    img = shape.filter(ImageFilter.GaussianBlur(radius=2 * s))  # halo
    img.alpha_composite(shape)
    return img.resize((w, h), Image.LANCZOS)


def _build_sheet_cell(cell, source_root):
    if "source" in cell:
        with Image.open(os.path.join(source_root, cell["source"])) as glyph:
            return glyph.convert("RGBA").resize((cell["w"], cell["h"]), Image.LANCZOS)
    color = cell.get("color", ACCENT_CYAN)
    # "box" confines the visible content to a sub-rect of the cell (transparent
    # elsewhere) — for textures drawn offset over neighbours (selected bars) or
    # content that must stay clear of an in-game overdraw region (rank icons).
    box = cell.get("box")
    w, h = (box[2], box[3]) if box else (cell["w"], cell["h"])
    if cell.get("panel"):
        panel = _render_panel_cell(w, h, color, cell.get("fill_alpha", 216), cell.get("border", True))
    elif cell.get("cursor"):
        panel = _render_cursor_cell(w, h, color, cell.get("inset"))
    elif "glyph" in cell:
        panel = _render_glyph_cell(w, h, cell["glyph"], color)
    else:
        panel = None
    if "text" in cell:
        label = _render_text_cell(w, h, cell["text"], color,
                                  cell.get("size", 0.6), cell.get("anchor", "center"),
                                  cell.get("fit"), cell.get("ink", False))
        if panel is None:
            panel = label
        else:
            panel.alpha_composite(label)
    if panel is None:
        raise ValueError(f"sheet cell needs 'source', 'text', 'cursor' or 'panel': {cell}")
    if box:
        full = Image.new("RGBA", (cell["w"], cell["h"]), (0, 0, 0, 0))
        full.paste(panel, (box[0], box[1]), panel)
        return full
    return panel


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

    # When --only selects a hueshift-derived asset, pass 1 must still build its
    # base so pass 2 can read the composed base from pack_root.
    hueshift_bases = set()
    if only is not None:
        for rel, entry in assets.items():
            if only not in rel:
                continue
            recipe = entry.get("recipe") or {}
            if recipe.get("type") == "hueshift" and recipe.get("base"):
                hueshift_bases.add(recipe["base"])

    def matches_pass1(rel):
        return only is None or only in rel or rel in hueshift_bases

    def matches_pass2(rel):
        return only is None or only in rel

    # Pass 1: everything except hueshift (hueshift reads composed outputs).
    for rel, entry in sorted(assets.items()):
        if not matches_pass1(rel):
            continue
        recipe = entry.get("recipe")
        if recipe is None:
            if not entry.get("optional") and matches_pass2(rel):
                skipped.append(rel)
            continue
        if recipe.get("type") == "hueshift":
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

    # Pass 2: hueshift derivations (requested outputs only).
    for rel, entry in sorted(assets.items()):
        if not matches_pass2(rel):
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
