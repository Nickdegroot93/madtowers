# ICONS.md — Ability Icon Generation (SUPERSEDED 2026-08-29)

> **The neon icon set this file describes was RETIRED on 2026-08-29.** Nick replaced all 52
> ability icons with a new hand-supplied painterly set (the medal-cube family; delivered as
> 256px PNGs, overwritten in place in `Assets/Art/Abilities/` so every asset GUID survived).
> Everything below documents the OLD v1 recipe and its `Tools/icon-gen/` pipeline — do NOT
> use it to author or "fix" icons in the current style. `ABILITY_CATALOG.md` (all 52
> abilities + descriptions) plus the brick renders at `Assets/Resources/VaultPosters/` are
> the reference material the new set was designed from. A new-icon workflow for future
> abilities is TBD with Nick (until then: ask him for the art).

How every ability icon in `Assets/Art/Abilities/` was created, and how to create new ones that
look identical. Follow this exactly when adding abilities — the set only reads as one coherent
style because every image came out of the same locked recipe. The runnable pipeline lives in
`Tools/icon-gen/`; this file is the contract behind it.

The current icon set (v1, July 2026) was generated in one batch of 55 from
`Tools/icon-gen/manifest.json`; it now holds **53** — Cube Supply and Spike Supply were removed
as abilities on 2026-07-29 (ABILITIES.md), and their icons and manifest entries went with them.

## Model and API

- **Service**: Higgsfield platform API, base URL `https://platform.higgsfield.ai`.
- **Model**: `openai/hazel` — Higgsfield's hosted alias for OpenAI's GPT-Image model.
- **Settings**: `aspect_ratio: "1:1"`, `quality: "medium"` (the whole v1 set is medium; don't mix
  quality tiers — "high" renders noticeably finer detail and would stand out).
- **Auth**: `Authorization: Key $HF_API_KEY:$HF_API_SECRET` — credentials in `~/.config/higgsfield/env`
  (Nick's Higgsfield account; not in the repo).
- Raw output is a 1024×1024 PNG behind a CloudFront URL, returned by polling
  `GET /requests/{request_id}/status` until `completed`.

### API traps (both cost real money or silent failure)

1. **Unknown body params are silently ignored and the job is billed anyway.** Only send
   `prompt`, `aspect_ratio`, `quality`. A typo'd parameter = a wasted credit, not an error.
2. **Cloudflare 403s Python's default urllib User-Agent** (error 1010). Send any other UA;
   the pipeline uses `higgsfield-server-js/2.0`. curl works out of the box.

## The locked prompt template

Only two things vary per icon: the **subject clause** and the **hue**. Everything else is verbatim:

```
Square mobile game ability icon for a power called {DISPLAY_NAME} in a block-stacking
tower game. Subject: {SUBJECT}. Style: near-black rounded-square background (#0B0E13),
the subject drawn as dark charcoal slab shapes outlined in glowing {HUE_LABEL} neon
light ({HUE_HEX}) with a subtle {HUE_LABEL} rim glow, like premium dark neon UI.
Bold, minimal, high contrast, centered with generous margins. No text, no lettering,
no numbers, no border ornaments. IMPORTANT: the entire square canvas background must
be solid near-black (#0B0E13) edge to edge - never grey, never white, no outer frame,
no white margin, no drop shadow; the neon artwork glows against pure darkness that
fills the whole image.
```

The final `IMPORTANT:` clause was added after the first batch: without it, ~30% of generations
drift to a grey ground or a white outer frame. With it, all 17 drifters passed QC on the first
retry. Keep it.

### Writing a subject clause

One sentence, concrete and visual, using the game's vocabulary. Look at
`Tools/icon-gen/manifest.json` for all 53 examples. Rules of thumb:

- Describe **blocks, bricks, towers, stacks** — the icon should read as this game, not generic fantasy.
- Name one clear focal action or object ("a molten glowing brick pouring streams of lava that
  fill the empty gaps in the blocks beneath it"), plus at most one secondary accent
  ("with one large snowflake accent").
- Physical metaphors beat abstractions: a padlock springing open, a parachute, a muzzled maw —
  not "a symbol of reduction".
- Never ask for text, numbers, arrows-with-labels, or UI chrome (the template bans them, but
  don't fight it in the subject either).

### The hue palette (pick one per ability, by theme)

| Hue | Label in prompt | Hex | Used for |
|---|---|---|---|
| cyan | `electric cyan` | `#4DDBFF` | ice, time, slowing, wind, lasers-as-safety (Freeze, Slo-Mo, Updraft, Hardline, Anchor…) |
| amber | `molten amber` | `#FFAA33` | heat, weight, friction, supply/industrial (Magma, Titan, Bedrock, Locksmith…) |
| violet | `neon violet` | `#B067FF` | queue tricks, teleport/space magic (Flip, Overdraw, Fission, Suspension…) |
| green | `acid green` | `#58F58B` | vines, cleansing, protection, guides (Overgrowth, Sanitize, Ward, Guardian, Vector Guide…) |
| red | `hot red` | `#FF5566` | lives, destruction, bombs, lasers-as-danger (Extra Life, Scrap, Sacrifice, Zap, Iron Will…) |

Do not invent a sixth hue; pick the closest theme. Do not put hue words in the subject clause
that contradict the chosen hue.

## Post-processing (mandatory, or the icon won't match in-game)

`Tools/icon-gen/postprocess.py` (v2 — read its docstring; the v1 version had a bug that
replaced the alpha channel and exposed ghost tile borders in-game):

Most raw generations come back as **stickers**: opaque artwork + glow on a *transparent* field,
often with a faint low-alpha rounded-square "tile border" the model sketched from the template's
"rounded-square background" phrase. The pipeline per icon:

1. **Ghost removal** — keep low-alpha pixels only near solid artwork (dilated alpha-core mask);
   isolated low-alpha border ridges and dust vanish. Never skip this: the ghost borders are
   invisible in raw previews on dark backgrounds but render as a "line around the icon" in-game.
   Rare case: the model draws the border at *full* opacity (v1 set: only Rebound) — ghost removal
   can't touch it. Fix: crop the raw ~3.5% inside its solid-alpha bbox (border encloses
   everything, so the bbox edge *is* the border), then re-run the pipeline.
2. **Subject normalization** — square crop window centered on the artwork so its max dimension
   spans **76%** of the canvas. This is what makes all icons read the same size on the cards.
3. **Composite onto #0B0E13** (the exact `IconTile` ground), downscale to **512×512**, bake a
   **rounded-corner alpha mask, radius 12%** (supersampled for smooth AA).

The rounded corners matter: icons render on `AbilityCardView`'s rounded `IconTile` and inside
the circular HUD bubble; square corners poke out. The card's icon tile is **near-black
(#0B0E13, matching the icons' composited ground)** — it was white before this set, changed in
`AbilityCardView.AddIconTile` (glyph inset 4%). If an icon ever looks like a dark square on a
white plate, that regressed.

## Quality control

`Tools/icon-gen/qc.py` classifies each raw image by mean luminance of the four 6% corner patches
(< 0.15 PASS, > 0.5 FRAME, otherwise GREY). Regenerate FRAME/GREY via `regen.py` — expect roughly
a third of a batch to need one retry. Caveat: qc.py reads raw RGB and can't see transparency, so
it misses sticker-mode quirks — the postprocess ghost-removal handles those. Always eyeball the
**processed** output too (QC catches background drift, not a bad composition, an off-brief
subject, or a leftover frame line).

## Adding new ability icons, end to end

1. Add an entry to `Tools/icon-gen/manifest.json`: `name` (the ability's **asset name**, e.g.
   `MagmaSpawn`), `display`, `icon` (target filename stem, `icon_<snake_case>`), `hue`, `subject`.
2. `python3 Tools/icon-gen/generate_all.py` — idempotent; only generates entries whose
   `gen_raw/<Name>.png` doesn't exist yet.
3. `python3 Tools/icon-gen/qc.py` — regenerate failures via `regen.py <Name> ...` until all PASS.
4. `python3 Tools/icon-gen/postprocess.py` then `python3 Tools/icon-gen/install.py` — installs
   PNG into `Assets/Art/Abilities/`; for a new icon it writes a fresh `.meta` **atomically
   alongside the PNG** (never let Unity refresh see a hand-authored PNG without its `.meta` —
   see the guid-mismatch incident in Unity lore). Existing icons keep their GUID.
5. In Unity: refresh, then assign the sprite to the ability asset's `icon` field (the batch run
   did this via `SerializedObject` on `FindProperty("icon")`).
6. Regenerating an **existing** icon is just: delete `gen_raw/<Name>.png`, tweak the subject in
   the manifest if desired, rerun steps 2–4 — the GUID and asset reference survive because the
   file is replaced in place.

Notes on running the scripts:

- They read/write `gen_raw/` and `processed/` relative to `Tools/icon-gen/` itself.
- `generate_all.py` accepts ability asset names as args to restrict the run
  (`python3 generate_all.py MagmaSpawn Titan`). **With no args it processes the whole manifest**
  — harmless when `gen_raw/` is populated (existing files are skipped), but on a fresh checkout
  that regenerates all ~55 icons at real credit cost. Raw generations are deliberately not
  committed; the installed PNGs in `Assets/Art/Abilities/` are the product.
- Freeze's approved anchor image is committed at `Tools/icon-gen/reference/freeze_neon.png`
  (the manifest `reuse` entry points at it), so a full-set rerun keeps the anchor bit-identical.

## Known set-level quirks (v1, reprocessed v2 July 2026)

- The first shipped pass had baked-in tile borders and undersized subjects on ~16 icons
  (Nick's review caught it). Root cause was the postprocess alpha bug above, not the prompts —
  the same raws reprocessed through v2 fixed all of them without regeneration.

- **Freeze** (`icon_freeze.png`) is the style anchor — the original approved audition image,
  generated *without* the hardened background clause. Any future full-set regeneration should
  still match Freeze, not the other way around.
- Composition varies between pure neon outline (Domino, Air Brake) and filled slab shapes
  (Extra Life, Magma). Both are inside the style envelope; don't chase uniformity there.
- Icons are **medium** quality on purpose. If a future set upgrade to `high` is wanted, regenerate
  *everything* in one batch, not incrementally.
