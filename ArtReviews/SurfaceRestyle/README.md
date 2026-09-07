# Surface and overlay restyle — commit review

Reviewed against `4f3fea4` (`fix: ice bricks`). This change contains runtime presentation
code, shader assets with their Unity metadata, and the corresponding design contracts.
The cancelled App Store screenshot work added no production gameplay changes.

## Included

- Flood: painted depth, broken surface light, refraction/reflection, and a visual ripple
  driven by the existing swallow splash. `SurfaceSceneFx` supplies tileable noise and
  half-resolution world rendering shared with void zones.
- Void zones: a readable violet cut face, moving interior currents, inward dust,
  opening animation and a disposable closing scar. This includes the later colour and
  motion revision requested after the initial grey version.
- Results, pause, hold-steady and medal debut: weighted entrances, clearer hierarchy,
  unscaled animation, safe-area framing, medal light sweeps and immediate skip-to-actions.
  Pause also implements the existing MEDALS.md requirement to label quitting after an
  earned rung as **Finish Run**.
- Review fix: `MedalLightSweepFx.Finish` disables updates before releasing its material.
  Unity defers component destruction, so an early/repeated skip must not leave an update
  scheduled against the released material.

## Validation

Rechecked during commit preparation:

- 29 source and shader-cost checks passed. Modifier rules, physics/game manager, medal
  derivation, death beat, restart/menu actions and the checked verification methods retain
  their baseline implementations. Both surface shaders use two tileable-noise samples
  and no per-fragment hash noise.
- Five live Unity checks passed: same-frame repeated results skip disables the sweep;
  action rows become immediately interactive; the sweep component/material are disposed;
  flood and two voids share one capture host; closing the last effects releases the host
  and scars.
- Flood, VoidZone and MedalLightSweep shaders reported no compiler messages. The Unity
  console returned zero error entries after the bounded runtime checks.
- New assets have matching `.meta` files; whitespace checks pass.

Earlier restyle evidence, retained locally: 36 UI/content path checks, four motion checks,
three lifecycle checks, 31 identical flood-height samples, and void pull/consume timing
within 0.02 seconds with the same rectangle and affected-brick count. Notched-phone and
tablet layouts were captured. Those broader click-through checks were not all rerun
during this commit review. Ad/store paths used Editor providers, not live transactions.

## Excluded and remaining review

- Raw captures, comparison galleries, generated logs and temporary MCP scripts in this
  directory and `ArtReviews/AppStore/` remain local and are ignored. They are not runtime
  dependencies. Only this review record is included.
- `Assets/Hovl Studio/Fullscreen effects/Prefabs/Screen buff.prefab` remains unstaged.
  Its separate edit changes particle widths from 19.043211 to 13.807531 in four serialized
  fields. The vendor's `HS_ScreenEffect` recalculates those fields from camera aspect in
  edit mode; this appears to be viewport-sizing noise. It predates this preparation and
  is preserved for separate review.
- Mobile GPU profiling remains outstanding. The half-resolution scene redraws are the
  main performance review point; Editor shader compilation does not establish device cost.
- The screenshot player is stopped. Local progress is restored to the saved pre-capture
  state, and Unity is left outside play mode.
