# Data Conventions

> The full designer manual — every level dial, recipes, and the modifier idea backlog —
> is [LEVELS.md](../../LEVELS.md) at the repo root.

All game *content* lives here as ScriptableObject assets. Scripts define what a thing CAN do;
these assets define which ones EXIST and with what values. Moving assets between folders is
always safe (Unity references by GUID via the .meta files — just move the .meta along with
the file, which the editor does automatically).

## Blocks/ — brick variants
One `.asset` per brick variant (Normal, Heavy, Anchor, ...). Most new variants need **no new
script**: create a Block Variant asset (right-click > Create > Stacking > Blocks) and set its
stats — mass, friction material, tint, control quirks. Only when a brick
needs new *behaviour* (like Anchor freezing in place) does it get a small subclass in
`Scripts/Blocks/Variants/` overriding a lifecycle hook.

Variants reach the game in two ways:
- as a mode's `fallbackBlockDataVariants` / a `BlockDefinition`'s default data, or
- injected at runtime by a power-up (`BlockVariantChancePowerUp`).

Keep this folder flat until it genuinely outgrows that; then group by theme, not one folder
per brick.

## PowerUps/ — choice offers
Sorted by rarity folder: `Common/`, `Rare/`, `Epic/` (add `Legendary/` when the first one
exists). **The folder is organization only — the `rarity` field on the asset is what the
game reads.** Keep them matching when you move things.

To add a power-up, see the doc comment on `Scripts/PowerUps/PowerUpDefinition.cs`.
A power-up only appears in-game once it's added to a game mode's `powerUpChoicePool`.

## GameModes/ + ../Resources/GameModes/
One asset per mode (rules, physics tuning dials, camera, islands, power-up pool/cadence).
Modes used by the level-select menu live in `Resources/` (loaded by name at runtime);
`Levels/` assets pair a mode with presentation and MUST stay under `Resources/Levels/` —
that path is loaded via `Resources.LoadAll`.
