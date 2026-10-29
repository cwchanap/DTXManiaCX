import os
import re
import tempfile
import unittest

import practicegen


class PatternTests(unittest.TestCase):
    def test_bar_rows_should_encode_hits_with_the_lane_channel(self):
        rows = list(practicegen.bar_rows({"BD": "x... x...", "HH": "x.x. x.x."}))

        # Rows follow CHANNELS order (HH before BD), not the pattern's order.
        self.assertEqual(rows, [
            ("11", "1100110011001100"),
            ("13", "1300000013000000"),
        ])

    def test_bar_rows_should_reject_lanes_on_different_grids(self):
        with self.assertRaises(ValueError):
            list(practicegen.bar_rows({"HH": "x.x.", "SD": "x.x.x."}))

    def test_bar_rows_should_reject_unknown_lanes_and_cells(self):
        with self.assertRaises(ValueError):
            list(practicegen.bar_rows({"XX": "x..."}))
        with self.assertRaises(ValueError):
            list(practicegen.bar_rows({"SD": "x.o."}))

    def test_with_crash_should_replace_the_downbeat_timekeeping_hit(self):
        crashed = practicegen.with_crash({"HH": "x.x.", "BD": "x..."})

        self.assertEqual(crashed, {"HH": "..x.", "BD": "x...", "CY": "x..."})

    def test_phrase_should_crash_cycle_grooves_and_end_on_the_fill(self):
        groove_a, groove_b, fill = {"BD": "x."}, {"SD": ".x"}, {"FT": "xx"}

        bars = practicegen.phrase((groove_a, groove_b), fill)

        self.assertEqual(len(bars), 8)
        self.assertEqual(bars[0], {"BD": "x.", "CY": "x."})
        self.assertEqual(bars[1:7], [groove_b, groove_a] * 3)
        self.assertEqual(bars[7], fill)


class ChartTests(unittest.TestCase):
    def test_every_bar_should_click_the_metronome_with_an_accented_downbeat(self):
        for song in practicegen.SONGS:
            with self.subTest(song.slug):
                lines = practicegen.render_chart(song).splitlines()
                clicks = [line for line in lines if re.match(r"#\d{3}01:", line)]

                bar_count = practicegen.COUNT_IN_BARS + len(song.bars) + 1
                self.assertEqual(clicks, ["#%03d01: 01020202" % bar for bar in range(bar_count)])

    def test_count_in_bars_should_hold_only_the_metronome(self):
        lines = practicegen.render_chart(practicegen.SONGS[0]).splitlines()
        count_in = ["#%03d" % bar for bar in range(practicegen.COUNT_IN_BARS)]

        drum_rows = [line for line in lines
                     if re.match(r"#\d{5}:", line) and line[:4] in count_in and line[4:6] != "01"]

        self.assertEqual(drum_rows, [])

    def test_drum_chips_should_never_reuse_a_click_wav_id(self):
        click_ids = {wav_id for wav_id, _, _ in practicegen.CLICKS}

        self.assertFalse(click_ids & set(practicegen.CHANNELS.values()))


class BundleTests(unittest.TestCase):
    def test_build_should_write_box_def_charts_and_clicks(self):
        with tempfile.TemporaryDirectory() as scratch:
            output_dir = os.path.join(scratch, "Practice")

            practicegen.build(output_dir)

            files = practicegen.read_tree(output_dir)
        expected = {"box.def"}
        for song in practicegen.SONGS:
            expected |= {song.slug + "/" + song.slug + ".dtx",
                         song.slug + "/click-accent.ogg",
                         song.slug + "/click.ogg"}
        self.assertEqual(set(files), expected)

    def test_stale_files_should_report_edited_and_missing_files(self):
        with tempfile.TemporaryDirectory() as scratch:
            output_dir = os.path.join(scratch, "Practice")
            practicegen.build(output_dir)
            song = practicegen.SONGS[0]
            with open(os.path.join(output_dir, song.slug, song.slug + ".dtx"), "a") as handle:
                handle.write("#99913: 13\n")
            os.remove(os.path.join(output_dir, "box.def"))

            stale = practicegen.stale_files(output_dir)

        self.assertEqual(stale, [song.slug + "/" + song.slug + ".dtx", "box.def"])

    def test_committed_bundle_should_match_the_generator(self):
        stale = practicegen.stale_files()

        self.assertEqual(
            stale, [],
            "run `python3 tools/practicegen/practicegen.py generate` and commit the result")


if __name__ == "__main__":
    unittest.main()
