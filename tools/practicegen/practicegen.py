#!/usr/bin/env python3
"""Generate the bundled metronome-only practice charts.

Every chart is a plain DTX file whose only audio is a metronome click on BGM
channel 01 (accent on beat 1, plain click on beats 2-4), reusing the CX Neon
metronome sounds. Drum chips use their channel number as the WAV id (11 = HH,
12 = SD, ...) with no #WAV definition, so hits stay silent (the game treats
undefined WAV ids as silent) until a practice kit adds #WAVxx lines.

Usage:
    python3 tools/practicegen/practicegen.py generate   # rewrite the bundle
    python3 tools/practicegen/practicegen.py check      # exit 1 if it is stale

Patterns are one string per lane on a fixed grid per bar: 'x' is a hit, '.'
a rest, and spaces are ignored. 16 steps are 16th notes, 8 steps are 8th
notes and 12 steps are swung triplet 8ths. Every lane in a bar uses the same
grid; a 1-step lane hits on the downbeat only.
"""
import argparse
import os
import shutil
import sys
import tempfile
from dataclasses import dataclass

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUTPUT_DIR = os.path.join(REPO_ROOT, "DTXMania.Game", "BundledSongs", "Practice")
CLICK_SOURCE_DIR = os.path.join(REPO_ROOT, "System", "CXNeon", "Sounds")

# (WAV id, file written beside each chart, CX Neon source file)
ACCENT_CLICK = ("01", "click-accent.ogg", "Metronome Accent.ogg")
BEAT_CLICK = ("02", "click.ogg", "Metronome Beat.ogg")
CLICKS = (ACCENT_CLICK, BEAT_CLICK)
BEATS_PER_BAR = 4
COUNT_IN_BARS = 2

# DTX drum channel per lane, in the order rows are written.
CHANNELS = {
    "LC": "1A",  # left crash
    "HH": "11",  # hi-hat closed
    "HO": "18",  # hi-hat open
    "LP": "1B",  # hi-hat foot pedal
    "SD": "12",
    "HT": "14",
    "BD": "13",
    "LT": "15",
    "FT": "17",
    "CY": "16",
    "RD": "19",
}
TIMEKEEPING_LANES = ("HH", "HO", "RD")

BOX_DEF = "#TITLE: Practice\n#GENRE: Practice\n"


def steps(row):
    """Returns a lane row's cells with the readability spaces removed."""
    cells = row.replace(" ", "")
    unknown = set(cells) - {"x", "."}
    if not cells or unknown:
        raise ValueError("lane row %r must be non-empty 'x'/'.' cells" % row)
    return cells


def bar_rows(pattern):
    """Yields (channel, DTX pairs) per lane of one bar, in CHANNELS order."""
    unknown = set(pattern) - set(CHANNELS)
    if unknown:
        raise ValueError("unknown lanes %s" % sorted(unknown))
    grids = {len(steps(row)) for row in pattern.values()}
    if len(grids) > 1:
        raise ValueError("lanes in one bar use different grids %s: %r" % (sorted(grids), pattern))
    for lane, channel in CHANNELS.items():
        if lane in pattern:
            yield channel, "".join(channel if cell == "x" else "00" for cell in steps(pattern[lane]))


def with_crash(pattern):
    """Starts a phrase: a crash replaces the time-keeping hit on the downbeat."""
    grid = len(steps(next(iter(pattern.values()))))
    result = dict(pattern)
    for lane in TIMEKEEPING_LANES:
        if lane in result:
            result[lane] = "." + steps(result[lane])[1:]
    result["CY"] = "x" + steps(result.get("CY", "." * grid))[1:]
    return result


def phrase(grooves, fill, bars=8):
    """A phrase: crash on its downbeat, grooves cycled, and a fill in its last bar."""
    if isinstance(grooves, dict):
        grooves = (grooves,)
    body = [grooves[index % len(grooves)] for index in range(bars - 1)]
    return [with_crash(body[0])] + body[1:] + [fill]


ENDING = {"CY": "x", "BD": "x"}


@dataclass(frozen=True)
class Song:
    slug: str  # song folder and chart file name; the prefix orders the box
    title: str
    bpm: int
    dlevel: int
    comment: str
    bars: tuple


POP_GROOVE = {
    "HH": "x.x. x.x. x.x. x.x.",
    "SD": ".... x... .... x...",
    "BD": "x... .... x... ....",
}
POP_GROOVE_B = {**POP_GROOVE, "BD": "x... .... x.x. ...."}
POP_FILL = {
    "HH": "x.x. x.x. .... ....",
    "SD": ".... x... x.x. ....",
    "HT": ".... .... .... x...",
    "FT": ".... .... .... ..x.",
    "BD": "x... .... .... ....",
}
POP_FILL_16 = {**POP_FILL, "SD": ".... x... xxxx ....", "HT": ".... .... .... xx..", "FT": ".... .... .... ..xx"}

JAZZ_RIDE = {
    "RD": "x.. x.x x.. x.x",
    "LP": "... x.. ... x..",
}
JAZZ_FEATHER = {**JAZZ_RIDE, "BD": "x.. x.. x.. x.."}
JAZZ_COMP_A = {**JAZZ_RIDE, "SD": "... ..x ... ..."}
JAZZ_COMP_B = {**JAZZ_RIDE, "SD": "..x ... ..x ..."}
JAZZ_COMP_FEATHER_A = {**JAZZ_COMP_A, "BD": JAZZ_FEATHER["BD"]}
JAZZ_COMP_FEATHER_B = {**JAZZ_COMP_B, "BD": JAZZ_FEATHER["BD"]}
JAZZ_FILL = {
    "RD": "x.. x.x ... ...",
    "LP": "... x.. ... x..",
    "SD": "... ... xxx xxx",
}

PUNK_GROOVE = {
    "HH": "xxxx xxxx",
    "SD": "..x. ..x.",
    "BD": "x... x...",
}
PUNK_GROOVE_B = {**PUNK_GROOVE, "BD": "x... xx.."}
PUNK_SKANK = {
    "RD": "x.x. x.x.",
    "SD": ".x.x .x.x",
    "BD": "x.x. x.x.",
}
PUNK_FILL = {
    "HH": "xxxx ....",
    "SD": "..x. xxxx",
    "BD": "x... ....",
}

METAL_EIGHTHS = {
    "HH": "x.x. x.x. x.x. x.x.",
    "SD": ".... x... .... x...",
    "BD": "x.x. x.x. x.x. x.x.",
}
METAL_GALLOP = {
    "RD": "x... x... x... x...",
    "SD": ".... x... .... x...",
    "BD": "x.xx x.xx x.xx x.xx",
}
METAL_SIXTEENTHS = {
    "CY": "x... x... x... x...",
    "SD": ".... x... .... x...",
    "BD": "xxxx xxxx xxxx xxxx",
}
METAL_FILL = {
    "HH": "x.x. x.x. .... ....",
    "SD": ".... x... xxxx ....",
    "HT": ".... .... .... xx..",
    "FT": ".... .... .... ..xx",
    "BD": "x.x. x.x. x.x. x.x.",
}

SONGS = (
    Song(
        slug="01-pop-8-beat",
        title="Pop 8-Beat",
        bpm=100,
        dlevel=15,
        comment="Metronome-only practice: 8th-note hi-hat 8-beat with 8th and 16th fills.",
        bars=tuple(
            phrase(POP_GROOVE, POP_FILL)
            + phrase(POP_GROOVE_B, POP_FILL)
            + phrase(POP_GROOVE, POP_FILL_16)
            + phrase(POP_GROOVE_B, POP_FILL_16)),
    ),
    Song(
        slug="02-jazz-swing",
        title="Jazz Swing",
        bpm=120,
        dlevel=25,
        comment="Metronome-only practice: swung ride, hi-hat foot on 2 and 4, feathered kick, snare comping.",
        bars=tuple(
            phrase(JAZZ_RIDE, JAZZ_FILL)
            + phrase(JAZZ_FEATHER, JAZZ_FILL)
            + phrase((JAZZ_COMP_A, JAZZ_COMP_B), JAZZ_FILL)
            + phrase((JAZZ_COMP_FEATHER_A, JAZZ_COMP_FEATHER_B), JAZZ_FILL)),
    ),
    Song(
        slug="03-punk-beat",
        title="Punk Beat",
        bpm=170,
        dlevel=35,
        comment="Metronome-only practice: fast 8th-note punk beat and the skank (polka) beat.",
        bars=tuple(
            phrase(PUNK_GROOVE, PUNK_FILL)
            + phrase(PUNK_GROOVE_B, PUNK_FILL)
            + phrase(PUNK_SKANK, PUNK_FILL)
            + phrase(PUNK_GROOVE, PUNK_FILL)
            + phrase(PUNK_SKANK, PUNK_FILL)),
    ),
    Song(
        slug="04-metal-double-bass",
        title="Metal Double Bass",
        bpm=130,
        dlevel=45,
        comment="Metronome-only practice: 8th-note double bass, gallop, then 16th-note double bass.",
        bars=tuple(
            phrase(METAL_EIGHTHS, METAL_FILL)
            + phrase(METAL_GALLOP, METAL_FILL)
            + phrase(METAL_SIXTEENTHS, METAL_FILL)
            + phrase(METAL_SIXTEENTHS, METAL_FILL)),
    ),
)


def render_chart(song):
    """Returns the DTX text of one practice chart."""
    lines = [
        "; Generated by tools/practicegen/practicegen.py - edit the generator, not this file.",
        "#TITLE: " + song.title,
        "#ARTIST: DTXManiaCX",
        "#GENRE: Practice",
        "#COMMENT: " + song.comment,
        "#BPM: %d" % song.bpm,
        "#DLEVEL: %d" % song.dlevel,
        "",
    ]
    lines += ["#WAV%s: %s" % (wav_id, name) for wav_id, name, _ in CLICKS]
    metronome = ACCENT_CLICK[0] + BEAT_CLICK[0] * (BEATS_PER_BAR - 1)
    bars = [{}] * COUNT_IN_BARS + list(song.bars) + [ENDING]
    for number, pattern in enumerate(bars):
        lines.append("")
        lines.append("#%03d01: %s" % (number, metronome))
        lines += ["#%03d%s: %s" % (number, channel, pairs) for channel, pairs in bar_rows(pattern)]
    return "\n".join(lines) + "\n"


def write_text(path, text):
    # ASCII keeps the charts readable under any DTX encoding guess; LF matches .gitattributes.
    with open(path, "w", encoding="ascii", newline="\n") as handle:
        handle.write(text)


def build(output_dir):
    """Writes the whole practice bundle into output_dir, which must not exist yet."""
    os.makedirs(output_dir)
    write_text(os.path.join(output_dir, "box.def"), BOX_DEF)
    for song in SONGS:
        folder = os.path.join(output_dir, song.slug)
        os.makedirs(folder)
        write_text(os.path.join(folder, song.slug + ".dtx"), render_chart(song))
        for _, name, source in CLICKS:
            shutil.copyfile(os.path.join(CLICK_SOURCE_DIR, source), os.path.join(folder, name))


def read_tree(root):
    """Returns {relative path: bytes} for every file under root ({} if it is missing)."""
    files = {}
    for directory, _, names in os.walk(root):
        for name in names:
            path = os.path.join(directory, name)
            with open(path, "rb") as handle:
                files[os.path.relpath(path, root).replace(os.sep, "/")] = handle.read()
    return files


def stale_files(output_dir=OUTPUT_DIR):
    """Returns the bundle paths that differ from a fresh build, sorted."""
    with tempfile.TemporaryDirectory() as scratch:
        expected_dir = os.path.join(scratch, "Practice")
        build(expected_dir)
        expected, actual = read_tree(expected_dir), read_tree(output_dir)
    return sorted(path for path in expected.keys() | actual.keys() if expected.get(path) != actual.get(path))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("command", choices=("generate", "check"))
    args = parser.parse_args(argv)

    if args.command == "generate":
        if os.path.isdir(OUTPUT_DIR):
            shutil.rmtree(OUTPUT_DIR)
        build(OUTPUT_DIR)
        print("wrote %d practice charts to %s" % (len(SONGS), os.path.relpath(OUTPUT_DIR, REPO_ROOT)))
        return 0

    stale = stale_files()
    if stale:
        print("practice bundle is stale; run `python3 tools/practicegen/practicegen.py generate`:", file=sys.stderr)
        for path in stale:
            print("  " + path, file=sys.stderr)
        return 1
    print("practice bundle is up to date")
    return 0


if __name__ == "__main__":
    sys.exit(main())
