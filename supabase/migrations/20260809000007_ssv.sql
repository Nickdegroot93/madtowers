-- ---------------------------------------------------------------------------
-- AdMob server-side verification (SSV) — BACKEND.md §6.4, SHOP.md §7.3 item 5.
--
-- Until now the client CLAIMED it had watched an ad: grant_ad_refill checked who
-- was asking and how often, but nothing proved an ad existed, because the ad plays
-- between the device and Google and our server never sees it. Anyone who proxied
-- the app's HTTPS could mint the daily cap in free lives without watching anything.
--
-- SSV moves the claim to Google: their servers call OUR endpoint with a signed
-- message ("user X completed a rewarded ad"), verified against Google's public
-- keys. Forging it means forging the signature.
--
-- Rollout is config-flipped, not deploy-gated: grant_ad_refill keeps working until
-- backend_config.ssv_enabled is set true, so SSV can be proven on a real device
-- before the client-claimed path is closed. One row to flip, one row to revert.
-- ---------------------------------------------------------------------------

-- The AdMob transaction id, so a replayed callback grants once. Nullable: rows
-- written by the legacy client-claimed path have no transaction.
alter table public.ad_grants add column if not exists transaction_id text;

-- NOT a partial index: distinct NULLs never conflict in a plain unique index, so the
-- legacy client-claimed rows coexist fine - and a partial index cannot serve as an
-- ON CONFLICT arbiter without repeating its predicate at every call site.
create unique index if not exists ad_grants_transaction_idx
  on public.ad_grants (transaction_id);

comment on column public.ad_grants.transaction_id is
  'AdMob SSV transaction_id. Unique when present - a replayed callback must grant once.';

insert into public.backend_config (key, value)
values ('ssv_enabled', 'false'::jsonb)
on conflict (key) do nothing;

comment on table public.backend_config is
  'Server-tunable knobs. xp_multiplier (number); ssv_enabled (bool) - when true the client-claimed grant_ad_refill is refused and only Google-verified SSV callbacks pay out.';

-- backend_config had no table grants at all, so the "one row to flip" was not
-- actually flippable except through a direct psql session. service_role is
-- server-side only (it must never ship in a client), so this is the operator path.
grant select, insert, update on public.backend_config to service_role;

create or replace function public.ssv_enabled()
returns boolean
language sql stable security definer set search_path = public
as $$
  select coalesce((select case when jsonb_typeof(value) = 'boolean' then value::text::boolean end
                     from public.backend_config where key = 'ssv_enabled'), false)
$$;

revoke all on function public.ssv_enabled() from public, anon, authenticated;
grant execute on function public.ssv_enabled() to service_role;   -- operator diagnostics

-- ---------------------------------------------------------------------------
-- The verified grant. Called ONLY by the Edge Function (service_role) after it has
-- checked Google's signature - never exposed to clients, or it would be the very
-- hole SSV closes. Takes the user explicitly because there is no auth.uid() here:
-- the caller is Google, not the player.
-- ---------------------------------------------------------------------------

create or replace function public.grant_ad_refill_verified(p_user_id uuid, p_transaction_id text)
returns jsonb
language plpgsql security definer set search_path = public
as $$
declare
  v_row  public.attempts%rowtype;
  v_gain int;
begin
  if p_user_id is null or p_transaction_id is null or p_transaction_id = '' then
    return jsonb_build_object('ok', false, 'reason', 'bad_request');
  end if;

  -- Replay guard first and cheaply: Google retries callbacks, and a retry must not
  -- pay twice. The unique index is the real enforcement; this is the fast path.
  if exists (select 1 from public.ad_grants where transaction_id = p_transaction_id) then
    return jsonb_build_object('ok', true, 'reason', 'already_granted');
  end if;

  select * into v_row from public.attempts where user_id = p_user_id for update;
  if not found then
    return jsonb_build_object('ok', false, 'reason', 'no_meter');
  end if;
  if public.ad_grants_remaining(p_user_id) <= 0 then
    return jsonb_build_object('ok', false, 'reason', 'rate_limited');
  end if;
  if v_row.premium then
    return jsonb_build_object('ok', false, 'reason', 'premium');
  end if;

  -- Regen before the top-up, same as every other meter path.
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
    -- Budget untouched (the branch heals), matching grant_ad_refill.
    return jsonb_build_object('ok', false, 'reason', 'attempts_full', 'attempts', v_row.count);
  end if;

  v_row.count := least(5, v_row.count + 2);
  if v_row.count >= 5 then v_row.last_regen_at := now(); end if;
  update public.attempts
     set count = v_row.count, last_regen_at = v_row.last_regen_at, updated_at = now()
   where user_id = p_user_id;

  insert into public.ad_grants (user_id, transaction_id) values (p_user_id, p_transaction_id)
  on conflict (transaction_id) do nothing;   -- concurrent duplicate callbacks

  return jsonb_build_object('ok', true, 'attempts', v_row.count,
                            'grants_remaining', public.ad_grants_remaining(p_user_id));
end
$$;

revoke all on function public.grant_ad_refill_verified(uuid, text) from public, anon, authenticated;
grant execute on function public.grant_ad_refill_verified(uuid, text) to service_role;

-- ---------------------------------------------------------------------------
-- Close the client-claimed path once SSV is proven. Behaviour is otherwise
-- untouched; only the new guard at the top is added.
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
  -- SSV live: the client no longer gets to assert it watched anything. The reward
  -- arrives from Google's callback instead, so the app just refreshes its meter.
  if public.ssv_enabled() then
    return jsonb_build_object('ok', false, 'reason', 'ssv_required',
                              'grants_remaining', public.ad_grants_remaining(v_uid));
  end if;
  select * into v_row from public.attempts where user_id = v_uid for update;
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
