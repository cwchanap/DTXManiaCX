#!/usr/bin/env python3
"""sfxgen - CX Neon sound pipeline (ElevenLabs sound-generation API).

Commands:
  generate    Call ElevenLabs for each manifest sound (or --only NAME),
              save MP3 to raw/, then loudness-normalize + encode OGG/Vorbis
              into System/CXNeon/Sounds/. Requires ELEVENLABS_API_KEY and ffmpeg.
  validate    Check that every manifest sound exists in the output directory.

Python 3.9+, stdlib only (urllib for HTTP).
"""
import argparse
import json
import os
import subprocess
import sys
import urllib.request

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MANIFEST_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "manifest.json")
RAW_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "raw")
API_URL = "https://api.elevenlabs.io/v1/sound-generation"


def load_sounds(manifest_path):
    with open(manifest_path, encoding="utf-8") as f:
        return json.load(f)["sounds"]


def output_dir(manifest_path):
    with open(manifest_path, encoding="utf-8") as f:
        return os.path.join(REPO_ROOT, json.load(f)["output_dir"])


def postprocess_command(raw_path, ogg_path):
    return [
        "ffmpeg", "-y", "-i", raw_path,
        "-af", "loudnorm=I=-16:TP=-1.5:LRA=11",
        "-c:a", "libvorbis", "-qscale:a", "5",
        ogg_path,
    ]


def generate_one(sound, api_key, out_dir):
    raw_path = os.path.join(RAW_DIR, sound["file"].replace(".ogg", ".mp3"))
    os.makedirs(RAW_DIR, exist_ok=True)
    body = json.dumps({
        "text": sound["prompt"],
        "duration_seconds": sound["duration_seconds"],
        "prompt_influence": sound["prompt_influence"],
    }).encode("utf-8")
    request = urllib.request.Request(
        API_URL, data=body,
        headers={"xi-api-key": api_key, "Content-Type": "application/json"})
    print("generating %s ..." % sound["file"])
    with urllib.request.urlopen(request, timeout=120) as response:
        with open(raw_path, "wb") as f:
            f.write(response.read())

    os.makedirs(out_dir, exist_ok=True)
    ogg_path = os.path.join(out_dir, sound["file"])
    subprocess.run(postprocess_command(raw_path, ogg_path), check=True)
    print("wrote %s" % ogg_path)


def validate_pack(manifest_path, out_dir):
    return [s["file"] for s in load_sounds(manifest_path)
            if not os.path.exists(os.path.join(out_dir, s["file"]))]


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--manifest", default=MANIFEST_PATH)
    sub = parser.add_subparsers(dest="command", required=True)

    generate = sub.add_parser("generate")
    generate.add_argument("--only", default=None, help="generate a single file, e.g. 'Decide.ogg'")

    sub.add_parser("validate")

    args = parser.parse_args(argv)
    out_dir = output_dir(args.manifest)

    if args.command == "generate":
        api_key = os.environ.get("ELEVENLABS_API_KEY")
        if not api_key:
            print("error: ELEVENLABS_API_KEY is not set", file=sys.stderr)
            return 2
        failures = []
        for sound in load_sounds(args.manifest):
            if args.only and sound["file"] != args.only:
                continue
            try:
                generate_one(sound, api_key, out_dir)
            except Exception as exc:
                print("error: failed to generate %s: %s" % (sound["file"], exc), file=sys.stderr)
                failures.append(sound["file"])
        if failures:
            print("generate: %d sound(s) failed: %s" % (len(failures), ", ".join(failures)), file=sys.stderr)
            return 1
        return 0

    if args.command == "validate":
        missing = validate_pack(args.manifest, out_dir)
        for name in missing:
            print("MISSING  %s" % name)
        print("validate: %d missing sound(s)" % len(missing))
        return 1 if missing else 0

    return 2


if __name__ == "__main__":
    sys.exit(main())
