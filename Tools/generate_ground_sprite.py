#!/usr/bin/env python3
"""Procedurally renders each theme's plateau strip and floating island cells:

  Skins/<Theme>/plateau.png - one tile (256x96 px = 2 x 0.75 units) of the landable
  surface; the game TILES it to any floor width (never stretched) with outlined end
  caps preserved by a 12px sprite border (set by BlockSkinImportSettings).

  Skins/<Theme>/island_1..3.png - 1x1-cell floating support islands (128x128 px =
  1 world unit), same material language as the plateau (base color, edge line,
  grain). Deliberately SYMMETRIC - border ring all around, no lit "top", features
  that read at any angle - so the game can rotate them in 90-degree steps for 12
  effective looks per theme. Variants: 1 plain, 2 hairline crack, 3 pebble flecks.

The plateau is the ONLY ground visual - theme scenery (hills, dunes, mountains, props)
lives in the backdrop system (BackdropPreset), never attached to the floor, so nothing
decorative can be mistaken for a landing surface. Buildings were removed by design
(git history has the renderers). Pure stdlib, deterministic, 128 px/unit. STYLE.md.
"""
import os, random, struct, zlib

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
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr)
                + chunk(b"IDAT", zlib.compress(bytes(raw), 6)) + chunk(b"IEND", b""))


def grain(x, y):
    n = ((x * 374761393 + y * 668265263) ^ (x * y * 31)) & 1023
    return 1.0 + (n / 1023.0 - 0.5) * 0.05


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


def render_ground_cap(theme, cap, fleck=None, fleck_chance=0.0):
    """Walkable cap band (256x64 = 2 x 0.5 u, horizontally seamless) FloorTerrain lays along
    every floor-run top: a near-black baked outline at the very top (the landable line), a
    top-lit band in the cap colour, and a scalloped lower edge (period 32 px, jittered per
    scallop) that hangs over the masonry with a shadow lip - grass/moss/sand per theme.
    Optional flecks (petals, grains) scatter inside the band."""
    W, H, OUTLINE = 256, 64, 6
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


if __name__ == "__main__":
    # Classic: chunky beveled stone blocks; masonry columns in the same stone family.
    STONE = (148, 142, 132)
    render_plateau("Classic", STONE, line=tuple(v * 0.30 for v in STONE),
                   blocks=2, bevel=0.12, tone_steps=(1.0, 0.93))
    render_islands("Classic", STONE, line=tuple(v * 0.30 for v in STONE))
    render_ground_fill("Classic", (124, 116, 104))
    render_ground_cap("Classic", (172, 164, 148))
    remove_legacy("Classic")

    # Sakura Ridge: indigo stone with a sakura-coral cap. Dark enough to separate
    # from the pale sky, warm enough to belong with the temple/flower foreground.
    render_plateau("Japan", (74, 75, 116), line=(34, 34, 62),
                   blocks=1, top=(232, 145, 138), top_h=24)
    render_islands("Japan", (74, 75, 116), line=(34, 34, 62))
    render_ground_fill_ashlar("Japan", (88, 90, 128))
    render_ground_cap("Japan", (150, 168, 128), fleck=(232, 145, 152), fleck_chance=0.010)
    remove_legacy("Japan")

    # Desert: sun-baked terracotta capped with wind-blown sand - the floor belongs to
    # the dunes the same way each chapter's cap belongs to its scenery.
    render_plateau("Desert", (206, 118, 82),
                   blocks=1, top=(243, 190, 132))
    render_islands("Desert", (206, 118, 82))
    render_ground_fill_strata("Desert", (182, 118, 78))
    render_ground_cap("Desert", (238, 192, 124), fleck=(210, 158, 96), fleck_chance=0.012)
    remove_legacy("Desert")

    # Jungle: damp dark stone capped with moss, matching the imported jungle layers
    # without making the floor read like scenery.
    render_plateau("Jungle", (74, 103, 76), line=(35, 55, 43),
                   blocks=1, top=(83, 151, 79), top_h=30)
    render_islands("Jungle", (74, 103, 76), line=(35, 55, 43))
    render_ground_fill_cobble("Jungle", (86, 106, 84), joint=(38, 56, 40))
    render_ground_cap("Jungle", (96, 158, 74), fleck=(140, 196, 96), fleck_chance=0.012)
    remove_legacy("Jungle")

    # Frozen Peaks: cold steel-blue stone under a deep snow cap - dark enough to hold
    # its edge against the pale winter backdrop, capped in the same near-white as the
    # imported snowfields so the floor belongs to the mountain.
    render_plateau("Winter", (78, 92, 120), line=(36, 44, 62),
                   blocks=1, top=(238, 243, 252), top_h=26)
    render_islands("Winter", (78, 92, 120), line=(36, 44, 62))
    render_ground_fill_strata("Winter", (96, 110, 142), tone_var=0.05, line_factor=0.62,
                              crack_chance=0.35, fleck=(198, 214, 240), fleck_chance=0.004,
                              bands=(36, 20, 28, 44))
    render_ground_cap("Winter", (236, 242, 252), fleck=(190, 205, 235), fleck_chance=0.010)
    remove_legacy("Winter")

    # Fangkuai District: dusk aubergine stone with a lantern-rose cap - the floor picks
    # up the pack's glowing-window pink without competing with the pieces.
    render_plateau("Fangkuai", (84, 62, 90), line=(38, 26, 42),
                   blocks=1, top=(206, 116, 138), top_h=24)
    render_islands("Fangkuai", (84, 62, 90), line=(38, 26, 42))
    render_ground_fill_panels("Fangkuai", (96, 72, 102), tone_var=0.11)
    render_ground_cap("Fangkuai", (204, 118, 140), fleck=(236, 152, 170), fleck_chance=0.010)
    remove_legacy("Fangkuai")

    # Kvartal 4: sovietwave panel concrete - grey-green slabs under a worn courtyard-grass
    # cap, dark enough to belong to the night district without vanishing into it.
    render_plateau("Kvartal", (96, 104, 98), line=(44, 48, 45),
                   blocks=2, bevel=0.10, tone_steps=(1.0, 0.94), top=(88, 108, 72), top_h=22)
    render_islands("Kvartal", (96, 104, 98), line=(44, 48, 45))
    render_ground_fill_panels("Kvartal", (100, 106, 100), stain_strength=0.18)
    render_ground_cap("Kvartal", (92, 114, 76), fleck=(130, 148, 108), fleck_chance=0.008)
    remove_legacy("Kvartal")
