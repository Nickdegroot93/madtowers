# Data Model

The game should be data-first. Code should define what is possible; assets should define what a level actually uses.

## LevelDefinition

`LevelDefinition` is the future top-level authored asset.

Fields:
- Display name
- `GameModeConfig`
- Background image
- Background tint
- Music

Future fields can include theme VFX, level intro text, allowed power-up set, and level-specific tutorial prompts.

## GameModeConfig

`GameModeConfig` is the mechanical rules asset.

Current responsibilities:
- Starting lives
- Initial fall speed and gravity
- Difficulty scaling mode: none, per block, or over time
- Difficulty adjustment mode: additive or percent
- Block bag
- Variant pool
- Spawn delay
- Grid spacing
- Landing/stability settings
- Lateral brace stability settings
- Connected-component brace size limits
- Floor segments
- Camera composition
- Power-up spawn timing
- Static support island frequency, horizontal range, and weighted shape table

Future responsibilities:
- Weighted power-up pool
- Phase-based support island rules
- Allowed block variants by phase
- Timed level modifiers
- Starting camera/floor layout presets

## Static Support Islands

Static support islands are level/world geometry, not tetromino blocks.

Current responsibilities in `GameModeConfig`:
- Enabled/disabled flag
- Height interval for spawn rolls
- Spawn chance per interval
- First height where rolls begin
- Spawn-ahead height
- Allowed horizontal column range
- Center clear columns, so islands cannot spawn in the default falling lane
- Weighted shape entries

Each shape entry contains:
- Display name
- Weight
- Grid cell offsets

Example tuning:
- A 1x1 shape with weight `6`
- A 2-wide shape with weight `3`
- A corner shape with weight `1`

This lets one level make islands rare and tiny, while another level can make larger supports common.

## BlockDefinition

`BlockDefinition` is the shape asset.

Current responsibilities:
- Display name
- Prefab
- Default variant
- Bag copies

This is where shape identity belongs. It should not hold per-level tuning unless the shape itself truly owns that value.

## BlockData

`BlockData` is the variant asset.

Current responsibilities:
- Display name
- Mass
- Physics material
- Gravity multiplier
- Color, sprite, and material overrides
- Landing mode override
- Stability-failure override for future sturdy/anchor blocks

Examples:
- Normal
- Heavy
- Icy
- Bouncy
- Light
- Sturdy
- Fragile

## Power-Ups

Power-ups are still prototype-level. The next architecture step should be a `PowerUpDefinition` ScriptableObject before adding many effects.

Suggested future fields:
- Display name
- Icon
- Prefab override
- Effect type
- Duration
- Weight
- Targeting rule, such as next block, current block, tower, time, or level

## Spawn Tables

The current block bag is good enough for shapes. As levels become more complex, avoid adding more random logic directly to `Spawner`.

Suggested future data:
- Weighted block shape entries
- Weighted variant entries
- Phase-based rules, such as first 10 blocks, after height 20, or after 60 seconds
- Forced next block rewards from power-ups
