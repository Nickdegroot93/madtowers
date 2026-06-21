# MadTowers Image Pipeline

How raw images Nick exports become in-game art: where each kind lives, what
format/size to use, how it's imported, and how it's rendered. Companion to
[ART.md](ART.md) (authoring spec — silhouettes, palettes, house style); when the
two disagree on **format**, this file wins (it documents the photographic-JPG
exception added later).

The menu UI is built entirely in code (`MainMenuRuntime`). Only two art hooks are
wired through the Inspector — `ChapterDefinition.menuBackgroundImage` and
`LevelDefinition.menuThumbnail`; everything else is loaded by path or generated.

---

## 1. Folder layout (one folder per chapter)

```
Assets/Art/Chapters/<ChapterName>/        e.g. JungleDepths/, BarrenLands/
    <chapter>.jpg                          full-screen menu background
    <chapter>-1.jpg .. <chapter>-N.jpg     one thumbnail per level, in order

Assets/Resources/Menu/                     HUD/currency icons, loaded by path
    coin.png   heart.png   ...
```

Names are only for humans — the background and thumbnails are referenced by GUID
(assigned in the Inspector), not by filename, so there's no magic-string coupling.
Keep the `<chapter>` / `<chapter>-N` convention for tidiness.

---

## 2. Auto-import (no manual setup, no hand-authored `.meta`)

`Assets/Editor/MenuArtImportSettings.cs` (an `AssetPostprocessor`, sibling of
`BlockSkinImportSettings`) configures **anything** dropped under
`Art/Chapters/**` or `Resources/Menu/**` as a UI sprite: `Sprite` type, Single,
Full-Rect mesh, centre pivot, `alphaIsTransparency`, no mipmaps. So importing new
art is a pure file-drop — Unity assigns the GUID and the sprite settings on its
own. Do **not** hand-author `.meta` files for these folders.

(Block/ground art has its own importer for `Resources/Skins/**`; ability icons in
`Art/Abilities/**` are generated pre-configured. Don't widen this importer to
those — they're handled.)

---

## 3. Format & compression policy

| Art has transparency? | Examples | Format |
|---|---|---|
| **Yes** — alpha matters | menu icons, ability icons, block/ground sprites | **PNG** |
| **No** — opaque photo/render | level thumbnails, chapter backgrounds | **JPG** (≈10–20× smaller) |

PNG is lossless with no DCT, so a full-colour render is ~1.3–2 MB as PNG vs
~70–170 KB as JPG — same pixels. JPG has no alpha, so it's opaque art only.
Then downscale the **long edge** to the largest on-screen use. One tool does both:

```sh
Tools/compress_chapter_image.sh background "src.png" "Assets/Art/Chapters/<Chapter>/<chapter>.jpg"   # <=1440px, q82
Tools/compress_chapter_image.sh thumbnail  "src.png" "Assets/Art/Chapters/<Chapter>/<chapter>-1.jpg" # <=800px,  q80
```

It only downscales (never upscales past the source). Measured: backgrounds
~1.3–1.6 MB → ~120–170 KB; thumbnails ~1.0–2.0 MB → ~60–120 KB.

---

## 4. Chapter background images

- **Where:** `Assets/Art/Chapters/<Chapter>/<chapter>.jpg`.
- **Reference:** `ChapterDefinition.menuBackgroundImage` (read as
  `chapter.MenuBackgroundImage`). A chapter may instead set `menuBackgroundVideo`.
- **Shape:** portrait, full-screen. Drawn `Stretch`ed to cover the screen on a
  swipeable track, so author near the phone aspect (~3:4, e.g. 752×1344) — extreme
  off-aspect art distorts.
- It's also the source for the **frosted glass** behind the cards (a blurred,
  screen-locked copy) and for the **next-chapter card** preview.

Parallax *gameplay* backdrops are a separate system — see [BACKDROPS.md](BACKDROPS.md).

---

## 5. Per-level thumbnails

- **Where:** `Assets/Art/Chapters/<Chapter>/<chapter>-N.jpg`, one per level.
- **Reference:** `LevelDefinition.menuThumbnail`. Empty → a generated abstract
  placeholder (`MenuSprites.LevelThumbnail`), so the menu never looks broken.
- **Rendered:** `RuntimeUiKit.CreateCoverImage` — CSS `object-fit: cover`: scaled
  to fill and clipped by a **rounded `Mask`** (so corners match the card radius)
  in both the card slot (132×152) and the detail modal (760×440). Author with the
  focal point centred — cover-cropping eats the edges.

---

## 6. Menu / currency icons (transparent → cropped)

- **Where:** `Assets/Resources/Menu/<name>.png` (transparent PNG).
- **Loaded by path:** `MainMenuRuntime.MenuIcon("<name>")` → cached
  `Resources.Load<Sprite>("Menu/<name>")`. (This is why they live in `Resources/`,
  unlike thumbnails/backgrounds which are direct references.)
- Raw exports often have a wide transparent margin; trim it so the emblem fills
  its slot:

```sh
# --square pads the tight crop back to a centred square so a round emblem keeps shape
python3 Tools/crop_transparent_border.py --square ~/Downloads/coin.png Assets/Resources/Menu/coin.png
```

Drawn on a uGUI `Image`, `color = white`, `preserveAspect = true`. Ability icons
(`Art/Abilities/icon_*.png`, `AbilityDefinition.icon`) are generated pre-margined
by `Tools/generate_ability_icons.py` and need no cropping.

---

## 7. Recipes

**Add a level to a chapter**
1. Export the thumbnail (any size; opaque).
2. `Tools/compress_chapter_image.sh thumbnail "src.png" "Assets/Art/Chapters/<Chapter>/<chapter>-N.jpg"`.
3. In the level's `LevelDefinition`, set **Menu Thumbnail** to that sprite.
   (Auto-imported as a sprite — no other setup.)

**Add a new chapter**
1. Make the folder `Assets/Art/Chapters/<ChapterName>/`.
2. Compress the background → `<chapter>.jpg` and each level thumbnail →
   `<chapter>-N.jpg` (tool above).
3. Create the `ChapterDefinition` (`Assets/Resources/Chapters/`) and its
   `LevelDefinition`s (`Assets/Resources/Levels/`); set `menuBackgroundImage` and
   each `menuThumbnail`. The frosted glass, next-chapter preview, swipe paging and
   level cards all pick it up automatically — no code changes.

See [LEVELS.md](LEVELS.md) for the full level/chapter data model.
