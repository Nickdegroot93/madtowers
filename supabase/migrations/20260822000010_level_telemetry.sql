-- ---------------------------------------------------------------------------
-- Level-difficulty telemetry (2026-08-22). The runs ledger already answers most
-- tuning questions (attempts-to-first-pass, replay rate, durations, and
-- paid_progress = how far toward the goal each run got); what was missing is
-- WHY a run ended. One cheap column + a dashboard-only rollup view:
--   * runs.fail_cause — 'lives' | 'flood' | 'timeout' | 'abandon' | 'other',
--     null on wins. Set only inside finish_run; the client merely claims it,
--     and a nonsense claim degrades to null, never an error.
--   * level_stats — per-level difficulty rollup for Nick, readable ONLY from
--     the dashboard: this schema grants client roles explicitly (core.sql),
--     so no grant = players cannot query it (belt-and-braces revoke below,
--     since the view runs with owner rights and would otherwise cross RLS).
-- finish_run body is migration 5's verbatim except the two marked lines; the
-- fail_cause write rides the same runs UPDATE. Reply shape untouched.
-- ---------------------------------------------------------------------------

alter table public.runs
  add column if not exists fail_cause text
  check (fail_cause is null
         or fail_cause in ('lives', 'flood', 'timeout', 'abandon', 'other'));
comment on column public.runs.fail_cause is
  'Why the run ended, client-claimed, losses only (null on wins): lives topple-out, flood swallow, timeout, pause-menu abandon, or other.';

drop function public.finish_run(uuid, boolean, int, real, real);

create or replace function public.finish_run(p_run_id uuid, p_won boolean, p_score int, p_height real,
                                             p_progress real default null, p_fail_cause text default null)
returns jsonb
language plpgsql security definer set search_path = public
as $$
declare
  v_uid      uuid := auth.uid();
  v_run      public.runs%rowtype;
  v_row      public.attempts%rowtype;
  v_prior    public.scores%rowtype;
  v_gain     int;
  v_new_best boolean := false;
  v_prog     numeric;
  v_xp       int := 0;
  v_xp_total bigint := 0;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;

  select * into v_run from public.runs where run_id = p_run_id for update;
  if not found or v_run.user_id <> v_uid then
    return jsonb_build_object('accepted', false, 'reason', 'unknown_run');
  end if;
  if v_run.finished_at is not null then
    return jsonb_build_object('accepted', false, 'reason', 'already_finished');
  end if;
  if v_run.started_at < now() - interval '24 hours' then
    return jsonb_build_object('accepted', false, 'reason', 'expired');
  end if;
  if extract(epoch from (now() - v_run.started_at)) < 5 then
    return jsonb_build_object('accepted', false, 'reason', 'too_fast');
  end if;
  if p_score is null or p_score < 0 or p_score > 100000
     or p_height is null or p_height < 0 or p_height > 1000 then
    return jsonb_build_object('accepted', false, 'reason', 'implausible_result');
  end if;

  v_prog := least(2.0, greatest(0.0, coalesce(p_progress, 0.0)::numeric));

  update public.runs
     set finished_at = now(), won = coalesce(p_won, false), score = p_score,
         paid_progress = v_prog,
         -- telemetry (this migration): why the run ended - losses only, unknown -> null
         fail_cause = case when coalesce(p_won, false) then null
                           when p_fail_cause in ('lives', 'flood', 'timeout', 'abandon', 'other') then p_fail_cause
                           else null end
   where run_id = p_run_id;

  select * into v_row from public.attempts where user_id = v_uid for update;

  -- Regen accrues on BOTH outcomes before the reply: the run itself took wall time, and
  -- the client applies the returned count verbatim - an un-regenerated loss reply would
  -- clobber attempts the player actually has (review finding: false OUT-OF-ATTEMPTS).
  if not v_row.premium and v_row.count < 5 then
    v_gain := floor(extract(epoch from (now() - v_row.last_regen_at)) / 600)::int;
    if v_gain > 0 then
      v_row.count := least(5, v_row.count + v_gain);
      if v_row.count >= 5 then v_row.last_regen_at := now();
      else v_row.last_regen_at := v_row.last_regen_at + make_interval(secs => v_gain * 600);
      end if;
    end if;
  end if;

  -- loss-only lives: a win refunds the attempt (SHOP.md §7), enforced here
  if coalesce(p_won, false) and not v_row.premium and v_row.count < 5 then
    v_row.count := v_row.count + 1;
    if v_row.count >= 5 then v_row.last_regen_at := now(); end if;
  end if;

  update public.attempts
     set count = v_row.count, last_regen_at = v_row.last_regen_at, updated_at = now()
   where user_id = v_uid;

  select * into v_prior
    from public.scores
   where user_id = v_uid and level_id = v_run.level_id and board = v_run.board;
  if not found then
    v_new_best := (p_score > 0) or (p_height > 0);
  else
    v_new_best := (p_score > v_prior.best_score) or (p_height > v_prior.best_height);
  end if;

  insert into public.scores (user_id, level_id, board, best_score, best_height, loadout, achieved_at)
  values (v_uid, v_run.level_id, v_run.board, p_score, p_height,
          case when v_run.board = 'boosted' then v_run.loadout end, now())
  on conflict (user_id, level_id, board) do update
    set best_score  = greatest(public.scores.best_score, excluded.best_score),
        best_height = greatest(public.scores.best_height, excluded.best_height),
        achieved_at = case when excluded.best_score > public.scores.best_score
                           then excluded.achieved_at else public.scores.achieved_at end,
        loadout     = case when excluded.best_score > public.scores.best_score
                           then excluded.loadout else public.scores.loadout end;

  v_xp := round(public.xp_for_run(v_prog, coalesce(p_won, false)) * public.xp_multiplier())::int;
  if v_xp > 0 then
    update public.profiles
       set xp = xp + v_xp, updated_at = now()
     where user_id = v_uid
     returning xp into v_xp_total;
    v_xp_total := coalesce(v_xp_total, 0);
  else
    select coalesce(xp, 0) into v_xp_total from public.profiles where user_id = v_uid;
  end if;

  -- Key names are the client DTO contract (RunGate.FinishRunDto): accepted, reason,
  -- new_best, attempts, premium, seconds_until_next, xp_gained, xp_total.
  return jsonb_build_object(
    'accepted', true, 'new_best', v_new_best,
    'attempts', v_row.count, 'premium', v_row.premium,
    'seconds_until_next', public.secs_until_next(v_row.count, v_row.last_regen_at, v_row.premium),
    'xp_gained', v_xp, 'xp_total', v_xp_total);
end
$$;

comment on function public.finish_run(uuid, boolean, int, real, real, text) is
  'Closes a run: plausibility checks, win refund, sanity-bounded score upsert, XP award, fail-cause telemetry. The only leaderboard and XP write path.';
revoke all on function public.finish_run(uuid, boolean, int, real, real, text) from public, anon;
grant execute on function public.finish_run(uuid, boolean, int, real, real, text) to authenticated, service_role;

-- ---------------------------------------------------------------------------
-- level_stats: Nick's per-level difficulty rollup, dashboard-only. Reading it:
--   * median_attempts_to_first_win high = a wall; win_rate ~1.0 = filler.
--   * avg_loss_progress low = players die early (bad floor / impossible start);
--     near 1.0 = photo-finish losses (fair but tight).
--   * fail_abandon high = the "not fun" signal no other number gives.
--   * replay_rate = which levels people come back to after beating them.
-- ---------------------------------------------------------------------------

create view public.level_stats as
with finished as (
  select * from public.runs where finished_at is not null
),
first_wins as (
  select user_id, level_id, min(finished_at) as first_win_at
    from finished
   where won
   group by user_id, level_id
),
per_player as (
  select f.user_id, f.level_id, w.first_win_at,
         count(*) filter (where w.first_win_at is not null
                            and f.finished_at <= w.first_win_at) as attempts_to_first_win,
         count(*) filter (where w.first_win_at is not null
                            and f.finished_at > w.first_win_at)  as replays_after_win
    from finished f
    left join first_wins w using (user_id, level_id)
   group by f.user_id, f.level_id, w.first_win_at
),
player_agg as (
  select level_id,
         count(*)                                          as players,
         count(*) filter (where first_win_at is not null)  as winners,
         percentile_cont(0.5) within group (order by attempts_to_first_win)
           filter (where first_win_at is not null)         as median_attempts_to_first_win,
         count(*) filter (where replays_after_win > 0)     as replayers
    from per_player
   group by level_id
),
run_agg as (
  select level_id,
         count(*)                                                  as runs,
         round(avg(won::int)::numeric, 3)                          as win_rate,
         round(avg(paid_progress) filter (where not won)::numeric, 3) as avg_loss_progress,
         round((percentile_cont(0.5) within group
           (order by extract(epoch from finished_at - started_at)))::numeric, 1) as median_run_seconds,
         count(*) filter (where fail_cause = 'lives')   as fail_lives,
         count(*) filter (where fail_cause = 'flood')   as fail_flood,
         count(*) filter (where fail_cause = 'timeout') as fail_timeout,
         count(*) filter (where fail_cause = 'abandon') as fail_abandon
    from finished
   group by level_id
)
select r.level_id,
       p.players, p.winners, r.runs, r.win_rate,
       p.median_attempts_to_first_win,
       round(p.replayers::numeric / nullif(p.winners, 0), 3) as replay_rate,
       r.median_run_seconds, r.avg_loss_progress,
       r.fail_lives, r.fail_flood, r.fail_timeout, r.fail_abandon
  from run_agg r
  join player_agg p using (level_id)
 order by r.level_id;

comment on view public.level_stats is
  'Per-level difficulty rollup over the runs ledger. DASHBOARD-ONLY: never grant client roles - the view runs with owner rights and aggregates every player.';
revoke all on public.level_stats from public, anon, authenticated;
