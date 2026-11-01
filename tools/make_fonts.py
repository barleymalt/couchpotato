"""Makes the embedded UI fonts: static Manrope Regular, SemiBold and Bold from the variable font, Latin only.

Usage: python3 tools/make_fonts.py <Manrope[wght].ttf from github.com/google/fonts/tree/main/ofl/manrope>
SemiBold gets its own family name ("Manrope SemiBold") so plain GDI, which only knows regular and bold, can pick it.
"""
import os
import sys

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

OUT = os.path.join(os.path.dirname(__file__), "..", "src", "MagicRemote", "Fonts")
LATIN = "U+0020-007E,U+00A0-017F,U+2010-2027,U+2030-203A,U+2190-2193,U+2212,U+20AC"

# (file, weight, GDI family, GDI style, bold)
STYLES = [
    ("Manrope-Regular.ttf", 400, "Manrope", "Regular", False),
    ("Manrope-SemiBold.ttf", 600, "Manrope SemiBold", "Regular", False),
    ("Manrope-Bold.ttf", 700, "Manrope", "Bold", True),
]


def make(source, file, weight, family, style, bold):
    font = instancer.instantiateVariableFont(TTFont(source), {"wght": weight})
    options = subset.Options()
    options.layout_features = ["kern", "liga", "calt", "tnum"]
    options.name_IDs = ["*"]
    sub = subset.Subsetter(options)
    sub.populate(unicodes=subset.parse_unicodes(LATIN))
    sub.subset(font)

    full = family if style == "Regular" else family + " " + style
    name = font["name"]
    for nid in (16, 17, 21, 22, 25):
        name.removeNames(nameID=nid)
    for nid, value in ((1, family), (2, style), (3, full + ";MagicRemote"), (4, full), (6, full.replace(" ", "-"))):
        name.setName(value, nid, 3, 1, 0x409)
        name.setName(value, nid, 1, 0, 0)
    if family != "Manrope":
        name.setName("Manrope", 16, 3, 1, 0x409)
        name.setName("SemiBold", 17, 3, 1, 0x409)

    font["OS/2"].usWeightClass = weight
    font["OS/2"].fsSelection = (font["OS/2"].fsSelection & ~0b1100001) | (0b100000 if bold else 0b1000000)
    font["head"].macStyle = 1 if bold else 0
    font.save(os.path.join(OUT, file))


def main():
    os.makedirs(OUT, exist_ok=True)
    for style in STYLES:
        make(sys.argv[1], *style)
        print("wrote", style[0], os.path.getsize(os.path.join(OUT, style[0])), "bytes")


if __name__ == "__main__":
    main()
