#!/usr/bin/env python3
"""Generate CX Neon source art into tools/skingen/source/ and wire copy recipes.

Produces original (non-Gitadora) synthwave UI assets matching STYLE.md tokens and
NX sheet metrics used by the game. Run:

  python3 tools/skingen/generate_source.py
  python3 tools/skingen/skingen.py compose
  python3 tools/skingen/skingen.py validate
"""
from __future__ import annotations

import json
import math
import os
import random
from typing import Callable, Dict, List, Optional, Sequence, Tuple

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SOURCE_ROOT = os.path.join(HERE, "source")
MANIFEST_PATH = os.path.join(HERE, "manifest.json")

# Palette (STYLE.md)
BG = (15, 23, 42, 255)          # #0F172A
PANEL = (30, 41, 59, 235)       # #1E293B ~92%
CYAN = (34, 211, 238, 255)      # #22D3EE
MAGENTA = (232, 121, 249, 255)  # #E879F9
SUCCESS = (34, 197, 94, 255)    # #22C55E
DANGER = (239, 68, 68, 255)     # #EF4444
TEXT = (241, 245, 249, 255)     # #F1F5F9
WHITE = (255, 255, 255, 255)
BLACK = (0, 0, 0, 255)
TRANSPARENT = (0, 0, 0, 0)

LANE_COLORS = [
    (160, 64, 255, 255),   # LC purple
    (255, 200, 0, 255),    # HH yellow
    (255, 96, 255, 255),   # LP magenta
    (255, 64, 64, 255),    # SN red
    (0, 200, 255, 255),    # HT cyan-blue
    (255, 128, 0, 255),    # BD orange
    (0, 128, 255, 255),    # LT blue
    (0, 255, 128, 255),    # FT green
    (255, 100, 200, 255),  # CY pink
    (255, 100, 200, 255),  # RD pink
]

CHIP_WIDTHS = [70, 58, 64, 56, 56, 56, 74, 48, 58, 74, 48, 58]
CHIP_HUES = [
    (255, 255, 255, 255),  # BD white-ish
    (255, 100, 200, 255),  # RD
    (255, 64, 64, 255),    # SN
    (0, 200, 255, 255),    # HT
    (0, 128, 255, 255),    # LT
    (0, 255, 128, 255),    # FT
    (255, 200, 0, 255),    # CY
    (255, 200, 0, 255),    # HH close
    (160, 64, 255, 255),   # LC-ish
    (160, 64, 255, 255),   # LC crash
    (255, 200, 0, 255),    # HH open
    (255, 96, 255, 255),   # LP
]

# Lane strip positions for 7_Paret (from PerformanceUILayout)
LANE_LEFT = [295, 367, 416, 467, 524, 573, 642, 691, 745, 815]
LANE_WIDTHS = [72, 49, 51, 57, 49, 69, 49, 54, 70, 38]


def _font(size: int, bold: bool = True) -> ImageFont.FreeTypeFont:
    candidates = [
        "/System/Library/Fonts/Supplemental/Arial Bold.ttf" if bold else "/System/Library/Fonts/Supplemental/Arial.ttf",
        "/System/Library/Fonts/Supplemental/Arial.ttf",
        "/Library/Fonts/Arial.ttf",
        "/System/Library/Fonts/Helvetica.ttc",
        "/System/Library/Fonts/SFNSMono.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
    ]
    for path in candidates:
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size)
            except Exception:
                continue
    return ImageFont.load_default()


# When False, existing source files (e.g. AI-polished backgrounds) are kept.
FORCE_OVERWRITE = False


# Always regenerate these layout-sensitive sheets even when FORCE_OVERWRITE is
# off — corrected digit placement must not be skipped by a stale AI/hand file.
_ALWAYS_REGENERATE = frozenset({
    "5_bpm font.png",
    "7_lag numbers.png",
})


def _save(img: Image.Image, rel: str) -> str:
    """Save under SOURCE_ROOT using a path that mirrors Graphics/ for copy recipes."""
    # strip Graphics/ prefix for source path cleanliness
    assert rel.startswith("Graphics/")
    out_rel = rel[len("Graphics/"):]
    path = os.path.join(SOURCE_ROOT, out_rel)
    os.makedirs(os.path.dirname(path) if os.path.dirname(path) else SOURCE_ROOT, exist_ok=True)
    if os.path.isfile(path) and not FORCE_OVERWRITE and out_rel not in _ALWAYS_REGENERATE:
        return out_rel
    # Ensure parent for nested? all are flat under Graphics/
    if img.mode != "RGBA" and not rel.lower().endswith((".jpg", ".jpeg")):
        img = img.convert("RGBA")
    if rel.lower().endswith((".jpg", ".jpeg")):
        img.convert("RGB").save(path, quality=92)
    else:
        img.save(path)
    return out_rel


def _rgba(c, a=None):
    if len(c) == 4 and a is None:
        return c
    return (c[0], c[1], c[2], a if a is not None else (c[3] if len(c) > 3 else 255))


def _blend_color(a, b, t: float):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3)) + (255,)


def new_rgba(w: int, h: int, color=TRANSPARENT) -> Image.Image:
    return Image.new("RGBA", (max(1, w), max(1, h)), color)


def draw_chamfered_rect(draw: ImageDraw.ImageDraw, box, fill, outline=None, outline_width=1, chamfer=8):
    x0, y0, x1, y1 = box
    c = min(chamfer, (x1 - x0) // 3, (y1 - y0) // 3)
    pts = [
        (x0 + c, y0), (x1 - c, y0), (x1, y0 + c), (x1, y1 - c),
        (x1 - c, y1), (x0 + c, y1), (x0, y1 - c), (x0, y0 + c),
    ]
    draw.polygon(pts, fill=fill)
    if outline is not None:
        draw.line(pts + [pts[0]], fill=outline, width=outline_width)


def add_glow(base: Image.Image, color, radius=6, strength=0.7) -> Image.Image:
    """Soft outer glow from non-transparent pixels."""
    alpha = base.split()[-1]
    glow = Image.new("RGBA", base.size, (0, 0, 0, 0))
    g = Image.new("RGBA", base.size, (*color[:3], int(255 * strength)))
    g.putalpha(alpha)
    g = g.filter(ImageFilter.GaussianBlur(radius))
    return Image.alpha_composite(g, base)


def panel(w, h, fill=PANEL, edge=CYAN, chamfer=10, glow=True) -> Image.Image:
    img = new_rgba(w, h)
    draw = ImageDraw.Draw(img)
    inset = 2 if min(w, h) > 8 else 0
    draw_chamfered_rect(draw, (inset, inset, w - 1 - inset, h - 1 - inset), fill, edge, 1, chamfer)
    # faint inner grid
    step = 16
    grid = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    gd = ImageDraw.Draw(grid)
    for x in range(inset + 4, w - inset, step):
        gd.line([(x, inset + 2), (x, h - inset - 2)], fill=(34, 211, 238, 18), width=1)
    for y in range(inset + 4, h - inset, step):
        gd.line([(inset + 2, y), (w - inset - 2, y)], fill=(34, 211, 238, 14), width=1)
    img = Image.alpha_composite(img, grid)
    if glow and min(w, h) >= 12:
        img = add_glow(img, CYAN, radius=3, strength=0.35)
    return img


def bar_plate(w, h, selected=False, folder=False) -> Image.Image:
    edge = CYAN if selected else (34, 211, 238, 120)
    fill = (30, 41, 59, 240) if selected else (30, 41, 59, 200)
    img = panel(w, h, fill=fill, edge=edge, chamfer=6, glow=selected)
    draw = ImageDraw.Draw(img)
    # left accent
    accent = MAGENTA if selected else CYAN
    draw.rectangle([2, 4, 6, h - 5], fill=accent)
    if folder:
        # small folder glyph zone
        draw.rectangle([12, h // 2 - 8, 28, h // 2 + 8], outline=CYAN, width=1)
        draw.rectangle([12, h // 2 - 12, 22, h // 2 - 6], fill=CYAN)
    return img


def stage_background(w, h, variant: str) -> Image.Image:
    img = Image.new("RGBA", (w, h), BG)
    draw = ImageDraw.Draw(img)
    # vignette-ish gradient top->bottom
    for y in range(h):
        t = y / max(1, h - 1)
        if variant == "startup":
            c = _blend_color((8, 12, 22), (15, 23, 42), t)
        elif variant == "title":
            c = _blend_color((12, 18, 36), (20, 10, 40), t)
        elif variant == "config":
            c = _blend_color((12, 18, 32), (15, 23, 42), t)
        elif variant == "song":
            c = _blend_color((10, 20, 40), (25, 15, 45), t)
        elif variant == "transition":
            c = _blend_color((8, 16, 36), (30, 12, 40), t)
        elif variant == "performance":
            c = _blend_color((8, 10, 18), (12, 16, 28), t)
        elif variant == "result":
            c = _blend_color((12, 18, 36), (28, 12, 40), t)
        elif variant == "failed":
            c = _blend_color((20, 8, 12), (40, 10, 16), t)
        else:
            c = BG
        draw.line([(0, y), (w, y)], fill=c)

    # horizon
    horizon = int(h * 0.62)
    grid_alpha = {
        "startup": 0, "title": 90, "config": 40, "song": 80,
        "transition": 70, "performance": 25, "result": 60, "failed": 40,
    }.get(variant, 50)

    if grid_alpha > 0:
        # perspective floor grid
        for i in range(1, 18):
            y = horizon + int((h - horizon) * (i / 18.0) ** 1.6)
            a = int(grid_alpha * (1 - i / 20))
            draw.line([(0, y), (w, y)], fill=(34, 211, 238, a), width=1)
        vanishing = (w // 2, horizon)
        for i in range(-12, 13):
            x_bottom = w // 2 + i * (w // 10)
            a = int(grid_alpha * 0.7)
            draw.line([vanishing, (x_bottom, h)], fill=(34, 211, 238, a), width=1)
        # horizon glow
        for r in range(40, 0, -2):
            a = int(30 * (1 - r / 40))
            col = (34, 211, 238, a) if variant != "title" else (232, 121, 249, a)
            draw.ellipse([w // 2 - 200 - r, horizon - r // 3, w // 2 + 200 + r, horizon + r // 3],
                         outline=col)

    if variant == "title":
        # magenta sun disc
        sun_r = 90
        cx, cy = w // 2, horizon + 10
        for r in range(sun_r, 0, -2):
            t = 1 - r / sun_r
            col = (*_blend_color(MAGENTA, (255, 180, 80), t)[:3], 220)
            draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=col)
        # scanline cuts
        for y in range(cy - sun_r // 3, cy + sun_r, 6):
            draw.line([(cx - sun_r, y), (cx + sun_r, y)], fill=(15, 23, 42, 180), width=2)

    if variant == "startup":
        # single thin cyan horizon
        draw.line([(w // 4, horizon), (3 * w // 4, horizon)], fill=CYAN, width=2)

    if variant == "result":
        # upward rays
        for i in range(-8, 9):
            x = w // 2 + i * 40
            draw.line([(w // 2, h), (x, 0)], fill=(34, 211, 238, 18), width=3)
        rng = random.Random(42)
        for _ in range(40):
            x, y = rng.randint(0, w - 1), rng.randint(0, h - 1)
            r = rng.randint(1, 3)
            draw.ellipse([x - r, y - r, x + r, y + r], fill=(232, 121, 249, rng.randint(40, 120)))

    if variant == "transition":
        for i in range(20):
            y = 40 + i * 32
            draw.line([(0, y), (w, y + 8)], fill=(34, 211, 238, 25), width=2)

    if variant == "song":
        rng = random.Random(7)
        for _ in range(12):
            x, y = rng.randint(50, w - 100), rng.randint(40, h // 2)
            ww, hh = rng.randint(40, 120), rng.randint(20, 60)
            draw.rounded_rectangle([x, y, x + ww, y + hh], radius=6, outline=(34, 211, 238, 50), width=1)

    if variant == "failed":
        # glitch scanlines
        for y in range(0, h, 4):
            draw.line([(0, y), (w, y)], fill=(0, 0, 0, 40), width=1)
        draw.rectangle([w // 2 - 200, h // 2 - 40, w // 2 + 200, h // 2 + 40], outline=DANGER, width=2)

    # scanlines overlay
    scan = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    sd = ImageDraw.Draw(scan)
    for y in range(0, h, 3):
        sd.line([(0, y), (w, y)], fill=(0, 0, 0, 18), width=1)
    img = Image.alpha_composite(img, scan)
    return img


def draw_text_centered(draw, box, text, font, fill=TEXT, glow_color=None):
    x0, y0, x1, y1 = box
    bbox = draw.textbbox((0, 0), text, font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    x = x0 + (x1 - x0 - tw) // 2 - bbox[0]
    y = y0 + (y1 - y0 - th) // 2 - bbox[1]
    if glow_color:
        for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, 1)):
            draw.text((x + dx, y + dy), text, font=font, fill=glow_color)
    draw.text((x, y), text, font=font, fill=fill)


def digit_sheet(glyphs: Sequence[Tuple[str, int]], cell_h: int, font_size: int,
                fill=TEXT, glow=CYAN) -> Image.Image:
    total_w = sum(w for _, w in glyphs)
    img = new_rgba(total_w, cell_h)
    draw = ImageDraw.Draw(img)
    font = _font(font_size, bold=True)
    x = 0
    for ch, w in glyphs:
        # glow layer
        for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            draw_text_centered(draw, (x + dx, dy, x + w + dx, cell_h + dy), ch, font, fill=(*glow[:3], 120))
        draw_text_centered(draw, (x, 0, x + w, cell_h), ch, font, fill=fill)
        x += w
    return img


def console_font(dimmed=False) -> Image.Image:
    """256x256 sheet of 8x16 cells, 32 cols x 16 rows (ASCII order)."""
    img = new_rgba(256, 256)
    draw = ImageDraw.Draw(img)
    font = _font(10, bold=False)
    fill = (241, 245, 249, 160 if dimmed else 255)
    for code in range(256):
        col, row = code % 32, code // 32
        x, y = col * 8, row * 16
        ch = chr(code) if 32 <= code < 127 else ("." if code == 0 else "")
        if not ch:
            continue
        # tiny glyph
        try:
            draw.text((x + 1, y + 2), ch, font=font, fill=fill)
        except Exception:
            pass
    return img


def effect_burst(w, h, color=CYAN, style="radial") -> Image.Image:
    img = new_rgba(w, h)
    draw = ImageDraw.Draw(img)
    cx, cy = w // 2, h // 2
    if style == "radial":
        max_r = min(w, h) // 2
        for r in range(max_r, 0, -1):
            t = r / max_r
            a = int(255 * (1 - t) ** 0.5)
            core = _blend_color(WHITE, color, t)
            draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(*core[:3], a))
        # rays
        for i in range(12):
            ang = i * math.pi / 6
            x2 = cx + int(math.cos(ang) * max_r)
            y2 = cy + int(math.sin(ang) * max_r)
            draw.line([(cx, cy), (x2, y2)], fill=(*color[:3], 160), width=2)
    elif style == "flame":
        max_r = min(w, h) // 2
        for i in range(max_r, 0, -1):
            t = i / max_r
            a = int(220 * (1 - t))
            ww = int((w * 0.35) * (1 - t * 0.3))
            hh = int(h * 0.5 * t + 4)
            col = _blend_color(WHITE, color, t * 0.8)
            draw.ellipse([cx - ww, cy - hh - i // 2, cx + ww, cy + hh // 3 - i // 3],
                         fill=(*col[:3], a))
    elif style == "star":
        pts = []
        for i in range(8):
            ang = i * math.pi / 4 - math.pi / 2
            r = min(w, h) // 2 - 1 if i % 2 == 0 else min(w, h) // 5
            pts.append((cx + int(math.cos(ang) * r), cy + int(math.sin(ang) * r)))
        draw.polygon(pts, fill=(*color[:3], 230))
        # hot core
        r = max(2, min(w, h) // 8)
        draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=WHITE)
    elif style == "ring":
        max_r = min(w, h) // 2 - 1
        for r in range(max_r, max(0, max_r - 6), -1):
            a = int(200 * ((r - (max_r - 6)) / 6))
            draw.ellipse([cx - r, cy - r, cx + r, cy + r], outline=(*color[:3], a), width=2)
        draw.ellipse([cx - 3, cy - 3, cx + 3, cy + 3], fill=WHITE)
    elif style == "wave":
        max_r = min(w, h) // 2 - 1
        for r in (max_r, max_r * 2 // 3, max_r // 3):
            draw.ellipse([cx - r, cy - r, cx + r, cy + r], outline=(*color[:3], 180), width=2)
    return img


def hueshift_rgba(img: Image.Image, degrees: float) -> Image.Image:
    offset = int(degrees / 360.0 * 255) % 256
    rgba = img.convert("RGBA")
    alpha = rgba.getchannel("A")
    h, s, v = rgba.convert("RGB").convert("HSV").split()
    h = h.point(lambda value: (value + offset) % 256)
    shifted = Image.merge("HSV", (h, s, v)).convert("RGBA")
    shifted.putalpha(alpha)
    return shifted


def word_plate(w, h, text, color=CYAN, bg_alpha=200) -> Image.Image:
    img = panel(w, h, fill=(30, 41, 59, bg_alpha), edge=color, chamfer=8, glow=True)
    draw = ImageDraw.Draw(img)
    size = max(10, min(h - 10, w // max(1, len(text)) - 2))
    font = _font(size, bold=True)
    draw_text_centered(draw, (0, 0, w, h), text, font, fill=TEXT, glow_color=(*color[:3], 180))
    return img


def rank_badge(letter: str, color) -> Image.Image:
    size = 274
    img = new_rgba(size, size)
    draw = ImageDraw.Draw(img)
    # shield
    cx, cy = size // 2, size // 2
    r = 110
    pts = [
        (cx, cy - r),
        (cx + r * 0.85, cy - r * 0.4),
        (cx + r * 0.75, cy + r * 0.5),
        (cx, cy + r),
        (cx - r * 0.75, cy + r * 0.5),
        (cx - r * 0.85, cy - r * 0.4),
    ]
    shield = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shield)
    sd.polygon(pts, fill=(30, 41, 59, 230), outline=color)
    shield = add_glow(shield, color, radius=12, strength=0.6)
    img = Image.alpha_composite(img, shield)
    draw = ImageDraw.Draw(img)
    font = _font(120 if len(letter) == 1 else 90, bold=True)
    draw_text_centered(draw, (0, 0, size, size), letter, font, fill=TEXT, glow_color=(*color[:3], 200))
    return img


def drumkit_piece(kind: str, w: int, h: int) -> Image.Image:
    img = new_rgba(w, h)
    draw = ImageDraw.Draw(img)
    cx, cy = w // 2, h // 2
    body = (40, 48, 64, 255)
    rim = CYAN
    if kind == "cymbal":
        for r, col in ((min(w, h) // 2 - 10, rim), (min(w, h) // 2 - 18, (50, 55, 70, 255)),
                       (min(w, h) // 2 - 40, (70, 75, 90, 255))):
            draw.ellipse([cx - r, cy - r // 3, cx + r, cy + r // 3], outline=col, width=3)
        draw.ellipse([cx - 6, cy - 4, cx + 6, cy + 4], fill=rim)
    elif kind == "hihat":
        for dy, rr in ((-12, min(w, h) // 2 - 20), (12, min(w, h) // 2 - 20)):
            draw.ellipse([cx - rr, cy + dy - rr // 4, cx + rr, cy + dy + rr // 4], outline=rim, width=3)
        draw.line([(cx, cy - 40), (cx, cy + 50)], fill=(80, 90, 110, 255), width=4)
    elif kind == "drum":
        rw, rh = w // 3, h // 4
        draw.ellipse([cx - rw, cy - rh, cx + rw, cy + rh], fill=body, outline=rim, width=4)
        draw.ellipse([cx - rw + 10, cy - rh + 8, cx + rw - 10, cy + rh - 8], outline=(*CYAN[:3], 120), width=2)
    elif kind == "kick":
        r = min(w, h) // 2 - 20
        draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=body, outline=rim, width=5)
        draw.ellipse([cx - r // 2, cy - r // 2, cx + r // 2, cy + r // 2], outline=(*CYAN[:3], 100), width=2)
    elif kind == "pedal":
        draw.polygon([(cx - 40, cy + 40), (cx + 40, cy + 40), (cx + 20, cy - 30), (cx - 20, cy - 30)],
                     fill=body, outline=rim)
        draw.ellipse([cx - 15, cy - 50, cx + 15, cy - 20], fill=rim)
    elif kind == "skeleton":
        # simple stands layout
        draw.line([(w * 0.2, h * 0.7), (w * 0.2, h * 0.35)], fill=(80, 90, 110, 255), width=4)
        draw.line([(w * 0.5, h * 0.75), (w * 0.5, h * 0.4)], fill=(80, 90, 110, 255), width=5)
        draw.line([(w * 0.8, h * 0.7), (w * 0.8, h * 0.3)], fill=(80, 90, 110, 255), width=4)
        draw.line([(w * 0.15, h * 0.75), (w * 0.85, h * 0.75)], fill=(60, 70, 90, 255), width=3)
        for x in (0.2, 0.5, 0.8):
            draw.ellipse([w * x - 8, h * 0.3 - 8, w * x + 8, h * 0.3 + 8], outline=CYAN, width=2)
    img = add_glow(img, CYAN, radius=4, strength=0.3)
    return img


def build_all() -> Dict[str, str]:
    """Return mapping Graphics/rel -> source relative path under source/."""
    random.seed(0)
    sources: Dict[str, str] = {}

    def put(rel: str, img: Image.Image):
        sources[rel] = _save(img, rel)

    # --- Stage backgrounds ---
    for rel, variant in [
        ("Graphics/1_background.jpg", "startup"),
        ("Graphics/2_background.jpg", "title"),
        ("Graphics/4_background.png", "config"),
        ("Graphics/5_background.jpg", "song"),
        ("Graphics/6_background.jpg", "transition"),
        ("Graphics/7_background.jpg", "performance"),
        ("Graphics/8_background.jpg", "result"),
        ("Graphics/7_stage_failed.jpg", "failed"),
    ]:
        put(rel, stage_background(1280, 720, variant))

    # Full combo overlay
    fc = stage_background(1280, 720, "result")
    d = ImageDraw.Draw(fc)
    d.rectangle([0, 0, 1280, 720], fill=(0, 0, 0, 100))
    plate = word_plate(500, 90, "FULL COMBO", color=(255, 215, 80, 255))
    fc.paste(plate, (390, 315), plate)
    put("Graphics/7_FullCombo.png", fc)

    # Danger tile / pause / shutter / covers
    danger = new_rgba(1280, 720)
    dd = ImageDraw.Draw(danger)
    for i in range(0, 40):
        a = int(80 + 40 * math.sin(i / 5))
        dd.rectangle([0, i * 2, 1280, i * 2 + 1], fill=(239, 68, 68, a))
    dd.rectangle([0, 0, 1280, 40], fill=(*DANGER[:3], 100))
    dd.rectangle([0, 680, 1280, 720], fill=(*DANGER[:3], 100))
    put("Graphics/7_Danger.png", danger)

    pause = new_rgba(1280, 720, (0, 0, 0, 160))
    plate = word_plate(360, 80, "PAUSED", color=CYAN)
    pause.paste(plate, (460, 320), plate)
    put("Graphics/7_pause_overlay.png", pause)

    put("Graphics/7_lanes_Cover_cls.png", panel(216, 720, fill=(15, 23, 42, 240), edge=CYAN, chamfer=0))
    put("Graphics/7_shutter.png", panel(558, 720, fill=(15, 23, 42, 245), edge=MAGENTA, chamfer=0))

    # Title menu
    menu = new_rgba(256, 256)
    row_h = 50
    for i, focused in enumerate([False, True, False]):
        p = panel(240, row_h - 4, fill=(30, 41, 59, 230 if not focused else 250),
                  edge=CYAN if focused else (34, 211, 238, 100), glow=focused)
        menu.paste(p, (8, 30 + i * row_h), p)
    put("Graphics/2_menu.png", menu)

    # Config panels
    put("Graphics/4_header panel.png", panel(1280, 105, chamfer=0))
    put("Graphics/4_footer panel.png", panel(1280, 30, chamfer=0))
    put("Graphics/4_menu panel.png", panel(180, 172))
    put("Graphics/4_menu cursor.png", panel(64, 25, fill=(34, 211, 238, 60), edge=CYAN, chamfer=4))
    put("Graphics/4_item bar.png", panel(18, 720, fill=(30, 41, 59, 180), edge=CYAN, chamfer=0, glow=True))
    put("Graphics/4_itembox.png", panel(538, 80, fill=(30, 41, 59, 180), edge=(34, 211, 238, 80), chamfer=6, glow=False))
    put("Graphics/4_itembox cursor.png", panel(497, 68, fill=(34, 211, 238, 40), edge=CYAN, chamfer=6))
    put("Graphics/4_Description Panel.png", panel(280, 360, fill=(30, 41, 59, 200)))

    # Song select
    put("Graphics/5_header panel.png", panel(1280, 105, chamfer=0))
    put("Graphics/5_footer panel.png", panel(1280, 30, chamfer=0))
    put("Graphics/5_status panel.png", panel(561, 342))
    put("Graphics/5_BPM.png", panel(187, 67))
    put("Graphics/5_skill point panel.png", panel(187, 64))
    put("Graphics/5_play history panel.png", panel(458, 151))
    put("Graphics/5_difficulty panel.png", panel(561, 321))
    put("Graphics/5_difficulty frame.png", panel(187, 60, fill=(34, 211, 238, 30), edge=CYAN, chamfer=4))
    put("Graphics/5_graph panel drums.png", panel(110, 321))
    put("Graphics/5_graph panel guitar bass.png", panel(110, 321))
    put("Graphics/5_comment bar.png", panel(720, 114))
    put("Graphics/5_preimage panel.png", panel(430, 313, chamfer=14))
    put("Graphics/5_scrollbar.png", panel(12, 504, fill=(30, 41, 59, 200), edge=CYAN, chamfer=2))

    put("Graphics/5_bar score.png", bar_plate(620, 48, selected=False))
    put("Graphics/5_bar score selected.png", bar_plate(640, 96, selected=True))
    put("Graphics/5_bar box.png", bar_plate(709, 60, selected=False, folder=True))
    put("Graphics/5_bar box selected.png", bar_plate(640, 150, selected=True, folder=True))
    put("Graphics/5_bar other.png", bar_plate(620, 48, selected=False))
    put("Graphics/5_bar other selected.png", bar_plate(640, 96, selected=True))

    # default jackets
    jacket = panel(400, 400, chamfer=20)
    jd = ImageDraw.Draw(jacket)
    cx, cy = 200, 200
    jd.ellipse([80, 80, 320, 320], outline=CYAN, width=4)
    jd.ellipse([160, 160, 240, 240], fill=MAGENTA)
    jd.ellipse([185, 185, 215, 215], fill=BG)
    put("Graphics/5_default_preview.png", jacket)
    put("Graphics/5_preimage default.png", jacket.copy())

    # Bitmap fonts
    put("Graphics/5_level number.png",
        digit_sheet([(str(i), 20) for i in range(10)] + [(".", 10), ("-", 20), ("?", 20)], 28, 22))
    put("Graphics/5_skill number.png",
        digit_sheet([(str(i), 12) for i in range(10)] + [(".", 6), ("%", 12)], 20, 14))
    put("Graphics/5_bpm font.png",
        digit_sheet([(str(i), 12) for i in range(10)] + [(":", 12)], 20, 14))  # last cell 12 for spacing; total 132
    # fix bpm font to exactly 132: 10*12 + 12 = 132 if colon is 12, but code uses 6px colon at x=123
    # Rebuild with correct layout: digits 0-9 at 12px, colon at x=123 w=6 → pad 3px after 120
    bpm = new_rgba(132, 20)
    bd = ImageDraw.Draw(bpm)
    font = _font(14, bold=True)
    for i in range(10):
        draw_text_centered(bd, (i * 12, 0, i * 12 + 12, 20), str(i), font, fill=TEXT, glow_color=(*CYAN[:3], 100))
    draw_text_centered(bd, (123, 0, 129, 20), ":", font, fill=TEXT, glow_color=(*CYAN[:3], 100))
    put("Graphics/5_bpm font.png", bpm)

    put("Graphics/5_skill max.png", word_plate(53, 20, "MAX", color=MAGENTA, bg_alpha=220))
    put("Graphics/7_skill max.png", word_plate(100, 42, "MAX", color=MAGENTA, bg_alpha=220))

    # skill icons strip 350x53, 10 x 35px cells
    icons = new_rgba(350, 53)
    labels = ["SS", "S", "A", "B", "C", "D", "E", "F", "FC", "EX"]
    colors = [CYAN, CYAN, SUCCESS, (180, 220, 80, 255), (255, 200, 0, 255),
              (255, 140, 0, 255), DANGER, (120, 120, 140, 255), MAGENTA, MAGENTA]
    for i, (lab, col) in enumerate(zip(labels, colors)):
        cell = word_plate(35, 53, lab, color=col, bg_alpha=200)
        icons.paste(cell, (i * 35, 0), cell)
    put("Graphics/5_skill icon.png", icons)

    # 6_Difficulty 262x600 — 12 rows of 50px (some empty ok)
    diff6 = new_rgba(262, 600)
    diff_labels = ["MASTER", "BASIC", "ADVANCED", "EXTREME", "REAL",
                   "DTX", "DEBUT", "NOVICE", "REGULAR", "EXPERT", "RAW", "RWS"]
    for i, lab in enumerate(diff_labels):
        row = word_plate(262, 48, lab, color=CYAN if i < 5 else MAGENTA)
        diff6.paste(row, (0, i * 50 + 1), row)
    put("Graphics/6_Difficulty.png", diff6)

    # 6_LevelNumber 1030x130 — large digits
    put("Graphics/6_LevelNumber.png",
        digit_sheet([(str(i), 100) for i in range(10)] + [(".", 30)], 130, 90))

    # 7_Difficulty 60x720 twelve 60x60
    diff7 = new_rgba(60, 720)
    tiers = ["DTX", "DEB", "NOV", "REG", "EXP", "MAS", "BAS", "ADV", "EXT", "RAW", "RWS", "REA"]
    tier_colors = [CYAN, SUCCESS, SUCCESS, (255, 200, 0, 255), (255, 140, 0, 255), MAGENTA,
                   SUCCESS, (0, 200, 255, 255), DANGER, TEXT, TEXT, MAGENTA]
    for i, (lab, col) in enumerate(zip(tiers, tier_colors)):
        cell = word_plate(60, 60, lab[:3], color=col, bg_alpha=230)
        diff7.paste(cell, (0, i * 60), cell)
    put("Graphics/7_Difficulty.png", diff7)

    # Performance panels / gauges
    put("Graphics/7_Gauge.png", panel(543, 94, chamfer=8))
    gauge_fill = new_rgba(504, 31)
    gd = ImageDraw.Draw(gauge_fill)
    for x in range(504):
        t = x / 503
        if t < 0.25:
            col = _blend_color(DANGER, (255, 200, 0), t / 0.25)
        else:
            col = _blend_color(CYAN, SUCCESS, (t - 0.25) / 0.75)
        gd.line([(x, 0), (x, 30)], fill=col)
    put("Graphics/7_gauge_bar.png", gauge_fill)
    put("Graphics/7_gauge_bar.jpg", gauge_fill.convert("RGB").convert("RGBA"))

    put("Graphics/7_Drum_Progress_bg.png", panel(32, 720, chamfer=0))
    prog = new_rgba(32, 720)
    pd = ImageDraw.Draw(prog)
    pd.rectangle([4, 0, 28, 720], fill=CYAN)
    put("Graphics/7_progress_fill.png", prog)

    put("Graphics/7_SkillPanel.png", panel(257, 439))
    # Performance-stage variant: same panel without the baked LEVEL/PLAY rows
    # (the play screen draws the difficulty badge + level digits there).
    # Theme.ini references this via Performance.SkillPanelTexture.
    put("Graphics/7_SkillPanel_perf.png", panel(257, 439))
    put("Graphics/7_Graph_main.png", panel(562, 1024, chamfer=4))
    gfill = new_rgba(256, 512)
    gfd = ImageDraw.Draw(gfill)
    for y in range(512):
        t = 1 - y / 511
        col = _blend_color(CYAN, MAGENTA, t)
        gfd.line([(0, y), (255, y)], fill=col)
    put("Graphics/7_Graph_Gauge.png", gfill)
    put("Graphics/7_JacketPanel.png", panel(349, 347, chamfer=12))

    # Score numbers 360x78: digits 36x50 row0, label SCORE at (0,50) 86x28
    score = new_rgba(360, 78)
    sd = ImageDraw.Draw(score)
    font = _font(40, bold=True)
    for i in range(10):
        draw_text_centered(sd, (i * 36, 0, i * 36 + 36, 50), str(i), font, fill=TEXT, glow_color=(*CYAN[:3], 120))
    lab = word_plate(86, 28, "SCORE", color=CYAN, bg_alpha=180)
    score.paste(lab, (0, 50), lab)
    put("Graphics/7_score numbersGD.png", score)

    # Rate numbers
    put("Graphics/7_Ratenumber_l.png",
        digit_sheet([(str(i), 28) for i in range(10)] + [(".", 10)], 42, 32))
    put("Graphics/7_Ratenumber_s.png",
        digit_sheet([(str(i), 20) for i in range(10)] + [("%", 20), (".", 10)], 26, 18))
    put("Graphics/7_RatePercent_l.png", word_plate(28, 32, "%", color=CYAN))
    put("Graphics/7_LevelNumber.png",
        digit_sheet([(str(i), 16) for i in range(10)] + [(".", 5)], 32, 22))
    # pad level number to 165 width
    ln = digit_sheet([(str(i), 16) for i in range(10)] + [(".", 5)], 32, 22)
    ln_full = new_rgba(165, 32)
    ln_full.paste(ln, (0, 0), ln)
    put("Graphics/7_LevelNumber.png", ln_full)

    put("Graphics/7_lag numbers.png",
        digit_sheet([(str(i), 12) for i in range(10)] + [(".", 8)], 128, 48))
    # force size
    lag_n = new_rgba(128, 128)
    font = _font(48, bold=True)
    ld = ImageDraw.Draw(lag_n)
    for i in range(10):
        draw_text_centered(ld, ((i % 5) * 25, (i // 5) * 60, (i % 5) * 25 + 25, (i // 5) * 60 + 60),
                           str(i), font, fill=TEXT, glow_color=(*CYAN[:3], 100))
    put("Graphics/7_lag numbers.png", lag_n)

    lag = new_rgba(200, 40)
    # optional lag indicator chips - early/late
    early = word_plate(90, 36, "FAST", color=CYAN)
    late = word_plate(90, 36, "SLOW", color=DANGER)
    lag.paste(early, (5, 2), early)
    lag.paste(late, (105, 2), late)
    put("Graphics/7_lag.png", lag)

    # Judge strings XG — paint at exact source rects used by game
    judge = new_rgba(448, 256)
    jd = ImageDraw.Draw(judge)
    placements = [
        ((3, 6, 85, 28), "PERFECT", CYAN),
        ((95, 6, 170, 28), "GREAT", SUCCESS),
        ((4, 44, 84, 66), "GOOD", (255, 200, 0, 255)),
        ((114, 44, 152, 66), "POOR", (255, 140, 0, 255)),
        ((17, 82, 69, 104), "MISS", DANGER),
    ]
    for box, text, col in placements:
        font = _font(16, bold=True)
        draw_text_centered(jd, box, text, font, fill=TEXT, glow_color=(*col[:3], 180))
    # accent bars
    jd.rectangle([17, 111, 193, 129], fill=(255, 220, 0, 200))
    jd.rectangle([17, 131, 193, 149], fill=(*SUCCESS[:3], 200))
    jd.rectangle([18, 151, 194, 169], fill=(*CYAN[:3], 200))
    put("Graphics/7_JudgeStrings_XG.png", judge)
    put("Graphics/7_judge.png", judge.copy())

    # Combo sheets
    combo = new_rgba(600, 380)
    font = _font(100, bold=True)
    cd = ImageDraw.Draw(combo)
    for i in range(10):
        x = (i % 5) * 120
        y = (i // 5) * 160
        draw_text_centered(cd, (x, y, x + 120, y + 160), str(i), font, fill=TEXT, glow_color=(*CYAN[:3], 160))
    label = word_plate(250, 60, "COMBO", color=CYAN)
    combo.paste(label, (0, 320), label)
    put("Graphics/ScreenPlayDrums combo.png", combo)

    combo2 = new_rgba(480, 316)
    font = _font(80, bold=True)
    cd = ImageDraw.Draw(combo2)
    for i in range(10):
        x = (i % 5) * 96
        y = (i // 5) * 128
        draw_text_centered(cd, (x, y, x + 96, y + 128), str(i), font, fill=TEXT, glow_color=(*MAGENTA[:3], 160))
    put("Graphics/ScreenPlayDrums combo_2.png", combo2)

    # Result word plates
    put("Graphics/ScreenResult StageCleared.png", word_plate(600, 190, "STAGE CLEARED", color=CYAN))
    put("Graphics/ScreenResult fullcombo.png", word_plate(520, 190, "FULL COMBO", color=(255, 215, 80, 255)))
    put("Graphics/ScreenResult Excellent.png", word_plate(520, 190, "EXCELLENT", color=MAGENTA))
    put("Graphics/8_New Record.png", word_plate(90, 12, "NEW", color=MAGENTA))

    # Rank badges
    rank_map = [
        ("Graphics/8_rankSS.png", "SS", (200, 255, 255, 255)),
        ("Graphics/8_rankS.png", "S", CYAN),
        ("Graphics/8_rankA.png", "A", SUCCESS),
        ("Graphics/8_rankB.png", "B", (180, 220, 80, 255)),
        ("Graphics/8_rankC.png", "C", (255, 200, 0, 255)),
        ("Graphics/8_rankD.png", "D", (255, 140, 0, 255)),
        ("Graphics/8_rankE.png", "E", DANGER),
    ]
    for rel, letter, col in rank_map:
        put(rel, rank_badge(letter, col))

    # Console fonts
    put("Graphics/Console font 8x16.png", console_font(False))
    put("Graphics/Console font 2 8x16.png", console_font(True))

    # Hit bar — 8x8 source strip; renderer samples SourceWidth=8 x Height=6.
    hit = new_rgba(8, 8)
    hd = ImageDraw.Draw(hit)
    for x in range(8):
        t = abs(x - 3.5) / 3.5
        a = int(255 * (1 - t) ** 0.5)
        col = _blend_color(WHITE, CYAN, t)
        hd.line([(x, 0), (x, 7)], fill=(*col[:3], a))
    put("Graphics/ScreenPlayDrums hit-bar.png", hit)

    # Effects
    put("Graphics/hit_fx.png", effect_burst(8, 32, CYAN, "ring"))
    put("Graphics/ScreenPlayDrums chip wave.png", effect_burst(64, 64, CYAN, "wave"))
    put("Graphics/7_WailingFlush.png", effect_burst(64, 64, MAGENTA, "radial"))
    put("Graphics/7_Bonus.png", effect_burst(117, 88, CYAN, "star"))
    put("Graphics/7_Bonus_100.png", word_plate(117, 88, "100", color=CYAN))
    put("Graphics/7_explosion.png", effect_burst(256, 256, CYAN, "radial"))

    # Wailing fire atlas — runtime expects 2688×720 (wide horizontal strip).
    wf = new_rgba(2688, 720)
    flame = effect_burst(336, 720, MAGENTA, "flame")
    for x in range(0, 2688, 336):
        wf.paste(flame, (x, 0), flame)
    put("Graphics/7_WailingFire.png", wf)

    # Combobomb: 14 frames of 360x340
    bomb = new_rgba(360, 4760)
    for i in range(14):
        frame = effect_burst(360, 340, _blend_color(CYAN, MAGENTA, i / 13), "radial")
        bomb.paste(frame, (0, i * 340), frame)
    put("Graphics/7_combobomb.png", bomb)

    # Chip fire master + hueshift variants (and legacy color names)
    fire_master = effect_burst(128, 128, CYAN, "flame")
    put("Graphics/ScreenPlayDrums chip fire_LC.png", fire_master)
    fire_hues = {
        "HH": 40, "LP": 80, "SD": 120, "HT": 160,
        "BD": 200, "LT": 240, "FT": 280, "CY": 300, "RD": 320,
    }
    for code, deg in fire_hues.items():
        put(f"Graphics/ScreenPlayDrums chip fire_{code}.png", hueshift_rgba(fire_master, deg))
    color_hues = {"red": 120, "blue": 0, "green": 200, "purple": 280, "yellow": 50}
    for name, deg in color_hues.items():
        put(f"Graphics/ScreenPlayDrums chip fire_{name}.png", hueshift_rgba(fire_master, deg))

    star_master = effect_burst(32, 32, CYAN, "star")
    put("Graphics/ScreenPlayDrums chip star_LC.png", star_master)
    for code, deg in fire_hues.items():
        put(f"Graphics/ScreenPlayDrums chip star_{code}.png", hueshift_rgba(star_master, deg))

    # Drum chips sheet 718x776 — 12 cols x 11 rows of 64px
    chips = new_rgba(718, 776)
    x = 0
    for col, (ww, hue) in enumerate(zip(CHIP_WIDTHS, CHIP_HUES)):
        for row in range(11):
            cell = new_rgba(ww, 64)
            cd = ImageDraw.Draw(cell)
            # note bar
            margin = 4
            y0 = 20
            h0 = 24
            # animate brightness by row
            bright = 0.6 + 0.4 * (0.5 + 0.5 * math.sin(row))
            colr = tuple(min(255, int(c * bright)) for c in hue[:3]) + (255,)
            cd.rounded_rectangle([margin, y0, ww - margin, y0 + h0], radius=4, fill=colr)
            # hot core line
            cd.line([(margin + 2, y0 + h0 // 2), (ww - margin - 2, y0 + h0 // 2)], fill=WHITE, width=2)
            # glow edges for overlay rows
            if row in (0, 1, 10):
                cd.rounded_rectangle([margin - 1, y0 - 1, ww - margin + 1, y0 + h0 + 1],
                                    radius=4, outline=(*hue[:3], 180), width=2)
            chips.paste(cell, (x, row * 64), cell)
        x += ww
    put("Graphics/7_chips_drums.png", chips)

    # Long notes atlas
    longn = new_rgba(256, 256)
    ld = ImageDraw.Draw(longn)
    for i, col in enumerate(LANE_COLORS):
        x0 = (i % 5) * 50 + 5
        y0 = (i // 5) * 120 + 10
        ld.rectangle([x0, y0, x0 + 40, y0 + 100], fill=(*col[:3], 180))
        ld.ellipse([x0, y0 - 8, x0 + 40, y0 + 12], fill=col)
        ld.ellipse([x0, y0 + 88, x0 + 40, y0 + 108], fill=col)
    put("Graphics/7_longnotes.png", longn)

    # Lane background strips (7_Paret) — sheet is 558x720 but lanes start at x=295 on screen.
    # The texture is drawn at a screen offset; NX texture is the lane cluster region.
    # We create 558-wide strips matching LANE_WIDTHS packed from the left of the texture.
    paret = Image.new("RGBA", (558, 720), (15, 23, 42, 255))
    pd = ImageDraw.Draw(paret)
    # Pack lanes from x=0 using layout widths (sum=558)
    x = 0
    for i, ww in enumerate(LANE_WIDTHS):
        tint = (*LANE_COLORS[i][:3], 40)
        pd.rectangle([x, 0, x + ww - 1, 719], fill=(20 + i, 28, 45, 255))
        # subtle tint overlay
        overlay = Image.new("RGBA", (ww, 720), tint)
        paret.paste(Image.alpha_composite(paret.crop((x, 0, x + ww, 720)), overlay), (x, 0))
        pd = ImageDraw.Draw(paret)
        pd.line([(x, 0), (x, 719)], fill=(*LANE_COLORS[i][:3], 80), width=1)
        pd.line([(x + ww - 1, 0), (x + ww - 1, 719)], fill=(*LANE_COLORS[i][:3], 80), width=1)
        x += ww
    put("Graphics/7_Paret.png", paret)

    # Pads 384x288 = 4x3 of 96
    pads = new_rgba(384, 288)
    # idle-ish drawings for each cell
    pad_labels = [
        ["LC", "HH", "CY", "RD"],
        ["SN", "HT", "LT", "FT"],
        ["LP", "BD", "", ""],
    ]
    for row in range(3):
        for col in range(4):
            lab = pad_labels[row][col]
            if not lab:
                continue
            cell = panel(96, 96, fill=(30, 41, 59, 220), edge=CYAN, chamfer=12)
            cd = ImageDraw.Draw(cell)
            draw_text_centered(cd, (0, 0, 96, 96), lab, _font(22, True), fill=TEXT, glow_color=(*CYAN[:3], 100))
            pads.paste(cell, (col * 96, row * 96), cell)
    put("Graphics/7_pads.png", pads)

    # Panel icons strip 42x768
    icons = new_rgba(42, 768)
    for i in range(16):
        cell = panel(42, 48, fill=(30, 41, 59, 220), edge=CYAN if i % 2 == 0 else MAGENTA, chamfer=4)
        icons.paste(cell, (0, i * 48), cell)
    put("Graphics/7_panel_icons.jpg", icons)

    # Drumkit pieces
    put("Graphics/drumkit_cymbal.png", drumkit_piece("cymbal", 512, 453))
    put("Graphics/drumkit_drum.png", drumkit_piece("drum", 512, 466))
    put("Graphics/drumkit_hihat.png", drumkit_piece("hihat", 512, 481))
    put("Graphics/drumkit_kick.png", drumkit_piece("kick", 512, 501))
    put("Graphics/drumkit_pedal.png", drumkit_piece("pedal", 398, 512))
    put("Graphics/drumkit_skeleton.png", drumkit_piece("skeleton", 1280, 720))

    return sources


def wire_recipes(sources: Dict[str, str]) -> None:
    with open(MANIFEST_PATH, encoding="utf-8") as f:
        manifest = json.load(f)
    for rel, entry in manifest["assets"].items():
        if rel in sources:
            # Preserve authored sheet/hueshift recipes; only fill missing ones.
            if not entry.get("recipe"):
                entry["recipe"] = {"type": "copy", "source": sources[rel]}
        elif not entry.get("optional"):
            # leave null; validate will catch missing
            pass
    # atomic-ish write
    tmp = MANIFEST_PATH + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, sort_keys=True)
        f.write("\n")
    os.replace(tmp, MANIFEST_PATH)


def main(argv=None):
    global FORCE_OVERWRITE
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--force", action="store_true",
                        help="overwrite existing source files (default: keep AI/hand edits)")
    args = parser.parse_args(argv)
    FORCE_OVERWRITE = args.force
    print("Generating CX Neon source art into", SOURCE_ROOT)
    sources = build_all()
    print("Generated %d source files" % len(sources))
    wire_recipes(sources)
    print("Wired recipes in", MANIFEST_PATH)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
