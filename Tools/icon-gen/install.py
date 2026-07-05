#!/usr/bin/env python3
"""Install processed icons into Assets/Art/Abilities. Existing icons keep their .meta
(same GUID → references intact); new icons get a fresh .meta written atomically alongside
the PNG (never a PNG without its .meta — Unity's refresh races otherwise).
Prints NAME=GUID lines for the new icons so the editor pass can wire the asset refs."""
import json, pathlib, shutil, uuid

ROOT = pathlib.Path(__file__).parent
PROCESSED = ROOT / "processed"
DEST = pathlib.Path("/Users/Nick/MadTowers/Assets/Art/Abilities")
TEMPLATE = (DEST / "icon_freeze.png.meta").read_text()

manifest = json.loads((ROOT / "manifest.json").read_text())
new_guids = {}
for entry in manifest["abilities"]:
    src = PROCESSED / f"{entry['icon']}.png"
    if not src.exists():
        print(f"SKIP {entry['name']} (not processed)")
        continue
    png = DEST / f"{entry['icon']}.png"
    meta = DEST / f"{entry['icon']}.png.meta"
    if not meta.exists():
        guid = uuid.uuid4().hex
        old_guid = "f1cd658dabe2416e8d0388f5a1f81b83"
        old_sprite_id = "3743ecf41d1e42419cec40db41b9a560"
        text = TEMPLATE.replace(old_guid, guid).replace(old_sprite_id, uuid.uuid4().hex)
        shutil.copy(src, png)
        meta.write_text(text)
        new_guids[entry["name"]] = guid
    else:
        shutil.copy(src, png)

print(f"installed {len(manifest['abilities'])} icons, {len(new_guids)} new")
for name, guid in new_guids.items():
    print(f"NEW {name}={guid}")
