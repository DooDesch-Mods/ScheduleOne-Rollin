#!/usr/bin/env bash
# Neon on a wheelchair with a Chassis that builds the underglow as lights only (Chassis 1.7.1 and later).
# Spawns a chair, sets a neon, and checks that Rollin fits it under the seat once and does not retry every scan.
# Usage: bash tests/neon-lights.sh <slot>   (game running with a Debug Rollin and a save loaded)
set -u
SLOT="${1:?slot}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
S1=(python "$ROOT/Workspace/tools/bridge/s1.py" --slot "$SLOT")
fail=0
check() { if [ "$2" = 1 ]; then echo "PASS $1"; else echo "FAIL $1"; fail=1; fi; }

spawn=$("${S1[@]}" run "rollin spawn" --max-wait 8000)
echo "$spawn" | grep -E "spawned wheelchair net [0-9]+" >/dev/null && ok=1 || ok=0
check "wheelchair spawned" "$ok"
[ "$ok" = 1 ] || { echo "$spawn"; exit 1; }

tune=$("${S1[@]}" run "rollin tune set neon FF2EC4,fblr" --until "neon FF2EC4" --max-wait 8000)
echo "$tune" | grep -E "neon FF2EC4 on fblr" >/dev/null && ok=1 || ok=0
check "Chassis built the neon" "$ok"

# The chair is scanned twice a second; six seconds are a dozen scans.
moved=0; spam=0
for _ in 1 2 3 4 5 6; do
  sleep 1
  tail=$("${S1[@]}" log -n 600)
  moved=$(echo "$tail" | grep -c "moved under the seat")
  spam=$(echo "$tail" | grep -c "has no renderer")
done
echo "log: $moved 'moved under the seat', $spam 'has no renderer'"
check "neon fitted under the seat" "$([ "$moved" -ge 1 ] && echo 1 || echo 0)"
check "neon fitted once, no retry every scan" "$([ "$moved" -le 1 ] && [ "$spam" = 0 ] && echo 1 || echo 0)"
exit $fail
