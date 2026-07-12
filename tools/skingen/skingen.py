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

from PIL import Image

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
KNOWN_EXTS = IMAGE_EXTS + (".mp4",)
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


def read_dims(file_path):
    if not file_path.lower().endswith(IMAGE_EXTS):
        return None, None
    try:
        with Image.open(file_path) as img:
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

    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, sort_keys=True)
        f.write("\n")

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
        if entry.get("width") is not None:
            width, height = read_dims(target)
            if width is not None and (width, height) != (entry["width"], entry["height"]):
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
                with Image.open(os.path.join(source_root, cell["source"])) as glyph:
                    glyph = glyph.convert("RGBA").resize((cell["w"], cell["h"]), Image.LANCZOS)
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
            "### `%s`\n\n- **Target size:** %s (generate at 2x or larger)\n- **Prompt:**\n\n> %s\n"
            % (rel, dims, prompt))

    with open(out_path, "w", encoding="utf-8") as f:
        f.write("# CX Neon — Image Generation Prompts\n\n")
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
