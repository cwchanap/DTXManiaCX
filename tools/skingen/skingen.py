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


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--manifest", default=MANIFEST_PATH)
    sub = parser.add_subparsers(dest="command", required=True)

    sub.add_parser("bootstrap")

    validate = sub.add_parser("validate")
    validate.add_argument("--pack", default=DEFAULT_TARGET_PACK,
                          help="pack root containing Graphics/ (default: System/CXNeon)")

    args = parser.parse_args(argv)

    if args.command == "bootstrap":
        return bootstrap(args.manifest)

    if args.command == "validate":
        errors = validate_pack(args.manifest, args.pack)
        for error in errors:
            print(error)
        print("validate: %d problem(s) in %s" % (len(errors), args.pack))
        return 1 if errors else 0

    return 2


if __name__ == "__main__":
    sys.exit(main())
