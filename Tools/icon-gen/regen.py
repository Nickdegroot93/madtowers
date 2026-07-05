#!/usr/bin/env python3
"""Regenerate named entries with a hardened background clause appended to the template."""
import json, pathlib, sys
sys.path.insert(0, str(pathlib.Path(__file__).parent))
import generate_all as g

HARD_BG = (" IMPORTANT: the entire square canvas background must be solid near-black (#0B0E13) "
           "edge to edge - never grey, never white, no outer frame, no white margin, no drop "
           "shadow; the neon artwork glows against pure darkness that fills the whole image.")

names = set(sys.argv[1:])
targets = [e for e in g.manifest["abilities"] if e["name"] in names]
for e in targets:
    e["subject"] = e["subject"] + "."  # no-op marker; real change is the template below
    (g.RAW / f"{e['name']}.png").unlink(missing_ok=True)

g.TEMPLATE = g.TEMPLATE + HARD_BG

from concurrent.futures import ThreadPoolExecutor
with ThreadPoolExecutor(max_workers=6) as pool:
    for msg in pool.map(g.generate, targets):
        print(msg, flush=True)
