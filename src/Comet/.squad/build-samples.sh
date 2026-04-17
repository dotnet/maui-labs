#!/bin/bash
set +e
cd "$(dirname "$0")/../sample"
mkdir -p /tmp/comet-build-logs 2>/dev/null || true
LOG_DIR=/Users/davidortinau/work/maui-labs/src/Comet/.squad/buildlogs
mkdir -p "$LOG_DIR"
for d in "$@"; do
  csproj=$(ls "$d"/*.csproj 2>/dev/null | head -1)
  [ -z "$csproj" ] && { echo "SKIP $d (no csproj)"; continue; }
  TFM=net11.0-ios
  if [ "$d" = "CometMacApp" ]; then TFM=net11.0-macos; fi
  echo "=== BUILD $d ($TFM) ==="
  dotnet build "$csproj" -c Debug -f "$TFM" -nologo -v:m -clp:NoSummary 2>&1 | cat | tr -d '\r' > "$LOG_DIR/$d.log"
  rc=$?
  errs=$(grep -E ": error " "$LOG_DIR/$d.log" | wc -l | tr -d ' ')
  echo "$d : rc=$rc errors=$errs"
done
