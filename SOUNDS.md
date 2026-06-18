# Sound inventory

Every sound the game currently triggers, **every** place it fires, and what it should
"be" so a replacement (sourced or hand-made) drops in cleanly. **All SFX today are
procedurally synthesized** by `Tools/generate_sfx.py` — placeholders, and they all sound
rough; this file is the map for replacing them. Music is real `.ogg` files. To swap a
sound: drop a new `<name>.wav`/`.ogg` into `Assets/Resources/Audio/Sfx/` with the SAME
file name and it plays through `SfxPlayer` unchanged (pooled, cached, pitch-jittered). No
code edit needed.

Prefer **CC0 / royalty-free** (freesound.org CC0, Kenney, Sonniss GDC packs).

**There are only 8 clips for the whole game**, so several are heavily overloaded — one
clip stands in for many unrelated events (see `swoosh_01` and `pop_01`). The "should be"
notes assume each clip keeps its current spread; splitting an overloaded clip into
purpose-made sounds is the bigger win (see *Gaps* below).

## SFX (placeholders — to be replaced)

Each heading is one clip file; the bullets are **every** call site that plays it.

### `impact_heavy_01`, `impact_heavy_02` — Impact (the core game-feel sound)
Played via `SfxPlayer.PlayVariant("impact_heavy", 2, …)` — randomly one of the two takes.
- A **flick-dropped** piece lands ([BlockController.Landing.cs:34](Assets/SourceFiles/Scripts/Blocks/BlockController/BlockController.Landing.cs#L34)) — only on the committed auto-drop, not gentle landings.

**Should be:** a satisfying weighty block/stone *thud*. Two slightly different takes so
repeats don't fatigue. Heard constantly — the "this game feels good" sound.

### `impact_soft_01` — Impact (dud / quiet removal)
- Bullet **wasted shot** — hits floor/island/frozen block ([BulletImpact.cs:51](Assets/SourceFiles/Scripts/Blocks/Variants/BulletImpact.cs#L51)).
- Generic **destroy a placed block** shatter ([AbilityEffects.DestroyBlockWithShatter](Assets/SourceFiles/Scripts/Abilities/Effects/AbilityEffects.cs#L52)) — shared by every ability that shatters a block, including Scrap.
- **Extract** deletes the chosen block ([ExtractTargetingSession.cs:328](Assets/SourceFiles/Scripts/Abilities/Effects/ExtractTargetingSession.cs#L328)).

**Should be:** a quiet, dull *thud* / "nothing much happened". MUST read clearly softer &
duller than `impact_shatter_01`.

### `impact_shatter_01` — Block break (the payoff "kill")
- Bullet **destroys a block** ([BulletImpact.cs:43](Assets/SourceFiles/Scripts/Blocks/Variants/BulletImpact.cs#L43)).
- **Sacrifice** destroys the lost block + the cost block ([SacrificeAbility.cs:87](Assets/SourceFiles/Scripts/Abilities/Definitions/SacrificeAbility.cs#L87)).
- **Fission** shatters the active piece into 1×1 shards ([FissionAbility.cs:47](Assets/SourceFiles/Scripts/Abilities/Definitions/FissionAbility.cs#L47)).

**Should be:** a sharp, bright *crack/shatter* — stone or glass breaking.

### `gun_cock_01` — Spell / ability
- Bullet ability **activation / transform** ([BulletAbility.cs:34](Assets/SourceFiles/Scripts/Abilities/Definitions/BulletAbility.cs#L34)).

**Should be:** a single gun cock (pull back, slam home) — "weapon readied".

### `swoosh_01` — Movement / generic ability (HEAVILY OVERLOADED — 12 sites)
The catch-all "something moved/activated" sound. Most of these want their own sound.
- Corner-**nudge dash** ([BlockController.Input.cs:60](Assets/SourceFiles/Scripts/Blocks/BlockController/BlockController.Input.cs#L60)).
- **Hold button** reveals when Pocket Cache unlocks ([HoldButton.cs:138](Assets/SourceFiles/Scripts/UI/HoldButton.cs#L138)).
- **Pocket Cache** bank / swap ([HoldCache.cs:116](Assets/SourceFiles/Scripts/Spawning/HoldCache.cs#L116)).
- A **status screen-field** appears, e.g. Brace's shield-up haze ([StatusFieldController.cs:59](Assets/SourceFiles/Scripts/Abilities/StatusFieldController.cs#L59)).
- **Rebound** rescue lift (saved block beamed back) ([RescueLift.cs:126](Assets/SourceFiles/Scripts/Abilities/Effects/RescueLift.cs#L126)).
- **Hardline** catch (lost block becomes a platform) ([HardlineAbility.cs:64](Assets/SourceFiles/Scripts/Abilities/Definitions/HardlineAbility.cs#L64)).
- **Transmute / Shrink** transforms the active piece ([TransmuteAbility.cs:34](Assets/SourceFiles/Scripts/Abilities/Definitions/TransmuteAbility.cs#L34)).
- **Flip** swaps the active piece with the next queued piece ([FlipAbility.cs:23](Assets/SourceFiles/Scripts/Abilities/Definitions/FlipAbility.cs#L23)).
- **Slo-Mo / slow-window** activate ([SlowWindowConsumable.cs:22](Assets/SourceFiles/Scripts/Abilities/Definitions/SlowWindowConsumable.cs#L22)).
- **Fission** shard advances from the queue into the drop slot ([FissionSession.cs:160](Assets/SourceFiles/Scripts/Abilities/Effects/FissionSession.cs#L160)).
- **Overdraw** activates and replaces the current active piece with the draft row ([OverdrawAbility.cs:39](Assets/SourceFiles/Scripts/Abilities/Definitions/OverdrawAbility.cs#L39)).
- **Overdraw** manual draft choice commits and flies into the drop lane ([OverdrawSession.cs:269](Assets/SourceFiles/Scripts/Abilities/Effects/OverdrawSession.cs#L269)). The final auto-committed choice is intentionally silent.

**Should be:** a short airy *whoosh* — air pushed aside. **Split me up:** a movement
whoosh is wrong for a shield-up, a save, a transmute, etc.

### `nudge_thud_01` — UI / feedback
- A **failed** nudge (blocked by bricks/islands) ([BlockController.Input.cs:87](Assets/SourceFiles/Scripts/Blocks/BlockController/BlockController.Input.cs#L87)).

**Should be:** a dry *knock* — a refusal, distinct from a landing.

### `pop_01` — UI / spawn (OVERLOADED — 6 sites)
- Support **island materializes** ([IslandPopFx.cs:49](Assets/SourceFiles/Scripts/World/IslandPopFx.cs#L49)).
- Generic **status consumable** activate ([StatusConsumableAbility.cs:19](Assets/SourceFiles/Scripts/Abilities/Definitions/StatusConsumableAbility.cs#L19)).
- Generic **status combo** fired ([StatusComboAbility.cs:21](Assets/SourceFiles/Scripts/Abilities/Definitions/StatusComboAbility.cs#L21)).
- **Freeze** power-up activate ([FreezePowerUp.cs:30](Assets/SourceFiles/Scripts/Abilities/Definitions/FreezePowerUp.cs#L30)).
- **Suspension** locks the selected block in place ([ExtractTargetingSession.cs:363](Assets/SourceFiles/Scripts/Abilities/Effects/ExtractTargetingSession.cs#L363)).
- **Dummy consumable** ([DummyConsumableAbility.cs:10](Assets/SourceFiles/Scripts/Abilities/Definitions/DummyConsumableAbility.cs#L10)) — test asset only.

**Should be:** a friendly rising *blip/pop*. **Overloaded** — most abilities want their own.

### Gaps — sounds the game wants but does not have
- **Per-ability activation sounds.** Most abilities reuse `swoosh_01`, `pop_01`, or shared impact clips. Each should get its own (Brace = shield-up shimmer; Freeze = icy crackle; Slo-Mo = time-warp wow; Transmute = a magic morph; Flip = crisp queue-swap flick; Suspension = clean gravity-lock shimmer; Fission = a brittle multi-crack distinct from the generic shatter; Extract = a soft extract/pluck; Overdraw = a sleek card/air shuffle plus soft select tick; Scrap = compact vaporize/undo puff; etc.).
- **Combo / pattern fired** — a distinct chime, separate from the generic pop.
- **Per-shard drop / land in Fission** — currently the generic landing sound; a lighter "tick" per micro-cube would read better.
- **Life lost** / **game over** — none wired.
- **Level win / hold-steady success** — none wired.
- **Power-up offer appears** / **card pick** (UI) — none wired.
- **Countdown ticks** (5-4-3-2-1 hold-steady) — none wired.
- **Laser line clear** (puzzle modes) — none wired.

## Music (real tracks, per chapter — `ChapterDefinition.musicPlaylist`)
| Chapter | Tracks |
|---|---|
| Training Wheels | [training_wheels_a.ogg](Assets/Audio/Music/training_wheels_a.ogg), [training_wheels_b.ogg](Assets/Audio/Music/training_wheels_b.ogg) |
| Desert | [desert_a.ogg](Assets/Audio/Music/desert_a.ogg), [desert_b.ogg](Assets/Audio/Music/desert_b.ogg) |

Played by [MusicPlayer.cs](Assets/SourceFiles/Scripts/Core/MusicPlayer.cs) (crossfades through the chapter's playlist). Source `.ogg` originals also live under `Assets/SourceFiles/SoundFX/`.

## How playback works (for whoever wires replacements)
- `SfxPlayer.Play(name, volume, pitchJitter)` — loads `Resources/Audio/Sfx/<name>`, plays a pooled one-shot with random pitch ±jitter.
- `SfxPlayer.PlayVariant(baseName, count, …)` — picks `<baseName>_01..0N` at random (used by `impact_heavy`). Add takes by adding `_03`, `_04`, … and bumping the count at the call site.
- File name IS the contract. Keep names; replace bytes.
</content>
