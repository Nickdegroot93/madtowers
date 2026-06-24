# MadTowers Responsive UI — Safe Areas & Screen Independence

Every screen of this game ships to phones with wildly different shapes: notches,
camera cutouts, status bars, rounded corners, home indicators, and aspect ratios
from tall 21:9 to near-square. **This document is the contract for making UI that
looks right on all of them. Read it before adding or moving any UI.**

All runtime UI is built imperatively in C# (no UI prefabs/scenes), so these rules
live in code, not in the inspector. Visual style lives in STYLE.md; this file is
purely about *layout that survives any screen*.

Code: `Assets/SourceFiles/Scripts/UI/RuntimeUiKit.cs` (helpers + `CreateOverlayCanvas`),
`Assets/SourceFiles/Scripts/UI/SafeAreaFitter.cs` (the component).

---

## 1. The two rules

1. **Never pin a readable or interactive element to a raw screen edge.** Phones cover
   their edges (notch on top, home indicator on the bottom, curved corners on the
   sides). Pin to the **safe area** instead (§3).
2. **Size by fraction, not by fixed pixels, for anything width-dependent.** A card,
   bar, or row that should "fill the width" must *stretch* between anchors, not carry
   a hard-coded `sizeDelta.x`. Fixed pixel widths only look right on the one screen
   they were tuned on (§4).

Both rules degrade to a perfect no-op on a clean, notchless 9:16 screen — so applying
them never costs anything; skipping them breaks a real phone.

---

## 2. The canvas

Every overlay canvas comes from `RuntimeUiKit.CreateOverlayCanvas`, which sets:

- `ScaleWithScreenSize`, reference resolution **1080×1920**, `matchWidthOrHeight = 0.5`.

`match = 0.5` blends width- and height-scaling, so on an odd aspect ratio the canvas's
effective UI width is **not exactly 1080** — it's a bit more on a narrow phone, a bit
less on a wide one. That is exactly why Rule 2 exists: a stretched element fills whatever
the real width turns out to be; a fixed-width one leaves a wrong-sized gap. Do not change
the reference resolution or match without understanding that every tuned offset in the
codebase assumes 1080×1920.

---

## 3. Safe area

Unity exposes the device-safe region as `Screen.safeArea` (a `Rect` in **screen pixels**).
The React-Native equivalent people reach for is `react-native-safe-area-context`; this is
the same idea, built in. There are two supported ways to consume it — pick by how the UI
is structured.

### 3a. Preferred: `SafeAreaFitter` (container)

Attach `SafeAreaFitter` to a **full-screen** child of the canvas and parent your UI inside
it. The fitter drives that RectTransform's anchors to the safe-area fractions and zeroes its
offsets, so everything inside is automatically clear of cutouts. It re-applies whenever the
safe area or screen size changes (rotation, resize, foldables), so you build the UI once.

```csharp
var safe = CreateLayer(canvas.transform, "SafeAreaLayer");   // full-screen rect
safe.gameObject.AddComponent<SafeAreaFitter>();
// ...parent top bar / content / nav under `safe`. Background stays OUTSIDE it.
```

The main menu uses this: `MainMenuRuntime.EnsureRoot` puts the top status bar, the
swipeable chapter content, and the bottom nav inside one `SafeAreaLayer`; the background
art is a sibling *outside* it so it still bleeds full-screen behind the notch.

Caveats:
- The fitter's rect must fill the screen at rest — its anchors are read as screen fractions.
- **Backgrounds/art that should bleed behind the notch must not be inside a fitter.**

### 3b. Offset helpers (for hand-positioned elements)

When an element is positioned with explicit offsets (as the in-game HUD is), add the inset
from `RuntimeUiKit` to the relevant edge instead of wrapping it:

```csharp
RuntimeUiKit.SafeAreaTopInset(canvas)     // UI units, ready to add to a top offset
RuntimeUiKit.SafeAreaBottomInset(canvas)  // ...bottom
RuntimeUiKit.SafeAreaLeftInset(canvas) / SafeAreaRightInset(canvas)
RuntimeUiKit.SafeAreaInsets(canvas)       // Vector4 (left, right, top, bottom), UI units
RuntimeUiKit.SafeAreaInsetsPixels()       // same, raw screen pixels (canvas-independent)
```

Two things you **must** get right with the offset path:
- **Clamp is built in.** Each inset is clamped to `SafeAreaMaxInsetFraction` (10%) of the
  screen, because `Screen.safeArea` can momentarily report a degenerate rect on the first
  frame / in the simulator / mid-rotation. Without the clamp that shoves a bar a full screen
  inward and it vanishes. Real notches and indicators are well under 10%.
- **Re-apply on change.** `Screen.safeArea` and `canvas.scaleFactor` are only trustworthy
  after the first frame and can change later. Read them in `Update` against a cached
  screen-state key and re-apply when it differs (see `UIManager.Update`, `AbilityHud.Update`,
  `HoldButton.Update`). The fitter does this for you; the offset path must do it itself.

### 3c. Where it's applied today (audit)

| Surface | Edge | Handling |
|---|---|---|
| Main menu top status bar | top | `SafeAreaFitter` (menu safe layer) |
| Main menu bottom nav | bottom | `SafeAreaFitter` (menu safe layer) |
| Main menu chapter content | all | `SafeAreaFitter` (menu safe layer) |
| In-game top HUD bar + NEXT card | top | `SafeAreaTopInset` + `TopMarginBelowSafeArea`, re-applied in `UIManager.Update` |
| In-game hearts (lives) | bottom-left | `SafeAreaInsets`, re-applied in `UIManager.Update` |
| Ability/consumable slots | bottom-center | `SafeAreaBottomInset`, re-applied in `AbilityHud.Update`; gesture-exclusion rect extended to match |
| Hold (pocket cache) bubble | left | `SafeAreaLeftInset`, re-applied in `HoldButton.Update` |
| Menu background art | — | intentionally **full-screen**, bleeds behind the notch |

**Intentional exceptions (do not "fix" without thought):**
- **Nudge gesture pills** (`UIManager`) stay pinned to the bottom corners. They are the
  *visual* of a touch zone wired to `TouchGestureInput`, and the comment "the visual never
  lies about the hitbox" is load-bearing — moving the pill without moving the gesture zone
  would make the hint point at the wrong place. The home indicator is bottom-center; corner
  pills barely overlap it.
- **Centered modals** (pause, level complete, level summary) need no inset — they float in
  the middle. A top-pinned control *inside* a modal still does.
- **Banners at a height fraction** (instruction / win-countdown at 74% height) are mid-screen,
  not edge-pinned; safe area doesn't apply.

---

## 4. Width independence (percentages, not pixels)

For anything that should track the screen width, **stretch between anchors** and use
offsets only for fixed padding. Do not give it a fixed `sizeDelta.x`.

```csharp
// GOOD: fills the row on any width; padding is constant, width is whatever's left.
rect.anchorMin = new Vector2(0f, 1f);
rect.anchorMax = new Vector2(1f, 1f);          // stretch across the parent
rect.offsetMin = new Vector2(sideInset, ...);  // left/bottom padding
rect.offsetMax = new Vector2(-sideInset, ...); // right/top padding

// BAD: only correct at one width.
rect.sizeDelta = new Vector2(790f, ...);       // hard-coded width
```

The level cards (`MainMenuRuntime.BuildLevelCard`) are the reference: they stretch between
a single `LevelCardSideInset` per side, so the card width tracks the screen and an element
that must ride an edge (the action badge) is anchored to that edge `(1,1)`, not placed at a
fixed x. Heights and font sizes can stay fixed (they scale with the canvas); it's **widths
and edge distances** that must be fractional/stretched.

---

## 5. Checklist for new UI

- [ ] Pinned to the top, bottom, or a side? → inside a `SafeAreaFitter`, or add the matching
      `RuntimeUiKit.SafeArea*Inset` to its offset.
- [ ] Using the offset path? → re-applied in an `Update` on screen-state change, and relying
      on the built-in clamp.
- [ ] Should it fill the width? → stretch anchors + padding offsets, never a fixed `sizeDelta.x`.
- [ ] Has a coupled gesture/exclusion rect (slots, nudge, hold)? → move the rect with the
      visual so they never disagree.
- [ ] Background/full-bleed art? → keep it **outside** any fitter.
- [ ] Verified on a notched aspect (e.g. iPhone with Dynamic Island in the Device Simulator)
      **and** a clean 9:16, confirming no regression on the notchless case.
