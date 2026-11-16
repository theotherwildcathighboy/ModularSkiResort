#!/usr/bin/env bash
# T226 paired A/B (round 2, tourism-lab): alternate the bonus between 0 and PCT every SLICE frames for N slices, so city drift (new
# hotels, population) cancels in neighboring pairs. Writes OUT/alt.jsonl, one line per slice: pct, frames, the bridge snapshot at the
# slice end, and the wall-clock window (to match Tourism Overhaul's diagnostics log).
#   bash tools/testbridge/tourism-alt.sh <out dir> <pct> <slice frames> <slices>
set -u
OUT="$1"; PCT="$2"; SLICE="$3"; N="$4"
cd "$(dirname "$0")"
mkdir -p "$OUT"
op() { node bridge-call.mjs --op "$(printf '%s' "$1" | base64 -w0)"; }
frame() { op '{"op":"status"}' | node -e 'let d="";process.stdin.on("data",c=>d+=c).on("end",()=>{try{console.log(JSON.parse(d).sim.frameIndex)}catch(e){console.log(-1)}})'; }
op '{"op":"sim","speed":8}' >/dev/null
for i in $(seq 0 $((N - 1))); do
  P=$(( i % 2 == 0 ? 0 : PCT ))
  op "{\"op\":\"tourism\",\"action\":\"bonus\",\"pct\":$P}" >/dev/null
  W0=$(date -Iseconds | sed 's/[+-]..:..$//'); F0=$(frame); GOAL=$((F0 + SLICE))
  while :; do sleep 10; F=$(frame); [ "$F" -lt 0 ] && continue; [ "$F" -ge "$GOAL" ] && break; done
  SNAP=$(op '{"op":"tourism"}')
  W1=$(date -Iseconds | sed 's/[+-]..:..$//')
  printf '{"slice":%d,"pct":%d,"f0":%d,"f1":%d,"start":"%s","end":"%s","snap":%s}\n' "$i" "$P" "$F0" "$F" "$W0" "$W1" "$SNAP" >> "$OUT/alt.jsonl"
  echo "$(date +%T) slice $i pct $P done"
done
op '{"op":"tourism","action":"bonus","pct":0}' >/dev/null
op '{"op":"sim","speed":0}' >/dev/null
echo "$(date +%T) alt all done"
