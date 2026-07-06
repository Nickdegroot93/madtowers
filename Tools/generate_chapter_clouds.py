#!/usr/bin/env python3
"""Flat vector-style cloud strips for chapter backdrops, matching the DenielHest packs'
minimal silhouette language: staggered lumpy clouds + smaller cloudlets, flat-ish
scalloped undersides, rounded ends. Every shape is a union of baseline-tangent circles
(no rounded-rect "pills"). Deterministic per theme.

    python3 gen_chapter_clouds.py <r,g,b> <seed> <out.png>
"""
import random, sys
from PIL import Image, ImageDraw

W, H = 2560, 320
COL = tuple(int(v) for v in sys.argv[1].split(",")) + (255,)
rng = random.Random(sys.argv[2])
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))

def cloud(cx, base_y, scale, puffs):
    """Union of circles tangent to one baseline: lumpy top, gently scalloped flat
    underside, rounded ends. Center puffs are the tallest."""
    d = ImageDraw.Draw(img)
    radii = []
    for i in range(puffs):
        center_bias = 1.0 - abs(i - (puffs - 1) / 2) / max(1.0, (puffs - 1) / 2)
        radii.append(int((24 + 26 * center_bias + rng.randint(0, 8)) * scale))
    span = sum(int(r * 1.15) for r in radii[:-1])
    x = cx - span // 2
    for r in radii:
        d.ellipse((x - r, base_y - 2 * r, x + r, base_y), fill=COL)
        x += int(r * 1.15)

# staggered diagonal composition like the vendor strips: three main clouds,
# four trailing cloudlets (small real clouds, not pills)
cloud(430, 165, 1.1, 5)
cloud(760, 150, 0.45, 3)
cloud(1280, 235, 0.8, 4)
cloud(1040, 245, 0.35, 2)
cloud(2120, 140, 1.0, 5)
cloud(1840, 135, 0.4, 3)
cloud(2430, 255, 0.35, 2)

img.save(sys.argv[3])
print(sys.argv[3], img.size)
