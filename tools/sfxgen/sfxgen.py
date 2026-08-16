#!/usr/bin/env python3
"""sfxgen - CX Neon sound pipeline (ElevenLabs sound-generation API).

Commands:
  generate    Call ElevenLabs for each manifest sound (or --only NAME),
              save MP3 to raw/, then loudness-normalize + encode OGG/Vorbis
              into System/CXNeon/Sounds/. Requires ELEVENLABS_API_KEY and ffmpeg.
  validate    Check that every manifest sound exists in the output directory
              and is a runtime-compatible OGG/Vorbis file (rejects missing,
              empty, directory, corrupt/truncated, non-Vorbis, and unsupported
              sample-rate payloads that would fall back to silent audio).
              Requires ffmpeg and ffprobe.

Python 3.9+, stdlib only except ffmpeg/ffprobe (external binaries) for encode/decode/probe.
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import urllib.request

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MANIFEST_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "manifest.json")
RAW_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "raw")
SOUNDPATH_CS = os.path.join(REPO_ROOT, "DTXMania.Game", "Lib", "Resources", "SoundPath.cs")
API_URL = "https://api.elevenlabs.io/v1/sound-generation"

# Hard cap on a single sound-generation response. The manifest caps
# duration_seconds at 22s, and ElevenLabs sound-generation MP3 output is well
# under 1 MB at that duration — so 50 MB is far above any legitimate response
# and exists only to fail fast if the API ever returns a malformed/huge payload
# instead of buffering it all into memory. Without this cap, response.read()
# would allocate the entire body before any code could abort.
MAX_RESPONSE_BYTES = 50 * 1024 * 1024
MIN_RUNTIME_SAMPLE_RATE = 8000
MAX_RUNTIME_SAMPLE_RATE = 48000
MAX_DETERMINISTIC_DURATION_SECONDS = 1.0
DETERMINISTIC_DURATION_TOLERANCE_SECONDS = 0.02


def load_sounds(manifest_path):
    with open(manifest_path, encoding="utf-8") as f:
        sounds = json.load(f)["sounds"]

    normalized = []
    for sound in sounds:
        entry = dict(sound)
        generator = entry.get("generator", "elevenlabs")
        if generator not in ("elevenlabs", "ffmpeg_sine"):
            raise ValueError("unsupported generator %r for %s" % (generator, entry.get("file", "<unnamed>")))

        duration = entry.get("duration_seconds")
        if not isinstance(duration, (int, float)) or duration <= 0:
            raise ValueError("duration_seconds must be positive for %s" % entry.get("file", "<unnamed>"))

        if generator == "elevenlabs":
            if not isinstance(entry.get("prompt"), str) or not entry["prompt"].strip():
                raise ValueError("prompt is required for %s" % entry.get("file", "<unnamed>"))
            if not isinstance(entry.get("prompt_influence"), (int, float)):
                raise ValueError("prompt_influence is required for %s" % entry.get("file", "<unnamed>"))
            if duration > 22:
                raise ValueError("duration_seconds must be at most 22 for %s" % entry.get("file", "<unnamed>"))
        else:
            frequency = entry.get("frequency_hz")
            if not isinstance(frequency, (int, float)) or frequency <= 0:
                raise ValueError("frequency_hz must be positive for %s" % entry.get("file", "<unnamed>"))
            if duration > MAX_DETERMINISTIC_DURATION_SECONDS:
                raise ValueError(
                    "duration_seconds must be short (<= %.2fs) for %s"
                    % (MAX_DETERMINISTIC_DURATION_SECONDS, entry.get("file", "<unnamed>")))

        entry["generator"] = generator
        normalized.append(entry)

    return normalized


def scan_sound_paths():
    """All Sounds/*.ogg literals in SoundPath.cs, sorted.

    Mirrors skingen.scan_texture_paths so the test suite can catch drift in
    either direction between the C# constants and the sfxgen manifest: a new
    sound added to SoundPath.cs but missing from manifest.json (or vice versa)
    is a bug — the pack would either ship without the asset (CxNeonPackTests
    catches this) or the manifest would reference a sound the game never plays
    (only this scan catches that).
    """
    with open(SOUNDPATH_CS, encoding="utf-8") as f:
        source = f.read()
    return sorted(set(re.findall(r'"(Sounds/[^"]+\.ogg)"', source)))


def output_dir(manifest_path):
    with open(manifest_path, encoding="utf-8") as f:
        return os.path.join(REPO_ROOT, json.load(f)["output_dir"])


def postprocess_command(raw_path, ogg_path):
    return [
        "ffmpeg", "-y", "-i", raw_path,
        "-af", "loudnorm=I=-16:TP=-1.5:LRA=11",
        "-ar", str(MAX_RUNTIME_SAMPLE_RATE),
        "-c:a", "libvorbis", "-qscale:a", "5",
        ogg_path,
    ]


def ffmpeg_sine_command(sound, ogg_path):
    """Return the deterministic short sine-burst ffmpeg command for one entry."""
    duration = sound["duration_seconds"]
    frequency = sound["frequency_hz"]
    fade_start = max(0.0, duration - min(0.01, duration / 3.0))
    fade_duration = min(0.01, duration / 3.0)
    return [
        "ffmpeg", "-y",
        "-f", "lavfi",
        "-i", "sine=frequency=%s:duration=%s" % (frequency, duration),
        "-af", "afade=t=out:st=%s:d=%s" % (fade_start, fade_duration),
        "-ac", "2",
        "-ar", str(MAX_RUNTIME_SAMPLE_RATE),
        "-c:a", "vorbis", "-strict", "experimental", "-qscale:a", "5",
        ogg_path,
    ]


def _stream_response_to_file(response, raw_path, max_bytes=MAX_RESPONSE_BYTES):
    """Stream an HTTP response body to disk in fixed-size chunks.

    Aborts if the body exceeds max_bytes, deleting the partial file and raising
    ValueError. Streaming avoids buffering the entire MP3 into memory before
    any size check can run; the cap fails fast on a malformed/huge response
    instead of allocating it all.
    """
    written = 0
    try:
        with open(raw_path, "wb") as f:
            while True:
                chunk = response.read(64 * 1024)
                if not chunk:
                    break
                written += len(chunk)
                if written > max_bytes:
                    raise ValueError(
                        "response exceeded %d bytes (cap=%d); aborting" % (written, max_bytes))
                f.write(chunk)
    except Exception:
        # Don't leave a partial file behind — ffmpeg would then try to
        # postprocess a truncated MP3 and emit a confusing error.
        try:
            os.remove(raw_path)
        except OSError:
            pass
        raise


def generate_one(sound, api_key, out_dir):
    if sound.get("generator", "elevenlabs") == "ffmpeg_sine":
        os.makedirs(out_dir, exist_ok=True)
        ogg_path = os.path.join(out_dir, sound["file"])
        print("generating %s ..." % sound["file"])
        subprocess.run(ffmpeg_sine_command(sound, ogg_path), check=True)
        print("wrote %s" % ogg_path)
        return

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
        _stream_response_to_file(response, raw_path)

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


def _sample_rate(path):
    """Return the first audio stream's sample rate, or None when unavailable."""
    if not shutil.which("ffprobe"):
        return None
    result = subprocess.run(
        ["ffprobe", "-v", "error", "-select_streams", "a:0",
         "-show_entries", "stream=sample_rate", "-of", "csv=p=0", path],
        stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=30)
    if result.returncode != 0:
        return None
    try:
        return int(result.stdout.decode("utf-8", errors="replace").strip())
    except ValueError:
        return None


def _duration_seconds(path):
    """Return the decoded container duration in seconds, or None."""
    if not shutil.which("ffprobe"):
        return None
    result = subprocess.run(
        ["ffprobe", "-v", "error", "-show_entries", "format=duration",
         "-of", "default=noprint_wrappers=1:nokey=1", path],
        stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=30)
    if result.returncode != 0:
        return None
    try:
        return float(result.stdout.decode("utf-8", errors="replace").strip())
    except ValueError:
        return None


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
    not runtime-compatible. An empty list means the pack is sound."""
    errors = []
    for sound in load_sounds(manifest_path):
        path = os.path.join(out_dir, sound["file"])
        if not os.path.exists(path):
            errors.append("MISSING  %s" % sound["file"])
        elif not _decode_ok(path):
            errors.append("UNREADABLE %s: exists but could not be decoded as audio" % sound["file"])
        else:
            sample_rate = _sample_rate(path)
            if sample_rate is None:
                errors.append("UNREADABLE %s: sample rate could not be determined" % sound["file"])
            elif not MIN_RUNTIME_SAMPLE_RATE <= sample_rate <= MAX_RUNTIME_SAMPLE_RATE:
                errors.append(
                    "INCOMPATIBLE %s: sample rate %d Hz is outside MonoGame's "
                    "supported %d-%d Hz range"
                    % (sound["file"], sample_rate,
                       MIN_RUNTIME_SAMPLE_RATE, MAX_RUNTIME_SAMPLE_RATE))
            if sound["generator"] == "ffmpeg_sine":
                duration = _duration_seconds(path)
                if duration is None:
                    errors.append("UNREADABLE %s: duration could not be determined" % sound["file"])
                elif duration > sound["duration_seconds"] + DETERMINISTIC_DURATION_TOLERANCE_SECONDS:
                    errors.append(
                        "INCOMPATIBLE %s: duration %.3fs exceeds manifest %.3fs"
                        % (sound["file"], duration, sound["duration_seconds"]))
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
        failures = []
        only_matched = args.only is None
        sounds = load_sounds(args.manifest)
        selected_sounds = [
            sound for sound in sounds
            if args.only is None or sound["file"] == args.only
        ]
        if args.only and not selected_sounds:
            print("error: --only %r matched no manifest entry" % args.only, file=sys.stderr)
            return 1
        api_key = os.environ.get("ELEVENLABS_API_KEY")
        if any(sound["generator"] == "elevenlabs" for sound in selected_sounds) and not api_key:
            print("error: ELEVENLABS_API_KEY is not set", file=sys.stderr)
            return 2
        for sound in sounds:
            if args.only and sound["file"] != args.only:
                continue
            if args.only:
                only_matched = True
            try:
                generate_one(sound, api_key, out_dir)
            except Exception as exc:
                print("error: failed to generate %s: %s" % (sound["file"], exc), file=sys.stderr)
                failures.append(sound["file"])
        if args.only and not only_matched:
            print("error: --only %r matched no manifest entry" % args.only, file=sys.stderr)
            return 1
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
