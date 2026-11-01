"""Draws the MagicRemote logo (teal tile, dark remote) into the PC .ico, the TV app icons and docs/icon.png.

Usage: python3 tools/make_icon.py
"""
import os

from PIL import Image, ImageDraw

ROOT = os.path.join(os.path.dirname(__file__), "..")
TEAL = (0x52, 0xC4, 0xA0, 255)
DARK = (0x0B, 0x26, 0x1E, 255)


def logo(size):
    """The logo on a 40-unit grid, drawn 8x larger and scaled down for smooth edges."""
    k = size * 8 / 40
    big = Image.new("RGBA", (size * 8, size * 8), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    d.rounded_rectangle([0, 0, 40 * k - 1, 40 * k - 1], radius=11 * k, fill=TEAL)
    d.rounded_rectangle([13 * k, 7 * k, 27 * k, 33 * k], radius=7 * k, fill=DARK)
    d.ellipse([(20 - 3.2) * k, (15 - 3.2) * k, (20 + 3.2) * k, (15 + 3.2) * k], fill=TEAL)
    if size >= 20:  # the small button vanishes at tray size
        d.ellipse([(20 - 1.6) * k, (25 - 1.6) * k, (20 + 1.6) * k, (25 + 1.6) * k], fill=TEAL)
    return big.resize((size, size), Image.LANCZOS)


def main():
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    images = [logo(s) for s in sizes]
    images[-1].save(os.path.join(ROOT, "src", "MagicRemote", "MagicRemote.ico"), sizes=[(s, s) for s in sizes], append_images=images[:-1])
    logo(80).save(os.path.join(ROOT, "tvapp", "app", "icon.png"))
    logo(130).save(os.path.join(ROOT, "tvapp", "app", "largeIcon.png"))
    logo(256).save(os.path.join(ROOT, "docs", "icon.png"))


if __name__ == "__main__":
    main()
