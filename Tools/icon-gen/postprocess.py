#!/usr/bin/env python3
"""Raw generation -> game-ready icon. v2 (the v1 putalpha bug replaced the alpha channel,
exposing RGB residue under transparent pixels — that's what drew ghost tile borders in-game).

Most raws are stickers: opaque artwork + glow on a transparent field, with a faint low-alpha
"tile border" ghost the model sketched. Pipeline per icon:

1. Ghost removal: keep low-alpha pixels only near solid artwork (dilated core mask);
   isolated low-alpha ridges/dust vanish.
2. Subject normalization: square window centered on the artwork so its max dimension
   spans ~76% of the canvas — uniform apparent size across the set.
3. Composite onto the UI ground #0B0E13, downscale to 512, bake 12% rounded-corner alpha.

Fully-opaque raws (no transparent field) skip 1-2 and just composite/resize/mask.
"""
import json, pathlib
from PIL import Image, ImageDraw, ImageFilter

ROOT = pathlib.Path(__file__).parent
RAW = ROOT / "gen_raw"
OUT = ROOT / "processed"
OUT.mkdir(exist_ok=True)

SIZE = 512
RADIUS = int(SIZE * 0.12)
GROUND = (11, 14, 19)          # #0B0E13, matches AbilityCardView's IconTile
SUBJECT_SPAN = 0.76            # artwork max-dimension fraction of the canvas
CORE_ALPHA = 150               # "solid artwork" threshold
GHOST_REACH = 4                # dilation passes; MaxFilter(31) each ≈ 15px -> ~60px total

mask = Image.new("L", (SIZE * 4, SIZE * 4), 0)
ImageDraw.Draw(mask).rounded_rectangle([0, 0, SIZE * 4 - 1, SIZE * 4 - 1], radius=RADIUS * 4, fill=255)
mask = mask.resize((SIZE, SIZE), Image.LANCZOS)

def clean_and_window(im):
    """Strip ghost pixels; return (cleaned RGBA, square crop window) or (im, None) if opaque."""
    a = im.getchannel("A")
    hist = a.histogram()
    if sum(hist[:16]) / (im.width * im.height) < 0.005:
        return im, None  # baked-background raw, nothing to clean
    core = a.point(lambda v: 255 if v >= CORE_ALPHA else 0)
    keep = core
    for _ in range(GHOST_REACH):
        keep = keep.filter(ImageFilter.MaxFilter(31))
    cleaned_a = Image.composite(a, Image.new("L", a.size, 0), keep.point(lambda v: 255 if v else 0))
    im = im.copy()
    im.putalpha(cleaned_a)
    bbox = core.getbbox()
    if bbox is None:
        return im, None
    w, h = im.size
    cx, cy = (bbox[0] + bbox[2]) / 2, (bbox[1] + bbox[3]) / 2
    side = min(w, max(bbox[2] - bbox[0], bbox[3] - bbox[1]) / SUBJECT_SPAN)
    x0 = min(max(cx - side / 2, 0), w - side)
    y0 = min(max(cy - side / 2, 0), h - side)
    return im, (round(x0), round(y0), round(x0 + side), round(y0 + side))

manifest = json.loads((ROOT / "manifest.json").read_text())
bad = []
for entry in manifest["abilities"]:
    src = RAW / f"{entry['name']}.png"
    if not src.exists():
        bad.append(f"missing: {entry['name']}")
        continue
    im = Image.open(src).convert("RGBA")
    if im.width != im.height:
        s = min(im.size)
        im = im.crop(((im.width - s) // 2, (im.height - s) // 2,
                      (im.width + s) // 2, (im.height + s) // 2))
    im, window = clean_and_window(im)
    if window:
        im = im.crop(window)
    ground = Image.new("RGBA", im.size, GROUND + (255,))
    im = Image.alpha_composite(ground, im).resize((SIZE, SIZE), Image.LANCZOS)
    im.putalpha(mask)
    im.save(OUT / f"{entry['icon']}.png", optimize=True)

print(f"processed {len(list(OUT.glob('*.png')))} / {len(manifest['abilities'])}")
for b in bad: print(b)
