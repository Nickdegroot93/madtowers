#!/usr/bin/env python3
"""Procedurally renders the floor base sprite for a theme. Two Classic variants:

  ground.png        - stone tower base with brick courses and a door (active)
  render_hill()     - grassy brown hill preset, currently unused (kept as a
                      starting point for nature-flavored themes)

Pure stdlib, deterministic. Output: Assets/Resources/Skins/Classic/ at 128 px/unit.

Layout contract with PlayAreaController.ApplyGroundSkin (both variants):
- the flat plateau spans PLATEAU_FRACTION (0.85) of the canvas width, centered
- the plateau surface is the exact top edge of the canvas
Shared style rules live in STYLE.md (outline = 30% value, top bevel, gradient).
"""
import zlib, struct, math, random, os

PPU = 128
W, H = 2080, 1568              # 16.25 x 12.25 world units
PLATEAU = 1768                 # px; PLATEAU_FRACTION = 1768/2080 = 0.85
OUTLINE = 14

OUT_DIR = os.path.join(os.path.dirname(__file__), "..",
                       "Assets", "Resources", "Skins", "Classic")


# ---------- shared helpers ----------

def write_png(path, w, h, px):
    def chunk(tag, data):
        c = tag + data
        return struct.pack(">I", len(data)) + c + struct.pack(">I", zlib.crc32(c) & 0xffffffff)
    raw = bytearray()
    stride = w * 4
    for y in range(h):
        raw.append(0)
        raw += px[y * stride:(y + 1) * stride]
    ihdr = struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr)
                + chunk(b"IDAT", zlib.compress(bytes(raw), 6)) + chunk(b"IEND", b""))


def rasterize_lines(w, h, segs, half_width):
    buf = [0.0] * (w * h)
    for (x1, y1), (x2, y2) in segs:
        minx = max(0, int(min(x1, x2) - half_width - 1))
        maxx = min(w - 1, int(max(x1, x2) + half_width + 1))
        miny = max(0, int(min(y1, y2) - half_width - 1))
        maxy = min(h - 1, int(max(y1, y2) + half_width + 1))
        vx, vy = x2 - x1, y2 - y1
        vv = vx * vx + vy * vy or 1e-6
        for py in range(miny, maxy + 1):
            row = py * w
            for px_ in range(minx, maxx + 1):
                t = ((px_ - x1) * vx + (py - y1) * vy) / vv
                t = 0.0 if t < 0 else (1.0 if t > 1 else t)
                dist = math.hypot(px_ - (x1 + vx * t), py - (y1 + vy * t))
                v = half_width + 0.8 - dist
                if v > 0:
                    v = min(v, 1.0)
                    if v > buf[row + px_]:
                        buf[row + px_] = v
    return buf


def smax(a, b, k):
    h = min(1.0, max(0.0, 0.5 - 0.5 * (b - a) / k))
    return (b * h + a * (1 - h)) + k * h * (1 - h)


def grain(x, y):
    n = ((x * 374761393 + y * 668265263) ^ (x * y * 31)) & 1023
    return 1.0 + (n / 1023.0 - 0.5) * 0.05


def faceted_profiles(rng, taper, h):
    """Piecewise-linear side offsets, faded in with depth so the top edge stays
    perfectly straight at plateau width."""
    STEP = 130
    def make():
        knots = [rng.uniform(-34, 34) for _ in range(h // STEP + 2)]
        phase = rng.uniform(0, math.tau)
        prof = []
        for y in range(h):
            i, frac = divmod(y, STEP)
            jag = knots[i] * (1 - frac / STEP) + knots[i + 1] * (frac / STEP)
            jag += 5 * math.sin(y * 0.06 + phase)
            fade = min(1.0, y / 180.0)
            prof.append(min(W / 2 - 4, PLATEAU / 2 + taper * y + jag * fade))
        return prof
    return make(), make()


def put(px, x, y, rr, gg, bb, alpha):
    o = (y * W + x) * 4
    px[o] = min(255, max(0, int(rr)))
    px[o + 1] = min(255, max(0, int(gg)))
    px[o + 2] = min(255, max(0, int(bb)))
    px[o + 3] = int(alpha * 255)


# ---------- variant A: grassy hill ----------

def render_hill():
    rng = random.Random("classic-hill")
    cx = W / 2
    GRASS = (104, 188, 76)
    DIRT = (138, 99, 64)
    outline_col = tuple(v * 0.30 for v in DIRT)

    hw_l, hw_r = faceted_profiles(rng, taper=0.12, h=H)
    strata = [1.0 + (rng.random() - 0.5) * 0.16 for _ in range(14)]

    rocks = []
    for _ in range(11):
        ry = rng.uniform(220, H - 120)
        rocks.append((cx + rng.uniform(-0.8, 0.8) * (PLATEAU / 2), ry,
                      rng.uniform(30, 80), 0))
    rocks = [(rx, ry, ra, ra * rng.uniform(0.55, 0.85)) for rx, ry, ra, _ in rocks]

    segs = []
    for _ in range(7):
        x = cx + rng.uniform(-0.85, 0.85) * (PLATEAU / 2)
        y = rng.uniform(160, H * 0.7)
        ang = math.pi / 2 + rng.uniform(-0.8, 0.8)
        for _ in range(4):
            ln = rng.uniform(60, 130)
            nx, ny = x + math.cos(ang) * ln, y + math.sin(ang) * ln
            segs.append(((x, y), (nx, ny)))
            x, y = nx, ny
            ang += rng.uniform(-0.7, 0.7)
    cracks = rasterize_lines(W, H, segs, 2.6)

    pg = rng.uniform(0, math.tau)
    def grass_bottom(x):  # wavy underside of the grass cap
        return 56 + 16 * math.sin(x * 0.012 + pg) + 9 * math.sin(x * 0.031 + 2 * pg)

    px = bytearray(W * H * 4)
    for y in range(H):
        grad = 1.05 - 0.50 * (y / H)
        row_rocks = [r for r in rocks if abs(y - r[1]) < r[3] + 2]
        xl, xr = cx - hw_l[y], cx + hw_r[y]
        for x in range(max(0, int(xl) - 2), min(W, int(xr) + 3)):
            d_side = max(x - xr, xl - x)
            d = smax(d_side, -y, 24.0)
            if d > 0.5:
                continue
            alpha = min(1.0, 0.5 - d)
            gb = grass_bottom(x)
            in_grass = y < gb
            if in_grass:
                rr, gg, bb = GRASS
                rr, gg, bb = rr * grad, gg * grad, bb * grad
                if OUTLINE <= y < OUTLINE + 16:      # sunlit top of the grass
                    f = 1.0 + 0.20 * (1.0 - (y - OUTLINE) / 16.0)
                    rr, gg, bb = rr * f, gg * f, bb * f
                elif y > gb - 9:                     # shaded underside lip
                    rr, gg, bb = rr * 0.82, gg * 0.82, bb * 0.82
            else:
                b = strata[min(13, int((y + 40 * math.sin(x * 0.0045 + 1.7)
                                        + 18 * math.sin(x * 0.013)) // 150) % 14)]
                rr, gg, bb = (DIRT[0] * grad * b, DIRT[1] * grad * b, DIRT[2] * grad * b)
                if d < -OUTLINE:
                    for rx_, ry_, ra_, rb_ in row_rocks:
                        e = ((x - rx_) / ra_) ** 2 + ((y - ry_) / rb_) ** 2
                        if e < 1.0:
                            f = 1.0 - 0.17 * min(1.0, (1.0 - e) * 3.0)
                            rr, gg, bb = rr * f, gg * f, bb * f
                            break
                    ck = cracks[y * W + x]
                    if ck > 0:
                        f = 1.0 - 0.40 * ck
                        rr, gg, bb = rr * f, gg * f, bb * f
            f = grain(x, y)
            rr, gg, bb = rr * f, gg * f, bb * f
            t_out = min(1.0, max(0.0, (d + OUTLINE) / 1.5 + 0.5))
            if t_out > 0:
                rr = rr * (1 - t_out) + outline_col[0] * grad * t_out
                gg = gg * (1 - t_out) + outline_col[1] * grad * t_out
                bb = bb * (1 - t_out) + outline_col[2] * grad * t_out
            put(px, x, y, rr, gg, bb, alpha)

    out = os.path.abspath(os.path.join(OUT_DIR, "ground_hill.png"))
    write_png(out, W, H, px)
    print(f"{out}  (hill, {W}x{H}, plateau fraction {PLATEAU / W})")


# ---------- variant B: stone tower base ----------

def render_tower():
    """Two crisp axis-aligned rectangles - platform slab on top of a brick wall -
    each with a full dark outline, so a dark line separates them where they meet.
    No rounded corners, no edge wobble, no corner shading."""
    cx = W / 2
    STONE = (148, 142, 132)
    WOOD = (124, 84, 50)
    outline_col = tuple(v * 0.30 for v in STONE)

    SLAB_H = 96                  # top platform slab (plateau width)
    SLAB_STONE_W = 320           # slab divided into long stones by dark joints
    JOINT = 7
    body_hw = PLATEAU / 2 - 170  # tower body is narrower than the slab

    # staggered brick grid
    BRICK_H, BRICK_W, MORTAR = 88, 224, 7

    # door (arched, wooden) near the top of the body
    door_w, door_top, door_bot = 170, SLAB_H + 60, SLAB_H + 420
    arch_r = door_w / 2

    # two small windows
    wins = [(cx - body_hw * 0.55, SLAB_H + 240), (cx + body_hw * 0.55, SLAB_H + 240)]

    slab_l, slab_r = int(cx - PLATEAU / 2), int(cx + PLATEAU / 2)
    body_l, body_r = int(cx - body_hw), int(cx + body_hw)

    px = bytearray(W * H * 4)
    for y in range(H):
        grad = 1.06 - 0.46 * (y / H)
        in_slab_row = y < SLAB_H
        xl, xr = (slab_l, slab_r) if in_slab_row else (body_l, body_r)
        for x in range(xl, xr):
            if in_slab_row:
                edge = min(x - slab_l, slab_r - 1 - x, y, SLAB_H - 1 - y)
                if edge < OUTLINE:
                    rr, gg, bb = (c * grad for c in outline_col)
                else:
                    rr, gg, bb = (STONE[0] * grad, STONE[1] * grad, STONE[2] * grad)
                    if y < OUTLINE + 18:  # flat highlight strip along the top
                        f = 1.0 + 0.18
                        rr, gg, bb = rr * f, gg * f, bb * f
                    if (x - slab_l) % SLAB_STONE_W < JOINT:  # stone joints
                        rr, gg, bb = (c * grad for c in outline_col)
            else:
                edge = min(x - body_l, body_r - 1 - x, y - SLAB_H)
                if edge < OUTLINE:
                    rr, gg, bb = (c * grad for c in outline_col)
                else:
                    rr, gg, bb = (STONE[0] * grad, STONE[1] * grad, STONE[2] * grad)
                    # brick courses with staggered mortar joints
                    row = int((y - SLAB_H) // BRICK_H)
                    yy = (y - SLAB_H) % BRICK_H
                    xx = (x - cx + (BRICK_W // 2 if row % 2 else 0)) % BRICK_W
                    bshade = 1.0 + (((row * 7 + int((x - cx + 10000) // BRICK_W) * 13) % 5) - 2) * 0.03
                    rr, gg, bb = rr * bshade, gg * bshade, bb * bshade
                    if yy < MORTAR or xx < MORTAR:
                        rr, gg, bb = rr * 0.55, gg * 0.55, bb * 0.55
                    # door
                    dx = x - cx
                    if door_top + arch_r >= y >= door_top and dx * dx + (y - (door_top + arch_r)) ** 2 < arch_r * arch_r \
                            or door_top + arch_r < y < door_bot and abs(dx) < arch_r:
                        de = arch_r - math.hypot(dx, y - (door_top + arch_r)) if y <= door_top + arch_r \
                            else min(arch_r - abs(dx), door_bot - y)
                        if de < 10:
                            rr, gg, bb = (c * grad for c in outline_col)
                        else:
                            plank = 0.92 + 0.08 * ((int(dx + 1000) // 34) % 2)
                            f = grad * plank
                            rr, gg, bb = WOOD[0] * f, WOOD[1] * f, WOOD[2] * f
                    # windows
                    for wx, wy in wins:
                        if abs(x - wx) < 46 and abs(y - wy) < 60:
                            if abs(x - wx) > 38 or abs(y - wy) > 52:
                                rr, gg, bb = (c * grad for c in outline_col)
                            else:
                                rr, gg, bb = 28, 26, 32
                            break

            f = grain(x, y)
            put(px, x, y, rr * f, gg * f, bb * f, 1.0)

    out = os.path.abspath(os.path.join(OUT_DIR, "ground.png"))
    write_png(out, W, H, px)
    print(f"{out}  (tower, {W}x{H}, plateau fraction {PLATEAU / W})")


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    render_tower()
