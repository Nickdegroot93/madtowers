#!/usr/bin/env python3
"""Bake small, deterministic material fields for the hazard shaders.

RGBA: top-lit stone relief / broad noise / fine noise / carved fracture mask.
Noise is periodic; all expensive random fields and pitting are authored here,
never evaluated as per-fragment hashes on a phone. No standard art is written.
Requires numpy and Pillow. Meta is installed first; PNG is replaced atomically.
"""
from pathlib import Path
import math
import os
import uuid

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
SIZE = 256


def periodic_noise(rng, cells):
    grid = rng.random_sample((cells, cells)).astype(np.float32)
    p = np.arange(SIZE, dtype=np.float32) * cells / SIZE
    index = p.astype(int)
    f = p - index
    f = f * f * (3 - 2 * f)
    a = grid[index[:, None], index[None, :]]
    b = grid[index[:, None], (index[None, :] + 1) % cells]
    c = grid[(index[:, None] + 1) % cells, index[None, :]]
    d = grid[(index[:, None] + 1) % cells, (index[None, :] + 1) % cells]
    return (a * (1 - f) + b * f) * (1 - f[:, None]) + (c * (1 - f) + d * f) * f[:, None]


def write_texture(name, pixels, sprite=False):
    p = ROOT / "Assets" / "Resources" / (name + ".png")
    meta = Path(str(p) + ".meta")
    if not meta.exists():
        text = f"""fileFormatVersion: 2
guid: {uuid.uuid4().hex}
TextureImporter:
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: {0 if sprite else 1}
    sRGBTexture: {1 if sprite else 0}
    borderMipMap: 0
  isReadable: 0
  streamingMipmaps: 0
  textureFormat: 1
  maxTextureSize: 256
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: {1 if sprite else 0}
    wrapV: {1 if sprite else 0}
    wrapW: 0
  nPOTScale: 0
  compressionQuality: 100
  textureType: {8 if sprite else 0}
  textureShape: 1
  spriteMode: {1 if sprite else 0}
  spriteMeshType: 1
  spritePixelsToUnits: 32
  spritePivot: {{x: 0.5, y: 0.5}}
  spriteSheet:
    serializedVersion: 2
    sprites: []
    spriteID: {uuid.uuid4().hex}
  alphaSource: 1
  alphaIsTransparency: {1 if sprite else 0}
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 256
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 100
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
  userData: Hazard material data; linear RGBA; repeat; never a sprite
  assetBundleName:
  assetBundleVariant:
"""
        temp = Path(str(meta) + ".tmp")
        temp.write_text(text)
        os.replace(temp, meta)
    temp = Path(str(p) + ".tmp")
    Image.fromarray(pixels).save(temp, format="PNG")
    os.replace(temp, p)
    print(p)


def main():
    rng = np.random.RandomState(41827)
    broad = periodic_noise(rng, 3) * .62 + periodic_noise(rng, 7) * .38
    medium = periodic_noise(rng, 13)
    fine = periodic_noise(rng, 80) * .65 + periodic_noise(rng, 128) * .35
    height = broad * .6 + medium * .3 + periodic_noise(rng, 29) * .1
    # Straight-above light. Periodic differences keep the tile edge continuous.
    slope = (np.roll(height, 1, axis=0) - np.roll(height, -1, axis=0)) * 2.9
    light = 1 + np.clip(slope, -.17, .17) + (broad - .5) * .16 + (fine - .5) * .10
    flakes = Image.new("L", (SIZE, SIZE), 0)
    draw = ImageDraw.Draw(flakes)
    for i in range(95):
        x, y = rng.uniform(0, SIZE, 2)
        radius = rng.uniform(1.2, 3.4) if i < 72 else rng.uniform(5, 16)
        points = [(x + math.cos(j * math.tau / 5) * radius * rng.uniform(.7, 1.2),
                   y + math.sin(j * math.tau / 5) * radius * .55 * rng.uniform(.7, 1.2)) for j in range(5)]
        value = int(rng.uniform(70, 210))
        for dx in (-SIZE, 0, SIZE):
            for dy in (-SIZE, 0, SIZE):
                draw.polygon([(a + dx, b + dy) for a, b in points], fill=value)
    pits = np.asarray(flakes.filter(ImageFilter.GaussianBlur(.55)), np.float32) / 255
    light *= 1 - pits * .34
    light += np.maximum(np.roll(pits, 2, axis=0) - pits, 0) * .25
    cracks = Image.new("L", (SIZE, SIZE), 0)
    cd = ImageDraw.Draw(cracks)
    cd.line([(0, 84), (31, 84), (50, 74), (85, 93), (107, 87), (139, 109), (169, 102), (196, 78), (222, 84), (256, 84)], fill=255, width=3)
    cd.line([(178, 0), (178, 23), (166, 51), (180, 69), (169, 102), (150, 126), (157, 150)], fill=245, width=3)
    cd.line([(178, 256), (178, 232), (191, 211), (179, 188), (198, 169)], fill=255, width=3)
    cd.line([(50, 74), (44, 56), (28, 45)], fill=140, width=2)
    fracture = np.asarray(cracks.filter(ImageFilter.GaussianBlur(.45)), np.float32) / 255
    data = np.stack([np.clip(light * .5, 0, 1), broad, fine, fracture], axis=2)
    write_texture("HazardSurface", (data * 255 + .5).astype(np.uint8))
    # A chipped, shaded flake for opt-in hazard shatters. Same one-unit bounds
    # as RuntimeSprites.Square, so existing particle sizes/trajectories stay put.
    chip = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    cd = ImageDraw.Draw(chip)
    cd.polygon([(4, 9), (14, 2), (28, 7), (30, 22), (20, 30), (5, 25), (1, 16)], fill=(168, 161, 145, 255))
    cd.polygon([(4, 9), (14, 2), (28, 7), (23, 11), (12, 8)], fill=(235, 228, 209, 255))
    cd.polygon([(23, 11), (28, 7), (30, 22), (20, 30), (19, 22)], fill=(105, 103, 94, 255))
    cd.line([(8, 15), (14, 18), (19, 16), (24, 20)], fill=(122, 119, 106, 255), width=1)
    write_texture("HazardShard", np.asarray(chip), sprite=True)
    # Periodic cooling plates. Distance to the nearest plate boundary is baked,
    # so the lava shader needs one texture sample, never a fragment Voronoi search.
    magma_rng = np.random.RandomState(9206)
    yy, xx = np.mgrid[:SIZE, :SIZE].astype(np.float32)
    warp_x = (periodic_noise(magma_rng, 9) - .5) * 10
    warp_y = (periodic_noise(magma_rng, 11) - .5) * 10
    distances = []
    for sy in range(3):
        for sx in range(3):
            px, py = (np.array([sx, sy]) + magma_rng.uniform(.2, .8, 2)) * SIZE / 3
            dx = (xx + warp_x - px + SIZE / 2) % SIZE - SIZE / 2
            dy = (yy + warp_y - py + SIZE / 2) % SIZE - SIZE / 2
            distances.append(np.sqrt(dx * dx + dy * dy))
    closest = np.sort(np.stack(distances), axis=0)
    gap = np.clip((closest[1] - closest[0]) / 20, 0, 1)
    write_texture("MagmaCracks", (gap * 255 + .5).astype(np.uint8))


if __name__ == "__main__":
    main()
