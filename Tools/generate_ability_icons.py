#!/usr/bin/env python3
"""Procedurally renders ability-card icons and ability block sprites.

Pure stdlib (no PIL). Output: Assets/Art/Abilities/. The house style for every
ability icon lives in ART.md ("Ability icons") - in short: one bold centered
emblem, thick rounded silhouette with a dark outline, vertical gradient + top
bevel (same lighting language as the block sprites), soft radial glow behind
the emblem, 4-point sparkle accents. 512x512, transparent, generous margins
(emblem within the middle ~70%) because the card crops nothing.

Adding an ability's art = add a render_* function + an entry in ARTWORK, rerun.
Deterministic (seeded) so regeneration is stable.
"""
import math, os, random, sys

sys.path.insert(0, os.path.dirname(__file__))
from generate_piece_sprites import write_png

OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "Assets", "Art", "Abilities")


# ---------------------------------------------------------------- canvas helpers

class Canvas:
    """RGBA float canvas with straight-alpha 'over' compositing."""
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.px = [[ (0.0, 0.0, 0.0, 0.0) ] * w for _ in range(h)]

    def blend(self, x, y, r, g, b, a):
        if a <= 0 or not (0 <= x < self.w and 0 <= y < self.h): return
        br, bg, bb, ba = self.px[y][x]
        oa = a + ba * (1 - a)
        if oa <= 0: return
        self.px[y][x] = ((r * a + br * ba * (1 - a)) / oa,
                         (g * a + bg * ba * (1 - a)) / oa,
                         (b * a + bb * ba * (1 - a)) / oa, oa)

    def to_bytes(self):
        out = bytearray(self.w * self.h * 4)
        i = 0
        for row in self.px:
            for r, g, b, a in row:
                out[i] = int(max(0, min(255, r)))
                out[i + 1] = int(max(0, min(255, g)))
                out[i + 2] = int(max(0, min(255, b)))
                out[i + 3] = int(max(0, min(255, a * 255)))
                i += 4
        return out


def draw_glow(c, cx, cy, radius, color, peak=0.35):
    """Soft radial backlight behind the emblem (quadratic falloff to TRUE zero
    well inside the bounds - no square halo at the texture edge)."""
    r2 = radius * radius
    for y in range(max(0, int(cy - radius)), min(c.h, int(cy + radius) + 1)):
        for x in range(max(0, int(cx - radius)), min(c.w, int(cx + radius) + 1)):
            d2 = (x - cx) ** 2 + (y - cy) ** 2
            if d2 >= r2: continue
            t = 1.0 - d2 / r2
            c.blend(x, y, *color, peak * t * t)


def draw_sparkle(c, cx, cy, size, color=(255, 255, 255), alpha=0.95):
    """4-point star: two slim diamonds (vertical + horizontal)."""
    for y in range(int(cy - size), int(cy + size) + 1):
        for x in range(int(cx - size), int(cx + size) + 1):
            dx, dy = abs(x - cx), abs(y - cy)
            v = dx / (size * 0.22) + dy / size          # tall diamond
            h = dx / size + dy / (size * 0.22)          # wide diamond
            d = min(v, h)
            if d < 1.0:
                c.blend(x, y, *color, alpha * (1.0 - d) ** 2)


def shade(d, y, top, bottom, base, outline_px=14, bevel_px=16):
    """Shared emblem shading from a signed distance (px, negative inside):
    dark outline ring, top bevel highlight, vertical gradient. Returns
    (r,g,b,coverage) or None when outside."""
    if d >= 0.75: return None
    cov = min(1.0, 0.75 - d)
    br, bg, bb = base
    if d > -outline_px:                                  # outline ring
        f = 0.30
        return (br * f, bg * f, bb * f, cov)
    t = (y - top) / max(1.0, bottom - top)               # vertical gradient
    f = 1.18 - 0.5 * t
    r, g, b = br * f, bg * f, bb * f
    inner = -d - outline_px
    if inner < bevel_px and t < 0.45:                    # top bevel highlight
        k = (1.0 - inner / bevel_px) * 0.5 * (1.0 - t / 0.45)
        r, g, b = r + (255 - r) * k, g + (255 - g) * k, b + (255 - b) * k
    return (r, g, b, cov)


# ---------------------------------------------------------------- bullet shapes

def bullet_distance(x, y, cx, top, tip, half_w, dome_h):
    """Signed distance (approx, px) to a shell silhouette pointing DOWN:
    domed top, straight body, tapering to a point at (cx, tip)."""
    y_taper = top + (tip - top) * 0.58
    dome_top = top + dome_h
    if y < dome_top:                                     # elliptical dome cap
        ny = (dome_top - y) / dome_h
        if ny >= 1.0:                                    # above the apex
            return math.hypot(x - cx, (ny - 1.0) * dome_h)
        return abs(x - cx) - half_w * math.sqrt(1.0 - ny * ny)
    if y <= y_taper:                                     # straight body
        return abs(x - cx) - half_w
    if y >= tip:
        return abs(x - cx) + (y - tip)
    # linear taper to the point; correct for the slanted edge so the outline
    # keeps constant thickness along the tip
    u = (tip - y) / (tip - y_taper)
    slope = half_w / (tip - y_taper)
    return (abs(x - cx) - half_w * u) / math.sqrt(1.0 + slope * slope)


def draw_bullet(c, cx, top, tip, half_w, base, ring=True, outline_px=14, bevel_px=16):
    dome_h = half_w * 0.55
    y_taper = top + (tip - top) * 0.58
    ring_y0 = top + dome_h + (tip - top) * 0.06
    ring_h = (tip - top) * 0.045
    ring2_y0 = y_taper - ring_h * 2.2                    # second groove at the shoulder
    spec_x = cx - half_w * 0.42                          # specular sheen stripe center
    spec_w = half_w * 0.18
    for y in range(c.h):
        for x in range(c.w):
            d = bullet_distance(x, y, cx, top, tip, half_w, dome_h)
            s = shade(d, y, top, tip, base, outline_px, bevel_px)
            if s is None: continue
            r, g, b, cov = s
            if d <= -outline_px:
                if y > y_taper:                          # tip darkens toward the point
                    f = 1.0 - 0.22 * (y - y_taper) / (tip - y_taper)
                    r, g, b = r * f, g * f, b * f
                k = math.exp(-((x - spec_x) / spec_w) ** 2) * 0.30  # metallic sheen
                r, g, b = r + (255 - r) * k, g + (255 - g) * k, b + (255 - b) * k
                if ring and (ring_y0 <= y <= ring_y0 + ring_h or
                             ring2_y0 <= y <= ring2_y0 + ring_h):
                    r, g, b = r * 0.55, g * 0.55, b * 0.55  # casing grooves
            c.blend(x, y, r, g, b, cov)


def draw_speed_line(c, cx, y0, y1, width, color, alpha=0.8):
    """Vertical motion streak, fading toward the top: a capsule from (cx,y0+width)
    to (cx,y1-width) with radius `width` - one distance formula covers the body and
    both rounded caps."""
    for y in range(int(y0), int(y1) + 1):
        t = (y - y0) / max(1.0, y1 - y0)
        for x in range(int(cx - width), int(cx + width) + 1):
            dx = abs(x - cx)
            dy = max(0.0, (y0 + width) - y, y - (y1 - width))
            d = math.hypot(dx, dy)
            cov = max(0.0, min(1.0, 1.0 - (d - width) / 1.5 if d > width else 1.0))
            c.blend(x, y, *color, alpha * t * cov)


# ---------------------------------------------------------------- block-piece shapes

def rounded_box_distance(x, y, cx, cy, half_w, half_h, radius):
    qx = abs(x - cx) - (half_w - radius)
    qy = abs(y - cy) - (half_h - radius)
    ox, oy = max(qx, 0.0), max(qy, 0.0)
    outside = math.hypot(ox, oy)
    inside = min(max(qx, qy), 0.0)
    return outside + inside - radius


def draw_straight_piece(c, cx, top, bottom, half_w, base, outline_px=14, bevel_px=18):
    """Vertical 1x4 straight piece emblem: one bold rounded bar with subtle
    cell seams, matching the generated block lighting language."""
    cy = (top + bottom) * 0.5
    half_h = (bottom - top) * 0.5
    radius = half_w * 0.32
    spec_x = cx - half_w * 0.38
    spec_w = half_w * 0.22
    seam_ys = [top + (bottom - top) * u for u in (0.25, 0.5, 0.75)]

    for y in range(c.h):
        for x in range(c.w):
            d = rounded_box_distance(x, y, cx, cy, half_w, half_h, radius)
            s = shade(d, y, top, bottom, base, outline_px, bevel_px)
            if s is None: continue
            r, g, b, cov = s
            if d <= -outline_px:
                k = math.exp(-((x - spec_x) / spec_w) ** 2) * 0.22
                r, g, b = r + (255 - r) * k, g + (255 - g) * k, b + (255 - b) * k
                for seam_y in seam_ys:
                    seam = math.exp(-((y - seam_y) / 3.8) ** 2)
                    r, g, b = r * (1.0 - 0.28 * seam), g * (1.0 - 0.28 * seam), b * (1.0 - 0.28 * seam)
            c.blend(x, y, r, g, b, cov)


def draw_square_piece(c, cx, cy, half_size, base, outline_px=14, bevel_px=18, alpha=1.0):
    """2x2 square piece emblem with a cross seam, matching the generated block style."""
    top = cy - half_size
    bottom = cy + half_size
    radius = half_size * 0.18
    spec_x = cx - half_size * 0.36
    spec_w = half_size * 0.24

    for y in range(c.h):
        for x in range(c.w):
            d = rounded_box_distance(x, y, cx, cy, half_size, half_size, radius)
            s = shade(d, y, top, bottom, base, outline_px, bevel_px)
            if s is None: continue
            r, g, b, cov = s
            if d <= -outline_px:
                k = math.exp(-((x - spec_x) / spec_w) ** 2) * 0.20
                r, g, b = r + (255 - r) * k, g + (255 - g) * k, b + (255 - b) * k
                vertical = math.exp(-((x - cx) / 3.8) ** 2)
                horizontal = math.exp(-((y - cy) / 3.8) ** 2)
                seam = max(vertical, horizontal)
                r, g, b = r * (1.0 - 0.28 * seam), g * (1.0 - 0.28 * seam), b * (1.0 - 0.28 * seam)
            c.blend(x, y, r, g, b, cov * alpha)


# ---------------------------------------------------------------- renderers

def render_icon_bullet(path):
    """Card artwork: silver shell plunging down, speed lines above, sparkles."""
    S = 512
    c = Canvas(S, S)
    silver = (214, 218, 228)
    draw_glow(c, S / 2, S / 2 + 10, 235, (235, 240, 255), peak=0.30)
    for lx, ly0, ly1, w in ((150, 96, 210, 9), (362, 120, 240, 9), (256, 50, 120, 11)):
        draw_speed_line(c, lx, ly0, ly1, w, (240, 244, 255), alpha=0.55)
    draw_bullet(c, S / 2, 128, 448, 92, silver, outline_px=15, bevel_px=20)
    rng = random.Random("bullet-icon")
    for sx, sy, sz in ((118, 312, 26), (398, 286, 20), (352, 410, 15)):
        draw_sparkle(c, sx + rng.randint(-4, 4), sy + rng.randint(-4, 4), sz)
    write_png(path, S, S, c.to_bytes())


def render_icon_spike_supply(path):
    """Card artwork: a clean white straight piece appearing more often."""
    S = 512
    c = Canvas(S, S)
    pearl = (224, 232, 235)
    draw_glow(c, S / 2, S / 2, 185, (232, 238, 240), peak=0.24)
    draw_straight_piece(c, S / 2, 82, 440, 68, pearl, outline_px=15, bevel_px=22)

    for lx, ly0, ly1, w in ((158, 320, 408, 7), (354, 104, 202, 7)):
        draw_speed_line(c, lx, ly0, ly1, w, (248, 252, 255), alpha=0.36)
    for sx, sy, sz in ((154, 122, 22), (360, 350, 26), (332, 154, 14)):
        draw_sparkle(c, sx, sy, sz, color=(252, 255, 255), alpha=0.9)

    write_png(path, S, S, c.to_bytes())


def render_icon_cube_supply(path):
    """Card artwork: a clean white square piece appearing more often."""
    S = 512
    c = Canvas(S, S)
    pearl = (224, 232, 235)
    draw_glow(c, S / 2, S / 2, 185, (232, 238, 240), peak=0.24)
    draw_square_piece(c, S / 2, S / 2, 132, pearl, outline_px=15, bevel_px=22)

    for lx, ly0, ly1, w in ((142, 314, 404, 7), (370, 96, 190, 7)):
        draw_speed_line(c, lx, ly0, ly1, w, (248, 252, 255), alpha=0.34)
    for sx, sy, sz in ((146, 130, 22), (372, 348, 26), (342, 152, 14)):
        draw_sparkle(c, sx, sy, sz, color=(252, 255, 255), alpha=0.9)

    write_png(path, S, S, c.to_bytes())


def render_icon_vector_guide(path):
    """Card artwork: active block, projection line, translucent landing ghost."""
    S = 512
    c = Canvas(S, S)
    pearl = (224, 232, 235)
    guide = (248, 252, 255)
    draw_glow(c, S / 2, S / 2, 210, (232, 238, 240), peak=0.24)

    draw_square_piece(c, S / 2, 146, 76, pearl, outline_px=13, bevel_px=18)
    draw_speed_line(c, S / 2, 215, 312, 8, guide, alpha=0.62)
    draw_speed_line(c, S / 2 - 42, 238, 296, 4, guide, alpha=0.28)
    draw_speed_line(c, S / 2 + 42, 238, 296, 4, guide, alpha=0.28)
    draw_square_piece(c, S / 2, 370, 102, pearl, outline_px=13, bevel_px=18, alpha=0.42)

    for sx, sy, sz in ((140, 142, 20), (374, 352, 24), (356, 182, 14)):
        draw_sparkle(c, sx, sy, sz, color=(252, 255, 255), alpha=0.88)

    write_png(path, S, S, c.to_bytes())


def render_icon_high_friction(path):
    """Card artwork: two pale blocks gripping at a bright contact seam."""
    S = 512
    c = Canvas(S, S)
    pearl = (224, 232, 235)
    guide = (248, 252, 255)
    draw_glow(c, S / 2, S / 2, 205, (232, 238, 240), peak=0.24)

    draw_square_piece(c, S / 2 - 46, 190, 96, pearl, outline_px=14, bevel_px=20)
    draw_square_piece(c, S / 2 + 46, 322, 96, pearl, outline_px=14, bevel_px=20)

    for x in (188, 222, 256, 290, 324):
        draw_speed_line(c, x, 238, 278, 5, guide, alpha=0.52)
    for sx, sy, sz in ((144, 170, 22), (368, 340, 24), (340, 200, 14)):
        draw_sparkle(c, sx, sy, sz, color=(252, 255, 255), alpha=0.88)

    write_png(path, S, S, c.to_bytes())


def render_block_bullet(path):
    """The in-game 1x1 projectile piece: aged bronze shell, pointy bottom,
    same lighting language as the tetromino block sprites (PPU 256)."""
    S = 256
    c = Canvas(S, S)
    bronze = (196, 138, 78)
    draw_bullet(c, S / 2, 2, 254, 100, bronze, outline_px=11, bevel_px=14)
    draw_sparkle(c, 96, 52, 13, alpha=0.7)               # glint on the dome
    write_png(path, S, S, c.to_bytes())


def render_piece_bullet(path):
    """ThemeSkins whole-piece sprite for Block_Bullet (Classic; all themes fall back
    to it): the block_bullet art on the piece-sprite canvas - CELL 256 + BLEED 32
    margins, PPU 256, matching generate_piece_sprites.py conventions."""
    S, BLEED = 320, 32
    c = Canvas(S, S)
    bronze = (196, 138, 78)
    draw_bullet(c, S / 2, BLEED + 2, S - BLEED - 2, 100, bronze, outline_px=11, bevel_px=14)
    draw_sparkle(c, BLEED + 64, BLEED + 50, 13, alpha=0.7)
    write_png(path, S, S, c.to_bytes())


SKINS_DIR = os.path.join(os.path.dirname(__file__), "..",
                         "Assets", "Resources", "Skins", "Classic")

ARTWORK = {
    "icon_bullet.png": render_icon_bullet,
    "icon_spike_supply.png": render_icon_spike_supply,
    "icon_cube_supply.png": render_icon_cube_supply,
    "icon_vector_guide.png": render_icon_vector_guide,
    "icon_high_friction.png": render_icon_high_friction,
    "block_bullet.png": render_block_bullet,
}
SKIN_ARTWORK = {
    "piece_Bullet.png": render_piece_bullet,
}

if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    for name, fn in ARTWORK.items():
        out = os.path.abspath(os.path.join(OUT_DIR, name))
        fn(out)
        print(out)
    for name, fn in SKIN_ARTWORK.items():
        out = os.path.abspath(os.path.join(SKINS_DIR, name))
        fn(out)
        print(out)
