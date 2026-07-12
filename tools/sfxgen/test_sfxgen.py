import json
import os
import tempfile
import unittest

import sfxgen


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
            open(os.path.join(tmp, "Move.ogg"), "wb").close()
            missing = sfxgen.validate_pack(sfxgen.MANIFEST_PATH, tmp)
        self.assertNotIn("Move.ogg", missing)
        self.assertIn("Decide.ogg", missing)
        self.assertEqual(len(missing), 7)


class FfmpegCommandTests(unittest.TestCase):
    def test_postprocess_command_normalizes_and_encodes_vorbis(self):
        cmd = sfxgen.postprocess_command("in.mp3", "out.ogg")
        self.assertEqual(cmd[0], "ffmpeg")
        self.assertIn("loudnorm=I=-16:TP=-1.5:LRA=11", " ".join(cmd))
        self.assertIn("libvorbis", cmd)
        self.assertEqual(cmd[-1], "out.ogg")


if __name__ == "__main__":
    unittest.main()
