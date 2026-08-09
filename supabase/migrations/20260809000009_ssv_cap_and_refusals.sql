-- ---------------------------------------------------------------------------
-- Four production-path defects in the verified SSV grant (review 2026-08-09,
-- all introduced by migration 8's claim-first rewrite):
--
-- 1. THE DAILY CAP WAS DEAD CODE. ad_grants_remaining() is clamped with
--    greatest(0, ...) so migration 8's `< 0` check could never fire — Google-
--    verified grants were unlimited server-side, and the only thing enforcing
--    10/day was the client hiding its own button. The bound that keeps premium
--    "unlimited lives" sellable was enforced nowhere.
-- 2. No ssv_enabled gate: with the flag false (the migration default) and the
--    SSV URL registered in the console, BOTH paths paid — +4 per watch.
-- 3. Refusals consumed budget: the claim row counted toward the daily window
--    even when the grant was refused (attempts_full etc.), silently breaking
--    the "attempts_full heals, budget untouched" contract the client relies on.
-- 4. A callback for a deleted account hit the ad_grants.user_id FK and raised,
--    which the Edge Function turns into a 500 — and 5xx tells Google to RETRY,
--    producing an error storm per deleted-user transaction.
--
-- One mechanism fixes 1 and 3 together: claim rows are inserted granted=false
-- and flipped true only when the grant actually pays. The replay guard counts
-- ALL rows (a refused transaction stays final); the daily budget counts only
-- GRANTED rows. The cap check is `<= 0` against a count the claim row no longer
-- pollutes — reachable, and refusal-neutral.
-- ---------------------------------------------------------------------------

alter table public.ad_grants add column if not exists granted boolean not null default true;

-- Server-side operators (and the local test suite) may read/write the ledger
-- directly; clients still cannot (no anon/authenticated grants, no RLS policies).
grant select, insert on public.ad_grants to service_role;
comment on column public.ad_grants.granted is
  'True = the row paid +2 and counts toward the daily budget. False = a claimed but refused SSV transaction: replay-final, budget-neutral.';

-- Budget counts only what was actually paid. Legacy client-claimed rows default
-- to true, which is correct: they all paid.
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
       and granted
       and created_at > now() - interval '24 hours'))
$$;

revoke all on function public.ad_grants_remaining(uuid) from public, anon, authenticated;

create or replace function public.grant_ad_refill_verified(p_user_id uuid, p_transaction_id text)
returns jsonb
language plpgsql security definer set search_path = public
as $$
declare
  v_row    public.attempts%rowtype;
  v_gain   int;
  v_claim  bigint;
begin
  if p_user_id is null or p_transaction_id is null or p_transaction_id = '' then
    return jsonb_build_object('ok', false, 'reason', 'bad_request');
  end if;

  -- The two grant paths are mutually exclusive on the flag. Without this, the
  -- window between registering the SSV URL and flipping ssv_enabled paid twice
  -- per watch: once to the client's claim, once to Google's callback. A refusal
  -- here is final (the Edge Function answers 200) - the client-claimed path is
  -- the one paying while the flag is off, so the player still gets the reward.
  if not public.ssv_enabled() then
    return jsonb_build_object('ok', false, 'reason', 'ssv_disabled');
  end if;

  -- Claim the transaction first (the unique index arbitrates the replay race),
  -- but as granted=false: a claim is not a payment. The FK guard covers accounts
  -- deleted between the watch and the callback - that must be a clean final
  -- answer, not an exception that the Edge Function converts into a retryable
  -- 500 for a callback that can never succeed.
  begin
    insert into public.ad_grants (user_id, transaction_id, granted)
    values (p_user_id, p_transaction_id, false)
    on conflict (transaction_id) do nothing
    returning id into v_claim;
  exception when foreign_key_violation then
    return jsonb_build_object('ok', false, 'reason', 'no_user');
  end;

  -- Already claimed: paid or refused, either way that transaction is settled.
  if v_claim is null then
    return jsonb_build_object('ok', true, 'reason', 'already_granted');
  end if;

  select * into v_row from public.attempts where user_id = p_user_id for update;
  if not found then
    return jsonb_build_object('ok', false, 'reason', 'no_meter');
  end if;

  -- Reachable now (migration 8 compared `< 0` against a function clamped at 0,
  -- so verified grants were unlimited). Runs AFTER the attempts lock, so two
  -- distinct transactions for one user serialize and cannot both pass at 9/10.
  if public.ad_grants_remaining(p_user_id) <= 0 then
    return jsonb_build_object('ok', false, 'reason', 'rate_limited');
  end if;
  if v_row.premium then
    return jsonb_build_object('ok', false, 'reason', 'premium');
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
    -- Budget-neutral by construction now: the claim row is granted=false and
    -- stays that way, so this branch heals exactly like the legacy path.
    return jsonb_build_object('ok', false, 'reason', 'attempts_full', 'attempts', v_row.count);
  end if;

  v_row.count := least(5, v_row.count + 2);
  if v_row.count >= 5 then v_row.last_regen_at := now(); end if;
  update public.attempts
     set count = v_row.count, last_regen_at = v_row.last_regen_at, updated_at = now()
   where user_id = p_user_id;

  update public.ad_grants set granted = true where id = v_claim;

  return jsonb_build_object('ok', true, 'attempts', v_row.count,
                            'grants_remaining', public.ad_grants_remaining(p_user_id));
end
$$;

revoke all on function public.grant_ad_refill_verified(uuid, text) from public, anon, authenticated;
grant execute on function public.grant_ad_refill_verified(uuid, text) to service_role;
