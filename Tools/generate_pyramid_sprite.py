#!/usr/bin/env python3
"""piece_Pyramid.png - the Giza Dusk signature brick's fixed look.

One sprite, written to Skins/Classic only: every chapter falls back to Classic
file-by-file (ChapterSkins.LoadWithFallback), so shipping no per-chapter override
IS the theme-independent look (ART.md section 13), the Sandstone precedent.

Geometry contract (must match Block_Pyramid.prefab):
- 3-wide MONUMENT: a straight ashlar base course (three real 1x1 cells at
  x = -1, 0, 1) with the pyramid top rising from its shoulders to a sharp visual
  apex at (0, 1.92). Cell renderers average to local (0, 0), so PieceSkin lands
  there - the canvas is symmetric about that point.
- The collider triangle (base y = 0.5 spanning +-1.45, apex (0.07, 1.85) - offset
  so nothing balances, see PHYSICS.md) stays just inside this art, like the
  0.94-width box forgiveness stays inside the cells. Its ~42/44-deg faces carry
  LandableSlope (landing-gate opt-in).

Style: same SDF body/bevel/outline/mottle language as generate_piece_sprites.py
(imported), plus carved ashlar course joints and a pharaoh-gold capstone.

Usage: python3 Tools/generate_pyramid_sprite.py [--preview <dir>]
"""

import os
import sys
import uuid

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from generate_piece_sprites import (  # noqa: E402
    CELL, BLEED, R, OUTLINE, BEVEL,
    mottle_field, desaturate, shift_down,
)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "Assets", "Resources", "Skins", "Classic")
META_TEMPLATE = os.path.join(OUT_DIR, "piece_O.png.meta")

BODY = np.array([213, 179, 128], np.float32) / 255.0   # warm ashlar sandstone
GOLD = np.array([240, 186, 70], np.float32) / 255.0    # pharaoh-gold capstone
OUTLINE_LEVEL = 0.22                                    # Egypt preset outline
APEX_Y = 1.92          # visual apex (local units above the piece pivot)
BASE_Y = -0.5          # base line
SHOULDER_Y = 0.5       # top of the straight base course = foot of the pyramid
CAP_Y = 1.55           # capstone starts here
COURSE_YS = (SHOULDER_Y, 0.85, 1.2, CAP_Y)   # horizontal mortar joints
COURSE_JOINTS = (                  # staggered vertical joints per course span
    ((BASE_Y, SHOULDER_Y), (-0.5, 0.5)),     # the three cubes of the base course
    ((SHOULDER_Y, 0.85), (-0.55, 0.55)),
    ((0.85, 1.2), (0.0,)),
    ((1.2, CAP_Y), (-0.2, 0.2)),
)

W = 3 * CELL + 2 * BLEED
HALF_EXTENT_Y = 2.0                # covers [-0.5, 1.92] with margin, symmetric
H = 2 * int(HALF_EXTENT_Y * CELL) + 2 * BLEED


def px_of(xl):
    return W / 2.0 + xl * CELL


def py_of(yl):
    return H / 2.0 - yl * CELL


def sd_polygon(verts, xs, ys):
    """Exact signed distance to a polygon (IQ formula), vectorised."""
    n = len(verts)
    d = np.full(xs.shape, 1e18, np.float32)
    sign = np.ones(xs.shape, np.float32)
    for i in range(n):
        jx, jy = verts[i - 1]
        ix, iy = verts[i]
        ex, ey = jx - ix, jy - iy
        wx, wy = xs - ix, ys - iy
        t = np.clip((wx * ex + wy * ey) / (ex * ex + ey * ey), 0.0, 1.0)
        bx, by = wx - ex * t, wy - ey * t
        d = np.minimum(d, bx * bx + by * by)
        c0 = ys >= iy
        c1 = ys < jy
        c2 = ex * wy > ey * wx
        flip = (c0 & c1 & c2) | (~c0 & ~c1 & ~c2)
        sign = np.where(flip, -sign, sign)
    return sign * np.sqrt(d)


def carve_line(mask, col, interior, depth=0.55, lip=0.30):
    """The generator's carved-joint treatment: dark core, lit lower lip, shadow above."""
    col *= (1.0 - depth * mask * interior)[..., None]
    lo = shift_down(mask, 5) * np.clip((0.38 - mask) / 0.38, 0.0, 1.0)
    hi = shift_down(mask, -4) * np.clip((0.38 - mask) / 0.38, 0.0, 1.0)
    col *= (1.0 + lip * lo * interior)[..., None]
    col *= (1.0 - 0.13 * hi * interior)[..., None]
    return col


def line_mask(dist_px, half_width):
    return np.clip(1.0 - np.abs(dist_px) / half_width, 0.0, 1.0) ** 0.8


def render():
    nprng = np.random.RandomState(0x9172a)
    ys, xs = np.mgrid[0:H, 0:W].astype(np.float32)
    xs += 0.5
    ys += 0.5

    # --- silhouette: rounded pentagon (straight base course + pyramid top) -----
    rl = R / CELL
    verts_local = [
        (-1.5 + rl, BASE_Y + rl),        # bottom-left
        (-1.5 + rl, SHOULDER_Y - 0.35 * rl),  # left shoulder
        (0.0, APEX_Y - 1.5 * rl),        # apex
        (1.5 - rl, SHOULDER_Y - 0.35 * rl),   # right shoulder
        (1.5 - rl, BASE_Y + rl),         # bottom-right
    ]
    verts = [(px_of(x), py_of(y)) for x, y in verts_local]
    sdf = sd_polygon(verts, xs, ys)
    if sdf[int(py_of(0.0)), int(px_of(0.0))] > 0:
        sdf = -sdf                       # normalise winding: negative inside
    sdf -= R

    alpha = np.clip(0.5 - sdf, 0.0, 1.0)
    inside = alpha > 0.0
    body = sdf < -OUTLINE * 0.35

    # --- luminance: same model as the piece generator ---------------------------
    grad = 1.13 - 0.36 * (ys / H) ** 1.15
    mottle = mottle_field(W, H, nprng)
    grain = (nprng.rand(H, W).astype(np.float32) - 0.5) * 0.05
    # alternating strata brightness, band edges exactly on the course joints
    blockb = np.ones((H, W), np.float32)
    band_edges = (BASE_Y,) + COURSE_YS + (APEX_Y,)
    band_vals = (0.028, -0.030, 0.034, -0.038, 0.0)
    for k in range(len(band_edges) - 1):
        band = (ys <= py_of(band_edges[k])) & (ys > py_of(band_edges[k + 1]))
        blockb[band] += band_vals[k]
    lum = grad * blockb * (1.0 + mottle * 0.17) * (1.0 + grain)
    col = BODY[None, None, :] * lum[..., None]

    # --- capstone: solid gold tip above CAP_Y ------------------------------------
    capness = np.clip((py_of(CAP_Y) - ys) / (0.03 * CELL) + 0.5, 0.0, 1.0)
    cap_col = GOLD[None, None, :] * (lum * 1.10)[..., None]
    col = col * (1.0 - capness[..., None]) + cap_col * capness[..., None]

    # --- bevel rim (tempered on the slopes so it reads dusk-lit stone) -----------
    gy, gx = np.gradient(sdf)
    band = np.clip((sdf + OUTLINE + BEVEL) / BEVEL, 0.0, 1.0) ** 1.6
    band *= np.clip((-sdf - OUTLINE * 0.55) / (OUTLINE * 0.45), 0.0, 1.0)
    upness = np.clip(-gy, 0.0, 1.0)
    topness = np.clip((upness - 0.15) / 0.85, 0.0, 1.0) ** 2.2
    botness = np.clip((gy - 0.25) / 0.5, 0.0, 1.0)
    sideness = np.clip((np.abs(gx) - 0.25) / 0.5, 0.0, 1.0) * (1.0 - topness) * (1.0 - botness)
    col *= (1.0 - 0.09 * band)[..., None]
    hi_col = 1.0 - (1.0 - BODY) * 0.42
    hi_gold = 1.0 - (1.0 - GOLD) * 0.30
    hi = hi_col[None, None, :] * (1.0 - capness[..., None]) + hi_gold[None, None, :] * capness[..., None]
    k_top = (0.50 + 0.28 * capness) * band * topness
    col = col * (1.0 - k_top[..., None]) + hi * (grad * 1.04)[..., None] * k_top[..., None]
    col *= (1.0 - 0.26 * band * botness)[..., None]
    col *= (1.0 - 0.12 * band * sideness)[..., None]

    # --- ashlar joints -----------------------------------------------------------
    for yl in COURSE_YS:
        m = line_mask(ys - py_of(yl), 4.5) * (sdf < -OUTLINE * 0.6)
        col = carve_line(m, col, body)
    for (y0, y1), joints in COURSE_JOINTS:
        for xl in joints:
            m = (line_mask(xs - px_of(xl), 3.5)
                 * (ys < py_of(y0 + 0.02)) * (ys > py_of(y1 - 0.02))
                 * (sdf < -OUTLINE * 0.8))
            col = carve_line(m, col, body, depth=0.45, lip=0.22)

    # --- outline -----------------------------------------------------------------
    out_col = desaturate(BODY, 0.30) * OUTLINE_LEVEL
    t_out = np.clip((sdf + OUTLINE) / 2.0 + 0.5, 0.0, 1.0)
    col = col * (1.0 - t_out[..., None]) + out_col[None, None, :] * grad[..., None] * t_out[..., None]

    rgba = np.zeros((H, W, 4), np.uint8)
    rgba[..., :3] = (np.clip(col, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)
    rgba[..., 3] = (alpha * 255.0 + 0.5).astype(np.uint8)
    rgba[~inside, :3] = 0
    return Image.fromarray(rgba)


def write_atomic(path, data: bytes):
    tmp = path + ".tmp"
    with open(tmp, "wb") as f:
        f.write(data)
        f.flush()
        os.fsync(f.fileno())
    os.replace(tmp, path)


def main():
    img = render()
    out_png = os.path.join(OUT_DIR, "piece_Pyramid.png")
    from io import BytesIO
    buf = BytesIO()
    img.save(buf, "PNG")
    write_atomic(out_png, buf.getvalue())
    print(f"{out_png}  ({img.width}x{img.height})")

    meta_path = out_png + ".meta"
    if not os.path.exists(meta_path):  # keep the guid stable across reruns
        with open(META_TEMPLATE, "r") as f:
            meta = f.read()
        old_guid = [l for l in meta.splitlines() if l.startswith("guid:")][0].split()[1]
        meta = meta.replace(old_guid, uuid.uuid4().hex)
        write_atomic(meta_path, meta.encode())
        print(meta_path)

    if "--preview" in sys.argv:
        pdir = sys.argv[sys.argv.index("--preview") + 1]
        os.makedirs(pdir, exist_ok=True)
        sheet = Image.new("RGBA", (img.width + 80, img.height + 80), (150, 118, 108, 255))
        sheet.alpha_composite(img, (40, 40))
        out = os.path.join(pdir, "preview_pyramid.png")
        sheet.convert("RGB").save(out)
        print(out)


if __name__ == "__main__":
    main()
