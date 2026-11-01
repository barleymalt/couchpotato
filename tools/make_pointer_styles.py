"""
Draws the Neon (cyberpunk) and Arcane (dark fantasy) pointer styles: one 256x256 SVG per Windows pointer
role, rendered to PNG into src/Couchpotato/Cursors, plus hotspots-drawn.txt and docs/pointer-styles-drawn.png.
The app animates Busy (turning the spinner) and builds Working from Normal plus a small Busy.

Usage: python3 tools/make_pointer_styles.py      (needs Pillow and resvg-py: pip install resvg-py)
The PNGs are committed, so this only needs running after changing the art. All art here is original.
"""
import io
import os

import resvg_py
from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
OUT = os.path.join(ROOT, "src", "Couchpotato", "Cursors")


def pts(points):
    return "M" + " L".join(f"{x},{y}" for x, y in points) + " Z"


def svg(defs, body):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">'
            f"<defs>{defs}</defs>{body}</svg>")


# ---------------------------------------------------------------- Neon (cyberpunk)
RED, CYAN, YELLOW, INK = "#FF4D57", "#3CF2FF", "#FCEE0A", "#0A0B10"
NEON_DEFS = (
    '<filter id="glow" x="-40%" y="-40%" width="180%" height="180%">'
    '<feGaussianBlur in="SourceGraphic" stdDeviation="4" result="b"/>'
    '<feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>'
    '<linearGradient id="fill" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#26121A"/><stop offset="1" stop-color="#07080C"/></linearGradient>'
    f'<pattern id="scan" width="7" height="7" patternUnits="userSpaceOnUse"><rect width="7" height="2" fill="{RED}" opacity="0.22"/></pattern>'
)


def neon_body(d, rule="nonzero", width=8):
    return (f'<path d="{d}" transform="translate(-7,4)" fill="none" stroke="{CYAN}" stroke-width="{width - 1}" stroke-linejoin="miter" fill-rule="{rule}" opacity="0.9"/>'
            f'<path d="{d}" fill="url(#fill)" stroke="{RED}" stroke-width="{width}" stroke-linejoin="miter" fill-rule="{rule}" filter="url(#glow)"/>'
            f'<path d="{d}" fill="url(#scan)" fill-rule="{rule}"/>')


NEON_ARROW = [(22, 16), (22, 196), (62, 158), (90, 218), (100, 224), (126, 212), (128, 204), (102, 146), (156, 146)]
NEON_ARROW_SVG = (neon_body(pts(NEON_ARROW))
                  + f'<path d="M38,64 L38,150" stroke="{RED}" stroke-width="3"/>'
                  + f'<path d="{pts([(32, 46), (46, 60), (32, 60)])}" fill="{YELLOW}"/>')


def neon_double_arrow(angle):
    d = pts([(14, 128), (66, 76), (66, 110), (190, 110), (190, 76), (242, 128), (190, 180), (190, 146), (66, 146), (66, 180)])
    return f'<g transform="rotate({angle} 128 128)">{neon_body(d)}<rect x="122" y="122" width="12" height="12" fill="{YELLOW}"/></g>'


def octagon(cx, cy, r):
    import math
    return [(round(cx + r * math.cos(math.radians(22.5 + 45 * i)), 1), round(cy + r * math.sin(math.radians(22.5 + 45 * i)), 1)) for i in range(8)]


def neon_badge(glyph):
    return (f'<g transform="scale(0.82)">{NEON_ARROW_SVG}</g>'
            + neon_body(pts(octagon(190, 190, 54)), width=6) + glyph)


def neon_spinner():
    import math
    parts = []
    colors = [RED] * 8 + [CYAN] * 3 + [YELLOW]
    for i in range(12):
        a0, a1 = math.radians(i * 30 + 4), math.radians(i * 30 + 26)
        p = [(128 + 104 * math.cos(a0), 128 + 104 * math.sin(a0)), (128 + 104 * math.cos(a1), 128 + 104 * math.sin(a1)),
             (128 + 74 * math.cos(a1), 128 + 74 * math.sin(a1)), (128 + 74 * math.cos(a0), 128 + 74 * math.sin(a0))]
        d = pts([(round(x, 1), round(y, 1)) for x, y in p])
        opacity = 0.35 + 0.65 * (i / 11)
        parts.append(f'<path d="{d}" fill="{colors[i]}" opacity="{opacity:.2f}" filter="url(#glow)"/>')
    ghost = f'<g transform="translate(-6,3)" opacity="0.5">' + "".join(p.replace("filter=\"url(#glow)\"", "").replace(RED, CYAN) for p in parts[:8]) + "</g>"
    hexa = pts([(round(128 + 46 * math.cos(math.radians(60 * i)), 1), round(128 + 46 * math.sin(math.radians(60 * i)), 1)) for i in range(6)])
    return ghost + "".join(parts) + neon_body(hexa, width=5) + f'<rect x="120" y="120" width="16" height="16" fill="{YELLOW}"/>'


def neon():
    n = {}
    n["normal"] = NEON_ARROW_SVG, (22, 16)
    hand = [(104, 12), (124, 12), (132, 20), (132, 96), (160, 96), (168, 104), (168, 110), (190, 110), (198, 118), (198, 124),
            (214, 124), (222, 132), (222, 196), (200, 236), (110, 236), (96, 222), (60, 170), (60, 150), (68, 142), (80, 142), (96, 160), (96, 20)]
    n["link"] = (neon_body(pts(hand)) + f'<path d="M132,96 L132,140 M168,110 L168,150 M198,124 L198,160" stroke="{RED}" stroke-width="3"/>'
                 + f'<rect x="112" y="210" width="80" height="6" fill="{YELLOW}"/>', (114, 14))
    ibeam = [(86, 30), (170, 30), (170, 52), (142, 52), (142, 204), (170, 204), (170, 226), (86, 226), (86, 204), (114, 204), (114, 52), (86, 52)]
    n["text"] = neon_body(pts(ibeam)) + f'<rect x="124" y="120" width="8" height="16" fill="{YELLOW}"/>', (128, 128)
    n["busy"] = neon_spinner(), (128, 128)
    n["working"] = NEON_ARROW_SVG, (22, 16)  # the arrow; the mockup adds the small spinner
    bars = [[(122, 18), (134, 18), (134, 96), (122, 96)], [(122, 160), (134, 160), (134, 238), (122, 238)],
            [(18, 122), (96, 122), (96, 134), (18, 134)], [(160, 122), (238, 122), (238, 134), (160, 134)]]
    brackets = f'<path d="M86,102 L86,86 L102,86 M154,86 L170,86 L170,102 M170,154 L170,170 L154,170 M102,170 L86,170 L86,154" fill="none" stroke="{CYAN}" stroke-width="4"/>'
    n["precision"] = "".join(neon_body(pts(b), width=5) for b in bars) + brackets + f'<rect x="124" y="124" width="8" height="8" fill="{YELLOW}"/>', (128, 128)
    ring = pts(octagon(128, 128, 104)) + " " + pts(octagon(128, 128, 78))
    slash = pts([(62, 80), (80, 62), (194, 176), (176, 194)])
    n["unavailable"] = neon_body(ring, rule="evenodd") + neon_body(slash), (128, 128)
    pen = [(20, 236), (38, 190), (184, 44), (212, 72), (66, 218)]
    n["pen"] = neon_body(pts(pen)) + f'<path d="M168,60 L196,88" stroke="{YELLOW}" stroke-width="6"/>', (20, 236)
    n["ns"] = neon_double_arrow(90), (128, 128)
    n["ew"] = neon_double_arrow(0), (128, 128)
    n["nwse"] = neon_double_arrow(45), (128, 128)
    n["nesw"] = neon_double_arrow(-45), (128, 128)
    move = [(128, 10), (170, 52), (144, 52), (144, 112), (204, 112), (204, 86), (246, 128), (204, 170), (204, 144), (144, 144), (144, 204),
            (170, 204), (128, 246), (86, 204), (112, 204), (112, 144), (52, 144), (52, 170), (10, 128), (52, 86), (52, 112), (112, 112), (112, 52), (86, 52)]
    n["move"] = neon_body(pts(move)) + f'<rect x="120" y="120" width="16" height="16" fill="{YELLOW}"/>', (128, 128)
    up = [(128, 14), (196, 96), (152, 96), (152, 226), (142, 236), (114, 236), (104, 226), (104, 96), (60, 96)]
    n["up"] = neon_body(pts(up)) + f'<rect x="122" y="150" width="12" height="40" fill="{YELLOW}" opacity="0.9"/>', (128, 16)
    glyph = 'fill="none" stroke="' + YELLOW + '" stroke-width="9" stroke-linecap="square" stroke-linejoin="miter"'
    n["help"] = neon_badge(f'<path d="M172,176 L180,166 L202,166 L210,176 L210,186 L192,196 L192,206" {glyph}/><rect x="187" y="216" width="10" height="10" fill="{YELLOW}"/>'), (18, 13)
    n["location"] = neon_badge(f'<path d="{pts([(190, 230), (168, 198), (168, 172), (178, 162), (202, 162), (212, 172), (212, 198)])}" fill="none" stroke="{YELLOW}" stroke-width="7"/><rect x="184" y="178" width="12" height="12" fill="{YELLOW}"/>'), (18, 13)
    n["person"] = neon_badge(f'<rect x="179" y="160" width="22" height="22" fill="{YELLOW}"/><path d="{pts([(164, 220), (170, 194), (210, 194), (216, 220)])}" fill="{YELLOW}"/>'), (18, 13)
    return {k: (svg(NEON_DEFS, v[0]), v[1]) for k, v in n.items()}


# ---------------------------------------------------------------- Arcane (dark fantasy)
GOLD_DARK, OUTLINE = "#5A3814", "#120C08"
ARC_DEFS = (
    '<linearGradient id="iron" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#6B6158"/><stop offset="0.45" stop-color="#352C25"/><stop offset="1" stop-color="#15110D"/></linearGradient>'
    '<linearGradient id="gold" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#FFEBB0"/><stop offset="0.5" stop-color="#E1A94C"/><stop offset="1" stop-color="#8A5620"/></linearGradient>'
    '<radialGradient id="ember" cx="0.4" cy="0.35" r="0.7"><stop offset="0" stop-color="#FFE3A0"/><stop offset="0.45" stop-color="#F08A2C"/><stop offset="1" stop-color="#6E2406"/></radialGradient>'
    '<linearGradient id="quill" x1="0" y1="1" x2="1" y2="0"><stop offset="0" stop-color="#C9B48A"/><stop offset="1" stop-color="#F6EBD2"/></linearGradient>'
    '<filter id="shadow" x="-30%" y="-30%" width="160%" height="160%"><feDropShadow dx="4" dy="6" stdDeviation="4" flood-color="#000" flood-opacity="0.55"/></filter>'
    '<filter id="glow" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur in="SourceAlpha" stdDeviation="7" result="b"/>'
    '<feFlood flood-color="#FF9A3C" flood-opacity="0.85"/><feComposite in2="b" operator="in" result="g"/>'
    '<feMerge><feMergeNode in="g"/><feMergeNode in="SourceGraphic"/></feMerge></filter>'
)


def plate(shape, fill="url(#iron)", rim="url(#gold)", rim_width=5):
    """A metal plate with a dark outline and a gold rim; shape is an SVG element without fill/stroke."""
    return (shape.replace("/>", f' fill="none" stroke="{OUTLINE}" stroke-width="{rim_width + 7}" stroke-linejoin="round"/>')
            + shape.replace("/>", f' fill="{fill}" stroke="{rim}" stroke-width="{rim_width}" stroke-linejoin="round"/>'))


def gauntlet_upright():
    return "".join([
        plate('<path d="M88,200 L214,196 L224,246 L84,250 Z"/>', fill="url(#gold)", rim=GOLD_DARK, rim_width=4),
        f'<path d="M92,216 L218,212 M90,232 L220,228" stroke="{GOLD_DARK}" stroke-width="3"/>',
        plate('<path d="M96,118 L206,118 Q216,160 206,202 L100,206 Q88,160 96,118 Z"/>'),
        plate('<rect x="136" y="96" width="74" height="38" rx="14"/>'),
        f'<path d="M160,100 L160,130 M185,100 L185,130" stroke="{OUTLINE}" stroke-width="4"/>',
        plate('<path d="M100,148 L72,124 Q60,112 72,102 L82,96 Q92,92 100,102 L118,126 Z"/>'),
        plate('<rect x="100" y="86" width="36" height="46" rx="10"/>'),
        plate('<rect x="102" y="50" width="32" height="42" rx="10"/>'),
        plate('<path d="M104,58 L104,32 Q118,6 132,32 L132,58 Z"/>'),
        '<circle cx="152" cy="162" r="17" fill="url(#ember)" stroke="url(#gold)" stroke-width="4"/>',
        '<circle cx="146" cy="156" r="4" fill="#fff" opacity="0.75"/>',
    ])


ARC_TIP = (30, 24)


def gauntlet(extra=""):
    # Drawn pointing up, tip at (118,19); turned 30° so it points up-left with the tip at ARC_TIP.
    return f'<g filter="url(#shadow)"><g transform="translate({ARC_TIP[0] - 118},{ARC_TIP[1] - 19}) rotate(-30 118 19)">{gauntlet_upright()}</g></g>{extra}'


def leaf(cx, tip_y, base_y, half):
    """A spearhead pointing up, as a path element."""
    mid = (tip_y + base_y) / 2
    return f'<path d="M{cx},{tip_y} Q{cx + half * 1.5},{mid} {cx + half},{base_y} L{cx - half},{base_y} Q{cx - half * 1.5},{mid} {cx},{tip_y} Z"/>'


def arc_double(angle):
    return (f'<g filter="url(#shadow)" transform="rotate({angle} 128 128)">'
            + plate('<rect x="60" y="116" width="136" height="24" rx="6"/>', fill="url(#gold)", rim=GOLD_DARK, rim_width=3)
            + f'<g transform="rotate(90 128 128)">{plate(leaf(128, 10, 72, 32))}</g>'
            + f'<g transform="rotate(-90 128 128)">{plate(leaf(128, 10, 72, 32))}</g>'
            + '<circle cx="128" cy="128" r="16" fill="url(#ember)" stroke="url(#gold)" stroke-width="3"/></g>')


def arc_spinner():
    import math
    runes = ["M-6,-10 L-6,10 M-6,-10 L6,-2 L-6,4", "M0,-10 L0,10 M-7,-4 L7,4", "M-7,10 L0,-10 L7,10 M-4,2 L4,2",
             "M-6,-10 L6,10 M6,-10 L-6,10", "M-6,-10 L-6,10 L6,10", "M0,-10 L6,0 L0,10 L-6,0 Z"]
    marks = []
    for i in range(12):
        a = i * 30
        r = runes[i % len(runes)]
        marks.append(f'<g transform="rotate({a} 128 128) translate(128,42)"><path d="{r}" fill="none" stroke="#FFE3A0" stroke-width="3.2" stroke-linecap="round" stroke-linejoin="round"/></g>')
    star = []
    for k in (0, 1):
        tri = [(round(128 + 40 * math.cos(math.radians(-90 + 120 * i + 60 * k)), 1), round(128 + 40 * math.sin(math.radians(-90 + 120 * i + 60 * k)), 1)) for i in range(3)]
        star.append(f'<path d="{pts(tri)}" fill="none" stroke="url(#gold)" stroke-width="4" stroke-linejoin="round"/>')
    return ('<g filter="url(#glow)">'
            f'<circle cx="128" cy="128" r="104" fill="none" stroke="{OUTLINE}" stroke-width="18"/>'
            '<circle cx="128" cy="128" r="104" fill="none" stroke="url(#gold)" stroke-width="9"/>'
            '<circle cx="128" cy="128" r="102" fill="none" stroke="#2A1A10" stroke-width="2" stroke-dasharray="3 9"/>'
            f'<circle cx="128" cy="128" r="66" fill="none" stroke="{OUTLINE}" stroke-width="8"/>'
            '<circle cx="128" cy="128" r="66" fill="none" stroke="url(#gold)" stroke-width="3.5"/>'
            '<circle cx="128" cy="128" r="85" fill="#1A120C" opacity="0.55"/>'
            + "".join(marks) + "".join(star)
            + '<circle cx="128" cy="128" r="12" fill="url(#ember)"/></g>')


ARC_ARROW = [(24, 18), (24, 198), (66, 158), (98, 224), (128, 210), (98, 146), (156, 146)]


def arc_arrow():
    """The normal pointer: a plain arrow in iron and gold, so it can't be mistaken for the gauntlet (links)."""
    return ('<g filter="url(#shadow)">'
            + plate(f'<path d="{pts(ARC_ARROW)}"/>', rim_width=6)
            + '<path d="M38,60 L38,160 L62,138" fill="none" stroke="url(#gold)" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" opacity="0.8"/>'
            + '<circle cx="64" cy="112" r="10" fill="url(#ember)" stroke="url(#gold)" stroke-width="3"/>'
            + '<circle cx="61" cy="109" r="3" fill="#fff" opacity="0.75"/></g>')


def arc_badge(glyph):
    return (f'<g transform="scale(0.82)">{arc_arrow()}</g>'
            '<g filter="url(#shadow)"><circle cx="190" cy="190" r="52" fill="#2B1C13" stroke="' + OUTLINE + '" stroke-width="12"/>'
            '<circle cx="190" cy="190" r="52" fill="none" stroke="url(#gold)" stroke-width="6"/>'
            '<circle cx="190" cy="190" r="42" fill="none" stroke="#8A5620" stroke-width="1.5" stroke-dasharray="2 5"/>' + glyph + "</g>")


def arcane():
    a = {}
    a["normal"] = arc_arrow(), (24, 18)
    sparkle = ('<g filter="url(#glow)"><path d="M22,2 Q25,15 38,18 Q25,21 22,34 Q19,21 6,18 Q19,15 22,2 Z" fill="#FFF2C8"/>'
               '<circle cx="22" cy="18" r="5" fill="#fff"/></g>')
    a["link"] = gauntlet(sparkle), (29, 22)
    ibeam = ('<g filter="url(#shadow)">'
             + plate('<rect x="119" y="40" width="18" height="176" rx="5"/>', fill="url(#gold)", rim=GOLD_DARK, rim_width=3)
             + plate('<path d="M80,26 Q128,46 176,26 L176,46 Q128,62 80,46 Z"/>', fill="url(#gold)", rim=GOLD_DARK, rim_width=3)
             + plate('<path d="M80,230 Q128,210 176,230 L176,210 Q128,194 80,210 Z"/>', fill="url(#gold)", rim=GOLD_DARK, rim_width=3)
             + plate('<path d="M128,108 L142,128 L128,148 L114,128 Z"/>', fill="url(#ember)", rim="url(#gold)", rim_width=3) + "</g>")
    a["text"] = ibeam, (128, 128)
    a["busy"] = arc_spinner(), (128, 128)
    a["working"] = arc_arrow(), (24, 18)
    lines = "".join(plate(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="3"/>', fill="url(#gold)", rim=GOLD_DARK, rim_width=2)
                    for x, y, w, h in ((121, 34, 14, 70), (121, 152, 14, 70), (34, 121, 70, 14), (152, 121, 70, 14)))
    tips = "".join(f'<g transform="rotate({r} 128 128)">{plate(leaf(128, 10, 46, 17))}</g>' for r in (0, 90, 180, 270))
    a["precision"] = (f'<g filter="url(#shadow)">{lines}{tips}'
                      + plate('<path d="M128,116 L140,128 L128,140 L116,128 Z"/>', fill="url(#ember)", rim="url(#gold)", rim_width=2) + "</g>", (128, 128))
    a["unavailable"] = ('<g filter="url(#shadow)">'
                        f'<circle cx="128" cy="128" r="90" fill="none" stroke="{OUTLINE}" stroke-width="34"/>'
                        '<circle cx="128" cy="128" r="90" fill="none" stroke="#9C2A1E" stroke-width="24"/>'
                        '<circle cx="128" cy="128" r="102" fill="none" stroke="url(#gold)" stroke-width="3"/>'
                        '<circle cx="128" cy="128" r="78" fill="none" stroke="url(#gold)" stroke-width="3"/>'
                        + plate('<rect x="40" y="116" width="176" height="24" rx="4" transform="rotate(45 128 128)"/>', fill="#9C2A1E", rim="url(#gold)", rim_width=3)
                        + "</g>", (128, 128))
    a["pen"] = ('<g filter="url(#shadow)">'
                + plate('<path d="M58,196 Q84,96 214,22 Q196,118 78,214 Z"/>', fill="url(#quill)", rim="#8A6B3A", rim_width=3)
                + '<path d="M40,214 Q120,120 210,28" fill="none" stroke="#6B4A22" stroke-width="3"/>'
                + '<path d="M100,150 L130,140 M118,128 L150,116 M136,106 L168,92 M154,84 L184,70" stroke="#B59A68" stroke-width="2"/>'
                + plate('<path d="M18,238 L30,198 L58,218 Z"/>', fill="url(#gold)", rim=GOLD_DARK, rim_width=3) + "</g>", (18, 238))
    a["ns"] = arc_double(90), (128, 128)
    a["ew"] = arc_double(0), (128, 128)
    a["nwse"] = arc_double(45), (128, 128)
    a["nesw"] = arc_double(-45), (128, 128)
    shafts = plate('<rect x="117" y="60" width="22" height="136" rx="5"/>', fill="url(#gold)", rim=GOLD_DARK, rim_width=3) + \
        plate('<rect x="60" y="117" width="136" height="22" rx="5"/>', fill="url(#gold)", rim=GOLD_DARK, rim_width=3)
    heads = "".join(f'<g transform="rotate({r} 128 128)">{plate(leaf(128, 10, 66, 28))}</g>' for r in (0, 90, 180, 270))
    a["move"] = (f'<g filter="url(#shadow)">{shafts}{heads}<circle cx="128" cy="128" r="18" fill="url(#ember)" stroke="url(#gold)" stroke-width="3"/></g>', (128, 128))
    a["up"] = ('<g filter="url(#shadow)">'
               + plate('<rect x="120" y="92" width="16" height="148" rx="4"/>', fill="#5A3A1E", rim="url(#gold)", rim_width=3)
               + plate(leaf(128, 12, 92, 26))
               + plate('<rect x="94" y="88" width="68" height="14" rx="5"/>', fill="url(#gold)", rim=GOLD_DARK, rim_width=3) + "</g>", (128, 14))
    gold_glyph = 'fill="none" stroke="url(#gold)" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"'
    a["help"] = arc_badge(f'<path d="M172,178 Q172,158 190,158 Q208,158 208,175 Q208,188 192,194 L190,206" {gold_glyph}/><circle cx="190" cy="220" r="6" fill="url(#gold)"/>'), (20, 15)
    a["location"] = arc_badge('<path d="M190,232 Q166,202 166,184 Q166,160 190,160 Q214,160 214,184 Q214,202 190,232 Z" fill="url(#gold)" stroke="' + GOLD_DARK + '" stroke-width="3"/>'
                              '<circle cx="190" cy="184" r="9" fill="url(#ember)"/>'), (20, 15)
    a["person"] = arc_badge('<circle cx="190" cy="176" r="13" fill="url(#gold)"/><path d="M164,222 Q166,196 190,196 Q214,196 216,222 Z" fill="url(#gold)"/>'), (20, 15)
    return {k: (svg(ARC_DEFS, v[0]), v[1]) for k, v in a.items()}


def main():
    lines = ["# style role x y (in the 256x256 images); generated by tools/make_pointer_styles.py"]
    rows = []
    for style, make in (("neon", neon), ("arcane", arcane)):
        row = []
        for role, (text, hot) in make().items():
            if role == "working":
                continue  # the app builds it from normal + busy
            png = Image.open(io.BytesIO(bytes(resvg_py.svg_to_bytes(svg_string=text, width=256, height=256)))).convert("RGBA")
            png.save(os.path.join(OUT, f"{style}-{role}.png"), optimize=True)
            lines.append(f"{style} {role} {hot[0]} {hot[1]}")
            row.append(png)
        rows.append(row)
    with open(os.path.join(OUT, "hotspots-drawn.txt"), "w") as f:
        f.write("\n".join(lines) + "\n")

    cell = 96
    sheet = Image.new("RGBA", (cell * len(rows[0]), cell * len(rows) * 2), (0, 0, 0, 0))
    for half, bg in enumerate([(32, 36, 44, 255), (245, 245, 245, 255)]):
        for r, row in enumerate(rows):
            for c, img in enumerate(row):
                x, y = c * cell, (half * len(rows) + r) * cell
                sheet.paste(bg, (x, y, x + cell, y + cell))
                sheet.alpha_composite(img.resize((cell - 12, cell - 12), Image.LANCZOS), (x + 6, y + 6))
    sheet.save(os.path.join(ROOT, "docs", "pointer-styles-drawn.png"), optimize=True)
    print("wrote", len(lines) - 1, "pointers")


if __name__ == "__main__":
    main()
