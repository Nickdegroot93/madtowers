#!/usr/bin/env bash
# Post-victory "Keep Playing" scores reach the leaderboard (migration 20260808000005).
# The bug this guards: a first win consumed the run_id at victory, so every point
# earned during Keep Playing was dropped - boards filled with ties at the target.
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
    try: cur = cur[int(part)] if isinstance(cur, list) else cur[part]
    except Exception: print(""); sys.exit(0)
print(cur if isinstance(cur, str) else json.dumps(cur))
PYEOF
}
rpc() {
  curl -s -m 20 -X POST "$SUPABASE_URL/rest/v1/rpc/$1" \
    -H "apikey: $ANON_KEY" -H "Authorization: Bearer $2" \
    -H "Content-Type: application/json" -d "$3"
}
board_score() { # board_score <jwt> <level_id>
  curl -s -m 20 "$SUPABASE_URL/rest/v1/scores?level_id=eq.$2&board=eq.clean&select=best_score,best_height" \
    -H "apikey: $ANON_KEY" -H "Authorization: Bearer $1"
}

R="$(curl -s -m 20 -X POST "$SUPABASE_URL/auth/v1/signup" \
     -H "apikey: $ANON_KEY" -H "Content-Type: application/json" -d '{"data":{}}')"
JWT="$(jget "$R" access_token)"
[ -n "$JWT" ] || { echo "FAIL: could not sign in"; exit 1; }

LEVEL="Level_KeepPlaying_$$"
# Soft landing: runs are free until a chapter is done, and the meter must be live
# for start_run to behave like a real campaign run.
rpc merge_progress "$JWT" \
  '{"p_payload":{"schemaVersion":4,"completedLevelIds":["Level_A","Level_B","Level_C"]},"p_schema_version":4}' >/dev/null

XP0="$(jget "$(rpc get_profile "$JWT" '{}')" xp)"

S="$(rpc start_run "$JWT" "{\"p_level_id\":\"$LEVEL\",\"p_board\":\"clean\",\"p_loadout\":null}")"
RUN="$(jget "$S" run_id)"
[ -n "$RUN" ] || { echo "FAIL: start_run refused: $S"; exit 1; }
sleep 6

# --- the win, exactly as the victory flow reports it (progress 1.0, no overshoot) ---
F="$(rpc finish_run "$JWT" "{\"p_run_id\":\"$RUN\",\"p_won\":true,\"p_score\":100,\"p_height\":10.0,\"p_progress\":1.0}")"
XP_WIN="$(jget "$F" xp_gained)"
[ "$(jget "$F" accepted)" = "true" ] && [ "$XP_WIN" = "75" ] \
  && ok "win banks at the moment of victory (75 XP, score 100)" \
  || bad "finish_run: accepted=$(jget "$F" accepted) xp=$XP_WIN"

[ "$(jget "$(board_score "$JWT" "$LEVEL")" 0 best_score)" = "100" ] \
  && ok "board holds the victory score before Keep Playing" \
  || bad "board after win = $(board_score "$JWT" "$LEVEL")"

# --- Keep Playing: 100 -> 300 blocks, progress 1.0 -> 2.0 ---------------------
I="$(rpc improve_run_score "$JWT" "{\"p_run_id\":\"$RUN\",\"p_score\":300,\"p_height\":25.0,\"p_progress\":2.0}")"
[ "$(jget "$I" accepted)" = "true" ] \
  && ok "improve_run_score accepted for a finished, won run" \
  || bad "improve refused: $I"

[ "$(jget "$(board_score "$JWT" "$LEVEL")" 0 best_score)" = "300" ] \
  && ok "THE FIX: 300 blocks stacked after victory reach the leaderboard" \
  || bad "board after Keep Playing = $(board_score "$JWT" "$LEVEL"), expected 300"

# Overshoot pays: xp_for_run(2.0,won)=85 vs xp_for_run(1.0,won)=75 -> delta 10.
[ "$(jget "$I" xp_gained)" = "10" ] \
  && ok "overshoot pays the XP delta (10), not the whole award again" \
  || bad "improve xp_gained = $(jget "$I" xp_gained), expected 10"

# --- the client queue retries; a repeat must be worth nothing ------------------
I2="$(rpc improve_run_score "$JWT" "{\"p_run_id\":\"$RUN\",\"p_score\":300,\"p_height\":25.0,\"p_progress\":2.0}")"
[ "$(jget "$I2" xp_gained)" = "0" ] \
  && ok "a retried report pays 0 XP (idempotent via paid_progress)" \
  || bad "retry paid $(jget "$I2" xp_gained) XP"

# --- the anti-farm invariant: what was paid is RECORDED on the run -------------
# If paid_progress were ever left at 0 on a finished run, improve_run_score would
# re-pay participation + progress in full. The migration back-fills legacy rows to
# 2.0 for exactly this reason; this guards the forward invariant.
PP="$(curl -s -m 20 "$SUPABASE_URL/rest/v1/runs?run_id=eq.$RUN&select=paid_progress" \
      -H "apikey: $ANON_KEY" -H "Authorization: Bearer $JWT")"
[ "$(jget "$PP" 0 paid_progress)" = "2" ] \
  && ok "runs.paid_progress records what was paid (2.0 after the improvement)" \
  || bad "paid_progress = $(jget "$PP" 0 paid_progress), expected 2"

# --- raises only: a stale/reordered report must never lower a board entry ------
rpc improve_run_score "$JWT" "{\"p_run_id\":\"$RUN\",\"p_score\":50,\"p_height\":2.0,\"p_progress\":1.0}" >/dev/null
[ "$(jget "$(board_score "$JWT" "$LEVEL")" 0 best_score)" = "300" ] \
  && ok "a lower late report cannot drag the board back down" \
  || bad "board dropped to $(board_score "$JWT" "$LEVEL")"

# --- the narrow gates ---------------------------------------------------------
U="$(rpc improve_run_score "$JWT" '{"p_run_id":"00000000-0000-0000-0000-000000000000","p_score":9,"p_height":1,"p_progress":1}')"
[ "$(jget "$U" reason)" = "unknown_run" ] \
  && ok "someone else's run refused (unknown_run)" || bad "unknown run: $U"

S2="$(rpc start_run "$JWT" "{\"p_level_id\":\"$LEVEL\",\"p_board\":\"clean\",\"p_loadout\":null}")"
RUN2="$(jget "$S2" run_id)"
if [ -n "$RUN2" ]; then
  N="$(rpc improve_run_score "$JWT" "{\"p_run_id\":\"$RUN2\",\"p_score\":9,\"p_height\":1,\"p_progress\":1}")"
  [ "$(jget "$N" reason)" = "not_finished" ] \
    && ok "an unfinished run is refused (finish_run owes the attempt refund)" \
    || bad "unfinished run: $N"
  sleep 6
  rpc finish_run "$JWT" "{\"p_run_id\":\"$RUN2\",\"p_won\":false,\"p_score\":40,\"p_height\":4,\"p_progress\":0.4}" >/dev/null
  L="$(rpc improve_run_score "$JWT" "{\"p_run_id\":\"$RUN2\",\"p_score\":900,\"p_height\":9,\"p_progress\":2}")"
  [ "$(jget "$L" reason)" = "not_won" ] \
    && ok "a LOST run cannot be improved (no second act to report)" \
    || bad "lost run: $L"
fi

XP1="$(jget "$(rpc get_profile "$JWT" '{}')" xp)"
echo "   (xp ${XP0} -> ${XP1})"

rpc delete_account "$JWT" '{}' >/dev/null
echo "RESULT: $PASS passed, $FAIL failed"
[ "$FAIL" -eq 0 ]
