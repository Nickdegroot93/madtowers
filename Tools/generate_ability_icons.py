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


def draw_ring(c, cx, cy, radius, thickness, color, alpha=0.9):
    """Soft circular rim (the 'pocket' bubble). Alpha falls off to zero at the band
    edges so the ring reads crisp but anti-aliased."""
    outer = radius + thickness
    for y in range(max(0, int(cy - outer)), min(c.h, int(cy + outer) + 1)):
        for x in range(max(0, int(cx - outer)), min(c.w, int(cx + outer) + 1)):
            d = math.hypot(x - cx, y - cy)
            edge = thickness - abs(d - radius)
            if edge <= 0: continue
            c.blend(x, y, *color, alpha * min(1.0, edge / thickness))


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


def draw_brake_bar(c, cx, cy, half_w, half_h, base, outline_px=14, bevel_px=16):
    """Bold horizontal rounded plate - the brake the falling block eases onto."""
    top, bottom = cy - half_h, cy + half_h
    radius = half_h * 0.55
    spec_x = cx - half_w * 0.4
    spec_w = half_w * 0.5
    for y in range(c.h):
        for x in range(c.w):
            d = rounded_box_distance(x, y, cx, cy, half_w, half_h, radius)
            s = shade(d, y, top, bottom, base, outline_px, bevel_px)
            if s is None: continue
            r, g, b, cov = s
            if d <= -outline_px:
                k = math.exp(-((x - spec_x) / spec_w) ** 2) * 0.24
                r, g, b = r + (255 - r) * k, g + (255 - g) * k, b + (255 - b) * k
            c.blend(x, y, r, g, b, cov)


def render_icon_air_brake(path):
    """Card artwork: a falling block easing onto a bold brake plate, motion streaks
    above tapering as it slows."""
    S = 512
    c = Canvas(S, S)
    pearl = (224, 232, 235)
    plate = (208, 218, 225)
    streak = (108, 124, 152)                 # muted slate - reads as motion on the white card
    draw_glow(c, S / 2, S / 2, 205, (232, 238, 240), peak=0.24)

    for lx, ly0, ly1, w, a in ((S / 2, 30, 156, 13, 0.9),
                               (S / 2 - 78, 60, 168, 9, 0.62),
                               (S / 2 + 78, 60, 168, 9, 0.62)):
        draw_speed_line(c, lx, ly0, ly1, w, streak, alpha=a)

    draw_square_piece(c, S / 2, 256, 90, pearl, outline_px=14, bevel_px=20)
    draw_brake_bar(c, S / 2, 398, 152, 30, plate, outline_px=14, bevel_px=16)

    for sx, sy, sz in ((142, 196, 22), (372, 300, 24), (348, 214, 14)):
        draw_sparkle(c, sx, sy, sz, color=(252, 255, 255), alpha=0.88)

    write_png(path, S, S, c.to_bytes())


def render_icon_foresight(path):
    """Card artwork: two upcoming pieces in a queue - the near one bright and large,
    the one seen behind it smaller and faded (planning ahead)."""
    S = 512
    c = Canvas(S, S)
    pearl = (224, 232, 235)
    draw_glow(c, S / 2, S / 2, 205, (232, 238, 240), peak=0.24)

    # immediate-next: bright, large, upper (mirrors the HUD's primary slot)
    draw_square_piece(c, S / 2, 196, 104, pearl, outline_px=14, bevel_px=20)
    # the one after: smaller, dimmer, lower - the extra foresight buys
    draw_square_piece(c, S / 2, 372, 66, pearl, outline_px=12, bevel_px=16, alpha=0.4)

    for sx, sy, sz in ((150, 150, 22), (372, 330, 18), (350, 170, 13)):
        draw_sparkle(c, sx, sy, sz, color=(252, 255, 255), alpha=0.85)

    write_png(path, S, S, c.to_bytes())


def render_icon_shrink(path):
    """Card artwork: a large faded block collapsing into a small solid one (reduce),
    with inward corner ticks for the squeeze."""
    S = 512
    c = Canvas(S, S)
    pearl = (224, 232, 235)
    guide = (130, 146, 170)
    draw_glow(c, S / 2, S / 2, 200, (232, 238, 240), peak=0.24)

    # the "before": large, faded outline ghost
    draw_square_piece(c, S / 2, S / 2, 150, pearl, outline_px=14, bevel_px=20, alpha=0.28)
    # the "after": small, bright, solid in the centre
    draw_square_piece(c, S / 2, S / 2, 70, pearl, outline_px=12, bevel_px=16)

    # inward compression ticks from each corner toward the centre
    for cx, cy in ((150, 150), (362, 150), (150, 362), (362, 362)):
        draw_sparkle(c, cx, cy, 18, color=(252, 255, 255), alpha=0.82)

    write_png(path, S, S, c.to_bytes())


def draw_tall_brick(c, cx, cy, half_w, half_h, base, outline_px=14, bevel_px=18):
    """A 1x2 vertical brick: rounded rect with a single horizontal mid seam (the two cells)."""
    top, bottom = cy - half_h, cy + half_h
    radius = half_w * 0.30
    spec_x = cx - half_w * 0.38
    spec_w = half_w * 0.24
    for y in range(c.h):
        for x in range(c.w):
            d = rounded_box_distance(x, y, cx, cy, half_w, half_h, radius)
            s = shade(d, y, top, bottom, base, outline_px, bevel_px)
            if s is None: continue
            r, g, b, cov = s
            if d <= -outline_px:
                k = math.exp(-((x - spec_x) / spec_w) ** 2) * 0.22
                r, g, b = r + (255 - r) * k, g + (255 - g) * k, b + (255 - b) * k
                seam = math.exp(-((y - cy) / 3.8) ** 2)
                r, g, b = r * (1 - 0.28 * seam), g * (1 - 0.28 * seam), b * (1 - 0.28 * seam)
            c.blend(x, y, r, g, b, cov)


def render_icon_pip(path):
    """Card artwork: a small slate 1x1 brick dropping in among the pieces."""
    S = 512
    c = Canvas(S, S)
    slate = (156, 166, 180)
    streak = (128, 144, 168)
    draw_glow(c, S / 2, S / 2, 195, (232, 238, 240), peak=0.22)
    for lx, ly0, ly1, w, a in ((S / 2, 58, 214, 12, 0.85),
                               (S / 2 - 68, 94, 224, 8, 0.55),
                               (S / 2 + 68, 94, 224, 8, 0.55)):
        draw_speed_line(c, lx, ly0, ly1, w, streak, alpha=a)
    draw_square_piece(c, S / 2, 334, 92, slate, outline_px=14, bevel_px=20)
    for sx, sy, sz in ((148, 306, 18), (366, 306, 16)):
        draw_sparkle(c, sx, sy, sz, color=(252, 255, 255), alpha=0.8)
    write_png(path, S, S, c.to_bytes())


def render_icon_domino(path):
    """Card artwork: a small slate 1x2 brick dropping in among the pieces."""
    S = 512
    c = Canvas(S, S)
    slate = (156, 166, 180)
    streak = (128, 144, 168)
    draw_glow(c, S / 2, S / 2, 200, (232, 238, 240), peak=0.22)
    for lx, ly0, ly1, w, a in ((S / 2, 40, 150, 12, 0.85),
                               (S / 2 - 78, 70, 156, 8, 0.52),
                               (S / 2 + 78, 70, 156, 8, 0.52)):
        draw_speed_line(c, lx, ly0, ly1, w, streak, alpha=a)
    draw_tall_brick(c, S / 2, 332, 70, 136, slate, outline_px=14, bevel_px=18)
    for sx, sy, sz in ((140, 250, 18), (374, 250, 16)):
        draw_sparkle(c, sx, sy, sz, color=(252, 255, 255), alpha=0.8)
    write_png(path, S, S, c.to_bytes())


def _heart_inside(nx, ny):
    # classic heart implicit (nx,ny normalized, y up); inside when <= 0
    v = nx * nx + ny * ny - 1.0
    return v * v * v - nx * nx * ny * ny * ny <= 0.0


def draw_heart(c, cx, cy, R, base, outline=(46, 32, 38)):
    """A chunky heart: filled with a vertical gradient and a dark outline ring."""
    for y in range(int(cy - R * 1.7), int(cy + R * 1.8)):
        for x in range(int(cx - R * 1.5), int(cx + R * 1.5)):
            nx = (x - cx) / R
            ny = (cy - y) / R + 0.30
            if not _heart_inside(nx, ny):
                continue
            if _heart_inside(nx * 1.13, (ny - 0.03) * 1.13):  # smaller -> interior fill
                t = max(0.0, min(1.0, (y - (cy - R)) / (2.2 * R)))
                f = 1.14 - 0.42 * t
                c.blend(x, y, base[0] * f, base[1] * f, base[2] * f, 1.0)
            else:
                c.blend(x, y, outline[0], outline[1], outline[2], 1.0)


def render_icon_recovery(path):
    """Card artwork: a heart - the life you recover; the breather after a loss."""
    S = 512
    c = Canvas(S, S)
    heart = (232, 84, 96)
    draw_glow(c, S / 2, S / 2, 200, (255, 208, 212), peak=0.26)
    draw_heart(c, S / 2, S / 2 - 4, 118, heart)
    for sx, sy, sz in ((150, 170, 18), (366, 170, 16), (300, 372, 13)):
        draw_sparkle(c, sx, sy, sz, color=(255, 240, 242), alpha=0.8)
    write_png(path, S, S, c.to_bytes())


def render_icon_slomo(path):
    """Card artwork: a block easing down through slow, spaced motion dashes (not the
    long continuous streaks of a fast drop)."""
    S = 512
    c = Canvas(S, S)
    pearl = (224, 232, 235)
    slow = (120, 150, 185)
    draw_glow(c, S / 2, S / 2, 200, (232, 238, 240), peak=0.24)
    for y0 in (72, 132, 192):
        draw_speed_line(c, S / 2, y0, y0 + 34, 12, slow, alpha=0.72)
    draw_square_piece(c, S / 2, 358, 96, pearl, outline_px=14, bevel_px=20)
    write_png(path, S, S, c.to_bytes())


def render_icon_sacrifice(path):
    """Card artwork: rare-blue abyss laser taking one brick while another is paid above."""
    S = 512
    c = Canvas(S, S)
    rare = (84, 155, 245)
    pearl = (224, 232, 235)
    draw_glow(c, S / 2, S / 2, 210, rare, peak=0.27)

    # Abyss laser: thick blue-white bar with small end sparks.
    for y in range(328, 350):
        v = 1.0 - abs((y - 339) / 13.0)
        alpha = max(0.0, min(1.0, v)) ** 0.45
        for x in range(86, 426):
            edge = min(1.0, min(x - 86, 426 - x) / 26.0)
            c.blend(x, y, rare[0] + 60, rare[1] + 45, 255, 0.20 + 0.58 * alpha * edge)
    for y in range(336, 342):
        for x in range(112, 400):
            c.blend(x, y, 245, 252, 255, 0.88)

    draw_square_piece(c, S / 2, 234, 86, pearl, outline_px=13, bevel_px=18)
    draw_square_piece(c, S / 2, 390, 70, rare, outline_px=12, bevel_px=16)

    for sx, sy, sz in ((116, 338, 18), (398, 338, 18), (338, 186, 14), (174, 246, 12)):
        draw_sparkle(c, sx, sy, sz, color=(245, 252, 255), alpha=0.88)

    write_png(path, S, S, c.to_bytes())


def _shield_inside(x, y, cx, top, bottom, half_w):
    if y < top or y > bottom:
        return False
    mid = top + (bottom - top) * 0.45
    if y <= mid:
        w = half_w
    else:
        u = (y - mid) / (bottom - mid)
        w = half_w * math.sqrt(max(0.0, 1.0 - u * u))  # taper to a point
    return abs(x - cx) <= w


def draw_shield(c, cx, cy, half_w, half_h, base, outline=(40, 44, 60)):
    """A heraldic shield: flat-topped, tapering to a point, dark outline ring + top bevel."""
    top, bottom = cy - half_h, cy + half_h
    inset = 13
    for y in range(int(top - 2), int(bottom + 2)):
        for x in range(int(cx - half_w - 2), int(cx + half_w + 2)):
            if not _shield_inside(x, y, cx, top, bottom, half_w):
                continue
            if _shield_inside(x, y, cx, top + inset, bottom - inset * 1.6, half_w - inset):
                t = (y - top) / (2 * half_h)
                f = 1.16 - 0.42 * t
                r, g, b = base[0] * f, base[1] * f, base[2] * f
                rise = y - (top + inset)
                if 0 <= rise < 26:                       # top bevel highlight
                    k = (1.0 - rise / 26.0) * 0.36
                    r, g, b = r + (255 - r) * k, g + (255 - g) * k, b + (255 - b) * k
                c.blend(x, y, r, g, b, 1.0)
            else:
                c.blend(x, y, outline[0], outline[1], outline[2], 1.0)


def render_icon_last_stand(path):
    """Card artwork: a steel shield - holding the line on your last life."""
    S = 512
    c = Canvas(S, S)
    steel = (96, 134, 196)
    draw_glow(c, S / 2, S / 2, 200, (210, 224, 245), peak=0.26)
    draw_shield(c, S / 2, S / 2 - 6, 128, 150, steel)
    for sx, sy, sz in ((146, 176, 18), (366, 176, 16)):
        draw_sparkle(c, sx, sy, sz, color=(235, 244, 255), alpha=0.8)
    write_png(path, S, S, c.to_bytes())


def render_icon_rebound(path):
    """Card artwork: a block beamed back up to safety on a cyan rescue light, dissolving
    into magic sparkles."""
    S = 512
    c = Canvas(S, S)
    pearl = (224, 232, 235)
    cyan = (96, 200, 232)
    draw_glow(c, S / 2, S / 2, 200, (200, 238, 248), peak=0.26)
    for lx, ly0, ly1, w, a in ((S / 2, 248, 470, 13, 0.82),
                               (S / 2 - 66, 286, 462, 8, 0.5),
                               (S / 2 + 66, 286, 462, 8, 0.5)):
        draw_speed_line(c, lx, ly0, ly1, w, cyan, alpha=a)
    draw_square_piece(c, S / 2, 168, 90, pearl, outline_px=14, bevel_px=20)
    for sx, sy, sz in ((150, 150, 20), (366, 150, 18), (S // 2, 90, 16), (206, 250, 12), (314, 250, 12)):
        draw_sparkle(c, sx, sy, sz, color=(220, 245, 255), alpha=0.85)
    write_png(path, S, S, c.to_bytes())


def render_icon_pocket_cache(path):
    """Card artwork: a block tucked inside a glowing circular 'pocket' bubble, with a
    second faded block beside it - the shape you swap in and out of the hold."""
    S = 512
    c = Canvas(S, S)
    pearl = (224, 232, 235)
    bubble = (190, 224, 255)
    cx, cy = S / 2, S / 2 + 6
    draw_glow(c, cx, cy, 196, bubble, peak=0.30)        # the pocket's glassy fill
    draw_ring(c, cx, cy, 150, 14, (210, 236, 255), alpha=0.92)  # bright rim
    draw_ring(c, cx, cy, 116, 6, (210, 236, 255), alpha=0.30)   # faint inner highlight
    draw_square_piece(c, cx, cy, 78, pearl, outline_px=13, bevel_px=18)  # the cached block
    draw_square_piece(c, S / 2 + 150, 150, 40, pearl, outline_px=10, bevel_px=12, alpha=0.4)  # the one swapping in
    for sx, sy, sz in ((132, 150, 18), (360, 360, 14), (150, 372, 11)):
        draw_sparkle(c, sx, sy, sz, color=(236, 248, 255), alpha=0.85)
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
    "icon_air_brake.png": render_icon_air_brake,
    "icon_foresight.png": render_icon_foresight,
    "icon_shrink.png": render_icon_shrink,
    "icon_pip.png": render_icon_pip,
    "icon_domino.png": render_icon_domino,
    "icon_recovery.png": render_icon_recovery,
    "icon_slomo.png": render_icon_slomo,
    "icon_sacrifice.png": render_icon_sacrifice,
    "icon_last_stand.png": render_icon_last_stand,
    "icon_rebound.png": render_icon_rebound,
    "icon_pocket_cache.png": render_icon_pocket_cache,
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
