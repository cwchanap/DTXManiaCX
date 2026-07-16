import os
import subprocess
import tempfile
import unittest

import sfxgen


def _make_silent_ogg(path, seconds=0.1):
    """Generate a tiny valid Ogg/Vorbis file via ffmpeg for test fixtures.

    The production pipeline always encodes with libvorbis (see
    sfxgen.postprocess_command), and the game plays .ogg through NVorbis, which
    only understands Vorbis — so test fixtures must be Vorbis too. Raises
    unittest.SkipTest when ffmpeg or libvorbis is unavailable.
    """
    cmd = ["ffmpeg", "-y", "-f", "lavfi", "-i", "anullsrc=r=8000:cl=mono",
           "-t", str(seconds), "-c:a", "libvorbis", "-qscale:a", "0", path]
    try:
        subprocess.run(cmd, check=True,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    except (subprocess.CalledProcessError, FileNotFoundError) as exc:
        raise unittest.SkipTest(
            "ffmpeg with libvorbis is required to build OGG/Vorbis fixtures: %s" % exc)


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
