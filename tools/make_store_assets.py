"""Draws the MSIX logo images (packaging/Assets) at every size Windows asks for.

Same design as the app icon: a dark rounded square with two 3x6 grids of white dots.
Run: python tools/make_store_assets.py   (needs Pillow)
"""
from pathlib import Path

from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent.parent / "packaging" / "Assets"
BG = (32, 32, 40, 255)
DOT = (255, 255, 255, 255)
SS = 4  # supersampling


def icon(size: int) -> Image.Image:
    """The app icon at size x size (geometry measured from the 256 px original)."""
    big = size * SS
    k = big / 256
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, big - 1, big - 1], radius=56 * k, fill=BG)
    r = 7 * k
    centres_x = [127.5 + side * 46 + (c - 1) * 20.5 for side in (-1, 1) for c in range(3)]
    centres_y = [127.5 + (row - 2.5) * 20.4 for row in range(6)]
    for cx in centres_x:
        for cy in centres_y:
            d.ellipse([cx * k - r, cy * k - r, cx * k + r, cy * k + r], fill=DOT)
    return img.resize((size, size), Image.LANCZOS)


def on_canvas(width: int, height: int, icon_size: int) -> Image.Image:
    canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    i = icon(icon_size)
    canvas.paste(i, ((width - icon_size) // 2, (height - icon_size) // 2), i)
    return canvas


def save(img: Image.Image, name: str) -> None:
    img.save(OUT / name)
    print(name, img.size)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for scale in (100, 200, 400):
        f = scale / 100
        save(icon(round(50 * f)), f"StoreLogo.scale-{scale}.png")
        save(icon(round(44 * f)), f"Square44x44Logo.scale-{scale}.png")
        save(on_canvas(round(150 * f), round(150 * f), round(100 * f)), f"Square150x150Logo.scale-{scale}.png")
        save(on_canvas(round(310 * f), round(150 * f), round(100 * f)), f"Wide310x150Logo.scale-{scale}.png")
    # Taskbar / Start list icons without the accent-colour plate.
    for size in (16, 24, 32, 48, 256):
        save(icon(size), f"Square44x44Logo.targetsize-{size}_altform-unplated.png")
        save(icon(size), f"Square44x44Logo.targetsize-{size}.png")


if __name__ == "__main__":
    main()
