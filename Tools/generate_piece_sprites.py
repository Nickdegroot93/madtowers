#!/usr/bin/env python3
"""Procedurally renders Tricky-Towers-style whole-piece tetromino sprites, PER THEME.

Pure stdlib (no PIL). Output: piece_X.png into Assets/Resources/Skins/<Theme>/ for each
entry in THEME_PRESETS. A theme without its own entry falls back to the Classic pieces
at runtime (ThemeSkins fallback chain) - adding a block look for a theme = adding one
preset dict here and rerunning. Style per piece: rounded silhouette, dark colored
outline, vertical gradient, top bevel highlight, embossed crack lines along cell seams.
Deterministic per shape (seeded) so regeneration is stable. Style rules: STYLE.md
(the 7 shapes keep their hue identities in every theme - shift saturation/value only).
"""
import zlib, struct, math, random, os

CELL, BLEED = 256, 32
R = 22            # silhouette corner radius (px)
OUTLINE = 13      # outline thickness (px)
BEVEL = 18        # bevel band thickness inside the outline (px)

SKINS_DIR = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "Skins")

# One entry per theme that wants its own block look. "colors" must keep the 7 hue
# identities (STYLE.md); "outline" is the outline value factor (0.32 = classic).
THEME_PRESETS = {
    "Classic": {
        "colors": {
            "I": (64, 196, 222),   # cyan
            "O": (240, 200, 60),   # yellow
            "T": (170, 95, 205),   # purple
            "S": (120, 195, 80),   # green
            "Z": (228, 88, 88),    # red
            "J": (95, 125, 225),   # blue
            "L": (238, 152, 66),   # orange
        },
        "outline": 0.32,
    },
    # "Desert": { ... desaturated/warm-shifted hues, softer outline ... },
}

SHAPES = {  # (col,row), row 0 = top of canvas, matches prefab spawn orientation
    "I": [(0,0),(1,0),(2,0),(3,0)],
    "J": [(0,0),(0,1),(1,1),(2,1)],
    "L": [(2,0),(0,1),(1,1),(2,1)],
    "O": [(0,0),(1,0),(0,1),(1,1)],
    "S": [(1,0),(2,0),(0,1),(1,1)],
    "T": [(1,0),(0,1),(1,1),(2,1)],
    "Z": [(0,0),(1,0),(1,1),(2,1)],
}
# Maximal rectangles (col0,row0,col1,row1 inclusive) per shape. The silhouette SDF
# is the union of these — per-cell boxes would make every internal seam read as a
# boundary (outline + alpha edge across the piece). S/Z need the extra 1x2 rect so
# the partial seam between their two rows is interior to some rectangle.
RECTS = {
    "I": [(0,0,3,0)],
    "O": [(0,0,1,1)],
    "T": [(0,1,2,1), (1,0,1,1)],
    "L": [(0,1,2,1), (2,0,2,1)],
    "J": [(0,1,2,1), (0,0,0,1)],
    "S": [(1,0,2,0), (0,1,1,1), (1,0,1,1)],
    "Z": [(0,0,1,0), (1,1,2,1), (1,0,1,1)],
}


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


def make_sdf(rects):
    boxes = []
    for c0, r0, c1, r1 in rects:
        cx = BLEED + (c0 + c1 + 1) * CELL / 2
        cy = BLEED + (r0 + r1 + 1) * CELL / 2
        hx = (c1 - c0 + 1) * CELL / 2 - R
        hy = (r1 - r0 + 1) * CELL / 2 - R
        boxes.append((cx, cy, hx, hy))
    def sdf(x, y):
        best = 1e9
        for cx, cy, hx, hy in boxes:
            dx = abs(x - cx) - hx
            dy = abs(y - cy) - hy
            ax, ay = max(dx, 0.0), max(dy, 0.0)
            d = math.hypot(ax, ay) + min(max(dx, dy), 0.0)
            if d < best:
                best = d
        return best - R
    return sdf


def seam_polylines(cells, rng):
    """Jittered polylines along internal cell seams + a couple of wandering cracks."""
    filled = set(cells)
    segs = []
    def jittered(p0, p1, amp_mid, amp_end):
        n = 4
        pts = []
        dx, dy = p1[0] - p0[0], p1[1] - p0[1]
        length = math.hypot(dx, dy)
        nx, ny = -dy / length, dx / length
        for i in range(n + 1):
            t = i / n
            amp = amp_end if i in (0, n) else amp_mid
            off = rng.uniform(-amp, amp)
            pts.append((p0[0] + dx * t + nx * off, p0[1] + dy * t + ny * off))
        return list(zip(pts, pts[1:]))
    hairlines = []
    for c, r in cells:
        x0, y0 = BLEED + c * CELL, BLEED + r * CELL
        if (c + 1, r) in filled:  # vertical seam to the right
            segs += jittered((x0 + CELL, y0 + 6), (x0 + CELL, y0 + CELL - 6), 9, 4)
        if (c, r + 1) in filled:  # horizontal seam below
            segs += jittered((x0 + 6, y0 + CELL), (x0 + CELL - 6, y0 + CELL), 9, 4)
    # wandering hairline cracks
    for _ in range(2):
        c, r = cells[rng.randrange(len(cells))]
        x = BLEED + c * CELL + rng.uniform(60, CELL - 60)
        y = BLEED + r * CELL + rng.uniform(60, CELL - 60)
        ang = rng.uniform(0, math.tau)
        for _ in range(3):
            length = rng.uniform(45, 85)
            nx_, ny_ = x + math.cos(ang) * length, y + math.sin(ang) * length
            hairlines.append(((x, y), (nx_, ny_)))
            x, y = nx_, ny_
            ang += rng.uniform(-0.9, 0.9)
    return segs, hairlines


def rasterize_lines(w, h, segs, half_width, shift_y=0.0):
    """Distance-based line intensity buffer, bbox-limited per segment."""
    buf = [0.0] * (w * h)
    for (x1, y1), (x2, y2) in segs:
        y1s, y2s = y1 + shift_y, y2 + shift_y
        minx = max(0, int(min(x1, x2) - half_width - 1))
        maxx = min(w - 1, int(max(x1, x2) + half_width + 1))
        miny = max(0, int(min(y1s, y2s) - half_width - 1))
        maxy = min(h - 1, int(max(y1s, y2s) + half_width + 1))
        vx, vy = x2 - x1, y2s - y1s
        vv = vx * vx + vy * vy or 1e-6
        for py in range(miny, maxy + 1):
            row = py * w
            for px_ in range(minx, maxx + 1):
                t = ((px_ - x1) * vx + (py - y1s) * vy) / vv
                t = 0.0 if t < 0 else (1.0 if t > 1 else t)
                dist = math.hypot(px_ - (x1 + vx * t), py - (y1s + vy * t))
                v = half_width + 0.8 - dist
                if v > 0:
                    v = min(v, 1.0)
                    if v > buf[row + px_]:
                        buf[row + px_] = v
    return buf


def render(shape, preset, out_dir):
    cells = SHAPES[shape]
    rng = random.Random(shape)
    cols = max(c for c, _ in cells) + 1
    rows = max(r for _, r in cells) + 1
    w, h = cols * CELL + 2 * BLEED, rows * CELL + 2 * BLEED
    sdf = make_sdf(RECTS[shape])
    base = preset["colors"][shape]
    outline_col = tuple(v * preset["outline"] for v in base)
    cell_bright = {cr: 1.0 + (rng.random() - 0.5) * 0.09 for cr in cells}

    seams, hairlines = seam_polylines(cells, rng)
    seam_buf = rasterize_lines(w, h, seams, 4.5)
    hair_buf = rasterize_lines(w, h, hairlines, 2.4)
    cracks = [max(s * 1.0, hl * 0.62) for s, hl in zip(seam_buf, hair_buf)]
    emboss = rasterize_lines(w, h, seams, 4.0, shift_y=5.0)

    px = bytearray(w * h * 4)
    for y in range(h):
        grad = 1.16 - 0.34 * (y / h)
        for x in range(w):
            d = sdf(x, y)
            if d > 0.5:
                continue
            alpha = min(1.0, 0.5 - d)
            # outline vs interior
            t_out = min(1.0, max(0.0, (d + OUTLINE) / 1.5 + 0.5))  # 1 = outline zone
            cidx = ((x - BLEED) // CELL, (y - BLEED) // CELL)
            b = cell_bright.get(cidx, 1.0)
            rr, gg, bb = (base[0] * grad * b, base[1] * grad * b, base[2] * grad * b)
            # bevel: lighten top-facing inner rim, darken bottom-facing
            if d > -OUTLINE - BEVEL:
                gy = sdf(x, y + 1.5) - sdf(x, y - 1.5)
                band = min(1.0, (d + OUTLINE + BEVEL) / 6.0)
                if gy < -0.4:
                    f = 1.0 + 0.22 * band
                elif gy > 0.4:
                    f = 1.0 - 0.16 * band
                else:
                    f = 1.0
                rr, gg, bb = rr * f, gg * f, bb * f
            # cracks (interior only)
            if d < -OUTLINE:
                idx = y * w + x
                ck = cracks[idx]
                if ck > 0:
                    f = 1.0 - 0.52 * ck
                    rr, gg, bb = rr * f, gg * f, bb * f
                eb = emboss[idx]
                if eb > 0 and ck < 0.4:
                    f = 1.0 + 0.16 * eb
                    rr, gg, bb = rr * f, gg * f, bb * f
            # subtle grain
            n = ((x * 374761393 + y * 668265263) ^ (x * y * 31)) & 1023
            f = 1.0 + (n / 1023.0 - 0.5) * 0.05
            rr, gg, bb = rr * f, gg * f, bb * f
            # blend into outline color
            if t_out > 0:
                rr = rr * (1 - t_out) + outline_col[0] * grad * t_out
                gg = gg * (1 - t_out) + outline_col[1] * grad * t_out
                bb = bb * (1 - t_out) + outline_col[2] * grad * t_out
            o = (y * w + x) * 4
            px[o] = min(255, max(0, int(rr)))
            px[o + 1] = min(255, max(0, int(gg)))
            px[o + 2] = min(255, max(0, int(bb)))
            px[o + 3] = int(alpha * 255)
    out = os.path.abspath(os.path.join(out_dir, f"piece_{shape}.png"))
    write_png(out, w, h, px)
    print(f"{out}  ({w}x{h})")


if __name__ == "__main__":
    for theme, preset in THEME_PRESETS.items():
        out_dir = os.path.join(SKINS_DIR, theme)
        os.makedirs(out_dir, exist_ok=True)
        for s in SHAPES:
            render(s, preset, out_dir)
