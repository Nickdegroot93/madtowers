#!/usr/bin/env python3
"""Procedurally renders Tricky-Towers-style whole-piece tetromino sprites, PER THEME.

Requires numpy + Pillow. Output: piece_X.png into Assets/Resources/Skins/<Theme>/
for each entry in THEME_PRESETS. A theme without its own entry falls back to the
Classic pieces at runtime (ChapterSkins fallback chain) - adding a block look for
a theme = adding one preset dict here and rerunning.

Style per piece (the "carved stone toy" look, see STYLE.md):
  - rounded silhouette, THICK near-black outline that keeps the base hue
  - light from straight above: bright embossed bevel just inside the top edge,
    shadowed bevel along the bottom, neutral-dark sides
  - vertical gradient (lighter top, darker bottom) + mottled multi-octave stone
    body + per-cell brightness variance + fine grain
  - chunky embossed cracks along cell seams that run all the way through the
    outline (each cell reads as its own stone), plus shorter "plate" cracks
    growing inward from the silhouette edge, plus faint wandering hairlines
  - small pit specks and edge nicks for wear

Deterministic per shape (seeded) so regeneration is stable and every theme keeps
the same crack layout (only the palette changes chapter to chapter).
Style rules: STYLE.md (the 7 shapes keep their hue identities in every theme -
shift saturation/value only).

    python3 Tools/generate_piece_sprites.py [--preview <dir>]

--preview also writes one contact-sheet PNG per theme (all shapes on the theme's
approximate sky color) into <dir> for judging outside Unity.
"""
import math, os, random, sys, zlib

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

CELL, BLEED = 256, 32
R = 22            # silhouette corner radius (px) - STYLE.md geometry invariant
OUTLINE = 17      # outline thickness (px)
BEVEL = 26        # bevel band thickness inside the outline (px)

SKINS_DIR = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "Skins")

# One entry per theme that wants its own block look. "colors" must keep the 7 hue
# identities (STYLE.md); "outline" is the outline value factor (fraction of the
# base color's value the outline keeps - the outline is also mildly desaturated
# so it reads near-black while never going flat black).
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
            "Pip": (232, 100, 180),    # special brick - bright magenta
            "Domino": (200, 82, 184),  # special brick - deeper violet-magenta (paired, visibly distinct from Pip)
        },
        "outline": 0.22,
        "edgeShine": {"Pip": 0.15, "Domino": 0.15},  # faint extra top-edge highlight
        "preview_bg": (155, 170, 200),
    },
    # Sun-baked sand family: heavily desaturated, warm-cast, hue identities preserved
    # in muted form (cool I/J vs warm Z/L stay distinguishable). Softer outline to
    # match the theme's soft-shading language.
    "Desert": {
        "colors": {
            "I": (199, 190, 162),  # bleached bone (cool cast)
            "O": (231, 191, 112),  # golden sand
            "T": (188, 136, 146),  # dusty clay-rose
            "S": (166, 172, 118),  # desert sage
            "Z": (209, 112, 88),   # terracotta
            "J": (152, 150, 172),  # slate sand (cool cast)
            "L": (223, 148, 82),   # burnt orange
            "Pip": (210, 186, 152),    # special brick - light sandstone
            "Domino": (188, 164, 134), # special brick - deeper sandstone (paired, distinct from Pip)
        },
        "outline": 0.28,
        "edgeShine": {"Pip": 0.15, "Domino": 0.15},  # faint extra top-edge highlight
        "preview_bg": (205, 185, 155),
    },
    # Jungle Depths: saturated canopy light with dark organic outlines. Hue identities
    # stay intact, but every piece is pulled into leaf, orchid, clay, and river tones.
    "Jungle": {
        "colors": {
            "I": (82, 205, 188),   # river teal
            "O": (226, 198, 82),   # filtered sun
            "T": (156, 96, 194),   # orchid
            "S": (91, 177, 82),    # leaf green
            "Z": (207, 88, 82),    # red bromeliad
            "J": (78, 121, 210),   # deep blue flower
            "L": (220, 136, 62),   # clay orange
            "Pip": (214, 96, 172),     # bright jungle fruit
            "Domino": (164, 80, 154),  # deeper fruit-vine
        },
        "outline": 0.19,
        "edgeShine": {"Pip": 0.15, "Domino": 0.15},
        "preview_bg": (70, 120, 85),
    },
    # The Vault's studio brick: ONE neutral warm-stone tone for every shape. Not a playable
    # chapter skin - it's the "goes with everything" brick the collection thumbnails pose in
    # front of a dark neutral backdrop (BLOCKPREVIEWS.md), so variant overlays (vines, frost,
    # gears) read on a calm base instead of a chapter colour.
    "Vault": {
        "colors": {
            "I": (198, 189, 173),
            "O": (198, 189, 173),
            "T": (198, 189, 173),
            "S": (198, 189, 173),
            "Z": (198, 189, 173),
            "J": (198, 189, 173),
            "L": (198, 189, 173),
            "Pip": (198, 189, 173),
            "Domino": (198, 189, 173),
        },
        "outline": 0.24,
        "edgeShine": {"Pip": 0.15, "Domino": 0.15},
        "preview_bg": (34, 36, 42),
    },
    # Frozen Peaks: frost-pulled hues under a pale alpine sky. Cool cast everywhere,
    # but the warm identities (O/Z/L) survive as winter sun, rowan berry, and lantern
    # amber so the 7 shapes stay tellable against the snow.
    "Winter": {
        "colors": {
            "I": (126, 200, 216),   # glacial cyan
            "O": (224, 198, 126),   # pale winter sun
            "T": (162, 118, 192),   # frost lilac
            "S": (108, 164, 128),   # frosted pine
            "Z": (204, 96, 104),    # rowan berry
            "J": (98, 124, 198),    # deep ice blue
            "L": (216, 142, 92),    # lantern amber
            "Pip": (208, 122, 178),     # cold pink
            "Domino": (152, 102, 172),  # frozen violet
        },
        "outline": 0.24,
        "edgeShine": {"Pip": 0.15, "Domino": 0.15},
        "preview_bg": (186, 196, 222),
    },
    # Sakura Ridge: muted ukiyo-e / washi tones. These belong to the background's
    # sakura, Fuji, temple indigo, and coral highlights; readability comes from the
    # outline/shape language rather than neon opposite colors.
    "Japan": {
        "colors": {
            "I": (83, 171, 172),    # patinated teal
            "O": (222, 201, 130),   # washi gold
            "T": (158, 96, 157),    # muted plum
            "S": (120, 154, 120),   # soft moss jade
            "Z": (215, 104, 108),   # sakura vermilion
            "J": (86, 106, 160),    # temple indigo
            "L": (218, 133, 92),    # warm terracotta
            "Pip": (220, 116, 150),     # sakura pink
            "Domino": (128, 92, 150),   # wisteria purple
        },
        "outline": 0.20,
        "edgeShine": {"Pip": 0.15, "Domino": 0.15},
        "preview_bg": (205, 175, 185),
    },
}

SHAPES = {  # (col,row), row 0 = top of canvas, matches prefab spawn orientation
    "I": [(0,0),(1,0),(2,0),(3,0)],
    "J": [(0,0),(0,1),(1,1),(2,1)],
    "L": [(2,0),(0,1),(1,1),(2,1)],
    "O": [(0,0),(1,0),(0,1),(1,1)],
    "S": [(1,0),(2,0),(0,1),(1,1)],
    "T": [(1,0),(0,1),(1,1),(2,1)],
    "Z": [(0,0),(1,0),(1,1),(2,1)],
    "Pip": [(0,0)],            # 1x1 shrink brick
    "Domino": [(0,0),(0,1)],   # 1x2 shrink brick (vertical)
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
    "Pip": [(0,0,0,0)],
    "Domino": [(0,0,0,1)],
}


def sdf_grid(rects, w, h):
    """Signed distance (px) to the rounded union-of-rects silhouette, per pixel."""
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    xs += 0.5; ys += 0.5
    best = np.full((h, w), 1e9, np.float32)
    for c0, r0, c1, r1 in rects:
        cx = BLEED + (c0 + c1 + 1) * CELL / 2
        cy = BLEED + (r0 + r1 + 1) * CELL / 2
        hx = (c1 - c0 + 1) * CELL / 2 - R
        hy = (r1 - r0 + 1) * CELL / 2 - R
        dx = np.abs(xs - cx) - hx
        dy = np.abs(ys - cy) - hy
        d = np.hypot(np.maximum(dx, 0), np.maximum(dy, 0)) + np.minimum(np.maximum(dx, dy), 0)
        np.minimum(best, d, out=best)
    return best - R


def value_noise(w, h, scale_px, nprng):
    """Smooth value noise in [-0.5, 0.5], one octave at the given feature size."""
    gw, gh = max(2, round(w / scale_px)) + 1, max(2, round(h / scale_px)) + 1
    grid = nprng.rand(gh, gw).astype(np.float32)
    img = Image.fromarray((grid * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
    return np.asarray(img, np.float32) / 255.0 - 0.5


def mottle_field(w, h, nprng):
    """Multi-octave stone mottling in about [-0.5, 0.5]."""
    return (value_noise(w, h, 110, nprng) * 0.5
            + value_noise(w, h, 52, nprng) * 0.32
            + value_noise(w, h, 22, nprng) * 0.18)


def jittered(rng, p0, p1, amp_mid, amp_end, n=6):
    """Polyline points from p0 to p1 with perpendicular jitter."""
    dx, dy = p1[0] - p0[0], p1[1] - p0[1]
    length = math.hypot(dx, dy) or 1e-6
    nx, ny = -dy / length, dx / length
    pts = []
    for i in range(n + 1):
        t = i / n
        amp = amp_end if i in (0, n) else amp_mid
        off = rng.uniform(-amp, amp)
        pts.append((p0[0] + dx * t + nx * off, p0[1] + dy * t + ny * off))
    return pts


def wander(rng, x, y, ang, steps, lo, hi, turn=0.9):
    """Random-walk polyline used for plate cracks and hairlines."""
    pts = [(x, y)]
    for _ in range(steps):
        length = rng.uniform(lo, hi)
        x, y = x + math.cos(ang) * length, y + math.sin(ang) * length
        pts.append((x, y))
        ang += rng.uniform(-turn, turn)
    return pts


def silhouette_edge_point(rng, cells, side_filter=None):
    """A random point on the outer boundary of the piece + its inward direction."""
    filled = set(cells)
    options = []
    for c, r in cells:
        if (c, r - 1) not in filled: options.append((c, r, "top"))
        if (c, r + 1) not in filled: options.append((c, r, "bottom"))
        if (c - 1, r) not in filled: options.append((c, r, "left"))
        if (c + 1, r) not in filled: options.append((c, r, "right"))
    if side_filter:
        filtered = [o for o in options if o[2] in side_filter]
        options = filtered or options
    c, r, side = options[rng.randrange(len(options))]
    x0, y0 = BLEED + c * CELL, BLEED + r * CELL
    t = rng.uniform(0.2, 0.8)
    if side == "top":    return (x0 + CELL * t, y0), math.pi / 2
    if side == "bottom": return (x0 + CELL * t, y0 + CELL), -math.pi / 2
    if side == "left":   return (x0, y0 + CELL * t), 0.0
    return (x0 + CELL, y0 + CELL * t), math.pi


def build_crack_layers(shape, cells, w, h, rng):
    """Draw the crack line-work into intensity maps.

    Returns (chunky, hairline, pits) float arrays in [0,1]:
      chunky   - cell seams + plate cracks (deep carved lines, get the emboss)
      hairline - faint wandering surface cracks
      pits     - small round pit specks
    """
    seam_img = Image.new("L", (w, h), 0)
    plate_img = Image.new("L", (w, h), 0)
    hair_img = Image.new("L", (w, h), 0)
    pit_img = Image.new("L", (w, h), 0)
    seams, plates, hairs, pits = (ImageDraw.Draw(i) for i in
                                  (seam_img, plate_img, hair_img, pit_img))
    filled = set(cells)

    # Cell seams: every internal cell boundary, jittered, overshooting past the
    # silhouette edge so the crack visibly cuts through the outline (each cell
    # reads as a separate stone, like the reference art).
    OVER = OUTLINE + BLEED  # the alpha mask clips whatever pokes outside
    for c, r in cells:
        x0, y0 = BLEED + c * CELL, BLEED + r * CELL
        if (c + 1, r) in filled:
            seams.line(jittered(rng, (x0 + CELL, y0 - OVER), (x0 + CELL, y0 + CELL + OVER), 10, 5),
                       fill=255, width=9, joint="curve")
        if (c, r + 1) in filled:
            seams.line(jittered(rng, (x0 - OVER, y0 + CELL), (x0 + CELL + OVER, y0 + CELL), 10, 5),
                       fill=255, width=9, joint="curve")

    # Plate cracks: few, short, calm. Two kinds, both anchored to existing
    # structure so they read as stone fractures, not floating scratches:
    #  a) edge cracks — nick the silhouette edge and bite a short way inward
    #  b) seam branches — split off an internal seam roughly perpendicular
    for _ in range(max(1, len(cells) // 2)):
        (x, y), ang = silhouette_edge_point(rng, cells)
        pts = wander(rng, x, y, ang + rng.uniform(-0.35, 0.35),
                     rng.randrange(1, 3), 55, 90, turn=0.35)
        plates.line(pts, fill=255, width=7, joint="curve")
    seam_specs = []
    for c, r in cells:
        x0, y0 = BLEED + c * CELL, BLEED + r * CELL
        if (c + 1, r) in filled:
            seam_specs.append(((x0 + CELL, y0), True))    # vertical seam, top corner
        if (c, r + 1) in filled:
            seam_specs.append(((x0, y0 + CELL), False))   # horizontal seam, left corner
    rng.shuffle(seam_specs)
    for (sx, sy), vertical in seam_specs[:max(1, len(seam_specs) // 2)]:
        t = rng.uniform(0.25, 0.75) * CELL
        x, y = (sx, sy + t) if vertical else (sx + t, sy)
        ang = (0.0 if vertical else math.pi / 2) + (math.pi if rng.random() < 0.5 else 0.0)
        pts = wander(rng, x, y, ang + rng.uniform(-0.4, 0.4), 2, 40, 70, turn=0.4)
        plates.line(pts, fill=235, width=6, joint="curve")

    # Hairlines: a couple of faint wandering surface cracks well inside the body.
    for _ in range(2):
        c, r = cells[rng.randrange(len(cells))]
        x = BLEED + c * CELL + rng.uniform(60, CELL - 60)
        y = BLEED + r * CELL + rng.uniform(60, CELL - 60)
        hairs.line(wander(rng, x, y, rng.uniform(0, math.tau), 3, 45, 85, turn=0.6),
                   fill=255, width=3, joint="curve")

    # Pit specks: sparse tiny weathering pits.
    for _ in range(2 + len(cells)):
        c, r = cells[rng.randrange(len(cells))]
        px = BLEED + c * CELL + rng.uniform(35, CELL - 35)
        py = BLEED + r * CELL + rng.uniform(35, CELL - 35)
        rad = rng.uniform(1.8, 3.4)
        pits.ellipse((px - rad, py - rad, px + rad, py + rad), fill=255)

    blur = ImageFilter.GaussianBlur(1.3)
    chunky = np.maximum(np.asarray(seam_img.filter(blur), np.float32),
                        np.asarray(plate_img.filter(blur), np.float32)) / 255.0
    hairline = np.asarray(hair_img.filter(blur), np.float32) / 255.0
    pit = np.asarray(pit_img.filter(ImageFilter.GaussianBlur(0.8)), np.float32) / 255.0
    return chunky, hairline, pit


def shift_down(a, px):
    """Shift an intensity map down by px rows (used for emboss offsets)."""
    out = np.zeros_like(a)
    if px > 0:
        out[px:, :] = a[:-px, :]
    elif px < 0:
        out[:px, :] = a[-px:, :]
    else:
        out[:] = a
    return out


def desaturate(col, amount):
    lum = col[0] * 0.299 + col[1] * 0.587 + col[2] * 0.114
    return col + (lum - col) * amount


def render(shape, preset, out_dir):
    cells = SHAPES[shape]
    rng = random.Random(shape)          # same layout in every theme, stable reruns
    nprng = np.random.RandomState(zlib.crc32(shape.encode()) & 0x7fffffff)
    cols = max(c for c, _ in cells) + 1
    rows = max(r for _, r in cells) + 1
    w, h = cols * CELL + 2 * BLEED, rows * CELL + 2 * BLEED

    sdf = sdf_grid(RECTS[shape], w, h)
    alpha = np.clip(0.5 - sdf, 0.0, 1.0)
    inside = alpha > 0.0

    base = np.asarray(preset["colors"][shape], np.float32) / 255.0
    edge_shine = preset.get("edgeShine", {}).get(shape, 0.0)

    # --- luminance model: gradient * per-cell variance * mottle * grain -------
    ys = (np.arange(h, dtype=np.float32) + 0.5)[:, None] / h
    grad = 1.13 - 0.36 * ys ** 1.15                       # light from straight above
    cellb = np.ones((h, w), np.float32)
    for (c, r) in cells:
        b = 1.0 + (rng.random() - 0.5) * 0.09
        y0, y1 = BLEED + r * CELL, BLEED + (r + 1) * CELL
        x0, x1 = BLEED + c * CELL, BLEED + (c + 1) * CELL
        cellb[max(0, y0):y1, max(0, x0):x1] = b
    mottle = mottle_field(w, h, nprng)
    grain = (nprng.rand(h, w).astype(np.float32) - 0.5) * 0.05
    lum = grad * cellb * (1.0 + mottle * 0.17) * (1.0 + grain)
    col = base[None, None, :] * lum[..., None]

    # --- bevel: embossed rim just inside the outline --------------------------
    gy, gx = np.gradient(sdf)                             # outward-facing normal
    band = np.clip((sdf + OUTLINE + BEVEL) / BEVEL, 0.0, 1.0) ** 1.6
    band *= np.clip((-sdf - OUTLINE * 0.55) / (OUTLINE * 0.45), 0.0, 1.0)  # fade under outline
    topness = np.clip((-gy - 0.25) / 0.5, 0.0, 1.0)
    botness = np.clip((gy - 0.25) / 0.5, 0.0, 1.0)
    sideness = np.clip((np.abs(gx) - 0.25) / 0.5, 0.0, 1.0) * (1.0 - topness) * (1.0 - botness)
    col *= (1.0 - 0.09 * band)[..., None]                 # faint AO ring inside the outline
    hi_col = 1.0 - (1.0 - base) * 0.42                    # base pushed toward white, hue kept
    k_top = (0.72 + edge_shine) * band * topness
    col = col * (1.0 - k_top[..., None]) + hi_col[None, None, :] * (grad * 1.04)[..., None] * k_top[..., None]
    col *= (1.0 - 0.26 * band * botness)[..., None]       # bottom inner shadow
    col *= (1.0 - 0.12 * band * sideness)[..., None]      # sides slightly shaded

    # --- cracks (carved: dark core, lit lower lip, shadowed upper lip) --------
    chunky, hairline, pit = build_crack_layers(shape, cells, w, h, rng)
    crack = np.maximum(chunky, hairline * 0.55)
    body = sdf < -OUTLINE * 0.35                          # cracks may cut the outline
    col *= (1.0 - 0.55 * crack * body)[..., None]
    lip_lo = shift_down(chunky, 5) * np.clip((0.38 - crack) / 0.38, 0.0, 1.0)
    lip_hi = shift_down(chunky, -4) * np.clip((0.38 - crack) / 0.38, 0.0, 1.0)
    interior = sdf < -OUTLINE
    col *= (1.0 + 0.30 * lip_lo * interior)[..., None]    # light catches below the crack
    col *= (1.0 - 0.13 * lip_hi * interior)[..., None]    # shadow above it
    pit_lip = shift_down(pit, 3) * np.clip((0.3 - pit) / 0.3, 0.0, 1.0)
    col *= (1.0 - 0.42 * pit * interior)[..., None]
    col *= (1.0 + 0.20 * pit_lip * interior)[..., None]

    # --- outline: thick, near-black, hue kept ---------------------------------
    out_col = desaturate(base, 0.30) * preset["outline"]
    t_out = np.clip((sdf + OUTLINE) / 2.0 + 0.5, 0.0, 1.0)
    o_shade = (grad * (1.0 - 0.30 * crack))[..., None]    # cracks nick the outline too
    col = col * (1.0 - t_out[..., None]) + out_col[None, None, :] * o_shade * t_out[..., None]

    rgba = np.zeros((h, w, 4), np.uint8)
    rgba[..., :3] = (np.clip(col, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)
    rgba[..., 3] = (alpha * 255.0 + 0.5).astype(np.uint8)
    rgba[~inside, :3] = 0
    out = os.path.abspath(os.path.join(out_dir, f"piece_{shape}.png"))
    Image.fromarray(rgba, "RGBA").save(out)
    print(f"{out}  ({w}x{h})")
    return Image.fromarray(rgba, "RGBA")


def write_preview(theme, preset, images, preview_dir):
    """One contact sheet per theme on its approximate sky color, at ~game zoom."""
    scale = 0.5
    pad = 40
    thumbs = [im.resize((int(im.width * scale), int(im.height * scale)), Image.LANCZOS)
              for im in images]
    cw = max(t.width for t in thumbs) + pad
    per_row = 3
    rows = math.ceil(len(thumbs) / per_row)
    rh = max(t.height for t in thumbs) + pad
    sheet = Image.new("RGBA", (cw * per_row + pad, rh * rows + pad),
                      tuple(preset.get("preview_bg", (150, 150, 160))) + (255,))
    for i, t in enumerate(thumbs):
        x = pad + (i % per_row) * cw
        y = pad + (i // per_row) * rh
        sheet.alpha_composite(t, (x, y))
    out = os.path.join(preview_dir, f"preview_{theme}.png")
    sheet.convert("RGB").save(out)
    print(out)


if __name__ == "__main__":
    preview_dir = None
    if "--preview" in sys.argv:
        preview_dir = sys.argv[sys.argv.index("--preview") + 1]
        os.makedirs(preview_dir, exist_ok=True)
    for theme, preset in THEME_PRESETS.items():
        out_dir = os.path.join(SKINS_DIR, theme)
        os.makedirs(out_dir, exist_ok=True)
        images = [render(s, preset, out_dir) for s in SHAPES]
        if preview_dir:
            write_preview(theme, preset, images, preview_dir)
