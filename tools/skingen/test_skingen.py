import json
import os
import tempfile
import unittest

from PIL import Image

import skingen


class ScanTexturePathTests(unittest.TestCase):
    def test_scan_finds_known_constants_and_expands_lane_templates(self):
        paths = skingen.scan_texture_paths()
        self.assertIn("Graphics/1_background.jpg", paths)
        self.assertIn("Graphics/7_chips_drums.png", paths)
        # f-string lane templates must expand to all 10 lane codes
        self.assertIn("Graphics/ScreenPlayDrums chip fire_LC.png", paths)
        self.assertIn("Graphics/ScreenPlayDrums chip star_RD.png", paths)
        # a bare prefix (no extension) must not appear
        for p in paths:
            self.assertTrue(p.endswith((".png", ".jpg", ".jpeg", ".mp4")), p)

    def test_scan_marks_video_as_present(self):
        self.assertIn("Graphics/7_background.mp4", skingen.scan_texture_paths())


class ValidateTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.pack = os.path.join(self.tmp.name, "Graphics")
        os.makedirs(self.pack)

    def tearDown(self):
        self.tmp.cleanup()

    def _manifest(self, assets):
        path = os.path.join(self.tmp.name, "manifest.json")
        with open(path, "w") as f:
            json.dump({"assets": assets}, f)
        return path

    def _png(self, name, size):
        Image.new("RGBA", size, (0, 0, 0, 0)).save(os.path.join(self.pack, name))

    def test_validate_passes_for_complete_pack(self):
        self._png("a.png", (4, 2))
        manifest = self._manifest({"Graphics/a.png": {"width": 4, "height": 2, "optional": False, "recipe": None}})
        errors = skingen.validate_pack(manifest, os.path.dirname(self.pack))
        self.assertEqual(errors, [])

    def test_validate_reports_missing_required_file(self):
        manifest = self._manifest({"Graphics/missing.png": {"width": 4, "height": 2, "optional": False, "recipe": None}})
        errors = skingen.validate_pack(manifest, os.path.dirname(self.pack))
        self.assertEqual(len(errors), 1)
        self.assertIn("missing.png", errors[0])

    def test_validate_reports_dimension_mismatch(self):
        self._png("a.png", (8, 8))
        manifest = self._manifest({"Graphics/a.png": {"width": 4, "height": 2, "optional": False, "recipe": None}})
        errors = skingen.validate_pack(manifest, os.path.dirname(self.pack))
        self.assertEqual(len(errors), 1)
        self.assertIn("8x8", errors[0])

    def test_validate_skips_optional_and_null_dims(self):
        self._png("nodims.png", (3, 3))
        manifest = self._manifest({
            "Graphics/optional.png": {"width": 4, "height": 2, "optional": True, "recipe": None},
            "Graphics/nodims.png": {"width": None, "height": None, "optional": False, "recipe": None},
        })
        errors = skingen.validate_pack(manifest, os.path.dirname(self.pack))
        self.assertEqual(errors, [])


if __name__ == "__main__":
    unittest.main()
