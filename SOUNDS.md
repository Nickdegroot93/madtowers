# Sound inventory

Every sound the game triggers, **every** place it fires, and what it should "be" so a
replacement drops in cleanly. To swap a sound: drop a file with the SAME base name into
`Assets/Resources/Audio/Sfx/` (any Unity-importable format — `.wav`/`.ogg`/`.mp3`) and it
plays through `SfxPlayer` unchanged (pooled, cached, optional pitch jitter). **File name
is the contract — keep the name, replace the bytes.** Music lives in
`Assets/Resources/Audio/Music/` (menu) and `Assets/Audio/Music/` (chapters).

Prefer **CC0 / royalty-free** (freesound.org CC0, Kenney, Sonniss GDC packs).

Two tiers of SFX today:
- **Authored** (real sounds, leading/trailing silence trimmed, mostly `.wav`): `freeze`,
  `transmute`, `nudge`, `rotate-swoosh`, `pop`, `countdown`, `ui-button-click`,
  `ui-leave-game`, `ui-start-game` (+ unwired `zap`).
- **Placeholder** (procedurally synthesized by `Tools/generate_sfx.py`, rough — still to be
  replaced): `impact_heavy_01/02`, `impact_soft_01`, `impact_shatter_01`, `gun_cock_01`,
  `swoosh_01`, `nudge_thud_01`, `pop_01`.

`swoosh_01` (10 sites) and `pop_01` (3 sites) are still overloaded — one clip standing in
for many unrelated events. Splitting those into purpose-made sounds is the next big win
(see *Gaps*).

## SFX

Each heading is one clip; the bullets are **every** call site that plays it.

### `impact_heavy_01`, `impact_heavy_02` — Impact (the core game-feel sound) · *placeholder*
Played via `SfxPlayer.PlayVariant("impact_heavy", 2, …)` — randomly one of two takes.
- A **flick-dropped** piece lands ([BlockController.Landing.cs:34](Assets/SourceFiles/Scripts/Blocks/BlockController/BlockController.Landing.cs#L34)) — committed auto-drop only, not gentle landings.

**Should be:** a satisfying weighty block/stone *thud*. Two takes so repeats don't fatigue.

### `impact_soft_01` — Impact (dud / quiet removal) · *placeholder*
- Zap **wasted shot** — empty column ([ZapSession.cs](Assets/SourceFiles/Scripts/Abilities/Effects/ZapSession.cs)).
- Generic **destroy a placed block** shatter ([ImpactFx.cs:27](Assets/SourceFiles/Scripts/Core/ImpactFx.cs#L27)) — shared by every shatter, incl. Scrap.
- **Extract** deletes the chosen block ([ExtractTargetingSession.cs:359](Assets/SourceFiles/Scripts/Abilities/Effects/ExtractTargetingSession.cs#L359)).

**Should be:** a quiet, dull *thud*. Clearly softer & duller than `impact_shatter_01`.

### `impact_shatter_01` — Block break (the payoff "kill") · *placeholder*
- Zap **destroys the targeted block** ([ZapSession.cs](Assets/SourceFiles/Scripts/Abilities/Effects/ZapSession.cs)).
- **Sacrifice** destroys the lost + cost block ([SacrificeAbility.cs:87](Assets/SourceFiles/Scripts/Abilities/Definitions/SacrificeAbility.cs#L87)).
- **Fission** shatters the active piece into shards ([FissionAbility.cs:47](Assets/SourceFiles/Scripts/Abilities/Definitions/FissionAbility.cs#L47)).

**Should be:** a sharp, bright *crack/shatter* — stone or glass breaking.

### `swoosh_01` — Movement / generic ability (OVERLOADED — 11 sites) · *placeholder*
The catch-all "something moved/activated". Most of these want their own sound.
- **Zap** activation — the active piece vanishes into the laser ([ZapAbility.cs](Assets/SourceFiles/Scripts/Abilities/Definitions/ZapAbility.cs)).
- **Hold button** reveals when Pocket Cache unlocks ([HoldButton.cs:138](Assets/SourceFiles/Scripts/UI/HoldButton.cs#L138)).
- **Pocket Cache** bank / swap ([HoldCache.cs:116](Assets/SourceFiles/Scripts/Spawning/HoldCache.cs#L116)).
- A **status screen-field** appears, e.g. Brace's shield-up haze ([StatusFieldController.cs:59](Assets/SourceFiles/Scripts/Abilities/StatusFieldController.cs#L59)).
- **Rebound** rescue lift ([RescueLift.cs:126](Assets/SourceFiles/Scripts/Abilities/Effects/RescueLift.cs#L126)).
- **Hardline** catch ([HardlineAbility.cs:64](Assets/SourceFiles/Scripts/Abilities/Definitions/HardlineAbility.cs#L64)).
- **Flip** swaps active piece with next queued ([FlipAbility.cs:23](Assets/SourceFiles/Scripts/Abilities/Definitions/FlipAbility.cs#L23)).
- **Slo-Mo / slow-window** activate ([SlowWindowConsumable.cs:22](Assets/SourceFiles/Scripts/Abilities/Definitions/SlowWindowConsumable.cs#L22)).
- **Fission** shard advances from the queue ([FissionSession.cs:160](Assets/SourceFiles/Scripts/Abilities/Effects/FissionSession.cs#L160)).
- **Overdraw** activates ([OverdrawAbility.cs:39](Assets/SourceFiles/Scripts/Abilities/Definitions/OverdrawAbility.cs#L39)).
- **Overdraw** manual draft choice commits ([OverdrawSession.cs:269](Assets/SourceFiles/Scripts/Abilities/Effects/OverdrawSession.cs#L269)).

**Should be:** a short airy *whoosh*. **Split me up** — a whoosh is wrong for a shield-up, a save, etc.

### `nudge_thud_01` — Failed nudge · *placeholder*
- A **failed** nudge, blocked by bricks/islands ([BlockController.Input.cs:87](Assets/SourceFiles/Scripts/Blocks/BlockController/BlockController.Input.cs#L87)).

**Should be:** a dry *knock* — a refusal, distinct from a landing.

### `pop_01` — Generic activate (OVERLOADED — 3 sites) · *placeholder*
- Generic **status consumable** activate ([StatusConsumableAbility.cs:19](Assets/SourceFiles/Scripts/Abilities/Definitions/StatusConsumableAbility.cs#L19)).
- Generic **status combo** fired, e.g. Overdrive ([StatusComboAbility.cs:21](Assets/SourceFiles/Scripts/Abilities/Definitions/StatusComboAbility.cs#L21)).
- **Suspension** locks the selected block in place ([ExtractTargetingSession.cs:368](Assets/SourceFiles/Scripts/Abilities/Effects/ExtractTargetingSession.cs#L368)).

**Should be:** a friendly rising *blip/pop*. **Overloaded** — most of these want their own.

### `freeze` — Freeze ability · *authored*
- **Freeze** power-up activate ([FreezePowerUp.cs:29](Assets/SourceFiles/Scripts/Abilities/Definitions/FreezePowerUp.cs#L29)).

### `transmute` — Morph a shape · *authored*
- **Transmute / Shrink** morphs the active piece into another shape ([TransmuteAbility.cs:34](Assets/SourceFiles/Scripts/Abilities/Definitions/TransmuteAbility.cs#L34)). Covers any morph (Shrink→Pip + the generic transmute), NOT Flip's shape-swap.

### `nudge` — Successful nudge · *authored*
- A **successful** left/right nudge dash ([BlockController.Input.cs:60](Assets/SourceFiles/Scripts/Blocks/BlockController/BlockController.Input.cs#L60)).

### `rotate-swoosh` — Rotate a block · *authored*
- Rotate the active piece, both directions ([BlockController.Input.cs:113](Assets/SourceFiles/Scripts/Blocks/BlockController/BlockController.Input.cs#L113) / [:120](Assets/SourceFiles/Scripts/Blocks/BlockController/BlockController.Input.cs#L120)).

### `pop` — Sky block materializes · *authored*
- A support / sky block **pops in out of thin air** ([IslandPopFx.cs:49](Assets/SourceFiles/Scripts/World/IslandPopFx.cs#L49)).

### `countdown` — Level-finish clock (sustained) · *authored*
A clock that **starts** when the 5→0 hold-steady countdown arms and **stops** at 0 / abort /
teardown. Played via `SfxPlayer.PlayLoop("countdown")` / `StopLoop()` (dedicated source).
- Armed in [LevelRuntimeController.cs:268](Assets/SourceFiles/Scripts/Levels/LevelRuntimeController.cs#L268); stopped in `DestroyCountdownUi` ([:338](Assets/SourceFiles/Scripts/Levels/LevelRuntimeController.cs#L338)).

### `ui-button-click` — UI button (6 sites) · *authored*
- Bottom nav tabs ([MainMenuRuntime.cs:1183](Assets/SourceFiles/Scripts/Menu/MainMenuRuntime.cs#L1183)), Home tab ([:1212](Assets/SourceFiles/Scripts/Menu/MainMenuRuntime.cs#L1212)), chapter switch ([:700](Assets/SourceFiles/Scripts/Menu/MainMenuRuntime.cs#L700)), generic menu button ([:1269](Assets/SourceFiles/Scripts/Menu/MainMenuRuntime.cs#L1269)), opening the level-summary modal ([:1287](Assets/SourceFiles/Scripts/Menu/MainMenuRuntime.cs#L1287)), and the Ranks button ([:1407](Assets/SourceFiles/Scripts/Menu/MainMenuRuntime.cs#L1407)).

### `ui-leave-game` — Leave to menu · *authored*
- Confirmed "Back to Menu" / quit a run ([PauseMenuController.cs:147](Assets/SourceFiles/Scripts/UI/PauseMenuController.cs#L147)).

### `ui-start-game` — Start a level · *authored*
- **Play** button in the level-summary modal ([MainMenuRuntime.cs:1389](Assets/SourceFiles/Scripts/Menu/MainMenuRuntime.cs#L1389)).

### `zap` — *authored, UNWIRED*
Present in `Resources/Audio/Sfx/` but not triggered anywhere yet. Earmarked for the **laser
line clear** gap below.

### `pocket_seal`, `pocket_fill`, `pocket_vent`, `pocket_pop` — Airtight air pockets · *generated (ElevenLabs)*
Airtight mode's sealed-hollow hazard (LEVELS.md "Airtight details"). Prompts live in
`Tools/generate_elevenlabs_sfx.py`; regenerate any one with `--only <name> --force`.
NOTE: the hazard/status sounds deliberately skip the house STYLE prefix — cinematic
sci-fi/electrical character is the point, and the prefix homogenized them into
indistinguishable low-frequency pops (July 2026).
- `pocket_fill` — the 16 s rising tension bed played on the pocket's own AudioSource while
  the smoke rises, swelling with the fill level and CUT wherever the vent or pop lands
  ([AirPocketFx.cs Build/SetFill](Assets/SourceFiles/Scripts/Abilities/Effects/AirPocketFx.cs)).
- `pocket_seal` — a placement seals an empty region and the fuse arms ([AirPocketModifier.cs ReconcilePockets](Assets/SourceFiles/Scripts/Levels/Modifiers/AirPocketModifier.cs)).
- `pocket_vent` — the rescue: a sealing block is destroyed and the smoke escapes ([AirPocketModifier.cs Vent](Assets/SourceFiles/Scripts/Levels/Modifiers/AirPocketModifier.cs)).
- `pocket_pop` — detonation, alongside the size-scaled Tremor quake + camera impact ([AirPocketModifier.cs Detonate](Assets/SourceFiles/Scripts/Levels/Modifiers/AirPocketModifier.cs)).

**Should be:** a deep muffled airless *whump* (seal); a sharp relieving steam-hiss (vent);
a fat muffled underground *boom* (pop).

### `blackout_in`, `blackout_out` — Blackout status · *generated (ElevenLabs)*
The scheduled power-loss state (LEVELS.md "Blackout details"). Prompts in
`Tools/generate_elevenlabs_sfx.py`.
- `blackout_in` — the curtain starts fading in ([BlackoutOverlay.cs Awake](Assets/SourceFiles/Scripts/Abilities/Effects/BlackoutOverlay.cs)).
- `blackout_out` — the relight pre-fade begins ([BlackoutOverlay.cs LateUpdate](Assets/SourceFiles/Scripts/Abilities/Effects/BlackoutOverlay.cs)).

**Should be:** a deep descending power-down *whomp*, hum dying (in); breakers clunking back,
hum swelling (out).

### `void_open`, `void_suck` — Void Zones · *generated (ElevenLabs)*
The forbidden-sky-rectangle hazard (LEVELS.md "Void Zones details"). Prompts in
`Tools/generate_elevenlabs_sfx.py`.
- `void_open` — a zone tears open ahead of the tower ([VoidZoneModifier.cs TrySpawnZone](Assets/SourceFiles/Scripts/Levels/Modifiers/VoidZoneModifier.cs)).
- `void_suck` — a landed block is dragged into the eye ([VoidSuckFx.cs Begin](Assets/SourceFiles/Scripts/Abilities/Effects/VoidSuckFx.cs)).

**Should be:** a low fabric-of-space rip settling into a whirl (open); an accelerating
spiral whoosh ending in a deep gulp-thud (suck).

### `sandstone_crack`, `sandstone_burst` — Sandstone brick · *generated (ElevenLabs)*
The load-bearing-limit brick (BLOCKVARIANTS.md catalog). Prompts in
`Tools/generate_elevenlabs_sfx.py` (no house STYLE prefix — it turned the crack into a low
tonal chime; these need broadband dry grit).
- `sandstone_crack` — the damage ratchet crosses a new third ([SandstoneBlockBehaviour.cs FixedUpdate](Assets/SourceFiles/Scripts/Blocks/Variants/SandstoneBlockBehaviour.cs)); pitch drops slightly per stage.
- `sandstone_burst` — the brick crumbles ([SandstoneBlockBehaviour.cs Crumble](Assets/SourceFiles/Scripts/Blocks/Variants/SandstoneBlockBehaviour.cs)).

**Should be:** a dry stone fracture tick, grit settling (crack); a modest sandstone collapse —
crunch into pouring sand, no explosion (burst).

## Music

Played by [MusicPlayer.cs](Assets/SourceFiles/Scripts/Core/MusicPlayer.cs): a random opener,
then a fixed A→B→A rotation. Survives scene loads, ignores pause, stops on game over.

| Context | Tracks | Trigger |
|---|---|---|
| **Menu** (menu, settings, custom game — everywhere outside a level) | [menu-a.ogg](Assets/Resources/Audio/Music/menu-a.ogg), [menu-b.ogg](Assets/Resources/Audio/Music/menu-b.ogg) | `MusicPlayer.PlayMenu()` ([MainMenuRuntime.cs:125](Assets/SourceFiles/Scripts/Menu/MainMenuRuntime.cs#L125)) |
| Training Wheels | training_wheels_a/b.ogg | `MusicPlayer.PlayForChapter` ([GameManager.cs:78](Assets/SourceFiles/Scripts/Core/GameManager.cs#L78)) |
| Desert | desert_a/b.ogg | same |
| Jungle Depths | jungle-depths-a/b.ogg | same |
| Sakura Ridge | sakura-ridge-a/b.ogg | same |

Entering a level swaps menu → chapter music; returning to the menu swaps back.

## How playback works
- `SfxPlayer.Play(name, volume, pitchJitter)` — pooled one-shot from `Resources/Audio/Sfx/<name>`.
- `SfxPlayer.PlayVariant(baseName, count, …)` — random `<baseName>_01..0N` (used by `impact_heavy`).
- `SfxPlayer.PlayLoop(name, volume)` / `StopLoop()` — sustained, stoppable clip (the countdown).
- `MusicPlayer.PlayForChapter(chapter)` / `PlayMenu()` — playlist with random opener + A→B rotation.
- **All audio is cleanly split** into `SfxPlayer` (SFX) and `MusicPlayer` (music) — nothing else
  makes sound — so independent music/SFX volume control is a straightforward future add (an
  `AudioMixer` with Music + SFX groups, both players routed to them).

## Gaps — sounds the game wants but does not have
- **Per-ability identity** for everything still on `swoosh_01` / `pop_01`: Brace shield-up,
  Slo-Mo time-warp, Flip queue-swap flick, Hardline catch, Rebound save, Pocket Cache,
  Overdraw shuffle, Suspension gravity-lock, status consumable/combo.
- **Combo / pattern fired** — a distinct chime, separate from the generic `pop_01`.
- **Per-shard drop / land in Fission** — a lighter "tick" per micro-cube.
- **Gentle (non-flick) landings** — silent today; only flick-drops play `impact_heavy`.
- **Life lost** / **game over** — none wired.
- **Level win / hold-steady success** — none wired (the countdown plays, but not the win itself).
- **Power-up offer appears** / **ability card pick** — none wired (`ui-button-click` is menu-only).
- **Laser line clear** (puzzle modes) — none wired; `zap` is the earmarked clip.
