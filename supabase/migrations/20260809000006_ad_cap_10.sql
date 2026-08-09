-- ---------------------------------------------------------------------------
-- Rewarded refill cap 3/day -> 10/day (Nick 2026-08-09).
--
-- 10 grants x +2 attempts = up to 20 lives a day from ads, against a meter that
-- holds 5. The old 3 was never a design target: it was the stopgap bound on how
-- much a FORGED claim could mint while the grant is still client-claimed
-- (BACKEND.md §6.4 - SSV replaces that and is the real fix).
--
-- The trade it does NOT remove: premium sells unlimited lives, so free refills
-- have to stay finite or the unlock has nothing to sell. 10 is generous for a
-- normal player and still bounded.
--
-- Nothing else changes - grant_ad_refill and ad_grants_remaining both read the
-- cap through this function, which is exactly why it was given one home.
-- ---------------------------------------------------------------------------

create or replace function public.ad_refill_daily_cap()
returns int language sql immutable as $$ select 10 $$;
