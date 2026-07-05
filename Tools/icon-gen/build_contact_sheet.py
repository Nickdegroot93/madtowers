#!/usr/bin/env python3
"""Contact sheet of all 55 new ability icons, grouped by rarity, for Nick's review."""
import base64, json, pathlib

ROOT = pathlib.Path(__file__).parent
PROCESSED = ROOT / "processed"
manifest = json.loads((ROOT / "manifest.json").read_text())

RARITY = {}  # name -> (rarity, order)
for line in (ROOT / "abilities_authoritative.txt").read_text().strip().splitlines():
    name, display, rarity, icon, path = line.split("|")
    RARITY[name] = rarity

groups = {"Common": [], "Rare": [], "Epic": []}
for e in manifest["abilities"]:
    r = RARITY.get(e["name"], "Common")
    f = PROCESSED / f"{e['icon']}.png"
    if not f.exists(): continue
    b64 = base64.b64encode(f.read_bytes()).decode()
    groups[r].append((e["display"], e["hue"], b64))

sections = ""
for rarity in ["Common", "Rare", "Epic"]:
    cells = "".join(
        f'<figure><img src="data:image/png;base64,{b64}" alt="{d}" loading="lazy">'
        f'<figcaption>{d}</figcaption></figure>'
        for d, hue, b64 in groups[rarity])
    sections += f'<h2>{rarity} <span class="count">{len(groups[rarity])}</span></h2><div class="grid">{cells}</div>'

html = f'''<title>MadTowers — New Ability Icons (all 55)</title>
<style>
  :root {{ --bg:#0b0e13; --panel:#11151d; --edge:#1d2430; --ink:#e9edf4; --muted:#8b94a5; --cyan:#4ddbff; }}
  body {{ background:var(--bg); color:var(--ink); font:16px/1.5 system-ui,-apple-system,sans-serif;
    padding:clamp(20px,4vw,56px); max-width:1280px; margin:0 auto; }}
  .eyebrow {{ color:var(--cyan); text-transform:uppercase; letter-spacing:.18em; font-size:12px; font-weight:700; }}
  h1 {{ font-size:clamp(26px,4vw,40px); font-weight:900; margin:8px 0 8px; }}
  .lede {{ color:var(--muted); max-width:64ch; margin:0 0 12px; }}
  h2 {{ font-size:15px; font-weight:800; text-transform:uppercase; letter-spacing:.12em;
    margin:34px 0 14px; }}
  .count {{ color:var(--muted); font-weight:600; margin-left:6px; }}
  .grid {{ display:grid; grid-template-columns:repeat(auto-fill,minmax(150px,1fr)); gap:14px; }}
  figure {{ margin:0; background:var(--panel); border:1px solid var(--edge); border-radius:12px;
    padding:10px; }}
  figure img {{ width:100%; height:auto; border-radius:8px; display:block; }}
  figcaption {{ font-size:12.5px; color:var(--muted); text-align:center; padding-top:8px;
    font-weight:600; }}
</style>
<header>
  <div class="eyebrow">MadTowers · icon set v1</div>
  <h1>All 55 ability icons, neon-glow style</h1>
  <p class="lede">Generated with the locked template in five thematic accent hues.
  They are already installed in the game — open the Vault (everything is unlocked) to see them
  on real cards. Name any you dislike and those get regenerated.</p>
</header>
{sections}
'''
(ROOT / "icon-set-v1.html").write_text(html)
print("built", sum(len(v) for v in groups.values()), "icons")
