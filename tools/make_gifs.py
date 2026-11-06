"""Turns the frames written by tools/FrameDump into the README GIFs.

    dotnet run --project tools/FrameDump -- artifacts/frames
    python tools/make_gifs.py artifacts/frames docs/media

Needs Pillow (pip install pillow). Each GIF draws the LEDs as glowing dots on a dark module, the
way they look on the laptop. Two-module GIFs draw the keyboard between the modules.
"""

import struct
import sys
from pathlib import Path

from PIL import Image, ImageDraw

CELL = 9           # pixels per LED
PAD = 6            # module border
KEYBOARD = 48      # width of the keyboard strip between two modules
BG = (43, 45, 49)  # graphite deck
MODULE = (31, 33, 36)
OFF = (60, 63, 69)


def read(path):
    data = Path(path).read_bytes()
    width, height, count, delay, layout = struct.unpack_from("<5i", data)
    size = width * height
    frames = [data[20 + i * size: 20 + (i + 1) * size] for i in range(count)]
    return width, height, delay, layout, frames


def draw_module(draw, x0, y0, pixels, width, col0, height):
    draw.rounded_rectangle([x0, y0, x0 + PAD * 2 + 9 * CELL, y0 + PAD * 2 + height * CELL], 10, fill=MODULE)
    for y in range(height):
        for x in range(9):
            v = pixels[(col0 + x) + width * y] / 255
            cx = x0 + PAD + x * CELL + CELL / 2
            cy = y0 + PAD + y * CELL + CELL / 2
            if v > 0.35:
                r = CELL * 0.62
                glow = int(MODULE[0] + (110 - MODULE[0]) * v)
                draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(glow, glow, glow + 2))
            lv = int(60 + (255 - 60) * v)
            color = OFF if v == 0 else (lv, lv, min(255, lv + 2))
            r = CELL * 0.36
            draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=color)


def render(path, out):
    width, height, delay, layout, frames = read(path)
    mod_w = PAD * 2 + 9 * CELL
    mod_h = PAD * 2 + height * CELL
    margin = 10
    if layout == 0:
        size = (mod_w + margin * 2, mod_h + margin * 2)
    else:
        size = (mod_w * 2 + KEYBOARD + margin * 4, mod_h + margin * 2)

    images = []
    for f in frames:
        img = Image.new("RGB", size, BG)
        d = ImageDraw.Draw(img)
        if layout == 0:
            draw_module(d, margin, margin, f, width, 0, height)
        else:
            right_col = 9 if layout == 1 else width - 9
            draw_module(d, margin, margin, f, width, 0, height)
            kx = margin * 2 + mod_w
            for row in range(0, mod_h - 32, 20):
                for col in range(0, KEYBOARD, 20):
                    d.rounded_rectangle([kx + col, margin + 16 + row, kx + col + 16, margin + 32 + row], 4, fill=(52, 55, 60))
            draw_module(d, kx + KEYBOARD + margin, margin, f, width, right_col, height)
        images.append(img.quantize(colors=24, method=Image.Quantize.MEDIANCUT))

    images[0].save(out, save_all=True, append_images=images[1:], duration=delay, loop=0, optimize=True, disposal=1)
    print(f"{out}: {len(frames)} frames")


def main():
    src = Path(sys.argv[1] if len(sys.argv) > 1 else "artifacts/frames")
    dst = Path(sys.argv[2] if len(sys.argv) > 2 else "docs/media")
    dst.mkdir(parents=True, exist_ok=True)
    for f in sorted(src.glob("*.bin")):
        render(f, dst / (f.stem + ".gif"))


if __name__ == "__main__":
    main()
