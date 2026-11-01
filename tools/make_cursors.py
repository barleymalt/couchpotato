"""
Draws the simple pointer styles as 256x256 PNGs (normal, text, busy) into src/MagicRemote/Cursors,
plus hotspots.txt and a preview sheet in docs/pointer-styles.png.

Usage: python3 tools/make_cursors.py      (needs Pillow)
The PNGs are committed, so this only needs running after changing the art.
"""
import math
import os
from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
OUT = os.path.join(ROOT, "src", "MagicRemote", "Cursors")
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
    return pointer, (26, 22), text, (128, 128), busy, (128, 128)


def theme_classic():
    def paint(m, outline=8):
        return outlined(m, lambda s: solid((255, 255, 255, 255), s), (0, 0, 0, 255), outline,
                        extra_before=[shadow(grow(m, outline))])
    pointer = paint(mask_from(polygons=[ARROW]))
    text = paint(mask_from(polygons=[IBEAM]), 7)
    busy = paint(mask_from(**BUSY), 7)
    return pointer, (24, 20), text, (128, 128), busy, (128, 128)


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
    return pointer, (26, 23), text, (128, 128), busy, (128, 128)


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
    return pointer, (int(2 * cell), int(2 * cell)), text, (128, 128), busy, (128, 128)


# Fantasy and Sci-fi became the fully drawn Arcane and Neon (tools/make_pointer_styles.py).
THEMES = [("drop", theme_drop), ("classic", theme_classic), ("cartoon", theme_cartoon), ("retro", theme_retro)]


def main():
    os.makedirs(OUT, exist_ok=True)
    hotspots = []
    sheet_rows = []
    for name, make in THEMES:
        pointer, p_hot, text, t_hot, busy, b_hot = make()
        work = working(pointer, busy)  # only for the preview sheet: the app builds Working itself
        for shape, img, hot in (("normal", pointer, p_hot), ("text", text, t_hot), ("busy", busy, b_hot)):
            img.save(os.path.join(OUT, f"{name}-{shape}.png"), optimize=True)
            hotspots.append(f"{name} {shape} {hot[0]} {hot[1]}")
        sheet_rows.append((name, [pointer, text, busy, work]))

    with open(os.path.join(OUT, "hotspots.txt"), "w") as f:
        f.write("# theme shape x y (in the 256x256 images); generated by tools/make_cursors.py\n")
        f.write("\n".join(hotspots) + "\n")

    cell = 112
    sheet = Image.new("RGBA", (cell * 8, cell * len(sheet_rows)), (0, 0, 0, 0))
    for r, (name, imgs) in enumerate(sheet_rows):
        for half, bg in enumerate([(245, 245, 245, 255), (32, 36, 44, 255)]):
            for c, img in enumerate(imgs):
                x, y = (half * 4 + c) * cell, r * cell
                sheet.paste(bg, (x, y, x + cell, y + cell))
                sheet.alpha_composite(img.resize((cell - 16, cell - 16), Image.LANCZOS), (x + 8, y + 8))
    sheet.save(os.path.join(ROOT, "docs", "pointer-styles.png"), optimize=True)
    print("wrote", len(hotspots), "cursors")


if __name__ == "__main__":
    main()
