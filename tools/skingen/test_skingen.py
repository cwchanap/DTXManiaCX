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

    def test_manifest_covers_every_scanned_texture_path(self):
        """Guard against bootstrap-forget drift: every path that
        scan_texture_paths() discovers in TexturePath.cs must have a
        corresponding entry in manifest.json. If a new texture constant is
        added to TexturePath.cs but `skingen.py bootstrap` is not re-run,
        the asset would be silently absent from the pack."""
        manifest = skingen.load_manifest(skingen.MANIFEST_PATH)
        manifest_keys = set(manifest.get("assets", {}).keys())
        scanned = set(skingen.scan_texture_paths())
        missing = sorted(scanned - manifest_keys)
        self.assertEqual(missing, [],
                         "TexturePath.cs has paths absent from manifest.json "
                         "(run `python tools/skingen/skingen.py bootstrap`):\n  " +
                         "\n  ".join(missing))


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

    def test_validate_reports_unreadable_image(self):
        # A file that exists but is not a decodable image must be flagged,
        # otherwise the CI validator passes while the C# gate only checks
        # file existence and the broken asset ships and falls back at runtime.
        with open(os.path.join(self.pack, "broken.png"), "wb") as f:
            f.write(b"not a real png")
        manifest = self._manifest({"Graphics/broken.png": {"width": 4, "height": 2, "optional": False, "recipe": None}})
        errors = skingen.validate_pack(manifest, os.path.dirname(self.pack))
        self.assertEqual(len(errors), 1)
        self.assertIn("UNREADABLE", errors[0])
        self.assertIn("broken.png", errors[0])

    def test_validate_reports_truncated_image_with_valid_header(self):
        # A PNG with a correct header but truncated pixel payload: Image.open
        # reads the header lazily and img.size would match, but img.load()
        # raises because the pixel data is incomplete. Texture2D.FromStream
        # decodes the whole image and would fail at runtime.
        src = os.path.join(self.pack, "truncated.png")
        Image.new("RGBA", (4, 4), (255, 0, 0, 255)).save(src)
        with open(src, "rb") as f:
            data = f.read()
        # Keep the PNG signature + IHDR (first 33 bytes) but drop most IDAT.
        with open(src, "wb") as f:
            f.write(data[:33])
        manifest = self._manifest({"Graphics/truncated.png": {"width": 4, "height": 4, "optional": False, "recipe": None}})
        errors = skingen.validate_pack(manifest, os.path.dirname(self.pack))
        self.assertEqual(len(errors), 1)
        self.assertIn("UNREADABLE", errors[0])
        self.assertIn("truncated.png", errors[0])

    def test_validate_decodes_null_dim_image_and_flags_corrupt(self):
        # A manifest entry with width/height: null must still be decoded: a
        # corrupt file used to skip read_dims entirely and pass validation
        # because the C# gate checks existence only.
        with open(os.path.join(self.pack, "nodims_broken.png"), "wb") as f:
            f.write(b"not a real png")
        manifest = self._manifest({
            "Graphics/nodims_broken.png": {"width": None, "height": None, "optional": False, "recipe": None},
        })
        errors = skingen.validate_pack(manifest, os.path.dirname(self.pack))
        self.assertEqual(len(errors), 1)
        self.assertIn("UNREADABLE", errors[0])
        self.assertIn("nodims_broken.png", errors[0])

    def test_validate_rejects_directory_in_place_of_file(self):
        os.makedirs(os.path.join(self.pack, "isdir.png"))
        manifest = self._manifest({"Graphics/isdir.png": {"width": 4, "height": 2, "optional": False, "recipe": None}})
        errors = skingen.validate_pack(manifest, os.path.dirname(self.pack))
        self.assertEqual(len(errors), 1)
        self.assertIn("NOTAFILE", errors[0])

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

    def test_sheet_text_cell_renders_label_glyphs(self):
        # Text-bearing sheet cells (menu labels, judge strings) are rendered
        # procedurally by compose — the AI generator is prompted "no text" so
        # glyphs can never come from generated art.
        manifest = self._manifest({"Graphics/menu.png": {
            "width": 128, "height": 32, "optional": False,
            "recipe": {"type": "sheet", "cells": [
                {"text": "EXIT", "x": 0, "y": 0, "w": 128, "h": 32}]}}})
        skingen.compose(manifest, self.source, self.out)
        with Image.open(os.path.join(self.out, "Graphics", "menu.png")) as img:
            self.assertEqual(img.size, (128, 32))
            alphas = [img.getpixel((x, y))[3] for x in range(128) for y in range(32)]
            # glyph core pixels are essentially opaque...
            self.assertGreater(max(alphas), 200)
            # ...but the cell is not a filled box: most of it stays transparent
            self.assertGreater(sum(1 for a in alphas if a < 16), len(alphas) // 2)

    def test_sheet_text_cell_honours_accent_color(self):
        manifest = self._manifest({"Graphics/judge.png": {
            "width": 96, "height": 24, "optional": False,
            "recipe": {"type": "sheet", "cells": [
                {"text": "MISS", "x": 0, "y": 0, "w": 96, "h": 24, "color": "#EF4444"}]}}})
        skingen.compose(manifest, self.source, self.out)
        with Image.open(os.path.join(self.out, "Graphics", "judge.png")) as img:
            # the glow halo must carry the requested accent: some visible pixel
            # is red-dominant (default cyan accent would be blue/green-dominant)
            red_dominant = any(
                px[0] > px[2] + 20 and px[3] > 60
                for px in img.getdata())
            self.assertTrue(red_dominant)

    def test_sheet_cursor_cell_keeps_center_translucent(self):
        # The title menu cursor row is drawn ON TOP of the label row at the
        # same position, so its fill must stay translucent enough for the
        # label text to read through, while the frame stays clearly visible.
        manifest = self._manifest({"Graphics/cursor.png": {
            "width": 128, "height": 32, "optional": False,
            "recipe": {"type": "sheet", "cells": [
                {"cursor": True, "x": 0, "y": 0, "w": 128, "h": 32}]}}})
        skingen.compose(manifest, self.source, self.out)
        with Image.open(os.path.join(self.out, "Graphics", "cursor.png")) as img:
            center_alpha = img.getpixel((64, 16))[3]
            self.assertGreater(center_alpha, 0)      # tint present
            self.assertLess(center_alpha, 160)       # label still reads through
            edge_alpha = max(img.getpixel((x, 16))[3] for x in range(6))
            self.assertGreater(edge_alpha, center_alpha)  # frame brighter than fill


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
