-- ---------------------------------------------------------------------------
-- Close the SSV double-grant race (review 2026-08-09).
--
-- grant_ad_refill_verified checked "has this transaction_id been seen?" BEFORE
-- taking the row lock on attempts. Google retries a callback it thinks was slow,
-- so two calls with the SAME transaction_id can overlap: both read the ledger and
-- see nothing, then serialize on the attempts lock. The first grants +2 and inserts
-- the ledger row; the second unblocks, re-reads under READ COMMITTED, still passes
-- its count < 5 check, and grants ANOTHER +2 - while its
-- `insert ... on conflict do nothing` quietly does nothing. The meter moved twice,
-- the ledger recorded one grant, and the daily budget was charged once.
--
-- Fix: claim the transaction FIRST and let the unique index arbitrate. The insert
-- either wins (this call owns the grant) or hits the conflict (someone else owns
-- it, so return already_granted). No read-then-write window remains.
--
-- The ledger row is now written before the meter check, which means a refusal
-- (attempts_full, rate_limited, premium) consumes the transaction. That is correct
-- for SSV: the transaction genuinely was delivered and answered, and Google must
-- not have it re-answered differently on a retry.
-- ---------------------------------------------------------------------------

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

  -- Claim the transaction. The unique index on transaction_id is the arbiter, so
  -- two concurrent deliveries of the same callback cannot both proceed.
  insert into public.ad_grants (user_id, transaction_id)
  values (p_user_id, p_transaction_id)
  on conflict (transaction_id) do nothing
  returning id into v_claim;

  if v_claim is null then
    return jsonb_build_object('ok', true, 'reason', 'already_granted');
  end if;

  select * into v_row from public.attempts where user_id = p_user_id for update;
  if not found then
    return jsonb_build_object('ok', false, 'reason', 'no_meter');
  end if;
  -- The claim row above is already counted, hence < 0 rather than <= 0.
  if public.ad_grants_remaining(p_user_id) < 0 then
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
    return jsonb_build_object('ok', false, 'reason', 'attempts_full', 'attempts', v_row.count);
  end if;

  v_row.count := least(5, v_row.count + 2);
  if v_row.count >= 5 then v_row.last_regen_at := now(); end if;
  update public.attempts
     set count = v_row.count, last_regen_at = v_row.last_regen_at, updated_at = now()
   where user_id = p_user_id;

  return jsonb_build_object('ok', true, 'attempts', v_row.count,
                            'grants_remaining', greatest(0, public.ad_grants_remaining(p_user_id)));
end
$$;

revoke all on function public.grant_ad_refill_verified(uuid, text) from public, anon, authenticated;
grant execute on function public.grant_ad_refill_verified(uuid, text) to service_role;
