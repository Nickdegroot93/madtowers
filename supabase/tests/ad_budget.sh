#!/usr/bin/env bash
# Rewarded-refill budget mirror (SHOP.md §7.3 item 6, migration 20260808000004).
# Proves the client can know its remaining budget BEFORE spending a watch on a
# refill the server would refuse. Requires the local stack (supabase start).
set -u

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SUPA_BIN="$REPO_ROOT/Tools/bin/supabase"
if [ -z "${SUPABASE_URL:-}" ] || [ -z "${ANON_KEY:-}" ]; then
  STATUS_ENV="$(cd "$REPO_ROOT" && "$SUPA_BIN" status -o env 2>/dev/null || true)"
  SUPABASE_URL="$(echo "$STATUS_ENV" | sed -n 's/^API_URL="\{0,1\}\([^"]*\)"\{0,1\}$/\1/p' | head -1)"
  ANON_KEY="$(echo "$STATUS_ENV" | sed -n 's/^ANON_KEY="\{0,1\}\([^"]*\)"\{0,1\}$/\1/p' | head -1)"
fi
SUPABASE_URL="${SUPABASE_URL:-http://127.0.0.1:55321}"

PASS=0; FAIL=0
ok()  { echo "PASS: $1"; PASS=$((PASS+1)); }
bad() { echo "FAIL: $1"; FAIL=$((FAIL+1)); }

jget() {
  python3 - "$@" <<'PYEOF'
import json, sys
try: cur = json.loads(sys.argv[1])
except Exception: print(""); sys.exit(0)
for part in sys.argv[2:]:
    try:
        cur = cur[int(part)] if isinstance(cur, list) else cur[part]
    except Exception: print(""); sys.exit(0)
print(cur if isinstance(cur, str) else json.dumps(cur))
PYEOF
}

rpc() {
  curl -s -m 20 -X POST "$SUPABASE_URL/rest/v1/rpc/$1" \
    -H "apikey: $ANON_KEY" -H "Authorization: Bearer $2" \
    -H "Content-Type: application/json" -d "$3"
}

R="$(curl -s -m 20 -X POST "$SUPABASE_URL/auth/v1/signup" \
     -H "apikey: $ANON_KEY" -H "Content-Type: application/json" -d '{"data":{}}')"
JWT="$(jget "$R" access_token)"
[ -n "$JWT" ] || { echo "FAIL: could not sign in"; exit 1; }

# --- 1) boot read carries the full budget ------------------------------------
# CAP is read from the server rather than hard-coded: ad_refill_daily_cap() is the
# single home for the number precisely so tuning it does not mean editing code.
P="$(rpc get_profile "$JWT" '{}')"
CAP="$(jget "$P" ad_grants_remaining)"
case "$CAP" in
  ''|*[!0-9]*) bad "get_profile ad_grants_remaining = '$CAP', expected a number"; CAP=0 ;;
  *) [ "$CAP" -gt 0 ] \
       && ok "get_profile exposes a full budget ($CAP) on a fresh account" \
       || bad "fresh account already has no budget" ;;
esac

# --- 1b) the refresh path carries it too -------------------------------------
# get_attempts is the debounced focus-regain refresh; without the budget on this
# reply an exhausted mirror could never heal (the button is hidden, so no
# grant_ad_refill is ever sent to correct it).
A="$(rpc get_attempts "$JWT" '{}')"
[ "$(jget "$A" grants_remaining)" = "$CAP" ] \
  && ok "get_attempts carries the budget (the only self-heal path)" \
  || bad "get_attempts grants_remaining = $(jget "$A" grants_remaining), expected $CAP"

# --- 2) a full meter must NOT consume budget ---------------------------------
# The meter starts at 5/5, so this hits the attempts_full branch. That branch heals
# (the next spent attempt makes room) and must report the budget untouched.
G="$(rpc grant_ad_refill "$JWT" '{}')"
[ "$(jget "$G" reason)" = "attempts_full" ] \
  && [ "$(jget "$G" grants_remaining)" = "$CAP" ] \
  && ok "attempts_full leaves the budget at $CAP (does not read as a cap)" \
  || bad "attempts_full path: reason=$(jget "$G" reason) remaining=$(jget "$G" grants_remaining)"

# --- 3) spend attempts so refills can land -----------------------------------
# Soft landing (SHOP.md §7.1): with zero completions runs are FREE and the meter
# never moves, so the meter has to be switched on before any of this means anything.
PAYLOAD='{"schemaVersion":4,"completedLevelIds":["Level_A","Level_B","Level_C"]}'
rpc merge_progress "$JWT" "{\"p_payload\":$PAYLOAD,\"p_schema_version\":4}" >/dev/null
for i in 1 2 3; do
  S="$(rpc start_run "$JWT" '{"p_level_id":"Level_NN2_Budget","p_board":"clean","p_loadout":null}')"
  RID="$(jget "$S" run_id)"
  [ -n "$RID" ] && rpc finish_run "$JWT" \
    "{\"p_run_id\":\"$RID\",\"p_won\":false,\"p_score\":1,\"p_height\":1}" >/dev/null
done

# --- 4) the budget drains one per grant, and is reported AFTER the insert -----
EXPECT=$((CAP - 1))
DRAIN_OK=1
for i in $(seq 1 "$CAP"); do
  G="$(rpc grant_ad_refill "$JWT" '{}')"
  if [ "$(jget "$G" ok)" != "true" ]; then
    bad "grant $i refused early: $(jget "$G" reason)"; DRAIN_OK=0; break
  fi
  GOT="$(jget "$G" grants_remaining)"
  if [ "$GOT" != "$EXPECT" ]; then
    bad "grant $i reported remaining=$GOT, expected $EXPECT"; DRAIN_OK=0; break
  fi
  EXPECT=$((EXPECT-1))
  # Spend the granted attempts back down so the next grant isn't attempts_full.
  for j in 1 2 3; do
    S="$(rpc start_run "$JWT" '{"p_level_id":"Level_NN2_Budget","p_board":"clean","p_loadout":null}')"
    RID="$(jget "$S" run_id)"
    [ -n "$RID" ] && rpc finish_run "$JWT" \
      "{\"p_run_id\":\"$RID\",\"p_won\":false,\"p_score\":1,\"p_height\":1}" >/dev/null
  done
done
[ "$DRAIN_OK" = "1" ] && ok "budget drains $((CAP - 1)) -> 0 over $CAP grants, reported after each insert"

# --- 5) exhausted budget is visible without another watch --------------------
G="$(rpc grant_ad_refill "$JWT" '{}')"
[ "$(jget "$G" reason)" = "rate_limited" ] && [ "$(jget "$G" grants_remaining)" = "0" ] \
  && ok "grant $((CAP + 1)) refused as rate_limited with remaining=0" \
  || bad "over-cap grant: reason=$(jget "$G" reason) remaining=$(jget "$G" grants_remaining)"

P="$(rpc get_profile "$JWT" '{}')"
[ "$(jget "$P" ad_grants_remaining)" = "0" ] \
  && ok "get_profile reports 0 — a relaunched client hides the button with no watch" \
  || bad "get_profile after exhaustion = $(jget "$P" ad_grants_remaining), expected 0"

# --- 5b) the heal path reports the exhausted budget too ----------------------
A="$(rpc get_attempts "$JWT" '{}')"
[ "$(jget "$A" grants_remaining)" = "0" ] \
  && ok "get_attempts reports 0 after exhaustion (refresh keeps the mirror honest)" \
  || bad "get_attempts after exhaustion = $(jget "$A" grants_remaining), expected 0"

# --- 6) the helper stays server-internal -------------------------------------
H="$(curl -s -m 20 -o /dev/null -w "%{http_code}" -X POST \
      "$SUPABASE_URL/rest/v1/rpc/ad_grants_remaining" \
      -H "apikey: $ANON_KEY" -H "Authorization: Bearer $JWT" \
      -H "Content-Type: application/json" -d '{"p_uid":"00000000-0000-0000-0000-000000000000"}')"
[ "$H" != "200" ] \
  && ok "ad_grants_remaining not callable by clients (http $H) — no reading other ledgers" \
  || bad "ad_grants_remaining is client-callable (http 200)"

rpc delete_account "$JWT" '{}' >/dev/null
echo "RESULT: $PASS passed, $FAIL failed"
[ "$FAIL" -eq 0 ]
