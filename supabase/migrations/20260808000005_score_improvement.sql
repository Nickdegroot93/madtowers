-- ---------------------------------------------------------------------------
-- Post-victory scores reach the leaderboard (the "Keep Playing" fix).
--
-- The bug: a first win calls finish_run at the moment the goal verifies, which
-- consumes the run_id. The victory panel then invites the player to KEEP PLAYING
-- and promises "keep stacking to push your best score even higher" - but every
-- point earned after that was dropped. The local best climbed (ProgressStore) while
-- the leaderboard kept the victory number, so a player's own profile disagreed with
-- the board, and every casual player who wins once and never replays sat at exactly
-- the target score. A board full of ties at N is not a board.
--
-- Why not simply report later instead: XP.md's "Win timing (deliberate)" is right
-- that the attempt refund and the win must NOT wait for a session that may end
-- minutes later or never (app killed). That reasoning applies to the refund; it
-- does not apply to the score. So the two are split rather than reversed: the win
-- banks instantly as before, and the score becomes improvable afterwards.
--
-- Replays already worked - a level beaten before never re-arms the win flow
-- (LevelRuntimeController.cs:353), so the run ends at game over and reports its
-- full score. This closes the one-run-per-level gap that remained.
-- ---------------------------------------------------------------------------

-- What the run has already been PAID for, so an improvement pays only the delta.
-- Without this, a retry of the improvement (the client queue retries on failure)
-- would pay the overshoot again every time.
alter table public.runs add column if not exists paid_progress real not null default 0;
comment on column public.runs.paid_progress is
  'Goal progress already paid XP for. finish_run sets it; improve_run_score pays only the difference.';

-- Back-fill BEFORE improve_run_score exists, or every run already finished under the old
-- finish_run would read as paid_progress = 0 and could re-collect its entire award
-- (participation + progress, up to 50 XP x multiplier) on a single hand-made RPC call.
-- 2.0 is the maximum the formula recognises, so an improvement against a legacy run is
-- worth exactly nothing - the safe direction to be wrong in. Unfinished runs keep 0:
-- they have genuinely been paid nothing yet.
update public.runs set paid_progress = 2.0 where finished_at is not null;

-- One home for the award formula (constants mirrored in XpSystem.cs). It is now
-- evaluated from two places, and two copies would drift - the same argument that
-- moved the ad daily cap into ad_refill_daily_cap().
create or replace function public.xp_for_run(p_progress numeric, p_won boolean)
returns int
language sql immutable
as $$
  select case when p_progress > 0 then
              10                                                        -- participation
            + round(40 * least(1.0, p_progress))::int                   -- progress to goal
            + round(10 * (p_progress - least(1.0, p_progress)))::int    -- overshoot past it
         else 0 end
       + case when p_won then 25 else 0 end
$$;

revoke all on function public.xp_for_run(numeric, boolean) from public, anon, authenticated;

-- The multiplier read, also duplicated before now. Type-guarded: a malformed config
-- value must degrade to 1x rather than abort a transaction carrying a win refund.
create or replace function public.xp_multiplier()
returns numeric
language sql stable security definer set search_path = public
as $$
  select least(10.0, greatest(0.0, coalesce(
    (select case when jsonb_typeof(c.value) = 'number' then (c.value #>> '{}')::numeric end
       from public.backend_config c where c.key = 'xp_multiplier'), 1.0)))
$$;

revoke all on function public.xp_multiplier() from public, anon, authenticated;

-- ---------------------------------------------------------------------------
-- finish_run: unchanged behaviour, but now records paid_progress and shares the
-- award formula. Reply shape is untouched (RunGate.FinishRunDto).
-- ---------------------------------------------------------------------------

create or replace function public.finish_run(p_run_id uuid, p_won boolean, p_score int, p_height real, p_progress real default null)
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
         paid_progress = v_prog
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

-- ---------------------------------------------------------------------------
-- improve_run_score: raise the score/height/progress of an ALREADY FINISHED,
-- WON run - the post-victory Keep Playing session. Deliberately narrow:
--   * won runs only. A loss reported its final score already; there is no second
--     act to report, so accepting one would just be an extra forgery surface.
--   * raises only (greatest), so a replayed or reordered report can never lower
--     a board entry.
--   * never touches attempts. The refund happened at the win and must not repeat.
--   * pays only the XP DELTA above paid_progress, so the client queue's retries
--     are idempotent - the second identical call is worth exactly 0.
-- ---------------------------------------------------------------------------

create or replace function public.improve_run_score(p_run_id uuid, p_score int, p_height real, p_progress real default null)
returns jsonb
language plpgsql security definer set search_path = public
as $$
declare
  v_uid      uuid := auth.uid();
  v_run      public.runs%rowtype;
  v_prior    public.scores%rowtype;
  v_prog     numeric;
  v_new_best boolean := false;
  v_xp       int := 0;
  v_xp_total bigint := 0;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;

  select * into v_run from public.runs where run_id = p_run_id for update;
  if not found or v_run.user_id <> v_uid then
    return jsonb_build_object('accepted', false, 'reason', 'unknown_run');
  end if;
  if v_run.finished_at is null then
    -- Not finished yet: finish_run is the right call, and accepting here would skip
    -- the attempt refund the player is owed.
    return jsonb_build_object('accepted', false, 'reason', 'not_finished');
  end if;
  if not coalesce(v_run.won, false) then
    return jsonb_build_object('accepted', false, 'reason', 'not_won');
  end if;
  if v_run.started_at < now() - interval '24 hours' then
    return jsonb_build_object('accepted', false, 'reason', 'expired');
  end if;
  if p_score is null or p_score < 0 or p_score > 100000
     or p_height is null or p_height < 0 or p_height > 1000 then
    return jsonb_build_object('accepted', false, 'reason', 'implausible_result');
  end if;

  v_prog := greatest(v_run.paid_progress::numeric,
                     least(2.0, greatest(0.0, coalesce(p_progress, 0.0)::numeric)));

  -- Pay the difference between what this run has already been paid for and what it
  -- is now worth. A repeat of the same report leaves paid_progress unchanged and so
  -- pays nothing.
  v_xp := round((public.xp_for_run(v_prog, true) - public.xp_for_run(v_run.paid_progress::numeric, true))
                * public.xp_multiplier())::int;
  v_xp := greatest(0, v_xp);

  update public.runs
     set score = greatest(coalesce(score, 0), p_score),
         paid_progress = v_prog
   where run_id = p_run_id;

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

  if v_xp > 0 then
    update public.profiles
       set xp = xp + v_xp, updated_at = now()
     where user_id = v_uid
     returning xp into v_xp_total;
    v_xp_total := coalesce(v_xp_total, 0);
  else
    select coalesce(xp, 0) into v_xp_total from public.profiles where user_id = v_uid;
  end if;

  -- Same key names as finish_run so the client can share a DTO.
  return jsonb_build_object(
    'accepted', true, 'new_best', v_new_best,
    'xp_gained', v_xp, 'xp_total', v_xp_total);
end
$$;

revoke all on function public.improve_run_score(uuid, int, real, real) from public, anon;
grant execute on function public.improve_run_score(uuid, int, real, real) to authenticated, service_role;

comment on function public.improve_run_score(uuid, int, real, real) is
  'Post-victory Keep Playing score. Raises only; pays the XP delta above runs.paid_progress.';
