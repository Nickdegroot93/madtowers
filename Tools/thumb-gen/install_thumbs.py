#!/usr/bin/env python3
"""Install generated thumbnails from gen_raw/ into Assets/Art/Chapters/<Folder>/ as
<stem>-N.jpg via the standard thumbnail compression (800px long edge, JPG q80).
Overwrites in place - existing metas (and their GUIDs / level wiring) survive."""
import pathlib, subprocess

ROOT = pathlib.Path(__file__).resolve().parents[2]
RAW = pathlib.Path(__file__).parent / "gen_raw"
SCRIPT = ROOT / "Tools/compress_chapter_image.sh"

# slug -> (chapter art folder, file stem)
INSTALL = {
    "sakura-ridge":    ("SakuraRidge", "sakura-ridge"),
    "barren-lands":    ("BarrenLands", "barren-lands"),
    "jungle":          ("JungleDepths", "jungle"),
    "frozen-peaks":    ("FrozenPeaks", "frozen-peaks"),
    "fangkuai":        ("FangkuaiDistrict", "fangkuai-district"),
    "kvartal-4":       ("Kvartal4", "kvartal-4"),
    "neon-nightfall":  ("NeonNightfall", "neon-nightfall"),
    "burning-steppes": ("BurningSteppes", "burning-steppes"),
    "giza-dusk":       ("GizaDusk", "giza-dusk"),
    "lost-city":       ("LostCity", "lost-city"),
    "sector-isla":     ("SectorIsla", "sector-isla"),
    "hallows-end":     ("HallowsEnd", "hallows-end"),
    "amber-tide":      ("AmberTide", "amber-tide"),
}

if __name__ == "__main__":
    missing = []
    for slug, (folder, stem) in INSTALL.items():
        for i in range(1, 6):
            src = RAW / f"{slug}-{i}.png"
            if not src.exists():
                missing.append(src.name)
                continue
            dst = ROOT / f"Assets/Art/Chapters/{folder}/{stem}-{i}.jpg"
            subprocess.run([str(SCRIPT), "thumbnail", str(src), str(dst)], check=True)
    if missing:
        print(f"MISSING {len(missing)}: {missing}")
    else:
        print("all 65 installed")
