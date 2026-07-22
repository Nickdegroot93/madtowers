#!/usr/bin/env python3
"""Generate the two-state run-life hearts (SHOP.md §2) via Higgsfield openai/hazel, styled
to sit next to the beveled-gold coin icon: FULL = faceted ruby gem heart, EMPTY = dark
hollow socket in the same silhouette. Then key out the solid magenta background and write
Assets/Resources/Menu/heart_full.png + heart_empty.png (HeartSprites picks them up with no
code change; delete the legacy Menu/heart.png once these land).

Usage:  python3 generate_hearts.py            # generate candidates (2 per state, PAID)
        python3 generate_hearts.py --process  # key + install the chosen candidates
Pick candidates by renaming the chosen raw files to heart_full.png / heart_empty.png in
hearts_raw/ before --process. Requires a valid ~/.config/higgsfield/env key.
"""
import json, pathlib, sys, time, urllib.request

ROOT = pathlib.Path(__file__).parent
RAW = ROOT / "hearts_raw"
RAW.mkdir(exist_ok=True)
DEST = ROOT.parent.parent / "Assets/Resources/Menu"

env = (pathlib.Path.home() / ".config/higgsfield/env").read_text()
creds = dict(line.replace("export ", "").replace('"', "").split("=", 1)
             for line in env.strip().splitlines())
HEADERS = {"Authorization": f"Key {creds['HF_API_KEY']}:{creds['HF_API_SECRET']}",
           "Content-Type": "application/json", "User-Agent": "higgsfield-server-js/2.0"}
BASE = "https://platform.higgsfield.ai"

STYLE = ("premium casual mobile game UI icon, chunky 3D beveled style exactly like a beveled "
         "faceted gold coin game icon: strong polished bevel facets, crisp geometric rim, "
         "saturated material, straight-on flat view, perfectly centered with generous margins, "
         "isolated on a solid pure magenta background (#FF00FF), no text, no shadows outside "
         "the shape, no glow outside the shape")

PROMPTS = {
    "full_a": ("Heart icon: a faceted ruby-red gemstone heart with a chunky beveled "
               "gold rim frame, rich deep red gem facets catching light, " + STYLE),
    "full_b": ("Heart icon: a chunky beveled heart of polished deep-red faceted ruby, "
               "geometric flat gem facets, thin darker red outline rim, no gold, " + STYLE),
    "empty_a": ("Empty heart slot icon: the same heart shape as a dark hollow SOCKET - a "
                "recessed empty heart-shaped cavity carved into dark charcoal stone with a "
                "thin dull gold beveled rim, unlit, clearly an empty slot awaiting a gem, " + STYLE),
    "empty_b": ("Empty heart slot icon: hollow dark heart-shaped recess, dark smoky glass, "
                "thin dark-red beveled rim, unlit and clearly empty, " + STYLE),
}

ALLOWED_KEYS = {"prompt", "aspect_ratio", "quality"}  # API silently bills unknown params


def api(path, body=None):
    req = urllib.request.Request(BASE + path,
        data=json.dumps(body).encode() if body is not None else None,
        headers=HEADERS, method="POST" if body is not None else "GET")
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.loads(r.read())


def gen(name, prompt):
    out = RAW / f"{name}.png"
    if out.exists():
        return f"skip {name}"
    body = {"prompt": prompt, "aspect_ratio": "1:1", "quality": "medium"}
    assert set(body) <= ALLOWED_KEYS
    rid = api("/openai/hazel", body)["request_id"]
    for _ in range(90):
        time.sleep(5)
        st = api(f"/requests/{rid}/status")
        if st["status"] == "completed":
            data = urllib.request.urlopen(urllib.request.Request(
                st["images"][0]["url"], headers={"User-Agent": HEADERS["User-Agent"]}), timeout=120).read()
            out.write_bytes(data)
            return f"done {name}"
        if st["status"] in ("failed", "nsfw"):
            return f"FAIL {name}: {st['status']}"
    return f"FAIL {name}: timeout"


def key_magenta(src, dst, size=256):
    """Alpha out the magenta backdrop with edge decontamination, crop to content, resize."""
    from PIL import Image
    im = Image.open(src).convert("RGBA")
    px = im.load()
    w, h = im.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            # Magenta-ness: high R+B, low G. Soft threshold keeps AA edges.
            mag = min(r, b) - g
            if mag > 90:
                px[x, y] = (r, g, b, 0)
            elif mag > 20:
                keep = 1.0 - (mag - 20) / 70.0
                # Decontaminate: pull the magenta cast out of the kept fringe.
                px[x, y] = (int(r - (1 - keep) * 60), g, int(b - (1 - keep) * 60), int(a * keep))
    bbox = im.getbbox()
    im = im.crop(bbox)
    side = max(im.size)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(im, ((side - im.width) // 2, (side - im.height) // 2))
    canvas.resize((size, size), Image.LANCZOS).save(dst)


if __name__ == "__main__":
    if "--process" in sys.argv:
        for state in ("full", "empty"):
            src = RAW / f"heart_{state}.png"
            if not src.exists():
                print(f"pick a candidate first: rename hearts_raw/{state}_a|b.png "
                      f"to hearts_raw/heart_{state}.png")
                continue
            dst = DEST / f"heart_{state}.png"
            key_magenta(src, dst)
            print(f"installed {dst}")
    else:
        from concurrent.futures import ThreadPoolExecutor
        with ThreadPoolExecutor(4) as pool:
            for result in pool.map(lambda kv: gen(*kv), PROMPTS.items()):
                print(result)
