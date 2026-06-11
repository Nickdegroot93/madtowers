#!/usr/bin/env python3
"""Procedurally renders each theme's plateau strip:

  Skins/<Theme>/plateau.png - one tile (256x96 px = 2 x 0.75 units) of the landable
  surface; the game TILES it to any floor width (never stretched) with outlined end
  caps preserved by a 12px sprite border (set by BlockSkinImportSettings).

The plateau is the ONLY ground visual - theme scenery (hills, dunes, mountains, props)
lives in the backdrop system (BackdropPreset), never attached to the floor, so nothing
decorative can be mistaken for a landing surface. Buildings were removed by design
(git history has the renderers). Pure stdlib, deterministic, 128 px/unit. STYLE.md.
"""
import os, struct, zlib

PLATEAU_W, PLATEAU_H = 256, 96   # one tile: 2.0 x 0.75 world units

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


def remove_legacy(theme):
    out_dir = os.path.join(SKINS_DIR, theme)
    for name in ("ground.png", "ground_4.png", "ground_hill.png", "building.png"):
        for path in (os.path.join(out_dir, name), os.path.join(out_dir, name + ".meta")):
            if os.path.exists(path):
                os.remove(path)
                print(f"removed {path}")


if __name__ == "__main__":
    # Classic: chunky beveled stone blocks
    STONE = (148, 142, 132)
    render_plateau("Classic", STONE, line=tuple(v * 0.30 for v in STONE),
                   blocks=2, bevel=0.12, tone_steps=(1.0, 0.93))
    remove_legacy("Classic")

    # Training Wheels: grass-capped earth - the floor belongs to the hill scenery
    render_plateau("TrainingWheels", (166, 124, 88), line=(96, 72, 52),
                   blocks=1, top=(118, 180, 98))
    remove_legacy("TrainingWheels")

    # Desert: sun-baked terracotta capped with wind-blown sand - the floor belongs to
    # the dunes the same way Training Wheels' grass cap belongs to its hills
    render_plateau("Desert", (206, 118, 82),
                   blocks=1, top=(243, 190, 132))
    remove_legacy("Desert")
