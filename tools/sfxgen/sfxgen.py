#!/usr/bin/env python3
"""sfxgen - CX Neon sound pipeline (ElevenLabs sound-generation API).

Commands:
  generate    Call ElevenLabs for each manifest sound (or --only NAME),
              save MP3 to raw/, then loudness-normalize + encode OGG/Vorbis
              into System/CXNeon/Sounds/. Requires ELEVENLABS_API_KEY and ffmpeg.
  validate    Check that every manifest sound exists in the output directory
              and is a fully decodable OGG/Vorbis file (rejects missing,
              empty, directory, and corrupt/truncated payloads that would
              fall back to silent audio at runtime). Also rejects Ogg
              containers whose audio codec is not Vorbis (e.g. Opus), which
              NVorbis cannot play at runtime. Requires ffmpeg and ffprobe.

Python 3.9+, stdlib only except ffmpeg/ffprobe (external binaries) for encode/decode/probe.
"""
import argparse
import json
import os
import shutil
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
    raw_path = os.path.join(RAW_DIR, sound["file"].removesuffix(".ogg") + ".mp3")
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


def _codec_name(path):
    """Return the lowercase ffmpeg codec name of the first audio stream, or None.

    Uses ffprobe (shipped with full ffmpeg builds). Returns None when ffprobe is
    unavailable or the file has no decodable audio stream, so callers can treat
    an unknown codec as a validation failure (fail-closed).
    """
    if not shutil.which("ffprobe"):
        return None
    result = subprocess.run(
        ["ffprobe", "-v", "error", "-select_streams", "a:0",
         "-show_entries", "stream=codec_name", "-of", "csv=p=0", path],
        stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=30)
    if result.returncode != 0:
        return None
    return result.stdout.decode("utf-8", errors="replace").strip().lower() or None


def _decode_ok(path):
    """Return True if path is a real, fully decodable Ogg/Vorbis audio file.

    Rejects directories, empty files, and corrupt/truncated payloads. A
    zero-byte or header-only OGG used to pass validate_pack because it only
    checked os.path.exists; NVorbis then returns silent audio at runtime with
    no error, so the broken asset ships unnoticed.

    The game loads .ogg exclusively through NVorbis.VorbisReader (see
    ManagedSound.LoadOggFile), which understands ONLY the Vorbis codec. ffmpeg
    happily decodes other Ogg-encapsulated codecs (Opus, FLAC, Speex), so the
    codec is probed explicitly and anything other than "vorbis" is rejected —
    otherwise an Ogg Opus asset would pass this gate and then fail (with a
    silent fallback) the moment the game tries to play it.
    """
    if os.path.isdir(path):
        return False
    if not os.path.isfile(path) or os.path.getsize(path) == 0:
        return False
    if not shutil.which("ffmpeg"):
        # Without ffmpeg we cannot verify the payload. Fail closed: report the
        # file as unreadable rather than silently accepting it, so a missing
        # decoder never lets a corrupt asset through the release gate.
        return False
    result = subprocess.run(
        ["ffmpeg", "-v", "error", "-i", path, "-f", "null", "-"],
        stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, timeout=30)
    if result.returncode != 0:
        return False
    return _codec_name(path) == "vorbis"


def validate_pack(manifest_path, out_dir):
    """Return a list of error strings for manifest sounds that are missing or
    not fully decodable. An empty list means the pack is sound."""
    errors = []
    for sound in load_sounds(manifest_path):
        path = os.path.join(out_dir, sound["file"])
        if not os.path.exists(path):
            errors.append("MISSING  %s" % sound["file"])
        elif not _decode_ok(path):
            errors.append("UNREADABLE %s: exists but could not be decoded as audio" % sound["file"])
    return errors


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
        errors = validate_pack(args.manifest, out_dir)
        for error in errors:
            print(error)
        print("validate: %d problem(s)" % len(errors))
        return 1 if errors else 0

    return 2


if __name__ == "__main__":
    sys.exit(main())
