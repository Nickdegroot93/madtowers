# XP.md — the account XP / level system

How the top-bar level badge and XP bar work: what earns XP, the level curve, where the
number lives, and the security model. Server side rides the BACKEND.md run handshake;
client constants live in `XpSystem.cs` and are MIRRORED in
`supabase/migrations/20260801000003_xp.sql` — change one, change both.

> **Status: BUILT 2026-08-01 (Nick approved the design same day). The level is
> PRESENTATION ONLY — it says "how much you've played" and nothing reads it for gameplay.
> It exists so accounts carry a correct level when online play arrives.**

## 1. The award (per finished campaign run)

Every run that ENDS — loss, pause-menu quit/restart, or win — reports once and pays:

| Component | XP | Rule |
|---|---|---|
| Participation | 10 | any run whose goal progress moved at all |
| Progress | 0–40 | linear in progress toward the level goal, capped at the target |
| Overshoot | 0–10 | progress past the goal, capped at 2× target |
| Win | +25 | every win, repeatable — no first-clear gating |

Progress is the run's **peak** unclamped `WinCondition.RunProgressRaw` (a collapse never
erases what the run reached): blocks-score/target, height/target, peak-standing/wave
target; Endless measures against a fixed 25-block reference. Custom Game runs have no
level identity and never earn. Examples: lose at 80/100 blocks = 42 XP; win = 75 XP;
a replay of a completed level ending at 120/100 = 52 XP.

**Win timing (deliberate):** a first win awards and reports at the moment the goal
VERIFIES — progress 1.0, so the win itself never carries overshoot — because the attempt
refund must not wait for a post-win Keep Playing session that may end minutes later (or
never, if the app is killed). Overshoot pays on runs that END past the target: replays of
completed levels (no win flow arms) and losses/quits after the goal was met but before
verification.

**Amended 2026-08-08 — the SCORE is no longer frozen at that moment, only the refund.**
The original rule bundled the score into the same decision, and the consequence was that
everything stacked during Keep Playing was dropped: the local best climbed while the
leaderboard kept the victory number, so a player's own profile contradicted the board and
every casual winner sat at exactly the target score. A board full of ties at N is not a
board. So the two are split: `finish_run` still banks the refund and the win award
instantly, and `improve_run_score` (migration `20260808000005`) raises the score
afterwards and pays the **overshoot delta** above `runs.paid_progress` — a first win then
a 2.0-progress Keep Playing session pays 75 then +10, exactly what one run ending at 2.0
would have paid. Retries are worth 0 by construction. `AwardRunXp` +
`LevelRuntimeController.AwardOvershootXp` mirror the same split locally so an
online-layer-disabled build does not quietly pay less for the identical action.

- **Quit pays** (Nick 2026-08-01): the pause menu's quit/restart call
  `LevelRuntimeController.ReportAbandonedRun()`, which banks local bests and reports the
  finish exactly like a loss at that point — score to the board, XP paid. Abandoning at
  80 blocks and toppling at 80 blocks are the same effort.
- **No time-played XP**, deliberately: it pays idling and is the first thing farmed.
  Per-run rewards correlate with time played without the exploit.

## 2. The level curve (no cap)

`Need(L) = 60 + 15·(L−1)` XP from level L to L+1; everyone starts at level 1. Linear
increments (quadratic total) on purpose — exponential curves explode without a level cap.
1→2 is one good run; 10→11 ≈ 4 runs; 50→51 ≈ 16; 99→100 ≈ 30. All math is closed-form in
`XpSystem` (`XpToReachLevel` / `LevelForXp` / `Fraction01`).

## 3. Where the number lives (BACKEND.md security model)

- **Online (the standard): `profiles.xp` is the authority**, written ONLY inside
  `finish_run` (security definer; same trust envelope as scores — the run must exist,
  belong to the caller, be unfinished, ≥5s old; progress is clamped to [0,2] server-side
  whatever the client claims). RLS has no client write policies and table write
  privileges are revoked: a player cannot UPDATE their own XP row (smoke check g3).
- **Client cache:** `ProgressStore.xpEarned` holds the server's last total (LWW
  overwrite, like the attempts fields), refreshed by every accepted `finish_run` reply
  (`xp_total`) and the boot `get_profile`. `merge_progress` strips `xpEarned` from pushed
  save payloads so a forged value never merges across devices.
- **Online layer disabled** (dev/editor): the same field is the local accumulator —
  `XpSystem.ReportLocalRun` applies the same formula at 1×.
- **Premium-offline runs earn nothing**, same rule as leaderboards (unranked): a local
  grant would visibly rewind on the next server verdict.

## 4. The multiplier (boost weekends — parked until levels matter)

`backend_config.xp_multiplier` scales the whole award server-side (clamped to [0,10] so a
config typo can't mint millions). No client grants on that table; changing it is a
service-role data edit:

```sql
update public.backend_config set value = '2.0' where key = 'xp_multiplier';  -- 2x weekend
update public.backend_config set value = '1.0' where key = 'xp_multiplier';  -- back to normal
```

The client neither knows nor needs the multiplier (totals come back from the server);
a "2× XP WEEKEND" banner would be new UI when the feature actually ships.

## 5. Display

Top bar: hex badge = `XpSystem.Level`, bar fill = `XpSystem.Fraction01`
(`PlayerProfileStore.Snapshot` carries both; `TopBarLive` re-renders on
`XpSystem.Changed`). Nothing else reads the level today.

## 6. Open items (revisit when levels start to matter)

- Level-up moment (toast/celebration) — nothing is shown today; JUICE.md governs.
- Per-level XP farming bounds (trivial-level replay) — cosmetic today, so accepted;
  tighten alongside the per-level score-bounds table (BACKEND.md §11).
- Online-play XP sources (multiplayer results, events) — new components in `finish_run`
  or sibling RPCs, same authority model.
- The 2× banner UI + scheduling when boost weekends become real.
