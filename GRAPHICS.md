# GRAPHICS.md — graphics settings & effect-gating architecture

**Status:** binding for anything rendered as an effect (VFX, post-processing, camera shake) and
for the frame-rate cap. Read this before adding a new ability/block look so it's toggle-able.

The player controls three graphics settings (Settings → Graphics). They persist in
`SettingsService` (`settings.graphics.*`) and are enforced at **central chokepoints**, never by
scattered per-effect checks. The payoff: every current **and future** effect respects them for
free — as long as new effects go through those chokepoints.

## The settings

| Setting | Default | Effect |
|---|---|---|
| **Frame Rate** | 60 | caps `Application.targetFrameRate` (30 / 60 / 120) — battery, heat, pacing |
| **Visual Effects** | on | post-processing (bloom/glow/grade) **and** decorative prefab VFX |
| **Screen Shake** | on | camera shake on impacts |

## The chokepoints — where each is enforced (and where YOUR new effect must go)

1. **Frame rate → `GraphicsSettingsApplier`** (`Core/`). Applies the cap at startup
   (`RuntimeInitialize`) and on every `SettingsService.Changed`. Global; individual code does nothing.
2. **Post-processing → `PostFxController`** (`Core/`). The ONE global post stack
   (vignette + bloom + colour grading). It sets the camera's `renderPostProcessing` from
   `SettingsService.VisualEffects`, live. **Add new post effects to this stack** — they inherit the
   toggle. Never add a second `Volume` or per-camera post.
3. **Decorative prefab VFX → `Vfx.Spawn`** (`Core/`). The ONE spawner for authored effect prefabs
   (CFXR etc.). Returns `null` when Visual Effects is off. **Play every decorative effect through
   `Vfx.Spawn`** — do not `Instantiate` effect prefabs directly — and it's gated automatically.
4. **Screen shake → `TowerCameraController.Impact(amplitude, duration)`** (`Camera/`). The ONE way
   to shake; `ImpactFx.ImpactPunch` wraps it (hit-stop + shake). Early-returns when Screen Shake is
   off. **Never move the camera for a shake yourself** — call `Impact` / `ImpactFx.ImpactPunch`.

## Rule for new abilities / blocks / looks

- Shake → `TowerCameraController.Impact` or `ImpactFx.ImpactPunch` (see ABILITIES.md juice standard).
- Authored VFX prefab → `Vfx.Spawn`.
- Glow / bloom / colour → the `PostFxController` stack.
- Anything else that should drop on low-end / battery-saver but isn't covered above: read
  `SettingsService.VisualEffects` and skip the heavy part when it's `false`.

## Notes / future

- `BlockShatterFx` (procedural block-break) is core feedback and is **not** gated by Visual Effects
  today — revisit only if it's a measured perf cost.
- The `120` option is offered on every device; a 60 Hz screen caps it to 60 (harmless). Add
  refresh-rate detection later if we want to hide unsupported options.
- Quality presets / URP render-scale can layer into `GraphicsSettingsApplier` later without touching
  call sites — that's the point of keeping the cap there.
