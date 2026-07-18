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

    def test_compose_only_hueshift_builds_base_into_empty_pack(self):
        # --only fire_HH must pull the LC base through pass 1 so an empty pack
        # can still emit the derived asset.
        base = Image.new("RGBA", (4, 4), (255, 0, 0, 200))
        base.save(os.path.join(self.source, "fire.png"))
        manifest = self._manifest({
            "Graphics/fire_LC.png": {"width": 4, "height": 4, "optional": False,
                                     "recipe": {"type": "copy", "source": "fire.png"}},
            "Graphics/fire_HH.png": {"width": 4, "height": 4, "optional": False,
                                     "recipe": {"type": "hueshift", "base": "Graphics/fire_LC.png", "degrees": 120}},
            "Graphics/other.png": {"width": 2, "height": 2, "optional": False,
                                   "recipe": {"type": "copy", "source": "fire.png"}},
        })
        empty_out = os.path.join(self.tmp.name, "empty_out")
        os.makedirs(empty_out, exist_ok=True)
        skingen.compose(manifest, self.source, empty_out, only="fire_HH")
        self.assertTrue(os.path.exists(os.path.join(empty_out, "Graphics", "fire_LC.png")))
        self.assertTrue(os.path.exists(os.path.join(empty_out, "Graphics", "fire_HH.png")))
        self.assertFalse(os.path.exists(os.path.join(empty_out, "Graphics", "other.png")))

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

    def test_sheet_text_cell_size_option_scales_glyph(self):
        # Digit strips (combo, rate numbers) must nearly fill their cell so the
        # renderers' fixed per-digit advance leaves no gaps; "size" raises the
        # target glyph height from the 0.6 default.
        def rows_with_ink(name, cell):
            manifest = self._manifest({f"Graphics/{name}": {
                "width": 64, "height": 64, "optional": False,
                "recipe": {"type": "sheet", "cells": [cell]}}})
            skingen.compose(manifest, self.source, self.out)
            with Image.open(os.path.join(self.out, "Graphics", name)) as img:
                return sum(
                    1 for y in range(64)
                    if any(img.getpixel((x, y))[3] > 100 for x in range(64)))

        small = rows_with_ink("d_small.png", {"text": "8", "x": 0, "y": 0, "w": 64, "h": 64})
        large = rows_with_ink("d_large.png", {"text": "8", "x": 0, "y": 0, "w": 64, "h": 64, "size": 0.9})
        self.assertGreater(large, small)

    def test_sheet_text_cell_anchor_top_moves_glyph_up(self):
        # Difficulty badges draw the level number over the badge's lower half,
        # so the tier label must sit at the top of the cell, not centered.
        manifest = self._manifest({"Graphics/badge.png": {
            "width": 60, "height": 60, "optional": False,
            "recipe": {"type": "sheet", "cells": [
                {"text": "BASIC", "x": 0, "y": 0, "w": 60, "h": 60, "size": 0.25, "anchor": "top"}]}}})
        skingen.compose(manifest, self.source, self.out)
        with Image.open(os.path.join(self.out, "Graphics", "badge.png")) as img:
            top_ink = sum(1 for y in range(30) for x in range(60) if img.getpixel((x, y))[3] > 100)
            bottom_ink = sum(1 for y in range(30, 60) for x in range(60) if img.getpixel((x, y))[3] > 100)
            self.assertGreater(top_ink, 0)
            self.assertEqual(bottom_ink, 0)  # lower half stays free for the level number

    def test_sheet_combined_cursor_text_cell_renders_panel_with_label(self):
        # Pad caps combine a framed panel with a label; "inset" shrinks the
        # panel inside the cell so overlapping destination rects in-game leave
        # neighbours' labels visible through the transparent margin.
        manifest = self._manifest({"Graphics/pad.png": {
            "width": 96, "height": 96, "optional": False,
            "recipe": {"type": "sheet", "cells": [
                {"cursor": True, "text": "LC", "x": 0, "y": 0, "w": 96, "h": 96,
                 "inset": 0.2, "size": 0.3}]}}})
        skingen.compose(manifest, self.source, self.out)
        with Image.open(os.path.join(self.out, "Graphics", "pad.png")) as img:
            self.assertEqual(img.getpixel((2, 2))[3], 0)          # margin transparent
            self.assertGreater(img.getpixel((48, 48))[3], 0)      # label/fill present at center
            frame_alpha = max(img.getpixel((x, 48))[3] for x in range(18, 26))
            self.assertGreater(frame_alpha, 150)                  # frame visible inside inset

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

    def test_sheet_panel_cell_renders_dark_body_with_accent_border(self):
        # Bar bodies and grid cells are near-opaque dark panels (unlike cursor
        # cells, whose fill must stay see-through): white titles need a solid
        # dark backdrop to read against changing backgrounds.
        manifest = self._manifest({"Graphics/bar.png": {
            "width": 128, "height": 48, "optional": False,
            "recipe": {"type": "sheet", "cells": [
                {"panel": True, "x": 0, "y": 0, "w": 128, "h": 48}]}}})
        skingen.compose(manifest, self.source, self.out)
        with Image.open(os.path.join(self.out, "Graphics", "bar.png")) as img:
            r, g, b, a = img.getpixel((64, 24))
            self.assertGreater(a, 180)               # body is near-opaque...
            self.assertLess(max(r, g, b), 60)        # ...and dark
            edge = max((img.getpixel((x, 24)) for x in range(6)),
                       key=lambda px: px[3])
            self.assertGreater(max(edge[:3]), 90)    # accent frame at the edge

    def test_sheet_panel_cell_box_confines_content_to_subrect(self):
        # Selected-bar textures are drawn 30px above the bar bounds and would
        # chop the neighbouring bars; "box" confines the visible body to a
        # sub-rect of the cell, leaving the rest of the canvas transparent.
        manifest = self._manifest({"Graphics/selbar.png": {
            "width": 64, "height": 64, "optional": False,
            "recipe": {"type": "sheet", "cells": [
                {"panel": True, "text": "SS", "x": 0, "y": 0, "w": 64, "h": 64,
                 "box": [0, 20, 64, 30]}]}}})
        skingen.compose(manifest, self.source, self.out)
        with Image.open(os.path.join(self.out, "Graphics", "selbar.png")) as img:
            top_ink = sum(1 for y in range(18) for x in range(64) if img.getpixel((x, y))[3] > 0)
            bottom_ink = sum(1 for y in range(52, 64) for x in range(64) if img.getpixel((x, y))[3] > 0)
            self.assertEqual(top_ink, 0)             # above the box: transparent
            self.assertEqual(bottom_ink, 0)          # below the box: transparent
            self.assertGreater(img.getpixel((32, 35))[3], 180)  # body inside the box

    def test_sheet_panel_cell_borderless_is_plain_dark_box(self):
        # The number boxes inside the BPM/skill panels are plain dark wells —
        # no accent frame — so bitmap digits sit in a quiet inset area.
        manifest = self._manifest({"Graphics/well.png": {
            "width": 64, "height": 32, "optional": False,
            "recipe": {"type": "sheet", "cells": [
                {"panel": True, "border": False, "x": 0, "y": 0, "w": 64, "h": 32}]}}})
        skingen.compose(manifest, self.source, self.out)
        with Image.open(os.path.join(self.out, "Graphics", "well.png")) as img:
            self.assertGreater(img.getpixel((32, 16))[3], 180)  # dark body present
            for px in img.getdata():
                if px[3] > 0:
                    self.assertLess(max(px[:3]), 60)  # nowhere an accent-bright pixel

    def test_sheet_glyph_cell_renders_drum_icon_with_transparent_margins(self):
        # Pad indicators are drawn as icons, not text (per design feedback):
        # "glyph" cells render simple neon drum shapes. Cells overlap in-game,
        # so margins must stay transparent; a "drum" head is a filled shape.
        manifest = self._manifest({"Graphics/pad.png": {
            "width": 96, "height": 96, "optional": False,
            "recipe": {"type": "sheet", "cells": [
                {"glyph": "drum", "color": "#FDE047", "x": 0, "y": 0, "w": 96, "h": 96}]}}})
        skingen.compose(manifest, self.source, self.out)
        with Image.open(os.path.join(self.out, "Graphics", "pad.png")) as img:
            self.assertEqual(img.getpixel((4, 4))[3], 0)         # corner transparent
            self.assertEqual(img.getpixel((4, 48))[3], 0)        # side margin transparent
            self.assertGreater(img.getpixel((48, 48))[3], 60)    # filled head at center
            colored = any(px[0] > 150 and px[1] > 150 and px[2] < 130 and px[3] > 150
                          for px in img.getdata())
            self.assertTrue(colored)                             # yellow accent present

    def test_sheet_glyph_cell_cymbal_is_hollow_ring(self):
        manifest = self._manifest({"Graphics/cym.png": {
            "width": 96, "height": 96, "optional": False,
            "recipe": {"type": "sheet", "cells": [
                {"glyph": "cymbal", "x": 0, "y": 0, "w": 96, "h": 96}]}}})
        skingen.compose(manifest, self.source, self.out)
        with Image.open(os.path.join(self.out, "Graphics", "cym.png")) as img:
            center = img.getpixel((48, 48))[3]
            ring = max(img.getpixel((x, 48))[3] for x in range(16, 40))
            self.assertGreater(ring, 150)          # ring stroke clearly visible
            self.assertLess(center, ring)          # middle stays dimmer than the ring

    def test_sheet_text_cell_ink_option_colors_the_glyph_core(self):
        # Status-panel digit strips need visible color hierarchy (levels gold,
        # rates green). The default near-white core with a soft halo reads as
        # plain white at small sizes; "ink" tints the glyph core itself.
        manifest = self._manifest({"Graphics/gold.png": {
            "width": 40, "height": 40, "optional": False,
            "recipe": {"type": "sheet", "cells": [
                {"text": "8", "x": 0, "y": 0, "w": 40, "h": 40, "size": 0.85,
                 "color": "#FACC15", "ink": True}]}}})
        skingen.compose(manifest, self.source, self.out)
        with Image.open(os.path.join(self.out, "Graphics", "gold.png")) as img:
            core = max((px for px in img.getdata() if px[3] > 200),
                       key=lambda px: sum(px[:3]))
            self.assertGreater(core[0], 180)            # bright...
            self.assertGreater(core[0] - core[2], 60)   # ...and clearly gold, not white

    def test_sheet_text_cell_fit_option_gives_uniform_digit_size(self):
        # Digit strips must render every digit at the SAME size. Without a
        # shared reference, the fitting loop shrinks wide digits ('8') to the
        # cell width while narrow '1' keeps the full size fraction — a visibly
        # oversized '1'. "fit" sizes the glyph against reference characters.
        def ink_rows(name, cell):
            manifest = self._manifest({f"Graphics/{name}": {
                "width": 12, "height": 20, "optional": False,
                "recipe": {"type": "sheet", "cells": [cell]}}})
            skingen.compose(manifest, self.source, self.out)
            with Image.open(os.path.join(self.out, "Graphics", name)) as img:
                return sum(
                    1 for y in range(20)
                    if any(img.getpixel((x, y))[3] > 100 for x in range(12)))

        digits = "0123456789"
        one = ink_rows("fit_one.png", {"text": "1", "x": 0, "y": 0, "w": 12, "h": 20,
                                       "size": 0.88, "fit": digits})
        eight = ink_rows("fit_eight.png", {"text": "8", "x": 0, "y": 0, "w": 12, "h": 20,
                                           "size": 0.88, "fit": digits})
        self.assertLessEqual(abs(one - eight), 1)

    def test_sheet_text_cell_anchor_bottom_sits_glyph_on_baseline(self):
        # The '.' glyph in digit strips must sit at the bottom of its cell —
        # centered it renders as a floating middle dot ("8·70").
        manifest = self._manifest({"Graphics/dot.png": {
            "width": 20, "height": 56, "optional": False,
            "recipe": {"type": "sheet", "cells": [
                {"text": ".", "x": 0, "y": 0, "w": 20, "h": 56, "anchor": "bottom"}]}}})
        skingen.compose(manifest, self.source, self.out)
        with Image.open(os.path.join(self.out, "Graphics", "dot.png")) as img:
            top_ink = sum(1 for y in range(28) for x in range(20) if img.getpixel((x, y))[3] > 100)
            bottom_ink = sum(1 for y in range(28, 56) for x in range(20) if img.getpixel((x, y))[3] > 100)
            self.assertEqual(top_ink, 0)             # upper half stays empty
            self.assertGreater(bottom_ink, 0)        # dot sits near the baseline


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
