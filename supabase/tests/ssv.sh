#!/usr/bin/env bash
# AdMob server-side verification: the verified grant path and the config flip that
# closes the client-claimed one (migration 20260809000007). Signature verification
# itself lives in the Edge Function and is exercised by its own test.
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
# Fails LOUDLY: an ignored 403 here made the flip look like it worked when the
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

R="$(curl -s -m 20 -X POST "$SUPABASE_URL/auth/v1/signup" -H "apikey: $ANON_KEY" \
     -H "Content-Type: application/json" -d '{"data":{}}')"
JWT="$(jget "$R" access_token)"
UID_="$(jget "$R" user id)"
[ -n "$JWT" ] && [ -n "$UID_" ] || { echo "FAIL: sign-in"; exit 1; }

rpc merge_progress "$JWT" \
  '{"p_payload":{"schemaVersion":4,"completedLevelIds":["A","B","C"]},"p_schema_version":4}' >/dev/null

# Spend attempts so a +2 has room to land.
for i in 1 2 3; do
  S="$(rpc start_run "$JWT" '{"p_level_id":"Level_SSV","p_board":"clean","p_loadout":null}')"
  RID="$(jget "$S" run_id)"
  [ -n "$RID" ] && rpc finish_run "$JWT" \
    "{\"p_run_id\":\"$RID\",\"p_won\":false,\"p_score\":1,\"p_height\":1}" >/dev/null
done
BEFORE="$(jget "$(rpc get_attempts "$JWT" '{}')" count)"

# --- 1) the verified grant pays --------------------------------------------
G="$(srpc grant_ad_refill_verified "{\"p_user_id\":\"$UID_\",\"p_transaction_id\":\"TX-$$-1\"}")"
AFTER="$(jget "$G" attempts)"
[ "$(jget "$G" ok)" = "true" ] && [ "$AFTER" = "$((BEFORE + 2))" ] \
  && ok "verified grant pays +2 ($BEFORE -> $AFTER)" \
  || bad "verified grant: $G (before=$BEFORE)"

# --- 2) Google retries callbacks; a replay must not pay twice ---------------
G2="$(srpc grant_ad_refill_verified "{\"p_user_id\":\"$UID_\",\"p_transaction_id\":\"TX-$$-1\"}")"
NOW="$(jget "$(rpc get_attempts "$JWT" '{}')" count)"
[ "$(jget "$G2" reason)" = "already_granted" ] && [ "$NOW" = "$AFTER" ] \
  && ok "replayed transaction_id grants once (attempts still $NOW)" \
  || bad "replay: $G2 attempts=$NOW"

# --- 3) it must NOT be reachable by a player --------------------------------
H="$(curl -s -m 20 -o /dev/null -w "%{http_code}" -X POST \
      "$SUPABASE_URL/rest/v1/rpc/grant_ad_refill_verified" \
      -H "apikey: $ANON_KEY" -H "Authorization: Bearer $JWT" \
      -H "Content-Type: application/json" \
      -d "{\"p_user_id\":\"$UID_\",\"p_transaction_id\":\"TX-forged\"}")"
[ "$H" != "200" ] \
  && ok "players cannot call the verified grant directly (http $H)" \
  || bad "verified grant is client-callable — SSV would be pointless"

# --- 4) the flip closes the client-claimed path ------------------------------
[ "$(jget "$(rpc grant_ad_refill "$JWT" '{}')" reason)" != "ssv_required" ] \
  && ok "with ssv_enabled=false the legacy claim path still works" \
  || bad "legacy path already closed before the flip"

cfg true >/dev/null
C="$(rpc grant_ad_refill "$JWT" '{}')"
[ "$(jget "$C" reason)" = "ssv_required" ] \
  && ok "ssv_enabled=true refuses the client-claimed grant" \
  || bad "after flip: $C"

# The verified path must keep working while the claim path is closed.
G3="$(srpc grant_ad_refill_verified "{\"p_user_id\":\"$UID_\",\"p_transaction_id\":\"TX-$$-2\"}")"
[ "$(jget "$G3" ok)" = "true" ] || [ "$(jget "$G3" reason)" = "attempts_full" ] \
  && ok "verified grant still pays while the claim path is closed" \
  || bad "verified grant after flip: $G3"

cfg false >/dev/null      # leave the stack as we found it
rpc delete_account "$JWT" '{}' >/dev/null
echo "RESULT: $PASS passed, $FAIL failed"
[ "$FAIL" -eq 0 ]
