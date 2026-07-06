#!/usr/bin/env python3
"""Night-water band for waterfront backdrops: dark base, sparse lighter ripple dashes,
and vertical neon reflection streaks (the city above) that waver slightly and fade
with depth. Horizontally seamless (drawn on a 64 px column grid); drawn once as a
band, so vertical tiling is not required.

    python3 Tools/generate_water_sprite.py "<r,g,b base>" "<r,g,b;r,g,b;...>" <seed> <out.png>
"""
import math, sys, zlib
from PIL import Image

base = tuple(int(v) for v in sys.argv[1].split(","))
glows = [tuple(int(v) for v in c.split(",")) for c in sys.argv[2].split(";")]
sd = zlib.crc32(sys.argv[3].encode()) & 0xffff
out = sys.argv[4]

W, H, COL = 1024, 320, 64


def h01(*vals):
    h = 2166136261
    for v in vals:
        h = ((h ^ int(v)) * 16777619) & 0xffffffff
    return (h % 10000) / 10000.0


img = Image.new("RGBA", (W, H))
px = img.load()
for y in range(H):
    depth = y / (H - 1)
    for x in range(W):
        r, g, b = base
        # sparse horizontal ripple dashes, lighter than the base
        if int(h01(sd, x // 24, y // 6) * 100) < 8 and (y % 6) < 2:
            r, g, b = (min(255, c * 1.6) for c in base)
        px[x, y] = (int(r), int(g), int(b), 255)
# vertical neon reflection streaks per 64px column, wavering, fading with depth
for ci in range(W // COL):
    if h01(sd, ci, 1) < 0.25:
        continue  # some columns stay dark
    glow = glows[int(h01(sd, ci, 2) * len(glows)) % len(glows)]
    cx0 = ci * COL + 8 + int(h01(sd, ci, 3) * (COL - 20))
    half_w = 2 + int(h01(sd, ci, 4) * 3)
    length = int(H * (0.5 + 0.45 * h01(sd, ci, 5)))
    for y in range(length):
        waver = int(2.5 * math.sin(y / 14.0 + ci))
        fade = (1.0 - y / length) * 0.55
        if int(h01(sd, ci, y // 9) * 100) < 22:
            continue  # broken reflection, like chop
        for dx in range(-half_w, half_w + 1):
            x = (cx0 + waver + dx) % W
            w = fade * (1.0 - abs(dx) / (half_w + 1))
            pr, pg, pb, _ = px[x, y]
            px[x, y] = (int(pr * (1 - w) + glow[0] * w),
                        int(pg * (1 - w) + glow[1] * w),
                        int(pb * (1 - w) + glow[2] * w), 255)
img.save(out)
print(out, img.size)
