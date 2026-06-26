#!/usr/bin/env python3
"""Regenerate the ability-card overlay sprites from the authored frame PNG.

The framed ability cards (Assets/SourceFiles/Scripts/Abilities/AbilityChoiceController.cs)
are built in code from a single hand-authored frame, plus four sprites DERIVED from that
frame so they align to it pixel-perfectly. Those four are produced by this script; if the
frame art is ever re-exported, re-run this so the derived sprites track the new pixels.

Inputs  (committed, hand-authored — NOT generated here):
    Assets/Resources/AbilityCardFrame.png   grayscale, transparent bg, 752x1344

Outputs (overwritten in place; their .meta files are committed and reused on re-import):
    AbilityCardIconBacking.png  white fill of the icon recess (alpha = exact recess shape)
    AbilityCardGem.png          the faceted gem (grayscale + alpha) for a re-tintable jewel
    AbilityCardGlowDot.png       standalone soft radial for the gem glow (own canvas)
    AbilityCardRimGlow.png       thin outer halo around the card silhouette (padded canvas)

The C# side measures slot rects / aspect against a 752x1344 frame; if you change the frame
size you must also re-measure those constants (the loader logs a warning on a mismatch).

Note (Unity import race): these PNGs are written next to pre-existing, committed .meta files,
so Unity re-imports them with stable GUIDs. If you add a NEW derived sprite, author its .meta
atomically alongside the PNG (see Tools/ability-card-art/README.md).

Usage:  python3 Tools/ability-card-art/generate_card_sprites.py
Requires: pillow, numpy
"""
import os
from collections import deque

import numpy as np
from PIL import Image, ImageFilter

RES = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Resources")
FRAME = os.path.join(RES, "AbilityCardFrame.png")


def load_luma_alpha():
    im = Image.open(FRAME).convert("RGBA")
    a = np.asarray(im).astype(np.float32) / 255.0
    luma = 0.299 * a[..., 0] + 0.587 * a[..., 1] + 0.114 * a[..., 2]
    return luma, a[..., 3], im.size  # (W,H)


def flood(mask, seed):
    """Connected region of `mask` reachable from seed=(y,x)."""
    h, w = mask.shape
    seen = np.zeros_like(mask, bool)
    if not mask[seed]:
        return seen
    dq = deque([seed]); seen[seed] = True
    while dq:
        y, x = dq.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < h and 0 <= nx < w and not seen[ny, nx] and mask[ny, nx]:
                seen[ny, nx] = True; dq.append((ny, nx))
    return seen


def save_white(alpha, path):
    h, w = alpha.shape
    out = np.zeros((h, w, 4), np.uint8); out[..., 0:3] = 255
    out[..., 3] = (np.clip(alpha, 0, 1) * 255).astype(np.uint8)
    Image.fromarray(out).save(path)
    print("wrote", os.path.basename(path))


def icon_backing(luma):
    """Flood the flat recess plateau (uniform mid-bright), erode ~1px so it tucks under the
    silver bevel, light feather for clean AA. Alpha = exact recess shape; RGB white."""
    mask = (luma > 0.60) & (luma < 0.82)
    region = flood(mask, (618, 376))
    m = Image.fromarray((region * 255).astype(np.uint8))
    m = m.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(0.6))
    save_white(np.asarray(m).astype(np.float32) / 255.0, os.path.join(RES, "AbilityCardIconBacking.png"))


def gem(luma):
    """Flood the bright gem near top-center (capped so it can't leak into the top border bar).
    Keep the gem's facet shading (grayscale) with alpha = mask so it can be re-tinted lighter."""
    h, w = luma.shape
    mask = (luma > 0.50)
    cap = np.zeros_like(mask); cap[20:140, 250:502] = True
    region = flood(mask & cap, (75, 376))
    gmask = Image.fromarray((region * 255).astype(np.uint8))
    gmask = gmask.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(0.6))
    ga = np.asarray(gmask).astype(np.float32) / 255.0
    gray = np.clip(luma * 1.05, 0, 1)
    out = np.zeros((h, w, 4), np.uint8)
    out[..., 0] = out[..., 1] = out[..., 2] = (gray * 255).astype(np.uint8)
    out[..., 3] = (ga * 255).astype(np.uint8)
    Image.fromarray(out).save(os.path.join(RES, "AbilityCardGem.png"))
    print("wrote AbilityCardGem.png")


def glow_dot():
    """Standalone soft radial (own canvas, not tied to the frame), windowed so alpha hits a
    HARD zero well inside the quad - otherwise the square sprite edge shows as a faint line."""
    S = 256; c = (S - 1) / 2.0
    yy, xx = np.mgrid[0:S, 0:S]
    r = np.sqrt((yy - c) ** 2 + (xx - c) ** 2) / (S / 2.0)
    core = np.exp(-(r / 0.42) ** 2)
    t = np.clip((0.92 - r) / (0.92 - 0.30), 0, 1)
    alpha = np.clip(core * (t * t * (3 - 2 * t)), 0, 1)
    alpha[r >= 0.92] = 0
    save_white(alpha, os.path.join(RES, "AbilityCardGlowDot.png"))


def rim_glow(alpha_chan, size):
    """Thin outer halo: blur the card silhouette OUTWARD on a padded canvas, subtract the
    (dilated) interior so only the rim remains, steep falloff, hard-zero border. The padding
    fraction here must match RimGlowMarginFrac in AbilityChoiceController.cs (0.06)."""
    w, h = size
    sil = (alpha_chan > 0.5).astype(np.float32)
    f = 0.06; px = round(f * w); py = round(f * h)
    pad = np.zeros((h + 2 * py, w + 2 * px), np.float32); pad[py:py + h, px:px + w] = sil
    silimg = Image.fromarray((pad * 255).astype(np.uint8))
    blur = np.asarray(silimg.filter(ImageFilter.GaussianBlur(11))).astype(np.float32) / 255.0
    dil = np.asarray(silimg.filter(ImageFilter.MaxFilter(5))).astype(np.float32) / 255.0
    ring = np.clip(blur * (1.0 - dil), 0, 1)
    ring = (ring / ring.max()) ** 1.5
    b = 4; ring[:b, :] = 0; ring[-b:, :] = 0; ring[:, :b] = 0; ring[:, -b:] = 0
    save_white(ring, os.path.join(RES, "AbilityCardRimGlow.png"))


def main():
    luma, alpha_chan, size = load_luma_alpha()
    if size != (752, 1344):
        print(f"WARNING: frame is {size}, expected (752, 1344). Seed coords / caps below were "
              "tuned for that size and will need re-measuring.")
    icon_backing(luma)
    gem(luma)
    glow_dot()
    rim_glow(alpha_chan, size)
    print("done — refresh Unity to re-import.")


if __name__ == "__main__":
    main()
