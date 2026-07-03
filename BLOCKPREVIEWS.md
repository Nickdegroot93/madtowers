# BLOCKPREVIEWS.md — "how this brick works" demos

The little looping clips that pop up to *show* a brick's behaviour when it's introduced (Bomb
detonating, Maw devouring, Magma melting to terrain). They read like short videos but are **live
in-engine loops**, not video files. Quick design note — not built yet.

This is a **separate feature from the first-run tutorial** ([TUTORIAL.md](TUTORIAL.md)):
- the *tutorial* teaches **gestures**, interactively, on the live board (you do it);
- a *block preview* teaches a **brick's behaviour**, passively, in a looping demo (you watch).

Introduction cadence (which brick, which chapter) is owned by [PROGRESSION.md](PROGRESSION.md);
the brick behaviours themselves by [BLOCKVARIANTS.md](BLOCKVARIANTS.md).

---

## Decision: in-engine real-time loops, not video files

For a chapter-themed game that must fit every phone aspect ratio, baked video fights everything
we've built. Render the demo with the actual game instead:

| | In-engine loop (chosen) | Pre-recorded video |
|---|---|---|
| App size | ~free (it's the game) | heavy; bloats the download |
| Responsive (RESPONSIVE.md) | crisp at any resolution/aspect | one aspect → letterbox/crop elsewhere |
| Theming | picks up chapter accent colours live | frozen to its recording |
| Staleness | always matches current physics + art | re-record on every art/physics change |
| Localisation | captions are live UI | baked-in text is frozen |
| Authoring cost | more engineering | just hit record (its only real win) |

**Video stays a fallback** only for a behaviour too non-deterministic or expensive to script into
a clean loop — none of the current bricks should need it.

---

## Shape

A small, self-contained **demo scenario per brick behaviour**, rendered off-screen into a
`RenderTexture` and shown inside a card (a `RawImage`):

- A tiny sandbox (a floor + a couple of blocks) in its own scene/prefab, on its own physics —
  isolated from the real game.
- A **scripted loop**: spawn the brick → let its behaviour play (drop it on the stack, detonate,
  devour, melt) → hold a beat → **reset → loop**. It doesn't need frame-perfect determinism
  across devices; it only has to *read* right, and the periodic reset stops drift accumulating.
- **Rendered on demand**: a dedicated demo camera → `RenderTexture`; only render while the card is
  visible (pause when off-screen) so idle demos cost nothing.
- **Themed**: the sandbox uses the active chapter's accent colours, so a Bomb demo shown in the
  volcano chapter looks like the volcano chapter — automatically.
- A one-line **caption** ("Bomb — clears its neighbours, then everything above drops").

Data-driven: one `BrickDemo` definition per behaviour — `{ brick, scenario script, loop seconds,
caption }` — so adding a brick is authoring a scenario, not writing bespoke UI.

---

## Where they surface (open — pick during build)

- **Debut card**: the first time a brick appears in the campaign, a modal shows its demo before
  the level (pairs with PROGRESSION.md's "teach, then test").
- **Codex / collection screen**: a browsable grid of every brick, each card looping its demo —
  the natural home, and a nice progression reward.
- **Pre-level briefing**: a strip of the bricks a level will feature.

Likely **debut card + codex** share the same demo component.

---

## Open decisions

- **Surfacing** — debut modal, codex, briefing, or all three (shared component either way).
- **Interactive?** — pure loop, or tap-to-replay / scrub. Loop is enough to start.
- **Performance budget** — one demo rendering at a time (debut) is trivial; a codex grid of many
  needs care (render only visible cards, or pre-bake first frames as posters).
- **Reset style** — hard cut vs. a quick fade between loops (fade reads calmer).
- **Authoring** — hand-scripted scenarios vs. a recorded input replay played back live.

*Build after the tutorial. Update when the demo system lands or a brick's behaviour changes.*
