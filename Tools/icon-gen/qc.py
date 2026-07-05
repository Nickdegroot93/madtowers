#!/usr/bin/env python3
"""Classify raw generations by background: PASS (near-black), FRAME (light surround with a
dark slab inside -> auto-croppable), GREY (off-template ground -> regenerate)."""
import json, pathlib, sys
from PIL import Image

ROOT = pathlib.Path(__file__).parent
RAW = ROOT / "gen_raw"

def corner_lum(im):
    w, h = im.size
    p = int(w * 0.06)
    px = im.convert("RGB")
    total, n = 0.0, 0
    for (x0, y0) in [(0, 0), (w - p, 0), (0, h - p), (w - p, h - p)]:
        region = px.crop((x0, y0, x0 + p, y0 + p)).resize((8, 8))
        for r, g, b in region.getdata():
            total += (0.299 * r + 0.587 * g + 0.114 * b) / 255
            n += 1
    return total / n

manifest = json.loads((ROOT / "manifest.json").read_text())
result = {"PASS": [], "FRAME": [], "GREY": [], "MISSING": []}
for e in manifest["abilities"]:
    f = RAW / f"{e['name']}.png"
    if not f.exists():
        result["MISSING"].append(e["name"]); continue
    lum = corner_lum(Image.open(f))
    cls = "PASS" if lum < 0.15 else ("FRAME" if lum > 0.5 else "GREY")
    result[cls].append(f"{e['name']}:{lum:.2f}")

for k, v in result.items():
    print(f"{k} ({len(v)}): {' '.join(v)}")
