-- MadTowers core backend schema — BACKEND.md §4 (tables/RLS) + §6 (server-authoritative
-- attempts & the start_run/finish_run handshake). Migration #1.
--
-- Security model (§4.3): client ships the anon key only; every table has RLS enabled;
-- there are NO write policies anywhere — all mutations go through SECURITY DEFINER
-- functions. Anonymous Supabase users carry role `authenticated` (with is_anonymous
-- claim), so grants target `authenticated`.

-- ---------------------------------------------------------------------------
-- Tables
-- ---------------------------------------------------------------------------

create table public.profiles (
  user_id      uuid primary key references auth.users(id) on delete cascade,
  display_name text not null,
  is_linked    boolean not null default false,
  created_at   timestamptz not null default now(),
  updated_at   timestamptz not null default now()
);
comment on table public.profiles is 'Identity/display. Auto Builder-XXXX name at signup; rename only via claim_display_name().';

create unique index profiles_display_name_lower_idx on public.profiles (lower(display_name));

create table public.progress (
  user_id        uuid primary key references auth.users(id) on delete cascade,
  payload        jsonb not null default '{}'::jsonb,
  schema_version int not null default 1,
  updated_at     timestamptz not null default now()
);
comment on table public.progress is 'The synced ProgressStore save document (BACKEND.md §4.2). Written only via merge_progress().';

create table public.scores (
  user_id     uuid not null references auth.users(id) on delete cascade,
  level_id    text not null,
  board       text not null check (board in ('clean','boosted')),
  best_score  int not null default 0,
  best_height real not null default 0,
  loadout     jsonb,
  achieved_at timestamptz not null default now(),
  primary key (user_id, level_id, board)
);
comment on table public.scores is 'Leaderboard rows, CLEAN/BOOSTED split (SHOP.md §5). Written only via finish_run().';

create index scores_leaderboard_idx on public.scores (level_id, board, best_score desc);

create table public.attempts (
  user_id       uuid primary key references auth.users(id) on delete cascade,
  count         int not null default 5,
  last_regen_at timestamptz not null default now(),
  premium       boolean not null default false,
  updated_at    timestamptz not null default now()
);
comment on table public.attempts is 'Server-owned attempts meter (BACKEND.md §6). Lazy regen +1/600s, cap 5. premium set only via receipt validation.';

create table public.runs (
  run_id      uuid primary key default gen_random_uuid(),
  user_id     uuid not null references auth.users(id) on delete cascade,
  level_id    text not null,
  board       text not null check (board in ('clean','boosted')),
  loadout     jsonb,
  started_at  timestamptz not null default now(),
  finished_at timestamptz,
  won         boolean,
  score       int
);
comment on table public.runs is 'Run ledger — the start_run/finish_run anti-cheat handshake (BACKEND.md §6.2) + analytics.';

create index runs_user_started_idx on public.runs (user_id, started_at desc);

create table public.ad_grants (
  id         bigint generated always as identity primary key,
  user_id    uuid not null references auth.users(id) on delete cascade,
  created_at timestamptz not null default now()
);
comment on table public.ad_grants is 'Rate-limit ledger for grant_ad_refill(). Replace claim path with AdMob SSV before production (BACKEND.md §6.4).';

create index ad_grants_user_time_idx on public.ad_grants (user_id, created_at desc);

-- ---------------------------------------------------------------------------
-- RLS: reads only; zero direct write policies. Belt-and-braces: also revoke
-- write table privileges from client roles (definer functions run as owner).
-- ---------------------------------------------------------------------------

alter table public.profiles  enable row level security;
alter table public.progress  enable row level security;
alter table public.scores    enable row level security;
alter table public.attempts  enable row level security;
alter table public.runs      enable row level security;
alter table public.ad_grants enable row level security;

create policy profiles_select_all on public.profiles for select to authenticated using (true);
create policy progress_select_own on public.progress for select to authenticated using (auth.uid() = user_id);
create policy scores_select_all   on public.scores   for select to authenticated using (true);
create policy attempts_select_own on public.attempts for select to authenticated using (auth.uid() = user_id);
create policy runs_select_own     on public.runs     for select to authenticated using (auth.uid() = user_id);
-- ad_grants: no policies at all (server-internal).

revoke insert, update, delete, truncate, references, trigger
  on public.profiles, public.progress, public.scores, public.attempts, public.runs, public.ad_grants
  from anon, authenticated;

-- Table-level SELECT must be granted explicitly (RLS policies filter rows but do not
-- grant privileges; migrations run as a role whose default privileges don't cover
-- client roles). ad_grants stays server-internal: no client grant at all.
grant select on public.profiles, public.progress, public.scores, public.attempts, public.runs
  to authenticated;

-- ---------------------------------------------------------------------------
-- Signup trigger: every new auth user gets profile (Builder-XXXX) + attempts + progress
-- ---------------------------------------------------------------------------

create or replace function public.handle_new_user()
returns trigger
language plpgsql security definer set search_path = public
as $$
declare
  v_name  text;
  v_tries int := 0;
begin
  loop
    v_tries := v_tries + 1;
    if v_tries > 20 then
      v_name := 'Builder-' || substr(replace(new.id::text, '-', ''), 1, 12);
    else
      v_name := 'Builder-' || lpad(floor(random() * 10000)::int::text, 4, '0');
    end if;
    begin
      insert into public.profiles (user_id, display_name) values (new.id, v_name);
      exit;
    exception when unique_violation then
      if v_tries > 21 then
        raise; -- uid-derived name collided twice: give up loudly rather than loop forever
      end if;
    end;
  end loop;
  insert into public.attempts (user_id) values (new.id) on conflict do nothing;
  insert into public.progress (user_id) values (new.id) on conflict do nothing;
  return new;
end
$$;

create trigger on_auth_user_created
  after insert on auth.users
  for each row execute function public.handle_new_user();

-- ---------------------------------------------------------------------------
-- Merge helpers (DATA.md five rules). Deterministic, symmetric, idempotent.
-- ---------------------------------------------------------------------------

-- Generic monotonic merge: numbers -> max, booleans -> OR, strings -> lexicographic max,
-- arrays -> distinct union (sorted by text form), objects -> per-key recursion.
-- Type mismatch (schema drift) -> incoming wins.
create or replace function public.jsonb_merge_generic(a jsonb, b jsonb)
returns jsonb
language plpgsql immutable
as $$
declare
  ta  text;
  res jsonb;
begin
  -- One-sided values still self-merge: this normalizes nested arrays (distinct+sorted)
  -- so the result is identical no matter which device pushed first (symmetry check).
  if (a is null or jsonb_typeof(a) = 'null') and (b is null or jsonb_typeof(b) = 'null') then
    return coalesce(a, b);
  end if;
  if a is null or jsonb_typeof(a) = 'null' then return public.jsonb_merge_generic(b, b); end if;
  if b is null or jsonb_typeof(b) = 'null' then return public.jsonb_merge_generic(a, a); end if;
  ta := jsonb_typeof(a);
  if ta <> jsonb_typeof(b) then return b; end if;
  if ta = 'number' then
    return to_jsonb(greatest((a #>> '{}')::numeric, (b #>> '{}')::numeric));
  elsif ta = 'boolean' then
    return to_jsonb(((a #>> '{}')::boolean) or ((b #>> '{}')::boolean));
  elsif ta = 'string' then
    return to_jsonb(greatest(a #>> '{}', b #>> '{}'));
  elsif ta = 'array' then
    select coalesce(jsonb_agg(v order by v::text), '[]'::jsonb) into res
      from (select distinct u.value as v
              from (select jsonb_array_elements(a) as value
                    union all
                    select jsonb_array_elements(b) as value) u) d;
    return res;
  else -- object
    select coalesce(jsonb_object_agg(k.key, public.jsonb_merge_generic(a -> k.key, b -> k.key)), '{}'::jsonb) into res
      from (select jsonb_object_keys(a) as key
            union
            select jsonb_object_keys(b) as key) k;
    return res;
  end if;
end
$$;

-- bests: array of {levelId, board?, bestScore, bestHeightMeters, achievedAtUnixUtc}
-- merged per (levelId, board) key with per-metric max (via the generic object merge).
create or replace function public.merge_bests(a jsonb, b jsonb)
returns jsonb
language plpgsql immutable
as $$
declare
  v_all    jsonb;
  v_key    text;
  v_acc    jsonb;
  v_entry  jsonb;
  v_result jsonb := '[]'::jsonb;
begin
  if a is null or jsonb_typeof(a) <> 'array' then a := '[]'::jsonb; end if;
  if b is null or jsonb_typeof(b) <> 'array' then b := '[]'::jsonb; end if;
  v_all := a || b;
  for v_key in
    select distinct (e.value ->> 'levelId') || '|' || coalesce(e.value ->> 'board', 'clean')
      from jsonb_array_elements(v_all) e
     order by 1
  loop
    v_acc := null;
    for v_entry in
      select e.value from jsonb_array_elements(v_all) e
       where (e.value ->> 'levelId') || '|' || coalesce(e.value ->> 'board', 'clean') = v_key
    loop
      v_acc := case when v_acc is null then v_entry
                    else public.jsonb_merge_generic(v_acc, v_entry) end;
    end loop;
    v_result := v_result || jsonb_build_array(v_acc);
  end loop;
  return v_result;
end
$$;

-- Full payload merge: generic rules + two overrides (settings = wholesale LWW by
-- updatedAtUnixUtc; bests = keyed per-metric max).
create or replace function public.merge_payload(stored jsonb, incoming jsonb)
returns jsonb
language plpgsql immutable
as $$
declare
  merged jsonb;
  sa     jsonb;
  sb     jsonb;
  tsa    numeric;
  tsb    numeric;
begin
  stored   := coalesce(stored, '{}'::jsonb);
  incoming := coalesce(incoming, '{}'::jsonb);
  merged   := public.jsonb_merge_generic(stored, incoming);
  sa := stored -> 'settings';
  sb := incoming -> 'settings';
  if sa is not null and jsonb_typeof(sa) = 'object'
     and sb is not null and jsonb_typeof(sb) = 'object' then
    tsa := coalesce((sa ->> 'updatedAtUnixUtc')::numeric, 0);
    tsb := coalesce((sb ->> 'updatedAtUnixUtc')::numeric, 0);
    merged := jsonb_set(merged, '{settings}',
      case when tsb > tsa then sb
           when tsa > tsb then sa
           else public.jsonb_merge_generic(sa, sb) end, true);
  end if;
  if (stored ? 'bests') or (incoming ? 'bests') then
    merged := jsonb_set(merged, '{bests}', public.merge_bests(stored -> 'bests', incoming -> 'bests'), true);
  end if;
  return merged;
end
$$;

-- ---------------------------------------------------------------------------
-- Attempts helpers
-- ---------------------------------------------------------------------------

create or replace function public.secs_until_next(p_count int, p_last timestamptz, p_premium boolean)
returns int
language sql stable
as $$
  select case when p_premium or p_count >= 5 then 0
              else least(600, greatest(1,
                     600 - (floor(extract(epoch from (now() - p_last)))::int % 600)))
         end
$$;

-- ---------------------------------------------------------------------------
-- RPC: merge_progress
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
  -- the attempts meter and premium live in the attempts table; forged copies in a pushed
  -- save must not merge back to other devices (review finding).
  p_payload := coalesce(p_payload, '{}'::jsonb)
               - 'attemptsCount' - 'attemptsUpdatedAtUnixUtc' - 'premiumUnlocked';
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
comment on function public.merge_progress(jsonb, int) is 'Union/max/LWW merge of the client save into progress.payload (DATA.md five rules). Returns the merged document.';

-- ---------------------------------------------------------------------------
-- RPC: start_run — the only way to spend an attempt (BACKEND.md §6.1)
-- ---------------------------------------------------------------------------

-- Soft-landing verdict (SHOP.md §7.1 / BACKEND.md §5.1): the meter charges only once the
-- user's synced payload shows chapter 1 completed. The campaign unlocks sequentially, so
-- "completions >= chapter-1 level count" IS "chapter 1 done" without the server knowing
-- level content. The count lives in backend_config so a chapter-1 redesign is a data edit.
create table public.backend_config (
  key   text primary key,
  value jsonb not null
);
alter table public.backend_config enable row level security;
revoke all on public.backend_config from public, anon, authenticated;
insert into public.backend_config (key, value) values ('chapter1_level_count', '3');

create or replace function public.attempts_meter_charged(p_uid uuid)
returns boolean
language sql stable
as $$
  select coalesce(
    (select jsonb_array_length(coalesce(p.payload -> 'completedLevelIds', '[]'::jsonb)) >=
            coalesce((select (c.value #>> '{}')::int from public.backend_config c
                       where c.key = 'chapter1_level_count'), 3)
       from public.progress p
      where p.user_id = p_uid),
    false);
$$;
revoke all on function public.attempts_meter_charged(uuid) from public, anon, authenticated;

create or replace function public.start_run(p_level_id text, p_board text, p_loadout jsonb default null)
returns jsonb
language plpgsql security definer set search_path = public
as $$
declare
  v_uid     uuid := auth.uid();
  v_row     public.attempts%rowtype;
  v_gain    int;
  v_run_id  uuid;
  v_charged boolean;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  if p_board is null or p_board not in ('clean','boosted') then
    return jsonb_build_object('allowed', false, 'reason', 'bad_board');
  end if;
  if p_level_id is null or length(trim(p_level_id)) = 0 or length(p_level_id) > 128 then
    return jsonb_build_object('allowed', false, 'reason', 'bad_level');
  end if;
  -- A real loadout is tiny ({"lives":n,"boosts":[...]}); it is echoed to every
  -- leaderboard viewer via get_leaderboard, so an unbounded one is an egress
  -- amplifier (review finding). Cap it at the source.
  if p_loadout is not null and pg_column_size(p_loadout) > 2048 then
    return jsonb_build_object('allowed', false, 'reason', 'bad_loadout');
  end if;

  insert into public.attempts (user_id) values (v_uid) on conflict do nothing;
  select * into v_row from public.attempts where user_id = v_uid for update;
  v_charged := public.attempts_meter_charged(v_uid);

  -- lazy rolling regen: +1 per whole 600s elapsed, cap 5 (BACKEND.md §6.1)
  if not v_row.premium and v_row.count < 5 then
    v_gain := floor(extract(epoch from (now() - v_row.last_regen_at)) / 600)::int;
    if v_gain > 0 then
      v_row.count := least(5, v_row.count + v_gain);
      if v_row.count >= 5 then v_row.last_regen_at := now();
      else v_row.last_regen_at := v_row.last_regen_at + make_interval(secs => v_gain * 600);
      end if;
    end if;
  end if;

  if v_row.premium or not v_charged then
    null; -- premium and soft-landing-exempt runs never charge; still get a real run_id below
  elsif v_row.count <= 0 then
    update public.attempts
       set count = v_row.count, last_regen_at = v_row.last_regen_at, updated_at = now()
     where user_id = v_uid;
    return jsonb_build_object(
      'allowed', false, 'reason', 'out_of_attempts',
      'attempts', 0, 'premium', false, 'meter_charged', true,
      'seconds_until_next', public.secs_until_next(v_row.count, v_row.last_regen_at, false));
  else
    if v_row.count >= 5 then
      v_row.last_regen_at := now(); -- spending from cap starts the regen clock now
    end if;
    v_row.count := v_row.count - 1;
  end if;

  update public.attempts
     set count = v_row.count, last_regen_at = v_row.last_regen_at, updated_at = now()
   where user_id = v_uid;

  insert into public.runs (user_id, level_id, board, loadout)
  values (v_uid, p_level_id, p_board, case when p_board = 'boosted' then p_loadout end)
  returning run_id into v_run_id;

  return jsonb_build_object(
    'allowed', true, 'run_id', v_run_id,
    'attempts', v_row.count, 'premium', v_row.premium, 'meter_charged', v_charged,
    'seconds_until_next', public.secs_until_next(v_row.count, v_row.last_regen_at, v_row.premium));
end
$$;
comment on function public.start_run(text, text, jsonb) is 'Grants a campaign run: lazy regen, premium skip, decrement, run ledger row. Refusal carries seconds_until_next.';

-- ---------------------------------------------------------------------------
-- RPC: finish_run — refund on win + the only score-write path (BACKEND.md §6.2)
-- ---------------------------------------------------------------------------

create or replace function public.finish_run(p_run_id uuid, p_won boolean, p_score int, p_height real)
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

  return jsonb_build_object(
    'accepted', true, 'new_best', v_new_best,
    'attempts', v_row.count, 'premium', v_row.premium,
    'seconds_until_next', public.secs_until_next(v_row.count, v_row.last_regen_at, v_row.premium));
end
$$;
comment on function public.finish_run(uuid, boolean, int, real) is 'Closes a run: plausibility checks, win refund, sanity-bounded score upsert. The only leaderboard write path.';

-- ---------------------------------------------------------------------------
-- RPC: get_attempts — read-only projection for the client display cache (§6.3)
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
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  -- Key names are the client DTO contract (AttemptsSync.GetAttemptsDto): count,
  -- seconds_until_next, premium, meter_charged. (start_run/finish_run replies use
  -- 'attempts' - separate DTOs, aligned deliberately; do not "unify" one-sided.)
  v_charged := public.attempts_meter_charged(v_uid);
  select * into v_row from public.attempts where user_id = v_uid;
  if not found then
    return jsonb_build_object('count', 5, 'premium', false, 'seconds_until_next', 0,
                              'meter_charged', v_charged);
  end if;
  if v_row.premium then
    return jsonb_build_object('count', v_row.count, 'premium', true, 'seconds_until_next', 0,
                              'meter_charged', v_charged);
  end if;
  v_eff := least(5, v_row.count + greatest(0, floor(extract(epoch from (now() - v_row.last_regen_at)) / 600)::int));
  return jsonb_build_object(
    'count', v_eff, 'premium', false,
    'seconds_until_next', public.secs_until_next(v_eff, v_row.last_regen_at, false),
    'meter_charged', v_charged);
end
$$;
comment on function public.get_attempts() is 'Lock-free effective attempts + countdown; never writes.';

-- ---------------------------------------------------------------------------
-- RPC: get_leaderboard — top-N + caller rank in one round-trip (BACKEND.md §7)
-- ---------------------------------------------------------------------------

create or replace function public.get_leaderboard(p_level_id text, p_board text, p_limit int default 100)
returns jsonb
language sql stable security definer set search_path = public
as $$
  with ranked as (
    select s.user_id, s.best_score, s.best_height, s.achieved_at, s.loadout,
           rank() over (order by s.best_score desc, s.achieved_at asc, s.user_id) as rnk
      from public.scores s
     where s.level_id = p_level_id and s.board = p_board
  )
  select jsonb_build_object(
    'level_id', p_level_id,
    'board', p_board,
    -- Key names are the client DTO contract (Leaderboards.Entry, JsonUtility): rank,
    -- display_name, best_score, best_height, loadout, is_you. 'you' uses the same shape.
    'entries', coalesce((
       select jsonb_agg(jsonb_build_object(
                'rank', r.rnk,
                'display_name', p.display_name,
                'is_linked', p.is_linked,
                'best_score', r.best_score,
                'best_height', r.best_height,
                'loadout', r.loadout,
                'achieved_at', extract(epoch from r.achieved_at)::bigint,
                'is_you', (r.user_id = auth.uid())) order by r.rnk)
         from (select * from ranked order by rnk limit least(greatest(coalesce(p_limit, 100), 1), 100)) r
         join public.profiles p on p.user_id = r.user_id), '[]'::jsonb),
    'you', (select jsonb_build_object(
                'rank', r.rnk,
                'display_name', p.display_name,
                'is_linked', p.is_linked,
                'best_score', r.best_score,
                'best_height', r.best_height,
                'loadout', r.loadout,
                'achieved_at', extract(epoch from r.achieved_at)::bigint,
                'is_you', true)
              from ranked r
              join public.profiles p on p.user_id = r.user_id
             where r.user_id = auth.uid())
  )
$$;
comment on function public.get_leaderboard(text, text, int) is 'Top-N for a level+board joined to display names, plus the caller''s own rank even outside top N.';

-- ---------------------------------------------------------------------------
-- RPC: claim_display_name / mark_linked (BACKEND.md §3.5)
-- ---------------------------------------------------------------------------

create or replace function public.claim_display_name(p_name text)
returns jsonb
language plpgsql security definer set search_path = public
as $$
declare
  v_uid  uuid := auth.uid();
  v_name text := trim(coalesce(p_name, ''));
  v_word text;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  if v_name !~ '^[A-Za-z0-9 _-]{3,16}$' then
    return jsonb_build_object('ok', false, 'reason', 'invalid');
  end if;
  foreach v_word in array array['fuck','shit','cunt','nigg','bitch','asshole','dick','faggot','whore','slut','hitler','rape'] loop
    if position(v_word in lower(v_name)) > 0 then
      return jsonb_build_object('ok', false, 'reason', 'not_allowed');
    end if;
  end loop;
  if exists (select 1 from public.profiles
              where lower(display_name) = lower(v_name) and user_id <> v_uid) then
    return jsonb_build_object('ok', false, 'reason', 'taken');
  end if;
  begin
    update public.profiles
       set display_name = v_name, updated_at = now()
     where user_id = v_uid;
  exception when unique_violation then
    return jsonb_build_object('ok', false, 'reason', 'taken');
  end;
  -- Key name is the client DTO contract (OnlineService.ClaimNameDto): display_name.
  return jsonb_build_object('ok', true, 'display_name', v_name);
end
$$;
comment on function public.claim_display_name(text) is 'Validated rename: 3-16 chars [A-Za-z0-9 _-], case-insensitive unique, basic denylist.';

create or replace function public.mark_linked()
returns void
language plpgsql security definer set search_path = public
as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  if coalesce((auth.jwt() ->> 'is_anonymous')::boolean, false) = false then
    update public.profiles set is_linked = true, updated_at = now()
     where user_id = auth.uid() and is_linked = false;
  end if;
end
$$;
comment on function public.mark_linked() is 'Flags the profile linked once the JWT is no longer anonymous (after Apple/Google identity linking).';

-- ---------------------------------------------------------------------------
-- RPC: grant_ad_refill — +2 attempts, rate-limited stub.
-- PRODUCTION NOTE: this client-claimed path MUST be replaced by AdMob SSV
-- (server-side verification callback) before launch — BACKEND.md §6.4.
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
  if not found then
    return jsonb_build_object('ok', false, 'reason', 'no_meter');
  end if;
  if (select count(*) from public.ad_grants
       where user_id = v_uid and created_at > now() - interval '24 hours') >= 3 then
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
   where user_id = v_uid;
  insert into public.ad_grants (user_id) values (v_uid);
  return jsonb_build_object(
    'ok', true, 'attempts', v_row.count, 'premium', false,
    'seconds_until_next', public.secs_until_next(v_row.count, v_row.last_regen_at, false));
end
$$;

-- ---------------------------------------------------------------------------
-- RPC: delete_account — store-required wipe (BACKEND.md §3.7); cascades do the rest
-- ---------------------------------------------------------------------------

create or replace function public.delete_account()
returns void
language plpgsql security definer set search_path = public
as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  delete from auth.users where id = auth.uid();
end
$$;
comment on function public.delete_account() is 'Deletes the caller''s auth user; FK cascades wipe profiles/progress/scores/attempts/runs/ad_grants.';

-- ---------------------------------------------------------------------------
-- Function grants: client-callable RPCs -> authenticated (+ service_role).
-- Helpers and the trigger stay definer-internal (postgres owns them).
-- ---------------------------------------------------------------------------

revoke all on function public.handle_new_user()                        from public, anon, authenticated;
revoke all on function public.jsonb_merge_generic(jsonb, jsonb)        from public, anon, authenticated;
revoke all on function public.merge_bests(jsonb, jsonb)                from public, anon, authenticated;
revoke all on function public.merge_payload(jsonb, jsonb)              from public, anon, authenticated;
revoke all on function public.secs_until_next(int, timestamptz, boolean) from public, anon, authenticated;

revoke all on function public.merge_progress(jsonb, int)               from public, anon;
revoke all on function public.start_run(text, text, jsonb)             from public, anon;
revoke all on function public.finish_run(uuid, boolean, int, real)     from public, anon;
revoke all on function public.get_attempts()                           from public, anon;
revoke all on function public.get_leaderboard(text, text, int)         from public, anon;
revoke all on function public.claim_display_name(text)                 from public, anon;
revoke all on function public.mark_linked()                            from public, anon;
revoke all on function public.grant_ad_refill()                        from public, anon;
revoke all on function public.delete_account()                         from public, anon;

grant execute on function public.merge_progress(jsonb, int)            to authenticated, service_role;
grant execute on function public.start_run(text, text, jsonb)          to authenticated, service_role;
grant execute on function public.finish_run(uuid, boolean, int, real)  to authenticated, service_role;
grant execute on function public.get_attempts()                        to authenticated, service_role;
grant execute on function public.get_leaderboard(text, text, int)      to authenticated, service_role;
grant execute on function public.claim_display_name(text)              to authenticated, service_role;
grant execute on function public.mark_linked()                         to authenticated, service_role;
grant execute on function public.grant_ad_refill()                     to authenticated, service_role;
grant execute on function public.delete_account()                      to authenticated, service_role;
