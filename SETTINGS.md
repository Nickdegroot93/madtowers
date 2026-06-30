# SETTINGS.md — Settings screen design spec

**Status:** binding. The **skeleton + Sound & Haptics + Graphics tabs are
implemented** — `MainMenuRuntime.Settings.cs` builds the chapter-themed rail and
panels (wired into `BuildMenu` under `MenuTab.Settings`), `SettingsService`
persists settings. Sound has Music / SFX sliders + Mute-all; Graphics has frame
rate / visual effects / screen shake (enforced via central gates — see
[GRAPHICS.md](GRAPHICS.md)). Controls + Notifications/Account/About still show the
empty placeholder (§4). Pairs with [RESPONSIVE.md](RESPONSIVE.md) for layout and
with the chapter theming already in `MainMenuRuntime`.

This doc covers **structure + theming + the tab set**. Individual per-tab
controls are deliberately deferred (see §4).

---

## 1. Core principle — structure is chapter-agnostic, skin is chapter-driven

The settings screen has **one fixed structure** — same tabs, same layout — on
every chapter. What changes per chapter is only:

- **Background** — the active chapter's `MenuBackgroundImage` /
  `MenuBackgroundVideo`, i.e. the same backdrop the rest of the menu already
  shows. Reuse `BuildBackground(parent, chapter)`; do **not** add a second
  background system.
- **Accent colors** — every tinted element derives from the active chapter's
  two accent colors.

> The jungle greens in the first mock are **not** a settings theme — they're
> just because Jungle was the active chapter. Never hard-code any chapter's
> palette into the settings UI.

---

## 2. Theming contract (chapter color → UI role)

**Active chapter source:** in-menu it's `_chapters[_chapterIndex]`
(`MainMenuRuntime`). Resolve colors exactly as the nav and top bar already do,
reusing the existing derive helpers so settings matches the rest of the menu:

```csharp
// MainMenuRuntime.cs
Color ChapterLight(chapter) = Lerp(chapter.MenuAccentColor, TextPrimary, 0.46f);   // readable light tint
Color ChapterDark(chapter)  = Lerp(chapter.MenuAccentSecondaryColor, chapter.MenuAccentColor, 0.22f); // mid tone
```

Raw fields on `ChapterDefinition`: `MenuAccentColor` (light), `MenuAccentSecondaryColor` (dark), `MenuPanelColor` (glass fill).

| UI role | Color |
|---|---|
| Selected tab outline + glow | raw `MenuAccentColor` (light) |
| Selected tab icon / label | `ChapterLight()` |
| Inactive tab icon / label | muted neutral (match nav inactive gray) |
| Slider track fill + handle | raw `MenuAccentColor` (light) |
| Toggle ON | raw `MenuAccentColor`; **OFF** = neutral gray |
| Panel / card fill | `MenuPanelColor` (translucent glass) |
| Panel border / divider | `MenuAccentSecondaryColor` at low alpha |
| Section-header accent icon | `MenuAccentColor` |
| Body / muted text | **fixed** near-white / muted — *not* chapter-tinted |

**Rule:** chapter color is reserved for **interactive + selected** affordances
(outline, fill, handle, ON state). Keep body copy high-contrast neutral —
readability has to hold across every chapter backdrop.

**Reference values** (real, from `/Assets/Resources/Chapters/*.asset`):

| Chapter | Light (`MenuAccentColor`) | Dark (`MenuAccentSecondaryColor`) |
|---|---|---|
| Jungle Depths | `#75F261` | `#1F6B38` |
| Training Wheels | `#8FDBFF` | `#2E5975` |
| Barren Lands | `#D99E57` | `#9E5C21` |

---

## 3. Tab rail

Left vertical rail — icon + label per tab. Mirrors the bottom-nav button
pattern (`BuildNavButton`, `MainMenuRuntime.Nav.cs`: fractional slots, active
tint). Selected tab gets the light-accent outline + glow + edge notch.

Final tabs, top → bottom (**6**):

1. **UI / Controls**
2. **Graphics**
3. **Sound & Haptics**
4. **Notifications**
5. **Account**
6. **About / Legal**

**Decisions baked in:**

- **Sound + Haptics merged.** Vibration *is* haptics; both are feedback
  channels. One tab.
- **No standalone Accessibility tab in v1.** Its only shippable items today
  (reduce-effects, screen-shake) live under **Graphics**. Promote Accessibility
  to its own tab the day we actually build text scaling / colorblind /
  high-contrast — not before, or it reads as empty.
- **Language lives under Account**, behind an "if we localize" flag.
- **Gameplay tab removed** (was between Sound and Notifications). It was too thin
  for MVP; its candidate prefs (placement guide, confirm-consumable) are
  controls-adjacent and fold into **UI / Controls** instead.

---

## 4. Per-tab candidate content — DEFERRED (not building yet)

Listed so we can judge whether each tab earns its place. Tagged by readiness:
**now** = buildable today · **infra** = needs a system we haven't built.

| Tab | Candidate rows |
|---|---|
| **UI / Controls** | nudge buttons on/off **(now)** · consumable slot position (L/R) & size **(now)** · handedness preset = mirror whole layout **(now)** · HUD element visibility **(now)** |
| **Graphics** | ✅ **implemented:** frame-rate cap (30/60/120) · visual effects (bloom/post + prefab VFX) · screen shake. Enforced via central gates — see [GRAPHICS.md](GRAPHICS.md). Quality preset / render-scale can layer in later. |
| **Sound & Haptics** | ✅ **implemented:** music volume · SFX volume · mute all. Vibration on/off (+ intensity) deferred — no haptics layer yet. |
| **Notifications** | daily-reward / lives-refilled / events push toggles — **infra** (push, Phase E backend) |
| **Account** | sign in · cloud save · linked accounts · restore purchases · language — **infra** (Supabase, Phase E); greyed placeholder until then |
| **About / Legal** | version + build · privacy policy · terms · support / contact · rate the app · credits — **now**, store-required, static |

---

## 5. Layout & responsive — binding, see [RESPONSIVE.md](RESPONSIVE.md)

- Settings content mounts in the menu **ContentLayer**, inside the
  **SafeAreaLayer** (which already carries `SafeAreaFitter`). Background is a
  sibling of SafeAreaLayer so it bleeds behind the notch — reuse it.
- Canvas reference `1080×1920`, `matchWidthOrHeight = 0.5`.
- Tab rail and content panel **stretch by anchors/offsets**; never hard-code
  widths (RESPONSIVE rule 2). Heights and font sizes may be fixed — the canvas
  scaler handles those.
- Never pin to raw screen edges — the `SafeAreaFitter` owns the insets.
- Content panel scrolls internally when rows overflow
  (`RuntimeUiKit.CreateScrollColumn`); the rail stays fixed.

---

## 6. Implementation notes / known gaps

- **Settings persistence:** `SettingsService` (PlayerPrefs-backed, raises
  `Changed`) is the single source of truth. Each setting is a property; writes
  clamp, persist, and notify. `Save()` flushes to disk (also auto-saved by Unity
  on quit/pause). Add future tabs' keys here. `CustomGameSettings` is a separate
  dev/Custom-Game tool — unrelated.
- **Audio apply model (no AudioMixer):** `SettingsService` exposes
  `EffectiveMusic` / `EffectiveSfx` (per-channel level, 0 while muted).
  `MusicPlayer` sets `source.volume = BaseVolume × EffectiveMusic` and subscribes
  to `Changed` to update **live** while dragging. `SfxPlayer` multiplies each
  one-shot by `EffectiveSfx` (new sounds pick up changes immediately; a running
  SFX loop applies at start only). An `AudioMixer` would be the textbook route but
  needs an authored mixer asset and cuts against this code-first project; revisit
  if we add buses/DSP effects.
- **Controls:** `RuntimeUiKit.CreateSlider` (themed 0..1 slider) and
  `RuntimeUiKit.CreatePillToggle` (themed sliding toggle) are accent-themed kit
  primitives reused by the Sound rows and available to every future tab; rows are
  composed with the reusable `BuildPanelHeader` / `BuildRowLabel` /
  `NewSettingsRow` builders. `RuntimeUiKit.CreateSegmentedControl` (single-choice,
  used by Graphics → Frame Rate) now fills the option-row gap. Legacy
  `CreateToggleRow` / `CreateStepperRow` / `CreateCycleRow` (`RuntimeUiKit.Legacy.cs`)
  remain for non-themed dev UI.
- **No global font scale ("rem") yet** — investigated, deferred. Every text size
  is a hard-coded literal at its call site; there is no single typography knob.
  Adding one is clean: a static `RuntimeUiKit.FontScale` multiplied into the
  factories (`CreateTmp` / `CreateText` / `CreateLabel`, which ~67 text call sites
  funnel through) scales all kit-created text at once. Caveats: (a) box-fit text
  (ability cards, some HUD) must opt out via a `scalable = false` param —
  `AbilityCardView` already bypasses the kit so it's naturally excluded; (b) ~4
  sites create text directly (`UIManager` HUD, `LevelRuntimeController` countdown,
  `HeightLimitWavesModifier`) and would need routing through the kit; (c)
  fixed-height / `NoWrap` boxes can clip above ~12%, so a global bump needs a
  per-screen visual pass. This is the mechanism a future **Text Size**
  accessibility setting (§3) would drive. Note the `CanvasScaler`
  (`ScaleWithScreenSize`, 1080×1920) is **device-fit, not** a font-only lever.
- **No chapter-changed event** in `GameEvents`. The menu rebuilds on tab switch,
  so settings reads the active chapter at build time — fine. If we ever
  live-swap chapter while settings is open, add `GameEvents.ChapterChanged`.
- **Mount point:** `MenuTab.Settings` now routes to `BuildSettingsScreen()`
  (`MainMenuRuntime.Settings.cs`); the `BuildDummyScreen()` "COMING SOON" path is
  retained only for Shop / Chapters / Vault. The editor-only **Custom Game**
  entry moved into the settings panel footer (shown when `ContentCatalog.IsAvailable`).
- **Graphics gates:** frame rate → `GraphicsSettingsApplier`; post-processing →
  `PostFxController`; decorative VFX → `Vfx.Spawn`; screen shake →
  `TowerCameraController.Impact`. All future effects inherit the toggles for free
  if they route through these. Full contract in [GRAPHICS.md](GRAPHICS.md).
- **Rail icons** are procedural placeholders in `MenuSprites` (`Sliders`,
  `Monitor`, `Equalizer`, `Bell`, `Person`, `Info`; rows use `Note` / `Speaker` /
  `SpeakerOff` / `Sparkle` / `Shake`) — final per-tab art is a design pass (§7).
- **Per-tab dispatch:** `BuildSettingsPanelContent` currently branches
  `if Sound … else if Graphics … else empty`. Fine at two content tabs; when a
  third lands, move the content builder onto the `SettingsTabInfo` tuple (one
  declaration per tab) instead of growing the if-chain.

---

## 7. Open decisions

- **Restore purchases** — hide the row until IAP actually ships.
- Rail at 6 tabs: vertical icon+label fits a tall screen; confirm against the
  smallest target device before locking.
- Frame-rate `120` is offered on all devices (OS caps it on 60 Hz screens);
  add refresh-rate detection if we want to hide unsupported options.
