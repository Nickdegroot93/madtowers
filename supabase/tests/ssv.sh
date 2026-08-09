#!/usr/bin/env bash
# AdMob server-side verification: verified grants, the ssv_enabled flag that makes
# the two grant paths mutually exclusive, the daily cap on the VERIFIED path (dead
# code until migration 9 — the review regression this file now guards), and
# refusal budget-neutrality. Signature verification itself lives in the Edge
# Function and is exercised by its own test.
#
# LOCAL ONLY: needs the service-role key, which never leaves this machine.
set -u

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SUPA_BIN="$REPO_ROOT/Tools/bin/supabase"
STATUS_ENV="$(cd "$REPO_ROOT" && "$SUPA_BIN" status -o env 2>/dev/null || true)"
SUPABASE_URL="$(echo "$STATUS_ENV" | sed -n 's/^API_URL="\{0,1\}\([^"]*\)"\{0,1\}$/\1/p' | head -1)"
ANON_KEY="$(echo "$STATUS_ENV" | sed -n 's/^ANON_KEY="\{0,1\}\([^"]*\)"\{0,1\}$/\1/p' | head -1)"
SERVICE_KEY="$(echo "$STATUS_ENV" | sed -n 's/^SERVICE_ROLE_KEY="\{0,1\}\([^"]*\)"\{0,1\}$/\1/p' | head -1)"
if [ -z "${SERVICE_KEY:-}" ]; then echo "FAIL: no local service-role key (is the stack up?)"; exit 1; fi

PASS=0; FAIL=0
ok()  { echo "PASS: $1"; PASS=$((PASS+1)); }
bad() { echo "FAIL: $1"; FAIL=$((FAIL+1)); }

jget() {
  python3 - "$@" <<'PYEOF'
import json, sys
try: cur = json.loads(sys.argv[1])
except Exception: print(""); sys.exit(0)
for part in sys.argv[2:]:
    try: cur = cur[int(part)] if isinstance(cur, list) else cur[part]
    except Exception: print(""); sys.exit(0)
print(cur if isinstance(cur, str) else json.dumps(cur))
PYEOF
}
rpc()  { curl -s -m 20 -X POST "$SUPABASE_URL/rest/v1/rpc/$1" -H "apikey: $ANON_KEY" \
          -H "Authorization: Bearer $2" -H "Content-Type: application/json" -d "$3"; }
srpc() { curl -s -m 20 -X POST "$SUPABASE_URL/rest/v1/rpc/$1" -H "apikey: $SERVICE_KEY" \
          -H "Authorization: Bearer $SERVICE_KEY" -H "Content-Type: application/json" -d "$2"; }
# Fails LOUDLY: an ignored 403 here once made the flip look like it worked when the
# config table had no grants at all, and the test passed a hole it should have caught.
cfg()  {
  local code
  code="$(curl -s -m 20 -o /dev/null -w "%{http_code}" -X POST \
          "$SUPABASE_URL/rest/v1/backend_config?on_conflict=key" \
          -H "apikey: $SERVICE_KEY" -H "Authorization: Bearer $SERVICE_KEY" \
          -H "Content-Type: application/json" -H "Prefer: resolution=merge-duplicates" \
          -d "{\"key\":\"ssv_enabled\",\"value\":$1}")"
  case "$code" in 2*) ;; *) bad "could not set ssv_enabled=$1 (http $code)"; return 1 ;; esac
}
spend_attempts() { # spend_attempts <n>
  for _ in $(seq 1 "$1"); do
    local S RID
    S="$(rpc start_run "$JWT" '{"p_level_id":"Level_SSV","p_board":"clean","p_loadout":null}')"
    RID="$(jget "$S" run_id)"
    [ -n "$RID" ] && rpc finish_run "$JWT" \
      "{\"p_run_id\":\"$RID\",\"p_won\":false,\"p_score\":1,\"p_height\":1}" >/dev/null
  done
}
budget() { jget "$(rpc get_attempts "$JWT" '{}')" grants_remaining; }

R="$(curl -s -m 20 -X POST "$SUPABASE_URL/auth/v1/signup" -H "apikey: $ANON_KEY" \
     -H "Content-Type: application/json" -d '{"data":{}}')"
JWT="$(jget "$R" access_token)"
UID_="$(jget "$R" user id)"
[ -n "$JWT" ] && [ -n "$UID_" ] || { echo "FAIL: sign-in"; exit 1; }

rpc merge_progress "$JWT" \
  '{"p_payload":{"schemaVersion":4,"completedLevelIds":["A","B","C"]},"p_schema_version":4}' >/dev/null
spend_attempts 3
BEFORE="$(jget "$(rpc get_attempts "$JWT" '{}')" count)"

# --- 0) both paths never pay together (the double-pay window) -----------------
# Flag is false after db reset. The verified path must refuse - while the flag is
# off, the client-claimed path is the one paying, and the window between
# registering the SSV URL and flipping the flag used to pay +4 per watch.
D="$(srpc grant_ad_refill_verified "{\"p_user_id\":\"$UID_\",\"p_transaction_id\":\"TX-$$-dis\"}")"
[ "$(jget "$D" reason)" = "ssv_disabled" ] \
  && ok "flag OFF: verified path refuses (no double-pay window)" \
  || bad "flag off: $D"
L="$(rpc grant_ad_refill "$JWT" '{}')"
[ "$(jget "$L" ok)" = "true" ] \
  && ok "flag OFF: client-claimed path still pays" \
  || bad "flag off legacy: $L"

cfg true || exit 1
spend_attempts 3

# --- 1) the verified grant pays ------------------------------------------------
B1="$(jget "$(rpc get_attempts "$JWT" '{}')" count)"
G="$(srpc grant_ad_refill_verified "{\"p_user_id\":\"$UID_\",\"p_transaction_id\":\"TX-$$-1\"}")"
[ "$(jget "$G" ok)" = "true" ] && [ "$(jget "$G" attempts)" = "$((B1 + 2))" ] \
  && ok "flag ON: verified grant pays +2 ($B1 -> $(jget "$G" attempts))" \
  || bad "verified grant: $G (before=$B1)"

# --- 2) replay grants once ------------------------------------------------------
G2="$(srpc grant_ad_refill_verified "{\"p_user_id\":\"$UID_\",\"p_transaction_id\":\"TX-$$-1\"}")"
NOW="$(jget "$(rpc get_attempts "$JWT" '{}')" count)"
[ "$(jget "$G2" reason)" = "already_granted" ] && [ "$NOW" = "$(jget "$G" attempts)" ] \
  && ok "replayed transaction_id grants once" \
  || bad "replay: $G2 attempts=$NOW"

# --- 2b) CONCURRENT duplicates pay once -----------------------------------------
spend_attempts 3
RACE_BEFORE="$(jget "$(rpc get_attempts "$JWT" '{}')" count)"
TX="TX-$$-race"
srpc grant_ad_refill_verified "{\"p_user_id\":\"$UID_\",\"p_transaction_id\":\"$TX\"}" >/dev/null &
srpc grant_ad_refill_verified "{\"p_user_id\":\"$UID_\",\"p_transaction_id\":\"$TX\"}" >/dev/null &
wait
RACE_AFTER="$(jget "$(rpc get_attempts "$JWT" '{}')" count)"
[ "$RACE_AFTER" = "$((RACE_BEFORE + 2))" ] \
  && ok "concurrent duplicate callbacks pay once ($RACE_BEFORE -> $RACE_AFTER, not +4)" \
  || bad "race: $RACE_BEFORE -> $RACE_AFTER (expected +2)"

# --- 3) a refusal must NOT consume budget ---------------------------------------
# Fill the meter to 5 with real grants first, then a refused attempts_full callback
# must leave grants_remaining untouched. Migration 8 charged the budget for every
# claim, refused or not - the exact regen-mid-video race the client documents would
# then burn a slot AND pay nothing.
for i in 1 2 3; do
  CNT="$(jget "$(rpc get_attempts "$JWT" '{}')" count)"
  [ "$CNT" -ge 5 ] && break
  srpc grant_ad_refill_verified "{\"p_user_id\":\"$UID_\",\"p_transaction_id\":\"TX-$$-fill-$i\"}" >/dev/null
done
BUD_BEFORE="$(budget)"
F="$(srpc grant_ad_refill_verified "{\"p_user_id\":\"$UID_\",\"p_transaction_id\":\"TX-$$-full\"}")"
BUD_AFTER="$(budget)"
if [ "$(jget "$F" reason)" = "attempts_full" ]; then
  [ "$BUD_AFTER" = "$BUD_BEFORE" ] \
    && ok "attempts_full refusal is budget-neutral ($BUD_BEFORE unchanged)" \
    || bad "refusal consumed budget: $BUD_BEFORE -> $BUD_AFTER"
else
  bad "expected attempts_full, got: $F"
fi

# --- 4) deleted/unknown user is a clean final answer, not a retry storm ----------
N="$(srpc grant_ad_refill_verified '{"p_user_id":"00000000-0000-0000-0000-00000000dead","p_transaction_id":"TX-nouser"}')"
[ "$(jget "$N" reason)" = "no_user" ] \
  && ok "unknown user refused cleanly (no FK exception -> no 500 -> no Google retry storm)" \
  || bad "unknown user: $N"

# --- 5) THE DAILY CAP ACTUALLY BINDS (dead-code regression) -----------------------
# Seed granted rows up to the cap via service-role, then one more genuine callback
# must be rate_limited. Migration 8 compared `< 0` against a function clamped at 0:
# verified grants were UNLIMITED and only the client's hidden button "enforced" 10/day.
CAP_LEFT="$(budget)"
if [ -n "$CAP_LEFT" ] && [ "$CAP_LEFT" -gt 0 ]; then
  ROWS="["
  for i in $(seq 1 "$CAP_LEFT"); do
    [ "$i" -gt 1 ] && ROWS="$ROWS,"
    ROWS="$ROWS{\"user_id\":\"$UID_\",\"transaction_id\":\"TX-$$-seed-$i\"}"
  done
  ROWS="$ROWS]"
  SEED_CODE="$(curl -s -m 20 -o /dev/null -w "%{http_code}" -X POST "$SUPABASE_URL/rest/v1/ad_grants" \
    -H "apikey: $SERVICE_KEY" -H "Authorization: Bearer $SERVICE_KEY" \
    -H "Content-Type: application/json" -d "$ROWS")"
  case "$SEED_CODE" in 2*) ;; *) bad "could not seed granted rows (http $SEED_CODE)";; esac
fi
spend_attempts 2   # make meter room so rate_limited is the binding refusal
C="$(srpc grant_ad_refill_verified "{\"p_user_id\":\"$UID_\",\"p_transaction_id\":\"TX-$$-overcap\"}")"
[ "$(jget "$C" reason)" = "rate_limited" ] && [ "$(budget)" = "0" ] \
  && ok "verified grant past the daily cap is refused rate_limited (cap binds server-side)" \
  || bad "over-cap verified grant: $C budget=$(budget)"

# --- 6) it must NOT be reachable by a player -------------------------------------
H="$(curl -s -m 20 -o /dev/null -w "%{http_code}" -X POST \
      "$SUPABASE_URL/rest/v1/rpc/grant_ad_refill_verified" \
      -H "apikey: $ANON_KEY" -H "Authorization: Bearer $JWT" \
      -H "Content-Type: application/json" \
      -d "{\"p_user_id\":\"$UID_\",\"p_transaction_id\":\"TX-forged\"}")"
[ "$H" != "200" ] \
  && ok "players cannot call the verified grant directly (http $H)" \
  || bad "verified grant is client-callable — SSV would be pointless"

# --- 7) with the flag ON the client-claimed path is closed ------------------------
C2="$(rpc grant_ad_refill "$JWT" '{}')"
[ "$(jget "$C2" reason)" = "ssv_required" ] \
  && ok "flag ON: client-claimed grant refused (ssv_required)" \
  || bad "legacy path with flag on: $C2"

cfg false >/dev/null      # leave the stack as we found it
rpc delete_account "$JWT" '{}' >/dev/null
echo "RESULT: $PASS passed, $FAIL failed"
[ "$FAIL" -eq 0 ]
