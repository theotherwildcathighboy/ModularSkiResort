#!/usr/bin/env bash
# T226 tourism A/B driver (round 2, tourism-lab). Needs a running game with the bridge READY, a city loaded and a SkiResortMod build
# with the T226 prototype (ResortAttractionDevPct). For each bonus percent: set it, run the sim at speed 8, sample with the bridge
# `tourism` op every 2048 frames for DAYS game days, write the samples to OUT/ab-<pct>.json.
#
#   bash tools/testbridge/tourism-ab.sh <out dir> <days> <pct> [<pct> ...]
set -u
OUT="$1"; DAYS="$2"; shift 2
cd "$(dirname "$0")"
mkdir -p "$OUT"
op() { node bridge-call.mjs --op "$(printf '%s' "$1" | base64 -w0)"; }
frame() { op '{"op":"status"}' | node -e 'let d="";process.stdin.on("data",c=>d+=c).on("end",()=>{try{console.log(JSON.parse(d).sim.frameIndex)}catch(e){console.log(-1)}})'; }
PER_DAY=262144
for PCT in "$@"; do
  echo "$(date +%T) pct $PCT: $(op "{\"op\":\"tourism\",\"action\":\"bonus\",\"pct\":$PCT}")"
  W0=$(date -Iseconds)
  op '{"op":"tourism","action":"start","every":2048}' >/dev/null
  op '{"op":"sim","speed":8}' >/dev/null
  F0=$(frame); GOAL=$((F0 + DAYS * PER_DAY)); LAST=$F0; STALL=0
  while :; do
    sleep 30
    F=$(frame)
    if [ "$F" -lt 0 ]; then echo "$(date +%T) bridge unreachable"; STALL=$((STALL+1)); [ $STALL -gt 10 ] && exit 3; continue; fi
    if [ "$F" -le "$LAST" ]; then STALL=$((STALL+1)); [ $STALL -gt 6 ] && { echo "$(date +%T) sim not advancing at $F, setting speed 8"; op '{"op":"sim","speed":8}' >/dev/null; STALL=0; }; else STALL=0; fi
    LAST=$F
    [ "$F" -ge "$GOAL" ] && break
  done
  op '{"op":"tourism","action":"read","from":0}' > "$OUT/ab-$PCT.json"
  printf '{"start":"%s","end":"%s"}' "${W0%%[+-]??:??}" "$(date -Iseconds | sed 's/[+-]..:..$//')" > "$OUT/ab-$PCT.window.json"
  op '{"op":"tourism"}' > "$OUT/ab-$PCT-end-snapshot.json"
  echo "$(date +%T) pct $PCT done: frames $F0..$F, $(wc -c < "$OUT/ab-$PCT.json") bytes"
done
op '{"op":"sim","speed":0}' >/dev/null
echo "$(date +%T) all done"
