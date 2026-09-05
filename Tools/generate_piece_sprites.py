#!/usr/bin/env python3
"""Procedurally renders Tricky-Towers-style whole-piece tetromino sprites, PER THEME.

Requires numpy + Pillow. Output: piece_X.png into Assets/Resources/Skins/<Theme>/
for each entry in THEME_PRESETS. A theme without its own entry falls back to the
Classic pieces at runtime (ChapterSkins fallback chain) - adding a block look for
a theme = adding one preset dict here and rerunning.

Style per piece (carved stone slabs, see STYLE.md):
  - rounded silhouette, THICK near-black outline that keeps the base hue
  - light from straight above: bright embossed bevel just inside the top edge,
    shadowed bevel along the bottom, neutral-dark sides
  - vertical gradient (lighter top, darker bottom) + mottled multi-octave stone
    body + shallow chiselled relief + fine grain (no per-cell colour variation)
  - chunky embossed cracks along cell seams that run all the way through the
    outline (each cell reads as its own stone), plus shorter "plate" cracks
    growing inward from the silhouette edge, plus faint wandering hairlines
  - clustered angular pits and worn bevel facets, contained inside the silhouette

Deterministic per shape (seeded) so regeneration is stable and every theme keeps
the same crack layout (only the palette changes chapter to chapter).
Style rules: STYLE.md - every theme's pieces are materials of that chapter's
world (Desert is the reference); hue families stay as loose anchors, shape
pairs keep RGB distance >= ~40.

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

# One entry per theme that wants its own block look. "colors" are chapter
# materials (STYLE.md); "outline" is the outline value factor (fraction of the
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
    # Jungle Depths: every piece a jungle material - lichen, fern, orchid, papaya,
    # river stone - under dark organic outlines.
    "Jungle": {
        "colors": {
            "I": (170, 190, 150),     # pale lichen
            "O": (198, 186, 96),     # sun-dappled gold
            "T": (170, 120, 158),     # wild orchid
            "S": (118, 162, 84),     # fern
            "Z": (188, 94, 70),     # heliconia red
            "J": (86, 128, 118),     # leaf-shadow teal
            "L": (198, 138, 72),     # ripe papaya
            "Pip": (198, 190, 154),  # balsa
            "Domino": (150, 132, 102),  # bark
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
            "I": (188, 214, 228),     # glacial ice
            "O": (216, 196, 140),     # pale winter sun
            "T": (168, 146, 186),     # frost heather
            "S": (128, 160, 138),     # frosted pine
            "Z": (188, 100, 104),     # cold berry
            "J": (110, 140, 180),     # deep ice blue
            "L": (208, 152, 108),     # alpenglow amber
            "Pip": (214, 218, 224),  # fresh snow
            "Domino": (160, 168, 180),  # granite
        },
        "outline": 0.24,
        "edgeShine": {"Pip": 0.15, "Domino": 0.15},
        "preview_bg": (186, 196, 222),
    },
    # Fangkuai District: dusk-market tones under a mauve sunset sky. Every hue leans
    # warm and lantern-lit; the pack's neon-pink window glow lives in Pip/Domino.
    "Fangkuai": {
        "colors": {
            "I": (206, 198, 178),     # moon paper
            "O": (224, 176, 96),     # lantern gold
            "T": (170, 116, 158),     # plum dusk
            "S": (110, 160, 118),     # jade
            "Z": (200, 88, 74),     # cinnabar
            "J": (110, 118, 168),     # indigo night
            "L": (214, 136, 78),     # persimmon
            "Pip": (196, 204, 180),  # pale celadon
            "Domino": (152, 108, 96),  # rosewood
        },
        "outline": 0.21,
        "edgeShine": {"Pip": 0.15, "Domino": 0.15},
        "preview_bg": (96, 72, 96),
    },
    # Kvartal 4: sovietwave night - courtyard materials: lamplit snow, sodium amber,
    # panel concrete, faded brick, wet asphalt against the near-black green sky.
    "Kvartal": {
        "colors": {
            "I": (196, 200, 190),     # lamplit snow
            "O": (216, 174, 96),     # sodium amber
            "T": (172, 138, 186),     # cold lilac
            "S": (118, 148, 122),     # pine in snow
            "Z": (180, 96, 84),     # faded brick
            "J": (112, 132, 162),     # panel concrete
            "L": (200, 132, 80),     # rust
            "Pip": (202, 196, 178),  # worn plaster
            "Domino": (128, 128, 136),  # wet asphalt
        },
        "outline": 0.24,
        "edgeShine": {"Pip": 0.15, "Domino": 0.15},
        "preview_bg": (32, 40, 36),
    },
    # Neon Nightfall: saturated neon against the dark violet glowing-city sky. The one
    # chapter that goes full arcade — every hue at high chroma, near-black outlines so
    # the glow-colors pop like signage.
    "Neon": {
        "colors": {
            "I": (72, 202, 224),     # electric cyan
            "O": (232, 180, 70),     # amber signage
            "T": (188, 96, 200),     # hot magenta
            "S": (96, 200, 140),     # acid mint
            "Z": (226, 84, 120),     # neon coral
            "J": (96, 108, 216),     # ultraviolet
            "L": (236, 130, 66),     # strip orange
            "Pip": (188, 214, 222),  # pale hologram
            "Domino": (134, 130, 168),  # night chrome
        },
        "outline": 0.18,
        "edgeShine": {"Pip": 0.15, "Domino": 0.15},
        "preview_bg": (40, 34, 60),
    },
    # Molten Caldera: basalt night under lava light. Cool identities survive as
    # ash-cooled tones (I/J/S) while the warm shapes (O/Z/L) run hot — gold, lava,
    # molten orange — so the whole bag reads lit-from-beneath without losing the 7 hues.
    "Volcano": {
        "colors": {
            "I": (184, 168, 158),     # warm ash
            "O": (222, 164, 84),     # ember gold
            "T": (172, 118, 130),     # heat-haze mauve
            "S": (150, 146, 92),     # scorched olive
            "Z": (208, 84, 60),     # lava red
            "J": (128, 122, 138),     # basalt
            "L": (226, 126, 58),     # molten orange
            "Pip": (198, 186, 172),  # pumice
            "Domino": (140, 126, 118),  # dark basalt
        },
        "outline": 0.20,
        "edgeShine": {"Pip": 0.15, "Domino": 0.15},
        "preview_bg": (66, 40, 52),
    },
    # Giza Dusk: dusty-rose sunset over sandstone. Warm shapes go pharaoh gold /
    # carnelian / amber; cool identities survive as oxidized teal and lapis so the
    # bag stays readable against the pale dusk silhouettes.
    "Egypt": {
        "colors": {
            "I": (206, 192, 160),     # limestone
            "O": (232, 188, 96),     # pharaoh gold
            "T": (168, 128, 170),     # amethyst
            "S": (140, 162, 100),     # nile reed
            "Z": (202, 96, 70),     # carnelian
            "J": (96, 118, 186),     # lapis
            "L": (212, 132, 62),     # desert amber
            "Pip": (212, 200, 176),  # alabaster
            "Domino": (164, 134, 96),  # aged bronze
        },
        "outline": 0.22,
        "edgeShine": {"Pip": 0.15, "Domino": 0.15},
        "preview_bg": (150, 118, 108),
    },
    # Lost City: alien-moon night in the pack's teal/orange complementary scheme.
    # Cool identities lean oasis-teal and night-blue, warm shapes carry the moonlit
    # gold and rust of the ruins, so the bag belongs to both halves of the palette.
    "LostCity": {
        "colors": {
            "I": (140, 196, 186),     # moonlit teal
            "O": (224, 184, 100),     # moon gold
            "T": (150, 118, 172),     # alien violet
            "S": (104, 158, 128),     # ruin moss
            "Z": (192, 92, 66),     # rust
            "J": (84, 128, 148),     # deep dusk teal
            "L": (214, 134, 70),     # ember
            "Pip": (198, 200, 184),  # moonstone
            "Domino": (124, 146, 142),  # slate-teal
        },
        "outline": 0.21,
        "edgeShine": {"Pip": 0.15, "Domino": 0.15},
        "preview_bg": (36, 52, 58),
    },
    # Sector Isla: green-dusk lagoon light. The whole scene is teal-green, so cool
    # identities stay lagoon/jungle tones and the warm shapes (O/Z/L) carry the
    # sunset-salmon accents from the plate's clouds.
    "Island": {
        "colors": {
            "I": (122, 196, 176),     # lagoon aqua
            "O": (216, 184, 112),     # dusk sand
            "T": (182, 120, 156),     # orchid pink
            "S": (110, 168, 96),     # palm green
            "Z": (204, 92, 84),     # hibiscus
            "J": (88, 132, 150),     # deep water
            "L": (216, 146, 74),     # mango
            "Pip": (208, 196, 168),  # coral sand
            "Domino": (156, 138, 114),  # driftwood
        },
        "outline": 0.21,
        "edgeShine": {"Pip": 0.15, "Domino": 0.15},
        "preview_bg": (74, 110, 92),
    },
    # Hallow's End: jack-o'-lantern light against a blood-dusk graveyard. Warm
    # identities (O/L/Z) carry the pumpkin/ember glow, cool ones stay spectral —
    # every hue lifted enough to read over the near-black silhouette world.
    "Hallow": {
        "colors": {
            "I": (150, 186, 178),     # spectral teal
            "O": (214, 168, 88),     # candlelight
            "T": (156, 108, 170),     # witch violet
            "S": (118, 152, 78),     # toxic moss
            "Z": (178, 74, 64),     # dried blood
            "J": (110, 104, 152),     # midnight plum
            "L": (216, 122, 56),     # pumpkin
            "Pip": (204, 194, 172),  # bone
            "Domino": (134, 106, 96),  # coffin wood
        },
        "outline": 0.21,
        "edgeShine": {"Pip": 0.15, "Domino": 0.15},
        "preview_bg": (52, 30, 44),
    },
    # Monsoon Sector: rain-washed neon-city materials - everything wet and lit by the
    # pack's signature green: mint rain-wash, streetlight amber, neon orchid, brake-light
    # red, sodium orange, deep rain blue.
    "Techno": {
        "colors": {
            "I": (150, 200, 180),   # rain-washed mint
            "O": (210, 180, 90),    # streetlight amber
            "T": (160, 110, 180),   # neon orchid
            "S": (110, 200, 120),   # signal green
            "Z": (200, 80, 80),     # brake-light red
            "J": (90, 120, 170),    # deep rain blue
            "L": (210, 130, 70),    # sodium orange
            "Pip": (190, 200, 192),     # wet concrete
            "Domino": (110, 130, 126),  # dark asphalt teal
        },
        "outline": 0.20,
        "edgeShine": {"Pip": 0.15, "Domino": 0.15},
        "preview_bg": (36, 72, 58),
    },
    # Amber Tide: tropical-sunset materials - shell pink, sun amber, bougainvillea,
    # dusk palm, hibiscus coral, twilight plum, mango - everything the beauty shot's
    # pink-amber dusk would tint.
    "Tide": {
        "colors": {
            "I": (220, 188, 176),   # shell pink
            "O": (232, 186, 96),    # sun amber
            "T": (188, 104, 156),   # bougainvillea
            "S": (132, 150, 96),    # dusk palm
            "Z": (216, 96, 88),     # hibiscus coral
            "J": (122, 108, 160),   # twilight plum
            "L": (224, 138, 72),    # mango
            "Pip": (218, 198, 176),     # pale sand
            "Domino": (150, 112, 128),  # plum driftwood
        },
        "outline": 0.22,
        "edgeShine": {"Pip": 0.15, "Domino": 0.15},
        "preview_bg": (196, 128, 140),
    },
    # Sakura Ridge: muted ukiyo-e / washi tones. These belong to the background's
    # sakura, Fuji, temple indigo, and coral highlights; readability comes from the
    # outline/shape language rather than neon opposite colors.
    "Japan": {
        "colors": {
            "I": (162, 190, 168),     # celadon
            "O": (218, 196, 138),     # washi gold
            "T": (158, 126, 168),     # wisteria
            "S": (136, 162, 104),     # matcha
            "Z": (202, 88, 70),     # torii vermilion
            "J": (92, 108, 156),     # temple indigo
            "L": (210, 140, 92),     # persimmon
            "Pip": (216, 186, 188),  # sakura pink
            "Domino": (140, 138, 146),  # ink stone
        },
        "outline": 0.20,
        "edgeShine": {"Pip": 0.15, "Domino": 0.15},
        "preview_bg": (205, 175, 185),
    },
    # Crimson Core: darkwave signage against the near-black red-lit city. Claims the
    # Neon high-chroma exemption - these read as lit tubes, not pigment - but every
    # hue carries the chapter's warm red-night cast.
    "Crimson": {
        "colors": {
            "I": (96, 160, 200),    # cold signal blue
            "O": (232, 168, 88),    # sodium amber
            "T": (196, 88, 170),    # magenta sign
            "S": (110, 190, 130),   # exit-sign jade
            "Z": (232, 72, 84),     # crimson neon
            "J": (120, 100, 210),   # ultraviolet
            "L": (240, 130, 70),    # ember orange
            "Pip": (230, 200, 205),     # pale rose hologram
            "Domino": (140, 118, 126),  # smog chrome
        },
        "outline": 0.18,
        "edgeShine": {"Pip": 0.15, "Domino": 0.15},
        "preview_bg": (38, 24, 30),
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


def stone_surface(w, h, sdf, seed):
    """One piece-wide weathering field, shared by every chapter and every spawn.

    Value mottling stays within STYLE.md's +/-8%; relief is lit separately from
    straight above. Angular shallow flakes survive minification better than
    stronger pixel noise. Nothing in this pass can change alpha or the outline.
    """
    rng = random.Random(seed)
    nprng = np.random.RandomState(zlib.crc32(seed.encode()) & 0x7fffffff)
    broad = mottle_field(w, h, nprng)
    mottle = np.clip(broad * 0.38, -0.08, 0.08)
    relief = (value_noise(w, h, 60, nprng) * 0.55
              + value_noise(w, h, 28, nprng) * 0.30
              + value_noise(w, h, 9, nprng) * 0.15)
    # Facet slopes, not another colour/noise layer: upward slopes catch light.
    slope = -np.gradient(relief, axis=0)
    relief_light = np.clip(slope * 3.2, -0.13, 0.13)

    flakes = Image.new("L", (w, h), 0)
    pits = Image.new("L", (w, h), 0)
    fd, pd = ImageDraw.Draw(flakes), ImageDraw.Draw(pits)
    # Sample across the whole canvas: never re-roll a material or tint per cell.
    for _ in range(round(w * h / 1400)):
        x, y = rng.randrange(w), rng.randrange(h)
        if sdf[y, x] > -OUTLINE - 4:
            continue
        radius = rng.uniform(1.5, 5.0)
        large = rng.random() < 0.24
        if large:
            radius *= 4.0
        points = []
        for i in range(6):
            angle = math.tau * i / 6
            r = radius * rng.uniform(0.60, 1.20)
            points.append((x + math.cos(angle) * r, y + math.sin(angle) * r * 0.65))
        (fd if large else pd).polygon(points, fill=rng.randrange(100, 230))
    flakes = np.asarray(flakes.filter(ImageFilter.GaussianBlur(1.5)), np.float32) / 255.0
    pits = np.asarray(pits.filter(ImageFilter.GaussianBlur(0.65)), np.float32) / 255.0
    chips = Image.new("L", (w, h), 0)
    cd = ImageDraw.Draw(chips)
    gy, gx = np.gradient(sdf)
    edge_y, edge_x = np.where((sdf < -OUTLINE - 2) & (sdf > -OUTLINE - 4))
    for _ in range(max(1, len(edge_x) // 110)):
        i = rng.randrange(len(edge_x))
        x, y = edge_x[i], edge_y[i]
        nx, ny = float(gx[y, x]), float(gy[y, x])
        width, depth = rng.uniform(6, 15), rng.uniform(6, 17)
        cd.polygon([(x - ny * width, y + nx * width),
                    (x + ny * width, y - nx * width),
                    (x - nx * depth, y - ny * depth)], fill=rng.randrange(140, 230))
    chips = np.asarray(chips.filter(ImageFilter.GaussianBlur(0.8)), np.float32) / 255.0
    # Wide, irregular worn facets break the perfect manufactured outer bevel.
    wear = np.clip(value_noise(w, h, 19, nprng) * 2.8, -0.7, 0.7)
    return mottle, relief_light, flakes, pits, wear, chips


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

    Returns (chunky, hairline, pits, seam_distance):
      chunky   - cell seams + plate cracks (deep carved lines, get the emboss)
      hairline - faint wandering surface cracks
      pits     - small round pit specks
      seam_distance - distance in pixels to structural joints (not plate cracks)
    """
    seam_img = Image.new("L", (w, h), 0)
    plate_img = Image.new("L", (w, h), 0)
    hair_img = Image.new("L", (w, h), 0)
    pit_img = Image.new("L", (w, h), 0)
    seams, plates, hairs, pits = (ImageDraw.Draw(i) for i in
                                  (seam_img, plate_img, hair_img, pit_img))
    filled = set(cells)
    seam_paths = []

    # Cell seams: every internal cell boundary, jittered, overshooting past the
    # silhouette edge so the crack visibly cuts through the outline (each cell
    # reads as a separate stone, like the reference art).
    OVER = OUTLINE + BLEED  # the alpha mask clips whatever pokes outside
    for c, r in cells:
        x0, y0 = BLEED + c * CELL, BLEED + r * CELL
        if (c + 1, r) in filled:
            seam_paths.append(jittered(rng, (x0 + CELL, y0 - OVER),
                                       (x0 + CELL, y0 + CELL + OVER), 10, 5))
        if (c, r + 1) in filled:
            seam_paths.append(jittered(rng, (x0 - OVER, y0 + CELL),
                                       (x0 + CELL + OVER, y0 + CELL), 10, 5))
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    xs += 0.5; ys += 0.5
    distance_squared = np.full((h, w), 1e9, np.float32)
    for points in seam_paths:
        seams.line(points, fill=255, width=9, joint="curve")
        for (ax, ay), (bx, by) in zip(points, points[1:]):
            dx, dy = bx - ax, by - ay
            t = np.clip(((xs - ax) * dx + (ys - ay) * dy) / (dx * dx + dy * dy), 0, 1)
            np.minimum(distance_squared, (xs - ax - t * dx) ** 2 + (ys - ay - t * dy) ** 2,
                       out=distance_squared)

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
    return chunky, hairline, pit, np.sqrt(distance_squared)


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

    # --- luminance model: one base colour, top light, shared stone relief ------
    ys = (np.arange(h, dtype=np.float32) + 0.5)[:, None] / h
    grad = 1.13 - 0.36 * ys ** 1.15                       # light from straight above
    mottle, relief, flakes, weather_pits, wear, chips = stone_surface(w, h, sdf, shape + ":stone")
    grain = (nprng.rand(h, w).astype(np.float32) - 0.5) * 0.10  # +/-5%, never stronger
    lum = grad * (1.0 + mottle) * (1.0 + grain)
    col = base[None, None, :] * lum[..., None]
    interior = sdf < -OUTLINE
    col *= (1.0 + relief * interior)[..., None]
    col *= (1.0 - 0.16 * flakes * interior)[..., None]
    flake_lip = np.maximum(shift_down(flakes, 3) - flakes, 0)
    col *= (1.0 + 0.20 * flake_lip * interior)[..., None]

    chunky, hairline, pit, seam_distance = build_crack_layers(shape, cells, w, h, rng)

    # --- bevel: each stone has the same top-lit, 26px carved rim --------------
    # Structural joints share the outer edge's lighting, so a cell is a slab,
    # rather than a flat face divided by painted lines. Alpha still uses sdf ONLY.
    stone_sdf = np.maximum(sdf + OUTLINE, 4.5 - seam_distance)
    gy, gx = np.gradient(stone_sdf)                       # outward-facing normal
    bevel_t = np.clip((stone_sdf + BEVEL) / BEVEL, 0.0, 1.0)
    # Wear shifts the shading within the SAME 26px band, never the silhouette.
    band = np.clip(bevel_t + wear * bevel_t * (1.0 - bevel_t), 0, 1) ** 0.85
    band *= np.clip((-sdf - OUTLINE * 0.55) / (OUTLINE * 0.45), 0.0, 1.0)  # fade under outline
    topness = np.clip((-gy - 0.25) / 0.5, 0.0, 1.0)
    botness = np.clip((gy - 0.25) / 0.5, 0.0, 1.0)
    sideness = np.clip((np.abs(gx) - 0.25) / 0.5, 0.0, 1.0) * (1.0 - topness) * (1.0 - botness)
    col *= (1.0 - 0.09 * band)[..., None]                 # faint AO ring inside the outline
    hi_col = base + (1.0 - base) * 0.40                  # mineral highlight, hue kept
    k_top = (0.72 + edge_shine) * band * topness
    col = col * (1.0 - k_top[..., None]) + hi_col[None, None, :] * (grad * 1.04)[..., None] * k_top[..., None]
    col *= (1.0 - 0.26 * band * botness)[..., None]       # bottom inner shadow
    col *= (1.0 - 0.12 * band * sideness)[..., None]      # sides slightly shaded
    # Small missing bevel facets are painted INSIDE the dark seating outline.
    col *= (1.0 - 0.30 * chips * interior)[..., None]
    chip_lip = np.maximum(shift_down(chips, 3) - chips, 0)
    col *= (1.0 + 0.20 * chip_lip * interior)[..., None]

    # --- cracks (carved: dark core, lit lower lip, shadowed upper lip) --------
    joint_ao = np.clip(1.0 - seam_distance / 16.0, 0, 1)
    col *= (1.0 - 0.22 * joint_ao * interior)[..., None]
    crack = np.maximum(chunky, hairline * 0.55)
    body = sdf < -OUTLINE * 0.35                          # cracks may cut the outline
    col *= (1.0 - 0.55 * crack * body)[..., None]
    lip_lo = shift_down(chunky, 5) * np.clip((0.38 - crack) / 0.38, 0.0, 1.0)
    lip_hi = shift_down(chunky, -4) * np.clip((0.38 - crack) / 0.38, 0.0, 1.0)
    col *= (1.0 + 0.30 * lip_lo * interior)[..., None]    # light catches below the crack
    col *= (1.0 - 0.13 * lip_hi * interior)[..., None]    # shadow above it
    pit = np.maximum(pit, weather_pits)
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
    # Existing Unity metas belong to the importer. Replace complete PNGs only,
    # so an editor refresh never reads a half-written texture.
    temporary = out + ".tmp"
    Image.fromarray(rgba).save(temporary, format="PNG")
    os.replace(temporary, out)
    print(f"{out}  ({w}x{h})")
    return Image.fromarray(rgba)


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
