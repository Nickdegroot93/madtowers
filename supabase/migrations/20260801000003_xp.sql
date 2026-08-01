-- MadTowers account XP — server-authoritative accrual (XP.md; design approved 2026-08-01).
-- Migration #3. Client mirror of the award constants: Assets/SourceFiles/Scripts/Core/XpSystem.cs
-- — keep the two in sync, the numbers are a cross-layer contract.
--
-- Award per finished campaign run (all outcomes — loss, quit and win report through
-- finish_run): participation 10 (any run whose progress moved at all), progress toward the
-- level goal 0..40 linear (capped at the target), overshoot past the goal 0..10 (capped at
-- 2x target), win bonus +25. The whole award is scaled by the xp_multiplier config row
-- (boost weekends). The level curve — Need(L) = 60 + 15*(L-1), no cap — is presentation
-- and lives client-side only; the server stores nothing but the lifetime XP total.
--
-- Security (BACKEND.md §4.3): profiles.xp is written ONLY inside finish_run (security
-- definer). profiles has no client write policies and write privileges are revoked
-- (migration #1), so a player cannot update their own XP row; the multiplier lives in
-- backend_config, which has no client grants at all. Boost weekend = a service-role edit:
--   update public.backend_config set value = '2.0' where key = 'xp_multiplier';

alter table public.profiles add column xp bigint not null default 0;
comment on column public.profiles.xp is 'Lifetime XP. Written only by finish_run(); the account level derives from it client-side (XpSystem).';

insert into public.backend_config (key, value) values ('xp_multiplier', '1.0')
on conflict (key) do nothing;

-- ---------------------------------------------------------------------------
-- finish_run gains p_progress (unclamped goal progress; 1 = at target) and pays
-- XP inside the same exchange. The 4-arg overload is dropped, not kept: two
-- overloads would make PostgREST rpc dispatch ambiguous.
-- ---------------------------------------------------------------------------

drop function public.finish_run(uuid, boolean, int, real);

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
  v_mult     numeric;
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

  update public.runs
     set finished_at = now(), won = coalesce(p_won, false), score = p_score
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

  -- XP award (constants mirrored in XpSystem.cs). Progress is clamped server-side to
  -- [0, 2] whatever the client claims; the run-plausibility gates above (ownership,
  -- once-only, >=5s, sane score) already bound how often this can be farmed.
  v_prog := least(2.0, greatest(0.0, coalesce(p_progress, 0.0)::numeric));
  if v_prog > 0 then
    v_xp := 10                                             -- participation
          + round(40 * least(1.0, v_prog))::int            -- progress toward the goal
          + round(10 * (v_prog - least(1.0, v_prog)))::int; -- overshoot past it
  end if;
  if coalesce(p_won, false) then v_xp := v_xp + 25; end if;
  -- Type-guarded read: a malformed config value (string/object) must degrade to 1x, not
  -- abort the whole finish - the transaction also carries the win refund and the score.
  v_mult := coalesce((select case when jsonb_typeof(c.value) = 'number'
                                  then (c.value #>> '{}')::numeric end
                        from public.backend_config c
                       where c.key = 'xp_multiplier'), 1.0);
  v_mult := least(10.0, greatest(0.0, v_mult));            -- a config typo must not mint millions
  v_xp := round(v_xp * v_mult)::int;
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
comment on function public.finish_run(uuid, boolean, int, real, real) is 'Closes a run: plausibility checks, win refund, sanity-bounded score upsert, XP award. The only leaderboard and XP write path.';

revoke all on function public.finish_run(uuid, boolean, int, real, real) from public, anon;
grant execute on function public.finish_run(uuid, boolean, int, real, real) to authenticated, service_role;

-- ---------------------------------------------------------------------------
-- get_profile now carries the XP total (client boot read; cross-device display).
-- Shape contract (OnlineService.ProfileDto): display_name, is_linked, xp.
-- ---------------------------------------------------------------------------

create or replace function public.get_profile()
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_result jsonb;
begin
  select jsonb_build_object(
    'display_name', p.display_name,
    'is_linked',    p.is_linked,
    'xp',           p.xp
  )
  into v_result
  from public.profiles p
  where p.user_id = auth.uid();

  if v_result is null then
    -- Trigger should have created the row at signup; self-heal if it is missing.
    insert into public.profiles (user_id, display_name, is_linked)
    values (auth.uid(), 'Builder-' || lpad((abs(hashtext(auth.uid()::text)) % 10000)::text, 4, '0'), false)
    on conflict (user_id) do nothing;

    select jsonb_build_object('display_name', p.display_name, 'is_linked', p.is_linked, 'xp', p.xp)
    into v_result
    from public.profiles p
    where p.user_id = auth.uid();
  end if;

  return v_result;
end;
$$;

-- ---------------------------------------------------------------------------
-- merge_progress: xpEarned joins the server-owned strip list. The local save
-- field is a display cache of profiles.xp (or the offline accumulator when the
-- online layer is disabled) - a forged copy pushed in a save document must not
-- merge back to other devices, exactly like the attempts fields.
-- ---------------------------------------------------------------------------

create or replace function public.merge_progress(p_payload jsonb, p_schema_version int)
returns jsonb
language plpgsql security definer set search_path = public
as $$
declare
  v_uid    uuid := auth.uid();
  v_stored jsonb;
  v_merged jsonb;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  -- Abuse guard: a real payload is a few KB (review finding: unbounded jsonb is a
  -- storage/CPU DoS — merge_bests is O(n^2) in the bests array). Reject absurdity;
  -- the client treats the 400 as a failed push and keeps its local save untouched.
  if pg_column_size(p_payload) > 131072 then
    raise exception 'payload_too_large';
  end if;
  if jsonb_typeof(p_payload -> 'bests') = 'array'
     and jsonb_array_length(p_payload -> 'bests') > 2000 then
    raise exception 'payload_too_large';
  end if;
  -- Server-owned state never round-trips through the payload (DATA.md scope carve-out):
  -- the attempts meter, premium and XP live in server tables; forged copies in a pushed
  -- save must not merge back to other devices (review finding).
  p_payload := coalesce(p_payload, '{}'::jsonb)
               - 'attemptsCount' - 'attemptsUpdatedAtUnixUtc' - 'premiumUnlocked' - 'xpEarned';
  insert into public.progress (user_id) values (v_uid) on conflict do nothing;
  select payload into v_stored from public.progress where user_id = v_uid for update;
  v_merged := public.merge_payload(v_stored, p_payload);
  -- The per-call input cap can still be accreted past over many pushes with disjoint
  -- keys; bound the stored document too so the account can't become a CPU/storage bomb.
  if pg_column_size(v_merged) > 262144 then
    raise exception 'payload_too_large';
  end if;
  update public.progress
     set payload        = v_merged,
         schema_version = greatest(schema_version, coalesce(p_schema_version, 1)),
         updated_at     = now()
   where user_id = v_uid;
  return v_merged;
end
$$;
