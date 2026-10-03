#!/usr/bin/env bash
# Parity check: the ported engine against the PWA's, step for step.
#   PWA=/path/to/football-game ./run.sh            (default PWA: ../../../football-game)
# Needs node (with esbuild reachable via npx) and the .NET 8 SDK.
set -euo pipefail
cd "$(dirname "$0")"
PWA="${PWA:-$(cd ../../../football-game && pwd)}"
OUT="${OUT:-/tmp/gamenight-parity}"
mkdir -p "$OUT"
npx --yes esbuild@0.24 harness.ts --bundle --platform=node --format=esm --alias:pwa="$PWA/src" --outfile="$OUT/harness.mjs" --log-level=warning
dotnet build -c Release -v q -nologo Parity.csproj >/dev/null
RUNS=${RUNS:-"auto:1000:48000 auto:1001:48000 human:1002:20000 club:4242:48000 drill:freekicks:7:6000 drill:keeper:8:6000 drill:twovtwo:9:8000"}
fail=0
for r in $RUNS; do
  IFS=: read -r mode a b c <<<"$r"
  if [ "$mode" = drill ]; then mode="drill:$a"; seed=$b; steps=$c; else seed=$a; steps=$b; fi
  f="$OUT/${mode/:/-}-$seed.bin"
  node "$OUT/harness.mjs" "$f" "$seed" "$mode" "$steps" >/dev/null
  dotnet bin/Release/net8.0/Parity.dll "$f" "$seed" "$mode" "$steps" ${DUMP:-} || fail=1
done
exit $fail
