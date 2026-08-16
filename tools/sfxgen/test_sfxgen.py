import json
import os
import shutil
import subprocess
import tempfile
import unittest
from unittest import mock

import sfxgen


def _make_silent_ogg(path, seconds=0.1, sample_rate=8000):
    """Generate a tiny valid Ogg/Vorbis file via ffmpeg for test fixtures.

    The production pipeline encodes with libvorbis (see
    sfxgen.postprocess_command), and the game plays .ogg through NVorbis, which
    only understands Vorbis. Prefer libvorbis for parity, then fall back to
    FFmpeg's native Vorbis encoder so compatible local builds still exercise
    validation. Raises unittest.SkipTest when neither encoder is available.
    """
    base_cmd = ["ffmpeg", "-y", "-f", "lavfi", "-i",
                "anullsrc=r=%d:cl=stereo" % sample_rate,
                "-t", str(seconds)]
    encoders = [
        ["-c:a", "libvorbis", "-qscale:a", "0"],
        ["-c:a", "vorbis", "-strict", "experimental", "-qscale:a", "0"],
    ]
    last_error = None
    for encoder in encoders:
        try:
            subprocess.run(base_cmd + encoder + [path], check=True,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            return
        except (subprocess.CalledProcessError, FileNotFoundError) as exc:
            last_error = exc
    raise unittest.SkipTest(
        "ffmpeg with a Vorbis encoder is required to build fixtures: %s" % last_error)


def _make_silent_ogg_opus(path, seconds=0.1):
    """Generate a tiny valid Ogg/Opus file via ffmpeg, or skip if unavailable.

    Used to verify the validator rejects non-Vorbis Ogg containers that ffmpeg
    can decode but NVorbis cannot play at runtime.
    """
    cmd = ["ffmpeg", "-y", "-f", "lavfi", "-i", "anullsrc=r=8000:cl=mono",
           "-t", str(seconds), "-c:a", "libopus", "-b:a", "32k", path]
    try:
        subprocess.run(cmd, check=True,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    except (subprocess.CalledProcessError, FileNotFoundError) as exc:
        raise unittest.SkipTest(
            "ffmpeg with libopus is required for this test: %s" % exc)


class ManifestTests(unittest.TestCase):
    @staticmethod
    def _write_manifest(directory, sound):
        path = os.path.join(directory, "manifest.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump({"output_dir": "out", "sounds": [sound]}, f)
        return path

    def test_existing_elevenlabs_entry_defaults_generator_without_prompt_contract_drift(self):
        with tempfile.TemporaryDirectory() as tmp:
            manifest_path = ManifestTests._write_manifest(
                tmp,
                {
                    "file": "Legacy.ogg",
                    "duration_seconds": 0.5,
                    "prompt_influence": 0.4,
                    "prompt": "legacy click",
                })

            sounds = sfxgen.load_sounds(manifest_path)

        self.assertEqual("elevenlabs", sounds[0]["generator"])

    def test_ffmpeg_sine_entry_requires_frequency_and_positive_short_duration(self):
        invalid_sounds = [
            ({
                "file": "Beat.ogg",
                "generator": "ffmpeg_sine",
                "duration_seconds": 0.03,
            }, "frequency_hz"),
            ({
                "file": "Beat.ogg",
                "generator": "ffmpeg_sine",
                "duration_seconds": 0,
                "frequency_hz": 1000,
            }, "duration_seconds"),
            ({
                "file": "Beat.ogg",
                "generator": "ffmpeg_sine",
                "duration_seconds": 1.1,
                "frequency_hz": 1000,
            }, "short"),
        ]

        for sound, expected_message in invalid_sounds:
            with self.subTest(sound=sound):
                with tempfile.TemporaryDirectory() as tmp:
                    manifest_path = self._write_manifest(tmp, sound)
                    with self.assertRaisesRegex(ValueError, expected_message):
                        sfxgen.load_sounds(manifest_path)

    def test_ffmpeg_sine_entry_does_not_require_prompt_or_prompt_influence(self):
        with tempfile.TemporaryDirectory() as tmp:
            manifest_path = self._write_manifest(
                tmp,
                {
                    "file": "Beat.ogg",
                    "generator": "ffmpeg_sine",
                    "duration_seconds": 0.03,
                    "frequency_hz": 1000,
                })

            sounds = sfxgen.load_sounds(manifest_path)

        self.assertEqual("ffmpeg_sine", sounds[0]["generator"])

    def test_manifest_matches_soundpath_inventory(self):
        # Derive the expected inventory from SoundPath.cs instead of hardcoding
        # it, so drift in either direction is caught: a sound in the manifest
        # but not in SoundPath.cs would never be played by the game, and a
        # sound in SoundPath.cs but not in the manifest would be missing from
        # the pack. Mirrors skingen's
        # test_manifest_covers_every_scanned_texture_path.
        names = {s["file"] for s in sfxgen.load_sounds(sfxgen.MANIFEST_PATH)}
        expected = {os.path.basename(p) for p in sfxgen.scan_sound_paths()}
        self.assertEqual(names, expected,
                         "Manifest and SoundPath.cs disagree on the sound inventory "
                         "(add missing sounds to manifest.json or remove stale ones "
                         "from SoundPath.cs):\n  manifest-only: %s\n  soundpath-only: %s"
                         % (sorted(names - expected), sorted(expected - names)))

    def test_every_sound_has_prompt_and_duration(self):
        for sound in sfxgen.load_sounds(sfxgen.MANIFEST_PATH):
            self.assertGreater(sound["duration_seconds"], 0)
            if sound["generator"] == "elevenlabs":
                self.assertTrue(sound["prompt"].strip(), sound["file"])
                self.assertLessEqual(sound["duration_seconds"], 22)


class ValidateTests(unittest.TestCase):
    def test_validate_reports_missing_files(self):
        with tempfile.TemporaryDirectory() as tmp:
            _make_silent_ogg(os.path.join(tmp, "Move.ogg"))
            errors = sfxgen.validate_pack(sfxgen.MANIFEST_PATH, tmp)
        # Every sound except Move.ogg is missing.
        missing_names = [e for e in errors if e.startswith("MISSING")]
        self.assertEqual(len(missing_names), 9)
        self.assertFalse(any("Move.ogg" in e for e in errors))

    def test_validate_rejects_empty_file(self):
        # A zero-byte OGG used to pass because validate_pack checked only
        # os.path.exists; NVorbis then returns silent audio at runtime.
        with tempfile.TemporaryDirectory() as tmp:
            _make_silent_ogg(os.path.join(tmp, "Move.ogg"))
            open(os.path.join(tmp, "Decide.ogg"), "wb").close()  # empty
            errors = sfxgen.validate_pack(sfxgen.MANIFEST_PATH, tmp)
        unreadable = [e for e in errors if e.startswith("UNREADABLE") and "Decide.ogg" in e]
        self.assertEqual(len(unreadable), 1)
        self.assertFalse(any("Move.ogg" in e for e in errors))

    def test_validate_rejects_directory_in_place_of_file(self):
        with tempfile.TemporaryDirectory() as tmp:
            _make_silent_ogg(os.path.join(tmp, "Move.ogg"))
            os.makedirs(os.path.join(tmp, "Decide.ogg"))  # directory, not a file
            errors = sfxgen.validate_pack(sfxgen.MANIFEST_PATH, tmp)
        unreadable = [e for e in errors if e.startswith("UNREADABLE") and "Decide.ogg" in e]
        self.assertEqual(len(unreadable), 1)
        self.assertFalse(any("Move.ogg" in e for e in errors))

    def test_validate_rejects_corrupt_ogg(self):
        with tempfile.TemporaryDirectory() as tmp:
            _make_silent_ogg(os.path.join(tmp, "Move.ogg"))
            # Valid OGG header bytes but truncated/corrupt payload.
            with open(os.path.join(tmp, "Decide.ogg"), "wb") as f:
                f.write(b"OggS\x00\x02\x00\x00" + b"\x00" * 64)
            errors = sfxgen.validate_pack(sfxgen.MANIFEST_PATH, tmp)
        unreadable = [e for e in errors if e.startswith("UNREADABLE") and "Decide.ogg" in e]
        self.assertEqual(len(unreadable), 1)
        self.assertFalse(any("Move.ogg" in e for e in errors))

    def test_validate_rejects_ogg_opus_not_vorbis(self):
        # ffmpeg decodes Ogg Opus fine, but the game plays .ogg via NVorbis,
        # which only supports Vorbis. validate must reject Opus (and any other
        # non-Vorbis Ogg codec) so it never ships a silent-at-runtime asset.
        with tempfile.TemporaryDirectory() as tmp:
            _make_silent_ogg(os.path.join(tmp, "Move.ogg"))
            _make_silent_ogg_opus(os.path.join(tmp, "Decide.ogg"))
            errors = sfxgen.validate_pack(sfxgen.MANIFEST_PATH, tmp)
        unreadable = [e for e in errors if e.startswith("UNREADABLE") and "Decide.ogg" in e]
        self.assertEqual(len(unreadable), 1)
        self.assertFalse(any("Move.ogg" in e for e in errors))

    def test_validate_rejects_sample_rate_above_monogame_limit(self):
        # MonoGame SoundEffect accepts at most 48 kHz. A decodable 192 kHz
        # Vorbis file otherwise reaches ManagedSound and becomes silent when
        # ResourceManager catches the sampleRate exception.
        with tempfile.TemporaryDirectory() as tmp:
            _make_silent_ogg(
                os.path.join(tmp, "Move.ogg"), sample_rate=192000)
            errors = sfxgen.validate_pack(sfxgen.MANIFEST_PATH, tmp)
        incompatible = [
            e for e in errors
            if e.startswith("INCOMPATIBLE") and "Move.ogg" in e
        ]
        self.assertEqual(len(incompatible), 1)
        self.assertIn("192000", incompatible[0])

    def test_validate_passes_for_complete_decodable_pack(self):
        with tempfile.TemporaryDirectory() as tmp:
            for sound in sfxgen.load_sounds(sfxgen.MANIFEST_PATH):
                seconds = sound["duration_seconds"] if sound["generator"] == "ffmpeg_sine" else 0.1
                _make_silent_ogg(os.path.join(tmp, sound["file"]), seconds=seconds)
            errors = sfxgen.validate_pack(sfxgen.MANIFEST_PATH, tmp)
        self.assertEqual(errors, [])

    def test_validate_rejects_deterministic_click_that_exceeds_manifest_duration(self):
        with tempfile.TemporaryDirectory() as tmp:
            manifest_path = ManifestTests._write_manifest(
                tmp,
                {
                    "file": "Beat.ogg",
                    "generator": "ffmpeg_sine",
                    "duration_seconds": 0.03,
                    "frequency_hz": 1000,
                })
            _make_silent_ogg(os.path.join(tmp, "Beat.ogg"), seconds=0.2)

            errors = sfxgen.validate_pack(manifest_path, tmp)

        self.assertTrue(any("duration" in error.lower() for error in errors))


class FfmpegCommandTests(unittest.TestCase):
    def test_postprocess_command_normalizes_and_encodes_vorbis(self):
        cmd = sfxgen.postprocess_command("in.mp3", "out.ogg")
        self.assertEqual(cmd[0], "ffmpeg")
        self.assertIn("loudnorm=I=-16:TP=-1.5:LRA=11", " ".join(cmd))
        self.assertIn("libvorbis", cmd)
        self.assertEqual(cmd[-1], "out.ogg")

    def test_postprocess_command_resamples_to_monogame_compatible_rate(self):
        cmd = sfxgen.postprocess_command("in.mp3", "out.ogg")
        self.assertIn("-ar", cmd)
        sample_rate_index = cmd.index("-ar")
        self.assertEqual(cmd[sample_rate_index + 1], "48000")

    def test_ffmpeg_sine_command_uses_frequency_short_duration_and_vorbis_output(self):
        sound = {
            "file": "Beat.ogg",
            "generator": "ffmpeg_sine",
            "duration_seconds": 0.03,
            "frequency_hz": 1000,
        }

        cmd = sfxgen.ffmpeg_sine_command(sound, "out.ogg")
        joined = " ".join(cmd)

        self.assertIn("sine=frequency=1000:duration=0.03", joined)
        self.assertIn("afade=t=out", joined)
        self.assertIn(cmd[cmd.index("-c:a") + 1], ("libvorbis", "vorbis"))
        self.assertIn("-ac", cmd)
        self.assertEqual(cmd[cmd.index("-ac") + 1], "2")
        self.assertEqual(cmd[cmd.index("-ar") + 1], "48000")
        self.assertEqual(cmd[-1], "out.ogg")

    @unittest.skipUnless(shutil.which("ffmpeg"), "ffmpeg is required for generation")
    def test_selected_ffmpeg_sine_generation_does_not_require_elevenlabs_key(self):
        sound = {
            "file": "Beat.ogg",
            "generator": "ffmpeg_sine",
            "duration_seconds": 0.03,
            "frequency_hz": 1000,
        }
        with tempfile.TemporaryDirectory() as tmp:
            manifest_path = ManifestTests._write_manifest(tmp, sound)
            with mock.patch.object(sfxgen, "REPO_ROOT", tmp), \
                    mock.patch.dict(os.environ, {"ELEVENLABS_API_KEY": ""}, clear=False):
                result = sfxgen.main([
                    "--manifest", manifest_path,
                    "generate", "--only", "Beat.ogg",
                ])

            self.assertEqual(0, result)
            self.assertTrue(os.path.isfile(os.path.join(tmp, "out", "Beat.ogg")))


class _FakeResponse:
    """Minimal file-like object standing in for an HTTP response body."""

    def __init__(self, data):
        self._data = data

    def read(self, size=-1):
        if size is None or size < 0:
            chunk, self._data = self._data, b""
            return chunk
        chunk, self._data = self._data[:size], self._data[size:]
        return chunk


class StreamResponseTests(unittest.TestCase):
    def test_streams_full_body_to_file(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "out.mp3")
            sfxgen._stream_response_to_file(_FakeResponse(b"MP3DATA"), path)
            with open(path, "rb") as f:
                self.assertEqual(f.read(), b"MP3DATA")

    def test_aborts_when_body_exceeds_cap(self):
        # 1 KB cap, 4 KB body → must abort and delete the partial file.
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "out.mp3")
            with self.assertRaises(ValueError):
                sfxgen._stream_response_to_file(
                    _FakeResponse(b"\x00" * 4096), path, max_bytes=1024)
            self.assertFalse(os.path.exists(path),
                             "partial file must be removed on cap overflow")


if __name__ == "__main__":
    unittest.main()
