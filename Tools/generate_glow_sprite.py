#!/usr/bin/env python3
"""Soft glow sprites for backdrop light overlays (moon halos, street-lantern pools,
city-glow horizon bands). Vendor packs bake these as OPAQUE color fills meant for an
additive shader we don't use — these are the normal-alpha-blend equivalents: the color
stays constant and the ALPHA falls off smoothly, so a plain SpriteRenderer reads as glow.

    python3 Tools/generate_glow_sprite.py radial "<r,g,b>" <peak_alpha> <out.png>
    python3 Tools/generate_glow_sprite.py band   "<r,g,b>" <peak_alpha> <out.png>
    python3 Tools/generate_glow_sprite.py wash   "<r,g,b>" <alpha> <out.png>

radial: 512x512 circular halo, gaussian falloff — no perceivable rim.
band:   1024x256 horizontal strip, uniform across X (tiles seamlessly), vertical
        falloff from a bright core just below center to transparent at both edges.
wash:   64x64 uniform tint. Add as the LAST preset layer with fillView: 1 to color-
        grade the entire backdrop (nostalgic warm cast etc.) — it renders in front
        of all backdrop layers but behind gameplay, so the tower stays readable.
"""
import sys
from PIL import Image

kind = sys.argv[1]
col = tuple(int(v) for v in sys.argv[2].split(","))
peak = float(sys.argv[3])
out = sys.argv[4]

if kind == "radial":
    # Gaussian falloff: no perceivable rim, long atmospheric tail. Author the layer
    # BIG (the halo should span a large part of the screen) with a LOW peak — dense
    # saturated balls read as spotlights, not night-sky glow.
    import math
    S = 512
    img = Image.new("RGBA", (S, S))
    px = img.load()
    c = (S - 1) / 2
    for y in range(S):
        for x in range(S):
            d = ((x - c) ** 2 + (y - c) ** 2) ** 0.5 / c
            a = int(255 * peak * math.exp(-5.5 * d * d))  # ~0.4% of peak at the rim
            px[x, y] = col + (a,)
elif kind == "band":
    W, H = 1024, 256
    img = Image.new("RGBA", (W, H))
    px = img.load()
    core = 0.62  # brightest line sits just below center (city glow hugs the skyline)
    for y in range(H):
        t = y / (H - 1)
        d = abs(t - core) / (core if t < core else (1 - core))
        f = max(0.0, 1.0 - d)
        a = int(255 * peak * f * f)
        row = col + (a,)
        for x in range(W):
            px[x, y] = row
elif kind == "wash":
    img = Image.new("RGBA", (64, 64), col + (int(255 * peak),))
else:
    sys.exit("kind must be radial|band|wash")

img.save(out)
print(out, img.size)
