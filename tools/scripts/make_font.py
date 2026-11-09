#!/usr/bin/env python3
"""Builds the game's bitmap fonts (src/Roguelike.App/Fonts) from JetBrains Mono.

Each atlas has 16 columns and 32 rows: the 256 CP437 glyphs in regular weight, then the same 256 in
bold (glyph + 256). Line and block characters are drawn by hand so they meet at cell edges.
Run from the repo root: python3 tools/scripts/make_font.py
"""
import json
import os
from PIL import Image, ImageDraw, ImageFont

OUT = "src/Roguelike.App/Fonts"
REGULAR = "/usr/share/fonts/truetype/jetbrains-mono/JetBrainsMono-Regular.ttf"
BOLD = "/usr/share/fonts/truetype/jetbrains-mono/JetBrainsMono-Bold.ttf"
FALLBACK = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf"
FALLBACK_BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf"

CONTROL = " ☺☻♥♦♣♠•◘○◙♂♀♪♫☼►◄↕‼¶§▬↨↑↓→←∟↔▲▼"
USE_FALLBACK = set("☺☻♥♦♣♠◘○◙♂♀♪♫☼►◄↕‼¶§▬↨∟↔▲▼⌂≈≡∩Ω")

# Which edges each single-line box character touches: (up, down, left, right).
LINES = {179: "ud", 180: "udl", 191: "dl", 192: "ur", 193: "ulr", 194: "dlr", 195: "udr", 196: "lr", 197: "udlr", 217: "ul", 218: "dr"}


def character(code):
    if code < 32:
        return CONTROL[code]
    if code == 127:
        return "⌂"
    return bytes([code]).decode("cp437")


def draw_special(code, draw, x, y, w, h, thickness):
    """Line and block characters, which must reach the cell edges. True if this one was drawn."""
    white = (255, 255, 255, 255)
    if code in LINES:
        cx, cy = x + w // 2, y + h // 2
        t = thickness // 2
        edges = LINES[code]
        if "u" in edges: draw.rectangle([cx - t, y, cx + thickness - t - 1, cy + t], fill=white)
        if "d" in edges: draw.rectangle([cx - t, cy - t, cx + thickness - t - 1, y + h - 1], fill=white)
        if "l" in edges: draw.rectangle([x, cy - t, cx + t, cy + thickness - t - 1], fill=white)
        if "r" in edges: draw.rectangle([cx - t, cy - t, x + w - 1, cy + thickness - t - 1], fill=white)
        return True
    if code == 219: draw.rectangle([x, y, x + w - 1, y + h - 1], fill=white); return True
    if code == 220: draw.rectangle([x, y + h // 2, x + w - 1, y + h - 1], fill=white); return True
    if code == 223: draw.rectangle([x, y, x + w - 1, y + h // 2 - 1], fill=white); return True
    if code == 221: draw.rectangle([x, y, x + w // 2 - 1, y + h - 1], fill=white); return True
    if code == 222: draw.rectangle([x + w // 2, y, x + w - 1, y + h - 1], fill=white); return True
    if code in (176, 177, 178):
        keep = {176: 4, 177: 2}.get(code)
        for py in range(h):
            for px in range(w):
                on = (px + py) % keep == 0 if keep else (px + py) % 4 != 0
                if on:
                    draw.point((x + px, y + py), fill=white)
        return True
    return False


def build(name, file, w, h, size):
    image = Image.new("RGBA", (16 * w, 32 * h), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    faces = [(ImageFont.truetype(REGULAR, size), ImageFont.truetype(FALLBACK, size)),
             (ImageFont.truetype(BOLD, size), ImageFont.truetype(FALLBACK_BOLD, size))]
    for weight, (main, fallback) in enumerate(faces):
        for code in range(256):
            index = code + 256 * weight
            x, y = index % 16 * w, index // 16 * h
            if draw_special(code, draw, x, y, w, h, 2 if weight else 1):
                continue
            ch = character(code)
            if ch == " ":
                continue
            face = fallback if ch in USE_FALLBACK else main
            ascent, descent = face.getmetrics()
            left, _, right, _ = face.getbbox(ch)
            cell = Image.new("RGBA", (w, h), (0, 0, 0, 0))
            ImageDraw.Draw(cell).text(((w - (right - left)) / 2 - left, (h - ascent - descent) / 2), ch, font=face, fill=(255, 255, 255, 255))
            image.paste(cell, (x, y))
    image.save(os.path.join(OUT, file + ".png"))
    with open(os.path.join(OUT, file + ".font"), "w") as f:
        json.dump({"$type": "SadConsole.SadFont, SadConsole", "Name": name, "FilePath": file + ".png", "GlyphHeight": h, "GlyphPadding": 0,
                   "GlyphWidth": w, "SolidGlyphIndex": 219, "Columns": 16, "UnsupportedGlyphIndex": 63}, f, indent=2)


build("MapFont", "map", 18, 18, 15)
build("UiFont", "ui", 9, 18, 14)
