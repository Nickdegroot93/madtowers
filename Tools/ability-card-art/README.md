# Ability-card art pipeline

The in-game "Choose an Ability" cards are built entirely in code
([AbilityChoiceController.cs](../../Assets/SourceFiles/Scripts/Abilities/AbilityChoiceController.cs),
`CreateFramedCard`) from one hand-authored frame plus four sprites **derived** from it.

## Sprites (all in `Assets/Resources/`)

| File | Source | Tinted? |
|------|--------|---------|
| `AbilityCardFrame.png` | **Hand-authored** (AI-generated, cleaned to true-transparent grayscale, 752×1344) | per rarity |
| `AbilityCardIconBacking.png` | derived: white fill of the icon recess (alpha = exact recess shape) | no (stays white) |
| `AbilityCardGem.png` | derived: the faceted gem, grayscale+alpha so it re-tints lighter | per rarity (lighter) |
| `AbilityCardGlowDot.png` | derived: standalone soft radial for the gem glow | per rarity |
| `AbilityCardRimGlow.png` | derived: thin outer halo around the card silhouette | per rarity |

The four derived sprites are produced by [`generate_card_sprites.py`](generate_card_sprites.py)
so they align to the frame's exact pixels (a procedural shape could never match the hand-drawn
bevels). Layout slot rectangles, the gem center, and the card aspect are measured against the
**752×1344** frame and live as constants in `AbilityChoiceController.cs`.

## Re-exporting the frame art

1. Replace `Assets/Resources/AbilityCardFrame.png` (keep it 752×1344, or expect to re-measure).
2. `python3 Tools/ability-card-art/generate_card_sprites.py` (needs `pillow`, `numpy`).
3. Refresh Unity to re-import.
4. If the size or panel positions changed, re-measure the slot `Rect`s / `GemCenter` in
   `AbilityChoiceController.cs` (the frame loader logs an editor warning on a size mismatch).

## Adding a NEW derived sprite

Author its `.meta` atomically alongside the PNG (copy an existing card `.meta`, swap the `guid`
and `spriteID`) before Unity sees the file — otherwise Unity's import can race and report a
"guid mismatch". Then add a lazy loader via `LoadCardSprite(...)` in the controller.
