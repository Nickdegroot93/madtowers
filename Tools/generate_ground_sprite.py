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
                tone = 1.0 + (_hash01(zlib.crc32(theme.encode()) & 0xffff, course, brick_col_idx) - 0.5) * 2 * tone_var
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
                if _hash01(zlib.crc32(theme.encode()) & 0xffff, x * 7, y * 13) > 0.985:
                    f *= 0.72
                f *= grain(x, y)
                r, g, b = (base[0] * f, base[1] * f, base[2] * f)
            o = (y * S + x) * 4
            px[o] = min(255, max(0, int(r)))
            px[o + 1] = min(255, max(0, int(g)))
            px[o + 2] = min(255, max(0, int(b)))
            px[o + 3] = 255
    out_dir = os.path.join(SKINS_DIR, theme)
    out = os.path.abspath(os.path.join(out_dir, "ground_fill.png"))
    write_png(out, S, S, px)
    print(f"{out}  ({S}x{S})")


def render_ground_cap(theme, cap, fleck=None, fleck_chance=0.0):
    """Walkable cap band (256x64 = 2 x 0.5 u, horizontally seamless) FloorTerrain lays along
    every floor-run top: a near-black baked outline at the very top (the landable line), a
    top-lit band in the cap colour, and a scalloped lower edge (period 32 px, jittered per
    scallop) that hangs over the masonry with a shadow lip - grass/moss/sand per theme.
    Optional flecks (petals, grains) scatter inside the band."""
    W, H, OUTLINE = 256, 64, 6
    px = bytearray(W * H * 4)
    outline_col = tuple(c * 0.22 for c in cap)
    for x in range(W):
        scallop = x // 32
        jitter = (_hash01(zlib.crc32(theme.encode()) & 0xffff, scallop) - 0.5) * 10
        import math as _m
        edge = 40 + 8 * _m.sin(x * _m.tau / 32.0) + jitter
        for y in range(H):
            o = (y * W + x) * 4
            if y < OUTLINE:
                r, g, b, a = (*outline_col, 255)
            elif y < edge:
                f = 1.14 - 0.30 * ((y - OUTLINE) / max(1.0, edge - OUTLINE))
                f *= 1.0 + (_hash01(zlib.crc32(theme.encode()) & 0xffff, x // 16, 3) - 0.5) * 0.10
                f *= grain(x, y)
                r, g, b = (cap[0] * f, cap[1] * f, cap[2] * f)
                # Shadow lip along the scalloped edge.
                if y > edge - 4:
                    r, g, b = (r * 0.62, g * 0.62, b * 0.62)
                if fleck is not None and _hash01(zlib.crc32(theme.encode()) & 0xffff, x * 3, y * 5) > 1.0 - fleck_chance:
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
    render_ground_fill("Japan", (88, 90, 128))
    render_ground_cap("Japan", (150, 168, 128), fleck=(232, 145, 152), fleck_chance=0.010)
    remove_legacy("Japan")

    # Desert: sun-baked terracotta capped with wind-blown sand - the floor belongs to
    # the dunes the same way each chapter's cap belongs to its scenery.
    render_plateau("Desert", (206, 118, 82),
                   blocks=1, top=(243, 190, 132))
    render_islands("Desert", (206, 118, 82))
    render_ground_fill("Desert", (182, 118, 78))
    render_ground_cap("Desert", (238, 192, 124), fleck=(210, 158, 96), fleck_chance=0.012)
    remove_legacy("Desert")

    # Jungle: damp dark stone capped with moss, matching the imported jungle layers
    # without making the floor read like scenery.
    render_plateau("Jungle", (74, 103, 76), line=(35, 55, 43),
                   blocks=1, top=(83, 151, 79), top_h=30)
    render_islands("Jungle", (74, 103, 76), line=(35, 55, 43))
    render_ground_fill("Jungle", (82, 104, 80))
    render_ground_cap("Jungle", (96, 158, 74), fleck=(140, 196, 96), fleck_chance=0.012)
    remove_legacy("Jungle")

    # Frozen Peaks: cold steel-blue stone under a deep snow cap - dark enough to hold
    # its edge against the pale winter backdrop, capped in the same near-white as the
    # imported snowfields so the floor belongs to the mountain.
    render_plateau("Winter", (78, 92, 120), line=(36, 44, 62),
                   blocks=1, top=(238, 243, 252), top_h=26)
    render_islands("Winter", (78, 92, 120), line=(36, 44, 62))
    render_ground_fill("Winter", (86, 98, 128))
    render_ground_cap("Winter", (236, 242, 252), fleck=(190, 205, 235), fleck_chance=0.010)
    remove_legacy("Winter")

    # Fangkuai District: dusk aubergine stone with a lantern-rose cap - the floor picks
    # up the pack's glowing-window pink without competing with the pieces.
    render_plateau("Fangkuai", (84, 62, 90), line=(38, 26, 42),
                   blocks=1, top=(206, 116, 138), top_h=24)
    render_islands("Fangkuai", (84, 62, 90), line=(38, 26, 42))
    render_ground_fill("Fangkuai", (92, 68, 98))
    render_ground_cap("Fangkuai", (204, 118, 140), fleck=(236, 152, 170), fleck_chance=0.010)
    remove_legacy("Fangkuai")
