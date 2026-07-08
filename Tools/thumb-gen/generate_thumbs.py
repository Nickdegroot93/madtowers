#!/usr/bin/env python3
"""Generate MadTowers level thumbnails via Higgsfield openai/hazel (see ICONS.md's sister
pipeline Tools/icon-gen). Style anchor: Nick's hand-made Barren Lands / Jungle Depths
thumbnails - painterly storybook vignettes of stacked tetromino towers in chapter scenery.

Idempotent: skips already-downloaded files in gen_raw/. One optional arg pair restricts
the run: `generate_thumbs.py <chapter-slug> [index]` (e.g. `barren-lands 2`).
Install into Assets/ happens separately via install_thumbs.py.
"""
import json, pathlib, shutil, sys, time, urllib.request
from concurrent.futures import ThreadPoolExecutor

ROOT = pathlib.Path(__file__).parent
RAW = ROOT / "gen_raw"
RAW.mkdir(exist_ok=True)

env = (pathlib.Path.home() / ".config/higgsfield/env").read_text()
creds = dict(line.replace("export ", "").replace('"', "").split("=", 1)
             for line in env.strip().splitlines())
AUTH = f"Key {creds['HF_API_KEY']}:{creds['HF_API_SECRET']}"
BASE = "https://platform.higgsfield.ai"
HEADERS = {"Authorization": AUTH, "Content-Type": "application/json",
           "User-Agent": "higgsfield-server-js/2.0"}  # Cloudflare 1010-blocks Python's default UA
ALLOWED_KEYS = {"prompt", "aspect_ratio", "quality"}  # API silently bills unknown params

TEMPLATE = (
    "Vertical mobile game level thumbnail, storybook illustration for a block-stacking "
    "tower game. {composition}, set in {scenery}. Painterly textured flat-color style "
    "with soft gradients and gentle paper grain, {palette}. The blocks are chunky rounded "
    "stone squares with subtle cracks and bevels, colored {blocks}. Soft atmospheric "
    "lighting, cozy and playful adventure mood. No characters, no text, no lettering, "
    "no numbers, no UI, no frames or borders.")

# The 5 compositions repeat across chapters so the whole set reads as one series.
COMPOSITIONS = [
    "A small tidy stack of a few tetromino blocks resting on the ground",
    "A tall slightly wobbly tower of tetromino blocks reaching up toward the sky",
    "A tower of tetromino blocks balanced across two stone pillars over a gap",
    "Tetromino blocks stacked on a small floating stone island in the air",
    "A grand towering stack of tetromino blocks seen from below, its top in the clouds",
]

CHAPTERS = {
    "sakura-ridge": {
        "scenery": "a serene Japanese mountain ridge with cherry blossom trees, a distant "
                   "snow-capped Fuji and a red torii gate",
        "palette": "muted washi tones of soft pink sakura, pale sky, temple indigo accents",
        "blocks": "patinated teal, washi gold, wisteria purple and torii vermilion",
    },
    "barren-lands": {
        "scenery": "a sun-baked desert with layered mesas, saguaro cacti and drifting haze",
        "palette": "warm golden sand, terracotta and burnt orange under a pale hot sky",
        "blocks": "golden sand, terracotta, slate and bleached bone",
    },
    "jungle": {
        "scenery": "a deep misty jungle with mossy ancient ruins and hanging vines",
        "palette": "layered deep greens with shafts of soft jungle light",
        "blocks": "mossy green, fern, ripe papaya orange and river-stone teal",
    },
    "frozen-peaks": {
        "scenery": "high snowy alpine peaks with frosted pine trees and drifting snow",
        "palette": "pale glacial blues and snow whites with a touch of alpenglow",
        "blocks": "glacial ice blue, pale winter gold, frosted pine green and granite",
    },
    "fangkuai": {
        "scenery": "a Chinese hillside district at dusk with glowing paper lanterns and "
                   "tiled pagoda rooftops",
        "palette": "dusk aubergine and mauve with warm lantern-pink glow",
        "blocks": "lantern gold, jade green, cinnabar red and indigo",
    },
    "kvartal-4": {
        "scenery": "a snowy sovietwave courtyard at night between tall panel apartment "
                   "buildings with warm lit windows",
        "palette": "cold teal-green night tones with sodium-lamp amber glow on snow",
        "blocks": "lamplit snow grey, sodium amber, faded brick red and panel concrete blue",
    },
    "neon-nightfall": {
        "scenery": "a neon-lit night city waterfront with glowing skyscraper reflections",
        "palette": "deep night violet with electric cyan and hot magenta neon light",
        "blocks": "electric cyan, amber signage, hot magenta and ultraviolet blue",
    },
    "burning-steppes": {
        "scenery": "a volcanic ash plain with a smoking volcano and drifting embers",
        "palette": "warm ash greys and deep maroon under an ember-lit sky",
        "blocks": "warm ash grey, ember gold, lava red and molten orange",
    },
    "giza-dusk": {
        "scenery": "a futuristic Egyptian desert at dusk with pyramids and glowing "
                   "hieroglyph monuments",
        "palette": "dusky sandstone and gold with deep violet evening sky",
        "blocks": "limestone, pharaoh gold, lapis blue and carnelian red",
    },
    "lost-city": {
        "scenery": "alien desert ruins under a giant rising moon on a distant planet",
        "palette": "moonlit teals and dusky violet with warm ember accents",
        "blocks": "moonlit teal, moon gold, alien violet and rust orange",
    },
    "sector-isla": {
        "scenery": "a secret tropical island lagoon at green dusk with palm trees and "
                   "a distant ferris wheel",
        "palette": "lagoon teals and jungle greens with dusk gold highlights",
        "blocks": "lagoon aqua, dusk-gold sand, palm green and hibiscus red",
    },
    "hallows-end": {
        "scenery": "a Halloween graveyard at blood-red dusk with glowing jack-o'-lanterns, "
                   "crooked fences and a red eclipse moon",
        "palette": "deep blood-red and plum dusk with warm pumpkin glow",
        "blocks": "pumpkin orange, candlelight amber, witch violet and spectral teal",
    },
    "amber-tide": {
        "scenery": "a tropical sunset coast with palm jungle hills and a huge pale setting sun",
        "palette": "pink-amber sunset tones from pale rose to deep magenta",
        "blocks": "shell pink, sun amber, bougainvillea magenta and mango orange",
    },
}

def api(path, body=None):
    req = urllib.request.Request(BASE + path,
        data=json.dumps(body).encode() if body is not None else None,
        headers=HEADERS, method="POST" if body is not None else "GET")
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.loads(r.read())

def download(url, out):
    req = urllib.request.Request(url, headers={"User-Agent": HEADERS["User-Agent"]})
    with urllib.request.urlopen(req, timeout=120) as r:
        out.write_bytes(r.read())

def generate(job):
    slug, idx = job
    out = RAW / f"{slug}-{idx}.png"
    if out.exists():
        return f"skip {out.name} (exists)"
    c = CHAPTERS[slug]
    body = {"prompt": TEMPLATE.format(composition=COMPOSITIONS[idx - 1], **c),
            "aspect_ratio": "2:3", "quality": "medium"}
    assert set(body) <= ALLOWED_KEYS
    err = None
    for attempt in (1, 2):
        try:
            rid = api("/openai/hazel", body)["request_id"]
            for _ in range(90):
                time.sleep(5)
                st = api(f"/requests/{rid}/status")
                if st["status"] == "completed":
                    download(st["images"][0]["url"], out)
                    return f"done {out.name}"
                if st["status"] in ("failed", "nsfw"):
                    raise RuntimeError(f"{st['status']} (attempt {attempt})")
            raise RuntimeError(f"timeout (attempt {attempt})")
        except Exception as e:
            err = e
            time.sleep(3)
    return f"FAIL {out.name}: {err}"

if __name__ == "__main__":
    jobs = [(slug, i) for slug in CHAPTERS for i in range(1, 6)]
    if len(sys.argv) > 1:
        slug = sys.argv[1]
        if slug not in CHAPTERS:
            raise SystemExit(f"unknown chapter slug: {slug} (have: {', '.join(CHAPTERS)})")
        idxs = [int(sys.argv[2])] if len(sys.argv) > 2 else range(1, 6)
        jobs = [(slug, i) for i in idxs]
    with ThreadPoolExecutor(max_workers=6) as pool:
        for msg in pool.map(generate, jobs):
            print(msg, flush=True)
    print(f"TOTAL {len(list(RAW.glob('*.png')))}/{5 * len(CHAPTERS)}", flush=True)
