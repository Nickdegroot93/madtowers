# Abilities — follow-up tracker

Living checklist of unfinished work on ability **assets** (the abilities themselves are
functional; this tracks polish + content-gating debt). See ABILITIES.md for the system.

---

## 1. Icons needed

**RESOLVED 2026-08-29:** Nick's painterly icon set covered every ability - all 58 assets
carry an icon (verified 2026-08-30: zero `icon: {fileID: 0}` under Assets/Data/PowerUps).
New abilities get art from Nick (ICONS.md documents the retired generator recipe only).

---

## 2. Chapter gating (RESOLVED 2026-08-30)

Gates derived from the live campaign configs (first chapter whose levels ambiently spawn
the brick) and written into the assets:

| Ability | Brick | Intro chapter | Gate set |
|---|---|---|---|
| Lighten | Feather | 2 (Sakura Ridge) | 2 |
| Reinforce / Bedrock | Boulder | 9 (Lost City) | 9 |
| Liquefy / MagmaSpawn | Magma | 10 (Burning Steppes) | 10 |
| AnchorBrick / AnchorSpawn | Anchor | **never spawns ambiently** | 2 (judgment call: the ability IS the brick's only source; gated past the opening chapter so the debut lands after basics. Revisit if an Anchor-teaching chapter ever ships.) |
| VineBrick / Overgrowth | Vine | 1 (Jungle Depths) | 0 (ungated = chapter 1; nothing to gate) |

Full brick-intro map (from the same scan): Vine 1, Feather 2, Ice 4, Locked 5, Sandstone 6,
Curse 7, Vortex 8, Boulder+Tremor 9, Bomb+Magma 10, Maw 14. The suppressor abilities keep
self-gating via `requiresVariantsInLevel`. If a chapter reshuffle moves a brick's intro,
re-run the scan and update the gates.


## 3. Other

- [ ] Revisit Guardian (50%) + Rebound (20%) co-ownership balance once playtested
      (combined ~60% lost-block save).

## 4. Short-description copy rule

Shorts must be genuinely SHORT: one plain clause, no " - flavor tail" (that's the long
description's job), aim ≤ ~50 chars. A July 2026 audit flagged 18 of 55 and all were
rewritten (Freeze also gained a real `shortDescription` instead of falling back to the
long). Keep new abilities to the same bar; the swap dialog + HUD slots show the same
string, so a bad short leaks into three surfaces.
