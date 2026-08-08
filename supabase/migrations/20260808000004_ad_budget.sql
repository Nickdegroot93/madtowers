-- ---------------------------------------------------------------------------
-- Ad refill budget, mirrored to the client (SHOP.md §7.3 item 6).
--
-- The 3/day refill cap has been invisible to the client until it was already too
-- late: the player watched a full rewarded video, THEN grant_ad_refill said
-- "rate_limited", and only then did the button hide. That is a wasted watch and
-- reads as the game taking something. Surfacing the remaining budget lets the
-- button hide BEFORE the watch instead of after it.
--
-- Nothing here changes who may grant attempts. The cap is still enforced entirely
-- server-side inside grant_ad_refill; this only lets the client render the truth.
-- ---------------------------------------------------------------------------

-- The cap was an inline `>= 3` inside grant_ad_refill. Now that two functions need
-- it, it gets one home - a client showing a different number than the server
-- enforces is exactly the bug this whole change is about.
create or replace function public.ad_refill_daily_cap()
returns int language sql immutable as $$ select 3 $$;

-- Rolling 24h window, matching grant_ad_refill's own check exactly.
-- Definer + revoked from clients: ad_grants is server-internal (no RLS policies,
-- no client grants), and this must not become a way to read another user's ledger.
create or replace function public.ad_grants_remaining(p_uid uuid)
returns int
language sql
security definer
set search_path = public
stable
as $$
  select greatest(0, public.ad_refill_daily_cap() - (
    select count(*)::int
      from public.ad_grants
     where user_id = p_uid
       and created_at > now() - interval '24 hours'))
$$;

revoke all on function public.ad_refill_daily_cap() from public, anon, authenticated;
revoke all on function public.ad_grants_remaining(uuid) from public, anon, authenticated;

-- ---------------------------------------------------------------------------
-- get_profile: carries the remaining budget on the boot read, so the button is
-- correct from the first frame rather than after the first denial.
-- Shape contract (OnlineService.ProfileDto): display_name, is_linked, xp,
-- ad_grants_remaining. JSON key names are a client contract - never rename.
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
    'display_name',        p.display_name,
    'is_linked',           p.is_linked,
    'xp',                  p.xp,
    'ad_grants_remaining', public.ad_grants_remaining(auth.uid())
  )
  into v_result
  from public.profiles p
  where p.user_id = auth.uid();

  if v_result is null then
    -- Trigger should have created the row at signup; self-heal if it is missing.
    insert into public.profiles (user_id, display_name, is_linked)
    values (auth.uid(), 'Builder-' || lpad((abs(hashtext(auth.uid()::text)) % 10000)::text, 4, '0'), false)
    on conflict (user_id) do nothing;

    select jsonb_build_object(
      'display_name',        p.display_name,
      'is_linked',           p.is_linked,
      'xp',                  p.xp,
      'ad_grants_remaining', public.ad_grants_remaining(auth.uid())
    )
    into v_result
    from public.profiles p
    where p.user_id = auth.uid();
  end if;

  return v_result;
end;
$$;

-- ---------------------------------------------------------------------------
-- get_attempts: carries the budget too, and this is what lets the mirror HEAL.
-- Without it the client could latch a stale zero forever: budget hits 0 → button
-- hides → no grant_ad_refill is ever sent → nothing refreshes the figure, even
-- after the oldest grant ages out of the rolling window and the server would
-- happily allow another refill. get_attempts is already the debounced refresh run
-- on focus regain (AttemptsSync.Refresh), so the budget rides along for free.
-- Shape contract (AttemptsSync.AttemptsDto): count, premium, seconds_until_next,
-- meter_charged, grants_remaining. JSON key names are a client contract.
-- ---------------------------------------------------------------------------

create or replace function public.get_attempts()
returns jsonb
language plpgsql stable security definer set search_path = public
as $$
declare
  v_uid     uuid := auth.uid();
  v_row     public.attempts%rowtype;
  v_eff     int;
  v_charged boolean;
  v_grants  int;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  v_charged := public.attempts_meter_charged(v_uid);
  v_grants  := public.ad_grants_remaining(v_uid);
  select * into v_row from public.attempts where user_id = v_uid;
  if not found then
    return jsonb_build_object('count', 5, 'premium', false, 'seconds_until_next', 0,
                              'meter_charged', v_charged, 'grants_remaining', v_grants);
  end if;
  if v_row.premium then
    return jsonb_build_object('count', v_row.count, 'premium', true, 'seconds_until_next', 0,
                              'meter_charged', v_charged, 'grants_remaining', v_grants);
  end if;
  v_eff := least(5, v_row.count + greatest(0, floor(extract(epoch from (now() - v_row.last_regen_at)) / 600)::int));
  return jsonb_build_object(
    'count', v_eff, 'premium', false,
    'seconds_until_next', public.secs_until_next(v_eff, v_row.last_regen_at, false),
    'meter_charged', v_charged, 'grants_remaining', v_grants);
end
$$;

-- ---------------------------------------------------------------------------
-- grant_ad_refill: unchanged logic, plus grants_remaining on EVERY return path.
-- The success path reports the budget AFTER the ledger insert, so a client that
-- just spent its last grant hides the button immediately instead of one watch late.
-- PRODUCTION NOTE (unchanged): this client-claimed path MUST be replaced by AdMob
-- SSV before launch - BACKEND.md §6.4.
-- ---------------------------------------------------------------------------

create or replace function public.grant_ad_refill()
returns jsonb
language plpgsql security definer set search_path = public
as $$
declare
  v_uid  uuid := auth.uid();
  v_row  public.attempts%rowtype;
  v_gain int;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  -- Lock BEFORE the rate-limit count: the FOR UPDATE on attempts serializes this
  -- function per user, so concurrent calls can't all pass the daily-cap check
  -- (review finding: check-then-insert race).
  select * into v_row from public.attempts where user_id = v_uid for update;
  -- Every branch reports the REAL budget. A literal 0 here would conflate "this does
  -- not apply to you" with "you are capped", and the client latches the figure it is
  -- given: a transiently missing attempts row would zero the mirror for the whole
  -- session even after the row came back. Only rate_limited is genuinely 0.
  if not found then
    return jsonb_build_object('ok', false, 'reason', 'no_meter',
                              'grants_remaining', public.ad_grants_remaining(v_uid));
  end if;
  if public.ad_grants_remaining(v_uid) <= 0 then
    return jsonb_build_object('ok', false, 'reason', 'rate_limited', 'grants_remaining', 0);
  end if;
  if v_row.premium then
    return jsonb_build_object('ok', false, 'reason', 'premium',
                              'grants_remaining', public.ad_grants_remaining(v_uid));
  end if;
  if v_row.count < 5 then
    v_gain := floor(extract(epoch from (now() - v_row.last_regen_at)) / 600)::int;
    if v_gain > 0 then
      v_row.count := least(5, v_row.count + v_gain);
      if v_row.count >= 5 then v_row.last_regen_at := now();
      else v_row.last_regen_at := v_row.last_regen_at + make_interval(secs => v_gain * 600);
      end if;
    end if;
  end if;
  if v_row.count >= 5 then
    -- attempts_full does NOT consume budget, so report what is really left: this
    -- branch heals (the next spent attempt makes room) and must not read as a cap.
    return jsonb_build_object('ok', false, 'reason', 'attempts_full',
                              'attempts', v_row.count,
                              'grants_remaining', public.ad_grants_remaining(v_uid));
  end if;
  v_row.count := least(5, v_row.count + 2);
  if v_row.count >= 5 then v_row.last_regen_at := now(); end if;
  update public.attempts
     set count = v_row.count, last_regen_at = v_row.last_regen_at, updated_at = now()
   where user_id = v_uid;
  insert into public.ad_grants (user_id) values (v_uid);
  return jsonb_build_object(
    'ok', true, 'attempts', v_row.count, 'premium', false,
    'seconds_until_next', public.secs_until_next(v_row.count, v_row.last_regen_at, false),
    'grants_remaining', public.ad_grants_remaining(v_uid));
end
$$;
