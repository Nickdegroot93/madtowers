#!/usr/bin/env python3
"""Wire installed thumbnails into every LevelDefinition's menuThumbnail: level N of a
chapter gets <stem>-N.jpg. Run AFTER install_thumbs.py + a Unity refresh (metas must
exist). Idempotent - rewrites the menuThumbnail line in place."""
import pathlib, re

ROOT = pathlib.Path(__file__).resolve().parents[2]

# level prefix -> (chapter art folder, file stem)
WIRE = {
    "SR": ("SakuraRidge", "sakura-ridge"),
    "DR": ("BarrenLands", "barren-lands"),
    "JD": ("JungleDepths", "jungle"),
    "FP": ("FrozenPeaks", "frozen-peaks"),
    "FD": ("FangkuaiDistrict", "fangkuai-district"),
    "KV": ("Kvartal4", "kvartal-4"),
    "NN": ("NeonNightfall", "neon-nightfall"),
    "BS": ("BurningSteppes", "burning-steppes"),
    "GD": ("GizaDusk", "giza-dusk"),
    "LO": ("LostCity", "lost-city"),
    "SI": ("SectorIsla", "sector-isla"),
    "HE": ("HallowsEnd", "hallows-end"),
    "AT": ("AmberTide", "amber-tide"),
}

if __name__ == "__main__":
    wired = 0
    for level in sorted((ROOT / "Assets/Resources/Levels").glob("Level_*.asset")):
        m = re.match(r"Level_([A-Z]{2})(\d)_", level.name)
        if not m:
            print(f"skip {level.name} (no prefix)")
            continue
        prefix, idx = m.group(1), int(m.group(2))
        folder, stem = WIRE[prefix]
        meta = ROOT / f"Assets/Art/Chapters/{folder}/{stem}-{idx}.jpg.meta"
        guid = re.search(r"guid: ([0-9a-f]+)", meta.read_text()).group(1)
        txt = level.read_text()
        new = re.sub(r"menuThumbnail: \{fileID: [^}]*\}",
                     f"menuThumbnail: {{fileID: 21300000, guid: {guid}, type: 3}}", txt)
        if new != txt:
            level.write_text(new)
            wired += 1
    print(f"wired {wired} levels")
