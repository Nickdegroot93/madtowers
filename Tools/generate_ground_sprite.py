#!/usr/bin/env python3
"""Generate chapter ground fills/caps and 1x1 support-island sprites.

Ground: seamless 128x128 fill and horizontally seamless 256x64 cap, 128 px/unit.
Islands: three 128x128 cells. Legacy materials are rotation-safe; carved materials
use upright visual children. Shapes and collision belong to FloorTerrain and
StaticSupportIslandManager, independently of these images. STYLE.md / FLOORS.md.

Run all chapters, or --theme Jungle for a single chapter. Carved renderers use
numpy + Pillow (like the piece generator); all saved textures keep their old sizes.
"""
import os, random, struct, zlib
import functools, tempfile, uuid

SELECTED_THEME = None

def _for_theme(render):
    @functools.wraps(render)
    def selected(theme, *args, **kwargs):
        if SELECTED_THEME is None or SELECTED_THEME == theme:
            return render(theme, *args, **kwargs)
    return selected


PLATEAU_W, PLATEAU_H = 256, 96   # one tile: 2.0 x 0.75 world units
ISLAND_S = 128                   # one island cell: 1.0 x 1.0 world units

SKINS_DIR = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "Skins")


def write_png(path, w, h, buf):
    def chunk(tag, data):
        c = tag + data
        return struct.pack(">I", len(data)) + c + struct.pack(">I", zlib.crc32(c) & 0xffffffff)
    raw = bytearray()
    stride = w * 4
    for y in range(h):
        raw.append(0)
        raw += buf[y * stride:(y + 1) * stride]
    ihdr = struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)
    # Unity can refresh mid-write. Register a new GUID first, then atomically swap
    # the complete PNG; existing metas (and their automatic importer settings) stay intact.
    meta = path + ".meta"
    if not os.path.exists(meta):
        fd, temporary = tempfile.mkstemp(dir=os.path.dirname(path), suffix=".tmp")
        with os.fdopen(fd, "w") as f:
            f.write(f"fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\n")
        os.replace(temporary, meta)
    fd, temporary = tempfile.mkstemp(dir=os.path.dirname(path), suffix=".tmp")
    with os.fdopen(fd, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr)
                + chunk(b"IDAT", zlib.compress(bytes(raw), 6)) + chunk(b"IEND", b""))
    os.replace(temporary, path)


def grain(x, y):
    n = ((x * 374761393 + y * 668265263) ^ (x * y * 31)) & 1023
    return 1.0 + (n / 1023.0 - 0.5) * 0.05


@_for_theme
def render_plateau(theme, base, line=None, blocks=2, bevel=0.12, tone_steps=(1.0, 0.94),
                   top=None, top_h=26):
    """One strip segment with real material reads:
    - `blocks` segments per tile, each with its own brightness step (tone_steps cycles)
      and an inner bevel: lit left/top edge, shaded right/bottom edge (blocks=1 = one
      seamless surface, no joints - soft/organic themes)
    - `top` paints a cap band (grass, snow, moss...) in its own color instead of the
      plain sunlit band - ties the floor into the theme's scenery
    - dark underside band, outlined END CAPS (kept at the strip's ends by the 12px
      sprite border) so the landable boundary is unmistakable
    line defaults to base at 50% value."""
    w, h = PLATEAU_W, PLATEAU_H
    if line is None:
        line = tuple(c * 0.5 for c in base)
    block_w = (w - 24) / blocks  # caps excluded
    buf = bytearray(w * h * 4)
    for y in range(h):
        for x in range(w):
            rr, gg, bb = base
            if x < 12 or x >= w - 12:  # end caps (preserved by the sprite border)
                rr, gg, bb = line
            else:
                bx = (x - 12) % block_w
                bi = int((x - 12) / block_w)
                tone = tone_steps[bi % len(tone_steps)]
                rr, gg, bb = rr * tone, gg * tone, bb * tone
                if top is not None and y < top_h:  # cap band (grass etc.)
                    lip = 1.18 if y < 7 else 1.0
                    rr, gg, bb = top[0] * lip, top[1] * lip, top[2] * lip
                elif top is None and y < 10:     # sunlit top
                    rr, gg, bb = rr * 1.18, gg * 1.18, bb * 1.18
                elif y >= h - 12:                # shaded underside
                    rr, gg, bb = rr * 0.62, gg * 0.62, bb * 0.62
                elif blocks > 1 and bx < 6:      # joint between blocks
                    rr, gg, bb = line
                else:
                    # inner bevel per block: lit left edge, shaded right edge
                    if blocks > 1 and bx < 16:
                        lit = 1.0 + bevel
                        rr, gg, bb = rr * lit, gg * lit, bb * lit
                    elif blocks > 1 and bx > block_w - 12:
                        shade = 1.0 - bevel
                        rr, gg, bb = rr * shade, gg * shade, bb * shade
            f = grain(x, y)
            o = (y * w + x) * 4
            buf[o] = min(255, max(0, int(rr * f)))
            buf[o + 1] = min(255, max(0, int(gg * f)))
            buf[o + 2] = min(255, max(0, int(bb * f)))
            buf[o + 3] = 255

    out_dir = os.path.join(SKINS_DIR, theme)
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.abspath(os.path.join(out_dir, "plateau.png"))
    write_png(out, w, h, buf)
    print(f"{out}  ({w}x{h})")


@_for_theme
def render_islands(theme, base, line=None, variants=3):
    """Floating 1x1 support-island cells, `variants` per theme. Same material reads
    as the plateau (base + line + grain) but fully rotation-safe: a uniform border
    ring, a soft symmetric inner shade toward the rim, and only non-directional
    features (crack / pebbles). Deterministic per theme+variant."""
    s = ISLAND_S
    if line is None:
        line = tuple(c * 0.5 for c in base)

    for variant in range(1, variants + 1):
        rng = random.Random(f"{theme}-island-{variant}")

        # feature masks, painted first so the border always wins
        crack = set()
        if variant == 2:  # hairline crack: a random walk straight through the interior
            x, y = s // 2 + rng.randint(-20, 20), 16
            dx = rng.choice((-1, 1))
            while y < s - 16:
                for w in range(2):
                    if 12 <= x + w < s - 12:
                        crack.add((x + w, y))
                y += 1
                x += rng.choice((dx, 0, 0, -dx))
                x = max(14, min(s - 15, x))
        pebbles = []
        if variant == 3:  # a few embedded flecks, round = readable at any rotation
            for _ in range(5):
                pebbles.append((rng.randint(24, s - 24), rng.randint(24, s - 24),
                                rng.randint(3, 6), rng.choice((0.82, 1.14))))

        buf = bytearray(s * s * 4)
        for y in range(s):
            for x in range(s):
                d = min(x, y, s - 1 - x, s - 1 - y)  # distance to nearest edge
                if d < 7:                            # border ring (the plateau's line)
                    rr, gg, bb = line
                else:
                    # symmetric depth: slightly shaded at the rim, full base at center
                    tone = 0.90 + 0.10 * min(1.0, (d - 7) / 18.0)
                    rr, gg, bb = base[0] * tone, base[1] * tone, base[2] * tone
                    if (x, y) in crack:
                        rr, gg, bb = (rr + line[0]) * 0.5, (gg + line[1]) * 0.5, (bb + line[2]) * 0.5
                    else:
                        for px, py, pr, ptone in pebbles:
                            if (x - px) ** 2 + (y - py) ** 2 <= pr * pr:
                                rr, gg, bb = rr * ptone, gg * ptone, bb * ptone
                                break
                f = grain(x, y)
                o = (y * s + x) * 4
                buf[o] = min(255, max(0, int(rr * f)))
                buf[o + 1] = min(255, max(0, int(gg * f)))
                buf[o + 2] = min(255, max(0, int(bb * f)))
                buf[o + 3] = 255

        out_dir = os.path.join(SKINS_DIR, theme)
        os.makedirs(out_dir, exist_ok=True)
        out = os.path.abspath(os.path.join(out_dir, f"island_{variant}.png"))
        write_png(out, s, s, buf)
        print(f"{out}  ({s}x{s})")


@_for_theme
def remove_legacy(theme):
    out_dir = os.path.join(SKINS_DIR, theme)
    for name in ("ground.png", "ground_4.png", "ground_hill.png", "building.png"):
        for path in (os.path.join(out_dir, name), os.path.join(out_dir, name + ".meta")):
            if os.path.exists(path):
                os.remove(path)
                print(f"removed {path}")


def _hash01(*vals):
    h = 2166136261
    for v in vals:
        h = ((h ^ int(v)) * 16777619) & 0xffffffff
    return (h % 10000) / 10000.0


@_for_theme
def render_ground_fill(theme, base, mortar_factor=0.32, tone_var=0.10):
    """Seamless 1x1-unit masonry tile (128x128) the grounded floor COLUMNS are built from
    (FloorTerrain tiles it from each run's top down into the fog). Running-bond bricks,
    64x32 px (0.5 x 0.25 u), classic 2:1 - the same carved language as the piece sprites:
    dark mortar joints, per-brick tone steps, a top-lit bevel per brick, fine grain.
    Both axes are periodic so any column height/width tiles cleanly."""
    S, COURSE, BRICK, MORTAR = 128, 32, 64, 4
    px = bytearray(S * S * 4)
    mortar_col = tuple(c * mortar_factor for c in base)
    sd = _seed(theme)
    for y in range(S):
        course = y // COURSE
        yy = y % COURSE
        offset = (course % 2) * (BRICK // 2)
        for x in range(S):
            xs = (x + offset) % S
            brick_col_idx = xs // BRICK
            xx = xs % BRICK
            in_mortar = yy < MORTAR or xx < MORTAR
            if in_mortar:
                r, g, b = mortar_col
            else:
                tone = 1.0 + (_hash01(sd, course, brick_col_idx) - 0.5) * 2 * tone_var
                f = tone
                # Top-lit bevel inside the brick face (light from straight above, STYLE.md).
                if yy < MORTAR + 5:
                    f *= 1.16
                elif yy >= COURSE - 4:
                    f *= 0.86
                if xx < MORTAR + 3:
                    f *= 1.05
                elif xx >= BRICK - 3:
                    f *= 0.94
                # Sparse pits for wear.
                if _hash01(sd, x * 7, y * 13) > 0.985:
                    f *= 0.72
                f *= grain(x, y)
                r, g, b = (base[0] * f, base[1] * f, base[2] * f)
            _put(px, S, x, y, r, g, b)
    _write_fill(theme, px)


def _seed(theme):
    return zlib.crc32(theme.encode()) & 0xffff


def _write_fill(theme, px, S=128):
    out_dir = os.path.join(SKINS_DIR, theme)
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.abspath(os.path.join(out_dir, "ground_fill.png"))
    write_png(out, S, S, px)
    print(f"{out}  ({S}x{S})")


def _put(px, S, x, y, r, g, b):
    o = (y * S + x) * 4
    px[o] = min(255, max(0, int(r)))
    px[o + 1] = min(255, max(0, int(g)))
    px[o + 2] = min(255, max(0, int(b)))
    px[o + 3] = 255


@_for_theme
def render_ground_fill_panels(theme, base, seam_factor=0.35, tone_var=0.05, stain_strength=0.0):
    """Large slab paving: 0.5x0.5 u panels (64 px) with thin seams — prefab concrete
    (stain_strength > 0 adds weep streaks bleeding down from the seams: sovietwave
    panelka) or, stains off, big courtyard flagstones. Seamless both axes."""
    S, PANEL, SEAM = 128, 64, 3
    px = bytearray(S * S * 4)
    seam_col = tuple(c * seam_factor for c in base)
    sd = _seed(theme)
    for y in range(S):
        py_i, yy = y // PANEL, y % PANEL
        for x in range(S):
            px_i, xx = x // PANEL, x % PANEL
            if yy < SEAM or xx < SEAM:
                r, g, b = seam_col
            else:
                f = 1.0 + (_hash01(sd, py_i, px_i) - 0.5) * 2 * tone_var
                # panel bevel: lit top edge, shaded bottom/right (light from above)
                if yy < SEAM + 3:
                    f *= 1.10
                elif yy >= PANEL - 3:
                    f *= 0.90
                if xx >= PANEL - 3:
                    f *= 0.95
                # weep stains: darker vertical streaks below the top seam, per panel
                if stain_strength > 0:
                    for k in range(2):
                        sx = int(6 + _hash01(sd, py_i, px_i, k) * (PANEL - 12))
                        ln = int(PANEL * (0.35 + 0.5 * _hash01(sd, py_i, px_i, k + 7)))
                        if abs(xx - sx) <= 1 and yy < ln:
                            f *= 1.0 - stain_strength * (1.0 - yy / max(1, ln))
                if _hash01(sd, x * 7, y * 13) > 0.988:
                    f *= 0.74
                f *= grain(x, y)
                r, g, b = (base[0] * f, base[1] * f, base[2] * f)
            _put(px, S, x, y, r, g, b)
    _write_fill(theme, px)


@_for_theme
def render_ground_fill_strata(theme, base, tone_var=0.08, line_factor=0.55,
                              crack_chance=0.5, fleck=None, fleck_chance=0.0,
                              bands=(24, 16, 32, 20, 36)):
    """Sedimentary strata: horizontal bands of varying thickness, each with its own tone,
    a darker parting line along every band top, occasional short vertical cracks —
    desert sandstone, or (cool palette + pale flecks) packed glacier ice. Seamless."""
    S = 128
    BANDS = bands
    assert sum(BANDS) == S, "strata bands must sum to 128 for vertical periodicity"
    px = bytearray(S * S * 4)
    sd = _seed(theme)
    tops = []
    t = 0
    for h in BANDS:
        tops.append(t)
        t += h
    for y in range(S):
        band = 0
        for i, top in enumerate(tops):
            if y >= top:
                band = i
        yy = y - tops[band]
        h = BANDS[band]
        for x in range(S):
            f = 1.0 + (_hash01(sd, band) - 0.5) * 2 * tone_var
            if yy < 2:                      # parting line between layers
                f *= line_factor
            elif yy < 5:
                f *= 1.08                   # lit top of the layer
            elif yy >= h - 3:
                f *= 0.90
            # a few short vertical cracks per band, deterministic positions
            for k in range(3):
                if _hash01(sd, band, k) < crack_chance:
                    cx = int(_hash01(sd, band, k + 11) * S)
                    if (x - cx) % S <= 1 and 4 <= yy:
                        f *= 0.72
            if fleck is not None and _hash01(sd, x * 3, y * 5) > 1.0 - fleck_chance:
                r, g, b = fleck
                _put(px, S, x, y, r * grain(x, y), g * grain(x, y), b * grain(x, y))
                continue
            f *= grain(x, y)
            _put(px, S, x, y, base[0] * f, base[1] * f, base[2] * f)
    _write_fill(theme, px)


@_for_theme
def render_ground_fill_ashlar(theme, base, joint_factor=0.3, tone_var=0.07):
    """Castle-wall ashlar: 0.25 u tall courses of LARGE blocks (whole- or half-tile wide,
    per-course pattern and offset), thick dark joints, chiselled top-lit faces — Japanese
    ishigaki foundation walls. Seamless both axes."""
    S, COURSE, JOINT = 128, 64, 5
    px = bytearray(S * S * 4)
    joint_col = tuple(c * joint_factor for c in base)
    sd = _seed(theme)
    for y in range(S):
        course, yy = y // COURSE, y % COURSE
        # per-course: block width 64 or 128, plus an offset that preserves the 128 period
        wide = _hash01(sd, course, 1) < 0.55
        block_w = 128 if wide else 64
        offset = int(_hash01(sd, course, 2) * 4) * 32
        for x in range(S):
            xs = (x + offset) % S
            block_i, xx = xs // block_w, xs % block_w
            if yy < JOINT or xx < JOINT:
                r, g, b = joint_col
            else:
                f = 1.0 + (_hash01(sd, course, block_i, 3) - 0.5) * 2 * tone_var
                if yy < JOINT + 6:
                    f *= 1.14
                elif yy >= COURSE - 4:
                    f *= 0.88
                if xx < JOINT + 3:
                    f *= 1.04
                elif xx >= block_w - 3:
                    f *= 0.94
                # chisel marks: faint striations; modulus must divide 128 so the
                # lattice stays phase-aligned across tile seams
                if (x * 3 + y * 5) % 8 == 0:
                    f *= 0.968
                f *= grain(x, y)
                r, g, b = (base[0] * f, base[1] * f, base[2] * f)
            _put(px, S, x, y, r, g, b)
    _write_fill(theme, px)


@_for_theme
def render_ground_fill_cobble(theme, base, joint_factor=0.35, tone_var=0.12, joint=None):
    """Irregular rounded cobbles: a jittered 0.25 u grid of stones, nearest-stone lookup
    with toroidal wrapping (seamless both axes), mossy joints between them — jungle ruin
    paving. Per-stone tone, edge-shaded rims."""
    S, CELL = 128, 32
    N = S // CELL
    px = bytearray(S * S * 4)
    sd = _seed(theme)
    joint_col = joint if joint is not None else tuple(c * joint_factor for c in base)
    centers = {}
    for cy in range(N):
        for cx in range(N):
            jx = (_hash01(sd, cx, cy, 1) - 0.5) * 8
            jy = (_hash01(sd, cx, cy, 2) - 0.5) * 8
            centers[(cx, cy)] = (cx * CELL + CELL / 2 + jx, cy * CELL + CELL / 2 + jy,
                                 16 + _hash01(sd, cx, cy, 3) * 5)
    for y in range(S):
        for x in range(S):
            best, best_d = None, 1e9
            for oy in (-1, 0, 1):
                for ox in (-1, 0, 1):
                    cx = (x // CELL + ox) % N
                    cy = (y // CELL + oy) % N
                    sx, sy, r0 = centers[(cx, cy)]
                    # wrapped distance (torus) keeps the tile seamless
                    dx = (x - sx + S * 1.5) % S - S * 0.5
                    dy = (y - sy + S * 1.5) % S - S * 0.5
                    d = (dx * dx + dy * dy) ** 0.5 - r0
                    if d < best_d:
                        best_d, best = d, (cx, cy, r0)
            if best_d < -1.5:
                f = 1.0 + (_hash01(sd, best[0], best[1], 4) - 0.5) * 2 * tone_var
                f *= 1.0 + 0.12 * min(1.0, -best_d / 10.0)     # domed center
                f *= grain(x, y)
                r, g, b = (base[0] * f, base[1] * f, base[2] * f)
            elif best_d < 0:
                f = 0.72 * grain(x, y)                          # shaded rim
                r, g, b = (base[0] * f, base[1] * f, base[2] * f)
            else:
                f = grain(x, y)
                r, g, b = (joint_col[0] * f, joint_col[1] * f, joint_col[2] * f)
            _put(px, S, x, y, r, g, b)
    _write_fill(theme, px)


@_for_theme
def render_ground_fill_wetpave(theme, base, glows=((80, 220, 240), (240, 100, 180)),
                               seam_factor=0.55, tone_var=0.05, glow_strength=0.3):
    """Wet night pavement: large dark 0.5 u tiles with faintly lit seams (wet edges
    catch the light) and soft vertical neon reflection streaks per tile — the color
    of the city above, strongest at the tile top and fading down. Seamless both axes."""
    S, PANEL, SEAM = 128, 64, 2
    px = bytearray(S * S * 4)
    seam_col = tuple(min(255, c / seam_factor) for c in base)  # lighter, not darker: wet edge
    sd = _seed(theme)
    for y in range(S):
        py_i, yy = y // PANEL, y % PANEL
        for x in range(S):
            px_i, xx = x // PANEL, x % PANEL
            if yy < SEAM or xx < SEAM:
                f = grain(x, y)
                r, g, b = (seam_col[0] * f, seam_col[1] * f, seam_col[2] * f)
            else:
                f = 1.0 + (_hash01(sd, py_i, px_i) - 0.5) * 2 * tone_var
                f *= grain(x, y)
                r, g, b = (base[0] * f, base[1] * f, base[2] * f)
                # neon reflections: 2 soft streaks per tile, random glow color, fading down
                for k in range(2):
                    cx = int(6 + _hash01(sd, py_i, px_i, k) * (PANEL - 12))
                    half_w = 2 + int(_hash01(sd, py_i, px_i, k + 5) * 3)
                    if abs(xx - cx) <= half_w:
                        glow = glows[int(_hash01(sd, py_i, px_i, k + 9) * len(glows)) % len(glows)]
                        w = glow_strength * (1.0 - yy / PANEL) * (1.0 - abs(xx - cx) / (half_w + 1))
                        r = r * (1 - w) + glow[0] * w
                        g = g * (1 - w) + glow[1] * w
                        b = b * (1 - w) + glow[2] * w
            _put(px, S, x, y, r, g, b)
    _write_fill(theme, px)


@_for_theme
def render_ground_fill_basalt(theme, base, seam_factor=0.30, tone_var=0.08,
                              lava=(255, 150, 54), lava_chance=0.30, lava_strength=0.8):
    """Volcanic basalt blocks: 0.5 u dark panels laid running-bond with near-black
    joints; a share of the horizontal joints glow molten orange (magma light bleeding
    up through the cracks, hottest mid-joint and fading toward the panel ends), plus
    sparse ember specks in the faces. Seamless both axes."""
    S, PANEL, SEAM = 128, 64, 4
    px = bytearray(S * S * 4)
    seam_col = tuple(c * seam_factor for c in base)
    sd = _seed(theme)
    # Pick the glowing horizontal joints up front. The tile is only 2x2 panels, so a
    # plain per-joint chance can select zero; always include the lowest-hash joint.
    n = S // PANEL
    joints = {(p, q): _hash01(sd, p, q, 21) for p in range(n) for q in range(n)}
    glowing = {k for k, v in joints.items() if v < lava_chance}
    if not glowing:
        glowing = {min(joints, key=joints.get)}
    for y in range(S):
        py_i, yy = y // PANEL, y % PANEL
        offset = (py_i % 2) * (PANEL // 2)
        for x in range(S):
            xs = (x + offset) % S
            px_i, xx = xs // PANEL, xs % PANEL
            if yy < SEAM or xx < SEAM:
                r, g, b = seam_col
                # molten joints: horizontal only (lava pools along the bedding cracks)
                if yy < SEAM and xx >= SEAM and (py_i, px_i) in glowing:
                    t = 1.0 - abs(yy - (SEAM - 1) / 2.0) / (SEAM / 2.0)
                    w = lava_strength * max(0.0, t)
                    w *= min(1.0, min(xx - SEAM, PANEL - 1 - xx) / 10.0)  # fade at ends
                    if w > 0:
                        r = r * (1 - w) + lava[0] * w
                        g = g * (1 - w) + lava[1] * w
                        b = b * (1 - w) + lava[2] * w
                f = grain(x, y)
                r, g, b = r * f, g * f, b * f
            else:
                f = 1.0 + (_hash01(sd, py_i, px_i) - 0.5) * 2 * tone_var
                # matte basalt bevel: subtle top light, shaded base (light from above)
                if yy < SEAM + 3:
                    f *= 1.06
                elif yy >= PANEL - 3:
                    f *= 0.92
                if xx >= PANEL - 3:
                    f *= 0.95
                f *= grain(x, y)
                r, g, b = base[0] * f, base[1] * f, base[2] * f
                # sparse ember specks glowing in the rock face
                if _hash01(sd, x * 7, y * 13) > 0.996:
                    r, g, b = lava[0] * 0.9, lava[1] * 0.9, lava[2] * 0.9
            _put(px, S, x, y, r, g, b)
    _write_fill(theme, px)


@_for_theme
def render_ground_cap(theme, cap, fleck=None, fleck_chance=0.0):
    """Walkable cap band (256x64 = 2 x 0.5 u, horizontally seamless) FloorTerrain lays along
    every floor-run top: a near-black baked outline at the very top (the landable line), a
    top-lit band in the cap colour, and a scalloped lower edge (period 32 px, jittered per
    scallop) that hangs over the masonry with a shadow lip - grass/moss/sand per theme.
    Optional flecks (petals, grains) scatter inside the band."""
    # OUTLINE = 8 px = 0.0625 u: the one contour weight shared with FloorTerrain's runtime
    # side strips (OutlineWidth = 8/128) and close to the blocks' 17 px / 256 - unified
    # 2026-09-01, the old 6 px line met 0.09 u strips at visibly different thicknesses.
    W, H, OUTLINE = 256, 64, 8
    px = bytearray(W * H * 4)
    outline_col = tuple(c * 0.22 for c in cap)
    sd = _seed(theme)
    for x in range(W):
        scallop = x // 32
        jitter = (_hash01(sd, scallop) - 0.5) * 10
        import math as _m
        edge = 40 + 8 * _m.sin(x * _m.tau / 32.0) + jitter
        for y in range(H):
            o = (y * W + x) * 4
            if y < OUTLINE:
                r, g, b, a = (*outline_col, 255)
            elif y < edge:
                f = 1.14 - 0.30 * ((y - OUTLINE) / max(1.0, edge - OUTLINE))
                f *= 1.0 + (_hash01(sd, x // 16, 3) - 0.5) * 0.10
                f *= grain(x, y)
                r, g, b = (cap[0] * f, cap[1] * f, cap[2] * f)
                # Shadow lip along the scalloped edge.
                if y > edge - 4:
                    r, g, b = (r * 0.62, g * 0.62, b * 0.62)
                if fleck is not None and _hash01(sd, x * 3, y * 5) > 1.0 - fleck_chance:
                    r, g, b = fleck
                a = 255
            else:
                r, g, b, a = (0, 0, 0, 0)
            px[o] = min(255, max(0, int(r)))
            px[o + 1] = min(255, max(0, int(g)))
            px[o + 2] = min(255, max(0, int(b)))
            px[o + 3] = a
    out_dir = os.path.join(SKINS_DIR, theme)
    out = os.path.abspath(os.path.join(out_dir, "ground_cap.png"))
    write_png(out, W, H, px)
    print(f"{out}  ({W}x{H})")


# Carved materials share a baked relief field; the output stays at 128 px/unit.
# Supersampling is authoring-only. No new shader, texture budget or runtime noise.
def _carved_fields(w, h, seed):
    import numpy as np
    from PIL import Image, ImageDraw, ImageFilter
    rng = np.random.RandomState(seed)
    def noise(nx, ny):
        points = rng.uniform(-0.5, 0.5, (ny, nx))
        xx = np.arange(w) * nx / w
        yy = np.arange(h) * ny / h
        ix, iy = xx.astype(int), yy.astype(int)
        fx, fy = xx - ix, yy - iy
        fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
        a = points[iy[:, None], ix[None, :]]
        b = points[iy[:, None], (ix[None, :] + 1) % nx]
        c = points[(iy[:, None] + 1) % ny, ix[None, :]]
        d = points[(iy[:, None] + 1) % ny, (ix[None, :] + 1) % nx]
        return (a * (1-fx) + b * fx) * (1-fy[:, None]) + (c * (1-fx) + d * fx) * fy[:, None]
    broad = noise(3, 3) * .62 + noise(7, 7) * .38
    relief = noise(5, 5) * .65 + noise(13, 13) * .25 + noise(31, 31) * .10
    # Wrap the derivative too: there is no special lighting seam at the tile boundary.
    light = np.clip((np.roll(relief, 1, axis=0) - np.roll(relief, -1, axis=0)) * h / 60, -.13, .13)
    marks = Image.new('L', (w * 3, h * 3))
    pits = Image.new('L', marks.size)
    md, pd = ImageDraw.Draw(marks), ImageDraw.Draw(pits)
    for i in range(95):
        x, y = rng.uniform(0, w), rng.uniform(0, h)
        radius = rng.uniform(.5, 1.5) * w / 128
        target = pd
        if i < 14:
            radius *= rng.uniform(3, 5)
            target = md
        points = [(x-radius, y), (x-radius*.35, y-radius*.6),
                  (x+radius*.85, y-radius*.4), (x+radius, y+radius*.4),
                  (x-radius*.2, y+radius*.8)]
        value = int(rng.randint(95, 210))
        for tx in range(3):
            for ty in range(3):
                target.polygon([(a+tx*w,b+ty*h) for a,b in points], fill=value)
    flakes = np.asarray(marks.filter(ImageFilter.GaussianBlur(w/256)).crop((w,h,2*w,2*h)), dtype=float)/255
    pits = np.asarray(pits.filter(ImageFilter.GaussianBlur(w/512)).crop((w,h,2*w,2*h)), dtype=float)/255
    return broad, light, flakes, pits, noise(11, 11), noise(47, 47)


def _carved_colour(base, fields, finish=None):
    import numpy as np
    broad, light, flakes, pits, wear, grain_field = fields
    tone = 1 + np.clip(broad*.32, -.08, .08) + light
    tone += (np.roll(flakes, 2, axis=0)-flakes)*.20 - flakes*.06
    tone += (np.roll(pits, 2, axis=0)-pits)*.24 - pits*.13
    tone += grain_field*.10
    colour = np.asarray(base, float)[None, None, :] * tone[:, :, None]
    if finish is None:
        return colour
    # Material marks are broad and periodic, baked into the face before the bevel
    # and contour. No runtime noise or coloured silhouette lighting is introduced.
    h, w = broad.shape
    y, x = np.mgrid[:h, :w] / 4
    kind = finish['kind']
    if kind == 'sandstone':
        strata = np.sin(y * 2*np.pi/16 + broad*4 + np.sin(x*2*np.pi/(w/4))*.6)
        colour *= (1 + strata*.055 - flakes*.05)[:, :, None]
    elif kind == 'concrete':
        streak = np.clip((np.roll(broad, h//16, axis=0)+wear*.22)*2, 0, .35)
        colour *= (1-streak*.24)[:, :, None]
        colour += np.asarray(base)*np.clip(flakes-pits, 0, 1)[:, :, None]*.09
    elif kind == 'ice':
        glaze = np.clip((broad+.12)*2.4+flakes*.25, 0, .65)
        colour = colour*(1-glaze[:, :, None]) + np.asarray((179,204,218))*glaze[:, :, None]
        frost = np.clip(light*5 + grain_field*.16, 0, .35)
        colour += np.asarray((142,174,196))*frost[:, :, None]*.24
    elif kind == 'wet':
        # Broken, horizontal reflected colour on the slab face, never its outline.
        sheen = np.clip((broad+.08)*2.2, 0, .5)
        bands = .5+.5*np.sin(y*2*np.pi/32+wear*3)
        a, b = (np.asarray(c) for c in finish['reflections'])
        tint = a[None,None,:]*bands[:,:,None] + b[None,None,:]*(1-bands[:,:,None])
        mix = sheen*(.35+.65*np.clip(light*5+.25, 0, 1))*.32
        colour = colour*(1-mix[:,:,None])+tint*mix[:,:,None]
    elif kind == 'basalt':
        colour *= (1-flakes*.14-pits*.12)[:,:,None]
        ash = np.clip((broad+.03)*1.3, 0, .28)
        colour = colour*(1-ash[:,:,None])+np.asarray((135,122,117))*ash[:,:,None]
    return colour


def _carved_export(theme, name, colour, alpha, size):
    import numpy as np
    from PIL import Image
    pixels = np.dstack((np.clip(colour, 0, 255), np.clip(alpha*255, 0, 255))).astype('uint8')
    im = Image.fromarray(pixels).resize(size, Image.Resampling.LANCZOS)
    path = os.path.join(SKINS_DIR, theme, name + '.png')
    os.makedirs(os.path.dirname(path), exist_ok=True)
    write_png(path, *size, im.tobytes())
    print(f'{path}  ({size[0]}x{size[1]})')


@_for_theme
def render_ground_fill_carved(theme, base, moss=(78, 103, 48), moss_strength=.38, finish=None):
    """Periodic broad temple ashlar: 1 u x .5 u faces, worn bevels, recessed joints."""
    import numpy as np
    S = 512
    y, x = np.mgrid[:S,:S] / 4
    fields = _carved_fields(S, S, _seed(theme)+8127)
    broad, light, flakes, pits, wear, fine = fields
    # Both the joints and their wear wrap in X/Y. The half-bond repeats every unit.
    yy = (y + 1.6*np.sin(x*np.pi/64) + wear*1.8) % 64
    row = ((y + 1.6*np.sin(x*np.pi/64) + wear*1.8)//64).astype(int)
    xx = (x + row*64 + 1.1*np.sin(y*np.pi/32) + wear*1.4) % 128
    dx, dy = np.minimum(xx,128-xx), np.minimum(yy,64-yy)
    edge = np.minimum(dx,dy)
    colour = _carved_colour(base, fields, finish)
    # Broad faces and real value range; straight overhead light, symmetric side shade.
    tone = 1.13 - .36*np.power(yy/64,1.15)
    top = np.clip(1-(yy-2.3)/13,0,1)**1.15
    bottom = np.clip(1-(64-yy-2.3)/10,0,1)
    sides = np.clip(1-(dx-2.3)/10,0,1)
    colour *= (tone * (1-.26*bottom-.12*sides))[:,:,None]
    rim = (np.asarray(base)*.60+255*.40)
    mix = top*.72*(.83+wear*.35)
    colour = colour*(1-mix[:,:,None]) + rim*mix[:,:,None]
    groove = np.clip((3.2-edge)/1.2,0,1)
    colour *= (1-groove*.63)[:,:,None]
    # Two angular hairline fractures, continuous across the repeat and kept subordinate.
    fracture_x = 31 + 12*(1-np.abs((y/64)%2-1)) + 3*np.sin(y*np.pi/32) + wear*2
    dist = np.abs((x-fracture_x+64)%128-64)
    fracture = np.clip(1-dist/1.15,0,1) * (.45+.55*np.clip((edge-3)/10,0,1))
    colour *= (1-fracture*.30)[:,:,None]
    lip = np.clip(1-np.abs(dist-1.5),0,1)*.08
    colour += np.asarray(base)[None,None,:]*lip[:,:,None]
    moss_mask = np.clip((broad+.04)*3,0,1)*np.clip(1-edge/18,0,1)*moss_strength
    moss_colour = np.asarray(moss)[None,None,:]*(.82+light[:,:,None])
    colour = colour*(1-moss_mask[:,:,None])+moss_colour*moss_mask[:,:,None]
    _carved_export(theme,'ground_fill',colour,np.ones((S,S)),(128,128))


@_for_theme
def render_ground_cap_carved(theme, base, moss=(91, 124, 55), moss_strength=.85, finish=None):
    """A chipped stone ledge: continuous 8 px contour, 13 px top bevel, irregular lip."""
    import numpy as np
    W,H = 1024,256
    y,x = np.mgrid[:H,:W]/4
    fields = _carved_fields(W,H,_seed(theme)+5193)
    broad,light,flakes,pits,wear,fine = fields
    colour = _carved_colour(base,fields,finish)
    edge = 49+3*np.sin(x*np.pi/64)+2*np.sin(x*np.pi/16)+wear*5
    # Weathering stays inside the silhouette. The top remains a continuous landable line.
    top = np.clip(1-(y-8)/13,0,1)
    tone = 1.13-.36*np.clip((y-8)/40,0,1)**1.15
    lower = np.clip(1-(edge-y)/10,0,1)
    colour *= (tone*(1-lower*.30))[:,:,None]
    rim=np.asarray(base)*.6+255*.4
    mix=top*.72
    colour=colour*(1-mix[:,:,None])+rim*mix[:,:,None]
    moss_mask=np.clip((broad+.11)*3,0,1)*np.clip(1-(y-8)/24,0,1)*moss_strength
    moss_colour=np.asarray(moss)[None,None,:]*(1.13+top[:,:,None]*.20+light[:,:,None])
    colour=colour*(1-moss_mask[:,:,None])+moss_colour*moss_mask[:,:,None]
    joint=np.abs((x+wear*1.5+64)%128-64)
    colour *= (1-np.clip((2.1-joint)/1.1,0,1)*.50)[:,:,None]
    # Chipped underside and bevel pits share one stone colour, never a coloured outline.
    outline=np.asarray(base)*.22
    colour=np.where((y<8)[:,:,None],outline,colour)
    alpha=np.clip(edge-y+.5,0,1)
    _carved_export(theme,'ground_cap',colour,alpha,(256,64))


@_for_theme
def render_islands_carved(theme, base, moss=(78,103,48), variants=3, moss_strength=.55, finish=None):
    """Upright quarried cells; all wear stays inside the existing 1x1-cell canvas."""
    import numpy as np
    S=512
    y,x=np.mgrid[:S,:S]/4
    for variant in range(1,variants+1):
        fields=_carved_fields(S,S,_seed(theme)+1203+variant*97)
        broad,light,flakes,pits,wear,fine=fields
        dx=np.minimum(x,128-x);dy=np.minimum(y,128-y);edge=np.minimum(dx,dy)
        colour=_carved_colour(base,fields,finish)
        top=np.clip(1-(y-8)/13,0,1)
        bottom=np.clip(1-(128-y-8)/18,0,1)
        sides=np.clip(1-(dx-8)/13,0,1)
        colour*=((1.13-.36*(y/128)**1.15)*(1-.30*bottom-.12*sides))[:,:,None]
        mix=top*.72
        colour=colour*(1-mix[:,:,None])+(np.asarray(base)*.6+255*.4)*mix[:,:,None]
        # Quiet, large angular fracture; unlike the old hairline it has a lit cut lip.
        curve=42+y*.24+3*np.sin(y*np.pi/48)+wear*2
        dist=np.abs(x-curve)
        crack=np.clip(1-dist/1.7,0,1)*np.clip((edge-9)/7,0,1)
        colour*=(1-crack*.48)[:,:,None]
        colour+=np.asarray(base)*np.clip(1-np.abs(x-curve-2)/1.2,0,1)[:,:,None]*.15
        moss_mask=np.clip((broad+.08)*3,0,1)*np.clip(1-(y-8)/33,0,1)*moss_strength
        colour=colour*(1-moss_mask[:,:,None])+np.asarray(moss)*(1.1+light[:,:,None])*moss_mask[:,:,None]
        colour=np.where((edge<8)[:,:,None],np.asarray(base)*.22,colour)
        _carved_export(theme,'island_'+str(variant),colour,np.ones((S,S)),(128,128))


def _rock_relief(x, y, style, wear):
    """Periodic natural fault network, without courses or rectangular masonry bonds."""
    import numpy as np
    if style == 'basalt':
        sites = ((18, 22), (60, 66), (106, 100))
        aspect = .36  # long, columnar breaks, not brick-sized basalt blocks
    else:
        sites = ((20, 25), (88, 17), (39, 93), (109, 83))
        aspect = .82
    first = np.full(x.shape, np.inf)
    second = first.copy()
    # Minimum-image distances make the pattern periodic without a border special case.
    for sx, sy in sites:
        dx = (x+wear*6-sx+64) % 128-64
        dy = ((y+wear*4-sy+64) % 128-64)*aspect
        distance = np.sqrt(dx*dx+dy*dy)
        closer = distance < first
        second = np.where(closer, first, np.minimum(second, distance))
        first = np.minimum(first, distance)
    edge = np.maximum((second-first)*.55+wear*.9, 0)
    relief = np.clip((edge-.8)/12, 0, 1)
    # Positive slope down the image faces the overhead light. Wrap the derivative.
    slope = (np.roll(relief, -1, axis=0)-np.roll(relief, 1, axis=0))*10
    return edge, np.clip(slope, -.30, .36)


def _ground_surface(base, fields, style, finish=None, cap=False):
    """Material structure at the existing 128 px/unit, before silhouette dressing.

    Every field is periodic. Manufactured surfaces have restrained roughness and
    straight construction joints; natural ones have bedding or irregular faults.
    Timber uses directional grain, not stone pitting disguised by a palette swap.
    """
    import numpy as np
    broad, light, flakes, pits, wear, fine = fields
    h, w = broad.shape
    y, x = np.mgrid[:h, :w]/4
    rgb = np.asarray(base, float)
    colour = _carved_colour(base, fields, finish)
    if style in ('cladding', 'fluted', 'concrete', 'plaster'):
        roughness = {'cladding':.25, 'fluted':.30, 'concrete':.58, 'plaster':.72}[style]
        colour = rgb + (colour-rgb)*roughness
        if style in ('cladding', 'fluted'):
            # Large architectural sheets, square-set, without stagger or rock cracks.
            xx, yy = x % 128, y % 128
            dx = np.minimum(xx, 128-xx)
            dy = np.minimum(yy, 128-yy)
            if not cap:
                side = np.clip(1-(dx-2)/10, 0, 1)
                top = np.clip(1-(yy-2)/13, 0, 1)
                bottom = np.clip(1-(128-yy-2)/10, 0, 1)
                colour *= (1-side*.18-bottom*.22)[:,:,None]
                colour += rgb*top[:,:,None]*.22
                joint = np.clip((2.0-np.minimum(dx,dy))/1.1, 0, 1)
                colour *= (1-joint*.55)[:,:,None]
            if style == 'fluted' and not cap:
                # Broad vertical folded-metal ribs, visible at gameplay zoom.
                rib = (.5+.5*np.cos(x*2*np.pi/32))**3
                colour *= (1-.16*rib)[:,:,None]
            else:
                # A recessed vertical service channel and broad satin reflection.
                channel = np.clip(1-np.abs((x-25+64)%128-64)/4,0,1)
                if not cap:colour *= (1-channel*.18)[:,:,None]
            sheen = (.5+.5*np.cos(x*2*np.pi/128 + broad*.7))**3
            colour += rgb*sheen[:,:,None]*.09
            if finish and 'reflections' in finish:
                a,b = (np.asarray(c) for c in finish['reflections'])
                tint = a*sheen[:,:,None]+b*(1-sheen[:,:,None])
                mix = .10+light*.18
                colour = colour*(1-mix[:,:,None])+tint*mix[:,:,None]
        elif style == 'concrete':
            # Large precast panels and formwork staining; shallow expansion joints.
            if not cap:
                xx,yy=x%128,y%128
                edge=np.minimum(np.minimum(xx,128-xx),np.minimum(yy,128-yy))
                joint=np.clip((1.7-edge)/1.0,0,1)
                colour*=(1-joint*.42)[:,:,None]
                colour+=rgb*np.clip(1-(yy-2)/13,0,1)[:,:,None]*.13
                colour*= (1-np.clip(1-(128-yy-2)/9,0,1)*.11)[:,:,None]
            stain=np.clip((np.roll(broad,h//12,axis=0)+wear*.16)*2,0,.45)
            colour*=(1-stain*.18)[:,:,None]
        else:
            # Continuous lime-plaster: broad rubbed patches and exposed aggregate.
            patch=np.clip((broad+.03)*2.2,0,.50)
            colour*= (1-patch*.17)[:,:,None]
            colour+=rgb*flakes[:,:,None]*.12
    elif style == 'timber':
        # Face grain is vertical; a coping beam runs horizontally along the cap.
        u,v=(y,x) if cap else (x,y)
        bend=3*np.sin(v*2*np.pi/128)+2*np.sin(v*2*np.pi/64)
        grain=np.sin((u+bend)*2*np.pi/9+np.sin(v*2*np.pi/128))
        wide=np.sin((u+bend)*2*np.pi/32)
        colour=rgb*(1+broad*.15+grain*.035+wide*.075)[:,:,None]
        colour+=rgb*light[:,:,None]*.35
        if not cap:
            xx=x%64;edge=np.minimum(xx,64-xx)
            groove=np.clip((2.0-edge)/1.0,0,1)
            colour*=(1-groove*.56-np.clip(1-edge/8,0,1)*.10)[:,:,None]
        # One stretched knot in the repeat, integrated into grain rather than a decal.
        du=(u-35+64)%128-64;dv=(v-69+64)%128-64
        ring=np.sqrt((du/11)**2+(dv/25)**2)
        knot=np.exp(-ring*ring*.85)*np.sin(ring*15)*.065
        colour*= (1+knot)[:,:,None]
    elif style in ('sandrock','slate','coastal'):
        # Continuous rock bedding, with no vertical mortar joints or offset courses.
        period={'sandrock':32,'slate':16,'coastal':64}[style]
        wave=4*np.sin(x*2*np.pi/128)+1.8*np.sin(x*2*np.pi/64)+wear*2
        phase=(y+wave)%period
        distance=np.minimum(phase,period-phase)
        strength={'sandrock':.25,'slate':.30,'coastal':.20}[style]
        bedding=np.clip(1-distance/2.4,0,1)
        colour*=(1-bedding*strength)[:,:,None]
        lip=np.clip(1-np.abs(phase-4)/4,0,1)
        colour+=rgb*lip[:,:,None]*.16
        colour*= (1+.06*np.cos((y+wave)*2*np.pi/period))[:,:,None]
        if style=='coastal':
            salt=np.clip((broad+.04)*1.4,0,.28)
            colour=colour*(1-salt[:,:,None])+rgb*1.22*salt[:,:,None]
    elif style == 'glacier':
        # One wandering crevasse and a short branch. No tessellated ice tiles.
        bend=14*(1-np.abs((y/64)%2-1))+5*np.sin(y*np.pi/32)
        curve=36+bend+wear*5
        distance=np.abs((x-curve+64)%128-64)
        fissure=np.clip(1-distance/2.0,0,1)
        lip=np.clip(1-np.abs(distance-4)/3,0,1)
        branch_x=curve+(y-62)*.6
        branch=np.clip(1-np.abs((x-branch_x+64)%128-64)/1.6,0,1)
        branch*=np.clip((y-62)/10,0,1)*np.clip((110-y)/10,0,1)
        colour*=(1-fissure*.26-branch*.18)[:,:,None]
        colour+=np.asarray((156,186,212))*lip[:,:,None]*.13
    elif style in ('quarried','basalt'):
        edge,slope=_rock_relief(x,y,style,wear)
        strength=.44 if style=='quarried' else .32
        colour*= (1-np.clip(1-edge/1.6,0,1)*strength+slope*.62)[:,:,None]
    else:
        raise ValueError('Unknown ground structure: '+style)
    return colour


@_for_theme
def render_ground_fill_material(theme, base, deposit, strength, style, finish=None):
    """Structure-specific seamless fill; silhouettes and physics are runtime-owned."""
    import numpy as np
    fields=_carved_fields(512,512,_seed(theme)+8127)
    broad,light,flakes,pits,wear,fine=fields
    colour=_ground_surface(base,fields,style,finish)
    mask=np.clip((broad+.03)*2,0,1)*strength*.45
    colour=colour*(1-mask[:,:,None])+np.asarray(deposit)*mask[:,:,None]
    _carved_export(theme,'ground_fill',colour,np.ones((512,512)),(128,128))


@_for_theme
def render_ground_cap_material(theme, base, deposit, strength, style, finish=None):
    """A material-appropriate coping beam/ledge, inside the original cap band."""
    import numpy as np
    y,x=np.mgrid[:256,:1024]/4
    fields=_carved_fields(1024,256,_seed(theme)+5193)
    broad,light,flakes,pits,wear,fine=fields
    colour=_ground_surface(base,fields,style,finish,cap=True)
    manufactured=style in ('cladding','fluted','concrete','timber','plaster')
    irregular=.25 if manufactured else 1.0
    edge=49+irregular*(3*np.sin(x*np.pi/64)+2*np.sin(x*np.pi/16)+wear*5)
    top=np.clip(1-(y-8)/13,0,1)
    lower=np.clip(1-(edge-y)/10,0,1)
    colour*=((1.13-.36*np.clip((y-8)/40,0,1)**1.15)*(1-lower*.30))[:,:,None]
    rim=np.asarray(base)*.60+255*.40;mix=top*.72
    colour=colour*(1-mix[:,:,None])+rim*mix[:,:,None]
    mask=np.clip((broad+.11)*3,0,1)*np.clip(1-(y-8)/24,0,1)*strength
    colour=colour*(1-mask[:,:,None])+np.asarray(deposit)*(1.13+top[:,:,None]*.2)*mask[:,:,None]
    if style in ('cladding','fluted'):
        # Formed metal coping: one recessed horizontal fold, not a stone joint.
        colour*=(1-np.clip(1-np.abs(y-36)/3,0,1)*.20)[:,:,None]
    colour=np.where((y<8)[:,:,None],np.asarray(base)*.22,colour)
    _carved_export(theme,'ground_cap',colour,np.clip(edge-y+.5,0,1),(256,64))


@_for_theme
def render_islands_material(theme, base, deposit, strength, style, finish=None):
    """Same material on the fixed support cells; upright light, painted underside."""
    import numpy as np
    y,x=np.mgrid[:512,:512]/4
    dx=np.minimum(x,128-x);dy=np.minimum(y,128-y);edge=np.minimum(dx,dy)
    for variant in range(1,4):
        fields=_carved_fields(512,512,_seed(theme)+1203+variant*97)
        broad,light,flakes,pits,wear,fine=fields
        colour=_ground_surface(base,fields,style,finish)
        top=np.clip(1-(y-8)/13,0,1)
        bottom=np.clip(1-(128-y-8)/18,0,1)
        sides=np.clip(1-(dx-8)/13,0,1)
        colour*=((1.13-.36*(y/128)**1.15)*(1-.30*bottom-.12*sides))[:,:,None]
        mix=top*.72
        colour=colour*(1-mix[:,:,None])+(np.asarray(base)*.6+255*.4)*mix[:,:,None]
        mask=np.clip((broad+.08)*3,0,1)*np.clip(1-(y-8)/33,0,1)*strength
        colour=colour*(1-mask[:,:,None])+np.asarray(deposit)*(1.1+light[:,:,None])*mask[:,:,None]
        colour=np.where((edge<8)[:,:,None],np.asarray(base)*.22,colour)
        _carved_export(theme,'island_'+str(variant),colour,np.ones((512,512)),(128,128))


@_for_theme
def render_ground_material(theme, base, cap, deposit, strengths, structure, finish=None):
    """One chapter material for fill, ledge and islands; strengths are fill/cap/island.

    Structure selects construction/rock formation; finish supplies surface wear.
    Deposits can be moss, salt, sand or frost. Canvases and silhouette dressing
    retain the approved Jungle dimensions and line weight.
    """
    if structure != "masonry":
        render_islands_material(theme, base, deposit, strengths[2], structure, finish)
        render_ground_fill_material(theme, base, deposit, strengths[0], structure, finish)
        render_ground_cap_material(theme, cap, deposit, strengths[1], structure, finish)
        return
    render_islands_carved(theme, base, deposit, moss_strength=strengths[2], finish=finish)
    render_ground_fill_carved(theme, base, deposit, moss_strength=strengths[0], finish=finish)
    render_ground_cap_carved(theme, cap, deposit, moss_strength=strengths[1], finish=finish)


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--theme", help="Render only this chapter folder; omit to render all.")
    SELECTED_THEME = parser.parse_args().theme

    # Classic: chunky beveled stone blocks; masonry columns in the same stone family.
    STONE = (148, 142, 132)
    render_plateau("Classic", STONE, line=tuple(v * 0.30 for v in STONE),
                   blocks=2, bevel=0.12, tone_steps=(1.0, 0.93))
    render_islands("Classic", STONE, line=tuple(v * 0.30 for v in STONE))
    render_ground_fill("Classic", (124, 116, 104))
    render_ground_cap("Classic", (172, 164, 148))
    remove_legacy("Classic")

    # Legacy plateau output retained unchanged.
    render_plateau("Japan", (74, 75, 116), line=(34, 34, 62),
                   blocks=1, top=(232, 145, 138), top_h=24)
    # Irregular indigo foundation stone with mineral wear and lichen.
    render_ground_material("Japan", (112, 116, 137), (137, 144, 153),
                         (111, 128, 99), (0.12, 0.38, 0.25), structure="quarried")
    remove_legacy("Japan")

    # Legacy plateau output retained unchanged.
    render_plateau("Desert", (206, 118, 82),
                   blocks=1, top=(243, 190, 132))
    # Continuous wind-cut sandstone bedding; no rectangular masonry courses.
    render_ground_material("Desert", (180, 133, 94), (204, 166, 119),
                         (203, 172, 123), (0.1, 0.38, 0.27),
                         finish={'kind': 'sandstone'}, structure="sandrock")
    remove_legacy("Desert")

    # Jungle: damp dark stone capped with moss, matching the imported jungle layers
    # without making the floor read like scenery.
    render_plateau("Jungle", (74, 103, 76), line=(35, 55, 43),
                   blocks=1, top=(83, 151, 79), top_h=30)
    # Approved reference material: preserve these defaults when extending the renderers.
    # All fifteen chapter materials opt into ChapterSkins.GroundHasCarvedRelief.
    render_islands_carved("Jungle", (126, 138, 110))
    render_ground_fill_carved("Jungle", (126, 138, 110))
    render_ground_cap_carved("Jungle", (140, 150, 118))
    remove_legacy("Jungle")

    # Legacy plateau output retained unchanged.
    render_plateau("Winter", (78, 92, 120), line=(36, 44, 62),
                   blocks=1, top=(238, 243, 252), top_h=26)
    # Ice-glazed rock with wandering crevasses and a frost-worn ledge.
    render_ground_material("Winter", (110, 137, 163), (187, 207, 223),
                         (200, 221, 232), (0.22, 0.72, 0.54),
                         finish={'kind': 'ice'}, structure="glacier")
    remove_legacy("Winter")

    # Legacy plateau output retained unchanged.
    render_plateau("Fangkuai", (84, 62, 90), line=(38, 26, 42),
                   blocks=1, top=(206, 116, 138), top_h=24)
    # Weathered plum-stained timber, vertical grain and a horizontal coping beam.
    render_ground_material("Fangkuai", (130, 110, 126), (156, 132, 143),
                         (166, 139, 145), (0.09, 0.22, 0.16),
                         structure="timber")
    remove_legacy("Fangkuai")

    # Legacy plateau output retained unchanged.
    render_plateau("Kvartal", (96, 104, 98), line=(44, 48, 45),
                   blocks=2, bevel=0.10, tone_steps=(1.0, 0.94), top=(88, 108, 72), top_h=22)
    # Sodium-warmed precast concrete with square expansion joints and runoff stains.
    render_ground_material("Kvartal", (146, 135, 105), (157, 151, 119),
                         (102, 119, 81), (0.1, 0.45, 0.3),
                         finish={'kind': 'concrete'}, structure="concrete")
    remove_legacy("Kvartal")

    # Legacy plateau output retained unchanged.
    render_plateau("Volcano", (76, 56, 62), line=(30, 20, 26),
                   blocks=2, bevel=0.10, tone_steps=(1.0, 0.93))
    # Columnar basalt with irregular vertical faults, pitting and ash.
    render_ground_material("Volcano", (104, 86, 90), (133, 111, 104),
                         (146, 123, 107), (0.12, 0.3, 0.24),
                         finish={'kind': 'basalt'}, structure="basalt")
    remove_legacy("Volcano")

    # Legacy plateau output retained unchanged.
    render_plateau("Egypt", (172, 130, 98), line=(80, 58, 44),
                   blocks=2, bevel=0.11, tone_steps=(1.0, 0.93))
    # Monumental rose sandstone masonry, eroded sediment and mineral dust.
    render_ground_material("Egypt", (166, 132, 104), (194, 161, 127),
                         (200, 174, 130), (0.09, 0.3, 0.2),
                         finish={'kind': 'sandstone'}, structure="masonry")
    remove_legacy("Egypt")

    # Legacy plateau output retained unchanged.
    render_plateau("LostCity", (78, 104, 108), line=(36, 50, 54),
                   blocks=1, top=(104, 158, 144), top_h=24)
    # Irregular teal ruin foundations, mineral wear and oasis moss.
    render_ground_material("LostCity", (110, 139, 140), (134, 160, 153),
                         (97, 136, 117), (0.27, 0.58, 0.4), structure="quarried")
    remove_legacy("LostCity")

    # Legacy plateau output retained unchanged.
    render_plateau("Island", (90, 114, 98), line=(42, 56, 46),
                   blocks=1, top=(96, 168, 104), top_h=26)
    # Continuous worn resort plaster, rubbed patches and moss on the ledge.
    render_ground_material("Island", (126, 146, 125), (150, 165, 137),
                         (85, 130, 79), (0.29, 0.7, 0.48), structure="plaster")
    remove_legacy("Island")

    # Legacy plateau output retained unchanged.
    render_plateau("Hallow", (84, 62, 98), line=(36, 24, 46),
                   blocks=1, top=(224, 134, 72), top_h=24)
    # Layered violet slate with cleft bedding and dry ochre lichen.
    render_ground_material("Hallow", (117, 100, 130), (147, 124, 143),
                         (151, 130, 99), (0.13, 0.34, 0.25), structure="slate")
    remove_legacy("Hallow")

    # Legacy plateau output retained unchanged.
    render_plateau("Techno", (46, 60, 58), line=(20, 28, 26),
                   blocks=1, top=(96, 168, 130), top_h=24)
    # Rain-slick architectural cladding with restrained green/cyan reflections.
    render_ground_material("Techno", (91, 116, 113), (118, 139, 128),
                         (112, 140, 128), (0, 0.12, 0.08),
                         finish={'kind': 'wet', 'reflections': ((104, 192, 148), (90, 164, 184))}, structure="cladding")
    remove_legacy("Techno")

    # Legacy plateau output retained unchanged.
    render_plateau("Tide", (100, 66, 92), line=(46, 26, 44),
                   blocks=1, top=(226, 142, 110), top_h=24)
    # Continuous salt-worn coastal rock with broad sedimentary bedding.
    render_ground_material("Tide", (144, 108, 126), (179, 142, 143),
                         (183, 165, 146), (0.18, 0.4, 0.3), structure="coastal")
    remove_legacy("Tide")

    # Legacy plateau output retained unchanged.
    render_plateau("Neon", (84, 76, 112), line=(30, 26, 46), blocks=1)
    # Fluted metal cladding with folded coping and cyan/rose reflected colour.
    render_ground_material("Neon", (111, 104, 140), (139, 132, 164),
                         (139, 142, 169), (0, 0.1, 0.06),
                         finish={'kind': 'wet', 'reflections': ((101, 181, 202), (182, 111, 164))}, structure="fluted")
    remove_legacy("Neon")

    # Legacy plateau output retained unchanged.
    render_plateau("Crimson", (92, 66, 72), line=(34, 20, 24), blocks=1)
    # Satin composite cladding with recessed channels and crimson/rose reflections.
    render_ground_material("Crimson", (123, 98, 109), (150, 125, 133),
                         (153, 134, 137), (0, 0.12, 0.08),
                         finish={'kind': 'wet', 'reflections': ((201, 104, 104), (185, 155, 168))}, structure="cladding")
    remove_legacy("Crimson")
