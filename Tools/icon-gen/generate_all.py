#!/usr/bin/env python3
"""Generate all MadTowers ability icons via Higgsfield openai/hazel. Idempotent: skips
already-downloaded files, so a rerun only does the missing/failed ones."""
import json, os, pathlib, shutil, time, urllib.request
from concurrent.futures import ThreadPoolExecutor

ROOT = pathlib.Path(__file__).parent
RAW = ROOT / "gen_raw"
RAW.mkdir(exist_ok=True)

env = (pathlib.Path.home() / ".config/higgsfield/env").read_text()
creds = dict(line.replace("export ", "").replace('"', "").split("=", 1)
             for line in env.strip().splitlines())
AUTH = f"Key {creds['HF_API_KEY']}:{creds['HF_API_SECRET']}"
BASE = "https://platform.higgsfield.ai"

manifest = json.loads((ROOT / "manifest.json").read_text())
HUES = manifest["hues"]

TEMPLATE = ("Square mobile game ability icon for a power called {display} in a block-stacking "
            "tower game. Subject: {subject}. Style: near-black rounded-square background (#0B0E13), "
            "the subject drawn as dark charcoal slab shapes outlined in glowing {hue_label} neon "
            "light ({hue_hex}) with a subtle {hue_label} rim glow, like premium dark neon UI. "
            "Bold, minimal, high contrast, centered with generous margins. No text, no lettering, "
            "no numbers, no border ornaments.")

ALLOWED_KEYS = {"prompt", "aspect_ratio", "quality"}  # API silently bills unknown params

HEADERS = {"Authorization": AUTH, "Content-Type": "application/json",
           "User-Agent": "higgsfield-server-js/2.0"}  # Cloudflare 1010-blocks Python's default UA

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

def generate(entry):
    out = RAW / f"{entry['name']}.png"
    if out.exists():
        return f"skip {entry['name']} (exists)"
    if "reuse" in entry:
        shutil.copy(ROOT / entry["reuse"], out)
        return f"reuse {entry['name']}"
    hue = HUES[entry["hue"]]
    body = {"prompt": TEMPLATE.format(display=entry["display"], subject=entry["subject"],
                                      hue_label=hue["label"], hue_hex=hue["hex"]),
            "aspect_ratio": "1:1", "quality": "medium"}
    assert set(body) <= ALLOWED_KEYS
    for attempt in (1, 2):
        try:
            rid = api("/openai/hazel", body)["request_id"]
            for _ in range(90):
                time.sleep(5)
                st = api(f"/requests/{rid}/status")
                if st["status"] == "completed":
                    download(st["images"][0]["url"], out)
                    return f"done {entry['name']}"
                if st["status"] in ("failed", "nsfw"):
                    raise RuntimeError(f"{st['status']} (attempt {attempt})")
            raise RuntimeError(f"timeout (attempt {attempt})")
        except Exception as e:
            err = e
            time.sleep(3)
    return f"FAIL {entry['name']}: {err}"

if __name__ == "__main__":
    # Optional asset-name args restrict the run (e.g. `generate_all.py MagmaSpawn Titan`).
    # With no args it processes every manifest entry — existing gen_raw/ files are skipped,
    # but on a fresh checkout that means regenerating the WHOLE set (≈55 paid generations).
    import sys
    targets = manifest["abilities"]
    if len(sys.argv) > 1:
        names = set(sys.argv[1:])
        targets = [e for e in targets if e["name"] in names]
        unknown = names - {e["name"] for e in targets}
        if unknown:
            raise SystemExit(f"unknown ability names: {sorted(unknown)}")
    with ThreadPoolExecutor(max_workers=6) as pool:
        for msg in pool.map(generate, targets):
            print(msg, flush=True)

    got = len(list(RAW.glob("*.png")))
    print(f"TOTAL {got}/{len(manifest['abilities'])}", flush=True)
