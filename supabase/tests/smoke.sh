#!/usr/bin/env bash
# MadTowers backend smoke test — exercises auth trigger, merge_progress, the
# start_run/finish_run handshake, RLS write-denial, leaderboard, names, deletion.
# Requires: local stack running (supabase start), anonymous sign-ins enabled in
# supabase/config.toml ([auth] enable_anonymous_sign_ins = true), curl, python3.
set -u

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"

# --- resolve URL + anon key -------------------------------------------------
DEMO_ANON="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6ImFub24iLCJleHAiOjE5ODM4MTI5OTZ9.CRXP1A7WOeoJeXxjNni43kdQwgnWNReilDMblYTn_I0"
if [ -z "${SUPABASE_URL:-}" ] || [ -z "${ANON_KEY:-}" ]; then
  if command -v supabase >/dev/null 2>&1; then
    STATUS_ENV="$(cd "$REPO_ROOT" && supabase status -o env 2>/dev/null || true)"
    [ -z "${SUPABASE_URL:-}" ] && SUPABASE_URL="$(echo "$STATUS_ENV" | sed -n 's/^API_URL="\{0,1\}\([^"]*\)"\{0,1\}$/\1/p' | head -1)"
    [ -z "${ANON_KEY:-}" ]     && ANON_KEY="$(echo "$STATUS_ENV" | sed -n 's/^ANON_KEY="\{0,1\}\([^"]*\)"\{0,1\}$/\1/p' | head -1)"
  fi
fi
SUPABASE_URL="${SUPABASE_URL:-http://127.0.0.1:54321}"
ANON_KEY="${ANON_KEY:-$DEMO_ANON}"

PASS=0; FAIL=0
ok()  { echo "PASS: $1"; PASS=$((PASS+1)); }
bad() { echo "FAIL: $1"; FAIL=$((FAIL+1)); }

# jget '<json>' key [key|index ...] -> raw string for scalars/strings, json for bool/objects/arrays
jget() {
  python3 - "$@" <<'PYEOF'
import json, sys
try:
    cur = json.loads(sys.argv[1])
except Exception:
    print(""); sys.exit(0)
for part in sys.argv[2:]:
    try:
        if isinstance(cur, list): cur = cur[int(part)]
        elif isinstance(cur, dict): cur = cur.get(part)
        else: cur = None
    except Exception:
        cur = None
    if cur is None:
        print(""); sys.exit(0)
print(cur if isinstance(cur, str) else json.dumps(cur))
PYEOF
}

json_eq() {
  python3 - "$1" "$2" <<'PYEOF'
import json, sys
try:
    a, b = json.loads(sys.argv[1]), json.loads(sys.argv[2])
    sys.exit(0 if a == b else 1)
except Exception:
    sys.exit(1)
PYEOF
}

rpc() { # rpc <fn> <jwt> <json-body>
  curl -s -m 20 -X POST "$SUPABASE_URL/rest/v1/rpc/$1" \
    -H "apikey: $ANON_KEY" -H "Authorization: Bearer $2" \
    -H "Content-Type: application/json" -d "$3"
}

signup_anon() { # verified endpoint: auth-js signInAnonymously() -> POST /signup, body {"data":{}}
  curl -s -m 20 -X POST "$SUPABASE_URL/auth/v1/signup" \
    -H "apikey: $ANON_KEY" -H "Content-Type: application/json" -d '{"data":{}}'
}

# --- preflight ---------------------------------------------------------------
if ! curl -s -m 5 "$SUPABASE_URL/auth/v1/health" -H "apikey: $ANON_KEY" >/dev/null; then
  echo "ABORT: stack not reachable at $SUPABASE_URL (is 'supabase start' running?)"; exit 2
fi

SUFFIX="$(python3 -c 'import random; print(random.randint(100000, 999999))')"
LEVEL="Level_Smoke_$SUFFIX"

# --- a) anonymous sign-in ----------------------------------------------------
R="$(signup_anon)"
TOK="$(jget "$R" access_token)"
UID1="$(jget "$R" user id)"
if [ -n "$TOK" ] && [ -n "$UID1" ]; then ok "a) anonymous sign-in (user $UID1)"; else
  bad "a) anonymous sign-in — response: $R"
  echo "HINT: enable_anonymous_sign_ins = true must be set under [auth] in supabase/config.toml, then 'supabase db reset'."
  echo "RESULT: $PASS passed, $FAIL failed"; exit 1
fi

# --- b) trigger created profile + attempts -----------------------------------
P="$(curl -s -m 20 "$SUPABASE_URL/rest/v1/profiles?user_id=eq.$UID1&select=display_name,is_linked" \
  -H "apikey: $ANON_KEY" -H "Authorization: Bearer $TOK")"
NAME="$(jget "$P" 0 display_name)"
case "$NAME" in Builder-*) ok "b1) auto profile name: $NAME";; *) bad "b1) expected Builder-XXXX, got: $P";; esac
A="$(rpc get_attempts "$TOK" '{}')"
[ "$(jget "$A" count)" = "5" ] && ok "b2) attempts row starts at 5" || bad "b2) get_attempts: $A"

# --- c0) soft-landing: zero completions => runs are free (SHOP.md §7.1) ---------
S0="$(rpc start_run "$TOK" "{\"p_level_id\":\"$LEVEL\",\"p_board\":\"clean\",\"p_loadout\":null}")"
GA0="$(rpc get_attempts "$TOK" "{}")"
if [ "$(jget "$S0" allowed)" = "true" ] && [ "$(jget "$S0" meter_charged)" = "false" ] \
   && [ "$(jget "$GA0" count)" = "5" ] && [ "$(jget "$GA0" meter_charged)" = "false" ]; then
  ok "c0) soft-landing exempt run: allowed, uncharged, meter hidden"
else bad "c0) soft-landing: start=$S0 attempts=$GA0"; fi

# --- c) merge_progress idempotency (3 completions = chapter 1 done => meter on) --
PAYLOAD='{"schemaVersion":4,"completedLevelIds":["Level_A","Level_B","Level_C"],"bests":[{"levelId":"Level_A","board":"clean","bestScore":10,"bestHeightMeters":3.5,"achievedAtUnixUtc":1753000000}],"discoveredBlocks":["normal","maw"],"abilitiesUsed":{"zap":2},"currencyEarned":100,"currencySpent":40,"settings":{"music":0.8,"updatedAtUnixUtc":1753000000}}'
M1="$(rpc merge_progress "$TOK" "{\"p_payload\":$PAYLOAD,\"p_schema_version\":4}")"
M2="$(rpc merge_progress "$TOK" "{\"p_payload\":$PAYLOAD,\"p_schema_version\":4}")"
if [ -n "$M1" ] && json_eq "$M1" "$M2"; then ok "c) merge_progress idempotent"; else
  bad "c) merge results differ — M1=$M1 M2=$M2"; fi

# --- d) start_run drains the meter -------------------------------------------
RUN1=""; ALLOWED=0; REFUSED=""
for i in 1 2 3 4 5 6 7 8; do
  S="$(rpc start_run "$TOK" "{\"p_level_id\":\"$LEVEL\",\"p_board\":\"clean\",\"p_loadout\":null}")"
  if [ "$(jget "$S" allowed)" = "true" ]; then
    ALLOWED=$((ALLOWED+1))
    [ -z "$RUN1" ] && RUN1="$(jget "$S" run_id)"
  else
    REFUSED="$S"; break
  fi
done
[ "$ALLOWED" = "5" ] && ok "d1) exactly 5 starts allowed from a full meter" || bad "d1) allowed $ALLOWED starts (want 5)"
SECS="$(jget "$REFUSED" seconds_until_next)"
if [ "$(jget "$REFUSED" reason)" = "out_of_attempts" ] && [ -n "$SECS" ] && [ "$SECS" -gt 0 ] 2>/dev/null; then
  ok "d2) 6th start refused with seconds_until_next=$SECS"
else bad "d2) refusal malformed: $REFUSED"; fi

# --- e) finish_run win refund -------------------------------------------------
sleep 6  # runs must be >=5s old to be plausible
F="$(rpc finish_run "$TOK" "{\"p_run_id\":\"$RUN1\",\"p_won\":true,\"p_score\":42,\"p_height\":7.5}")"
[ "$(jget "$F" accepted)" = "true" ] && ok "e1) finish_run(won) accepted" || bad "e1) finish_run: $F"
A="$(rpc get_attempts "$TOK" '{}')"
AT="$(jget "$A" count)"
if [ -n "$AT" ] && [ "$AT" -ge 1 ] 2>/dev/null; then ok "e2) win refunded an attempt (now $AT)"; else bad "e2) no refund visible: $A"; fi

# --- f) double-finish rejected -------------------------------------------------
F2="$(rpc finish_run "$TOK" "{\"p_run_id\":\"$RUN1\",\"p_won\":true,\"p_score\":42,\"p_height\":7.5}")"
if [ "$(jget "$F2" accepted)" = "false" ] && [ "$(jget "$F2" reason)" = "already_finished" ]; then
  ok "f) double finish rejected"; else bad "f) double finish: $F2"; fi

# --- g) direct writes blocked by RLS -------------------------------------------
CODE="$(curl -s -o /dev/null -w "%{http_code}" -m 20 -X PATCH \
  "$SUPABASE_URL/rest/v1/attempts?user_id=eq.$UID1" \
  -H "apikey: $ANON_KEY" -H "Authorization: Bearer $TOK" \
  -H "Content-Type: application/json" -H "Prefer: return=representation" -d '{"count":99}')"
A="$(rpc get_attempts "$TOK" '{}')"
[ "$(jget "$A" attempts)" != "99" ] && ok "g1) direct PATCH attempts blocked (http $CODE, count unchanged)" \
  || bad "g1) direct PATCH mutated attempts! $A"
CODE2="$(curl -s -o /dev/null -w "%{http_code}" -m 20 -X POST "$SUPABASE_URL/rest/v1/scores" \
  -H "apikey: $ANON_KEY" -H "Authorization: Bearer $TOK" \
  -H "Content-Type: application/json" -d "{\"user_id\":\"$UID1\",\"level_id\":\"$LEVEL\",\"board\":\"clean\",\"best_score\":999999}")"
[ "$CODE2" -ge 400 ] 2>/dev/null && ok "g2) direct INSERT scores rejected (http $CODE2)" \
  || bad "g2) direct INSERT scores returned http $CODE2"

# --- h) leaderboard ------------------------------------------------------------
LB="$(rpc get_leaderboard "$TOK" "{\"p_level_id\":\"$LEVEL\",\"p_board\":\"clean\"}")"
E_SCORE="$(jget "$LB" entries 0 best_score)"
E_NAME="$(jget "$LB" entries 0 display_name)"
E_YOU="$(jget "$LB" entries 0 is_you)"
if [ "$E_SCORE" = "42" ] && [ "$E_NAME" = "$NAME" ] && [ "$E_YOU" = "true" ]; then
  ok "h) leaderboard shows $E_NAME @ 42 (is_you)"; else bad "h) leaderboard: $LB"; fi

# --- i) claim_display_name uniqueness -------------------------------------------
NEWNAME="Smk$SUFFIX"
C1="$(rpc claim_display_name "$TOK" "{\"p_name\":\"$NEWNAME\"}")"
[ "$(jget "$C1" ok)" = "true" ] && ok "i1) claimed name $NEWNAME" || bad "i1) claim failed: $C1"
R2="$(signup_anon)"
TOK2="$(jget "$R2" access_token)"
UID2="$(jget "$R2" user id)"
if [ -z "$TOK2" ]; then bad "i2) second anonymous user failed: $R2"; else
  C2="$(rpc claim_display_name "$TOK2" "{\"p_name\":\"$NEWNAME\"}")"
  if [ "$(jget "$C2" ok)" = "false" ] && [ "$(jget "$C2" reason)" = "taken" ]; then
    ok "i2) duplicate name refused (taken)"; else bad "i2) duplicate claim: $C2"; fi
  C3="$(rpc claim_display_name "$TOK2" '{"p_name":"x"}')"
  [ "$(jget "$C3" reason)" = "invalid" ] && ok "i3) short name refused" || bad "i3) validation: $C3"
fi

# --- j) delete_account cascades --------------------------------------------------
if [ -n "${TOK2:-}" ]; then
  rpc delete_account "$TOK2" '{}' >/dev/null
  P2="$(curl -s -m 20 "$SUPABASE_URL/rest/v1/profiles?user_id=eq.$UID2&select=user_id" \
    -H "apikey: $ANON_KEY" -H "Authorization: Bearer $TOK")"
  json_eq "$P2" "[]" && ok "j) delete_account wiped profile (cascade)" || bad "j) profile survived deletion: $P2"
fi

# --- cleanup: the suite leaves no rows behind (safe to run against production) ----
# delete_account returns void -> empty/null body on success, an error object on failure.
Z="$(rpc delete_account "$TOK" '{}')"
case "$Z" in
  ""|null) ok "z) cleanup: primary test user deleted" ;;
  *)       bad "z) cleanup failed - test user may remain: $Z" ;;
esac

echo "RESULT: $PASS passed, $FAIL failed"
[ "$FAIL" = "0" ]
