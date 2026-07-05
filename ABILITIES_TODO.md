# Abilities — follow-up tracker

Living checklist of unfinished work on ability **assets** (the abilities themselves are
functional; this tracks polish + content-gating debt). See ABILITIES.md for the system.

---

## 1. Icons needed

Abilities currently shipping with no icon (`icon: {fileID: 0}`) — they fall back to
title text on the card, which works but looks unfinished. Generate via
`Tools/generate_ability_icons.py` (see ART.md §12), then wire the sprite into each asset.

**Epic**
- [ ] Guardian
- [ ] IronWill
- [ ] MagmaSpawn
- [ ] Overgrowth
- [ ] Purifier
- [ ] Slowburn
- [ ] Titan
- [ ] Updraft

**Rare**
- [ ] AnchorSpawn
- [ ] Ballast
- [ ] Bedrock
- [ ] BombSquad
- [ ] Dampener
- [ ] DeIcer
- [ ] DragChute
- [ ] Liquefy
- [ ] Locksmith
- [ ] Muzzle
- [ ] Reinforce
- [ ] Reroll
- [ ] Sanitize
- [ ] SteadyHands
- [ ] SureGrip
- [ ] VineBrick

**Common**
- [ ] Lighten
- [ ] Ward
- [ ] Zap

---

## 2. Chapter gating (pending level design)

Every ability that **introduces or boosts a block variant** must eventually carry a
`minChapterNumber` matching the chapter where that brick is first taught, so players are
never offered an ability for a brick they have never seen. Brick-intro chapters aren't
pinned yet, so most ship ungated (`minChapterNumber: 0`) and MUST be revisited once the
campaign layout is fixed. (Mechanism: `AbilityDefinition.minChapterNumber`, ABILITIES.md §7.)

| Ability | Type | Introduces brick | Current gate | Needs |
|---|---|---|---|---|
| AnchorBrick | transmute | Anchor | 0 (ungated) | set to Anchor's intro chapter |
| AnchorSpawn | booster | Anchor | 0 (ungated) | set to Anchor's intro chapter |
| VineBrick | transmute | Vine | 0 (ungated) | set to Vine's intro chapter |
| Overgrowth | booster | Vine | 0 (ungated) | set to Vine's intro chapter |
| Reinforce | transmute | Boulder | 0 (ungated) | set to Boulder's intro chapter |
| Bedrock | booster | Boulder | 0 (ungated) | set to Boulder's intro chapter |
| Lighten | transmute | Feather | 0 (ungated) | set to Feather's intro chapter |
| Liquefy | transmute | Magma | **7** (provisional) | confirm against real volcano chapter |
| MagmaSpawn | booster | Magma | **7** (provisional) | confirm against real volcano chapter |

> Note: the suppressor abilities (Muzzle/Maw, SteadyHands/Vortex, Dampener/Tremor,
> Ballast/Feather, DeIcer/Ice, Locksmith/Locked, BombSquad/Bomb) gate themselves
> automatically via `requiresVariantsInLevel` — they only appear where that brick can
> already spawn — so they do NOT need a `minChapterNumber`.

---

## 3. Other

- [ ] Revisit Guardian (50%) + Rebound (20%) co-ownership balance once playtested
      (combined ~60% lost-block save).

## 4. Short-description copy rule

Shorts must be genuinely SHORT: one plain clause, no " - flavor tail" (that's the long
description's job), aim ≤ ~50 chars. A July 2026 audit flagged 18 of 55 and all were
rewritten (Freeze also gained a real `shortDescription` instead of falling back to the
long). Keep new abilities to the same bar; the swap dialog + HUD slots show the same
string, so a bad short leaks into three surfaces.
