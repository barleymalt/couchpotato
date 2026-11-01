"""
Draws the simple pointer styles (Drop, Classic, Cartoon, Retro) as 256x256 PNGs, one per Windows pointer
role, into src/Couchpotato/Cursors, plus hotspots.txt and a preview sheet in docs/pointer-styles.png.
Each style is a way of painting a shape ("paint"); the shapes are shared, so every style has every pointer.
The app animates Busy and builds Working from Normal plus a small Busy.

Usage: python3 tools/make_cursors.py      (needs Pillow)
The PNGs are committed, so this only needs running after changing the art.
"""
import math
import os
from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
OUT = os.path.join(ROOT, "src", "Couchpotato", "Cursors")
SIZE = 256
SS = 4  # supersampling factor for smooth edges


# ---------- geometry (in 256x256 space) ----------

def teardrop(tip=(26, 22), angle_deg=58, length=150, radius=56, steps=96):
    ax, ay = math.cos(math.radians(angle_deg)), math.sin(math.radians(angle_deg))
    cx, cy = tip[0] + ax * length, tip[1] + ay * length
    d = math.hypot(cx - tip[0], cy - tip[1])
    phi = math.acos(radius / d)
    back = math.atan2(tip[1] - cy, tip[0] - cx)
    pts = [tip]
    start, end = back + phi, back + 2 * math.pi - phi
    for i in range(steps + 1):
        a = start + (end - start) * i / steps
        pts.append((cx + radius * math.cos(a), cy + radius * math.sin(a)))
    return pts


ARROW = [(24, 20), (24, 198), (70, 154), (104, 228), (136, 214), (102, 140), (166, 140)]
DELTA = [(22, 18), (58, 214), (92, 150), (188, 126)]
IBEAM = [(92, 36), (164, 36), (164, 54), (138, 54), (138, 202), (164, 202), (164, 220),
         (92, 220), (92, 202), (118, 202), (118, 54), (92, 54)]


HAND = [(104, 12), (124, 12), (132, 20), (132, 96), (160, 96), (168, 104), (168, 110), (190, 110), (198, 118), (198, 124),
        (214, 124), (222, 132), (222, 196), (200, 236), (110, 236), (96, 222), (60, 170), (60, 150), (68, 142), (80, 142), (96, 160), (96, 20)]
UP = [(128, 14), (196, 96), (152, 96), (152, 236), (104, 236), (104, 96), (60, 96)]
PEN = [(20, 236), (38, 190), (184, 44), (212, 72), (66, 218)]


def rotated(points, angle, c=(128, 128)):
    a = math.radians(angle)
    return [(c[0] + (x - c[0]) * math.cos(a) - (y - c[1]) * math.sin(a), c[1] + (x - c[0]) * math.sin(a) + (y - c[1]) * math.cos(a)) for x, y in points]


def rect(x0, y0, x1, y1):
    return [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]


# The arrows as convex pieces: Pillow 11 fills some of the whole outlines wrongly (a corner missing or extra).
DOUBLE_PARTS = [rect(64, 110, 192, 146), [(14, 128), (66, 76), (66, 180)], [(242, 128), (190, 76), (190, 180)]]
MOVE_PARTS = [rect(112, 50, 144, 206), rect(50, 112, 206, 144), [(128, 10), (170, 52), (86, 52)], [(246, 128), (204, 86), (204, 170)],
              [(128, 246), (86, 204), (170, 204)], [(10, 128), (52, 86), (52, 170)]]


def double(angle):
    return [rotated(part, angle) for part in DOUBLE_PARTS]


def scaled(points, k):
    return [(x * k, y * k) for x, y in points]


def more_shapes(normal, normal_hot):
    """The roles beyond normal, text and busy: role -> (mask, hotspot). Badged roles reuse a smaller normal pointer."""
    k = 0.74
    small, small_hot = scaled(normal, k), (round(normal_hot[0] * k), round(normal_hot[1] * k))
    question = [(164, 172, 178, 156, 18), (178, 156, 206, 156, 18), (206, 156, 220, 172, 18), (220, 172, 220, 188, 18),
                (220, 188, 196, 202, 18), (196, 202, 196, 216, 18)]
    centre = (128, 128)
    return {
        "link": (mask_from(polygons=[HAND]), (114, 14)),
        "help": (mask_from(polygons=[small], lines=question, ellipses=[(185, 228, 207, 250)]), small_hot),
        "precision": (mask_from(lines=[(128, 24, 128, 96, 14), (128, 160, 128, 232, 14), (24, 128, 96, 128, 14), (160, 128, 232, 128, 14)],
                                ellipses=[(120, 120, 136, 136)]), centre),
        "pen": (mask_from(polygons=[PEN]), (20, 236)),
        "unavailable": (mask_from(rings=[(128, 128, 86, 26, 0)], lines=[(70, 70, 186, 186, 26)]), centre),
        "ns": (mask_from(polygons=double(90)), centre),
        "ew": (mask_from(polygons=double(0)), centre),
        "nwse": (mask_from(polygons=double(45)), centre),
        "nesw": (mask_from(polygons=double(-45)), centre),
        "move": (mask_from(polygons=MOVE_PARTS), centre),
        "up": (mask_from(polygons=[UP]), (128, 16)),
        "location": (mask_from(polygons=[small, [(166, 190), (222, 190), (194, 248)]], ellipses=[(162, 142, 226, 206)]), small_hot),
        "person": (mask_from(polygons=[small], ellipses=[(174, 134, 214, 174), (152, 182, 236, 262)]), small_hot),
    }


def star(cx, cy, outer, inner, points=5, rotation=-90):
    pts = []
    for i in range(points * 2):
        r = outer if i % 2 == 0 else inner
        a = math.radians(rotation + i * 180 / points)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def mask_from(polygons=(), ellipses=(), lines=(), rings=()):
    """A supersampled L-mode mask of the union of shapes."""
    m = Image.new("L", (SIZE * SS, SIZE * SS), 0)
    d = ImageDraw.Draw(m)
    for poly in polygons:
        d.polygon([(x * SS, y * SS) for x, y in poly], fill=255)
    for (x0, y0, x1, y1) in ellipses:
        d.ellipse([x0 * SS, y0 * SS, x1 * SS, y1 * SS], fill=255)
    for (x0, y0, x1, y1, w) in lines:
        d.line([x0 * SS, y0 * SS, x1 * SS, y1 * SS], fill=255, width=int(w * SS))
        for x, y in ((x0, y0), (x1, y1)):
            d.ellipse([(x - w / 2) * SS, (y - w / 2) * SS, (x + w / 2) * SS, (y + w / 2) * SS], fill=255)
    for (cx, cy, r, w, gap) in rings:
        box = [(cx - r) * SS, (cy - r) * SS, (cx + r) * SS, (cy + r) * SS]
        d.arc(box, start=-90 + gap, end=270, fill=255, width=int(w * SS))
    return m


# ---------- mask operations (on supersampled masks) ----------

def grow(m, px):
    """Rounded dilation by px (256-space pixels)."""
    if px <= 0:
        return m
    blurred = m.filter(ImageFilter.GaussianBlur(px * SS * 0.6))
    return blurred.point(lambda v: 255 if v > 18 else int(v * 255 / 18))


def soften(m, px):
    """Rounds corners: blur, then re-threshold at the middle."""
    blurred = m.filter(ImageFilter.GaussianBlur(px * SS))
    return blurred.point(lambda v: 0 if v < 110 else 255 if v > 146 else (v - 110) * 7)


def solid(color, m):
    layer = Image.new("RGBA", m.size, color)
    layer.putalpha(ImageChops.multiply(m, Image.new("L", m.size, color[3] if len(color) > 3 else 255)))
    return layer


def gradient(top, bottom, m, axis="diagonal"):
    w, h = m.size
    g = Image.new("RGBA", (w, h))
    px = g.load()
    for y in range(0, h, SS):
        for x in range(0, w, SS):
            t = (x + y) / (w + h) if axis == "diagonal" else y / h
            c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(4))
            for yy in range(SS):
                for xx in range(SS):
                    px[x + xx, y + yy] = c
    alpha = ImageChops.multiply(m, g.getchannel("A"))
    g.putalpha(alpha)
    return g


def glow(color, m, px, strength=1.0):
    blurred = m.filter(ImageFilter.GaussianBlur(px * SS))
    blurred = blurred.point(lambda v: min(255, int(v * strength)))
    return solid(color[:3] + (255,), blurred)


def shadow(m, dx=5, dy=7, blur=5, alpha=110):
    shifted = ImageChops.offset(m, dx * SS, dy * SS).filter(ImageFilter.GaussianBlur(blur * SS))
    return solid((0, 0, 0, 255), shifted.point(lambda v: v * alpha // 255))


def compose(*layers):
    out = Image.new("RGBA", (SIZE * SS, SIZE * SS), (0, 0, 0, 0))
    for layer in layers:
        out.alpha_composite(layer)
    return out.resize((SIZE, SIZE), Image.LANCZOS)


def outlined(shape, fill_layer_fn, outline_color, outline_px, extra_before=(), extra_after=()):
    outer = grow(shape, outline_px)
    return compose(*extra_before, solid(outline_color, outer), fill_layer_fn(shape), *extra_after)


# ---------- themes ----------

BUSY = dict(rings=[(128, 128, 64, 26, 70)])


def working(pointer_img, busy_img):
    """Pointer with a small busy ring, for 'app starting'."""
    out = pointer_img.copy()
    small = busy_img.resize((120, 120), Image.LANCZOS)
    out.alpha_composite(small, (128, 128))
    return out


def theme_drop():
    def paint(m, outline=9):
        # A thin dark rim keeps the white edge visible on white windows.
        return outlined(m, lambda s: gradient((236, 64, 160, 240), (122, 64, 255, 240), s), (255, 255, 255, 255), outline,
                        extra_before=[shadow(grow(m, outline)), solid((40, 40, 52, 255), grow(m, outline + 2.5))])
    pointer = paint(mask_from(polygons=[teardrop()]))
    text = paint(mask_from(polygons=[IBEAM]), 7)
    busy = paint(mask_from(**BUSY), 7)
    return pointer, (26, 22), text, (128, 128), busy, (128, 128), paint, teardrop()


def theme_classic():
    def paint(m, outline=8):
        return outlined(m, lambda s: solid((255, 255, 255, 255), s), (0, 0, 0, 255), outline,
                        extra_before=[shadow(grow(m, outline))])
    pointer = paint(mask_from(polygons=[ARROW]))
    text = paint(mask_from(polygons=[IBEAM]), 7)
    busy = paint(mask_from(**BUSY), 7)
    return pointer, (24, 20), text, (128, 128), busy, (128, 128), paint, ARROW


def theme_cartoon():
    def paint(m, outline=11):
        m = soften(m, 5)
        highlight = ImageChops.multiply(soften(mask_from(ellipses=[(44, 54, 70, 120)]), 2), m)
        return outlined(m, lambda s: gradient((255, 236, 92, 255), (255, 160, 20, 255), s, axis="vertical"), (43, 33, 64, 255), outline,
                        extra_before=[shadow(grow(m, outline), 7, 9, 3, 90)],
                        extra_after=[solid((255, 255, 255, 170), highlight)])
    pointer = paint(mask_from(polygons=[ARROW]))
    text = paint(mask_from(polygons=[IBEAM]), 9)
    busy = paint(mask_from(**BUSY), 9)
    return pointer, (26, 23), text, (128, 128), busy, (128, 128), paint, ARROW


def theme_fantasy():
    gold = lambda s: gradient((255, 246, 190, 255), (226, 150, 20, 255), s)
    purple = (179, 107, 255, 255)

    def sparkles(points):
        return mask_from(polygons=[star(x, y, r, r * 0.28, 4, -90) for x, y, r in points])

    wand_star = mask_from(polygons=[star(50, 50, 44, 18)])
    stick = mask_from(lines=[(78, 78, 212, 212, 18)])
    bands = ImageChops.multiply(stick, mask_from(polygons=[[(110, 92), (92, 110), (104, 122), (122, 104)],
                                                           [(186, 168), (168, 186), (180, 198), (198, 180)]]))
    spark = sparkles([(118, 34, 13), (34, 120, 11), (104, 104, 8)])
    everything = ImageChops.lighter(ImageChops.lighter(wand_star, stick), spark)
    pointer = compose(glow(purple, everything, 9, 1.6),
                      solid((70, 40, 20, 255), grow(stick, 4)), solid((112, 70, 38, 255), stick), gold(bands),
                      solid((90, 50, 10, 255), grow(wand_star, 5)), gold(wand_star),
                      solid((255, 255, 255, 255), spark))

    def layers(m, outline=6):
        return [glow(purple, grow(m, outline), 8, 1.4), solid((90, 50, 10, 255), grow(m, outline)), gold(m)]
    text = compose(*layers(mask_from(polygons=[IBEAM])), solid((255, 255, 255, 255), sparkles([(186, 40, 14)])))
    busy = compose(*layers(mask_from(**BUSY)))
    return pointer, (50, 50), text, (128, 128), busy, (128, 128)


def theme_scifi():
    cyan = (60, 242, 255, 255)

    def paint(m, line=5):
        ring = ImageChops.subtract(grow(m, line), m)
        return compose(glow(cyan, grow(m, line), 10, 1.3), solid((0, 34, 46, 190), m), solid(cyan, ring))
    delta = mask_from(polygons=[DELTA])
    pointer = paint(delta)
    ticks = mask_from(lines=[(150, 210, 196, 210, 5), (196, 210, 196, 180, 5)])
    pointer.alpha_composite(compose(glow(cyan, ticks, 6), solid(cyan, ticks)))
    text = paint(mask_from(polygons=[IBEAM]))
    busy = paint(mask_from(**BUSY))
    return pointer, (22, 18), text, (128, 128), busy, (128, 128)


def theme_retro():
    def pixel(polygon, grid=22, scale_from=256):
        small = Image.new("L", (grid, grid), 0)
        ImageDraw.Draw(small).polygon([(x * grid / scale_from, y * grid / scale_from) for x, y in polygon], fill=255)
        outline = small.filter(ImageFilter.MaxFilter(3))
        img = Image.new("RGBA", (grid, grid), (0, 0, 0, 0))
        img.paste((0, 0, 0, 255), mask=outline)
        img.paste((255, 255, 255, 255), mask=small)
        return img.resize((SIZE, SIZE), Image.NEAREST)
    pointer = pixel(ARROW)
    text = pixel(IBEAM)
    ring = Image.new("L", (22, 22), 0)
    ImageDraw.Draw(ring).ellipse([6, 6, 15, 15], outline=255, width=2)
    busy = Image.new("RGBA", (22, 22), (0, 0, 0, 0))
    busy.paste((0, 0, 0, 255), mask=ring.filter(ImageFilter.MaxFilter(3)))
    busy.paste((255, 255, 255, 255), mask=ring)
    busy = busy.resize((SIZE, SIZE), Image.NEAREST)
    cell = SIZE / 22

    def paint(m):
        """Any shape as 22x22 pixel art, white with a black outline."""
        small = m.resize((22, 22), Image.BOX).point(lambda v: 255 if v > 90 else 0)
        img = Image.new("RGBA", (22, 22), (0, 0, 0, 0))
        img.paste((0, 0, 0, 255), mask=small.filter(ImageFilter.MaxFilter(3)))
        img.paste((255, 255, 255, 255), mask=small)
        return img.resize((SIZE, SIZE), Image.NEAREST)
    return pointer, (int(2 * cell), int(2 * cell)), text, (128, 128), busy, (128, 128), paint, ARROW


# Fantasy and Sci-fi became the fully drawn Arcane and Neon (tools/make_pointer_styles.py).
THEMES = [("drop", theme_drop), ("classic", theme_classic), ("cartoon", theme_cartoon), ("retro", theme_retro)]


def main():
    os.makedirs(OUT, exist_ok=True)
    hotspots = []
    sheet_rows = []
    for name, make in THEMES:
        pointer, p_hot, text, t_hot, busy, b_hot, paint, normal = make()
        roles = [("normal", pointer, p_hot), ("text", text, t_hot), ("busy", busy, b_hot)]
        roles += [(role, paint(m), hot) for role, (m, hot) in more_shapes(normal, p_hot).items()]
        for role, img, hot in roles:
            img.save(os.path.join(OUT, f"{name}-{role}.png"), optimize=True)
            hotspots.append(f"{name} {role} {hot[0]} {hot[1]}")
        sheet_rows.append([img for _, img, _ in roles])

    with open(os.path.join(OUT, "hotspots.txt"), "w") as f:
        f.write("# theme shape x y (in the 256x256 images); generated by tools/make_cursors.py\n")
        f.write("\n".join(hotspots) + "\n")

    cell = 96
    sheet = Image.new("RGBA", (cell * len(sheet_rows[0]), cell * len(sheet_rows) * 2), (0, 0, 0, 0))
    for half, bg in enumerate([(32, 36, 44, 255), (245, 245, 245, 255)]):
        for r, row in enumerate(sheet_rows):
            for c, img in enumerate(row):
                x, y = c * cell, (half * len(sheet_rows) + r) * cell
                sheet.paste(bg, (x, y, x + cell, y + cell))
                sheet.alpha_composite(img.resize((cell - 12, cell - 12), Image.LANCZOS), (x + 6, y + 6))
    sheet.save(os.path.join(ROOT, "docs", "pointer-styles.png"), optimize=True)
    print("wrote", len(hotspots), "cursors")


if __name__ == "__main__":
    main()
