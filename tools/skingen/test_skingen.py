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
        with open(path, "w", encoding="utf-8") as f:
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


class ComposeTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.source = os.path.join(self.tmp.name, "source")
        self.out = os.path.join(self.tmp.name, "out")
        os.makedirs(self.source)

    def tearDown(self):
        self.tmp.cleanup()

    def _manifest(self, assets):
        path = os.path.join(self.tmp.name, "manifest.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump({"assets": assets}, f)
        return path

    def test_copy_recipe_resizes_to_manifest_dims(self):
        Image.new("RGBA", (100, 50), (255, 0, 0, 255)).save(os.path.join(self.source, "bg.png"))
        manifest = self._manifest({"Graphics/bg.png": {
            "width": 10, "height": 5, "optional": False,
            "recipe": {"type": "copy", "source": "bg.png"}}})
        skingen.compose(manifest, self.source, self.out)
        with Image.open(os.path.join(self.out, "Graphics", "bg.png")) as img:
            self.assertEqual(img.size, (10, 5))

    def test_sheet_recipe_places_cells(self):
        Image.new("RGBA", (8, 8), (0, 255, 0, 255)).save(os.path.join(self.source, "glyph.png"))
        manifest = self._manifest({"Graphics/font.png": {
            "width": 16, "height": 8, "optional": False,
            "recipe": {"type": "sheet", "cells": [
                {"source": "glyph.png", "x": 8, "y": 0, "w": 8, "h": 8}]}}})
        skingen.compose(manifest, self.source, self.out)
        with Image.open(os.path.join(self.out, "Graphics", "font.png")) as img:
            self.assertEqual(img.size, (16, 8))
            self.assertEqual(img.getpixel((0, 0))[3], 0)      # left half transparent
            self.assertEqual(img.getpixel((12, 4)), (0, 255, 0, 255))  # cell placed

    def test_hueshift_recipe_derives_variant_preserving_alpha(self):
        base = Image.new("RGBA", (4, 4), (255, 0, 0, 200))
        base.save(os.path.join(self.source, "fire.png"))
        manifest = self._manifest({
            "Graphics/fire_LC.png": {"width": 4, "height": 4, "optional": False,
                                     "recipe": {"type": "copy", "source": "fire.png"}},
            "Graphics/fire_HH.png": {"width": 4, "height": 4, "optional": False,
                                     "recipe": {"type": "hueshift", "base": "Graphics/fire_LC.png", "degrees": 120}},
        })
        skingen.compose(manifest, self.source, self.out)
        with Image.open(os.path.join(self.out, "Graphics", "fire_HH.png")) as img:
            r, g, _b, a = img.getpixel((0, 0))
            self.assertEqual(a, 200)                # alpha preserved
            self.assertGreater(g, r)                # red rotated ~120deg toward green

    def test_compose_skips_assets_without_recipe(self):
        manifest = self._manifest({"Graphics/todo.png": {
            "width": 4, "height": 4, "optional": False, "recipe": None}})
        skipped = skingen.compose(manifest, self.source, self.out)
        self.assertEqual(skipped, ["Graphics/todo.png"])
        self.assertFalse(os.path.exists(os.path.join(self.out, "Graphics", "todo.png")))


class PromptsTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()

    def tearDown(self):
        self.tmp.cleanup()

    def _write(self, name, payload):
        path = os.path.join(self.tmp.name, name)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(payload, f)
        return path

    def test_prompts_renders_style_family_and_desc(self):
        manifest = self._write("manifest.json", {"assets": {
            "Graphics/a.png": {"width": 10, "height": 5, "optional": False, "recipe": None}}})
        descriptors = self._write("descriptors.json", {
            "families": {"panel": "Panel base prompt."},
            "assets": {"Graphics/a.png": {"family": "panel", "desc": "The A panel."}}})
        out = os.path.join(self.tmp.name, "PROMPTS.md")
        missing = skingen.render_prompts(manifest, descriptors, "BASE STYLE.", out)
        self.assertEqual(missing, [])
        with open(out, encoding="utf-8") as f:
            text = f.read()
        self.assertIn("Graphics/a.png", text)
        self.assertIn("10x5", text)
        self.assertIn("BASE STYLE.", text)
        self.assertIn("Panel base prompt.", text)
        self.assertIn("The A panel.", text)

    def test_prompts_skips_hueshift_derived_assets(self):
        manifest = self._write("manifest.json", {"assets": {
            "Graphics/derived.png": {"width": 4, "height": 4, "optional": False,
                                     "recipe": {"type": "hueshift", "base": "Graphics/a.png", "degrees": 90}}}})
        descriptors = self._write("descriptors.json", {"families": {}, "assets": {}})
        out = os.path.join(self.tmp.name, "PROMPTS.md")
        missing = skingen.render_prompts(manifest, descriptors, "S", out)
        self.assertEqual(missing, [])

    def test_prompts_reports_uncovered_required_assets(self):
        manifest = self._write("manifest.json", {"assets": {
            "Graphics/uncovered.png": {"width": 4, "height": 4, "optional": False, "recipe": None}}})
        descriptors = self._write("descriptors.json", {"families": {}, "assets": {}})
        out = os.path.join(self.tmp.name, "PROMPTS.md")
        missing = skingen.render_prompts(manifest, descriptors, "S", out)
        self.assertEqual(missing, ["Graphics/uncovered.png"])


if __name__ == "__main__":
    unittest.main()
