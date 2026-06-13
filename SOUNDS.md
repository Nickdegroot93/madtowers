# Sound inventory

Every sound the game currently triggers, what fires it, and what it should "be"
so a replacement (sourced or hand-made) drops in cleanly. **All SFX today are
procedurally synthesized** by `Tools/generate_sfx.py` — placeholders. Music is
real `.ogg` files. To swap a sound: drop a new `<name>.wav`/`.ogg` into
`Assets/Resources/Audio/Sfx/` with the SAME file name and it plays through
`SfxPlayer` unchanged (pooled, cached, pitch-jittered). No code edit needed.

Prefer **CC0 / royalty-free** (freesound.org CC0, Kenney, Sonniss GDC packs).

## SFX (placeholders — to be replaced)

| Name (file) | Triggered by | What it should be | Category |
|---|---|---|---|
| `impact_heavy_01`, `impact_heavy_02` | A piece flick-drops and lands ([BlockController.Landing.cs:34](Assets/SourceFiles/Scripts/Blocks/BlockController/BlockController.Landing.cs#L34)) — `PlayVariant` randomly picks one of the two each landing | Satisfying weighty block/stone *thud*. Two slightly different takes so repeats don't fatigue. The core "game feels good" sound — heard constantly. | Impact |
| `impact_soft_01` | Bullet **wasted shot** (hits floor/island/frozen — [BulletImpact.cs:51](Assets/SourceFiles/Scripts/Blocks/Variants/BulletImpact.cs#L51)); also a generic destroy-a-block effect ([AbilityEffects.cs:27](Assets/SourceFiles/Scripts/Abilities/Effects/AbilityEffects.cs#L27)) | Quiet, dull *thud* / "nothing happened". MUST read clearly softer & duller than `impact_shatter_01`. | Impact (dud) |
| `impact_shatter_01` | Bullet **destroys a block** ([BulletImpact.cs:51](Assets/SourceFiles/Scripts/Blocks/Variants/BulletImpact.cs#L51)) | Sharp, bright *crack/shatter* — stone or glass breaking. The payoff "kill" sound. | Block break |
| `gun_cock_01` | Bullet ability **activation / transform** ([BulletAbility.cs:41](Assets/SourceFiles/Scripts/Abilities/Definitions/BulletAbility.cs#L41)) | A single gun cock (pull back, slam home) — "weapon readied". | Spell / ability |
| `swoosh_01` | Corner-nudge **dash** ([BlockController.Input.cs:60](Assets/SourceFiles/Scripts/Blocks/BlockController/BlockController.Input.cs#L60)) | Short airy *whoosh* — air pushed aside. | Movement |
| `nudge_thud_01` | **Failed** nudge (blocked) ([BlockController.Input.cs:87](Assets/SourceFiles/Scripts/Blocks/BlockController/BlockController.Input.cs#L87)) | Dry *knock* — a refusal, distinct from a landing. | UI / feedback |
| `pop_01` | Support island materializes ([IslandPopFx.cs:49](Assets/SourceFiles/Scripts/World/IslandPopFx.cs#L49)); **also the generic ability-activate sound** for status consumables/combos ([StatusConsumableAbility.cs:19](Assets/SourceFiles/Scripts/Abilities/Definitions/StatusConsumableAbility.cs#L19), [StatusComboAbility.cs:21](Assets/SourceFiles/Scripts/Abilities/Definitions/StatusComboAbility.cs#L21), [DummyConsumableAbility.cs:10](Assets/SourceFiles/Scripts/Abilities/Definitions/DummyConsumableAbility.cs#L10)) | Friendly rising *blip/pop*. **Overloaded** — see gaps below; most abilities want their own sound. | UI / spawn |

### Sounds we don't have yet but the game will want
- **Per-ability activation sounds.** Right now every non-Bullet ability reuses `pop_01`. Each ability should get its own (Cement Tower = a heavy *set/pour*; Stasis = a shimmer/freeze; Overdrive = a power-up surge; etc.).
- **Combo / pattern fired** — a distinct chime, separate from the generic pop.
- **Life lost** / **game over** — none wired.
- **Level win / hold-steady success** — none wired.
- **Power-up offer appears** / **card pick** (UI) — none wired.
- **Countdown ticks** (5-4-3-2-1 hold-steady) — none wired.
- **Laser line clear** (puzzle modes) — none wired.

## Music (real tracks, per theme — `ThemeDefinition.musicPlaylist`)
| Theme | Tracks |
|---|---|
| Training Wheels | [training_wheels_a.ogg](Assets/Audio/Music/training_wheels_a.ogg), [training_wheels_b.ogg](Assets/Audio/Music/training_wheels_b.ogg) |
| Desert | [desert_a.ogg](Assets/Audio/Music/desert_a.ogg), [desert_b.ogg](Assets/Audio/Music/desert_b.ogg) |

Played by [MusicPlayer.cs](Assets/SourceFiles/Scripts/Core/MusicPlayer.cs) (crossfades through the theme's playlist). Source `.ogg` originals also live under `Assets/SourceFiles/SoundFX/`.

## How playback works (for whoever wires replacements)
- `SfxPlayer.Play(name, volume, pitchJitter)` — loads `Resources/Audio/Sfx/<name>`, plays a pooled one-shot with random pitch ±jitter.
- `SfxPlayer.PlayVariant(baseName, count, …)` — picks `<baseName>_01..0N` at random (used by `impact_heavy`). Add takes by adding `_03`, `_04`, … and bumping the count at the call site.
- File name IS the contract. Keep names; replace bytes.
