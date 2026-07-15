import os
import subprocess
import tempfile
import unittest

import sfxgen


def _make_silent_ogg(path, seconds=0.1):
    """Generate a tiny valid Ogg file via ffmpeg for test fixtures.

    Prefers libvorbis (the encoder the production pipeline uses); falls back
    to libopus in an Ogg container when libvorbis is unavailable (some minimal
    ffmpeg builds omit it). Either way the result is a valid Ogg file that
    ffmpeg can fully decode, which is what _decode_ok verifies. Raises
    unittest.SkipTest only when neither encoder nor ffmpeg is present.
    """
    candidates = (
        ["ffmpeg", "-y", "-f", "lavfi", "-i", "anullsrc=r=8000:cl=mono",
         "-t", str(seconds), "-c:a", "libvorbis", "-qscale:a", "0", path],
        ["ffmpeg", "-y", "-f", "lavfi", "-i", "anullsrc=r=8000:cl=mono",
         "-t", str(seconds), "-c:a", "libopus", "-b:a", "32k", path],
    )
    last_error = None
    for cmd in candidates:
        try:
            subprocess.run(cmd, check=True,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            return
        except (subprocess.CalledProcessError, FileNotFoundError) as exc:
            last_error = exc
            continue
    raise unittest.SkipTest(
        "ffmpeg with libvorbis or libopus is required to build OGG fixtures: %s" % last_error)


class ManifestTests(unittest.TestCase):
    def test_manifest_matches_soundpath_inventory(self):
        names = {s["file"] for s in sfxgen.load_sounds(sfxgen.MANIFEST_PATH)}
        expected = {"Move.ogg", "Decide.ogg", "Game start.ogg", "Now loading.ogg",
                    "Stage Clear.ogg", "Full Combo.ogg", "Excellent.ogg", "New Record.ogg"}
        self.assertEqual(names, expected)

    def test_every_sound_has_prompt_and_duration(self):
        for sound in sfxgen.load_sounds(sfxgen.MANIFEST_PATH):
            self.assertTrue(sound["prompt"].strip(), sound["file"])
            self.assertGreater(sound["duration_seconds"], 0)
            self.assertLessEqual(sound["duration_seconds"], 22)


class ValidateTests(unittest.TestCase):
    def test_validate_reports_missing_files(self):
        with tempfile.TemporaryDirectory() as tmp:
            _make_silent_ogg(os.path.join(tmp, "Move.ogg"))
            errors = sfxgen.validate_pack(sfxgen.MANIFEST_PATH, tmp)
        # Every sound except Move.ogg is missing.
        missing_names = [e for e in errors if e.startswith("MISSING")]
        self.assertEqual(len(missing_names), 7)
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

    def test_validate_passes_for_complete_decodable_pack(self):
        with tempfile.TemporaryDirectory() as tmp:
            for sound in sfxgen.load_sounds(sfxgen.MANIFEST_PATH):
                _make_silent_ogg(os.path.join(tmp, sound["file"]))
            errors = sfxgen.validate_pack(sfxgen.MANIFEST_PATH, tmp)
        self.assertEqual(errors, [])


class FfmpegCommandTests(unittest.TestCase):
    def test_postprocess_command_normalizes_and_encodes_vorbis(self):
        cmd = sfxgen.postprocess_command("in.mp3", "out.ogg")
        self.assertEqual(cmd[0], "ffmpeg")
        self.assertIn("loudnorm=I=-16:TP=-1.5:LRA=11", " ".join(cmd))
        self.assertIn("libvorbis", cmd)
        self.assertEqual(cmd[-1], "out.ogg")


if __name__ == "__main__":
    unittest.main()
